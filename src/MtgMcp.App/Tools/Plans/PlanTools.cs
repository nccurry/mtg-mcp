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
    /// Guards plan deletion and apply operations.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates plan tools for the MCP surface.
    /// </summary>
    public PlanTools(DeckPlanService plans, OperationModeGuard operationMode)
    {
        this.plans = plans;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Creates a plan from exact card adds and removals supplied by the caller.
    /// </summary>
    [McpServerTool(Name = "create_deck_plan_from_explicit_changes", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a persisted non-mutating deck edit plan from caller-supplied add/remove operations. The MCP does not choose cards or cuts.")]
    public Task<DeckEditPlan> CreateDeckPlanFromExplicitChangesAsync(
        string workspaceId,
        string? name = null,
        string? rationale = null,
        ExplicitDeckPlanCardChange[]? addCards = null,
        ExplicitDeckPlanCardChange[]? removeCards = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("create_deck_plan_from_explicit_changes");
        return plans.CreateDeckPlanFromExplicitChangesAsync(
            workspaceId,
            name,
            rationale,
            addCards,
            removeCards,
            cancellationToken);
    }

    /// <summary>
    /// Previews a persisted plan without mutating local or remote state.
    /// </summary>
    [McpServerTool(Name = "preview_deck_plan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Preview a persisted deck edit plan without mutating local or Archidekt state. Returns before and after cost, validation, roles, mana, consistency, and bracket metrics.")]
    public Task<DeckPlanPreviewResult> PreviewDeckPlanAsync(
        string planId,
        bool resolveAddedCards = true,
        CancellationToken cancellationToken = default)
    {
        return plans.PreviewDeckPlanAsync(planId, resolveAddedCards, cancellationToken);
    }

    /// <summary>
    /// Lists saved deck edit plans.
    /// </summary>
    [McpServerTool(Name = "list_deck_plans", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
    [McpServerTool(Name = "get_deck_plan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get a persisted deck edit plan by plan id.")]
    public Task<DeckEditPlan> GetDeckPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        return plans.GetDeckPlanAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Deletes a saved deck edit plan.
    /// </summary>
    [McpServerTool(Name = "delete_deck_plan", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete a persisted deck edit plan. This does not change deck contents.")]
    public Task<DeckEditPlanDeleteResult> DeleteDeckPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("delete_deck_plan");
        return plans.DeleteDeckPlanAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Applies a saved deck edit plan.
    /// </summary>
    [McpServerTool(Name = "apply_deck_plan", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
    [Description("Apply a persisted deck edit plan. Archidekt writeback workspaces require or create a checkpoint before multi-card edits.")]
    public Task<DeckEditPlanApplyResult> ApplyDeckPlanAsync(
        string planId,
        bool createCheckpoint = true,
        string? checkpointName = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanMutate("apply_deck_plan");
        return plans.ApplyDeckPlanAsync(planId, createCheckpoint, checkpointName, cancellationToken);
    }
}
