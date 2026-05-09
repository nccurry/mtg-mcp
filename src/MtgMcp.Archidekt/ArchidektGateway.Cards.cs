using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

public sealed partial class ArchidektGateway
{
    public async Task PersistCardsAsync(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> upsertedCards,
        IReadOnlyList<DeckCard> removedCards,
        CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        string deckId = RequireDeckId(workspace);
        List<object> cards = [];

        foreach (DeckCard card in upsertedCards)
        {
            string archidektCardId = card.ArchidektCardId
                ?? await ResolveArchidektCardIdAsync(card.Name, cancellationToken).ConfigureAwait(false);
            card.ArchidektCardId = archidektCardId;

            cards.Add(new
            {
                action = card.ArchidektDeckRelationId.HasValue ? "modify" : "add",
                cardid = ParseIntOrString(archidektCardId),
                deckRelationId = card.ArchidektDeckRelationId,
                patchId = Guid.NewGuid().ToString("N"),
                categories = card.Categories,
                modifications = new
                {
                    quantity = card.Quantity,
                    modifier = card.Modifier,
                    companion = card.Companion,
                    flippedDefault = card.FlippedDefault
                }
            });
        }

        foreach (DeckCard card in removedCards)
        {
            cards.Add(new
            {
                action = "remove",
                cardid = ParseIntOrString(card.ArchidektCardId),
                deckRelationId = card.ArchidektDeckRelationId,
                patchId = Guid.NewGuid().ToString("N"),
                categories = card.Categories,
                modifications = new
                {
                    quantity = 0,
                    modifier = card.Modifier,
                    companion = card.Companion,
                    flippedDefault = card.FlippedDefault
                }
            });
        }

        await SendJsonAsync(HttpMethod.Patch, $"api/decks/{deckId}/modifyCards/v2/", new { cards }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> ResolveArchidektCardIdAsync(string cardName, CancellationToken cancellationToken)
    {
        using JsonDocument document = await GetJsonAsync(
            $"api/cards/v2/?name={Uri.EscapeDataString(cardName)}&pageSize=25",
            cancellationToken).ConfigureAwait(false);
        string? fallback = null;

        foreach (JsonElement item in EnumerateCollection(document.RootElement))
        {
            string? id = GetString(item, "id");
            fallback ??= id;

            string? name = GetString(item, "name") ?? GetNestedString(item, "oracleCard", "name");
            if (name is not null && name.Equals(cardName, StringComparison.OrdinalIgnoreCase) && id is not null)
            {
                return id;
            }
        }

        return fallback ?? throw new InvalidOperationException($"Archidekt card id could not be resolved for '{cardName}'.");
    }
}
