namespace MtgMcp.Core;

/// <summary>
/// Builds recommendation plans for deck category cleanup.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Suggests deck categories.
    /// </summary>
    public async Task<CategoryPlanResult> SuggestDeckCategoriesAsync(
        string workspaceId,
        CancellationToken cancellationToken
    )
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        List<CategorySuggestion> suggestions = [];
        DeckEditPlan plan = CreatePlan(workspace, "Category cleanup plan", "category-cleanup");
        plan.Rationale = "Groups cards into the standard role taxonomy while preserving existing deck contents until the plan is applied.";

        foreach (string role in DeckRoles.Primary)
        {
            if (role is DeckRoles.Commander or DeckRoles.Maybeboard)
            {
                continue;
            }

            if (!workspace.Categories.Any(category => category.Name.Equals(role, StringComparison.OrdinalIgnoreCase)))
            {
                plan.Operations.Add(new DeckEditOperation
                {
                    Operation = DeckEditOperations.CreateCategory,
                    Category = role,
                    IncludedInDeck = true,
                    IncludedInPrice = true,
                    Rationale = $"Create standard role category {role}."
                });
            }
        }

        foreach (DeckCard card in workspace.Cards)
        {
            CardRoleAssignment assignment = DeckRoleClassifier.Classify(card);
            suggestions.Add(new CategorySuggestion
            {
                CardName = card.Name,
                CurrentPrimaryCategory = card.PrimaryCategory,
                SuggestedPrimaryRole = assignment.PrimaryRole,
                Tags = assignment.Tags,
                Confidence = assignment.Confidence
            });

            if (assignment.PrimaryRole is DeckRoles.Commander or DeckRoles.Maybeboard)
            {
                continue;
            }

            if (!string.Equals(card.PrimaryCategory, assignment.PrimaryRole, StringComparison.OrdinalIgnoreCase)
                && assignment.Confidence >= 0.55)
            {
                plan.Operations.Add(new DeckEditOperation
                {
                    Operation = DeckEditOperations.MoveCard,
                    CardName = card.Name,
                    FromCategory = card.PrimaryCategory,
                    ToCategory = assignment.PrimaryRole,
                    Rationale = $"Classified as {assignment.PrimaryRole} with {assignment.Confidence:0.00} confidence."
                });
            }
        }

        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.Confidence);
        await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        return new CategoryPlanResult { Plan = plan, Suggestions = suggestions };
    }
}
