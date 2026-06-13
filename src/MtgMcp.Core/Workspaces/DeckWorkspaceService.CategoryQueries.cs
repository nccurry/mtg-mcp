namespace MtgMcp.Core;

/// <summary>
/// Coordinates deck workspace service behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
    /// <summary>
    /// Lists compact local card rows that belong to one category.
    /// </summary>
    public async Task<DeckCategoryCardListResult> ListCardsByCategoryAsync(
        string workspaceId,
        string category,
        bool includeSecondary,
        int limit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        string normalizedCategory = NormalizeCategoryName(category);
        int safeLimit = Math.Clamp(limit, 1, 500);
        Dictionary<string, DeckCategory> categoryMap = DeckCategoryInclusion.BuildCategoryMap(workspace);
        List<DeckCard> matches = [];
        foreach (DeckCard card in workspace.Cards)
        {
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            bool matched = primaryCategory.Equals(normalizedCategory, StringComparison.OrdinalIgnoreCase)
                || includeSecondary && DeckCategoryOrdering.HasCategory(card, normalizedCategory);
            if (matched)
            {
                matches.Add(card);
            }
        }

        matches.Sort((left, right) =>
        {
            int comparison = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return comparison != 0
                ? comparison
                : string.Compare(
                    DeckCategoryOrdering.PrimaryCategory(left),
                    DeckCategoryOrdering.PrimaryCategory(right),
                    StringComparison.OrdinalIgnoreCase);
        });

        DeckCategoryCardListResult result = new()
        {
            WorkspaceId = workspace.Id,
            Category = normalizedCategory,
            IncludeSecondary = includeSecondary,
            Count = matches.Count,
            TotalQuantity = matches.Sum(card => Math.Max(0, card.Quantity)),
        };

        foreach (DeckCard card in matches.Take(safeLimit))
        {
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            CardPriceEvaluation price = EvaluateUsdPrice(card.Snapshot);
            result.Cards.Add(new DeckCategoryCardListRow
            {
                CardName = card.Name,
                Quantity = card.Quantity,
                PrimaryCategory = primaryCategory,
                Categories = DeckCategoryOrdering.OrderedDistinct(primaryCategory, card.Categories),
                Role = role.PrimaryRole,
                Tags = role.Tags,
                ManaValue = card.Snapshot?.ManaValue,
                TypeLine = card.Snapshot?.TypeLine,
                Price = price.PriceKnown ? price.Price : null,
                ScryfallUri = card.Snapshot?.ScryfallUri,
                IncludedInDeck = DeckCategoryInclusion.IsIncludedInDeck(categoryMap, primaryCategory),
                IncludedInPrice = IsIncludedInPrice(categoryMap, primaryCategory),
            });
        }

        return result;
    }

    /// <summary>
    /// Checks whether a primary category contributes to price totals.
    /// </summary>
    private static bool IsIncludedInPrice(
        IReadOnlyDictionary<string, DeckCategory> categories,
        string primaryCategory)
    {
        return !categories.TryGetValue(primaryCategory, out DeckCategory? category)
            || category.IncludedInPrice;
    }
}
