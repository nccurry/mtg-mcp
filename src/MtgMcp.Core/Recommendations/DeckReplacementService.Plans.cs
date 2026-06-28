namespace MtgMcp.Core;

/// <summary>
/// Contains replacement-plan persistence and operation assembly internals.
/// </summary>
public sealed partial class DeckReplacementService
{
    /// <summary>
    /// Saves a replacement plan.
    /// </summary>
    private async Task<DeckEditPlan> SaveReplacementPlanAsync(
        DeckWorkspace workspace,
        string name,
        string kind,
        IReadOnlyList<ReplacementSuggestion> suggestions,
        ReplacementWeights weights,
        DeckIntent? intent,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = DeckServiceHelpers.CreatePlan(workspace, name, kind);
        plan.Rationale = $"Weighted replacement plan using role={weights.Role:0.##}, power={weights.Power:0.##}, price={weights.Price:0.##}.";
        if (intent is not null)
        {
            plan.Warnings.Add("This plan used the deck intent stored in the workspace description.");
        }

        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.Score);
        if (suggestions.Count == 0)
        {
            plan.Warnings.Add("No replacements met the current filters.");
        }

        AddReplacementOperations(plan, workspace, suggestions);

        return await DeckServiceHelpers.RequirePlanRepository(planRepository).SaveAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the plan and adds a no-op warning when empty.
    /// </summary>
    private async Task SavePlanWithWarningsAsync(
        DeckEditPlan plan,
        int candidateCount,
        CancellationToken cancellationToken)
    {
        if (candidateCount == 0 || plan.Operations.Count == 0)
        {
            plan.Warnings.Add("No candidates met the current filters.");
        }

        plan.Confidence = plan.Operations.Count == 0 ? 0 : 0.65;
        await DeckServiceHelpers.RequirePlanRepository(planRepository).SaveAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds replacement operations to a plan.
    /// </summary>
    private static void AddReplacementOperations(
        DeckEditPlan plan,
        DeckWorkspace workspace,
        IReadOnlyList<ReplacementSuggestion> suggestions)
    {
        foreach (ReplacementSuggestion suggestion in suggestions)
        {
            DeckCard? currentCard = workspace.Cards.FirstOrDefault(card =>
                card.Name.Equals(suggestion.ReplaceCard, StringComparison.OrdinalIgnoreCase));
            int quantity = currentCard?.Quantity ?? 1;
            string category = currentCard is null
                ? DeckDefaults.Mainboard
                : DeckCategoryOrdering.PrimaryCategory(currentCard);

            plan.Operations.Add(DeckEditOperation.RemoveCard(
                suggestion.ReplaceCard,
                quantity,
                category,
                suggestion.Rationale));
            plan.Operations.Add(DeckEditOperation.AddCard(
                suggestion.WithCard,
                quantity,
                category,
                suggestion.Rationale));
        }
    }
}
