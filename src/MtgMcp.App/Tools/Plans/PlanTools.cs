using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes deck edit plan preview, lookup, deletion, and apply MCP tools.
/// </summary>
[McpServerToolType]
public sealed class PlanTools
{
    /// <summary>
    /// Persists, previews, and applies deck edit plans.
    /// </summary>
    private readonly DeckPlanService plans;

    /// <summary>
    /// Provides workspace state for compact plan-apply output.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Guards plan deletion and apply operations.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates plan tools for the MCP surface.
    /// </summary>
    public PlanTools(DeckPlanService plans, DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.plans = plans;
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Creates a plan from exact card adds and removals supplied by the caller.
    /// </summary>
    [McpServerTool(Name = "deck_plan_create", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a persisted non-mutating deck edit plan from caller-supplied add/remove/move operations. The MCP does not choose cards or cuts.")]
    public Task<DeckEditPlan> CreateDeckPlanFromExplicitChangesAsync(
        string workspaceId,
        string? name = null,
        string? rationale = null,
        ExplicitDeckPlanCardChange[]? addCards = null,
        ExplicitDeckPlanCardChange[]? removeCards = null,
        ExplicitDeckPlanMoveCardChange[]? moveCards = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("deck_plan_create");
        return plans.CreateDeckPlanFromExplicitChangesAsync(
            workspaceId,
            name,
            rationale,
            addCards,
            removeCards,
            moveCards,
            cancellationToken);
    }

    /// <summary>
    /// Previews a persisted plan without mutating local or remote state.
    /// </summary>
    [McpServerTool(Name = "deck_plan_preview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Preview a persisted deck edit plan without mutating local or Archidekt state. Returns before and after cost, validation, roles, mana, consistency, and bracket metrics.")]
    public Task<DeckPlanPreviewResult> PreviewDeckPlanAsync(
        string planId,
        bool resolveAddedCards = true,
        CancellationToken cancellationToken = default)
    {
        return plans.PreviewDeckPlanAsync(planId, resolveAddedCards, cancellationToken);
    }

    /// <summary>
    /// Previews caller-supplied card package operations without persisting a plan.
    /// </summary>
    [McpServerTool(Name = "deck_preview_card_package", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Preview caller-supplied add/remove/move card packages through the same plan preview and performance comparison code without saving a plan.")]
    public Task<DeckCardPackagePreviewResult> PreviewCardPackageAsync(
        string workspaceId,
        string? name = null,
        string? rationale = null,
        ExplicitDeckPlanCardChange[]? addCards = null,
        ExplicitDeckPlanCardChange[]? removeCards = null,
        ExplicitDeckPlanMoveCardChange[]? moveCards = null,
        bool resolveAddedCards = true,
        [Description("Simulation profile: auto, neutral, aggro, combo, control, value, big-mana, or stax.")]
        string simulationProfile = SimulationProfileIds.Auto,
        int simulations = 500,
        int maxTurn = 6,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return plans.PreviewCardPackageAsync(
            workspaceId,
            name,
            rationale,
            addCards,
            removeCards,
            moveCards,
            resolveAddedCards,
            simulationProfile,
            simulations,
            maxTurn,
            seed,
            cancellationToken);
    }

    /// <summary>
    /// Lists saved deck edit plans.
    /// </summary>
    [McpServerTool(Name = "deck_plan_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List persisted deck edit plans, optionally filtered by workspace id.")]
    public Task<IReadOnlyList<DeckEditPlan>> ListDeckPlansAsync(
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        return plans.ListDeckPlansAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Gets one saved deck edit plan.
    /// </summary>
    [McpServerTool(Name = "deck_plan_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get a persisted deck edit plan by plan id.")]
    public Task<DeckEditPlan> GetDeckPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        return plans.GetDeckPlanAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Clones a saved deck edit plan into a compatible workspace.
    /// </summary>
    [McpServerTool(Name = "deck_plan_clone", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Clone a deck edit plan into a compatible workspace after validating source reference, commander, format, categories, and card identity.")]
    public Task<DeckEditPlan> CloneDeckPlanAsync(
        string planId,
        string targetWorkspaceId,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("deck_plan_clone");
        return plans.CloneDeckPlanAsync(planId, targetWorkspaceId, cancellationToken);
    }

    /// <summary>
    /// Deletes a saved deck edit plan.
    /// </summary>
    [McpServerTool(Name = "deck_plan_delete", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete a persisted deck edit plan. This does not change deck contents.")]
    public Task<DeckEditPlanDeleteResult> DeleteDeckPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("deck_plan_delete");
        return plans.DeleteDeckPlanAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Applies a saved deck edit plan.
    /// </summary>
    [McpServerTool(Name = "deck_plan_apply", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Apply a persisted deck edit plan, returning structured success or failed-operation details. Archidekt writeback workspaces require or create a checkpoint before multi-card edits.")]
    public async Task<object> ApplyDeckPlanAsync(
        string planId,
        bool createCheckpoint = true,
        string? checkpointName = null,
        bool includeWorkspace = true,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("deck_plan_apply");
        if (includeWorkspace)
        {
            return await plans.ApplyDeckPlanAsync(planId, createCheckpoint, checkpointName, cancellationToken)
                .ConfigureAwait(false);
        }

        DeckEditPlan plan = await plans.GetDeckPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        DeckWorkspaceState before = await decks.GetWorkspaceStateAsync(plan.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckEditPlanApplyResult result = await plans.ApplyDeckPlanAsync(
                planId,
                createCheckpoint,
                checkpointName,
                cancellationToken)
            .ConfigureAwait(false);
        DeckWorkspaceState after = await decks.GetWorkspaceStateAsync(plan.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        return CompactMutationPresenter.FromPlanApply(before, after, result, plan);
    }
}
