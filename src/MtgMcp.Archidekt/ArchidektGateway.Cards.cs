using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
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

        await SendJsonAsync(
                HttpMethod.Patch,
                $"api/decks/{deckId}/modifyCards/v2/",
                new { cards },
                cancellationToken
            )
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
