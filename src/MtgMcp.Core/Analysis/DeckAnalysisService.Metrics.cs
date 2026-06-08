namespace MtgMcp.Core;

/// <summary>
/// Exposes cost, bracket, mana-base, and consistency analysis workflows.
/// </summary>
public sealed partial class DeckAnalysisService : DeckServiceBase
{
    /// <summary>
    /// Analyzes deck cost from locally cached card snapshots.
    /// </summary>
    public async Task<DeckCostAnalysis> AnalyzeDeckCostAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        return await AnalyzeDeckCostAsync(workspaceId, maxBudget: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Analyzes deck cost from locally cached card snapshots with an optional budget ceiling.
    /// </summary>
    public async Task<DeckCostAnalysis> AnalyzeDeckCostAsync(
        string workspaceId,
        decimal? maxBudget,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return AnalyzeDeckCost(workspace, maxBudget);
    }

    /// <summary>
    /// Estimates the Commander bracket for a workspace.
    /// </summary>
    public async Task<CommanderBracketEstimate> EstimateCommanderBracketAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        IReadOnlySet<string> gameChangers = await FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        return EstimateCommanderBracket(workspace, gameChangers);
    }

    /// <summary>
    /// Analyzes the deck mana base.
    /// </summary>
    public async Task<ManaBaseAnalysis> AnalyzeManaBaseAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return AnalyzeManaBase(workspace);
    }

    /// <summary>
    /// Analyzes deck consistency.
    /// </summary>
    public async Task<DeckConsistencyAnalysis> AnalyzeDeckConsistencyAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return AnalyzeDeckConsistency(workspace);
    }

}
