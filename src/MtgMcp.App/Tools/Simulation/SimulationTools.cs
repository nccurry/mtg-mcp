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
        return simulation.SimulateGoldfishAsync(workspaceId, targetTurn, simulations, seed, mulligan, cancellationToken);
    }

    /// <summary>
    /// Projects likely board state by a turn.
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
        return simulation.ProjectBoardStateAsync(workspaceId, turn, simulations, seed, cancellationToken);
    }

    /// <summary>
    /// Estimates likely goldfish win turns and win routes.
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
        return simulation.EstimateWinTurnAsync(workspaceId, maxTurn, simulations, seed, cancellationToken);
    }
}
