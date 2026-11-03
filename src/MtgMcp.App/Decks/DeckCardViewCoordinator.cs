using System.Security.Cryptography;
using System.Text;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Builds the <c>local_deck_get</c> result: the stored deck, optionally with a page of entries enriched from installed
/// Scryfall card data.
/// </summary>
internal sealed class DeckCardViewCoordinator
{
    /// <summary>
    /// Names the include value that adds concise card fields to each entry.
    /// </summary>
    internal const string IncludeCards = "cards";

    /// <summary>
    /// Names the include value that adds oracle tag slugs to each entry.
    /// </summary>
    internal const string IncludeTags = "tags";

    /// <summary>
    /// Gives the number of entries in a page when the caller names none.
    /// </summary>
    internal const int DefaultPageSize = 25;

    /// <summary>
    /// Gives the most entries a caller can ask for in one page.
    /// </summary>
    internal const int MaximumPageSize = 100;

    /// <summary>
    /// Gives the most card names a caller can ask for in one read.
    /// </summary>
    internal const int MaximumCardNames = 100;

    /// <summary>
    /// Gives the most category names a caller can ask for in one read.
    /// </summary>
    internal const int MaximumCategoryNames = 100;

    /// <summary>
    /// Says why entry card data is missing when no card data is installed.
    /// </summary>
    private const string NotInstalledLimit =
        "Card fields and tags are not available because Scryfall card data is not installed. " +
        "Run scryfall_card_data_sync to install it.";

    /// <summary>
    /// Lists the usual zone codes, as examples in a message.
    /// </summary>
    private static readonly string ZoneExamples = string.Join(", ", DeckZoneCounts.ZoneOrder);

    /// <summary>
    /// Says how to change the page when a stored deck is too large for one result.
    /// </summary>
    private const string StoredNarrowHint = "Ask for a smaller pageSize to get smaller pages.";

    /// <summary>
    /// Says how to change the page when a page with card data is too large for one result.
    /// </summary>
    private const string CardsNarrowHint = "Ask for fewer fields or a smaller pageSize to get smaller pages.";

    /// <summary>
    /// Reads stored decks.
    /// </summary>
    private readonly SqliteDeckStore store;

