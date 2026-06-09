using System.Text.Json.Serialization;

namespace MtgMcp.Core;

/// <summary>
/// Describes a goldfish win route.
/// </summary>
public sealed class WinRoute
{
    /// <summary>
    /// Gets or sets the route name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the route kind.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets the earliest likely turn.
    /// </summary>
    public int? EarliestTurn { get; set; }

    /// <summary>
    /// Gets or sets the route probability.
    /// </summary>
    public double Probability { get; set; }

    /// <summary>
    /// Gets or sets cards associated with the route.
    /// </summary>
    public List<string> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets the route rationale.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets deterministic route evidence captured during simulation.
    /// </summary>
    public List<SimulationRouteEvidence> Evidence { get; set; } = [];
}

/// <summary>
/// Reports likely win timing for a deck.
/// </summary>
public sealed class WinTurnEstimate
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulation model label shown to MCP clients.
    /// </summary>
    public string ModelLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of simulations.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the number of runs that reached a heuristic win by the maximum simulated turn.
    /// </summary>
    public int ObservedWins { get; set; }

    /// <summary>
    /// Gets or sets the fraction of all runs that reached a heuristic win.
    /// </summary>
    public double ObservedWinRate { get; set; }

    /// <summary>
    /// Gets or sets the median win turn among only the runs that reached a heuristic win.
    /// </summary>
    public int? MedianObservedWinTurn { get; set; }

    /// <summary>
    /// Gets or sets the twenty-fifth percentile win turn among only the runs that reached a heuristic win.
    /// </summary>
    public int? P25ObservedWinTurn { get; set; }

    /// <summary>
    /// Gets or sets the seventy-fifth percentile win turn among only the runs that reached a heuristic win.
    /// </summary>
    public int? P75ObservedWinTurn { get; set; }

    /// <summary>
    /// Gets or sets the legacy median observed win turn alias used by older in-process callers.
    /// </summary>
    [JsonIgnore]
    public int? MedianWinTurn { get => MedianObservedWinTurn; set => MedianObservedWinTurn = value; }

    /// <summary>
    /// Gets or sets the legacy twenty-fifth percentile observed win turn alias used by older in-process callers.
    /// </summary>
    [JsonIgnore]
    public int? P25WinTurn { get => P25ObservedWinTurn; set => P25ObservedWinTurn = value; }

    /// <summary>
    /// Gets or sets the legacy seventy-fifth percentile observed win turn alias used by older in-process callers.
    /// </summary>
    [JsonIgnore]
    public int? P75WinTurn { get => P75ObservedWinTurn; set => P75ObservedWinTurn = value; }

    /// <summary>
    /// Gets or sets cumulative win rates by turn.
    /// </summary>
    public Dictionary<int, double> WinByTurnRates { get; set; } = [];

    /// <summary>
    /// Gets or sets likely routes to victory.
    /// </summary>
    public List<WinRoute> Routes { get; set; } = [];

    /// <summary>
    /// Gets or sets representative route evidence across observed wins.
    /// </summary>
    public List<SimulationRouteEvidence> RouteEvidence { get; set; } = [];

    /// <summary>
    /// Gets or sets win estimate notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports a projected board state for a turn.
/// </summary>
public sealed class ProjectedTurnState
{
    /// <summary>
    /// Gets or sets the turn number.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets the projection model label shown to MCP clients.
    /// </summary>
    public string ModelLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the median lands on the battlefield.
    /// </summary>
    public int MedianLands { get; set; }

    /// <summary>
    /// Gets or sets the median total mana sources.
    /// </summary>
    public int MedianManaSources { get; set; }

    /// <summary>
    /// Gets or sets the median nonland permanent count.
    /// </summary>
    public int MedianNonlandPermanents { get; set; }

    /// <summary>
    /// Gets or sets the median cards in hand.
    /// </summary>
    public int MedianCardsInHand { get; set; }

    /// <summary>
    /// Gets or sets the median battlefield power.
    /// </summary>
    public int MedianPower { get; set; }

    /// <summary>
    /// Gets or sets the median token count.
    /// </summary>
    public int MedianTokens { get; set; }

    /// <summary>
    /// Gets or sets a readable board summary.
    /// </summary>
    public string LikelyBoard { get; set; } = "";

    /// <summary>
    /// Gets or sets confidence in this projection.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets notes explaining what this board projection does and does not model.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports a goldfish simulation.
/// </summary>
public sealed class GoldfishSimulationResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulation model label shown to MCP clients.
    /// </summary>
    public string ModelLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of simulations.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the target turn.
    /// </summary>
    public int TargetTurn { get; set; }

    /// <summary>
    /// Gets or sets the simple mulligan count.
    /// </summary>
    public int Mulligans { get; set; }

    /// <summary>
    /// Gets or sets the resolved simulation profile and selection evidence.
    /// </summary>
    public ResolvedSimulationProfile ProfileResolution { get; set; } = new();

    /// <summary>
    /// Gets or sets command-zone deployment timing metrics.
    /// </summary>
    public CommandZonePerformance CommandZone { get; set; } = new();

    /// <summary>
    /// Gets or sets turn-by-turn projections.
    /// </summary>
    public List<ProjectedTurnState> TurnSummaries { get; set; } = [];

    /// <summary>
    /// Gets or sets the win timing estimate.
    /// </summary>
    public WinTurnEstimate WinEstimate { get; set; } = new();

    /// <summary>
    /// Gets or sets representative play lines.
    /// </summary>
    public List<string> RepresentativeLines { get; set; } = [];

