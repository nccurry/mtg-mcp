using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Persists and applies deck edit plans.
/// </summary>
public sealed partial class DeckPlanService
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
    public async Task<DeckEditPlanDeleteResult> DeleteDeckPlanAsync(string planId, CancellationToken cancellationToken)
    {
        bool deleted = await RequirePlanRepository().DeleteAsync(planId, cancellationToken).ConfigureAwait(false);
        return new DeckEditPlanDeleteResult
        {
            PlanId = planId,
            Deleted = deleted
        };
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
        if (plan.Status != DeckEditPlanStatus.Draft)
        {
            if (plan.Status == DeckEditPlanStatus.Applied)
            {
                throw new InvalidOperationException($"Deck edit plan '{plan.PlanId}' has already been applied.");
            }

            throw new InvalidOperationException(
                $"Deck edit plan '{plan.PlanId}' cannot be applied because its status is '{plan.Status}'."
            );
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

        DeckEditPlanApplyAttemptResult attempt = await ApplyPlanOperationsAsync(
                plan,
                workspace,
                cancellationToken)
            .ConfigureAwait(false);

        return attempt switch
        {
            DeckEditPlanApplySuccess success => await CompleteSuccessfulApplyAsync(
                    plans,
                    plan,
                    checkpointId,
                    success,
                    cancellationToken)
                .ConfigureAwait(false),
            DeckEditPlanApplyFailure failure => await CompleteFailedApplyAsync(
                    plans,
                    plan,
                    checkpointId,
                    failure,
                    cancellationToken)
                .ConfigureAwait(false),
            null => throw new InvalidOperationException(
                "Unable to determine deck edit plan apply result."
            ),
        };
    }

    /// <summary>
    /// Applies plan operations and reports the closed set of successful or failed attempt states.
    /// </summary>
    private async Task<DeckEditPlanApplyAttemptResult> ApplyPlanOperationsAsync(
        DeckEditPlan plan,
        DeckWorkspace workspace,
        CancellationToken cancellationToken)
    {
        List<string> messages = [];
        int appliedOperations = 0;
        int attemptedOperations = 0;
        try
        {
            if (CanApplyAsCardBatch(plan.Operations))
            {
                DeckEditPlanBatchApplyResult batch = await ApplyCardOperationsInBatchAsync(
                        plan.WorkspaceId,
                        plan.Operations,
                        cancellationToken)
                    .ConfigureAwait(false);
                return new DeckEditPlanApplySuccess(
                    batch.AppliedOperations,
                    batch.AttemptedOperations,
                    batch.Messages
                );
            }

            for (int index = 0; index < plan.Operations.Count; index++)
            {
                DeckEditOperation operation = plan.Operations[index];
                attemptedOperations = index + 1;
                try
                {
                    DeckChangeResult? result = await ApplyOperationAsync(
                            plan.WorkspaceId,
                            operation,
                            cancellationToken)
                        .ConfigureAwait(false);
                    appliedOperations++;
                    if (result is not null)
                    {
                        messages.Add(result.Message);
                    }
                }
                catch (Exception exception) when (IsReportableApplyException(exception, cancellationToken))
                {
                    return new DeckEditPlanApplyFailure(
                        appliedOperations,
                        attemptedOperations,
                        new DeckEditPlanFailedOperation(index, operation),
                        exception,
                        ApplyStateUnknown: IsRemoteTimeout(workspace, exception),
                        messages
                    );
                }
            }
        }
        catch (DeckEditPlanOperationException exception)
        {
            return new DeckEditPlanApplyFailure(
                appliedOperations,
                exception.OperationIndex + 1,
                new DeckEditPlanFailedOperation(exception.OperationIndex, exception.Operation),
                exception.InnerException ?? exception,
                ApplyStateUnknown: false,
                messages
            );
        }
        catch (DeckEditPlanPersistenceException exception)
        {
            messages.AddRange(exception.Messages);
            return new DeckEditPlanApplyFailure(
                appliedOperations,
                exception.AttemptedOperations,
                FailedOperation: null,
                exception.InnerException ?? exception,
                ApplyStateUnknown: true,
                messages
            );
        }
        catch (Exception exception) when (IsReportableApplyException(exception, cancellationToken))
        {
            return new DeckEditPlanApplyFailure(
                appliedOperations,
                attemptedOperations,
                FailedOperation: null,
                exception,
                ApplyStateUnknown: IsRemoteTimeout(workspace, exception),
                messages
            );
        }

        return new DeckEditPlanApplySuccess(appliedOperations, attemptedOperations, messages);
    }

    /// <summary>
    /// Returns whether all operations can be applied in one card persistence pass.
    /// </summary>
    private static bool CanApplyAsCardBatch(IReadOnlyList<DeckEditOperation> operations)
    {
        if (operations.Count == 0)
        {
            return false;
        }

        foreach (DeckEditOperation operation in operations)
        {
            if (!operation.IsCardBatchOperation)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies card-only plans in memory and writes all card mutations in one repository or Archidekt call.
    /// </summary>
    private async Task<DeckEditPlanBatchApplyResult> ApplyCardOperationsInBatchAsync(
        string workspaceId,
        IReadOnlyList<DeckEditOperation> operations,
        CancellationToken cancellationToken)
    {
        DeckWorkspace persistedWorkspace = await LoadForMutationAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        int originalIncludedCount = CountCommanderIncludedCards(persistedWorkspace);
        DeckWorkspace workspace = CloneWorkspaceForBatchApply(persistedWorkspace);
        HashSet<DeckCard> upsertedCards = [];
        HashSet<DeckCard> removedCards = [];
        List<string> messages = [];

        for (int index = 0; index < operations.Count; index++)
        {
            DeckEditOperation operation = operations[index];
            try
            {
                string message = await ApplyCardOperationToWorkspaceAsync(
                        workspace,
                        operation,
                        upsertedCards,
                        removedCards,
                        cancellationToken)
                    .ConfigureAwait(false);
                messages.Add(message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new DeckEditPlanOperationException(index, operation, exception);
            }
        }

        EnsureCommanderIncludedBatchResultIsSafe(workspace, operations, originalIncludedCount);

        try
        {
            await PersistCardsAsync(
                    workspace,
                    upsertedCards.ToList(),
                    removedCards.ToList(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsReportableApplyException(exception, cancellationToken))
        {
            throw new DeckEditPlanPersistenceException(operations.Count, messages, exception);
        }

        return new DeckEditPlanBatchApplyResult
        {
            AppliedOperations = operations.Count,
            AttemptedOperations = operations.Count,
            Messages = messages
        };
    }

    /// <summary>
    /// Applies one card mutation to an already-loaded workspace.
    /// </summary>
    private async Task<string> ApplyCardOperationToWorkspaceAsync(
        DeckWorkspace workspace,
        DeckEditOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            DeckEditOperation.AddCardOperation add => await ApplyAddCardOperationToWorkspaceAsync(
                    workspace,
                    add,
                    upsertedCards,
                    removedCards,
                    cancellationToken)
                .ConfigureAwait(false),
            DeckEditOperation.RemoveCardOperation remove => ApplyRemoveCardOperationToWorkspace(
                workspace,
                remove,
                upsertedCards,
                removedCards),
            DeckEditOperation.SetCardQuantityOperation setQuantity => ApplySetQuantityOperationToWorkspace(
                workspace,
                setQuantity,
                upsertedCards,
                removedCards),
            DeckEditOperation.MoveCardOperation move => ApplyMoveCardOperationToWorkspace(
                workspace,
                move,
                upsertedCards,
                removedCards),
            DeckEditOperation.AddCardCategoryOperation addCategory => ApplyAddCardCategoryOperationToWorkspace(
                workspace,
                addCategory,
                upsertedCards,
                removedCards),
            DeckEditOperation.RemoveCardCategoryOperation removeCategory => ApplyRemoveCardCategoryOperationToWorkspace(
                workspace,
                removeCategory,
                upsertedCards,
                removedCards),
            DeckEditOperation.SetPrimaryCardCategoryOperation setPrimaryCategory => ApplySetPrimaryCategoryOperationToWorkspace(
                workspace,
                setPrimaryCategory,
                upsertedCards,
                removedCards),
            DeckEditOperation.CreateCategoryOperation => ThrowUnsupportedCardBatchOperation(operation),
            DeckEditOperation.RenameCategoryOperation => ThrowUnsupportedCardBatchOperation(operation),
            DeckEditOperation.DeleteCategoryOperation => ThrowUnsupportedCardBatchOperation(operation),
            DeckEditOperation.UpdateDeckMetadataOperation => ThrowUnsupportedCardBatchOperation(operation)
        };
    }

    /// <summary>
    /// Applies an add-card operation to a loaded workspace during a card batch.
    /// </summary>
    private async Task<string> ApplyAddCardOperationToWorkspaceAsync(
        DeckWorkspace workspace,
        DeckEditOperation.AddCardOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards,
        CancellationToken cancellationToken)
    {
        string cardName = Require(operation.CardName, "cardName");
        string category = NormalizeCategoryName(operation.Category ?? DeckDefaults.Mainboard);
        int amount = Math.Max(1, operation.Quantity ?? 1);
        EnsureCategory(workspace, category);

        DeckCard? existing = FindCard(workspace, cardName, category);
        DeckCard changed;
        if (existing is null)
        {
            changed = await CreateDeckCardForPlanAsync(cardName, amount, category, cancellationToken)
                .ConfigureAwait(false);
            workspace.Cards.Add(changed);
        }
        else
        {
            existing.Quantity += amount;
            changed = existing;
        }

        TrackChanged(upsertedCards, removedCards, changed);
        return $"Added {amount} {changed.Name} to {category}.";
    }

    /// <summary>
    /// Applies a remove-card operation to a loaded workspace during a card batch.
    /// </summary>
    private static string ApplyRemoveCardOperationToWorkspace(
        DeckWorkspace workspace,
        DeckEditOperation.RemoveCardOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards)
    {
        DeckCard card = FindRequiredPlanCard(
            workspace,
            Require(operation.CardName, "cardName"),
            operation.Category);
        int amount = Math.Max(1, operation.Quantity ?? 1);
        if (card.Quantity <= amount)
        {
            workspace.Cards.Remove(card);
            TrackRemoved(upsertedCards, removedCards, card);
        }
        else
        {
            card.Quantity -= amount;
            TrackChanged(upsertedCards, removedCards, card);
        }

        return $"Removed {amount} {card.Name}.";
    }

    /// <summary>
    /// Applies a set-quantity operation to a loaded workspace during a card batch.
    /// </summary>
    private static string ApplySetQuantityOperationToWorkspace(
        DeckWorkspace workspace,
        DeckEditOperation.SetCardQuantityOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards)
    {
        DeckCard card = FindRequiredPlanCard(
            workspace,
            Require(operation.CardName, "cardName"),
            operation.Category);
        int quantity = operation.Quantity ?? 1;
        if (quantity <= 0)
        {
            workspace.Cards.Remove(card);
            TrackRemoved(upsertedCards, removedCards, card);
        }
        else
        {
            card.Quantity = quantity;
            TrackChanged(upsertedCards, removedCards, card);
        }

        return $"Set {card.Name} quantity to {quantity}.";
    }

    /// <summary>
    /// Applies a move-card operation to a loaded workspace during a card batch.
    /// </summary>
    private static string ApplyMoveCardOperationToWorkspace(
        DeckWorkspace workspace,
        DeckEditOperation.MoveCardOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards)
    {
        DeckCard card = FindRequiredPlanCard(
            workspace,
            Require(operation.CardName, "cardName"),
            operation.FromCategory);
        string category = NormalizeCategoryName(Require(operation.ToCategory, "toCategory"));
        EnsureCategory(workspace, category);
        DeckCategoryOrdering.SetPrimary(card, category);
        TrackChanged(upsertedCards, removedCards, card);
        return $"Moved {card.Name} to {category}.";
    }

    /// <summary>
    /// Applies an add-card-category operation to a loaded workspace during a card batch.
    /// </summary>
    private static string ApplyAddCardCategoryOperationToWorkspace(
        DeckWorkspace workspace,
        DeckEditOperation.AddCardCategoryOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards)
    {
        DeckCard card = FindRequiredPlanCard(
            workspace,
            Require(operation.CardName, "cardName"),
            category: null);
        string category = NormalizeCategoryName(Require(operation.Category, "category"));
        EnsureCategory(workspace, category);
        DeckCategoryOrdering.AddSecondary(card, category);
        TrackChanged(upsertedCards, removedCards, card);
        return $"Added {category} to {card.Name}.";
    }

    /// <summary>
    /// Applies a remove-card-category operation to a loaded workspace during a card batch.
    /// </summary>
    private static string ApplyRemoveCardCategoryOperationToWorkspace(
        DeckWorkspace workspace,
        DeckEditOperation.RemoveCardCategoryOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards)
    {
        DeckCard card = FindRequiredPlanCard(
            workspace,
            Require(operation.CardName, "cardName"),
            category: null);
        string category = NormalizeCategoryName(Require(operation.Category, "category"));
        DeckCategoryOrdering.Remove(card, category);
        EnsureCategory(workspace, card.PrimaryCategory);
        TrackChanged(upsertedCards, removedCards, card);
        return $"Removed {category} from {card.Name}.";
    }

    /// <summary>
    /// Applies a set-primary-category operation to a loaded workspace during a card batch.
    /// </summary>
    private static string ApplySetPrimaryCategoryOperationToWorkspace(
        DeckWorkspace workspace,
        DeckEditOperation.SetPrimaryCardCategoryOperation operation,
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards)
    {
        DeckCard card = FindRequiredPlanCard(
            workspace,
            Require(operation.CardName, "cardName"),
            category: null);
        string category = NormalizeCategoryName(Require(operation.Category, "category"));
        EnsureCategory(workspace, category);
        DeckCategoryOrdering.SetPrimary(card, category);
        TrackChanged(upsertedCards, removedCards, card);
        return $"Set {card.Name} primary category to {category}.";
    }

    /// <summary>
    /// Throws a clear error if a non-card operation reaches the optimized batch path.
    /// </summary>
    private static string ThrowUnsupportedCardBatchOperation(DeckEditOperation operation)
    {
        throw new InvalidOperationException($"Operation '{operation.Operation}' cannot be applied as a card batch.");
    }

    /// <summary>
    /// Builds a workspace card for one batched add-card step.
    /// </summary>
    private async Task<DeckCard> CreateDeckCardForPlanAsync(
        string cardName,
        int quantity,
        string category,
        CancellationToken cancellationToken)
    {
        CardInfo? cardInfo = await TryGetCardForPlanMutationAsync(cardName, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = new()
        {
            Name = cardInfo?.Name ?? cardName.Trim(),
            Quantity = Math.Max(1, quantity),
            PrimaryCategory = category,
            Categories = [category],
            ScryfallId = cardInfo?.Id,
            ScryfallOracleId = cardInfo?.OracleId,
        };

        if (cardInfo is not null)
        {
            ApplyCardSnapshot(card, cardInfo);
        }

        DeckCategoryOrdering.Normalize(card, category);
        return card;
    }

    /// <summary>
    /// Resolves optional card facts for batched plan edits while allowing catalog outages.
    /// </summary>
    private async Task<CardInfo?> TryGetCardForPlanMutationAsync(
        string cardName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CardCatalog.GetCardAsync(cardName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds a card for a plan operation or reports the failing card and workspace clearly.
    /// </summary>
    private static DeckCard FindRequiredPlanCard(
        DeckWorkspace workspace,
        string cardName,
        string? category)
    {
        return FindCard(workspace, cardName, category)
            ?? throw new InvalidOperationException(
                $"Card '{cardName}' was not found in workspace '{workspace.Id}'."
            );
    }

    /// <summary>
    /// Tracks one changed card unless it has already been removed later in the batch.
    /// </summary>
    private static void TrackChanged(
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards,
        DeckCard card)
    {
        if (!removedCards.Contains(card))
        {
            upsertedCards.Add(card);
        }
    }

    /// <summary>
    /// Tracks one removed card and cancels any pending upsert for that same workspace row.
    /// </summary>
    private static void TrackRemoved(
        HashSet<DeckCard> upsertedCards,
        HashSet<DeckCard> removedCards,
        DeckCard card)
    {
        bool hadPendingUpsert = upsertedCards.Remove(card);
        if (!hadPendingUpsert
            || card.ArchidektDeckRelationId.HasValue
            || !string.IsNullOrWhiteSpace(card.ArchidektCardId))
        {
            removedCards.Add(card);
        }
    }

    /// <summary>
    /// Clones a workspace so failed batch validation cannot dirty cached state before persistence.
    /// </summary>
    private static DeckWorkspace CloneWorkspaceForBatchApply(DeckWorkspace workspace)
    {
        string json = JsonSerializer.Serialize(workspace);
        return JsonSerializer.Deserialize<DeckWorkspace>(json)
            ?? throw new InvalidOperationException("Unable to clone deck workspace for plan application.");
    }

    /// <summary>
    /// Counts included Commander cards, returning zero for non-Commander workspaces.
    /// </summary>
    private static int CountCommanderIncludedCards(DeckWorkspace workspace)
    {
        if (!workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return DeckCategoryInclusion.IncludedCards(workspace)
            .Sum(card => Math.Max(0, card.Quantity));
    }

    /// <summary>
    /// Refuses card batches that would leave a Commander deck newly over the size limit.
    /// </summary>
    private static void EnsureCommanderIncludedBatchResultIsSafe(
        DeckWorkspace workspace,
        IReadOnlyList<DeckEditOperation> operations,
        int originalIncludedCount)
    {
        if (!workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int finalIncludedCount = CountCommanderIncludedCards(workspace);
        if (finalIncludedCount <= 100 || finalIncludedCount <= originalIncludedCount)
        {
            return;
        }

        int operationIndex = FindCommanderSizeFailureOperationIndex(operations);
        throw new DeckEditPlanOperationException(
            operationIndex,
            operations[operationIndex],
            new InvalidOperationException(
                $"Applying this plan would leave this Commander deck with {finalIncludedCount} included cards, up from {originalIncludedCount}. Add to an excluded category such as Maybeboard, set the category to IncludedInDeck=false, or include enough cuts to keep the final deck at 100 cards."
            )
        );
    }

    /// <summary>
    /// Finds the edit most likely responsible for a final Commander deck-size overfill.
    /// </summary>
    private static int FindCommanderSizeFailureOperationIndex(IReadOnlyList<DeckEditOperation> operations)
    {
        for (int index = operations.Count - 1; index >= 0; index--)
        {
            if (operations[index].CanIncreaseCommanderIncludedCount)
            {
                return index;
            }
        }

        return Math.Max(0, operations.Count - 1);
    }

    /// <summary>
    /// Saves a successfully applied plan and returns the MCP-facing result.
    /// </summary>
    private async Task<DeckEditPlanApplyResult> CompleteSuccessfulApplyAsync(
        IDeckPlanRepository plans,
        DeckEditPlan plan,
        string? checkpointId,
        DeckEditPlanApplySuccess success,
        CancellationToken cancellationToken)
    {
        DeckWorkspace updatedWorkspace = await LoadWorkspaceAsync(plan.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        plan.Status = DeckEditPlanStatus.Applied;
        plan.AppliedAt = DateTimeOffset.UtcNow;
        plan.CheckpointId = checkpointId;
        await plans.SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        return new DeckEditPlanApplyResult
        {
            Success = true,
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            Persistence = DeckPersistence.For(updatedWorkspace),
            CheckpointId = checkpointId,
            Status = plan.Status,
            AppliedOperations = success.AppliedOperations,
            AttemptedOperations = success.AttemptedOperations,
            Messages = success.Messages,
            Workspace = updatedWorkspace
        };
    }

    /// <summary>
    /// Persists failed or partial plan state and returns structured MCP-safe failure details.
    /// </summary>
    private async Task<DeckEditPlanApplyResult> CompleteFailedApplyAsync(
        IDeckPlanRepository plans,
        DeckEditPlan plan,
        string? checkpointId,
        DeckEditPlanApplyFailure failure,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await TryLoadWorkspaceForFailureAsync(plan.WorkspaceId, cancellationToken)
            .ConfigureAwait(false);
        plan.Status = failure.ApplyStateUnknown
            ? DeckEditPlanStatus.ApplyStateUnknown
            : failure.AppliedOperations > 0
            ? DeckEditPlanStatus.PartiallyApplied
            : DeckEditPlanStatus.Failed;
        plan.AppliedAt = DateTimeOffset.UtcNow;
        plan.CheckpointId = checkpointId;
        string failureMessage = BuildFailureMessage(
            plan,
            failure.AttemptedOperations,
            failure.FailedOperation?.Index,
            failure.FailedOperation?.Operation,
            failure.Cause
        );
        plan.Warnings.Add(failureMessage);
        await plans.SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        return new DeckEditPlanApplyResult
        {
            Success = false,
            PlanId = plan.PlanId,
            WorkspaceId = plan.WorkspaceId,
            Persistence = DeckPersistence.For(workspace),
            CheckpointId = checkpointId,
            Status = plan.Status,
            AppliedOperations = failure.AppliedOperations,
            AttemptedOperations = failure.AttemptedOperations,
            FailedOperationIndex = failure.FailedOperation?.Index,
            FailedOperation = failure.FailedOperation?.Operation,
            Error = failureMessage,
            Messages = failure.Messages,
            Workspace = workspace
        };
    }

    /// <summary>
    /// Loads best-effort workspace state after a failed apply attempt.
    /// </summary>
    private async Task<DeckWorkspace> TryLoadWorkspaceForFailureAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        try
        {
            DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            if (workspace.Mode != WorkspaceMode.Archidekt
                || !workspace.WriteBack
                || string.IsNullOrWhiteSpace(workspace.ArchidektDeckId))
            {
                return workspace;
            }

            DeckWorkspace fresh = await RequireArchidektGateway()
                .ImportDeckAsync(workspace.ArchidektDeckId, writeBack: true, cancellationToken)
                .ConfigureAwait(false);
            fresh.Id = workspace.Id;
            fresh.WriteBack = true;
            foreach (DeckCard card in fresh.Cards)
            {
                DeckCategoryOrdering.Normalize(card);
            }

            return await Repository.SaveAsync(fresh, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsReportableApplyException(exception, cancellationToken))
        {
            return new DeckWorkspace { Id = workspaceId, Name = "Unavailable workspace" };
        }
    }

    /// <summary>
    /// Builds a concise failure message that identifies the operation boundary.
    /// </summary>
    private static string BuildFailureMessage(
        DeckEditPlan plan,
        int attemptedOperations,
        int? failedOperationIndex,
        DeckEditOperation? failedOperation,
        Exception exception)
    {
        string operationText = failedOperation is null || !failedOperationIndex.HasValue
            ? $"while confirming persistence after {attemptedOperations} attempted operation(s)"
            : $"at operation {failedOperationIndex.Value + 1}/{plan.Operations.Count} ({DescribeOperation(failedOperation.Value)})";
        return $"Failed to apply deck edit plan '{plan.PlanId}' {operationText}: {exception.GetType().Name}: {SecretRedactor.Redact(exception.Message)}";
    }

    /// <summary>
    /// Describes a plan operation without requiring clients to inspect the whole object.
    /// </summary>
    private static string DescribeOperation(DeckEditOperation operation)
    {
        string target =
            operation.CardName
            ?? operation.Category
            ?? operation.FromCategory
            ?? operation.ToCategory
            ?? operation.Name
            ?? "unnamed target";
        return $"{operation.Operation} {target}";
    }

    /// <summary>
    /// Reports recoverable apply failures while preserving caller-requested cancellation.
    /// </summary>
    private static bool IsReportableApplyException(Exception exception, CancellationToken cancellationToken)
    {
        return exception is not OperationCanceledException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
    }

    /// <summary>
    /// Identifies Archidekt write attempts where an HTTP timeout leaves remote state uncertain.
    /// </summary>
    private static bool IsRemoteTimeout(DeckWorkspace workspace, Exception exception)
    {
        return workspace.Mode == WorkspaceMode.Archidekt
            && workspace.WriteBack
            && exception is TaskCanceledException;
    }

    /// <summary>
    /// Applies one deck edit step.
    /// </summary>
    private async Task<DeckChangeResult?> ApplyOperationAsync(
        string workspaceId,
        DeckEditOperation operation,
        CancellationToken cancellationToken)
    {
        return operation switch
        {
            DeckEditOperation.AddCardOperation add => await workspaces.AddCardAsync(
                workspaceId,
                Require(add.CardName, "cardName"),
                add.Quantity ?? 1,
                add.Category ?? DeckDefaults.Mainboard,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.RemoveCardOperation remove => await workspaces.RemoveCardAsync(
                workspaceId,
                Require(remove.CardName, "cardName"),
                remove.Quantity ?? 1,
                remove.Category,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.SetCardQuantityOperation setQuantity => await workspaces.SetCardQuantityAsync(
                workspaceId,
                Require(setQuantity.CardName, "cardName"),
                setQuantity.Quantity ?? 1,
                setQuantity.Category,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.MoveCardOperation move => await workspaces.MoveCardAsync(
                workspaceId,
                Require(move.CardName, "cardName"),
                Require(move.ToCategory, "toCategory"),
                move.FromCategory,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.AddCardCategoryOperation addCategory => await workspaces.AddCardCategoryAsync(
                workspaceId,
                Require(addCategory.CardName, "cardName"),
                Require(addCategory.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.RemoveCardCategoryOperation removeCategory => await workspaces.RemoveCardCategoryAsync(
                workspaceId,
                Require(removeCategory.CardName, "cardName"),
                Require(removeCategory.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.SetPrimaryCardCategoryOperation setPrimaryCategory => await workspaces.SetPrimaryCardCategoryAsync(
                workspaceId,
                Require(setPrimaryCategory.CardName, "cardName"),
                Require(setPrimaryCategory.Category, "category"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.CreateCategoryOperation createCategory => await workspaces.CreateCategoryAsync(
                workspaceId,
                Require(createCategory.Category, "category"),
                createCategory.IncludedInDeck ?? !DeckDefaults.IsDefaultExcludedCategory(Require(createCategory.Category, "category")),
                createCategory.IncludedInPrice ?? !DeckDefaults.IsDefaultPriceExcludedCategory(Require(createCategory.Category, "category")),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.RenameCategoryOperation renameCategory => await workspaces.RenameCategoryAsync(
                workspaceId,
                Require(renameCategory.FromCategory, "fromCategory"),
                Require(renameCategory.ToCategory, "toCategory"),
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.DeleteCategoryOperation deleteCategory => await workspaces.DeleteCategoryAsync(
                workspaceId,
                Require(deleteCategory.Category, "category"),
                deleteCategory.ToCategory ?? DeckDefaults.Mainboard,
                cancellationToken).ConfigureAwait(false),
            DeckEditOperation.UpdateDeckMetadataOperation updateMetadata => await workspaces.UpdateDeckMetadataAsync(
                workspaceId,
                updateMetadata.Name,
                updateMetadata.Format,
                updateMetadata.Description,
                cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// Represents a successful or failed attempt to apply plan operations.
    /// </summary>
    private readonly union DeckEditPlanApplyAttemptResult(
        DeckEditPlanApplySuccess,
        DeckEditPlanApplyFailure
    );

    /// <summary>
    /// Carries operation counts and messages after all edits were applied.
    /// </summary>
    private sealed record DeckEditPlanApplySuccess(
        int AppliedOperations,
        int AttemptedOperations,
        List<string> Messages
    );

    /// <summary>
    /// Carries structured failure details before they are translated to the public result.
    /// </summary>
    private sealed record DeckEditPlanApplyFailure(
        int AppliedOperations,
        int AttemptedOperations,
        DeckEditPlanFailedOperation? FailedOperation,
        Exception Cause,
        bool ApplyStateUnknown,
        List<string> Messages
    );

    /// <summary>
    /// Keeps a failed operation index coupled to the operation payload.
    /// </summary>
    private sealed record DeckEditPlanFailedOperation(int Index, DeckEditOperation Operation);

    /// <summary>
    /// Captures the result of applying an in-memory card batch.
    /// </summary>
    private sealed class DeckEditPlanBatchApplyResult
    {
        /// <summary>
        /// Gets or sets how many operations were prepared and persisted.
        /// </summary>
        public int AppliedOperations { get; set; }

        /// <summary>
        /// Gets or sets how many operations were prepared before persistence completed.
        /// </summary>
        public int AttemptedOperations { get; set; }

        /// <summary>
        /// Gets or sets operation messages produced while mutating the workspace.
        /// </summary>
        public List<string> Messages { get; set; } = [];
    }

    /// <summary>
    /// Wraps a failure that happened after card operations were prepared for persistence.
    /// </summary>
    private sealed class DeckEditPlanPersistenceException : Exception
    {
        /// <summary>
        /// Creates an exception with operation-count context for structured MCP reporting.
        /// </summary>
        public DeckEditPlanPersistenceException(
            int attemptedOperations,
            IReadOnlyList<string> messages,
            Exception innerException)
            : base(innerException.Message, innerException)
        {
            AttemptedOperations = attemptedOperations;
            Messages = messages.ToList();
        }

        /// <summary>
        /// Gets how many operations had been prepared when persistence failed.
        /// </summary>
        public int AttemptedOperations { get; }

        /// <summary>
        /// Captures prepared card-change messages from the interrupted batch.
        /// </summary>
        public List<string> Messages { get; }
    }

    /// <summary>
    /// Wraps a failure that happened while preparing one concrete edit step.
    /// </summary>
    private sealed class DeckEditPlanOperationException : Exception
    {
        /// <summary>
        /// Creates an exception with operation context for structured MCP reporting.
        /// </summary>
        public DeckEditPlanOperationException(
            int operationIndex,
            DeckEditOperation operation,
            Exception innerException)
            : base(innerException.Message, innerException)
        {
            OperationIndex = operationIndex;
            Operation = operation;
        }

        /// <summary>
        /// Gets the zero-based operation index.
        /// </summary>
        public int OperationIndex { get; }

        /// <summary>
        /// Identifies the edit step that raised the exception.
        /// </summary>
        public DeckEditOperation Operation { get; }
    }
}
