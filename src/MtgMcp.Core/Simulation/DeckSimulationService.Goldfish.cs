namespace MtgMcp.Core;

/// <summary>
/// Provides heuristic goldfish simulation behavior.
/// </summary>
public sealed partial class DeckSimulationService : DeckServiceBase
{
    /// <summary>
    /// Labels heuristic no-interaction simulations that favor smooth sequencing.
    /// </summary>
    private const string GoldfishModelLabel = "optimistic-goldfish-model";

    /// <summary>
    /// Labels board projection output derived from heuristic goldfish snapshots.
    /// </summary>
    private const string BoardProjectionModelLabel = "heuristic-board-projection";

    /// <summary>
    /// Labels the random generator used by the heuristic goldfish family.
    /// </summary>
    private const string GoldfishRngKind = DeterministicSimulationRandom.Kind;

    /// <summary>
    /// Runs a heuristic no-interaction goldfish simulation.
    /// </summary>
    public async Task<GoldfishSimulationResult> SimulateGoldfishAsync(
        string workspaceId,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        return await SimulateGoldfishAsync(
                workspaceId,
                SimulationProfileIds.Auto,
                targetTurn,
                simulations,
                seed,
                mulligan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a heuristic no-interaction goldfish simulation with a caller-selected simulation profile.
    /// </summary>
    public async Task<GoldfishSimulationResult> SimulateGoldfishAsync(
        string workspaceId,
        string simulationProfile,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return SimulateGoldfish(workspace, simulationProfile, targetTurn, simulations, seed, mulligan, simulationProfiles);
    }

    /// <summary>
    /// Projects the likely board state by a requested turn.
    /// </summary>
    public async Task<ProjectedTurnState> ProjectBoardStateAsync(
        string workspaceId,
        int turn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        return await ProjectBoardStateAsync(
                workspaceId,
                SimulationProfileIds.Auto,
                turn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Projects the likely board state by a requested turn with a caller-selected simulation profile.
    /// </summary>
    public async Task<ProjectedTurnState> ProjectBoardStateAsync(
        string workspaceId,
        string simulationProfile,
        int turn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
            simulationProfile,
            turn,
            simulations,
            seed,
            mulligan: true,
            cancellationToken).ConfigureAwait(false);
        return result.TurnSummaries.LastOrDefault()
            ?? new ProjectedTurnState
            {
                Turn = Math.Max(1, turn),
                ModelLabel = BoardProjectionModelLabel,
                RngKind = GoldfishRngKind,
                LikelyBoard = "No projection could be produced.",
                Notes = ["Projection is derived from the optimistic goldfish model and does not model opponent interaction."],
            };
    }

    /// <summary>
    /// Estimates the likely win turn and win routes.
    /// </summary>
    public async Task<WinTurnEstimate> EstimateWinTurnAsync(
        string workspaceId,
        int maxTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        return await EstimateWinTurnAsync(
                workspaceId,
                SimulationProfileIds.Auto,
                maxTurn,
                simulations,
                seed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Estimates likely goldfish win turns and routes with a caller-selected simulation profile.
    /// </summary>
    public async Task<WinTurnEstimate> EstimateWinTurnAsync(
        string workspaceId,
        string simulationProfile,
        int maxTurn,
        int simulations,
        int seed,
        CancellationToken cancellationToken)
    {
        GoldfishSimulationResult result = await SimulateGoldfishAsync(
            workspaceId,
            simulationProfile,
            maxTurn,
            simulations,
            seed,
            mulligan: true,
            cancellationToken).ConfigureAwait(false);
        return result.WinEstimate;
    }

    /// <summary>
    /// Runs the goldfish simulator for a workspace.
    /// </summary>
    private static GoldfishSimulationResult SimulateGoldfish(
        DeckWorkspace workspace,
        string? requestedProfile,
        int targetTurn,
        int simulations,
        int seed,
        bool mulligan,
        SimulationProfileCatalog? simulationProfiles = null)
    {
        int safeTurn = Math.Clamp(targetTurn, 1, 20);
        int safeSimulations = Math.Clamp(simulations, 100, 10_000);
        DeckIntentResult intentResult = DeckIntentText.Extract(workspace.Description, workspace.Id);
        DeckIntent? intent = intentResult.Intent;
        ResolvedSimulationProfile profileResolution = (simulationProfiles ?? SimulationProfileCatalog.CreateDefault())
            .Resolve(workspace, requestedProfile, intent);
        CommandZonePlan commandZonePlan = CommandZonePlanner.Build(
            DeckServiceHelpers.IncludedCards(workspace),
            profileResolution.Profile);
        CommanderSpecificSimulationRules commanderRules = CommanderSpecificSimulationRules.Build(
            DeckServiceHelpers.IncludedCards(workspace));
        List<GoldfishRun> runs = [];
        for (int index = 0; index < safeSimulations; index++)
        {
            runs.Add(RunGoldfishGame(
                workspace,
                safeTurn,
                seed + index,
                mulligan,
                profileResolution,
                commandZonePlan,
                commanderRules));
        }

        GoldfishSimulationResult result = new()
        {
            WorkspaceId = workspace.Id,
            ModelLabel = GoldfishModelLabel,
            RngKind = GoldfishRngKind,
            Simulations = safeSimulations,
            TargetTurn = safeTurn,
            Mulligans = runs.Count(run => run.Mulliganed),
            ProfileResolution = profileResolution,
            CommandZone = BuildCommandZonePerformance(runs, safeTurn, commandZonePlan),
            WinEstimate = BuildWinEstimate(workspace, runs, safeTurn)
        };
        for (int turn = 1; turn <= safeTurn; turn++)
        {
            result.TurnSummaries.Add(BuildProjectedTurnState(turn, runs));
        }

        AddGoldfishSummaryMetrics(result, runs, safeTurn);

        IEnumerable<GoldfishRun> representativeCandidates = runs;
        if (commandZonePlan.HasBackgroundPair && runs.Any(run => run.CommanderWithBackgroundOnlineTurn.HasValue))
        {
            representativeCandidates = runs.Where(run => run.CommanderWithBackgroundOnlineTurn.HasValue);
        }
        else if (commandZonePlan.HasCommander && runs.Any(run => run.CommanderCastTurn.HasValue))
        {
            representativeCandidates = runs.Where(run => run.CommanderCastTurn.HasValue);
        }

        GoldfishRun representative = representativeCandidates
            .OrderBy(run => Math.Abs((run.WinTurn ?? safeTurn + 4) - (result.WinEstimate.MedianObservedWinTurn ?? safeTurn + 4)))
            .First();
        result.RepresentativeLines = representative.Line.Take(16).ToList();
        result.Notes.Add("Goldfish projection assumes no opponent interaction and uses role/tag heuristics rather than a full Magic rules engine.");
        result.Notes.Add("RNG kind mtgmcp-splitmix64-v1: results use the stable deterministic random source shared with Stats Lab.");
        result.Notes.Add(
            "Model label optimistic-goldfish-model: this tool projects board development and fallback win pressure, "
                + "so commander timing can differ from deck_analyze_performance's strict-sequencing-model scenarios.");
        result.Notes.Add("Commander is treated as available from the command zone when the deck has a Commander category.");
        result.Notes.Add($"Resolved simulation profile '{profileResolution.Profile.Id}' from {profileResolution.Source}.");
        result.Notes.AddRange(commanderRules.Assumptions);
        result.WinEstimate.Notes.AddRange(commanderRules.Assumptions);
        foreach (ProjectedTurnState summary in result.TurnSummaries)
        {
            summary.Notes.AddRange(commanderRules.Assumptions);
        }

        if (BuildPartialCommanderDeckWarning(workspace) is string partialDeckWarning)
        {
            result.Warnings.Add(partialDeckWarning);
        }

        result.Warnings.AddRange(profileResolution.Warnings);
        return result;
    }
}