    /// <summary>
    /// Gets or sets simulator notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal simulator warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Reports deterministic goldfish results for one compared deck.
/// </summary>
public sealed class GoldfishDeckComparison
{
    /// <summary>
    /// Gets or sets the stable comparison label, such as active or reference-1.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the deck came from the active workspace or an Archidekt import.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the caller-supplied Archidekt id or URL when this is a reference deck.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Gets or sets the compared workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the compared deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the Archidekt deck id when known.
    /// </summary>
    public string? ArchidektDeckId { get; set; }

    /// <summary>
    /// Gets or sets the count of included cards considered by the simulator.
    /// </summary>
    public int IncludedCards { get; set; }

    /// <summary>
    /// Gets or sets the raw goldfish result for this deck.
    /// </summary>
    public GoldfishSimulationResult Goldfish { get; set; } = new();

    /// <summary>
    /// Gets or sets arithmetic deltas from the active deck when this is a reference deck.
    /// </summary>
    public GoldfishComparisonDelta? DeltaFromActive { get; set; }
}

/// <summary>
/// Reports a reference deck that could not be imported for goldfish comparison.
/// </summary>
public sealed class GoldfishReferenceImportFailure
{
    /// <summary>
    /// Gets or sets the stable comparison label, such as reference-1.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the caller-supplied reference deck id or URL.
    /// </summary>
    public string Input { get; set; } = "";

    /// <summary>
    /// Gets or sets the detected reference source.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the deterministic reason this reference was not simulated.
    /// </summary>
    public string Reason { get; set; } = "";
}

/// <summary>
/// Reports arithmetic goldfish deltas between a reference deck and the active deck.
/// </summary>
public sealed class GoldfishComparisonDelta
{
    /// <summary>
    /// Gets or sets the active workspace id used as the baseline.
    /// </summary>
    public string BaselineWorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the reference workspace id compared to the baseline.
    /// </summary>
    public string ReferenceWorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the turn used for turn-summary deltas.
    /// </summary>
    public int TargetTurn { get; set; }

    /// <summary>
    /// Gets or sets reference median observed win turn minus active median observed win turn.
    /// </summary>
    public int? MedianObservedWinTurnDelta { get; set; }

    /// <summary>
    /// Gets or sets the legacy median observed win turn delta alias used by older in-process callers.
    /// </summary>
    [JsonIgnore]
    public int? MedianWinTurnDelta
    {
        get => MedianObservedWinTurnDelta;
        set => MedianObservedWinTurnDelta = value;
    }

    /// <summary>
    /// Gets or sets reference cumulative win rate minus active cumulative win rate at the target turn.
    /// </summary>
    public double TargetTurnWinRateDelta { get; set; }

    /// <summary>
    /// Gets or sets reference mulligan rate minus active mulligan rate.
    /// </summary>
    public double MulliganRateDelta { get; set; }

    /// <summary>
    /// Gets or sets reference median land count minus active median land count at the target turn.
    /// </summary>
    public int MedianLandsDelta { get; set; }

    /// <summary>
    /// Gets or sets reference median mana sources minus active median mana sources at the target turn.
    /// </summary>
    public int MedianManaSourcesDelta { get; set; }

    /// <summary>
    /// Gets or sets reference median nonland permanents minus active median nonland permanents at the target turn.
    /// </summary>
    public int MedianNonlandPermanentsDelta { get; set; }

    /// <summary>
    /// Gets or sets reference median cards in hand minus active median cards in hand at the target turn.
    /// </summary>
    public int MedianCardsInHandDelta { get; set; }

    /// <summary>
    /// Gets or sets reference median token count minus active median token count at the target turn.
    /// </summary>
    public int MedianTokensDelta { get; set; }
}

/// <summary>
/// Reports a generalized goldfish comparison across local and read-only imported decks.
/// </summary>
public sealed class DeckGoldfishComparisonResult
{
    /// <summary>
    /// Workspace id used as the comparison baseline.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Target turn used for every compared deck.
    /// </summary>
    public int TargetTurn { get; set; }

    /// <summary>
    /// Simulation count used for every compared deck.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Shared random seed used for every compared deck.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// True when simple mulligans were enabled.
    /// </summary>
    public bool Mulligan { get; set; }

    /// <summary>
    /// Baseline workspace goldfish result.
    /// </summary>
    public GoldfishDeckComparison BaselineDeck { get; set; } = new();

    /// <summary>
    /// Comparison rows for each successfully simulated non-baseline deck.
    /// </summary>
    public List<GoldfishDeckComparison> ComparedDecks { get; set; } = [];

    /// <summary>
    /// Inputs that could not be loaded or simulated without aborting the comparison.
    /// </summary>
    public List<GoldfishReferenceImportFailure> Failures { get; set; } = [];

    /// <summary>
    /// Deterministic comparison notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>
    /// Non-fatal comparison warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Reports an active deck goldfish comparison against caller-supplied Archidekt reference decks.
/// </summary>
public sealed class ArchidektGoldfishComparisonResult
{
    /// <summary>
    /// Gets or sets the active workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the projected target turn.
    /// </summary>
    public int TargetTurn { get; set; }

    /// <summary>
    /// Gets or sets the number of simulations actually run for each deck.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the shared random seed used for every compared deck.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// Gets or sets whether simple mulligans were enabled.
    /// </summary>
    public bool Mulligan { get; set; }

    /// <summary>
    /// Gets or sets the active workspace goldfish result.
    /// </summary>
    public GoldfishDeckComparison ActiveDeck { get; set; } = new();

    /// <summary>
    /// Gets or sets goldfish results for the Archidekt reference decks in caller order.
    /// </summary>
    public List<GoldfishDeckComparison> ReferenceDecks { get; set; } = [];

    /// <summary>
    /// Gets or sets references that could not be imported or simulated.
    /// </summary>
    public List<GoldfishReferenceImportFailure> ReferenceFailures { get; set; } = [];

    /// <summary>
    /// Gets or sets deterministic comparison notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal comparison warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}
