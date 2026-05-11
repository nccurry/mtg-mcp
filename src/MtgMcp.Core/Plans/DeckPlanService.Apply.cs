namespace MtgMcp.Core;

/// <summary>
/// Persists and applies deck edit plans.
/// </summary>
public sealed partial class DeckPlanService : DeckServiceBase
{
    /// <summary>
    /// Lists deck edit plans.
    /// </summary>
    public Task<IReadOnlyList<DeckEditPlan>> ListDeckPlansAsync(
        string? workspaceId,
        CancellationToken cancellationToken
    )
    {
        return RequirePlanRepository().ListAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Gets a deck edit plan.
    /// </summary>
    public async Task<DeckEditPlan> GetDeckPlanAsync(
        string planId,
        CancellationToken cancellationToken
    )
    {
        return await RequirePlanRepository().GetAsync(planId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Deck edit plan '{planId}' was not found.");
    }

    /// <summary>
    /// Deletes a deck edit plan.
    /// </summary>
    public Task DeleteDeckPlanAsync(string planId, CancellationToken cancellationToken)
    {
        return RequirePlanRepository().DeleteAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Applies a deck edit plan.
    /// </summary>
    public async Task<DeckEditPlanApplyResult> ApplyDeckPlanAsync(
        string planId,
        bool createCheckpoint,
        string? checkpointName,
        CancellationToken cancellationToken)
    {
        IDeckPlanRepository plans = RequirePlanRepository();
        DeckEditPlan plan = await GetDeckPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(plan.Status)
            && !plan.Status.Equals(DeckEditPlanStatus.Draft, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Deck edit plan '{plan.PlanId}' has already been applied.");
        }

        DeckWorkspace workspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        string? checkpointId = null;

        if (workspace.Mode == WorkspaceMode.Archidekt && workspace.WriteBack && plan.Operations.Count > 1)
        {
            if (!createCheckpoint)
            {
                throw new InvalidOperationException("Applying a multi-edit plan to an Archidekt writeback workspace requires a checkpoint.");
            }

            DeckCheckpoint checkpoint = await workspaces.CheckpointDeckAsync(
                workspace.Id,
                string.IsNullOrWhiteSpace(checkpointName) ? $"Before {plan.Name}" : checkpointName,
                $"Created before applying plan {plan.PlanId}.",
                cancellationToken).ConfigureAwait(false);
            checkpointId = checkpoint.Id;
        }

        List<string> messages = [];
        foreach (DeckEditOperation operation in plan.Operations)
        {
            DeckChangeResult? result = await ApplyOperationAsync(plan.WorkspaceId, operation, cancellationToken).ConfigureAwait(false);
            if (result is not null)
            {
                messages.Add(result.Message);
            }
        }

        DeckWorkspace updatedWorkspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken).ConfigureAwait(false);
        plan.Status = DeckEditPlanStatus.Applied;
        plan.AppliedAt = DateTimeOffset.UtcNow;
        plan.CheckpointId = checkpointId;
        await plans.SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        return new DeckEditPlanApplyResult
        {
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            Persistence = DeckPersistence.For(updatedWorkspace),
            CheckpointId = checkpointId,
            AppliedOperations = plan.Operations.Count,
            Messages = messages,
            Workspace = updatedWorkspace
        };
    }

    /// <summary>
    /// Applies one deck edit step.
    /// </summary>
    private async Task<DeckChangeResult?> ApplyOperationAsync(
        string workspaceId,
        DeckEditOperation operation,
        CancellationToken cancellationToken)
    {
        return operation.Operation switch
        {
            DeckEditOperations.AddCard => await workspaces.AddCardAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                operation.Quantity ?? 1,
                operation.Category ?? DeckDefaults.Mainboard,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.RemoveCard => await workspaces.RemoveCardAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                operation.Quantity ?? 1,
                operation.Category,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.SetCardQuantity => await workspaces.SetCardQuantityAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                operation.Quantity ?? 1,
                operation.Category,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.MoveCard => await workspaces.MoveCardAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.ToCategory, "toCategory"),
                operation.FromCategory,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.AddCardCategory => await workspaces.AddCardCategoryAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.RemoveCardCategory => await workspaces.RemoveCardCategoryAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.SetPrimaryCardCategory => await workspaces.SetPrimaryCardCategoryAsync(
                workspaceId,
                Require(operation.CardName, "cardName"),
                Require(operation.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.CreateCategory => await workspaces.CreateCategoryAsync(
                workspaceId,
                Require(operation.Category, "category"),
                operation.IncludedInDeck ?? true,
                operation.IncludedInPrice ?? true,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.RenameCategory => await workspaces.RenameCategoryAsync(
                workspaceId,
                Require(operation.FromCategory, "fromCategory"),
                Require(operation.ToCategory, "toCategory"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.DeleteCategory => await workspaces.DeleteCategoryAsync(
                workspaceId,
                Require(operation.Category, "category"),
                operation.ToCategory ?? DeckDefaults.Mainboard,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperations.UpdateDeckMetadata => await workspaces.UpdateDeckMetadataAsync(
                workspaceId,
                operation.Name,
                operation.Format,
                operation.Description,
                cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown deck edit operation '{operation.Operation}'.")
        };
    }
}
