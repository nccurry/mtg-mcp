namespace MtgMcp.Core;

/// <summary>
/// Delegates deck category cleanup planning to the focused category collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Suggests deck categories.
    /// </summary>
    public async Task<CategoryPlanResult> SuggestDeckCategoriesAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        return await categories
            .SuggestDeckCategoriesAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
    }
}
