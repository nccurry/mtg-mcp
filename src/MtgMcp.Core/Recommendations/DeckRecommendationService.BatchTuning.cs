namespace MtgMcp.Core;

/// <summary>
/// Delegates batch read-only tuning reports to the focused batch tuning collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Builds a read-only tuning report for one to eight workspaces.
    /// </summary>
    public async Task<DeckBatchTuningReport> BuildBatchTuningReportAsync(
        IReadOnlyList<string> workspaceIds,
        decimal? maxBudget,
        int targetTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        return await batchTuning
            .BuildBatchTuningReportAsync(
                workspaceIds,
                maxBudget,
                targetTurn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a read-only tuning report with a caller-selected goldfish simulation profile.
    /// </summary>
    public async Task<DeckBatchTuningReport> BuildBatchTuningReportAsync(
        IReadOnlyList<string> workspaceIds,
        decimal? maxBudget,
        string simulationProfile,
        int targetTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        return await batchTuning
            .BuildBatchTuningReportAsync(
                workspaceIds,
                maxBudget,
                simulationProfile,
                targetTurn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
