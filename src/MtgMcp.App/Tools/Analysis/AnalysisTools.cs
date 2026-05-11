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
    [McpServerTool(Name = "refresh_deck_card_snapshots", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Refresh Scryfall snapshot metadata for workspace cards without changing deck contents or writing card changes to Archidekt. Scope: all, included, maybeboard, or missing.")]
    public Task<DeckNormalizationResult> RefreshDeckCardSnapshotsAsync(
        string workspaceId,
        string scope = "missing",
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("refresh_deck_card_snapshots");
        return analysis.RefreshDeckCardSnapshotsAsync(workspaceId, scope, cancellationToken);
    }

    /// <summary>
    /// Summarizes workspace plan, categories, strengths, risks, and next analysis steps.
    /// </summary>
    [McpServerTool(Name = "summarize_deck_workspace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Summarize a deck workspace's plan, role distribution, category map, strengths, risks, and suggested next analysis steps.")]
    public Task<DeckPlanSummary> SummarizeDeckWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.SummarizeDeckWorkspaceAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Calculates hypergeometric and Monte Carlo odds for requested roles or tags.
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
    /// Analyzes cached deck prices and top cost drivers.
    /// </summary>
    [McpServerTool(Name = "analyze_deck_cost", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
    [McpServerTool(Name = "estimate_commander_bracket", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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
    [McpServerTool(Name = "analyze_mana_base", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
    [McpServerTool(Name = "analyze_deck_consistency", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
    [McpServerTool(Name = "analyze_deck_best_practices", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Analyze a deck against Commander best-practice heuristics, intent targets, role gaps, interaction coverage, wincon clarity, and cited rationale. Profile can be auto or a documented Heuristic Profile value.")]
    public Task<DeckBestPracticeAnalysis> AnalyzeDeckBestPracticesAsync(
        string workspaceId,
        string profile = "auto",
        CancellationToken cancellationToken = default)
    {
        return analysis.AnalyzeDeckBestPracticesAsync(workspaceId, profile, cancellationToken);
    }

    /// <summary>
    /// Finds completed combos in a deck.
    /// </summary>
    [McpServerTool(Name = "find_deck_combos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find completed combos using a configured combo catalog or local combo heuristics.")]
    public Task<DeckComboReport> FindDeckCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.FindDeckCombosAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Finds one-card-away or partial combo routes.
    /// </summary>
    [McpServerTool(Name = "find_near_miss_combos", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find one-card-away or partial combo routes using a configured combo catalog or local combo heuristics.")]
    public Task<DeckComboReport> FindNearMissCombosAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.FindNearMissCombosAsync(workspaceId, cancellationToken);
    }

    /// <summary>
    /// Estimates combo pressure from combo candidates, tutors, and tags.
    /// </summary>
    [McpServerTool(Name = "estimate_combo_pressure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Estimate combo pressure from completed combos, near misses, tutors, and combo-piece density.")]
    public Task<ComboPressureEstimate> EstimateComboPressureAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        return analysis.EstimateComboPressureAsync(workspaceId, cancellationToken);
    }
}
