namespace MtgMcp.Core;

/// <summary>
/// Provides unified deck brainstorming behavior.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
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
        CommanderMetaReport meta = await CompareToCommanderMetaAsync(workspaceId, limit: 15, cancellationToken).ConfigureAwait(false);
        NewCardsForDeckResult newCards = await FindNewCardsForDeckAsync(
            workspaceId,
            since: null,
            setCode: null,
            limit: 10,
            maxPrice: budget > 0 ? budget : null,
            cancellationToken).ConfigureAwait(false);
        GoalPackagePlanResult package = await FindCardsForDeckGoalAsync(
            workspaceId,
            string.IsNullOrWhiteSpace(goal) ? "improve weak roles" : goal,
            count: 3,
            maxPrice: budget > 0 ? budget : 10,
            strategy: targetPower,
            cancellationToken).ConfigureAwait(false);
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
            NewCards = newCards,
            GoalPackage = package,
            Combos = combos,
            Goldfish = goldfish
        };
        result.RankedRecommendations.AddRange(best.Recommendations.Take(5));
        result.RankedRecommendations.AddRange(package.Suggestions.Select(suggestion => $"Consider {suggestion.CardName}: {suggestion.Rationale}").Take(3));
        result.RankedRecommendations.AddRange(newCards.Suggestions.Select(suggestion => $"Review new card {suggestion.CardName}: {suggestion.Rationale}").Take(3));
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
