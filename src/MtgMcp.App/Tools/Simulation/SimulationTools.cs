using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes goldfish simulation and projection MCP tools.
/// </summary>
[McpServerToolType]
public sealed class SimulationTools
{
    /// <summary>
    /// Runs goldfish simulations and board projections.
    /// </summary>
    private readonly DeckSimulationService simulation;

    /// <summary>
    /// Creates simulation tools for the MCP surface.
    /// </summary>
    public SimulationTools(DeckSimulationService simulation)
    {
        this.simulation = simulation;
    }

    /// <summary>
    /// Runs no-interaction goldfish simulations.
    /// </summary>
    [McpServerTool(Name = "deck_simulate_goldfish", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run heuristic no-interaction goldfish simulations with profile-resolved London mulligans, command-zone sequencing, board projection, and win-route estimates.")]
    public Task<GoldfishSimulationResult> SimulateGoldfishAsync(
        string workspaceId,
        int targetTurn = 7,
        int simulations = 1_000,
        int seed = 1337,
        bool mulligan = true,
        CancellationToken cancellationToken = default)
    {
        return simulation.SimulateGoldfishAsync(workspaceId, targetTurn, simulations, seed, mulligan, cancellationToken);
    }

    /// <summary>
    /// Compares local workspaces and optional Archidekt references with the same goldfish settings.
    /// </summary>
    [McpServerTool(Name = "deck_compare_goldfish", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Compare deterministic no-interaction goldfish outputs for 2-8 total local workspace ids and optional read-only Archidekt deck ids or URLs. The first workspace id is the baseline; per-input failures are returned without aborting other decks.")]
    public Task<DeckGoldfishComparisonResult> CompareGoldfishAsync(
        [Description("Local workspace ids. The first id is the active comparison baseline.")]
        string[] workspaceIds,
        [Description("Optional Archidekt deck ids or URLs imported read-only for comparison.")]
        string[]? archidektDeckIdsOrUrls = null,
        int targetTurn = 7,
        int simulations = 1_000,
        int seed = 1337,
        bool mulligan = true,
        CancellationToken cancellationToken = default)
    {
        return simulation.CompareGoldfishAsync(
            workspaceIds,
            archidektDeckIdsOrUrls,
            targetTurn,
            simulations,
            seed,
            mulligan,
            cancellationToken);
    }

    /// <summary>
    /// Compares active deck goldfish output against caller-supplied Archidekt decks.
    /// </summary>
    [McpServerTool(Name = "archidekt_compare_goldfish", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Import up to three Archidekt deck ids or URLs read-only and compare deterministic goldfish simulation outputs against the active workspace. Non-Archidekt references are reported without aborting other comparisons.")]
    public Task<ArchidektGoldfishComparisonResult> CompareArchidektGoldfishAsync(
        string workspaceId,
        string deckIdOrUrl1,
        string? deckIdOrUrl2 = null,
        string? deckIdOrUrl3 = null,
        int targetTurn = 7,
        int simulations = 1_000,
        int seed = 1337,
        bool mulligan = true,
        CancellationToken cancellationToken = default)
    {
        return simulation.CompareArchidektGoldfishAsync(
            workspaceId,
            deckIdOrUrl1,
            deckIdOrUrl2,
            deckIdOrUrl3,
            targetTurn,
            simulations,
            seed,
            mulligan,
            cancellationToken);
    }

    /// <summary>
    /// Projects likely board state by a turn.
    /// </summary>
    [McpServerTool(Name = "deck_project_board_state", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Project the likely board state by a turn if the deck is not interacted with.")]
    public Task<ProjectedTurnState> ProjectBoardStateAsync(
        string workspaceId,
        int turn = 5,
        int simulations = 1_000,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return simulation.ProjectBoardStateAsync(workspaceId, turn, simulations, seed, cancellationToken);
    }

    /// <summary>
    /// Estimates likely goldfish win turns and win routes.
    /// </summary>
    [McpServerTool(Name = "deck_estimate_win_turn", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Estimate likely goldfish win turns and routes such as combat, finishers, or combo.")]
    public Task<WinTurnEstimate> EstimateWinTurnAsync(
        string workspaceId,
        int maxTurn = 12,
        int simulations = 1_000,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return simulation.EstimateWinTurnAsync(workspaceId, maxTurn, simulations, seed, cancellationToken);
    }

    /// <summary>
    /// Runs deterministic whole-deck performance analysis.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_performance", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Run deterministic Stats Lab Monte Carlo analysis for opening hands, mulligans, land drops, colors, castability, command-zone timing, combo assembly, stranded cards, and named scenarios. Returns modelVersion, replay fingerprints, metric scorecard dimensions that are not a power ranking, and bounded traceSummary samples.")]
    public Task<DeckPerformanceAnalysis> AnalyzeDeckPerformanceAsync(
        string workspaceId,
        [Description("Simulation profile: auto, neutral, aggro, combo, control, value, big-mana, stax, or configured profile id.")]
        string simulationProfile = "auto",
        int simulations = 50_000,
        int maxTurn = 8,
        int seed = 1337,
        bool includeMulligans = true,
        CancellationToken cancellationToken = default)
    {
        return simulation.AnalyzeDeckPerformanceAsync(
            workspaceId,
            simulationProfile,
            simulations,
            maxTurn,
            seed,
            includeMulligans,
            cancellationToken);
    }

    /// <summary>
    /// Compares performance before and after a persisted deck plan.
    /// </summary>
    [McpServerTool(Name = "deck_plan_compare_performance", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Preview a persisted deck edit plan and compare deterministic Stats Lab performance before and after the changes. Use scorecard dimensions, scenario deltas, modelVersion, replay fingerprints, and traceSummary context as metric evidence, not a universal deck power score.")]
    public Task<DeckPerformanceComparison> ComparePlanPerformanceAsync(
        string planId,
        [Description("Simulation profile: auto, neutral, aggro, combo, control, value, big-mana, stax, or configured profile id.")]
        string simulationProfile = "auto",
        int simulations = 50_000,
        int maxTurn = 8,
        int seed = 1337,
        CancellationToken cancellationToken = default)
    {
        return simulation.ComparePlanPerformanceAsync(
            planId,
            simulationProfile,
            simulations,
            maxTurn,
            seed,
            cancellationToken);
    }
}
