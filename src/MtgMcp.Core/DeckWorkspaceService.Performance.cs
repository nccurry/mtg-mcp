namespace MtgMcp.Core;

/// <summary>
/// Coordinates performance analysis workflows.
/// </summary>
public sealed partial class DeckWorkspaceService
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
        return DeckPerformanceAnalyzer.Analyze(
            workspace,
            profile,
            simulations,
            maxTurn,
            seed,
            includeMulligans,
            cancellationToken);
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
        DeckEditPlan plan = await GetDeckPlanAsync(planId, cancellationToken)
            .ConfigureAwait(false);
        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckWorkspace preview = CloneWorkspace(workspace);
        List<string> warnings = [.. plan.Warnings];

        foreach (DeckEditOperation operation in plan.Operations)
        {
            await ApplyPreviewOperationAsync(
                    preview,
                    operation,
                    resolveAddedCards: true,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        DeckPerformanceAnalysis before = DeckPerformanceAnalyzer.Analyze(
            workspace,
            profile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);
        DeckPerformanceAnalysis after = DeckPerformanceAnalyzer.Analyze(
            preview,
            profile,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken);

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
