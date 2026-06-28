using System.Text.Json;
using MtgMcp.Core;
using static MtgMcp.Core.MtgMcpJson;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Spaces deck re-reads after writes so Archidekt has time to expose new relation ids.
    /// </summary>
    private static readonly TimeSpan[] DeckRelationHydrationDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    /// <summary>
    /// Limits each Archidekt card mutation request to a size the live API accepts reliably.
    /// </summary>
    private const int CardMutationBatchSize = 50;

    /// <summary>
    /// Persists added, modified, and removed workspace cards to the bound Archidekt deck.
    /// </summary>
    public async Task PersistCardsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> upsertedCards,
        IReadOnlyList<DeckCard> removedCards,
        CancellationToken cancellationToken
    )
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        await ResolveMissingArchidektCardIdsAsync(upsertedCards, cancellationToken)
            .ConfigureAwait(false);

        List<(DeckCard? UpsertedCard, Dictionary<string, object?> Payload)> mutations = [];
        foreach (DeckCard card in upsertedCards)
        {
            string archidektCardId =
                card.ArchidektCardId
                ?? throw new InvalidOperationException(
                    $"Archidekt card id could not be resolved for '{card.Name}'.");
            card.ArchidektCardId = archidektCardId;

            mutations.Add((
                card,
                BuildCardMutationPayload(
                    card.ArchidektDeckRelationId.HasValue ? "modify" : "add",
                    archidektCardId,
                    card)));
        }

        foreach (DeckCard card in removedCards)
        {
            mutations.Add((
                null,
                BuildCardMutationPayload("remove", card.ArchidektCardId, card, quantity: 0)));
        }

        foreach ((DeckCard? UpsertedCard, Dictionary<string, object?> Payload)[] batch
            in mutations.Chunk(CardMutationBatchSize))
        {
            await PersistMutationBatchAsync(deckId, batch, refreshOnFailure: true, cancellationToken)
                .ConfigureAwait(false);
        }

        if (upsertedCards.Any(card => !card.ArchidektDeckRelationId.HasValue))
        {
            await HydrateMissingDeckRelationIdsAsync(deckId, upsertedCards, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves Archidekt card ids without writing card rows.
    /// </summary>
    public async Task ResolveCardIdsAsync(
        IReadOnlyList<DeckCard> cards,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        await ResolveMissingArchidektCardIdsAsync(cards, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a modifyCards payload item without null fields rejected by Archidekt.
    /// </summary>
    private static Dictionary<string, object?> BuildCardMutationPayload(
        string action,
        string? archidektCardId,
        DeckCard card,
        int? quantity = null)
    {
        DeckCategoryOrdering.Normalize(card);
        Dictionary<string, object?> modifications = new(StringComparer.Ordinal)
        {
            ["quantity"] = quantity ?? card.Quantity,
            ["companion"] = card.Companion,
            ["flippedDefault"] = card.FlippedDefault,
        };
        if (!string.IsNullOrWhiteSpace(card.Modifier))
        {
            modifications["modifier"] = card.Modifier;
        }

        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["cardid"] = ParseIntOrString(archidektCardId),
            ["patchId"] = Guid.NewGuid().ToString("N"),
            ["categories"] = card.Categories,
            ["modifications"] = modifications,
        };
        if (card.ArchidektDeckRelationId.HasValue)
        {
            payload["deckRelationId"] = card.ArchidektDeckRelationId.Value;
        }

        return payload;
    }

    /// <summary>
    /// Sends one Archidekt card mutation batch and narrows opaque failures to actionable card diagnostics.
    /// </summary>
    private async Task PersistMutationBatchAsync(
        string deckId,
        IReadOnlyList<(DeckCard? UpsertedCard, Dictionary<string, object?> Payload)> batch,
        bool refreshOnFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = await SendMutationBatchAsync(deckId, batch, cancellationToken)
                .ConfigureAwait(false);
            ApplyDeckRelationIds(document.RootElement, GetUpsertedCards(batch));
            return;
        }
        catch (HttpRequestException exception)
        {
            if (refreshOnFailure)
            {
                List<DeckCard> refreshedCards = GetUpsertedCards(batch);
                if (refreshedCards.Count > 0)
                {
                    await ReReadDestinationStateAsync(deckId, refreshedCards, cancellationToken)
                        .ConfigureAwait(false);
                    await RefreshArchidektCardIdsAsync(refreshedCards, cancellationToken)
                        .ConfigureAwait(false);

                    try
                    {
                        using JsonDocument retryDocument = await SendMutationBatchAsync(
                                deckId,
                                batch,
                                cancellationToken)
                            .ConfigureAwait(false);
                        ApplyDeckRelationIds(retryDocument.RootElement, refreshedCards);
                        return;
                    }
                    catch (HttpRequestException retryException)
                    {
                        exception = retryException;
                    }
                }
            }

            if (batch.Count <= 1)
            {
                throw CreateCardMutationException(batch, exception);
            }

            List<DeckCard> upsertedCards = GetUpsertedCards(batch);
            await ReReadDestinationStateAsync(deckId, upsertedCards, cancellationToken)
                .ConfigureAwait(false);

            int midpoint = batch.Count / 2;
            (DeckCard? UpsertedCard, Dictionary<string, object?> Payload)[] firstHalf = batch
                .Take(midpoint)
                .ToArray();
            (DeckCard? UpsertedCard, Dictionary<string, object?> Payload)[] secondHalf = batch
                .Skip(midpoint)
                .ToArray();

            List<Exception> failures = [];
            await PersistMutationBatchHalfAsync(deckId, firstHalf, failures, cancellationToken)
                .ConfigureAwait(false);
            await PersistMutationBatchHalfAsync(deckId, secondHalf, failures, cancellationToken)
                .ConfigureAwait(false);

            if (failures.Count == 1)
            {
                throw failures[0];
            }

            if (failures.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Archidekt rejected {failures.Count} card mutation rows after batch bisection.",
                    new AggregateException(failures));
            }
        }
    }

    /// <summary>
    /// Persists one bisected batch half and records card-level failures so later safe rows can still write.
    /// </summary>
    private async Task PersistMutationBatchHalfAsync(
        string deckId,
        IReadOnlyList<(DeckCard? UpsertedCard, Dictionary<string, object?> Payload)> batch,
        List<Exception> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await PersistMutationBatchAsync(deckId, batch, refreshOnFailure: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            failures.Add(exception);
        }
    }

    /// <summary>
    /// Sends one raw Archidekt card mutation request.
    /// </summary>
    private async Task<JsonDocument> SendMutationBatchAsync(
        string deckId,
        IReadOnlyList<(DeckCard? UpsertedCard, Dictionary<string, object?> Payload)> batch,
        CancellationToken cancellationToken)
    {
        List<object> cards = BuildMutationPayloads(batch);
        return await SendJsonAsync(
                HttpMethod.Patch,
                $"api/decks/{deckId}/modifyCards/v2/",
                new { cards },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds mutation payloads from current card ids and relation ids before every retry.
    /// </summary>
    private static List<object> BuildMutationPayloads(
        IReadOnlyList<(DeckCard? UpsertedCard, Dictionary<string, object?> Payload)> batch)
    {
        List<object> cards = [];
        foreach ((DeckCard? upsertedCard, Dictionary<string, object?> payload) in batch)
        {
            if (upsertedCard is null)
            {
                cards.Add(payload);
                continue;
            }

            string archidektCardId =
                upsertedCard.ArchidektCardId
                ?? throw new InvalidOperationException(
                    $"Archidekt card id could not be resolved for '{upsertedCard.Name}'.");
            cards.Add(BuildCardMutationPayload(
                upsertedCard.ArchidektDeckRelationId.HasValue ? "modify" : "add",
                archidektCardId,
                upsertedCard));
        }

        return cards;
    }

    /// <summary>
    /// Extracts card rows from a mixed mutation batch.
    /// </summary>
    private static List<DeckCard> GetUpsertedCards(
        IReadOnlyList<(DeckCard? UpsertedCard, Dictionary<string, object?> Payload)> batch)
    {
        List<DeckCard> cards = [];
        foreach ((DeckCard? upsertedCard, _) in batch)
        {
            if (upsertedCard is not null)
            {
                cards.Add(upsertedCard);
            }
        }

        return cards;
    }

    /// <summary>
    /// Refreshes known relation ids from the destination deck before retrying a failed write.
    /// </summary>
    private async Task ReReadDestinationStateAsync(
        string deckId,
        IReadOnlyList<DeckCard> upsertedCards,
        CancellationToken cancellationToken)
    {
        if (upsertedCards.Count == 0
            || upsertedCards.All(card => card.ArchidektDeckRelationId.HasValue))
        {
            return;
        }

        using JsonDocument document = await GetJsonAsync($"api/decks/{deckId}/", cancellationToken)
            .ConfigureAwait(false);
        List<DeckCategory> categories = ParseCategories(document.RootElement);
        List<DeckCard> remoteCards = ParseCards(document.RootElement, categories);
        ApplyRemoteDeckRelationIds(remoteCards, upsertedCards);
    }

    /// <summary>
    /// Creates a card-level exception when bisection identifies a single rejected mutation row.
    /// </summary>
    private static InvalidOperationException CreateCardMutationException(
        IReadOnlyList<(DeckCard? UpsertedCard, Dictionary<string, object?> Payload)> batch,
        HttpRequestException exception)
    {
        string cardName = batch.Count == 1 && batch[0].UpsertedCard is { } card
            ? card.Name
            : "unknown card";
        string archidektCardId = batch.Count == 1 && batch[0].UpsertedCard is { } upsertedCard
            ? upsertedCard.ArchidektCardId ?? "unknown"
            : "unknown";

        return new InvalidOperationException(
            $"Archidekt rejected card mutation for '{cardName}' "
                + $"(archidektCardId={archidektCardId}). "
                + "The cached id was evicted and re-resolved once; inspect this card row before retrying.",
            exception);
    }

    /// <summary>
    /// Copies Archidekt-assigned deck relation ids from modifyCards responses onto mutated cards.
    /// </summary>
    private static void ApplyDeckRelationIds(
        JsonElement root,
        IReadOnlyList<DeckCard> upsertedCards
    )
    {
        if (upsertedCards.Count == 0)
        {
            return;
        }

        foreach (JsonElement relation in EnumerateCardMutationResults(root))
        {
            long? relationId = GetDeckRelationId(relation);
            if (!relationId.HasValue)
            {
                continue;
            }

            DeckCard? card = FindUpsertedCard(relation, upsertedCards);
            if (card is not null)
            {
                card.ArchidektDeckRelationId = relationId.Value;
            }
        }
    }

    /// <summary>
    /// Enumerates relation-like objects from Archidekt card mutation response shapes.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateCardMutationResults(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        bool yieldedCollection = false;
        foreach (string propertyName in new[] { "cards", "results", "data", "deckCards", "relations" })
        {
            if (!root.TryGetProperty(propertyName, out JsonElement property))
            {
                continue;
            }

            yieldedCollection = true;
            foreach (JsonElement item in EnumerateCardMutationResults(property))
            {
                yield return item;
            }
        }

        if (!yieldedCollection)
        {
            yield return root;
        }
    }

    /// <summary>
    /// Finds the mutated workspace card that corresponds to an Archidekt relation response.
    /// </summary>
    private static DeckCard? FindUpsertedCard(
        JsonElement relation,
        IReadOnlyList<DeckCard> upsertedCards
    )
    {
        long? relationId = GetDeckRelationId(relation);
        if (relationId.HasValue)
        {
            DeckCard? existingRelation = upsertedCards.FirstOrDefault(card =>
                card.ArchidektDeckRelationId == relationId.Value
            );
            if (existingRelation is not null)
            {
                return existingRelation;
            }
        }

        string? cardId = GetRelationCardId(relation);
        if (!string.IsNullOrWhiteSpace(cardId))
        {
            DeckCard? existingCardId = upsertedCards.FirstOrDefault(card =>
                card.ArchidektCardId?.Equals(cardId, StringComparison.OrdinalIgnoreCase) == true
            );
            if (existingCardId is not null)
            {
                return existingCardId;
            }
        }

        string? name = GetRelationCardName(relation);
        return string.IsNullOrWhiteSpace(name)
            ? null
            : upsertedCards.FirstOrDefault(card =>
                card.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            );
    }

    /// <summary>
    /// Re-reads the deck when Archidekt accepts a card add without returning relation ids.
    /// </summary>
    private async Task HydrateMissingDeckRelationIdsAsync(
        string deckId,
        IReadOnlyList<DeckCard> upsertedCards,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 0; attempt <= DeckRelationHydrationDelays.Length; attempt++)
        {
            using JsonDocument document = await GetJsonAsync($"api/decks/{deckId}/", cancellationToken)
                .ConfigureAwait(false);
            List<DeckCategory> categories = ParseCategories(document.RootElement);
            List<DeckCard> remoteCards = ParseCards(document.RootElement, categories);

            ApplyRemoteDeckRelationIds(remoteCards, upsertedCards);

            if (upsertedCards.All(card => card.ArchidektDeckRelationId.HasValue))
            {
                return;
            }

            if (attempt < DeckRelationHydrationDelays.Length)
            {
                await Task.Delay(DeckRelationHydrationDelays[attempt], cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Copies relation ids from a fresh destination read onto matching local card rows.
    /// </summary>
    private static void ApplyRemoteDeckRelationIds(
        IReadOnlyList<DeckCard> remoteCards,
        IReadOnlyList<DeckCard> upsertedCards)
    {
        foreach (DeckCard card in upsertedCards.Where(card => !card.ArchidektDeckRelationId.HasValue))
        {
            DeckCard? remote = remoteCards.FirstOrDefault(remoteCard =>
                IsSameArchidektCard(remoteCard, card)
                && HasSameCategorySet(remoteCard.Categories, card.Categories)
                && remoteCard.Quantity == card.Quantity
            ) ?? remoteCards.FirstOrDefault(remoteCard =>
                IsSameArchidektCard(remoteCard, card)
                && HasSameCategorySet(remoteCard.Categories, card.Categories)
            );

            if (remote?.ArchidektDeckRelationId is not null)
            {
                card.ArchidektDeckRelationId = remote.ArchidektDeckRelationId;
            }
        }
    }

    /// <summary>
    /// Compares local and remote Archidekt card identities using ids first, then name.
    /// </summary>
    private static bool IsSameArchidektCard(DeckCard left, DeckCard right)
    {
        if (
            !string.IsNullOrWhiteSpace(left.ArchidektCardId)
            && !string.IsNullOrWhiteSpace(right.ArchidektCardId)
        )
        {
            return left.ArchidektCardId.Equals(right.ArchidektCardId, StringComparison.OrdinalIgnoreCase);
        }

        return left.Name.Equals(right.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares Archidekt category tags as an unordered case-insensitive set.
    /// </summary>
    private static bool HasSameCategorySet(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count
            && left.All(value => right.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the Archidekt card id from relation response variants.
    /// </summary>
    private static string? GetRelationCardId(JsonElement relation)
    {
        string? directId =
            GetString(relation, "cardid")
            ?? GetString(relation, "cardId")
            ?? GetString(relation, "card_id")
            ?? GetNestedString(relation, "card", "id")
            ?? GetNestedString(relation, "card", "pk");
        if (!string.IsNullOrWhiteSpace(directId))
        {
            return directId;
        }

        return relation.TryGetProperty("card", out JsonElement card)
            && card.ValueKind is JsonValueKind.Number or JsonValueKind.String
            ? GetString(relation, "card")
            : null;
    }

    /// <summary>
    /// Reads the printed card name from relation response variants.
    /// </summary>
    private static string? GetRelationCardName(JsonElement relation)
    {
        string? name =
            GetString(relation, "name")
            ?? GetNestedString(relation, "card", "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (
            relation.TryGetProperty("card", out JsonElement card)
            && card.ValueKind == JsonValueKind.Object
            && card.TryGetProperty("oracleCard", out JsonElement oracleCard)
            && oracleCard.ValueKind == JsonValueKind.Object
        )
        {
            return GetString(oracleCard, "name");
        }

        return null;
    }

    /// <summary>
    /// Resolves missing Archidekt card ids once for each unique imported print.
    /// </summary>
    private async Task ResolveMissingArchidektCardIdsAsync(
        IReadOnlyList<DeckCard> upsertedCards,
        CancellationToken cancellationToken)
    {
        List<IGrouping<string, DeckCard>> unresolvedGroups = upsertedCards
            .Where(card => string.IsNullOrWhiteSpace(card.ArchidektCardId))
            .GroupBy(GetResolutionCacheKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unresolvedGroups.Count == 0)
        {
            return;
        }

        foreach (IGrouping<string, DeckCard> group in unresolvedGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchidektCardIdCacheEntry? cachedEntry = await TryGetCachedArchidektCardIdAsync(
                    group.First(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (cachedEntry is not null && !string.IsNullOrWhiteSpace(cachedEntry.ArchidektId))
            {
                foreach (DeckCard card in group)
                {
                    card.ArchidektCardId = cachedEntry.ArchidektId;
                    card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution] = "cache";
                }

                continue;
            }

            ArchidektCardIdResolution resolution = await ResolveArchidektCardIdAsync(
                    group.First(),
                    cancellationToken)
                .ConfigureAwait(false);
            await StoreCachedArchidektCardIdAsync(group.First(), resolution, cancellationToken)
                .ConfigureAwait(false);
            foreach (DeckCard card in group)
            {
                card.ArchidektCardId = resolution.ArchidektId;
                card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution] = "resolved";
            }
        }
    }

    /// <summary>
    /// Evicts cached ids for cards in a failed mutation batch and resolves them from Archidekt again.
    /// </summary>
    private async Task RefreshArchidektCardIdsAsync(
        IReadOnlyList<DeckCard> cards,
        CancellationToken cancellationToken)
    {
        await EvictCachedArchidektCardIdsAsync(cards, cancellationToken).ConfigureAwait(false);
        List<IGrouping<string, DeckCard>> groups = cards
            .GroupBy(GetResolutionCacheKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (IGrouping<string, DeckCard> group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchidektCardIdResolution resolution = await ResolveArchidektCardIdAsync(
                    group.First(),
                    cancellationToken)
                .ConfigureAwait(false);
            await StoreCachedArchidektCardIdAsync(group.First(), resolution, cancellationToken)
                .ConfigureAwait(false);
            foreach (DeckCard card in group)
            {
                card.ArchidektCardId = resolution.ArchidektId;
                card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution] = "refreshed";
            }
        }
    }

    /// <summary>
    /// Builds a cache key for cards that should resolve to the same Archidekt print.
    /// </summary>
    private static string GetResolutionCacheKey(DeckCard card)
    {
        string printKey = MtgMcpText.FirstNonEmpty(
                card.ScryfallId,
                string.IsNullOrWhiteSpace(card.Snapshot.Set)
                    || string.IsNullOrWhiteSpace(card.Snapshot.CollectorNumber)
                    ? null
                    : $"{card.Snapshot.Set}:{card.Snapshot.CollectorNumber}",
                card.Name)
            ?? "";
        return $"{card.Name}|{printKey}";
    }

    /// <summary>
    /// Resolves a workspace card to the Archidekt print id accepted by card mutation calls.
    /// </summary>
    private async Task<ArchidektCardIdResolution> ResolveArchidektCardIdAsync(
        DeckCard card,
        CancellationToken cancellationToken
    )
    {
        using JsonDocument document = await GetJsonAsync(
                $"api/cards/v2/?name={Uri.EscapeDataString(card.Name)}&pageSize=25",
                cancellationToken
            )
            .ConfigureAwait(false);
        string? fallback = null;
        string? exactNameFallback = null;

        foreach (JsonElement item in EnumerateCollection(document.RootElement, "results", "data", "decks"))
        {
            string? id = GetString(item, "id");
            fallback ??= id;

            if (id is not null && IsSameScryfallPrint(item, card))
            {
                return new ArchidektCardIdResolution(id, "scryfall-print-match");
            }

            string? name = GetString(item, "name") ?? GetNestedString(item, "oracleCard", "name");
            if (
                name is not null
                && name.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                && id is not null
            )
            {
                exactNameFallback ??= id;
            }
        }

        if (!string.IsNullOrWhiteSpace(exactNameFallback))
        {
            return new ArchidektCardIdResolution(exactNameFallback, "exact-name-match");
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return new ArchidektCardIdResolution(fallback, "first-result-fallback");
        }

        throw new InvalidOperationException(
                $"Archidekt card id could not be resolved for '{card.Name}'."
            );
    }

    /// <summary>
    /// Checks whether an Archidekt card search result matches the imported Scryfall print.
    /// </summary>
    private static bool IsSameScryfallPrint(JsonElement item, DeckCard card)
    {
        if (
            !string.IsNullOrWhiteSpace(card.ScryfallId)
            && card.ScryfallId.Equals(GetString(item, "uid"), StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        string? set =
            GetString(item, "set")
            ?? GetString(item, "setCode")
            ?? GetNestedString(item, "edition", "editioncode")
            ?? GetNestedString(item, "edition", "code");
        string? collector =
            GetString(item, "collectorNumber")
            ?? GetString(item, "collector_number");
        return !string.IsNullOrWhiteSpace(card.Snapshot.Set)
            && !string.IsNullOrWhiteSpace(card.Snapshot.CollectorNumber)
            && card.Snapshot.Set.Equals(set, StringComparison.OrdinalIgnoreCase)
            && card.Snapshot.CollectorNumber.Equals(collector, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a cached Archidekt card id for any stable key on the card.
    /// </summary>
    private async Task<ArchidektCardIdCacheEntry?> TryGetCachedArchidektCardIdAsync(
        DeckCard card,
        CancellationToken cancellationToken)
    {
        await cardIdCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, ArchidektCardIdCacheEntry> cache = await LoadCardIdCacheAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (string key in GetArchidektCardIdCacheKeys(card))
            {
                if (cache.TryGetValue(key, out ArchidektCardIdCacheEntry? entry)
                    && !string.IsNullOrWhiteSpace(entry.ArchidektId))
                {
                    if (cardIdCacheNeedsSave)
                    {
                        await SaveCardIdCacheAsync(cache, cancellationToken).ConfigureAwait(false);
                    }

                    return entry;
                }
            }

            if (cardIdCacheNeedsSave)
            {
                await SaveCardIdCacheAsync(cache, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }
        finally
        {
            cardIdCacheLock.Release();
        }
    }

    /// <summary>
    /// Stores a resolved Archidekt card id under every stable key available for the card.
    /// </summary>
    private async Task StoreCachedArchidektCardIdAsync(
        DeckCard card,
        ArchidektCardIdResolution resolution,
        CancellationToken cancellationToken)
    {
        await cardIdCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, ArchidektCardIdCacheEntry> cache = await LoadCardIdCacheAsync(cancellationToken)
                .ConfigureAwait(false);
            bool changed = false;
            foreach (string key in GetArchidektCardIdCacheKeys(card))
            {
                ArchidektCardIdCacheEntry entry = CreateCardIdCacheEntry(card, resolution);
                if (!cache.TryGetValue(key, out ArchidektCardIdCacheEntry? existing)
                    || !existing.ArchidektId.Equals(resolution.ArchidektId, StringComparison.OrdinalIgnoreCase)
                    || !existing.ValidationStatus.Equals(
                        resolution.ValidationStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    cache[key] = entry;
                    changed = true;
                }
            }

            if (changed || cardIdCacheNeedsSave)
            {
                await SaveCardIdCacheAsync(cache, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            cardIdCacheLock.Release();
        }
    }

    /// <summary>
    /// Removes suspect cached ids before re-resolving a failed mutation batch.
    /// </summary>
    private async Task EvictCachedArchidektCardIdsAsync(
        IReadOnlyList<DeckCard> cards,
        CancellationToken cancellationToken)
    {
        await cardIdCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, ArchidektCardIdCacheEntry> cache = await LoadCardIdCacheAsync(cancellationToken)
                .ConfigureAwait(false);
            bool changed = false;
            foreach (DeckCard card in cards)
            {
                foreach (string key in GetArchidektCardIdCacheKeys(card))
                {
                    changed |= cache.Remove(key);
                }

                card.ArchidektCardId = null;
            }

            if (changed || cardIdCacheNeedsSave)
            {
                await SaveCardIdCacheAsync(cache, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            cardIdCacheLock.Release();
        }
    }

    /// <summary>
    /// Loads the persistent card-id cache, treating missing or malformed files as empty.
    /// </summary>
    private async Task<Dictionary<string, ArchidektCardIdCacheEntry>> LoadCardIdCacheAsync(
        CancellationToken cancellationToken)
    {
        if (cardIdCache is not null)
        {
            return cardIdCache;
        }

        string path = GetCardIdCacheFilePath();
        if (!File.Exists(path))
        {
            cardIdCache = new Dictionary<string, ArchidektCardIdCacheEntry>(StringComparer.OrdinalIgnoreCase);
            return cardIdCache;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            using JsonDocument document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cardIdCache = ParseCardIdCache(document.RootElement, File.GetLastWriteTimeUtc(path));
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is JsonException)
        {
            cardIdCache = new Dictionary<string, ArchidektCardIdCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }

        return cardIdCache;
    }

    /// <summary>
    /// Saves the card-id cache used to avoid repeated Archidekt print searches.
    /// </summary>
    private async Task SaveCardIdCacheAsync(
        Dictionary<string, ArchidektCardIdCacheEntry> cache,
        CancellationToken cancellationToken)
    {
        string path = GetCardIdCacheFilePath();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, cache, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        cardIdCacheNeedsSave = false;
    }

    /// <summary>
    /// Parses both structured cache entries and the legacy string-valued cache format.
    /// </summary>
    private Dictionary<string, ArchidektCardIdCacheEntry> ParseCardIdCache(
        JsonElement root,
        DateTime legacyTimestamp)
    {
        Dictionary<string, ArchidektCardIdCacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return cache;
        }

        DateTimeOffset timestamp = new(DateTime.SpecifyKind(legacyTimestamp, DateTimeKind.Utc));
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                string? archidektId = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(archidektId))
                {
                    continue;
                }

                cache[property.Name] = CreateLegacyCardIdCacheEntry(
                    property.Name,
                    archidektId,
                    timestamp);
                cardIdCacheNeedsSave = true;
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                cardIdCacheNeedsSave = true;
                continue;
            }

            ArchidektCardIdCacheEntry? entry = property.Value.Deserialize<ArchidektCardIdCacheEntry>(
                SerializerOptions);
            if (entry is null || string.IsNullOrWhiteSpace(entry.ArchidektId))
            {
                cardIdCacheNeedsSave = true;
                continue;
            }

            cache[property.Name] = NormalizeCardIdCacheEntry(property.Name, entry, timestamp);
        }

        return cache;
    }

    /// <summary>
    /// Builds a structured cache entry for a newly resolved Archidekt card id.
    /// </summary>
    private static ArchidektCardIdCacheEntry CreateCardIdCacheEntry(
        DeckCard card,
        ArchidektCardIdResolution resolution)
    {
        return new ArchidektCardIdCacheEntry
        {
            Source = "archidekt-card-search",
            Timestamp = DateTimeOffset.UtcNow,
            CardName = card.Name,
            ScryfallUid = card.ScryfallId,
            ArchidektId = resolution.ArchidektId,
            ValidationStatus = resolution.ValidationStatus,
        };
    }

    /// <summary>
    /// Wraps a legacy string cache value with provenance fields.
    /// </summary>
    private static ArchidektCardIdCacheEntry CreateLegacyCardIdCacheEntry(
        string key,
        string archidektId,
        DateTimeOffset timestamp)
    {
        return new ArchidektCardIdCacheEntry
        {
            Source = "legacy-string-cache",
            Timestamp = timestamp,
            CardName = GetCacheCardNameFromKey(key),
            ScryfallUid = GetCacheScryfallUidFromKey(key),
            ArchidektId = archidektId,
            ValidationStatus = "legacy-unvalidated",
        };
    }

    /// <summary>
    /// Fills missing structured cache fields so old prerelease entries remain readable.
    /// </summary>
    private static ArchidektCardIdCacheEntry NormalizeCardIdCacheEntry(
        string key,
        ArchidektCardIdCacheEntry entry,
        DateTimeOffset fallbackTimestamp)
    {
        if (string.IsNullOrWhiteSpace(entry.Source))
        {
            entry.Source = "archidekt-card-search";
        }

        if (entry.Timestamp == default)
        {
            entry.Timestamp = fallbackTimestamp;
        }

        if (string.IsNullOrWhiteSpace(entry.CardName))
        {
            entry.CardName = GetCacheCardNameFromKey(key);
        }

        if (string.IsNullOrWhiteSpace(entry.ScryfallUid))
        {
            entry.ScryfallUid = GetCacheScryfallUidFromKey(key);
        }

        if (string.IsNullOrWhiteSpace(entry.ValidationStatus))
        {
            entry.ValidationStatus = "unknown";
        }

        return entry;
    }

    /// <summary>
    /// Reads the card name encoded in a name-based cache key.
    /// </summary>
    private static string? GetCacheCardNameFromKey(string key)
    {
        const string prefix = "name:";
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? key[prefix.Length..]
            : null;
    }

    /// <summary>
    /// Reads the Scryfall uid encoded in a Scryfall-based cache key.
    /// </summary>
    private static string? GetCacheScryfallUidFromKey(string key)
    {
        const string prefix = "scryfall:";
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? key[prefix.Length..]
            : null;
    }

    /// <summary>
    /// Chooses the configured cache path or the user's local mtg-mcp cache file.
    /// </summary>
    private string GetCardIdCacheFilePath()
    {
        if (!string.IsNullOrWhiteSpace(options.CardIdCacheFile))
        {
            return options.CardIdCacheFile;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, "mtg-mcp", "archidekt-card-ids.json");
    }

    /// <summary>
    /// Builds stable lookup keys for a card's Scryfall id, printed set number, and name.
    /// </summary>
    private static List<string> GetArchidektCardIdCacheKeys(DeckCard card)
    {
        List<string> keys = [];
        if (!string.IsNullOrWhiteSpace(card.ScryfallId))
        {
            keys.Add($"scryfall:{card.ScryfallId}");
        }

        if (!string.IsNullOrWhiteSpace(card.Snapshot.Set)
            && !string.IsNullOrWhiteSpace(card.Snapshot.CollectorNumber))
        {
            keys.Add($"print:{card.Snapshot.Set}:{card.Snapshot.CollectorNumber}");
        }

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            keys.Add($"name:{card.Name}");
        }

        return keys;
    }

    /// <summary>
    /// Captures the chosen Archidekt id and how confidently the search result matched the card.
    /// </summary>
    private readonly record struct ArchidektCardIdResolution(
        string ArchidektId,
        string ValidationStatus);

    /// <summary>
    /// Stores a structured Archidekt card-id cache value with provenance.
    /// </summary>
    private sealed class ArchidektCardIdCacheEntry
    {
        /// <summary>
        /// Gets or sets the source that produced the cached id.
        /// </summary>
        public string Source { get; set; } = "";

        /// <summary>
        /// Gets or sets when this cache entry was written.
        /// </summary>
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the card name associated with the cached id when known.
        /// </summary>
        public string? CardName { get; set; }

        /// <summary>
        /// Gets or sets the Scryfall uid associated with the cached id when known.
        /// </summary>
        public string? ScryfallUid { get; set; }

        /// <summary>
        /// Gets or sets the Archidekt card id accepted by deck mutation calls.
        /// </summary>
        public string ArchidektId { get; set; } = "";

        /// <summary>
        /// Gets or sets the match confidence for the chosen Archidekt id.
        /// </summary>
        public string ValidationStatus { get; set; } = "";
    }
}
