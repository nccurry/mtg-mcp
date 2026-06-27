namespace MtgMcp.Core;

/// <summary>
/// Creates deck edit plans from explicit caller-provided operations.
/// </summary>
public sealed partial class DeckPlanService
{
    /// <summary>
    /// Persists a non-mutating plan whose card choices and cuts are supplied by the caller.
    /// </summary>
    public async Task<DeckEditPlan> CreateDeckPlanFromExplicitChangesAsync(
        string workspaceId,
        string? name,
        string? rationale,
        IReadOnlyList<ExplicitDeckPlanCardChange>? addCards,
        IReadOnlyList<ExplicitDeckPlanCardChange>? removeCards,
        IReadOnlyList<ExplicitDeckPlanMoveCardChange>? moveCards,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckEditPlan plan = CreatePlan(
            workspace,
            string.IsNullOrWhiteSpace(name) ? "Explicit deck edit plan" : name.Trim(),
            "explicit-changes");
        plan.Rationale = rationale?.Trim() ?? "";
        plan.Confidence = 1;

        AddCardOperations(plan, addCards);
        AddRemoveOperations(plan, removeCards);
        AddMoveOperations(plan, moveCards);
        if (plan.Operations.Count == 0)
        {
            throw new InvalidOperationException("At least one explicit card add, remove, or move is required.");
        }

        return await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds caller-selected add-card operations to a plan.
    /// </summary>
    private static void AddCardOperations(
        DeckEditPlan plan,
        IReadOnlyList<ExplicitDeckPlanCardChange>? changes)
    {
        foreach (ExplicitDeckPlanCardChange change in changes ?? [])
        {
            plan.Operations.Add(DeckEditOperation.AddCard(
                RequireChangeCardName(change),
                NormalizeChangeQuantity(change.Quantity),
                NormalizeChangeCategory(change.Category) ?? DeckDefaults.Mainboard,
                change.Rationale?.Trim() ?? ""));
        }
    }

    /// <summary>
    /// Adds caller-selected remove-card operations to a plan.
    /// </summary>
    private static void AddRemoveOperations(
        DeckEditPlan plan,
        IReadOnlyList<ExplicitDeckPlanCardChange>? changes)
    {
        foreach (ExplicitDeckPlanCardChange change in changes ?? [])
        {
            plan.Operations.Add(DeckEditOperation.RemoveCard(
                RequireChangeCardName(change),
                NormalizeChangeQuantity(change.Quantity),
                NormalizeChangeCategory(change.Category),
                change.Rationale?.Trim() ?? ""));
        }
    }

    /// <summary>
    /// Adds caller-selected move-card operations to a plan.
    /// </summary>
    private static void AddMoveOperations(
        DeckEditPlan plan,
        IReadOnlyList<ExplicitDeckPlanMoveCardChange>? changes)
    {
        foreach (ExplicitDeckPlanMoveCardChange change in changes ?? [])
        {
            plan.Operations.Add(DeckEditOperation.MoveCard(
                RequireMoveCardName(change),
                NormalizeChangeCategory(change.FromCategory),
                RequireMoveDestination(change),
                change.Rationale?.Trim() ?? ""));
        }
    }

    /// <summary>
    /// Validates the caller-supplied card name before the plan is saved.
    /// </summary>
    private static string RequireChangeCardName(ExplicitDeckPlanCardChange change)
    {
        return !string.IsNullOrWhiteSpace(change.CardName)
            ? change.CardName.Trim()
            : throw new InvalidOperationException("Explicit deck plan changes require a cardName.");
    }

    /// <summary>
    /// Validates the caller-supplied move card name before the plan is saved.
    /// </summary>
    private static string RequireMoveCardName(ExplicitDeckPlanMoveCardChange change)
    {
        return !string.IsNullOrWhiteSpace(change.CardName)
            ? change.CardName.Trim()
            : throw new InvalidOperationException("Explicit deck plan moves require a cardName.");
    }

    /// <summary>
    /// Validates the caller-supplied move destination before the plan is saved.
    /// </summary>
    private static string RequireMoveDestination(ExplicitDeckPlanMoveCardChange change)
    {
        return !string.IsNullOrWhiteSpace(change.ToCategory)
            ? change.ToCategory.Trim()
            : throw new InvalidOperationException("Explicit deck plan moves require a toCategory.");
    }

    /// <summary>
    /// Normalizes non-positive quantities to the single-card default used by mutation tools.
    /// </summary>
    private static int NormalizeChangeQuantity(int quantity)
    {
        return quantity > 0 ? quantity : 1;
    }

    /// <summary>
    /// Preserves only non-blank caller-supplied category values.
    /// </summary>
    private static string? NormalizeChangeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? null : category.Trim();
    }
}
