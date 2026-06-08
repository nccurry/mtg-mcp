using System.Text.Json;
using MtgMcp.Core;

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
        List<object> cards = [];

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
            cards.Clear();
            cards.AddRange(batch.Select(item => item.Payload));
            using JsonDocument document = await SendJsonAsync(
                    HttpMethod.Patch,
                    $"api/decks/{deckId}/modifyCards/v2/",
                    new { cards },
                    cancellationToken
                )
                .ConfigureAwait(false);

            List<DeckCard> batchUpserts = batch
                .Select(item => item.UpsertedCard)
                .OfType<DeckCard>()
                .ToList();
            ApplyDeckRelationIds(document.RootElement, batchUpserts);
        }

        if (upsertedCards.Any(card => !card.ArchidektDeckRelationId.HasValue))
        {
            await HydrateMissingDeckRelationIdsAsync(deckId, upsertedCards, cancellationToken)
                .ConfigureAwait(false);
        }
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
            string? cachedId = await TryGetCachedArchidektCardIdAsync(group.First(), cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(cachedId))
            {
                foreach (DeckCard card in group)
                {
                    card.ArchidektCardId = cachedId;
                    card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution] = "cache";
                }

                continue;
            }

            string resolvedId = await ResolveArchidektCardIdAsync(group.First(), cancellationToken)
                .ConfigureAwait(false);
            await StoreCachedArchidektCardIdAsync(group.First(), resolvedId, cancellationToken)
                .ConfigureAwait(false);
            foreach (DeckCard card in group)
            {
                card.ArchidektCardId = resolvedId;
                card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution] = "resolved";
            }
        }
    }

    /// <summary>
    /// Builds a cache key for cards that should resolve to the same Archidekt print.
    /// </summary>
    private static string GetResolutionCacheKey(DeckCard card)
    {
        string printKey = FirstNonEmpty(
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
    private async Task<string> ResolveArchidektCardIdAsync(
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

        foreach (JsonElement item in EnumerateCollection(document.RootElement))
        {
            string? id = GetString(item, "id");
            fallback ??= id;

            if (id is not null && IsSameScryfallPrint(item, card))
            {
                return id;
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

        return exactNameFallback
            ?? fallback
            ?? throw new InvalidOperationException(
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
    private async Task<string?> TryGetCachedArchidektCardIdAsync(
        DeckCard card,
        CancellationToken cancellationToken)
    {
        await cardIdCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, string> cache = await LoadCardIdCacheAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (string key in GetArchidektCardIdCacheKeys(card))
            {
                if (cache.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
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
        string archidektCardId,
        CancellationToken cancellationToken)
    {
        await cardIdCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, string> cache = await LoadCardIdCacheAsync(cancellationToken)
                .ConfigureAwait(false);
            bool changed = false;
            foreach (string key in GetArchidektCardIdCacheKeys(card))
            {
                if (!cache.TryGetValue(key, out string? existing)
                    || !existing.Equals(archidektCardId, StringComparison.OrdinalIgnoreCase))
                {
                    cache[key] = archidektCardId;
                    changed = true;
                }
            }

            if (changed)
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
    private async Task<Dictionary<string, string>> LoadCardIdCacheAsync(CancellationToken cancellationToken)
    {
        if (cardIdCache is not null)
        {
            return cardIdCache;
        }

        string path = GetCardIdCacheFilePath();
        if (!File.Exists(path))
        {
            cardIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return cardIdCache;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            Dictionary<string, string>? loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            cardIdCache = loaded is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is JsonException)
        {
            cardIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return cardIdCache;
    }

    /// <summary>
    /// Saves the card-id cache used to avoid repeated Archidekt print searches.
    /// </summary>
    private async Task SaveCardIdCacheAsync(
        Dictionary<string, string> cache,
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
}
