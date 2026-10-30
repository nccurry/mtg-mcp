using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
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
    /// Says why entry card data is missing when no card data is installed.
    /// </summary>
    private const string NotInstalledLimit =
        "Card fields and tags are not available because Scryfall card data is not installed. " +
        "Run scryfall_card_data_sync to install it.";

    /// <summary>
    /// Says how to read a stored deck that is too large for one result.
    /// </summary>
    private const string StoredNarrowHint =
        "The deck is too large for one result, so this page holds only some entries. Use nextCursor to read the " +
        "rest, or ask for a smaller pageSize. Every entry is reachable by following nextCursor.";

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
        CancellationToken cancellationToken)
    {
        OperationInvalidInput? invalid = Validate(include, fields, cursor, pageSize);
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
        if (include is null)
        {
            return GetStored(deck, cursor, pageSize);
        }

        string fingerprint = Fingerprint(deckId, include, fields);
        OperationResult<int> start = ReadStart(cursor, fingerprint, deck);
        if (start is not OperationSuccess<int> offset)
        {
            return OperationFailure.Forward<int, DeckDocumentView>(start);
        }

        List<DeckEntry> slice = [.. deck.Entries.Skip(offset.Data).Take(pageSize ?? DefaultPageSize)];
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

        List<DeckEntryView> entries = [];
        for (int index = 0; index < slice.Count; index++)
        {
            ScryfallInstalledCardView view = views.Data.Cards[index];
            entries.Add(DeckEntryView.From(slice[index]) with
            {
                CardData = view.Status,
                Card = view.Card,
                Tags = view.Tags,
            });
        }

        return BuildPage(
            deck,
            entries,
            offset.Data,
            fingerprint,
            views.Data.Installed ? null : [NotInstalledLimit],
            ScryfallFormattedResponse.NarrowHint);
    }

    /// <summary>
    /// Gets the stored deck. A deck that fits the result size limit comes back whole and unchanged; a larger one is
    /// paged by entries, with the same cursor and pageSize arguments as a page with card data.
    /// </summary>
    private static OperationResult<DeckDocumentView> GetStored(DeckDocument deck, string? cursor, int? pageSize)
    {
        List<DeckEntryView> all = [.. deck.Entries.Select(DeckEntryView.From)];
        if (cursor is null && pageSize is null)
        {
            ResultPageSlice whole = ResultPage.Take(
                all,
                0,
                ResultSize.Measure(ToView(deck, [], [])),
                entry => EntryBytes(entry, deck.CategoryAssignments));
            if (!whole.More)
            {
                return new OperationSuccess<DeckDocumentView>(ToView(deck, all, deck.CategoryAssignments));
            }
        }

        string fingerprint = Fingerprint(deck.DeckId, [], null);
        OperationResult<int> start = ReadStart(cursor, fingerprint, deck);
        if (start is not OperationSuccess<int> offset)
        {
            return OperationFailure.Forward<int, DeckDocumentView>(start);
        }

        List<DeckEntryView> slice = [.. all.Skip(offset.Data).Take(pageSize ?? int.MaxValue)];
        return BuildPage(deck, slice, offset.Data, fingerprint, null, StoredNarrowHint);
    }

    /// <summary>
    /// Checks the include, fields, cursor, and page size arguments against each other.
    /// </summary>
    private static OperationInvalidInput? Validate(
        IReadOnlyList<string>? include,
        IReadOnlyList<string>? fields,
        string? cursor,
        int? pageSize)
    {
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
    /// Checks that a page size, when given, is in range.
    /// </summary>
    private static OperationInvalidInput? ValidatePageSize(int? pageSize)
    {
        return pageSize is >= 1 and <= MaximumPageSize or null
            ? null
            : new OperationInvalidInput(
                "invalid-page-size",
                $"Page size must be from 1 through {MaximumPageSize}.",
                "pageSize");
    }

    /// <summary>
    /// Fingerprints the request fields that must stay the same from page to page, ignoring their order.
    /// </summary>
    private static string Fingerprint(Guid deckId, IReadOnlyList<string> include, IReadOnlyList<string>? fields)
    {
        string text = $"{deckId:D}|{string.Join(',', include.Order(StringComparer.Ordinal))}|" +
            string.Join(',', (fields ?? []).Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    /// <summary>
    /// Reads the first entry position of a page from its cursor. A cursor must belong to this request; only then is
    /// the deck revision compared, and only an unchanged revision makes the entry position meaningful.
    /// </summary>
    private static OperationResult<int> ReadStart(string? cursor, string fingerprint, DeckDocument deck)
    {
        if (cursor is null)
        {
            return new OperationSuccess<int>(0);
        }

        DeckViewCursor? decoded = DecodeCursor(cursor);
        if (decoded?.Fingerprint is null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(decoded.Fingerprint),
                Encoding.UTF8.GetBytes(fingerprint)))
        {
            return InvalidCursor();
        }

        if (decoded.Revision != deck.Revision)
        {
            return new OperationConflict(
                "deck-changed-between-pages",
                "The deck changed since the first page. Start again without a cursor.",
                new ConflictState(deck.Revision));
        }

        return decoded.Offset < 0 || decoded.Offset > deck.Entries.Count
            ? InvalidCursor()
            : new OperationSuccess<int>(decoded.Offset);
    }

    /// <summary>
    /// Decodes a cursor, or returns null when it is malformed.
    /// </summary>
    private static DeckViewCursor? DecodeCursor(string cursor)
    {
        try
        {
            string padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            return JsonSerializer.Deserialize<DeckViewCursor>(Convert.FromBase64String(padded));
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the failure for a cursor that does not belong to this request.
    /// </summary>
    private static OperationInvalidInput InvalidCursor()
    {
        return new OperationInvalidInput(
            "invalid-cursor",
            "The cursor is not valid for this request. Pass nextCursor unchanged with the same deckId, include, " +
            "and fields, or start again without a cursor.",
            "cursor");
    }

    /// <summary>
    /// Builds the page result from the page's entries, cutting the page short when it would pass the size limit.
    /// </summary>
    private static OperationResult<DeckDocumentView> BuildPage(
        DeckDocument deck,
        List<DeckEntryView> entries,
        int offset,
        string fingerprint,
        IReadOnlyList<string>? limits,
        string narrowHint)
    {
        ResultPageSlice slice = ResultPage.Take(
            entries,
            0,
            ResultSize.Measure(PageHeader(deck, offset)),
            entry => EntryBytes(entry, deck.CategoryAssignments));
        int keep = slice.Count;
        bool truncated = keep < entries.Count;
        List<DeckEntryView> kept = entries.GetRange(0, keep);
        HashSet<Guid> keptIds = [.. kept.Select(entry => entry.EntryId)];
        List<DeckCategoryAssignment> assignments =
            [.. deck.CategoryAssignments.Where(assignment => keptIds.Contains(assignment.EntryId))];
        int next = offset + keep;
        return new OperationSuccess<DeckDocumentView>(PageHeader(deck, offset) with
        {
            Entries = kept,
            CategoryAssignments = assignments,
            TotalEntries = deck.Entries.Count,
            NextCursor = next < deck.Entries.Count ? EncodeCursor(fingerprint, deck.Revision, next) : null,
            Limits = limits,
            Truncated = truncated ? true : null,
            Narrow = truncated ? narrowHint : null,
        });
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
    /// Builds the deck view that every page repeats, without entries or assignments: the details, categories, and
    /// provider bindings. The name is cut to a fixed length, and the description is cut on the first page and left
    /// out of the pages after it, so a long one cannot push every page over the size limit.
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
        };
    }

    /// <summary>
    /// Encodes the fingerprint, deck revision, and next entry position as an opaque cursor.
    /// </summary>
    private static string EncodeCursor(string fingerprint, long revision, int offset)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new DeckViewCursor(fingerprint, revision, offset));
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Carries the request fingerprint, deck revision, and next entry position before opaque encoding. A missing
    /// fingerprint decodes as null and is refused.
    /// </summary>
    private sealed record DeckViewCursor(string? Fingerprint, long Revision, int Offset);
}
