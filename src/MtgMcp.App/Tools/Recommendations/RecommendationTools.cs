using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes deck recommendation and improvement-planning MCP tools.
/// </summary>
[McpServerToolType]
public sealed class RecommendationTools
{
    /// <summary>
    /// Creates improvement plans and recommendation reports.
    /// </summary>
    private readonly DeckRecommendationService recommendations;

    /// <summary>
    /// Guards tools that persist planning state.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates recommendation tools for the MCP surface.
    /// </summary>
    public RecommendationTools(DeckRecommendationService recommendations, OperationModeGuard operationMode)
    {
        this.recommendations = recommendations;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Finds lower-cost card replacements.
    /// </summary>
    [McpServerTool(Name = "find_budget_replacements", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan of cheaper replacement suggestions using adjustable role, power, and price weights.")]
    public Task<RecommendationPlanResult> FindBudgetReplacementsAsync(
        string workspaceId,
        decimal maxPrice = 5,
        decimal minSavings = 1,
        int limit = 10,
        double roleWeight = 0.45,
        double powerWeight = 0.30,
        double priceWeight = 0.25,
        CancellationToken cancellationToken = default)
    {
        ReplacementWeights weights = new()
        {
            Role = roleWeight,
            Power = powerWeight,
            Price = priceWeight
        };
        operationMode.EnsureCanWritePlanningState("find_budget_replacements");
        return recommendations.FindBudgetReplacementsAsync(workspaceId, maxPrice, minSavings, limit, weights, cancellationToken);
    }

    /// <summary>
    /// Finds stronger card upgrades, optionally focused by power or price constraints.
    /// </summary>
    [McpServerTool(Name = "find_card_upgrades", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan of stronger replacement suggestions using optional focus, price, role, power, and price weights.")]
    public Task<RecommendationPlanResult> FindCardUpgradesAsync(
        string workspaceId,
        string focus = "balanced",
        decimal? maxPrice = null,
        int limit = 10,
        double? roleWeight = null,
        double? powerWeight = null,
        double? priceWeight = null,
        CancellationToken cancellationToken = default)
    {
        ReplacementWeights? weights = null;
        if (roleWeight.HasValue || powerWeight.HasValue || priceWeight.HasValue)
        {
            ReplacementWeights defaults = new();
            weights = new ReplacementWeights
            {
                Role = roleWeight ?? defaults.Role,
                Power = powerWeight ?? defaults.Power,
                Price = priceWeight ?? defaults.Price
            };
        }

        operationMode.EnsureCanWritePlanningState("find_card_upgrades");
        return recommendations.FindCardUpgradesAsync(workspaceId, focus, maxPrice, limit, weights, cancellationToken);
    }

    /// <summary>
    /// Finds replacements that lower Commander bracket pressure.
    /// </summary>
    [McpServerTool(Name = "find_bracket_reduction_candidates", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan to reduce Commander bracket pressure from Game Changers, fast mana, tutors, stax, combo, extra turns, and land denial.")]
    public Task<RecommendationPlanResult> FindBracketReductionCandidatesAsync(
        string workspaceId,
        int targetBracket = 2,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("find_bracket_reduction_candidates");
        return recommendations.FindBracketReductionCandidatesAsync(workspaceId, targetBracket, limit, cancellationToken);
    }

    /// <summary>
    /// Finds replacements that soften power level.
    /// </summary>
    [McpServerTool(Name = "find_power_reduction_candidates", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan to soften fast mana, tutors, stax, combo, extra-turn, and highly efficient pressure.")]
    public Task<RecommendationPlanResult> FindPowerReductionCandidatesAsync(
        string workspaceId,
        string targetPower = "casual",
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("find_power_reduction_candidates");
        return recommendations.FindPowerReductionCandidatesAsync(workspaceId, targetPower, limit, cancellationToken);
    }

    /// <summary>
    /// Finds land and fixing improvements.
    /// </summary>
    [McpServerTool(Name = "find_mana_base_improvements", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan for land count, fixing, color-source, and tapped-land improvements.")]
    public Task<RecommendationPlanResult> FindManaBaseImprovementsAsync(
        string workspaceId,
        decimal maxPrice = 10,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("find_mana_base_improvements");
        return recommendations.FindManaBaseImprovementsAsync(workspaceId, maxPrice, limit, cancellationToken);
    }

    /// <summary>
    /// Finds additions that improve ramp, draw, tutors, or card selection.
    /// </summary>
    [McpServerTool(Name = "find_consistency_improvements", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan to improve ramp, draw, tutor, card-selection, or balanced consistency gaps.")]
    public Task<RecommendationPlanResult> FindConsistencyImprovementsAsync(
        string workspaceId,
        string focus = "balanced",
        decimal maxPrice = 10,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("find_consistency_improvements");
        return recommendations.FindConsistencyImprovementsAsync(workspaceId, focus, maxPrice, limit, cancellationToken);
    }

    /// <summary>
    /// Suggests standard role categories for a workspace.
    /// </summary>
    [McpServerTool(Name = "suggest_deck_categories", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a persisted non-mutating category cleanup plan using standard MTG deck roles and secondary tags.")]
    public Task<CategoryPlanResult> SuggestDeckCategoriesAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("suggest_deck_categories");
        return recommendations.SuggestDeckCategoriesAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Compares a deck to Commander metagame context.
    /// </summary>
    [McpServerTool(Name = "compare_to_commander_meta", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Compare a deck to optional Commander metagame context, reporting popular included and missing cards.")]
    public Task<CommanderMetaReport> CompareToCommanderMetaAsync(
        string workspaceId,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        return recommendations.CompareToCommanderMetaAsync(workspaceId, limit, cancellationToken);
    }

    /// <summary>
    /// Finds missing popular commander or theme cards.
    /// </summary>
    [McpServerTool(Name = "find_missing_popular_cards", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan for popular commander/theme cards missing from the deck.")]
    public Task<GoalPackagePlanResult> FindMissingPopularCardsAsync(
        string workspaceId,
        int limit = 10,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("find_missing_popular_cards");
        return recommendations.FindMissingPopularCardsAsync(workspaceId, limit, maxPrice, cancellationToken);
    }

    /// <summary>
    /// Finds newly released cards that fit a deck.
    /// </summary>
    [McpServerTool(Name = "find_new_cards_for_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find newly released cards that fit the deck's format, color identity, intent, roles, and theme. Since accepts YYYY-MM-DD and defaults to the last year.")]
    public Task<NewCardsForDeckResult> FindNewCardsForDeckAsync(
        string workspaceId,
        string? since = null,
        string? setCode = null,
        int limit = 10,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        return recommendations.FindNewCardsForDeckAsync(workspaceId, since, setCode, limit, maxPrice, cancellationToken);
    }

    /// <summary>
    /// Finds cards for a natural-language deck goal.
    /// </summary>
    [McpServerTool(Name = "find_cards_for_deck_goal", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan from a natural-language goal such as table-wide interaction, token defense, finishers, or graveyard hate.")]
    public Task<GoalPackagePlanResult> FindCardsForDeckGoalAsync(
        string workspaceId,
        string goal,
        int count = 3,
        decimal maxPrice = 10,
        string strategy = "balanced",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("find_cards_for_deck_goal");
        return recommendations.FindCardsForDeckGoalAsync(workspaceId, goal, count, maxPrice, strategy, cancellationToken);
    }

    /// <summary>
    /// Runs a combined recommendation workflow.
    /// </summary>
    [McpServerTool(Name = "brainstorm_deck_improvements", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Run best-practice analysis, meta comparison, new-card radar, goal recommendations, combo review, and goldfish projection in one workflow.")]
    public Task<BrainstormDeckImprovementsResult> BrainstormDeckImprovementsAsync(
        string workspaceId,
        string goal = "",
        decimal budget = 10,
        string targetPower = "balanced",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("brainstorm_deck_improvements");
        return recommendations.BrainstormDeckImprovementsAsync(workspaceId, goal, budget, targetPower, cancellationToken);
    }
}
