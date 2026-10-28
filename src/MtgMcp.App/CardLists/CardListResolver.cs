using System.Text.Json.Serialization;
using MtgMcp.App.Decks;
using MtgMcp.App.Scryfall;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.CardLists;

/// <summary>
/// Says which list sources this server session can read. A source is accepted only when the toolset that owns it is
/// on, so selecting a narrow set of toolsets never lets a list tool reach decks or Archidekt.
/// </summary>
internal sealed class CardListAccess
{
    /// <summary>
    /// Names the reason given when the owning toolset was left out of the selection.
    /// </summary>
    internal const string ToolsetNotSelected = "toolset-not-selected";

    /// <summary>
    /// Creates access from the services of the toolsets that are on; a toolset that is off passes null.
    /// </summary>
    /// <param name="decks">The deck store, or null when the <c>decks</c> toolset is off.</param>
    /// <param name="archidekt">The Archidekt service, or null when the <c>archidekt</c> toolset is off.</param>
    /// <param name="archidektOffReason">
    /// Why Archidekt is off: <c>credentials-missing</c> or <c>credentials-invalid</c> when startup turned it off, or
    /// <c>toolset-not-selected</c> when it was left out of the selection.
    /// </param>
    internal CardListAccess(
        SqliteDeckStore? decks,
        ArchidektService? archidekt,
        string archidektOffReason = ToolsetNotSelected)
    {
        Decks = decks;
        Archidekt = archidekt;
        ArchidektOffReason = archidektOffReason;
    }

    /// <summary>
    /// Gets the deck store, or null when the <c>decks</c> toolset is off.
    /// </summary>
    internal SqliteDeckStore? Decks { get; }

    /// <summary>
    /// Gets the Archidekt service, or null when the <c>archidekt</c> toolset is off.
    /// </summary>
    internal ArchidektService? Archidekt { get; }

    /// <summary>
    /// Gets the reason the Archidekt sources are not available.
    /// </summary>
    internal string ArchidektOffReason { get; }
}

