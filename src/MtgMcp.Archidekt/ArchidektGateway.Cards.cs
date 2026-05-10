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
    /// Persists the cards.
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

        foreach (DeckCard card in upsertedCards)
        {
            string archidektCardId =
                card.ArchidektCardId
                ?? await ResolveArchidektCardIdAsync(card.Name, cancellationToken)
                    .ConfigureAwait(false);
            card.ArchidektCardId = archidektCardId;

            cards.Add(BuildCardMutationPayload(
                card.ArchidektDeckRelationId.HasValue ? "modify" : "add",
                archidektCardId,
                card));
        }

        foreach (DeckCard card in removedCards)
        {
            cards.Add(BuildCardMutationPayload("remove", card.ArchidektCardId, card, quantity: 0));
        }

        using JsonDocument document = await SendJsonAsync(
                HttpMethod.Patch,
                $"api/decks/{deckId}/modifyCards/v2/",
                new { cards },
                cancellationToken
            )
            .ConfigureAwait(false);
        ApplyDeckRelationIds(document.RootElement, upsertedCards);
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
    /// Resolves the archidekt card id.
    /// </summary>
    private async Task<string> ResolveArchidektCardIdAsync(
        string cardName,
        CancellationToken cancellationToken
    )
    {
        using JsonDocument document = await GetJsonAsync(
                $"api/cards/v2/?name={Uri.EscapeDataString(cardName)}&pageSize=25",
                cancellationToken
            )
            .ConfigureAwait(false);
        string? fallback = null;

        foreach (JsonElement item in EnumerateCollection(document.RootElement))
        {
            string? id = GetString(item, "id");
            fallback ??= id;

            string? name = GetString(item, "name") ?? GetNestedString(item, "oracleCard", "name");
            if (
                name is not null
                && name.Equals(cardName, StringComparison.OrdinalIgnoreCase)
                && id is not null
            )
            {
                return id;
            }
        }

        return fallback
            ?? throw new InvalidOperationException(
                $"Archidekt card id could not be resolved for '{cardName}'."
            );
    }
}
