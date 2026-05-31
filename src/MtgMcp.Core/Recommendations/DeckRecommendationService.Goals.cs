namespace MtgMcp.Core;

/// <summary>
/// Provides goal-driven card package behavior.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
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
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckGoalSpec spec = DeckGoalSpecCatalog.Build(goal, workspace.Format, maxPrice, strategy);
        DeckQueryRecommendationResult ranking = await RankCardsForDeckQueriesAsync(
            workspace,
            goal,
            spec.Searches,
            count,
            maxPrice,
            spec.RequiredRoles,
            spec.RequiredTags,
            spec.ExcludedRoles,
            spec.ExcludedTags,
            cancellationToken).ConfigureAwait(false);
        DeckEditPlan plan = await SaveQueryPlanAsync(
            workspace,
            ranking,
            spec.Category,
            spec.Rationale,
            "Goal package plan",
            "goal-package",
            cancellationToken).ConfigureAwait(false);

        return new GoalPackagePlanResult
        {
            Plan = plan,
            Goal = goal,
            Strategy = spec.Strategy,
            Suggestions = ranking.Candidates.Select(candidate => new GoalCardSuggestion
            {
                CardName = candidate.CardName,
                Role = candidate.Role,
                Tags = candidate.Tags,
                FitScore = candidate.Score,
                Price = candidate.Price,
                ScryfallUri = candidate.ScryfallUri,
                Rationale = candidate.Rationale
            }).ToList()
        };
    }
}
