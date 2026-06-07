using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes deck analysis, snapshot refresh, combo review, and summary MCP tools.
/// </summary>
[McpServerToolType]
public sealed class AnalysisTools
{
    /// <summary>
    /// Runs deck analysis workflows.
    /// </summary>
    private readonly DeckAnalysisService analysis;

    /// <summary>
    /// Guards tools that refresh planning-state metadata.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Creates analysis tools for the MCP surface.
    /// </summary>
    public AnalysisTools(DeckAnalysisService analysis, OperationModeGuard operationMode)
    {
        this.analysis = analysis;
        this.operationMode = operationMode;
    }

    /// <summary>
    /// Refreshes cached Scryfall snapshots for workspace cards.
    /// </summary>
    [McpServerTool(Name = "deck_refresh_card_metadata", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Refresh Scryfall snapshot metadata for workspace cards without changing deck contents or writing card changes to Archidekt. Scope: all, included, maybeboard, or missing.")]
    public Task<DeckNormalizationResult> RefreshDeckCardSnapshotsAsync(
        string workspaceId,
        [Description("Metadata refresh scope: all, included, maybeboard, or missing.")]
        string scope = "missing",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("deck_refresh_card_metadata");
        return analysis.RefreshDeckCardSnapshotsAsync(workspaceId, scope, cancellationToken);
    }

    /// <summary>
    /// Summarizes workspace plan, categories, strengths, risks, and next analysis steps.
    /// </summary>
    [McpServerTool(Name = "deck_summarize", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Summarize a deck workspace's plan, role distribution, category map, strengths, risks, and suggested next analysis steps.")]
    public Task<DeckPlanSummary> SummarizeDeckWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.SummarizeDeckWorkspaceAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Explains why cards are counted for a role, tag, or category target.
    /// </summary>
    [McpServerTool(Name = "deck_explain_role_counts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Explain role-count evidence card-by-card for one workspace target such as Ramp, Draw, Interaction, or Wincons.")]
    public Task<DeckRoleCountExplanation> ExplainRoleCountsAsync(
        string workspaceId,
        [Description("Role, tag, or category target to explain, such as Ramp, Draw, Interaction, or Wincons.")]
        string role,
        CancellationToken cancellationToken = default)
    {
        return analysis.ExplainRoleCountsAsync(workspaceId, role, cancellationToken);
    }

    /// <summary>
    /// Reviews weak-slot evidence without selecting final cuts.
    /// </summary>
    [McpServerTool(Name = "deck_review_weak_spots", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Return evidence-only weak-slot rows, role/category balance, existing excluded-card candidates, source statuses, and notes. The assistant should synthesize final recommendations.")]
    public Task<DeckWeakSpotReview> ReviewWeakSpotsAsync(
        string workspaceId,
        [Description("Heuristic analysis profile: auto or a documented deck intent Heuristic Profile value.")]
        string analysisProfile = "auto",
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return analysis.ReviewWeakSpotsAsync(workspaceId, analysisProfile, limit, cancellationToken);
    }

    /// <summary>
    /// Calculates hypergeometric and Monte Carlo odds for requested roles or tags.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_draw_odds", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
        return analysis.AnalyzeDrawOddsAsync(
            workspaceId,
            targets,
            turn,
            openingHandSize,
            simulations,
            seed,
            cancellationToken);
    }

    /// <summary>
    /// Analyzes turn-by-turn odds of making land drops.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_land_drop_odds", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze turn-by-turn odds of making land drops. Uses exact no-mulligan hypergeometric odds and deterministic seeded Monte Carlo for mulligans. onThePlay controls turn-1 draw assumptions.")]
    public Task<LandDropOddsAnalysis> AnalyzeLandDropOddsAsync(
        string workspaceId,
        int turn = 3,
        int openingHandSize = 7,
        bool onThePlay = false,
        bool includeMulligans = true,
        int simulations = 10_000,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return analysis.AnalyzeLandDropOddsAsync(
            workspaceId,
            turn,
            openingHandSize,
            onThePlay,
            includeMulligans,
            simulations,
            seed,
            cancellationToken);
    }

    /// <summary>
    /// Analyzes cached deck prices and top cost drivers.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_cost", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze cached deck prices, included total, maybeboard total, missing prices, and top cost drivers.")]
    public Task<DeckCostAnalysis> AnalyzeDeckCostAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.AnalyzeDeckCostAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Estimates the Commander bracket for a deck.
    /// </summary>
    [McpServerTool(Name = "deck_estimate_commander_bracket", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Estimate Commander bracket using live Scryfall Game Changer data plus fast mana, tutor, stax, combo, extra-turn, and mass-land-denial signals.")]
    public Task<CommanderBracketEstimate> EstimateCommanderBracketAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.EstimateCommanderBracketAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes land count, color sources, fixing, and tapped-land pressure.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_mana", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze land count, color sources, produced mana, tapped-land pressure, fixing, and mana-base risks.")]
    public Task<ManaBaseAnalysis> AnalyzeManaBaseAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.AnalyzeManaBaseAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Analyzes ramp, draw, tutor, selection, and low-curve density.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_consistency", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze ramp, draw, tutor, card-selection, low-curve density, and key draw odds for consistency.")]
    public Task<DeckConsistencyAnalysis> AnalyzeDeckConsistencyAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.AnalyzeDeckConsistencyAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Compares a deck against Commander construction heuristics.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_best_practices", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze a deck against Commander best-practice heuristics, intent targets, role gaps, interaction coverage, wincon clarity, and cited rationale. AnalysisProfile can be auto or a documented Heuristic Profile value.")]
    public Task<DeckBestPracticeAnalysis> AnalyzeDeckBestPracticesAsync(
        string workspaceId,
        [Description("Heuristic analysis profile: auto or a documented deck intent Heuristic Profile value.")]
        string analysisProfile = "auto",
        CancellationToken cancellationToken = default)
    {
        return analysis.AnalyzeDeckBestPracticesAsync(workspaceId, analysisProfile, cancellationToken);
    }

    /// <summary>
    /// Analyzes completed combos, near misses, and pressure in one evidence report.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_combos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Analyze completed combos, near misses, missing cards/templates, produced features, route labels, terminal/needs-payoff flags, and combo pressure. Local heuristics are separated from catalog evidence.")]
    public Task<DeckComboReport> AnalyzeCombosAsync(
        string workspaceId,
        bool includeNearMisses = true,
        bool includeHeuristics = true,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return analysis.AnalyzeCombosAsync(
            workspaceId,
            includeNearMisses,
            includeHeuristics,
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Searches combo catalog evidence containing one card.
    /// </summary>
    [McpServerTool(Name = "combo_search_by_card", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Search Commander Spellbook catalog evidence for combos containing one card. strictColorIdentity filters combos to the supplied commander's Scryfall color identity when commanderName is provided.")]
    public Task<ComboEvidenceSearchResult> SearchCombosByCardAsync(
        string cardNameOrId,
        string format = "commander",
        string? commanderName = null,
        bool strictColorIdentity = true,
        int limit = 50,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return analysis.SearchCombosByCardAsync(
            cardNameOrId,
            format,
            commanderName,
            strictColorIdentity,
            limit,
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Gets raw-preserving combo catalog details.
    /// </summary>
    [McpServerTool(Name = "combo_get_details", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Get raw-preserving Commander Spellbook combo details: combo id, cards, produces, requires, templates, prerequisites, steps, bracket tag, prevalence/popularity fields, source URI, and route labels.")]
    public Task<ComboEvidence?> GetComboDetailsAsync(
        string comboId,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return analysis.GetComboDetailsAsync(comboId, bypassCache, cancellationToken);
    }

    /// <summary>
    /// Classifies cards, combos, workspaces, or produced features into route labels.
    /// </summary>
    [McpServerTool(Name = "card_classify_win_routes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Classify exactly one evidence input into approved win-route labels. Provide exactly one of cardNames, workspaceId, comboId, or producedFeatures. Route labels include combat, tokens, storm, infinite-mana, self-mill, opponent-mill, extra-turns, aristocrats, alternate-win, value-combat, etb, and draw-deck.")]
    public Task<WinRouteClassificationResult> ClassifyWinRoutesAsync(
        string[]? cardNames = null,
        string? workspaceId = null,
        string? comboId = null,
        [Description("Normalized combo produced features to classify. Approved route labels include combat, tokens, storm, infinite-mana, self-mill, opponent-mill, extra-turns, aristocrats, alternate-win, value-combat, etb, and draw-deck.")]
        string[]? producedFeatures = null,
        string format = "commander",
        CancellationToken cancellationToken = default)
    {
        return analysis.ClassifyWinRoutesAsync(
            cardNames,
            workspaceId,
            comboId,
            producedFeatures,
            format,
            cancellationToken);
    }
}
