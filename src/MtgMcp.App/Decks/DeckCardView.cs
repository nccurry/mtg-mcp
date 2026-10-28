using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Decks;

/// <summary>
/// Describes one deck entry, with the card fields and oracle tags the caller asked for.
/// </summary>
internal sealed record DeckEntryView(
    Guid EntryId,
    int Quantity,
    string CardName,
    Guid? OracleId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string Language,
    string Finish,
    string Zone,
    int SortOrder,
    [property: JsonPropertyName("cardData"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CardData = null,
    [property: JsonPropertyName("card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ScryfallConciseCard? Card = null,
    [property: JsonPropertyName("tags"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Tags = null)
{
    /// <summary>
    /// Copies a stored entry without card data.
    /// </summary>
    internal static DeckEntryView From(DeckEntry entry)
    {
        return new DeckEntryView(
            entry.EntryId,
            entry.Quantity,
            entry.CardName,
            entry.OracleId,
            entry.PrintingId,
            entry.SetCode,
            entry.CollectorNumber,
            entry.Language,
            entry.Finish,
            entry.Zone,
            entry.SortOrder);
    }
}

/// <summary>
/// Carries a deck as <c>deck_get</c> returns it. Without an include list it holds the whole deck and its JSON matches
/// the stored deck; with one it holds a page of entries, the categories, and the assignments of those entries.
/// </summary>
internal sealed record DeckDocumentView(
    Guid DeckId,
    string Name,
    string Description,
    string Format,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DeckEntryView> Entries,
    IReadOnlyList<DeckCategory> Categories,
    IReadOnlyList<DeckCategoryAssignment> CategoryAssignments,
    IReadOnlyList<DeckProviderBinding> ProviderBindings)
{
    /// <summary>
    /// Gets the number of entries in the whole deck, set only when the result is a page.
    /// </summary>
    [JsonPropertyName("totalEntries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalEntries { get; init; }

    /// <summary>
    /// Gets the cursor that reads the next page of entries, or null on the last page or when the result is not a page.
    /// </summary>
    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }

    /// <summary>
    /// Gets the reasons some requested card data is missing, or null when there are none.
    /// </summary>
    [JsonPropertyName("limits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Limits { get; init; }

    /// <summary>
    /// Gets true when the page holds fewer entries than requested because of the result size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to read a truncated page in smaller pieces.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}

/// <summary>
/// Builds the <c>deck_get</c> result: the stored deck, optionally with a page of entries enriched from installed
/// Scryfall card data.
/// </summary>
internal sealed class DeckCardViewService
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
    /// Creates the service around the deck store and the Scryfall service.
    /// </summary>
    internal DeckCardViewService(SqliteDeckStore store, ScryfallService scryfall)
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

        bool includeCards = include.Contains(IncludeCards, StringComparer.Ordinal);
        bool includeTags = include.Contains(IncludeTags, StringComparer.Ordinal);
        string fingerprint = Fingerprint(deckId, include, fields);
        OperationResult<int> start = ReadStart(cursor, fingerprint, deck);
        if (start is not OperationSuccess<int> offset)
        {
            return ForwardFailure(start);
        }

        int size = pageSize ?? DefaultPageSize;
        List<DeckEntry> slice = [.. deck.Entries.Skip(offset.Data).Take(size)];
        OperationResult<ScryfallInstalledCardViews> read = await ScryfallToolExecution.RunAsync(() =>
            scryfall.ReadInstalledCardViewsAsync(
                [.. slice.Select(LookupFor)],
                fields,
                includeCards,
                includeTags,
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
            DeckEntryView entry = WithCardData(DeckEntryView.From(slice[index]), views.Cards[index]);
            entries.Add(entry);
            sizes.Add(EntryBytes(entry, deck.CategoryAssignments));
        }

        int budget = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes - HeaderBytes(deck);
        int keep = 0;
        int total = 0;
        while (keep < entries.Count && (keep == 0 || total + sizes[keep] <= budget))
        {
            total += sizes[keep];
            keep++;
        }

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
            Narrow = truncated
                ? "The page was cut to stay under the result size limit. Use nextCursor to read the rest, or ask " +
                    "for fewer fields, include tags only, or a smaller pageSize."
                : null,
        });
    }

    /// <summary>
    /// Adds the installed-data state, card, and tags of one lookup to its entry.
    /// </summary>
    private static DeckEntryView WithCardData(DeckEntryView entry, ScryfallInstalledCardView view)
    {
        return entry with { CardData = view.Status, Card = view.Card, Tags = view.Tags };
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
    /// Fingerprints the request fields that must stay the same from page to page.
    /// </summary>
    private static string Fingerprint(Guid deckId, IReadOnlyList<string> include, IReadOnlyList<string>? fields)
    {
        string text = $"{deckId:D}|{string.Join(',', include.Order(StringComparer.Ordinal))}|" +
            string.Join(',', fields ?? []);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    /// <summary>
    /// Reads the first entry position of a page from its cursor, checking it belongs to this request and deck state.
    /// </summary>
    private static OperationResult<int> ReadStart(string? cursor, string fingerprint, DeckDocument deck)
    {
        if (cursor is null)
        {
            return new OperationSuccess<int>(0);
        }

        DeckViewCursor? decoded = DecodeCursor(cursor);
        if (decoded is null ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(decoded.Fingerprint),
                Encoding.UTF8.GetBytes(fingerprint)) ||
            decoded.Offset < 0 ||
            decoded.Offset > deck.Entries.Count)
        {
            return new OperationInvalidInput(
                "invalid-cursor",
                "The cursor does not belong to this request. Send the same deckId, include, and fields.",
                "cursor");
        }

        return decoded.Revision == deck.Revision
            ? new OperationSuccess<int>(decoded.Offset)
            : new OperationConflict(
                "deck-changed-between-pages",
                "The deck changed since the first page. Start again without a cursor.",
                new ConflictState(deck.Revision));
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
    /// Passes a failure through to a result of another payload type.
    /// </summary>
    private static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(OperationResult<TSource> result)
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
    /// Passes a failure from a deck read through as a deck view failure.
    /// </summary>
    private static OperationResult<DeckDocumentView> ForwardFailure<TSource>(OperationResult<TSource> result)
    {
        return ForwardFailure<TSource, DeckDocumentView>(result);
    }

    /// <summary>
    /// Carries the request fingerprint, deck revision, and next entry position before opaque encoding.
    /// </summary>
    private sealed record DeckViewCursor(string Fingerprint, long Revision, int Offset);
}
