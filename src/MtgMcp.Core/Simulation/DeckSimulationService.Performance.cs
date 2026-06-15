namespace MtgMcp.Core;

/// <summary>
/// Provides deterministic whole-deck performance analysis workflows.
/// </summary>
public sealed partial class DeckSimulationService : DeckServiceBase
{
    /// <summary>
    /// Runs deterministic Monte Carlo performance analysis for a workspace.
    /// </summary>
    public async Task<DeckPerformanceAnalysis> AnalyzeDeckPerformanceAsync(
        string workspaceId,
        string profile,
        int simulations,
        int maxTurn,
        int seed,
        bool includeMulligans,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return AnalyzeDeckPerformanceSnapshot(
            workspace,
            profile,
            simulations,
            maxTurn,
            seed,
            includeMulligans,
            cancellationToken);
    }

    /// <summary>
    /// Runs deterministic Monte Carlo performance analysis for an in-memory workspace snapshot.
    /// </summary>
    public DeckPerformanceAnalysis AnalyzeDeckPerformanceSnapshot(
        DeckWorkspace workspace,
        string profile,
        int simulations,
        int maxTurn,
        int seed,
        bool includeMulligans,
        CancellationToken cancellationToken)
    {
        return DeckPerformanceAnalyzer.Analyze(
            workspace,
            profile,
            simulations,
            maxTurn,
            seed,
            includeMulligans,
            cancellationToken,
            simulationProfiles);
    }

    /// <summary>
    /// Compares deterministic performance before and after a persisted deck edit plan.
    /// </summary>
    public async Task<DeckPerformanceComparison> ComparePlanPerformanceAsync(
        string planId,
        string profile,
        int simulations,
        int maxTurn,
        int seed,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = await RequirePlanRepository()
            .GetAsync(planId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Deck plan '{planId}' was not found.");
        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckPlanPreviewer previewer = new(CardCatalog);
        DeckWorkspace preview = previewer.CloneWorkspace(workspace);
        List<string> warnings = [.. plan.Warnings];

        await previewer.ApplyOperationsAsync(
                preview,
                plan.Operations,
                resolveAddedCards: true,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);

        DeckPerformanceAnalysis before = DeckPerformanceAnalyzer.Analyze(
            workspace,
            profile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken,
            simulationProfiles);
        DeckPerformanceAnalysis after = DeckPerformanceAnalyzer.Analyze(
            preview,
            profile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken,
            simulationProfiles);

        return new DeckPerformanceComparison
        {
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            Before = before,
            After = after,
            Deltas = DeckPerformanceComparisonBuilder.BuildDeltas(before, after),
            Warnings = warnings
                .Concat(before.Warnings.Select(warning => $"Before: {warning}"))
                .Concat(after.Warnings.Select(warning => $"After: {warning}"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }
}
