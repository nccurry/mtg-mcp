using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Provides deck intelligence tool behavior.
/// </summary>
[McpServerToolType]
public sealed class IntelligenceTools
{
    /// <summary>
    /// Stores the decks service.
    /// </summary>
    private readonly DeckWorkspaceService decks;

    /// <summary>
    /// Stores the operation mode.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Handles intelligence tools.
    /// </summary>
    public IntelligenceTools(DeckWorkspaceService decks, OperationModeGuard operationMode)
    {
        this.decks = decks;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Normalizes deck cards.
    /// </summary>
    [McpServerTool(Name = "normalize_deck_cards", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Refresh Scryfall snapshot metadata for workspace cards without changing deck contents or writing card changes to Archidekt. Scope: all, included, maybeboard, or missing.")]
    public Task<DeckNormalizationResult> NormalizeDeckCardsAsync(
        string workspaceId,
        string scope = "missing",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("normalize_deck_cards");
        return decks.NormalizeDeckCardsAsync(workspaceId, scope, cancellationToken);
    }

    /// <summary>
    /// Summarizes the deck plan.
    /// </summary>
    [McpServerTool(Name = "summarize_deck_plan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Summarize a deck's plan, role distribution, category map, strengths, risks, and suggested next analysis steps.")]
    public Task<DeckPlanSummary> SummarizeDeckPlanAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.SummarizeDeckPlanAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes draw odds.
    /// </summary>
    [McpServerTool(Name = "analyze_draw_odds", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Calculate hypergeometric and Monte Carlo odds of seeing roles or tags by a turn. Targets is a comma-separated list such as 'Lands,Ramp,Draw,Discard'.")]
    public Task<DeckOddsAnalysis> AnalyzeDrawOddsAsync(
        string workspaceId,
        string? targets = null,
        int turn = 3,
        int openingHandSize = 7,
        int simulations = 10_000,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return decks.AnalyzeDrawOddsAsync(
            workspaceId,
            targets,
            turn,
            openingHandSize,
            simulations,
            seed,
            cancellationToken);
    }

    /// <summary>
    /// Analyzes deck cost.
    /// </summary>
    [McpServerTool(Name = "analyze_deck_cost", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze cached deck prices, included total, maybeboard total, missing prices, and top cost drivers.")]
    public Task<DeckCostAnalysis> AnalyzeDeckCostAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.AnalyzeDeckCostAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Previews a deck plan.
    /// </summary>
    [McpServerTool(Name = "preview_deck_plan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Preview a persisted deck edit plan without mutating local or Archidekt state. Returns before and after cost, validation, roles, mana, consistency, and bracket metrics.")]
    public Task<DeckPlanPreviewResult> PreviewDeckPlanAsync(
        string planId,
        bool resolveAddedCards = true,
        CancellationToken cancellationToken = default)
    {
        return decks.PreviewDeckPlanAsync(planId, resolveAddedCards, cancellationToken);
    }

    /// <summary>
    /// Estimates commander bracket.
    /// </summary>
    [McpServerTool(Name = "estimate_commander_bracket", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Estimate Commander bracket using live Scryfall Game Changer data plus fast mana, tutor, stax, combo, extra-turn, and mass-land-denial signals.")]
    public Task<CommanderBracketEstimate> EstimateCommanderBracketAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.EstimateCommanderBracketAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes mana base.
    /// </summary>
    [McpServerTool(Name = "analyze_mana_base", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze land count, color sources, produced mana, tapped-land pressure, fixing, and mana-base risks.")]
    public Task<ManaBaseAnalysis> AnalyzeManaBaseAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.AnalyzeManaBaseAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes deck consistency.
    /// </summary>
    [McpServerTool(Name = "analyze_deck_consistency", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze ramp, draw, tutor, card-selection, low-curve density, and key draw odds for consistency.")]
    public Task<DeckConsistencyAnalysis> AnalyzeDeckConsistencyAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.AnalyzeDeckConsistencyAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Finds budget replacements.
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
        return decks.FindBudgetReplacementsAsync(workspaceId, maxPrice, minSavings, limit, weights, cancellationToken);
    }

    /// <summary>
    /// Finds card upgrades.
    /// </summary>
    [McpServerTool(Name = "find_card_upgrades", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan of stronger replacement suggestions using adjustable role, power, and price weights.")]
    public Task<RecommendationPlanResult> FindCardUpgradesAsync(
        string workspaceId,
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
        operationMode.EnsureCanWritePlanningState("find_card_upgrades");
        return decks.FindCardUpgradesAsync(workspaceId, limit, weights, cancellationToken);
    }

    /// <summary>
    /// Finds power upgrades.
    /// </summary>
    [McpServerTool(Name = "find_power_upgrades", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted non-mutating plan of targeted power upgrades using focus, price, and optional role/power/price weight overrides.")]
    public Task<RecommendationPlanResult> FindPowerUpgradesAsync(
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

        operationMode.EnsureCanWritePlanningState("find_power_upgrades");
        return decks.FindPowerUpgradesAsync(workspaceId, focus, maxPrice, limit, weights, cancellationToken);
    }

    /// <summary>
    /// Finds bracket reduction candidates.
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
        return decks.FindBracketReductionCandidatesAsync(workspaceId, targetBracket, limit, cancellationToken);
    }

    /// <summary>
    /// Finds power reduction candidates.
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
        return decks.FindPowerReductionCandidatesAsync(workspaceId, targetPower, limit, cancellationToken);
    }

    /// <summary>
    /// Finds mana base improvements.
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
        return decks.FindManaBaseImprovementsAsync(workspaceId, maxPrice, limit, cancellationToken);
    }

    /// <summary>
    /// Finds consistency improvements.
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
        return decks.FindConsistencyImprovementsAsync(workspaceId, focus, maxPrice, limit, cancellationToken);
    }

    /// <summary>
    /// Suggests deck categories.
    /// </summary>
    [McpServerTool(Name = "suggest_deck_categories", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a persisted non-mutating category cleanup plan using standard MTG deck roles and secondary tags.")]
    public Task<CategoryPlanResult> SuggestDeckCategoriesAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("suggest_deck_categories");
        return decks.SuggestDeckCategoriesAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes deck best practices.
    /// </summary>
    [McpServerTool(Name = "analyze_deck_best_practices", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze a deck against Commander best-practice heuristics, intent targets, role gaps, interaction coverage, wincon clarity, and cited rationale. Profile can be auto or a documented Heuristic Profile value.")]
    public Task<DeckBestPracticeAnalysis> AnalyzeDeckBestPracticesAsync(
        string workspaceId,
        string profile = "auto",
        CancellationToken cancellationToken = default)
    {
        return decks.AnalyzeDeckBestPracticesAsync(workspaceId, profile, cancellationToken);
    }

    /// <summary>
    /// Compares a deck to commander meta.
    /// </summary>
    [McpServerTool(Name = "compare_to_commander_meta", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Compare a deck to optional Commander metagame context, reporting popular included and missing cards.")]
    public Task<CommanderMetaReport> CompareToCommanderMetaAsync(
        string workspaceId,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        return decks.CompareToCommanderMetaAsync(workspaceId, limit, cancellationToken);
    }

    /// <summary>
    /// Finds missing popular cards.
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
        return decks.FindMissingPopularCardsAsync(workspaceId, limit, maxPrice, cancellationToken);
    }

    /// <summary>
    /// Finds new cards for a deck.
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
        return decks.FindNewCardsForDeckAsync(workspaceId, since, setCode, limit, maxPrice, cancellationToken);
    }

    /// <summary>
    /// Finds cards for a deck goal.
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
        return decks.FindCardsForDeckGoalAsync(workspaceId, goal, count, maxPrice, strategy, cancellationToken);
    }

    /// <summary>
    /// Finds deck combos.
    /// </summary>
    [McpServerTool(Name = "find_deck_combos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find completed combos using a configured combo catalog or local combo heuristics.")]
    public Task<DeckComboReport> FindDeckCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.FindDeckCombosAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Finds combo near misses.
    /// </summary>
    [McpServerTool(Name = "find_near_miss_combos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find one-card-away or partial combo routes using a configured combo catalog or local combo heuristics.")]
    public Task<DeckComboReport> FindNearMissCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.FindNearMissCombosAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Estimates combo pressure.
    /// </summary>
    [McpServerTool(Name = "estimate_combo_pressure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Estimate combo pressure from completed combos, near misses, tutors, and combo-piece density.")]
    public Task<ComboPressureEstimate> EstimateComboPressureAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return decks.EstimateComboPressureAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Simulates a goldfish.
    /// </summary>
    [McpServerTool(Name = "simulate_goldfish", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run heuristic no-interaction goldfish simulations with simple mulligans, sequencing, board projection, and win-route estimates.")]
    public Task<GoldfishSimulationResult> SimulateGoldfishAsync(
        string workspaceId,
        int targetTurn = 7,
        int simulations = 1_000,
        int seed = 1337,
        bool mulligan = true,
        CancellationToken cancellationToken = default)
    {
        return decks.SimulateGoldfishAsync(workspaceId, targetTurn, simulations, seed, mulligan, cancellationToken);
    }

    /// <summary>
    /// Projects the board state.
    /// </summary>
    [McpServerTool(Name = "project_board_state", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Project the likely board state by a turn if the deck is not interacted with.")]
    public Task<ProjectedTurnState> ProjectBoardStateAsync(
        string workspaceId,
        int turn = 5,
        int simulations = 1_000,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return decks.ProjectBoardStateAsync(workspaceId, turn, simulations, seed, cancellationToken);
    }

    /// <summary>
    /// Estimates the win turn.
    /// </summary>
    [McpServerTool(Name = "estimate_win_turn", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Estimate likely goldfish win turns and routes such as combat, finishers, or combo.")]
    public Task<WinTurnEstimate> EstimateWinTurnAsync(
        string workspaceId,
        int maxTurn = 12,
        int simulations = 1_000,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return decks.EstimateWinTurnAsync(workspaceId, maxTurn, simulations, seed, cancellationToken);
    }

    /// <summary>
    /// Brainstorms deck improvements.
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
        return decks.BrainstormDeckImprovementsAsync(workspaceId, goal, budget, targetPower, cancellationToken);
    }

    /// <summary>
    /// Lists deck plans.
    /// </summary>
    [McpServerTool(Name = "list_deck_plans", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List persisted deck edit plans, optionally filtered by workspace id.")]
    public Task<IReadOnlyList<DeckEditPlan>> ListDeckPlansAsync(
        string? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        return decks.ListDeckPlansAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Gets a deck plan.
    /// </summary>
    [McpServerTool(Name = "get_deck_plan", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get a persisted deck edit plan by plan id.")]
    public Task<DeckEditPlan> GetDeckPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        return decks.GetDeckPlanAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Deletes a deck plan.
    /// </summary>
    [McpServerTool(Name = "delete_deck_plan", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete a persisted deck edit plan. This does not change deck contents.")]
    public Task DeleteDeckPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("delete_deck_plan");
        return decks.DeleteDeckPlanAsync(planId, cancellationToken);
    }

    /// <summary>
    /// Applies a deck plan.
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
        return decks.ApplyDeckPlanAsync(planId, createCheckpoint, checkpointName, cancellationToken);
    }
}
