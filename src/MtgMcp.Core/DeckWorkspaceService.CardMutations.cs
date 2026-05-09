namespace MtgMcp.Core;

public sealed partial class DeckWorkspaceService
{
    public async Task<DeckChangeResult> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCard? existing = FindCard(workspace, cardName, normalizedCategory);
        DeckCard changed;

        if (existing is null)
        {
            changed = await CreateDeckCardAsync(cardName, Math.Max(1, quantity), normalizedCategory, cancellationToken).ConfigureAwait(false);
            workspace.Cards.Add(changed);
        }
        else
        {
            existing.Quantity += Math.Max(1, quantity);
            changed = existing;
        }

        await PersistCardsAsync(workspace, [changed], [], cancellationToken).ConfigureAwait(false);
        return Change(workspace, DeckMutationKind.CardAdded, $"Added {Math.Max(1, quantity)} {changed.Name} to {normalizedCategory}.");
    }

    public async Task<DeckChangeResult> RemoveCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, category);
        int amount = Math.Max(1, quantity);
        DeckCard? removed = null;

        if (card.Quantity <= amount)
        {
            workspace.Cards.Remove(card);
            removed = card;
        }
        else
        {
            card.Quantity -= amount;
        }

        await PersistCardsAsync(
            workspace,
            removed is null ? [card] : [],
            removed is null ? [] : [removed],
            cancellationToken).ConfigureAwait(false);

        return Change(workspace, DeckMutationKind.CardRemoved, $"Removed {amount} {card.Name}.");
    }

    public async Task<DeckChangeResult> SetCardQuantityAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, category);
        DeckCard? removed = null;

        if (quantity <= 0)
        {
            workspace.Cards.Remove(card);
            removed = card;
        }
        else
        {
            card.Quantity = quantity;
        }

        await PersistCardsAsync(
            workspace,
            removed is null ? [card] : [],
            removed is null ? [] : [removed],
            cancellationToken).ConfigureAwait(false);

        return Change(workspace, DeckMutationKind.QuantityChanged, $"Set {card.Name} quantity to {quantity}.");
    }

    public async Task<DeckChangeResult> MoveCardAsync(
        string workspaceId,
        string cardName,
        string toCategory,
        string? fromCategory,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, fromCategory);
        string normalizedCategory = NormalizeCategoryName(toCategory);
        string previousCategory = card.PrimaryCategory;
        EnsureCategory(workspace, normalizedCategory);
        card.PrimaryCategory = normalizedCategory;
        if (!previousCategory.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase))
        {
            card.Categories.RemoveAll(value => value.Equals(previousCategory, StringComparison.OrdinalIgnoreCase));
        }

        AddCategoryName(card, normalizedCategory);

        await PersistCardsAsync(workspace, [card], [], cancellationToken).ConfigureAwait(false);
        return Change(workspace, DeckMutationKind.CardMoved, $"Moved {card.Name} to {normalizedCategory}.");
    }

    private async Task<DeckCard> CreateDeckCardAsync(
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken)
    {
        CardInfo? cardInfo = await cardCatalog.GetCardAsync(cardName, cancellationToken).ConfigureAwait(false);
        DeckCard card = new()
        {
            Name = cardInfo?.Name ?? cardName.Trim(),
            Quantity = Math.Max(1, quantity),
            PrimaryCategory = category,
            Categories = [category],
            ScryfallId = cardInfo?.Id,
            ScryfallOracleId = cardInfo?.OracleId
        };

        if (cardInfo is not null)
        {
            ApplyCardSnapshot(card, cardInfo);
        }

        return card;
    }

    private static void ApplyCardSnapshot(DeckCard card, CardInfo cardInfo)
    {
        card.Snapshot = new CardSnapshot
        {
            TypeLine = cardInfo.TypeLine,
            ManaValue = cardInfo.ManaValue,
            ColorIdentity = cardInfo.ColorIdentity.ToList(),
            Set = cardInfo.Set,
            CollectorNumber = cardInfo.CollectorNumber,
            ScryfallUri = cardInfo.ScryfallUri
        };
    }

    private static DeckCard? FindCard(DeckWorkspace workspace, string cardName, string? category)
    {
        foreach (DeckCard card in workspace.Cards)
        {
            if (!card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (category is null || card.PrimaryCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
            {
                return card;
            }
        }

        return null;
    }

    private static DeckCard FindRequiredCard(DeckWorkspace workspace, string cardName, string? category)
    {
        return FindCard(workspace, cardName, category)
            ?? throw new InvalidOperationException($"Card '{cardName}' was not found in workspace '{workspace.Id}'.");
    }
}
