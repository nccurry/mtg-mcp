namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Adds the card category.
    /// </summary>
    public async Task<DeckChangeResult> AddCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, category: null);
        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCategoryOrdering.AddSecondary(card, normalizedCategory);

        await PersistCardsAsync(workspace, [card], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CategoryChanged,
            $"Added {normalizedCategory} to {card.Name}."
        );
    }

    /// <summary>
    /// Removes the card category.
    /// </summary>
    public async Task<DeckChangeResult> RemoveCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, category: null);
        string normalizedCategory = NormalizeCategoryName(category);
        DeckCategoryOrdering.Remove(card, normalizedCategory);
        EnsureCategory(workspace, card.PrimaryCategory);

        await PersistCardsAsync(workspace, [card], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CategoryChanged,
            $"Removed {normalizedCategory} from {card.Name}."
        );
    }

    /// <summary>
    /// Sets the primary card category.
    /// </summary>
    public async Task<DeckChangeResult> SetPrimaryCardCategoryAsync(
        string workspaceId,
        string cardName,
        string category,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = FindRequiredCard(workspace, cardName, category: null);
        string normalizedCategory = NormalizeCategoryName(category);
        EnsureCategory(workspace, normalizedCategory);
        DeckCategoryOrdering.SetPrimary(card, normalizedCategory);

        await PersistCardsAsync(workspace, [card], [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CategoryChanged,
            $"Set {card.Name} primary category to {normalizedCategory}."
        );
    }

    /// <summary>
    /// Creates the category.
    /// </summary>
    public async Task<DeckChangeResult> CreateCategoryAsync(
        string workspaceId,
        string category,
        bool includedInDeck,
        bool includedInPrice,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string normalizedCategory = NormalizeCategoryName(category);
        DeckCategory deckCategory = EnsureCategory(workspace, normalizedCategory);
        deckCategory.IncludedInDeck = includedInDeck;
        deckCategory.IncludedInPrice = includedInPrice;

        await PersistCategoryAsync(workspace, deckCategory, cancellationToken)
            .ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CategoryChanged,
            $"Created category {normalizedCategory}."
        );
    }

    /// <summary>
    /// Renames the category.
    /// </summary>
    public async Task<DeckChangeResult> RenameCategoryAsync(
        string workspaceId,
        string oldName,
        string newName,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCategory category = FindRequiredCategory(workspace, oldName);
        string normalizedNewName = NormalizeCategoryName(newName);
        string previousName = category.Name;
        category.Name = normalizedNewName;

        foreach (DeckCard card in workspace.Cards)
        {
            DeckCategoryOrdering.Replace(card, previousName, normalizedNewName);
        }

        await PersistCategoryAsync(workspace, category, cancellationToken).ConfigureAwait(false);
        await PersistCardsAsync(workspace, workspace.Cards, [], cancellationToken)
            .ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CategoryChanged,
            $"Renamed {previousName} to {normalizedNewName}."
        );
    }

    /// <summary>
    /// Deletes the category.
    /// </summary>
    public async Task<DeckChangeResult> DeleteCategoryAsync(
        string workspaceId,
        string category,
        string replacementCategory,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCategory removedCategory = FindRequiredCategory(workspace, category);
        string replacement = NormalizeCategoryName(replacementCategory);
        EnsureCategory(workspace, replacement);

        workspace.Categories.Remove(removedCategory);
        foreach (DeckCard card in workspace.Cards)
        {
            bool wasPrimary = DeckCategoryOrdering.PrimaryCategory(card).Equals(
                removedCategory.Name,
                StringComparison.OrdinalIgnoreCase
            );
            bool removedFromCard =
                card.Categories.RemoveAll(value =>
                    value.Equals(removedCategory.Name, StringComparison.OrdinalIgnoreCase)
                ) > 0;

            if (wasPrimary)
            {
                DeckCategoryOrdering.SetPrimary(card, replacement);
            }
            else if (removedFromCard)
            {
                DeckCategoryOrdering.AddSecondary(card, replacement);
            }
        }

        await DeleteCategoryInAdapterAsync(workspace, removedCategory, cancellationToken)
            .ConfigureAwait(false);
        await PersistCardsAsync(workspace, workspace.Cards, [], cancellationToken)
            .ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CategoryChanged,
            $"Deleted {removedCategory.Name}."
        );
    }

    /// <summary>
    /// Finds the required category.
    /// </summary>
    private static DeckCategory FindRequiredCategory(DeckWorkspace workspace, string category)
    {
        return workspace.Categories.FirstOrDefault(value =>
                value.Name.Equals(category, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException(
                $"Category '{category}' was not found in workspace '{workspace.Id}'."
            );
    }

}