    /// <summary>
    /// Reads installed card data.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Creates the coordinator around the deck store and the Scryfall service.
    /// </summary>
    internal DeckCardViewCoordinator(SqliteDeckStore store, ScryfallService scryfall)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scryfall);
        this.store = store;
        this.scryfall = scryfall;
    }

    /// <summary>
    /// Gets one deck, with card fields and tags for a page of entries when include names them.
    /// </summary>
    internal async Task<OperationResult<DeckDocumentView>> GetAsync(
        Guid deckId,
        IReadOnlyList<string>? include,
        IReadOnlyList<string>? fields,
        string? cursor,
        int? pageSize,
        IReadOnlyList<string>? cardNames,
        IReadOnlyList<string>? zones,
        IReadOnlyList<string>? categoryNames,
        string? responseFormat,
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? invalid = Validate(
            include, fields, cursor, pageSize, cardNames, zones, categoryNames, responseFormat);
        if (invalid is not null)
        {
            return invalid;
        }

        OperationResult<DeckDocument> stored = await store.GetAsync(deckId, cancellationToken).ConfigureAwait(false);
        if (stored is not OperationSuccess<DeckDocument> success)
        {
            return OperationFailure.Forward<DeckDocument, DeckDocumentView>(stored);
        }

        DeckDocument deck = success.Data;
        bool concise = IsConcise(responseFormat);
        CardSelection selection = CardSelection.Of(deck, cardNames, zones, categoryNames);
        PageCursorBinding binding = PageCursorBinding.ForDeck(deck.DeckId, deck.Revision)
            .And(PageCursorBinding.ForRequest(
                Fingerprint(include ?? [], fields, cardNames, zones, categoryNames, concise)));
        if (include is null)
        {
            return GetStored(deck, selection, concise, binding, cursor, pageSize);
        }

        OperationResult<int> start = ReadStart(cursor, binding, deck, selection.Entries.Count);
        if (start is not OperationSuccess<int> offset)
        {
            return OperationFailure.Forward<int, DeckDocumentView>(start);
        }

        List<DeckEntry> slice = [.. selection.Entries.Skip(offset.Data).Take(pageSize ?? DefaultPageSize)];
        OperationResult<ScryfallInstalledCardViews> read = await ScryfallToolExecution.RunAsync(() =>
            scryfall.ReadInstalledCardViewsAsync(
                [.. slice.Select(entry => ScryfallEntryLookup.For(
                    entry.PrintingId, entry.SetCode, entry.CollectorNumber, entry.OracleId))],
                fields,
                include.Contains(IncludeCards, StringComparer.Ordinal),
                include.Contains(IncludeTags, StringComparer.Ordinal),
                cancellationToken)).ConfigureAwait(false);
        if (read is not OperationSuccess<ScryfallInstalledCardViews> views)
        {
            return OperationFailure.Forward<ScryfallInstalledCardViews, DeckDocumentView>(read);
        }

        List<DeckEntryView> entries = ToViews(deck, slice, concise);
        for (int index = 0; index < slice.Count; index++)
        {
            ScryfallInstalledCardView view = views.Data.Cards[index];
            entries[index] = entries[index] with
            {
                CardData = view.Status,
                Card = view.Card,
                Tags = view.Tags,
            };
        }

        return BuildPage(
            deck,
            selection,
            entries,
            concise,
            offset.Data,
            binding,
            views.Data.Installed ? null : [NotInstalledLimit],
            CardsNarrowHint);
    }

    /// <summary>
    /// Gets the stored deck. A deck that fits the result size limit comes back whole and unchanged, unless the caller
    /// named cards; a larger one, or a read of named cards, is paged by entries, with the same cursor and pageSize
    /// arguments as a page with card data.
    /// </summary>
    private static OperationResult<DeckDocumentView> GetStored(
        DeckDocument deck,
        CardSelection selection,
        bool concise,
        PageCursorBinding binding,
        string? cursor,
        int? pageSize)
    {
        List<DeckEntryView> all = ToViews(deck, selection.Entries, concise);
        if (cursor is null && pageSize is null && !selection.Filtered)
        {
            IReadOnlyList<DeckCategoryAssignment> rows = AssignmentRows(deck, concise);
            ResultPageSlice whole = ResultPage.Take(
                all,
                0,
                ResultSize.Measure(ToView(deck, [], [])),
                entry => EntryBytes(entry, rows));
            if (!whole.More)
            {
                return new OperationSuccess<DeckDocumentView>(ToView(deck, all, rows));
            }
        }

        OperationResult<int> start = ReadStart(cursor, binding, deck, all.Count);
        if (start is not OperationSuccess<int> offset)
        {
            return OperationFailure.Forward<int, DeckDocumentView>(start);
        }

        List<DeckEntryView> slice = [.. all.Skip(offset.Data).Take(pageSize ?? int.MaxValue)];
        return BuildPage(deck, selection, slice, concise, offset.Data, binding, null, StoredNarrowHint);
    }

    /// <summary>
    /// Tells whether a response format asks for concise entries. Standard gives every stored field.
    /// </summary>
    private static bool IsConcise(string? responseFormat)
    {
        return string.Equals(responseFormat?.Trim(), "concise", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gives the category assignment rows a result carries: none for concise entries, which name their categories.
    /// </summary>
    private static IReadOnlyList<DeckCategoryAssignment> AssignmentRows(DeckDocument deck, bool concise)
    {
        return concise ? [] : deck.CategoryAssignments;
    }

    /// <summary>
    /// Builds the views of stored entries, full or concise.
    /// </summary>
    private static List<DeckEntryView> ToViews(DeckDocument deck, IReadOnlyList<DeckEntry> entries, bool concise)
    {
        List<DeckEntryView> views = new(entries.Count);
        if (!concise)
        {
            foreach (DeckEntry entry in entries)
            {
                views.Add(DeckEntryView.From(entry));
            }

            return views;
        }

        Dictionary<Guid, IReadOnlyList<string>> namesByEntry = DeckEntryCategories.NamesByEntry(deck);
        foreach (DeckEntry entry in entries)
        {
            views.Add(DeckEntryView.Concise(entry, namesByEntry.GetValueOrDefault(entry.EntryId) ?? []));
        }

        return views;
    }

    /// <summary>
    /// Checks the include, fields, cursor, and page size arguments against each other.
    /// </summary>
    private static OperationInvalidInput? Validate(
        IReadOnlyList<string>? include,
        IReadOnlyList<string>? fields,
        string? cursor,
        int? pageSize,
        IReadOnlyList<string>? cardNames,
        IReadOnlyList<string>? zones,
        IReadOnlyList<string>? categoryNames,
        string? responseFormat)
    {
        OperationInvalidInput? shape =
            ValidateNames(cardNames, "cardNames", "invalid-card-names", "card", MaximumCardNames) ??
            ValidateZones(zones) ??
            ValidateNames(
                categoryNames,
                "categoryNames",
                "invalid-category-names",
                "category",
                MaximumCategoryNames) ??
            ValidateResponseFormat(responseFormat);
        if (shape is not null)
        {
            return shape;
        }

        if (include is null)
        {
            return fields is not null
                ? new OperationInvalidInput(
                    "include-required",
                    "fields applies only when include is given.",
                    "fields")
                : ValidatePageSize(pageSize);
        }

        if (include.Count == 0)
        {
            return new OperationInvalidInput(
                "empty-include",
                "include must name cards, tags, or both. Omit include to get the stored deck only.",
                "include");
        }

        for (int index = 0; index < include.Count; index++)
        {
            if (include[index] is not (IncludeCards or IncludeTags))
            {
                return new OperationInvalidInput(
                    "invalid-include",
                    $"Unknown include value '{include[index]}'. Allowed values: {IncludeCards}, {IncludeTags}.",
                    $"include[{index}]");
            }
        }

        if (fields is not null && !include.Contains(IncludeCards, StringComparer.Ordinal))
        {
            return new OperationInvalidInput(
                "fields-require-cards",
                "fields can only be used when include contains cards.",
                "fields");
        }

        return ValidatePageSize(pageSize);
    }

    /// <summary>
    /// Checks that the zones, when given, hold at least one zone code and no code is blank. A code no entry uses is
    /// not an error, because a deck may use any zone code.
    /// </summary>
    private static OperationInvalidInput? ValidateZones(IReadOnlyList<string>? zones)
    {
        if (zones is null)
        {
            return null;
        }

        if (zones.Count == 0)
        {
            return new OperationInvalidInput(
                "invalid-zones",
                $"zones must name at least one zone code, such as {ZoneExamples}. Omit it to read every zone.",
                "zones");
        }

        for (int index = 0; index < zones.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(zones[index]))
            {
                return new OperationInvalidInput("invalid-zones", "Every zone code must have text.", $"zones[{index}]");
            }
        }

        return null;
    }

    /// <summary>
    /// Checks that a name list, when given, holds 1 through a maximum number of names and none is blank.
    /// </summary>
    private static OperationInvalidInput? ValidateNames(
        IReadOnlyList<string>? names,
        string parameter,
        string reasonCode,
        string noun,
        int maximum)
    {
        if (names is null)
        {
            return null;
        }

        if (names.Count < 1 || names.Count > maximum)
        {
            return new OperationInvalidInput(
                reasonCode,
                $"{parameter} must name from 1 through {maximum} {noun}s. Omit it to read every entry.",
                parameter);
        }

        for (int index = 0; index < names.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(names[index]))
            {
                return new OperationInvalidInput(
                    reasonCode,
                    $"Every {noun} name must have text.",
                    $"{parameter}[{index}]");
            }
        }

        return null;
    }

    /// <summary>
    /// Checks that the response format, when given, is concise or standard.
    /// </summary>
    private static OperationInvalidInput? ValidateResponseFormat(string? responseFormat)
    {
        return responseFormat?.Trim().ToLowerInvariant() is null or "concise" or "standard"
            ? null
            : new OperationInvalidInput(
                "invalid-response-format",
                "responseFormat must be concise or standard.",
                "responseFormat");
    }

    /// <summary>
    /// Checks that a page size, when given, is in range.
    /// </summary>
    private static OperationInvalidInput? ValidatePageSize(int? pageSize)
    {
        return ResultPage.CheckPageSize(pageSize, MaximumPageSize);
    }

    /// <summary>
    /// Fingerprints the request fields that must stay the same from page to page, ignoring their order.
    /// </summary>
    private static string Fingerprint(
        IReadOnlyList<string> include,
        IReadOnlyList<string>? fields,
        IReadOnlyList<string>? cardNames,
        IReadOnlyList<string>? zones,
        IReadOnlyList<string>? categoryNames,
        bool concise)
    {
        string text = $"{string.Join(',', include.Order(StringComparer.Ordinal))}|" +
            string.Join(',', (fields ?? []).Order(StringComparer.Ordinal));
        text += FilterText("names", cardNames) + FilterText("zones", zones) +
            FilterText("categories", categoryNames);

        if (concise)
        {
            text += "|concise";
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    /// <summary>
    /// Writes one filter for the fingerprint, ignoring case, order, and repeats. A missing filter writes nothing.
    /// </summary>
    private static string FilterText(string label, IReadOnlyList<string>? values)
    {
        return values is null
            ? string.Empty
            : $"|{label}:" + string.Join(
                '\n',
                values.Select(value => value.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Reads the first entry position of a page from its cursor. A cursor must belong to this request and deck, and
    /// only an unchanged revision keeps the entry position meaningful.
    /// </summary>
    private static OperationResult<int> ReadStart(
        string? cursor,
        PageCursorBinding binding,
        DeckDocument deck,
        int total)
    {
        OperationResult<PageCursorPosition> start = PageCursor.Decode(
            cursor,
            binding,
            current: new ConflictState(deck.Revision));
        return start switch
        {
            OperationSuccess<PageCursorPosition> { Data.Offset: var offset } when offset <= total =>
                new OperationSuccess<int>(offset),
            OperationSuccess<PageCursorPosition> => PageCursor.Invalid(),
            _ => OperationFailure.Forward<PageCursorPosition, int>(start),
        };
    }

    /// <summary>
    /// Builds the page result from the page's entries, cutting the page short when it would pass the size limit.
    /// </summary>
    private static OperationResult<DeckDocumentView> BuildPage(
        DeckDocument deck,
        CardSelection selection,
        List<DeckEntryView> entries,
        bool concise,
        int offset,
        PageCursorBinding binding,
        IReadOnlyList<string>? limits,
        string narrowHint)
    {
        int total = selection.Entries.Count;
        IReadOnlyList<DeckCategoryAssignment> rows = AssignmentRows(deck, concise);
        DeckDocumentView header = PageHeader(deck, offset) with
        {
            CardNamesNotFound = selection.NotFound,
            ZonesNotFound = selection.ZonesNotFound,
            CategoryNamesNotFound = selection.CategoryNamesNotFound,
        };
        ResultPageSlice slice = ResultPage.Take(
            entries,
            0,
            ResultSize.Measure(header),
            entry => EntryBytes(entry, rows));
        int keep = slice.Count;
        bool truncated = keep < entries.Count;
        List<DeckEntryView> kept = entries.GetRange(0, keep);
        HashSet<Guid> keptIds = [.. kept.Select(entry => entry.EntryId)];
        List<DeckCategoryAssignment> assignments =
            [.. rows.Where(assignment => keptIds.Contains(assignment.EntryId))];
        int next = offset + keep;
        return new OperationSuccess<DeckDocumentView>(header with
        {
            Entries = kept,
            CategoryAssignments = assignments,
            TotalEntries = total,
            NextCursor = next < total ? PageCursor.Encode(PageCursorPosition.AtOffset(next), binding) : null,
            Limits = limits,
            Truncated = truncated ? true : null,
            Narrow = truncated ? NarrowText(selection, kept.Count, total, narrowHint) : null,
        });
    }

    /// <summary>
    /// Explains that a page is part of the result and how to get the rest.
    /// </summary>
    private static string NarrowText(CardSelection selection, int shown, int total, string narrowHint)
    {
        return !selection.Filtered
            ? $"This page shows {shown} of {total} entries; the deck is not missing the rest. " +
                $"Follow nextCursor. {narrowHint}"
            : $"This page shows {shown} of {total} matching entries; follow nextCursor for the rest. {narrowHint}";
    }

    /// <summary>
    /// Builds the deck view for a set of entries and assignments, keeping every other part of the deck.
    /// </summary>
    private static DeckDocumentView ToView(
        DeckDocument deck,
        IReadOnlyList<DeckEntryView> entries,
        IReadOnlyList<DeckCategoryAssignment> assignments)
    {
        return new DeckDocumentView(
            deck.DeckId,
            deck.Name,
            deck.Description,
            deck.Format,
            deck.Revision,
            deck.CreatedAtUtc,
            deck.UpdatedAtUtc,
            entries,
            deck.Categories,
            assignments,
            deck.ProviderBindings);
    }

    /// <summary>
    /// Measures the serialized size of an entry together with its category assignments.
    /// </summary>
    private static int EntryBytes(DeckEntryView entry, IReadOnlyList<DeckCategoryAssignment> assignments)
    {
        int total = ResultSize.Measure(entry);
        foreach (DeckCategoryAssignment assignment in assignments)
        {
            if (assignment.EntryId == entry.EntryId)
            {
                total += ResultSize.Measure(assignment) + 1;
            }
        }

        return total;
    }

    /// <summary>
    /// Builds the deck view that every page repeats, without entries or assignments: the details, categories,
    /// provider bindings, and deck-wide totals. The name is cut to a fixed length, and the description is cut on the
    /// first page and left out of the pages after it, so a long one cannot push every page over the size limit.
    /// </summary>
    private static DeckDocumentView PageHeader(DeckDocument deck, int offset)
    {
        ShownText name = ResultText.Name(deck.Name);
        ShownText description = ResultText.Description(deck.Description, offset == 0);
        return ToView(deck, [], []) with
        {
            Name = name.Text,
            Description = description.Text,
            NameTruncated = name.TruncatedOrNull,
            DescriptionTruncated = description.TruncatedOrNull,
            DescriptionLength = description.LengthIfTruncated,
            EntryCount = deck.Entries.Count,
            CardCount = deck.Entries.Sum(entry => entry.Quantity),
            CardsByZone = DeckZoneCounts.Of(deck),
        };
    }
    /// <summary>
    /// Holds the entries a read covers and, for each filter the caller gave, the values that match no entry of the
    /// whole deck.
    /// </summary>
    private sealed record CardSelection(
        IReadOnlyList<DeckEntry> Entries,
        IReadOnlyList<string>? NotFound,
        IReadOnlyList<string>? ZonesNotFound,
        IReadOnlyList<string>? CategoryNamesNotFound)
    {
        /// <summary>
        /// Gets true when the caller gave at least one filter.
        /// </summary>
        internal bool Filtered => NotFound is not null || ZonesNotFound is not null ||
            CategoryNamesNotFound is not null;

        /// <summary>
        /// Selects every entry, or the entries that match every filter given. Values inside one filter combine with
        /// OR, and the filters combine with AND.
        /// </summary>
        internal static CardSelection Of(
            DeckDocument deck,
            IReadOnlyList<string>? cardNames,
            IReadOnlyList<string>? zones,
            IReadOnlyList<string>? categoryNames)
        {
            Dictionary<Guid, IReadOnlyList<string>> namesByEntry = categoryNames is null
                ? []
                : DeckEntryCategories.NamesByEntry(deck);
            IReadOnlyList<DeckEntry> entries = deck.Entries;
            (entries, IReadOnlyList<string>? namesNotFound) = Narrow(
                entries,
                deck,
                cardNames,
                (entry, name) => CardNames.Match(entry.CardName, name));
            (entries, IReadOnlyList<string>? zonesNotFound) = Narrow(
                entries,
                deck,
                zones,
                (entry, zone) => string.Equals(entry.Zone, zone, StringComparison.OrdinalIgnoreCase));
            (entries, IReadOnlyList<string>? categoriesNotFound) = Narrow(
                entries,
                deck,
                categoryNames,
                (entry, category) => namesByEntry.TryGetValue(entry.EntryId, out IReadOnlyList<string>? names) &&
                    names.Contains(category, StringComparer.OrdinalIgnoreCase));
            return new CardSelection(entries, namesNotFound, zonesNotFound, categoriesNotFound);
        }

        /// <summary>
        /// Keeps the entries that match one of the requested values and returns them with the values that match no
        /// entry of the whole deck, which is null when the filter was not given.
        /// </summary>
        private static (IReadOnlyList<DeckEntry> Entries, IReadOnlyList<string>? NotFound) Narrow(
            IReadOnlyList<DeckEntry> entries,
            DeckDocument deck,
            IReadOnlyList<string>? requested,
            Func<DeckEntry, string, bool> matches)
        {
            if (requested is null)
            {
                return (entries, null);
            }

            List<string> notFound = [];
            HashSet<Guid> matched = [];
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string value in requested)
            {
                string name = value.Trim();
                if (!seen.Add(name))
                {
                    continue;
                }

                bool found = false;
                foreach (DeckEntry entry in deck.Entries)
                {
                    if (matches(entry, name))
                    {
                        matched.Add(entry.EntryId);
                        found = true;
                    }
                }

                if (!found)
                {
                    notFound.Add(name);
                }
            }

            return ([.. entries.Where(entry => matched.Contains(entry.EntryId))], notFound);
        }
    }
}
