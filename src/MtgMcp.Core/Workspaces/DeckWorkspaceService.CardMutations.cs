namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Stores the Commander deck size limit enforced before included-card additions.
    /// </summary>
    private const int CommanderDeckSizeLimit = 100;

    /// <summary>
    /// Adds a card to a workspace while refusing accidental Commander overfills.
    /// </summary>
    public Task<DeckChangeResult> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken
    )
    {
        return AddCardAsync(
            workspaceId,
            cardName,
            quantity,
            category,
            force: false,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds a card to a workspace and optionally permits an intentional Commander overfill.
    /// </summary>
    public async Task<DeckChangeResult> AddCardAsync(
        string workspaceId,
        string cardName,
        int quantity,
        string category,
        bool force,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string normalizedCategory = NormalizeCategoryName(category);
        int amount = Math.Max(1, quantity);
        EnsureCategory(workspace, normalizedCategory);
        EnsureCommanderIncludedAdditionIsSafe(workspace, normalizedCategory, amount, force);
        DeckCard? existing = FindCard(workspace, cardName, normalizedCategory);
        DeckCard changed;

        if (existing is null)
        {
            changed = await CreateDeckCardAsync(
                    cardName,
                    amount,
                    normalizedCategory,
                    cancellationToken
                )
                .ConfigureAwait(false);
            workspace.Cards.Add(changed);
        }
        else
        {
            existing.Quantity += amount;
            changed = existing;
        }

        await PersistCardsAsync(workspace, [changed], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CardAdded,
            $"Added {amount} {changed.Name} to {normalizedCategory}."
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
        string requestedName = cardName.Trim();
        string displayName = BasicLandIdentity.TryGetCanonicalName(requestedName, out string canonicalName)
            ? canonicalName
            : cardInfo?.Name ?? requestedName;
        DeckCard card = new()
        {
            Name = displayName,
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
        catch (Exception exception) when (
            exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
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

    /// <summary>
    /// Refuses included Commander additions that would unexpectedly exceed the deck size limit.
    /// </summary>
    private static void EnsureCommanderIncludedAdditionIsSafe(
        DeckWorkspace workspace,
        string category,
        int quantity,
        bool force)
    {
        if (force || !workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dictionary<string, DeckCategory> categoryMap = DeckCategoryInclusion.BuildCategoryMap(workspace);
        if (!DeckCategoryInclusion.IsIncludedInDeck(categoryMap, category))
        {
            return;
        }

        int includedCount = DeckCategoryInclusion.IncludedCards(workspace)
            .Sum(card => Math.Max(0, card.Quantity));
        int projectedCount = includedCount + Math.Max(1, quantity);
        if (projectedCount <= CommanderDeckSizeLimit)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Adding {Math.Max(1, quantity)} card(s) to included category '{category}' would raise this Commander deck from {includedCount} to {projectedCount} included cards. Add to an excluded category such as Maybeboard, set the category to IncludedInDeck=false, or retry with force=true if this overfill is intentional."
        );
    }
}