/// <summary>
/// Describes one list source that was read, with the marker that fixes its state.
/// </summary>
internal sealed record CardListSourceInfo
{
    /// <summary>
    /// Gets the position of the source in the request, such as <c>left</c> or <c>right.left</c>.
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Gets the source kind.
    /// </summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>
    /// Gets the saved search ID, for a saved search.
    /// </summary>
    [JsonPropertyName("snapshotId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SnapshotId { get; init; }

    /// <summary>
    /// Gets the checksum of a saved search.
    /// </summary>
    [JsonPropertyName("checksum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Checksum { get; init; }

    /// <summary>
    /// Gets the local deck ID.
    /// </summary>
    [JsonPropertyName("deckId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DeckId { get; init; }

    /// <summary>
    /// Gets the local deck revision that was read.
    /// </summary>
    [JsonPropertyName("revision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Revision { get; init; }

    /// <summary>
    /// Gets the backup ID.
    /// </summary>
    [JsonPropertyName("backupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? BackupId { get; init; }

    /// <summary>
    /// Gets the Archidekt deck ID.
    /// </summary>
    [JsonPropertyName("archidektDeckId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArchidektDeckId { get; init; }

    /// <summary>
    /// Gets the Archidekt state marker of a live deck, which changes when the deck changes.
    /// </summary>
    [JsonPropertyName("stateMarker")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StateMarker { get; init; }

    /// <summary>
    /// Gets the number of entries or cards the source gave.
    /// </summary>
    [JsonPropertyName("entries")]
    public int Entries { get; init; }

    /// <summary>
    /// Gets the text that the cursor stores to notice a source that changed between pages.
    /// </summary>
    [JsonIgnore]
    public string Marker { get; init; } = string.Empty;
}

/// <summary>
/// Describes a list entry that could not be matched to a Scryfall card, so it is left out of the result and listed.
/// </summary>
internal sealed record CardListUnmatched
{
    /// <summary>
    /// Gets the position of the source the entry came from.
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Gets the entry ID, when the source has entry IDs.
    /// </summary>
    [JsonPropertyName("entryId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntryId { get; init; }

    /// <summary>
    /// Gets the card name the source gave.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets why the entry has no card: <c>no-scryfall-identity</c> or <c>not-in-card-data</c>.
    /// </summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>
/// Holds one resolved entry: its card with facts, its quantity, and the deck context of the entry.
/// </summary>
internal sealed record CardListEntry(
    ScryfallListCard Card,
    int Quantity,
    string? Zone,
    IReadOnlyList<string>? Categories,
    string? EntryId);

/// <summary>
/// Holds a resolved list: the entries that have a card, the sources that were read, and the entries left unmatched.
/// </summary>
internal sealed record ResolvedCardList(
    IReadOnlyList<CardListEntry> Entries,
    IReadOnlyList<CardListSourceInfo> Sources,
    IReadOnlyList<CardListUnmatched> Unmatched,
    bool HasDeckFields);

/// <summary>
/// Holds a stored deck read from a source, with the zones the source chose and the information that fixes its state.
/// </summary>
internal sealed record ResolvedDeckDocument(DeckDocument Deck, IReadOnlyList<string>? Zones, CardListSourceInfo Info);

/// <summary>
/// Says what the resolver must read: raw provider objects for the <c>full</c> format, and tags and prices for
/// queries.
/// </summary>
internal sealed record CardListReadOptions(bool IncludeRaw, bool IncludeFacts);

/// <summary>
/// Reads any card list source into resolved cards. One resolver serves every tool that takes a list, so a new
/// source kind works in all of them.
/// </summary>
internal sealed class CardListResolver
{
    /// <summary>
    /// Gives the most levels of nested comparisons a source may hold.
    /// </summary>
    internal const int MaximumDepth = 3;

    /// <summary>
    /// Gives the most Scryfall IDs one <c>cards</c> source may hold.
    /// </summary>
    internal const int MaximumIdsPerSource = 150;

    /// <summary>
    /// Gives the most Scryfall IDs a whole request may hold across all <c>cards</c> sources.
    /// </summary>
    internal const int MaximumIdsPerRequest = 300;

    /// <summary>
    /// Says why entry card data is missing when no card data is installed.
    /// </summary>
    private const string NotInstalledMessage =
        "Scryfall card data is not installed. Run scryfall_card_data_sync to install it, then repeat the request.";

    /// <summary>
    /// Reads cards from installed data and saved searches.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Says which sources this session may read.
    /// </summary>
    private readonly CardListAccess access;

    /// <summary>
    /// Creates a resolver around the Scryfall service and the session's source access.
    /// </summary>
    internal CardListResolver(ScryfallService scryfall, CardListAccess access)
    {
        ArgumentNullException.ThrowIfNull(scryfall);
        ArgumentNullException.ThrowIfNull(access);
        this.scryfall = scryfall;
        this.access = access;
    }

    /// <summary>
    /// Checks the parts of a source that need no input or output: nesting depth, the number of IDs, the operation and
    /// match names of nested comparisons, and blank texts. Call it before reading anything.
    /// </summary>
    internal static OperationInvalidInput? Validate(CardListSourceInput source, string path, ref int idCount)
    {
        return Validate(source, path, 0, ref idCount);
    }

    /// <summary>
    /// Reads one source into resolved cards. A failure names the part of the request that caused it.
    /// </summary>
    internal async Task<OperationResult<ResolvedCardList>> ResolveAsync(
        CardListSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        switch (source)
        {
            case ScryfallSnapshotSourceInput snapshot:
                return await ResolveSnapshotAsync(snapshot, side, options, cancellationToken).ConfigureAwait(false);
            case CardsSourceInput cards:
                return await ResolveCardsAsync(cards, side, options, cancellationToken).ConfigureAwait(false);
            case DeckSourceInput deck:
                return await ResolveDeckAsync(deck, side, options, cancellationToken).ConfigureAwait(false);
            case DeckBackupSourceInput backup:
                return await ResolveBackupAsync(backup, side, options, cancellationToken).ConfigureAwait(false);
            case ArchidektDeckSourceInput remote:
                return await ResolveArchidektDeckAsync(remote, side, options, cancellationToken).ConfigureAwait(false);
            case ArchidektSnapshotSourceInput remoteSnapshot:
                return await ResolveArchidektSnapshotAsync(remoteSnapshot, side, options, cancellationToken)
                    .ConfigureAwait(false);
            case CompareSourceInput compare:
                return await ResolveCompareAsync(compare, side, options, cancellationToken).ConfigureAwait(false);
            default:
                return new OperationInvalidInput("invalid-source", "The list source kind is not supported.", side);
        }
    }

    /// <summary>
    /// Applies one set operation to two resolved lists. The result cards come from the left list when present,
    /// otherwise from the right list, and the entries each carry quantity 1.
    /// </summary>
    internal static ResolvedCardList Combine(
        ResolvedCardList left,
        ResolvedCardList right,
        ScryfallSetOperation operation,
        ScryfallMatchBy matchBy,
        IReadOnlyList<CardListSourceInfo>? sources = null)
    {
        Dictionary<Guid, CardListEntry> byPrinting = [];
        foreach (CardListEntry entry in left.Entries.Concat(right.Entries))
        {
            byPrinting.TryAdd(entry.Card.Card.Id, entry);
        }

        IReadOnlyList<ScryfallCard> combined = ScryfallCardSets.Combine(
            [.. left.Entries.Select(entry => entry.Card.Card)],
            [.. right.Entries.Select(entry => entry.Card.Card)],
            operation,
            matchBy);
        List<CardListEntry> entries = [];
        foreach (ScryfallCard card in combined)
        {
            CardListEntry source = byPrinting[card.Id];
            entries.Add(new CardListEntry(source.Card, 1, null, null, null));
        }

        return new ResolvedCardList(
            entries,
            sources ?? [.. left.Sources.Concat(right.Sources)],
            [.. left.Unmatched.Concat(right.Unmatched)],
            HasDeckFields: false);
    }

    /// <summary>
    /// Chooses the exact Scryfall identity of an entry: its printing, then its set and collector number, then its
    /// Oracle ID. An entry with none has no exact identity.
    /// </summary>
    internal static ScryfallCardLookup? LookupFor(
        Guid? printingId,
        string? setCode,
        string? collectorNumber,
        Guid? oracleId)
    {
        if (printingId is Guid printing)
        {
            return new ScryfallCardLookup("scryfall-id", printing.ToString("D"));
        }

        if (!string.IsNullOrWhiteSpace(setCode) && !string.IsNullOrWhiteSpace(collectorNumber))
        {
            return new ScryfallCardLookup("printing", null, setCode, collectorNumber);
        }

        return oracleId is Guid oracle ? new ScryfallCardLookup("oracle-id", oracle.ToString("D")) : null;
    }

    /// <summary>
    /// Checks one source and, for a nested comparison, its two lists.
    /// </summary>
    private static OperationInvalidInput? Validate(
        CardListSourceInput? source,
        string path,
        int depth,
        ref int idCount)
    {
        switch (source)
        {
            case null:
                return new OperationInvalidInput("invalid-source", $"{path} is required.", path);
            case CardsSourceInput cards:
                idCount += cards.Ids?.Count ?? 0;
                if (cards.Ids is null || cards.Ids.Count is 0 or > MaximumIdsPerSource)
                {
                    return new OperationInvalidInput(
                        "invalid-source",
                        $"A cards source needs 1 through {MaximumIdsPerSource} IDs.",
                        path + ".ids");
                }

                return idCount > MaximumIdsPerRequest
                    ? new OperationInvalidInput(
                        "too-many-card-ids",
                        $"A request can hold at most {MaximumIdsPerRequest} card IDs in all its cards sources.",
                        path + ".ids")
                    : null;
            case ArchidektDeckSourceInput { ArchidektDeckId: var deckId } when string.IsNullOrWhiteSpace(deckId):
                return new OperationInvalidInput("invalid-source", "archidektDeckId cannot be blank.", path);
            case ArchidektSnapshotSourceInput remote
                when string.IsNullOrWhiteSpace(remote.ArchidektDeckId) || string.IsNullOrWhiteSpace(remote.SnapshotId):
                return new OperationInvalidInput(
                    "invalid-source",
                    "archidektDeckId and snapshotId cannot be blank.",
                    path);
            case CompareSourceInput compare:
                return ValidateCompare(compare, path, depth + 1, ref idCount);
            default:
                return null;
        }
    }

    /// <summary>
    /// Checks a nested comparison: its depth, its names, and both of its lists.
    /// </summary>
    private static OperationInvalidInput? ValidateCompare(
        CompareSourceInput compare,
        string path,
        int depth,
        ref int idCount)
    {
        if (depth > MaximumDepth)
        {
            return new OperationInvalidInput(
                "compare-too-deep",
                $"Nested compare sources can go at most {MaximumDepth} levels deep.",
                path);
        }

        if (!ScryfallSetNames.TryParseOperation(compare.Operation, out _))
        {
            return new OperationInvalidInput(
                "invalid-operation",
                $"operation must be one of: {string.Join(", ", ScryfallSetNames.Operations)}.",
                path + ".operation");
        }

        if (!ScryfallSetNames.TryParseMatchBy(compare.MatchBy, out _))
        {
            return new OperationInvalidInput(
                "invalid-match-by",
                $"matchBy must be one of: {string.Join(", ", ScryfallSetNames.MatchModes)}.",
                path + ".matchBy");
        }

        return Validate(compare.Left, path + ".left", depth, ref idCount) ??
            Validate(compare.Right, path + ".right", depth, ref idCount);
    }

    /// <summary>
    /// Reads a saved search.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveSnapshotAsync(
        ScryfallSnapshotSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallSnapshotCards> read = await ScryfallToolExecution.RunAsync(() =>
            scryfall.ReadSnapshotCardsAsync(
                source.SnapshotId,
                options.IncludeRaw,
                options.IncludeFacts,
                cancellationToken)).ConfigureAwait(false);
        if (read is not OperationSuccess<ScryfallSnapshotCards> success)
        {
            return OperationFailure.Forward<ScryfallSnapshotCards, ResolvedCardList>(read);
        }

        if (options.IncludeFacts && !success.Data.Installed)
        {
            return new OperationNotCached("card-data-not-installed", NotInstalledMessage);
        }

        List<CardListEntry> entries = [.. success.Data.Cards.Select(card => new CardListEntry(card, 1, null, null,
            null))];
        CardListSourceInfo info = new()
        {
            Side = side,
            Kind = "scryfall-snapshot",
            SnapshotId = source.SnapshotId.ToString("D"),
            Checksum = success.Data.Checksum,
            Entries = entries.Count,
            Marker = "snapshot:" + success.Data.Checksum,
        };
        return new OperationSuccess<ResolvedCardList>(new ResolvedCardList(entries, [info], [], false));
    }

    /// <summary>
    /// Reads a short list of cards by Scryfall ID or Oracle ID.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveCardsAsync(
        CardsSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        List<ScryfallCardLookup?> printings = [.. source.Ids.Select(id => (ScryfallCardLookup?)new ScryfallCardLookup(
            "scryfall-id",
            id.ToString("D")))];
        OperationResult<ScryfallInstalledListCards> first = await ReadInstalledAsync(printings, options,
            cancellationToken)
            .ConfigureAwait(false);
        if (first is not OperationSuccess<ScryfallInstalledListCards> firstRead)
        {
            return OperationFailure.Forward<ScryfallInstalledListCards, ResolvedCardList>(first);
        }

        ScryfallListCard?[] cards = [.. firstRead.Data.Cards];
        List<int> misses = [.. Enumerable.Range(0, cards.Length).Where(index => cards[index] is null)];
        if (misses.Count > 0)
        {
            List<ScryfallCardLookup?> oracles = [.. misses.Select(index => (ScryfallCardLookup?)new ScryfallCardLookup(
                "oracle-id",
                source.Ids[index].ToString("D")))];
            OperationResult<ScryfallInstalledListCards> second = await ReadInstalledAsync(oracles, options,
                cancellationToken)
                .ConfigureAwait(false);
            if (second is not OperationSuccess<ScryfallInstalledListCards> secondRead)
            {
                return OperationFailure.Forward<ScryfallInstalledListCards, ResolvedCardList>(second);
            }

            for (int position = 0; position < misses.Count; position++)
            {
                cards[misses[position]] = secondRead.Data.Cards[position];
            }
        }

        List<CardListEntry> entries = [];
        List<CardListUnmatched> unmatched = [];
        for (int index = 0; index < cards.Length; index++)
        {
            if (cards[index] is { } card)
            {
                entries.Add(new CardListEntry(card, 1, null, null, null));
                continue;
            }

            unmatched.Add(new CardListUnmatched
            {
                Side = side,
                Name = source.Ids[index].ToString("D"),
                Reason = "not-in-card-data",
            });
        }

        CardListSourceInfo info = new() { Side = side, Kind = "cards", Entries = entries.Count, Marker = "cards" };
        return new OperationSuccess<ResolvedCardList>(new ResolvedCardList(entries, [info], unmatched, false));
    }

    /// <summary>
    /// Reads the stored deck of a <c>deck</c> or <c>deck-backup</c> source without resolving its cards, for tools that
    /// need the deck's entries, zones, and category IDs, such as castability. Other source kinds are refused.
    /// </summary>
    internal async Task<OperationResult<ResolvedDeckDocument>> ReadDeckDocumentAsync(
        CardListSourceInput source,
        string side,
        CancellationToken cancellationToken)
    {
        switch (source)
        {
            case DeckSourceInput deck:
                return await ReadDeckAsync(deck, side, cancellationToken).ConfigureAwait(false);
            case DeckBackupSourceInput backup:
                return await ReadBackupAsync(backup, side, cancellationToken).ConfigureAwait(false);
            default:
                return new OperationInvalidInput(
                    "invalid-source-kind",
                    "This tool reads a local deck, so the source kind must be deck or deck-backup. Entry and " +
                    "category IDs for choosing mana sources exist only in those.",
                    side + ".kind");
        }
    }

    /// <summary>
    /// Reads a local deck.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveDeckAsync(
        DeckSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        OperationResult<ResolvedDeckDocument> read = await ReadDeckAsync(source, side, cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<ResolvedDeckDocument> deck)
        {
            return OperationFailure.Forward<ResolvedDeckDocument, ResolvedCardList>(read);
        }

        return await ResolveDeckEntriesAsync(deck.Data.Deck, source.Zones, deck.Data.Info, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a local deck as it was in a backup.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveBackupAsync(
        DeckBackupSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        OperationResult<ResolvedDeckDocument> read = await ReadBackupAsync(source, side, cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<ResolvedDeckDocument> deck)
        {
            return OperationFailure.Forward<ResolvedDeckDocument, ResolvedCardList>(read);
        }

        return await ResolveDeckEntriesAsync(deck.Data.Deck, source.Zones, deck.Data.Info, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the stored deck of a <c>deck</c> source and checks its expected revision.
    /// </summary>
    private async Task<OperationResult<ResolvedDeckDocument>> ReadDeckAsync(
        DeckSourceInput source,
        string side,
        CancellationToken cancellationToken)
    {
        if (access.Decks is not { } store)
        {
            return ToolsetOff("decks", CardListAccess.ToolsetNotSelected, side);
        }

        OperationResult<DeckDocument> read = await store.GetAsync(source.DeckId,
            cancellationToken).ConfigureAwait(false);
        if (read is not OperationSuccess<DeckDocument> deck)
        {
            return OperationFailure.Forward<DeckDocument, ResolvedDeckDocument>(read);
        }

        if (source.ExpectedRevision is long expected && expected != deck.Data.Revision)
        {
            return new OperationConflict(
                "deck-revision-conflict",
                $"The deck is at revision {deck.Data.Revision}, not {expected}. Repeat the request with the " +
                "current revision, or omit expectedRevision.",
                new ConflictState(Revision: deck.Data.Revision));
        }

        CardListSourceInfo info = new()
        {
            Side = side,
            Kind = "deck",
            DeckId = deck.Data.DeckId,
            Revision = deck.Data.Revision,
            Entries = deck.Data.Entries.Count,
            Marker = "deck:" + deck.Data.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return new OperationSuccess<ResolvedDeckDocument>(new ResolvedDeckDocument(deck.Data, source.Zones, info));
    }

    /// <summary>
    /// Reads one deck from a local backup.
    /// </summary>
    private async Task<OperationResult<ResolvedDeckDocument>> ReadBackupAsync(
        DeckBackupSourceInput source,
        string side,
        CancellationToken cancellationToken)
    {
        if (access.Decks is not { } store)
        {
            return ToolsetOff("decks", CardListAccess.ToolsetNotSelected, side);
        }

        OperationResult<DeckDocument> read = await store.Backups.ReadDeckAsync(
            source.BackupId,
            source.DeckId,
            cancellationToken).ConfigureAwait(false);
        if (read is not OperationSuccess<DeckDocument> deck)
        {
            return OperationFailure.Forward<DeckDocument, ResolvedDeckDocument>(read);
        }

        CardListSourceInfo info = new()
        {
            Side = side,
            Kind = "deck-backup",
            BackupId = source.BackupId,
            DeckId = deck.Data.DeckId,
            Revision = deck.Data.Revision,
            Entries = deck.Data.Entries.Count,
            Marker = "backup:" + source.BackupId.ToString("N") + ":" +
                deck.Data.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return new OperationSuccess<ResolvedDeckDocument>(new ResolvedDeckDocument(deck.Data, source.Zones, info));
    }

    /// <summary>
    /// Turns the entries of a local deck document into resolved cards.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveDeckEntriesAsync(
        DeckDocument deck,
        IReadOnlyList<string>? zones,
        CardListSourceInfo info,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        List<DeckEntry> chosen = [.. deck.Entries.Where(entry => InZones(entry.Zone, zones))];
        Dictionary<Guid, IReadOnlyList<string>> categoryNames = DeckEntryCategories.NamesByEntry(deck);
        List<RawEntry> raw = [];
        foreach (DeckEntry entry in chosen)
        {
            IReadOnlyList<string> categories = categoryNames.GetValueOrDefault(entry.EntryId) ?? [];
            raw.Add(new RawEntry(
                LookupFor(entry.PrintingId, entry.SetCode, entry.CollectorNumber, entry.OracleId),
                entry.CardName,
                entry.Quantity,
                entry.Zone,
                categories,
                entry.EntryId.ToString("D")));
        }

        return await FinishAsync(raw, info, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a live Archidekt deck.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveArchidektDeckAsync(
        ArchidektDeckSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        if (access.Archidekt is not { } service)
        {
            return ToolsetOff("archidekt", access.ArchidektOffReason, side);
        }

        OperationResult<RemoteDeckSnapshot> read = await service.GetDeckAsync(source.ArchidektDeckId, cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<RemoteDeckSnapshot> deck)
        {
            return OperationFailure.Forward<RemoteDeckSnapshot, ResolvedCardList>(read);
        }

        CardListSourceInfo info = new()
        {
            Side = side,
            Kind = "archidekt-deck",
            ArchidektDeckId = source.ArchidektDeckId,
            StateMarker = deck.Data.RemoteFingerprint,
            Entries = deck.Data.Entries.Count,
            Marker = "archidekt:" + deck.Data.RemoteFingerprint,
        };
        return await ResolveRemoteEntriesAsync(deck.Data, source.Zones, info, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one saved Archidekt snapshot, which is fixed and needs no state marker.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveArchidektSnapshotAsync(
        ArchidektSnapshotSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        if (access.Archidekt is not { } service)
        {
            return ToolsetOff("archidekt", access.ArchidektOffReason, side);
        }

        OperationResult<RemoteNamedSnapshot> read = await service.GetSnapshotAsync(
            source.ArchidektDeckId,
            source.SnapshotId,
            cancellationToken).ConfigureAwait(false);
        if (read is not OperationSuccess<RemoteNamedSnapshot> snapshot)
        {
            return OperationFailure.Forward<RemoteNamedSnapshot, ResolvedCardList>(read);
        }

        CardListSourceInfo info = new()
        {
            Side = side,
            Kind = "archidekt-snapshot",
            ArchidektDeckId = source.ArchidektDeckId,
            SnapshotId = source.SnapshotId,
            Entries = snapshot.Data.Deck.Entries.Count,
            Marker = "archidekt-snapshot:" + source.SnapshotId,
        };
        return await ResolveRemoteEntriesAsync(snapshot.Data.Deck, source.Zones, info, options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns the entries of an Archidekt deck into resolved cards.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveRemoteEntriesAsync(
        RemoteDeckSnapshot deck,
        IReadOnlyList<string>? zones,
        CardListSourceInfo info,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        List<RawEntry> raw = [];
        foreach (RemoteDeckEntry entry in deck.Entries.Where(entry => InZones(entry.Zone, zones)))
        {
            List<string> categories = [.. entry.CategoryNames];
            if (entry.PrimaryCategoryName is { Length: > 0 } primary && !categories.Contains(primary,
                StringComparer.Ordinal))
            {
                categories.Insert(0, primary);
            }

            raw.Add(new RawEntry(
                LookupFor(entry.PrintingId, entry.SetCode, entry.CollectorNumber, entry.OracleId),
                entry.CardName,
                entry.Quantity,
                entry.Zone,
                categories,
                entry.ProviderRelationId));
        }

        return await FinishAsync(raw, info, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks up the cards of raw entries in installed card data and builds the resolved list.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> FinishAsync(
        List<RawEntry> raw,
        CardListSourceInfo info,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallInstalledListCards> read = await ReadInstalledAsync(
            [.. raw.Select(entry => entry.Lookup)],
            options,
            cancellationToken).ConfigureAwait(false);
        if (read is not OperationSuccess<ScryfallInstalledListCards> success)
        {
            return OperationFailure.Forward<ScryfallInstalledListCards, ResolvedCardList>(read);
        }

        List<CardListEntry> entries = [];
        List<CardListUnmatched> unmatched = [];
        for (int index = 0; index < raw.Count; index++)
        {
            RawEntry entry = raw[index];
            if (success.Data.Cards[index] is { } card)
            {
                entries.Add(new CardListEntry(card, entry.Quantity, entry.Zone, entry.Categories, entry.EntryId));
                continue;
            }

            unmatched.Add(new CardListUnmatched
            {
                Side = info.Side,
                EntryId = entry.EntryId,
                Name = entry.Name,
                Reason = entry.Lookup is null ? "no-scryfall-identity" : "not-in-card-data",
            });
        }

        return new OperationSuccess<ResolvedCardList>(
            new ResolvedCardList(entries, [info], unmatched, HasDeckFields: true));
    }

    /// <summary>
    /// Reads installed cards and turns "card data is not installed" into a not-cached result that names the sync tool.
    /// </summary>
    private async Task<OperationResult<ScryfallInstalledListCards>> ReadInstalledAsync(
        IReadOnlyList<ScryfallCardLookup?> lookups,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallInstalledListCards> read = await ScryfallToolExecution.RunAsync(() =>
            scryfall.ReadInstalledListCardsAsync(
                lookups,
                options.IncludeRaw,
                options.IncludeFacts,
                cancellationToken)).ConfigureAwait(false);
        if (read is OperationSuccess<ScryfallInstalledListCards> { Data.Installed: false })
        {
            return new OperationNotCached("card-data-not-installed", NotInstalledMessage);
        }

        return read;
    }

    /// <summary>
    /// Reads a nested comparison: resolves both lists and combines them.
    /// </summary>
    private async Task<OperationResult<ResolvedCardList>> ResolveCompareAsync(
        CompareSourceInput source,
        string side,
        CardListReadOptions options,
        CancellationToken cancellationToken)
    {
        OperationResult<ResolvedCardList> left = await ResolveAsync(source.Left, side + ".left", options,
            cancellationToken)
            .ConfigureAwait(false);
        if (left is not OperationSuccess<ResolvedCardList> leftList)
        {
            return left;
        }

        OperationResult<ResolvedCardList> right = await ResolveAsync(source.Right, side + ".right", options,
            cancellationToken)
            .ConfigureAwait(false);
        if (right is not OperationSuccess<ResolvedCardList> rightList)
        {
            return right;
        }

        if (!ScryfallSetNames.TryParseOperation(source.Operation, out ScryfallSetOperation operation) ||
            !ScryfallSetNames.TryParseMatchBy(source.MatchBy, out ScryfallMatchBy matchBy))
        {
            return new OperationInvalidInput("invalid-source", "The nested comparison names are invalid.", side);
        }

        return new OperationSuccess<ResolvedCardList>(Combine(leftList.Data, rightList.Data, operation, matchBy));
    }

    /// <summary>
    /// Reports whether an entry's zone is one of the chosen zones; no chosen zones means every zone.
    /// </summary>
    private static bool InZones(string zone, IReadOnlyList<string>? zones)
    {
        return zones is null || zones.Count == 0 || zones.Contains(zone, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the failure for a source whose owning toolset is off.
    /// </summary>
    private static OperationInvalidInput ToolsetOff(string toolset, string reason, string path)
    {
        string message = reason == CardListAccess.ToolsetNotSelected
            ? $"This source needs the {toolset} toolset, which this server was started without. Add {toolset} to " +
                "--toolsets, or use another source."
            : $"This source needs the {toolset} toolset, which is off because its credentials are not usable " +
                $"({reason}). Add the credentials and restart the server, or use another source.";
        return new OperationInvalidInput(reason, message, path);
    }

    /// <summary>
    /// Holds one source entry before its card is looked up.
    /// </summary>
    private sealed record RawEntry(
        ScryfallCardLookup? Lookup,
        string Name,
        int Quantity,
        string Zone,
        IReadOnlyList<string> Categories,
        string EntryId);
}
