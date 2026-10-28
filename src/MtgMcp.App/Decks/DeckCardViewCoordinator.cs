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
/// Builds the <c>deck_get</c> result: the stored deck, optionally with a page of entries enriched from installed
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
        "Run scryfall_corpus_sync to install it.";

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
            return ForwardFailure(stored);
        }

        DeckDocument deck = success.Data;
        if (include is null)
        {
            List<DeckEntryView> all = [.. deck.Entries.Select(DeckEntryView.From)];
            return new OperationSuccess<DeckDocumentView>(ToView(deck, all, deck.CategoryAssignments));
        }

        string fingerprint = Fingerprint(deckId, include, fields);
        OperationResult<int> start = ReadStart(cursor, fingerprint, deck);
        if (start is not OperationSuccess<int> offset)
        {
            return ForwardFailure(start);
        }

        List<DeckEntry> slice = [.. deck.Entries.Skip(offset.Data).Take(pageSize ?? DefaultPageSize)];
        OperationResult<ScryfallInstalledCardViews> read = await ScryfallToolExecution.RunAsync(() =>
            scryfall.ReadInstalledCardViewsAsync(
                [.. slice.Select(LookupFor)],
                fields,
                include.Contains(IncludeCards, StringComparer.Ordinal),
                include.Contains(IncludeTags, StringComparer.Ordinal),
                cancellationToken)).ConfigureAwait(false);
        if (read is not OperationSuccess<ScryfallInstalledCardViews> views)
        {
            return ForwardFailure(read);
        }

        return BuildPage(deck, slice, views.Data, offset.Data, fingerprint);
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
            if (fields is not null || cursor is not null || pageSize is not null)
            {
                return new OperationInvalidInput(
                    "include-required",
                    "fields, cursor, and pageSize apply only when include is given.",
                    fields is not null ? "fields" : cursor is not null ? "cursor" : "pageSize");
            }

            return null;
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
    /// Chooses the exact Scryfall identity of an entry: its printing, then its set and collector number, then its
    /// Oracle ID. An entry with none has no exact identity.
    /// </summary>
    private static ScryfallCardLookup? LookupFor(DeckEntry entry)
    {
        if (entry.PrintingId is Guid printingId)
        {
            return new ScryfallCardLookup("scryfall-id", printingId.ToString("D"));
        }

        if (!string.IsNullOrWhiteSpace(entry.SetCode) && !string.IsNullOrWhiteSpace(entry.CollectorNumber))
        {
            return new ScryfallCardLookup("printing", null, entry.SetCode, entry.CollectorNumber);
        }

        return entry.OracleId is Guid oracleId ? new ScryfallCardLookup("oracle-id", oracleId.ToString("D")) : null;
    }

    /// <summary>
    /// Builds the page result from the entries, their installed-data views, and the deck's other parts.
    /// </summary>
    private static OperationResult<DeckDocumentView> BuildPage(
        DeckDocument deck,
        List<DeckEntry> slice,
        ScryfallInstalledCardViews views,
        int offset,
        string fingerprint)
    {
        List<DeckEntryView> entries = [];
        List<int> sizes = [];
        for (int index = 0; index < slice.Count; index++)
        {
            ScryfallInstalledCardView view = views.Cards[index];
            DeckEntryView entry = DeckEntryView.From(slice[index]) with
            {
                CardData = view.Status,
                Card = view.Card,
                Tags = view.Tags,
            };
            entries.Add(entry);
            sizes.Add(EntryBytes(entry, deck.CategoryAssignments));
        }

        int keep = ResultSizeLimit.CountFitting(sizes, HeaderBytes(deck));
        bool truncated = keep < entries.Count;
        List<DeckEntryView> kept = entries.GetRange(0, keep);
        HashSet<Guid> keptIds = [.. kept.Select(entry => entry.EntryId)];
        List<DeckCategoryAssignment> assignments =
            [.. deck.CategoryAssignments.Where(assignment => keptIds.Contains(assignment.EntryId))];
        int next = offset + keep;
        return new OperationSuccess<DeckDocumentView>(ToView(deck, kept, assignments) with
        {
            TotalEntries = deck.Entries.Count,
            NextCursor = next < deck.Entries.Count ? EncodeCursor(fingerprint, deck.Revision, next) : null,
            Limits = views.Installed ? null : [NotInstalledLimit],
            Truncated = truncated ? true : null,
            Narrow = truncated ? ScryfallFormattedResponse.NarrowHint : null,
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
        int total = Measure(entry) + 1;
        foreach (DeckCategoryAssignment assignment in assignments)
        {
            if (assignment.EntryId == entry.EntryId)
            {
                total += Measure(assignment) + 1;
            }
        }

        return total;
    }

    /// <summary>
    /// Measures the serialized size of the deck's parts that every page repeats: its details, categories, and
    /// provider bindings.
    /// </summary>
    private static int HeaderBytes(DeckDocument deck)
    {
        return Measure(ToView(deck, [], []));
    }

    /// <summary>
    /// Measures a value as the server writes it.
    /// </summary>
    private static int Measure<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, ToolCallPolicy.SerializerOptions).Length;
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
    /// Passes a failure through to a deck view result.
    /// </summary>
    private static OperationResult<DeckDocumentView> ForwardFailure<TSource>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationSuccess<TSource> => new OperationUnavailable(
                "deck-result-mismatch",
                "The deck result could not be projected."),
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
        };
    }

    /// <summary>
    /// Carries the request fingerprint, deck revision, and next entry position before opaque encoding. A missing
    /// fingerprint decodes as null and is refused.
    /// </summary>
    private sealed record DeckViewCursor(string? Fingerprint, long Revision, int Offset);
}
