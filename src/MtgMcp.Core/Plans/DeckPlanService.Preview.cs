namespace MtgMcp.Core;

/// <summary>
/// Previews generated deck edit plans without mutating local or remote state.
/// </summary>
public sealed partial class DeckPlanService : DeckServiceBase
{
    /// <summary>
    /// Previews a deck edit plan without mutating local or remote state.
    /// </summary>
    public async Task<DeckPlanPreviewResult> PreviewDeckPlanAsync(
        string planId,
        bool resolveAddedCards,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = await GetDeckPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        DeckPlanPreviewer previewer = new(CardCatalog);
        DeckWorkspace preview = previewer.CloneWorkspace(workspace);
        IReadOnlySet<string> gameChangers = await FetchGameChangerNamesAsync(cancellationToken).ConfigureAwait(false);
        List<string> warnings = [];

        foreach (DeckEditOperation operation in plan.Operations)
        {
            await previewer.ApplyOperationAsync(preview, operation, resolveAddedCards, warnings, cancellationToken)
                .ConfigureAwait(false);
        }

        return new DeckPlanPreviewResult
        {
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            ResolveAddedCards = resolveAddedCards,
            Before = BuildMetricSnapshot(workspace, gameChangers),
            After = BuildMetricSnapshot(preview, gameChangers),
            Warnings = warnings
        };
    }

}
