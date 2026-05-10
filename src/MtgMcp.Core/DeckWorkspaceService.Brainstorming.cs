namespace MtgMcp.Core;

/// <summary>
/// Provides unified deck brainstorming behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
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
        DeckBestPracticeAnalysis best = await AnalyzeDeckBestPracticesAsync(workspaceId, cancellationToken).ConfigureAwait(false);
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
        DeckComboReport combos = await BuildComboReportAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        GoldfishSimulationResult goldfish = await SimulateGoldfishAsync(
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
        result.Notes.Add($"Created previewable goal-package plan {package.Plan.PlanId}; inspect it with get_deck_plan or preview_deck_plan before applying.");
        result.Notes.Add("Brainstorming uses previewable deck plans; only apply_deck_plan mutates deck contents.");
        return result;
    }
}
