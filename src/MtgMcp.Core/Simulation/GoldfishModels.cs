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
    /// Gets or sets the number of simulations.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the median win turn.
    /// </summary>
    public int? MedianWinTurn { get; set; }

    /// <summary>
    /// Gets or sets the twenty-fifth percentile win turn.
    /// </summary>
    public int? P25WinTurn { get; set; }

    /// <summary>
    /// Gets or sets the seventy-fifth percentile win turn.
    /// </summary>
    public int? P75WinTurn { get; set; }

    /// <summary>
    /// Gets or sets cumulative win rates by turn.
    /// </summary>
    public Dictionary<int, double> WinByTurnRates { get; set; } = [];

    /// <summary>
    /// Gets or sets likely routes to victory.
    /// </summary>
    public List<WinRoute> Routes { get; set; } = [];

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
}
