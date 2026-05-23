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
        if (plan.Operations.Count == 0)
        {
            throw new InvalidOperationException("At least one explicit card add or remove is required.");
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
            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.AddCard,
                CardName = RequireChangeCardName(change),
                Quantity = NormalizeChangeQuantity(change.Quantity),
                Category = NormalizeChangeCategory(change.Category) ?? DeckDefaults.Mainboard,
                Rationale = change.Rationale?.Trim() ?? ""
            });
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
            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.RemoveCard,
                CardName = RequireChangeCardName(change),
                Quantity = NormalizeChangeQuantity(change.Quantity),
                Category = NormalizeChangeCategory(change.Category),
                Rationale = change.Rationale?.Trim() ?? ""
            });
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
