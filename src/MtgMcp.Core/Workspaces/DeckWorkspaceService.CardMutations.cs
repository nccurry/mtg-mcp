namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Adds the card.
    /// </summary>
    public async Task<DeckChangeResult> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCard? existing = FindCard(workspace, cardName, normalizedCategory);
        DeckCard changed;

        if (existing is null)
        {
            changed = await CreateDeckCardAsync(
                    cardName,
                    Math.Max(1, quantity),
                    normalizedCategory,
                    cancellationToken
                )
                .ConfigureAwait(false);
            workspace.Cards.Add(changed);
        }
        else
        {
            existing.Quantity += Math.Max(1, quantity);
            changed = existing;
        }

        await PersistCardsAsync(workspace, [changed], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CardAdded,
            $"Added {Math.Max(1, quantity)} {changed.Name} to {normalizedCategory}."
        );
    }

    /// <summary>
    /// Removes the card.
    /// </summary>
    public async Task<DeckChangeResult> RemoveCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
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
                cancellationToken
            )
            .ConfigureAwait(false);

        return Change(workspace, DeckMutationKind.CardRemoved, $"Removed {amount} {card.Name}.");
    }

    /// <summary>
    /// Sets the card quantity.
    /// </summary>
    public async Task<DeckChangeResult> SetCardQuantityAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string? category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
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
                cancellationToken
            )
            .ConfigureAwait(false);

        return Change(
            workspace,
            DeckMutationKind.QuantityChanged,
            $"Set {card.Name} quantity to {quantity}."
        );
    }

    /// <summary>
    /// Moves the card.
    /// </summary>
    public async Task<DeckChangeResult> MoveCardAsync(
        string workspaceId,
        string cardName,
        string toCategory,
        string? fromCategory,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, fromCategory);
        string normalizedCategory = NormalizeCategoryName(toCategory);
        EnsureCategory(workspace, normalizedCategory);
        DeckCategoryOrdering.SetPrimary(card, normalizedCategory);

        await PersistCardsAsync(workspace, [card], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CardMoved,
            $"Moved {card.Name} to {normalizedCategory}."
        );
    }

    /// <summary>
    /// Creates the deck card.
    /// </summary>
    private async Task<DeckCard> CreateDeckCardAsync(
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken
    )
    {
        CardInfo? cardInfo = await TryGetCardForMutationAsync(cardName, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = new()
        {
            Name = cardInfo?.Name ?? cardName.Trim(),
            Quantity = Math.Max(1, quantity),
            PrimaryCategory = category,
            Categories = [category],
            ScryfallId = cardInfo?.Id,
            ScryfallOracleId = cardInfo?.OracleId,
        };

        if (cardInfo is not null)
        {
            ApplyCardSnapshot(card, cardInfo);
        }

        DeckCategoryOrdering.Normalize(card, category);
        return card;
    }

    /// <summary>
    /// Resolves optional card metadata for mutations while allowing deck edits to continue during catalog outages.
    /// </summary>
    private async Task<CardInfo?> TryGetCardForMutationAsync(
        string cardName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CardCatalog.GetCardAsync(cardName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the required card.
    /// </summary>
    private static DeckCard FindRequiredCard(
        DeckWorkspace workspace,
        string cardName,
        string? category
    )
    {
        return FindCard(workspace, cardName, category)
            ?? throw new InvalidOperationException(
                $"Card '{cardName}' was not found in workspace '{workspace.Id}'."
            );
    }
}
