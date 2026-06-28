namespace MtgMcp.Core;

/// <summary>
/// Orchestrates the unified deck brainstorming workflow from focused recommendation collaborators.
/// </summary>
public sealed class DeckBrainstormingService
{
    /// <summary>
    /// Supplies best-practice and combo analysis.
    /// </summary>
    private readonly DeckAnalysisService analysis;

    /// <summary>
    /// Supplies deterministic goldfish simulation.
    /// </summary>
    private readonly DeckSimulationService simulation;

    /// <summary>
    /// Supplies Commander meta comparison.
    /// </summary>
    private readonly DeckCommanderMetaService commanderMeta;

    /// <summary>
    /// Supplies recent-card radar.
    /// </summary>
    private readonly DeckNewCardService newCards;

    /// <summary>
    /// Persists previewable goal-package plans.
    /// </summary>
    private readonly DeckGoalPackageService goalPackages;

    /// <summary>
    /// Creates a brainstorming collaborator from focused analysis, simulation, and recommendation services.
    /// </summary>
    public DeckBrainstormingService(
        DeckAnalysisService analysis,
        DeckSimulationService simulation,
        DeckCommanderMetaService commanderMeta,
        DeckNewCardService newCards,
        DeckGoalPackageService goalPackages)
    {
        this.analysis = analysis;
        this.simulation = simulation;
        this.commanderMeta = commanderMeta;
        this.newCards = newCards;
        this.goalPackages = goalPackages;
    }

    /// <summary>
    /// Runs the unified brewing workflow.
    /// </summary>
    public async Task<BrainstormDeckImprovementsResult> BrainstormDeckImprovementsAsync(
        string workspaceId,
        string goal,
        decimal budget,
        string targetPower,
        CancellationToken cancellationToken)
    {
        DeckBestPracticeAnalysis best = await analysis.AnalyzeDeckBestPracticesAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        CommanderMetaReport meta = await commanderMeta
            .CompareToCommanderMetaAsync(workspaceId, limit: 15, cancellationToken)
            .ConfigureAwait(false);
        NewCardsForDeckResult newCardReport = await newCards
            .FindNewCardsForDeckAsync(
                workspaceId,
                since: null,
                setCode: null,
                limit: 10,
                maxPrice: budget > 0 ? budget : null,
                cancellationToken)
            .ConfigureAwait(false);
        GoalPackagePlanResult package = await goalPackages
            .FindCardsForDeckGoalAsync(
                workspaceId,
                string.IsNullOrWhiteSpace(goal) ? "improve weak roles" : goal,
                count: 3,
                maxPrice: budget > 0 ? budget : 10,
                strategy: targetPower,
                cancellationToken)
            .ConfigureAwait(false);
        DeckComboReport completedCombos = await analysis.FindDeckCombosAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckComboReport nearMissCombos = await analysis.FindNearMissCombosAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckComboReport combos = new()
        {
            WorkspaceId = workspaceId,
            Combos = completedCombos.Combos,
            NearMisses = nearMissCombos.NearMisses,
            Pressure = completedCombos.Pressure,
            Notes = completedCombos.Notes.Concat(nearMissCombos.Notes).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        GoldfishSimulationResult goldfish = await simulation.SimulateGoldfishAsync(
            workspaceId,
            targetTurn: 7,
            simulations: 500,
            seed: 1337,
            mulligan: true,
            cancellationToken).ConfigureAwait(false);

        BrainstormDeckImprovementsResult result = new()
        {
            WorkspaceId = workspaceId,
            BestPractices = best,
            Meta = meta,
            NewCards = newCardReport,
            GoalPackage = package,
            Combos = combos,
            Goldfish = goldfish
        };
        result.RankedRecommendations.AddRange(best.Recommendations.Take(5));
        result.RankedRecommendations.AddRange(package.Suggestions.Select(suggestion => $"Consider {suggestion.CardName}: {suggestion.Rationale}").Take(3));
        result.RankedRecommendations.AddRange(newCardReport.Suggestions.Select(suggestion => $"Review new card {suggestion.CardName}: {suggestion.Rationale}").Take(3));
        result.RankedRecommendations.AddRange(combos.NearMisses.Select(combo => $"Combo near miss: {combo.Name} needs {string.Join(", ", combo.MissingCards)}.").Take(2));
        result.RankedRecommendations = result.RankedRecommendations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        result.Notes.Add($"Created previewable goal-package plan {package.Plan.PlanId}; inspect it with deck_plan_get or deck_plan_preview before applying.");
        result.Notes.Add("Brainstorming uses previewable deck plans; only deck_plan_apply mutates deck contents.");
        return result;
    }
}
