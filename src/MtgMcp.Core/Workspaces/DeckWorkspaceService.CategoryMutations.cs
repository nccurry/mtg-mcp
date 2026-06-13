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
    /// Applies multiple card-category changes after validating the full batch.
    /// </summary>
    public async Task<DeckChangeResult> UpdateCardCategoriesBulkAsync(
        string workspaceId,
        IReadOnlyList<BulkCardCategoryChange> changes,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            throw new InvalidOperationException("At least one category change is required.");
        }

        DeckWorkspace workspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        List<ValidatedBulkCardCategoryChange> validatedChanges = [];
        for (int index = 0; index < changes.Count; index++)
        {
            BulkCardCategoryChange change = changes[index];
            if (string.IsNullOrWhiteSpace(change.CardName))
            {
                throw new InvalidOperationException($"Bulk category change at index {index} is missing a card name.");
            }

            if (string.IsNullOrWhiteSpace(change.Category))
            {
                throw new InvalidOperationException($"Bulk category change at index {index} is missing a category.");
            }

            DeckCard card = FindRequiredCard(workspace, change.CardName.Trim(), category: null);
            validatedChanges.Add(new ValidatedBulkCardCategoryChange(
                card,
                NormalizeBulkCategoryAction(change.Action, index),
                NormalizeCategoryName(change.Category)));
        }

        foreach (ValidatedBulkCardCategoryChange change in validatedChanges)
        {
            if (!change.Action.Equals(BulkCardCategoryActions.Remove, StringComparison.OrdinalIgnoreCase))
            {
                EnsureCategory(workspace, change.Category);
            }
        }

        List<DeckCard> changedCards = [];
        foreach (ValidatedBulkCardCategoryChange change in validatedChanges)
        {
            if (change.Action.Equals(BulkCardCategoryActions.AddSecondary, StringComparison.OrdinalIgnoreCase))
            {
                DeckCategoryOrdering.AddSecondary(change.Card, change.Category);
            }
            else if (change.Action.Equals(BulkCardCategoryActions.Remove, StringComparison.OrdinalIgnoreCase))
            {
                DeckCategoryOrdering.Remove(change.Card, change.Category);
                EnsureCategory(workspace, change.Card.PrimaryCategory);
            }
            else
            {
                DeckCategoryOrdering.SetPrimary(change.Card, change.Category);
            }

            if (!changedCards.Contains(change.Card))
            {
                changedCards.Add(change.Card);
            }
        }

        await PersistCardsAsync(workspace, changedCards, [], cancellationToken).ConfigureAwait(false);
        return Change(
            workspace,
            DeckMutationKind.CategoryChanged,
            $"Updated categories for {changedCards.Count} card(s) across {validatedChanges.Count} bulk request(s).");
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

    /// <summary>
    /// Normalizes a supported bulk card-category action.
    /// </summary>
    private static string NormalizeBulkCategoryAction(string? action, int index)
    {
        string normalized = string.IsNullOrWhiteSpace(action)
            ? BulkCardCategoryActions.AddSecondary
            : action.Trim().ToLowerInvariant();
        return normalized switch
        {
            BulkCardCategoryActions.AddSecondary or "add" or "add-category" => BulkCardCategoryActions.AddSecondary,
            BulkCardCategoryActions.Remove or "remove-category" => BulkCardCategoryActions.Remove,
            BulkCardCategoryActions.SetPrimary or "primary" or "set" => BulkCardCategoryActions.SetPrimary,
            _ => throw new InvalidOperationException(
                $"Bulk category change at index {index} has unsupported action '{action}'. Use add-secondary, remove, or set-primary.")
        };
    }

    /// <summary>
    /// Stores a validated bulk category update.
    /// </summary>
    private sealed record ValidatedBulkCardCategoryChange(
        DeckCard Card,
        string Action,
        string Category);
}
