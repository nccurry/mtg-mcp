namespace MtgMcp.Core;

/// <summary>
/// Builds recommendation plans for deck category cleanup.
/// </summary>
public sealed class DeckCategorySuggestionService
{
    /// <summary>
    /// Loads local workspaces for category planning.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Persists generated category plans when plan tools are enabled.
    /// </summary>
    private readonly IDeckPlanRepository? planRepository;

    /// <summary>
    /// Creates a category suggestion collaborator with explicit storage dependencies.
    /// </summary>
    public DeckCategorySuggestionService(
        IDeckWorkspaceRepository repository,
        IDeckPlanRepository? planRepository = null)
    {
        this.repository = repository;
        this.planRepository = planRepository;
    }

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
        DeckEditPlan plan = DeckServiceHelpers.CreatePlan(workspace, "Category cleanup plan", "category-cleanup");
        plan.Rationale = "Groups cards into the standard role taxonomy while preserving existing deck contents until the plan is applied.";

        foreach (string role in DeckRoles.Primary)
        {
            if (role is DeckRoles.Commander or DeckRoles.Maybeboard)
            {
                continue;
            }

            if (!workspace.Categories.Any(category => category.Name.Equals(role, StringComparison.OrdinalIgnoreCase)))
            {
                plan.Operations.Add(DeckEditOperation.CreateCategory(
                    role,
                    includedInDeck: true,
                    includedInPrice: true,
                    rationale: $"Create standard role category {role}."));
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
                ScryfallUri = DeckServiceHelpers.GetSnapshot(card).ScryfallUri,
                Confidence = assignment.Confidence
            });

            if (assignment.PrimaryRole is DeckRoles.Commander or DeckRoles.Maybeboard)
            {
                continue;
            }

            if (!string.Equals(card.PrimaryCategory, assignment.PrimaryRole, StringComparison.OrdinalIgnoreCase)
                && assignment.Confidence >= 0.55)
            {
                plan.Operations.Add(DeckEditOperation.MoveCard(
                    card.Name,
                    card.PrimaryCategory,
                    assignment.PrimaryRole,
                    $"Classified as {assignment.PrimaryRole} with {assignment.Confidence:0.00} confidence."));
            }
        }

        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.Confidence);
        await DeckServiceHelpers.RequirePlanRepository(planRepository).SaveAsync(plan, cancellationToken).ConfigureAwait(false);

        return new CategoryPlanResult { Plan = plan, Suggestions = suggestions };
    }

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
