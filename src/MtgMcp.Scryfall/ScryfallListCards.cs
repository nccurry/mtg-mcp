using System.Globalization;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Carries one mapped card together with the facts that come from installed card data.
/// </summary>
/// <param name="Card">The mapped card.</param>
/// <param name="Tags">The oracle tag slugs of the card, sorted; empty when none or when facts were not read.</param>
/// <param name="CheapestPriceUsd">The lowest USD price across installed English printings, or null.</param>
public sealed record ScryfallListCard(ScryfallCard Card, IReadOnlyList<string> Tags, string? CheapestPriceUsd);

/// <summary>
/// Carries cards read from installed card data, one for each requested lookup, in request order.
/// </summary>
/// <param name="Installed">Whether complete card data is installed.</param>
/// <param name="Cards">One card per lookup; null where the lookup has no exact identity or is not installed.</param>
public sealed record ScryfallInstalledListCards(bool Installed, IReadOnlyList<ScryfallListCard?> Cards);

/// <summary>
/// Carries the cards of one saved search together with the snapshot's identity.
/// </summary>
/// <param name="SnapshotId">The snapshot ID.</param>
/// <param name="Checksum">The snapshot checksum, which fixes its content.</param>
/// <param name="Installed">Whether installed card data supplied the tag and price facts.</param>
/// <param name="Cards">The cards in snapshot order.</param>
public sealed record ScryfallSnapshotCards(
    Guid SnapshotId,
    string Checksum,
    bool Installed,
    IReadOnlyList<ScryfallListCard> Cards);

/// <summary>
/// Carries cards written in a response format, with the reasons some requested fields are empty.
/// </summary>
/// <param name="ResponseFormat">The format name.</param>
/// <param name="Views">The cards in request order.</param>
/// <param name="Limits">Notes on empty fields, or null.</param>
public sealed record ScryfallFormattedList(
    string ResponseFormat,
    IReadOnlyList<ScryfallCardView> Views,
    IReadOnlyList<string>? Limits);

/// <summary>
/// Reads whole cards for card lists from installed card data and saved searches, and writes them in a response
/// format. It never uses the network.
/// </summary>
internal sealed class ScryfallListCardOperations
{
    /// <summary>
    /// Names the saved request kinds whose members are card objects.
    /// </summary>
    private static readonly string[] CardSnapshotOperations =
        [ScryfallOperationNames.Search, ScryfallOperationNames.CardPrints];

    /// <summary>
    /// Reads installed cards, tags, and prices.
    /// </summary>
    private readonly ScryfallCardDataStore store;

    /// <summary>
    /// Reads saved requests.
    /// </summary>
    private readonly ScryfallSnapshotStore snapshots;

    /// <summary>
    /// Writes values the way tool results are written.
    /// </summary>
    private readonly JsonSerializerOptions resultOptions;

