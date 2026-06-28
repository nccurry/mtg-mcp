namespace MtgMcp.Core;

/// <summary>
/// Delegates goal-driven card package behavior to the focused goal collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Creates a recommendation plan from a natural-language deckbuilding goal.
    /// </summary>
    public async Task<GoalPackagePlanResult> FindCardsForDeckGoalAsync(
        string workspaceId,
        string goal,
        int count,
        decimal maxPrice,
        string strategy,
        CancellationToken cancellationToken)
    {
        return await goalPackages
            .FindCardsForDeckGoalAsync(
                workspaceId,
                goal,
                count,
                maxPrice,
                strategy,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
