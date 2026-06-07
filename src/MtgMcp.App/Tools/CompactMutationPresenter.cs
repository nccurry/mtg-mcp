using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes compact mutation output for MCP tools when callers do not need a full workspace snapshot.
/// </summary>
internal static class CompactMutationPresenter
{
    /// <summary>
    /// Runs a workspace mutation and returns either the original full result or a compact diff.
    /// </summary>
    public static async Task<object> RunMutationAsync(
        DeckWorkspaceService decks,
        string workspaceId,
        bool includeWorkspace,
        Func<Task<DeckChangeResult>> mutation,
        int added,
        int removed,
        int moved,
        IEnumerable<string> changedCards,
        CancellationToken cancellationToken)
    {
        if (includeWorkspace)
        {
            return await mutation().ConfigureAwait(false);
        }

        DeckWorkspaceState before = await decks.GetWorkspaceStateAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckChangeResult result = await mutation().ConfigureAwait(false);
        DeckWorkspaceState after = await decks.GetWorkspaceStateAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return FromStates(
            before,
            after,
            result.WorkspaceId,
            result.Persistence,
            result.Message,
            added,
            removed,
            moved,
            changedCards);
    }

    /// <summary>
    /// Builds a compact diff for a plan apply result.
    /// </summary>
    public static CompactMutationResult FromPlanApply(
        DeckWorkspaceState before,
        DeckWorkspaceState after,
        DeckEditPlanApplyResult result,
        DeckEditPlan plan)
    {
        (int added, int removed, int moved) = CountPlanOperations(plan.Operations);
        CompactMutationResult compact = FromStates(
            before,
            after,
            result.WorkspaceId,
            result.Persistence,
            result.Success ? "Applied deck edit plan." : result.Error ?? "Deck edit plan apply failed.",
            added,
            removed,
            moved,
            ChangedCards(plan.Operations));
        compact.Success = result.Success;
        compact.PlanId = result.PlanId;
        compact.Status = result.Status;
        compact.CheckpointId = result.CheckpointId;
        compact.Notes.AddRange(result.Messages);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            compact.Notes.Add(result.Error);
        }

        return compact;
    }

    /// <summary>
    /// Builds a compact mutation result from before and after workspace state.
    /// </summary>
    private static CompactMutationResult FromStates(
        DeckWorkspaceState before,
        DeckWorkspaceState after,
        string workspaceId,
        string persistence,
        string message,
        int added,
        int removed,
        int moved,
        IEnumerable<string> changedCards)
    {
        return new CompactMutationResult
        {
            WorkspaceId = workspaceId,
            WorkspaceResourceUri = $"mtg://workspace/{workspaceId}",
            Persistence = persistence,
            Message = message,
            Added = Math.Max(0, added),
            Removed = Math.Max(0, removed),
            Moved = Math.Max(0, moved),
            ChangedCards = changedCards
                .Where(card => !string.IsNullOrWhiteSpace(card))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            IncludedCountBefore = before.IncludedCount,
            IncludedCountAfter = after.IncludedCount,
            CategoryCountsBefore = new Dictionary<string, int>(before.CategoryCounts, StringComparer.OrdinalIgnoreCase),
            CategoryCountsAfter = new Dictionary<string, int>(after.CategoryCounts, StringComparer.OrdinalIgnoreCase),
            Validation = after.Validation
        };
    }

    /// <summary>
    /// Counts card add, remove, and move operations in a plan.
    /// </summary>
    private static (int Added, int Removed, int Moved) CountPlanOperations(IEnumerable<DeckEditOperation> operations)
    {
        int added = 0;
        int removed = 0;
        int moved = 0;
        foreach (DeckEditOperation operation in operations)
        {
            int quantity = Math.Max(1, operation.Quantity ?? 1);
            switch (operation.Operation)
            {
                case DeckEditOperations.AddCard:
                    added += quantity;
                    break;
                case DeckEditOperations.RemoveCard:
                    removed += quantity;
                    break;
                case DeckEditOperations.MoveCard:
                case DeckEditOperations.SetPrimaryCardCategory:
                    moved++;
                    break;
            }
        }

        return (added, removed, moved);
    }

    /// <summary>
    /// Gets changed card names from card-facing plan operations.
    /// </summary>
    private static IEnumerable<string> ChangedCards(IEnumerable<DeckEditOperation> operations)
    {
        foreach (DeckEditOperation operation in operations)
        {
            if (!string.IsNullOrWhiteSpace(operation.CardName))
            {
                yield return operation.CardName;
            }

            if (!string.IsNullOrWhiteSpace(operation.ReplacementCardName))
            {
                yield return operation.ReplacementCardName;
            }
        }
    }
}
