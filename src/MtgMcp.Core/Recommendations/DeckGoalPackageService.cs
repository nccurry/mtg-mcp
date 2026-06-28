namespace MtgMcp.Core;

/// <summary>
/// Builds previewable card-package plans from natural-language deck goals.
/// </summary>
public sealed class DeckGoalPackageService
{
    /// <summary>
    /// Loads local workspaces for goal-package planning.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Ranks deck-aware catalog query candidates and persists generated plans.
    /// </summary>
    private readonly DeckQueryService queries;

    /// <summary>
    /// Creates a goal-package collaborator with explicit workspace and query dependencies.
    /// </summary>
    public DeckGoalPackageService(
        IDeckWorkspaceRepository repository,
        DeckQueryService queries)
    {
        this.repository = repository;
        this.queries = queries;
    }

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
        DeckQueryRecommendationResult ranking = await queries
            .RankCardsForDeckQueriesAsync(
                workspace,
                goal,
                spec.Searches,
                count,
                maxPrice,
                spec.RequiredRoles,
                spec.RequiredTags,
                spec.ExcludedRoles,
                spec.ExcludedTags,
                cancellationToken)
            .ConfigureAwait(false);
        DeckEditPlan plan = await queries
            .SaveQueryPlanAsync(
                workspace,
                ranking,
                spec.Category,
                spec.Rationale,
                "Goal package plan",
                "goal-package",
                cancellationToken)
            .ConfigureAwait(false);

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

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