    /// <summary>
    /// Creates the operations around the stores of one service.
    /// </summary>
    internal ScryfallListCardOperations(
        ScryfallCardDataStore store,
        ScryfallSnapshotStore snapshots,
        JsonSerializerOptions resultOptions)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(resultOptions);
        this.store = store;
        this.snapshots = snapshots;
        this.resultOptions = resultOptions;
    }

    /// <summary>
    /// Reads the installed card for each lookup; a null lookup stands for an entry with no exact identity.
    /// </summary>
    internal async Task<OperationResult<ScryfallInstalledListCards>> ReadInstalledAsync(
        IReadOnlyList<ScryfallCardLookup?> lookups,
        bool includeRaw,
        bool includeFacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookups);
        Guid? generationId = await store.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false);
        if (generationId is null ||
            !await store.ContainsCompleteGenerationAsync(generationId.Value, cancellationToken).ConfigureAwait(false))
        {
            return NotInstalled(lookups.Count);
        }

        List<ScryfallEvidenceLookup> requests = [];
        List<int> positions = [];
        for (int index = 0; index < lookups.Count; index++)
        {
            if (lookups[index] is { } lookup)
            {
                requests.Add(new ScryfallEvidenceLookup(lookup));
                positions.Add(index);
            }
        }

        List<StoredCardDataObject?>? found = await store.FindCardsInCompleteGenerationAsync(
            requests,
            generationId.Value,
            cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            // The generation disappeared after the first check, so no card data was actually read.
            return NotInstalled(lookups.Count);
        }

        ScryfallCard?[] mapped = new ScryfallCard?[lookups.Count];
        for (int position = 0; position < found.Count; position++)
        {
            if (found[position] is { } stored)
            {
                mapped[positions[position]] = Map(stored, includeRaw);
            }
        }

        ScryfallInstalledCardFactsRead facts = await ReadFactsAsync(
            mapped,
            generationId.Value,
            includeFacts,
            cancellationToken).ConfigureAwait(false);
        if (!facts.Installed)
        {
            return NotInstalled(lookups.Count);
        }

        List<ScryfallListCard?> cards = new(mapped.Length);
        foreach (ScryfallCard? card in mapped)
        {
            cards.Add(card is null ? null : WithFacts(card, facts));
        }

        return new OperationSuccess<ScryfallInstalledListCards>(new ScryfallInstalledListCards(true, cards));
    }

    /// <summary>
    /// Reads every card of one saved search or printings request, with facts from installed card data when asked.
    /// </summary>
    internal async Task<OperationResult<ScryfallSnapshotCards>> ReadSnapshotAsync(
        Guid snapshotId,
        bool includeRaw,
        bool includeFacts,
        CancellationToken cancellationToken)
    {
        StoredSnapshot? stored = await snapshots.FindByIdAsync(snapshotId, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return new OperationNotFound("scryfall-snapshot-not-found", "The requested snapshot does not exist.");
        }

        if (!CardSnapshotOperations.Contains(stored.Header.Operation, StringComparer.Ordinal))
        {
            return new OperationInvalidInput(
                "snapshot-has-no-cards",
                $"Snapshot {snapshotId:D} is a {stored.Header.Operation} request, which holds no cards. Use the " +
                "snapshotId that scryfall_search returned.",
                "snapshotId");
        }

        if (stored.Members.Count > ScryfallListQuery.MaximumEntries)
        {
            return new OperationInvalidInput(
                "list-too-large",
                $"The snapshot has {stored.Members.Count} cards, which is more than the limit of " +
                $"{ScryfallListQuery.MaximumEntries}.",
                "snapshotId");
        }

        List<ScryfallCard> cards = new(stored.Members.Count);
        foreach (string raw in stored.Members)
        {
            using JsonDocument document = JsonDocument.Parse(raw);
            cards.Add(ScryfallMapper.Card(
                document.RootElement,
                stored.Header.RetrievedAtUtc,
                stored.Header.SnapshotId.ToString("D", CultureInfo.InvariantCulture),
                tagCoverage: "not-included",
                includeRaw: includeRaw));
        }

        Guid? generationId = includeFacts
            ? await store.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false)
            : null;
        ScryfallInstalledCardFactsRead facts = await ReadFactsAsync(
            [.. cards],
            generationId,
            includeFacts,
            cancellationToken).ConfigureAwait(false);
        List<ScryfallListCard> listCards = [.. cards.Select(card => WithFacts(card, facts))];
        return new OperationSuccess<ScryfallSnapshotCards>(
            new ScryfallSnapshotCards(snapshotId, stored.Header.Checksum, facts.Installed, listCards));
    }

    /// <summary>
    /// Writes cards in a response format, in order, with no size cut; the caller cuts pages to the size limit.
    /// </summary>
    internal async Task<OperationResult<ScryfallFormattedList>> FormatAsync(
        IReadOnlyList<ScryfallCard> cards,
        string? responseFormat,
        IReadOnlyList<string>? fields,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cards);
        OperationResult<ScryfallCardFormat> parsed = ScryfallCardFormat.Parse(responseFormat, fields);
        if (parsed is not OperationSuccess<ScryfallCardFormat> format)
        {
            return OperationFailure.Forward<ScryfallCardFormat, ScryfallFormattedList>(parsed);
        }

        ScryfallCardFormatter formatter = new(store, format.Data, resultOptions);
        ScryfallFormattedCards formatted = await formatter.FormatAsync(
            [.. cards.Select(card => (ScryfallCard?)card)],
            null,
            cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallFormattedList>(new ScryfallFormattedList(
            format.Data.Name,
            [.. formatted.Views.Select(view => view!.Value)],
            formatted.Limits));
    }

    /// <summary>
    /// Builds the result used when no complete card data is installed.
    /// </summary>
    private static OperationSuccess<ScryfallInstalledListCards> NotInstalled(int count)
    {
        return new OperationSuccess<ScryfallInstalledListCards>(
            new ScryfallInstalledListCards(false, [.. Enumerable.Repeat<ScryfallListCard?>(null, count)]));
    }

    /// <summary>
    /// Maps one installed card without tag evidence.
    /// </summary>
    private static ScryfallCard Map(StoredCardDataObject stored, bool includeRaw)
    {
        using JsonDocument document = JsonDocument.Parse(stored.RawJson);
        return ScryfallMapper.Card(
            document.RootElement,
            stored.RetrievedAtUtc,
            stored.GenerationId.ToString("D", CultureInfo.InvariantCulture),
            tagCoverage: "not-included",
            includeRaw: includeRaw);
    }

    /// <summary>
    /// Reads tags and cheapest prices for the cards, or says nothing was needed.
    /// </summary>
    private async Task<ScryfallInstalledCardFactsRead> ReadFactsAsync(
        IReadOnlyList<ScryfallCard?> cards,
        Guid? generationId,
        bool includeFacts,
        CancellationToken cancellationToken)
    {
        if (!includeFacts)
        {
            return ScryfallInstalledCardFactsRead.NotRequested;
        }

        HashSet<Guid> oracleIds = [];
        foreach (ScryfallCard? card in cards)
        {
            if (card is not null && ScryfallConciseFieldTable.EffectiveOracleId(card) is Guid oracleId)
            {
                oracleIds.Add(oracleId);
            }
        }

        return await store.ReadInstalledCardFactsAsync(
            generationId,
            oracleIds,
            includeTags: true,
            includePrice: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pairs a card with its installed-data facts.
    /// </summary>
    private static ScryfallListCard WithFacts(ScryfallCard card, ScryfallInstalledCardFactsRead facts)
    {
        ScryfallInstalledCardFacts? found = facts.FactsFor(card);
        return new ScryfallListCard(card, found?.Tags ?? [], found?.CheapestPriceUsd);
    }
}
