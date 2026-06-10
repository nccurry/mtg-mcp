namespace MtgMcp.Core;

/// <summary>
/// Captures deterministic performance simulation results for one deck workspace.
/// </summary>
public sealed class DeckPerformanceAnalysis
{
    /// <summary>
    /// Gets or sets the analyzed workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulation model label shown to MCP clients.
    /// </summary>
    public string ModelLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the version of the JSON-compatible performance result shape.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets the deterministic Stats Lab behavior version.
    /// </summary>
    public string ModelVersion { get; set; } = "";

    /// <summary>
    /// Gets or sets the fingerprint of deck construction inputs sampled by the simulator.
    /// </summary>
    public string DeckFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the fingerprint of cached card facts used by the simulator.
    /// </summary>
    public string CardDataFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the fingerprint of the resolved simulation profile.
    /// </summary>
    public string ProfileFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the deterministic random source label used for replay.
    /// </summary>
    public string RngKind { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulation profile name.
    /// </summary>
    public string Profile { get; set; } = SimulationProfileIds.Neutral;

    /// <summary>
    /// Gets or sets the resolved simulation profile and why it was selected.
    /// </summary>
    public ResolvedSimulationProfile ProfileResolution { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of Monte Carlo runs used.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the final simulated turn.
    /// </summary>
    public int MaxTurn { get; set; }

    /// <summary>
    /// Gets or sets the random seed used for deterministic replay.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// Gets or sets whether London mulligan heuristics were applied.
    /// </summary>
    public bool IncludeMulligans { get; set; }

    /// <summary>
    /// Gets or sets the included deck size used by the simulator.
    /// </summary>
    public int DeckSize { get; set; }

    /// <summary>
    /// Gets or sets opening hand quality and mulligan metrics.
    /// </summary>
    public OpeningHandPerformance OpeningHands { get; set; } = new();

    /// <summary>
    /// Gets or sets named probability metrics by turn.
    /// </summary>
    public List<PerformanceProbability> TurnProbabilities { get; set; } = [];

    /// <summary>
    /// Gets or sets named average metrics by turn.
    /// </summary>
    public List<PerformanceAverage> TurnAverages { get; set; } = [];

    /// <summary>
    /// Gets or sets castability and color-source reliability metrics.
    /// </summary>
    public CastabilityPerformance Castability { get; set; } = new();

    /// <summary>
    /// Gets or sets commander timing and protection metrics.
    /// </summary>
    public CommanderPerformance Commander { get; set; } = new();

    /// <summary>
    /// Gets or sets command-zone timing metrics for commander, Background, and combined online states.
    /// </summary>
    public CommandZonePerformance CommandZone { get; set; } = new();

    /// <summary>
    /// Gets or sets combo and tutor assembly metrics.
    /// </summary>
    public ComboAssemblyPerformance ComboAssembly { get; set; } = new();

    /// <summary>
    /// Gets or sets cards most often stranded by mana value or color access.
    /// </summary>
    public List<StrandedCardPerformance> StrandedCards { get; set; } = [];

    /// <summary>
    /// Gets or sets derived metric dimensions for scanning performance strengths and risks.
    /// </summary>
    public PerformanceScorecard Scorecard { get; set; } = new();

    /// <summary>
    /// Gets or sets bounded deterministic trace summaries for sampled simulation runs.
    /// </summary>
    public PerformanceTraceSummary TraceSummary { get; set; } = new();

    /// <summary>
    /// Gets or sets named deckbuilder scenarios evaluated from the runs.
    /// </summary>
    public List<ScenarioPerformance> Scenarios { get; set; } = [];

    /// <summary>
    /// Gets or sets simplifying assumptions behind the analysis.
    /// </summary>
    public List<string> Assumptions { get; set; } = [];

    /// <summary>
    /// Gets or sets warnings about low data quality or simulator limits.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Compares performance before and after a persisted deck edit plan.
/// </summary>
public sealed class DeckPerformanceComparison
{
    /// <summary>
    /// Gets or sets the compared plan id.
    /// </summary>
    public string PlanId { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace id that owns the plan.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the baseline deck performance.
    /// </summary>
    public DeckPerformanceAnalysis Before { get; set; } = new();

    /// <summary>
    /// Gets or sets the preview deck performance after applying plan operations in memory.
    /// </summary>
    public DeckPerformanceAnalysis After { get; set; } = new();

    /// <summary>
    /// Gets or sets headline metric deltas from before to after.
    /// </summary>
    public List<PerformanceDelta> Deltas { get; set; } = [];

    /// <summary>
    /// Gets or sets preview or analysis warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Summarizes derived performance dimensions without producing a single power score.
/// </summary>
public sealed class PerformanceScorecard
{
    /// <summary>
    /// Gets or sets named scorecard dimensions.
    /// </summary>
    public List<PerformanceScorecardDimension> Dimensions { get; set; } = [];
}

/// <summary>
/// Describes one derived performance dimension.
/// </summary>
public sealed class PerformanceScorecardDimension
{
    /// <summary>
    /// Gets or sets the stable dimension name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized score from 0 to 1.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the main metric or scenario that produced the score.
    /// </summary>
    public string SourceMetric { get; set; } = "";

    /// <summary>
    /// Gets or sets a compact explanation of what the dimension means.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Provides bounded replay context without returning every simulated action.
/// </summary>
public sealed class PerformanceTraceSummary
{
    /// <summary>
    /// Gets or sets aggregate counters across all simulated runs.
    /// </summary>
    public Dictionary<string, int> AggregateCounters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets deterministic sampled run summaries.
    /// </summary>
    public List<PerformanceTraceRunSummary> SampledRuns { get; set; } = [];

    /// <summary>
    /// Gets or sets trace summary notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Summarizes one sampled run without exposing a full play log.
/// </summary>
public sealed class PerformanceTraceRunSummary
{
    /// <summary>
    /// Gets or sets the zero-based run index.
    /// </summary>
    public int RunIndex { get; set; }

    /// <summary>
    /// Gets or sets the per-run seed used for deterministic replay.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// Gets or sets the number of mulligans taken.
    /// </summary>
    public int Mulligans { get; set; }

    /// <summary>
    /// Gets or sets the kept hand size.
    /// </summary>
    public int KeptHandSize { get; set; }

    /// <summary>
    /// Gets or sets lands in the kept opening hand.
    /// </summary>
    public int KeptOpeningLands { get; set; }

    /// <summary>
    /// Gets or sets land drops made before the simulated horizon.
    /// </summary>
    public int LandDropsMade { get; set; }

    /// <summary>
    /// Gets or sets the earliest commander cast turn when observed.
    /// </summary>
    public int? CommanderCastTurn { get; set; }

    /// <summary>
    /// Gets or sets the earliest Background cast turn when observed.
    /// </summary>
    public int? BackgroundCastTurn { get; set; }

    /// <summary>
    /// Gets or sets the earliest combo assembly turn when observed.
    /// </summary>
    public int? ComboAssemblyTurn { get; set; }

    /// <summary>
    /// Gets or sets the earliest tutor-assisted combo turn when observed.
    /// </summary>
    public int? TutorAssistedComboTurn { get; set; }

    /// <summary>
    /// Gets or sets the number of cards stranded at the final turn.
    /// </summary>
    public int StrandedCardCount { get; set; }

    /// <summary>
    /// Gets or sets turns where interaction remained held up.
    /// </summary>
    public List<int> InteractionHeldUpTurns { get; set; } = [];

    /// <summary>
    /// Gets or sets bounded decision events explaining sampled mulligan and sequencing choices.
    /// </summary>
    public List<PerformanceDecisionEvent> DecisionEvents { get; set; } = [];
}

/// <summary>
/// Describes one compact simulator decision for sampled performance traces.
/// </summary>
public sealed class PerformanceDecisionEvent
{
    /// <summary>
    /// Gets or sets the decision phase, such as mulligan, sequencing, hold-up, or route-check.
    /// </summary>
    public string Phase { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulated turn when the decision happened, when applicable.
    /// </summary>
    public int? Turn { get; set; }

    /// <summary>
    /// Gets or sets the specific choice being explained.
    /// </summary>
    public string Decision { get; set; } = "";

    /// <summary>
    /// Gets or sets the chosen outcome, such as keep, bottom, cast, hold, skip, matched, or missing.
    /// </summary>
    public string Outcome { get; set; } = "";

    /// <summary>
    /// Gets or sets the card, route, or simulator object the choice concerned.
    /// </summary>
    public string Subject { get; set; } = "";

    /// <summary>
    /// Gets or sets the concise reason the simulator made the choice.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets bounded supporting facts for the decision.
    /// </summary>
    public List<string> Evidence { get; set; } = [];
}

/// <summary>
/// Describes opening hand and mulligan quality.
/// </summary>
public sealed class OpeningHandPerformance
{
    /// <summary>
    /// Gets or sets the rate of seven-card hands kept by the heuristic.
    /// </summary>
    public double SevenCardKeepRate { get; set; }

    /// <summary>
    /// Gets or sets average mulligans taken per run.
    /// </summary>
    public double AverageMulligans { get; set; }

    /// <summary>
    /// Gets or sets average card count in kept opening hands.
    /// </summary>
    public double AverageKeptHandSize { get; set; }

    /// <summary>
    /// Gets or sets average land count in kept opening hands.
    /// </summary>
    public double AverageKeptLands { get; set; }

    /// <summary>
    /// Gets or sets the rate of no-land opening hands before mulligans.
    /// </summary>
    public double NoLandSevenRate { get; set; }

    /// <summary>
    /// Gets or sets the rate of one-land opening hands before mulligans.
    /// </summary>
    public double OneLandSevenRate { get; set; }

    /// <summary>
    /// Gets or sets the rate of six-or-more-land opening hands before mulligans.
    /// </summary>
    public double FloodedSevenRate { get; set; }

    /// <summary>
    /// Gets or sets mulligan counts keyed by mulligans taken.
    /// </summary>
    public Dictionary<int, int> MulliganDistribution { get; set; } = [];
}

/// <summary>
/// Represents a named probability estimate with a confidence interval.
/// </summary>
public sealed class PerformanceProbability
{
    /// <summary>
    /// Gets or sets the metric name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the turn associated with the metric.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets the observed probability.
    /// </summary>
    public double Probability { get; set; }

    /// <summary>
    /// Gets or sets the lower bound of the approximate 95 percent interval.
    /// </summary>
    public double LowConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the upper bound of the approximate 95 percent interval.
    /// </summary>
    public double HighConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the sample size used for the estimate.
    /// </summary>
    public int SampleSize { get; set; }
}

/// <summary>
/// Represents a named average estimate by turn.
/// </summary>
public sealed class PerformanceAverage
{
    /// <summary>
    /// Gets or sets the metric name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the turn associated with the metric.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets the arithmetic mean.
    /// </summary>
    public double Average { get; set; }

    /// <summary>
    /// Gets or sets the 25th percentile.
    /// </summary>
    public double P25 { get; set; }

    /// <summary>
    /// Gets or sets the median.
    /// </summary>
    public double P50 { get; set; }

    /// <summary>
    /// Gets or sets the 75th percentile.
    /// </summary>
    public double P75 { get; set; }

    /// <summary>
    /// Gets or sets the sample size used for the estimate.
    /// </summary>
    public int SampleSize { get; set; }
}

/// <summary>
/// Summarizes spell castability and color source reliability.
/// </summary>
public sealed class CastabilityPerformance
{
    /// <summary>
    /// Gets or sets the average share of nonland cards in hand that are castable by turn.
    /// </summary>
    public List<PerformanceAverage> SpellCastabilityByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets the probability of producing each deck color by turn.
    /// </summary>
    public List<PerformanceProbability> ColorSourceReliability { get; set; } = [];
}

/// <summary>
/// Summarizes commander cast and protection timing.
/// </summary>
public sealed class CommanderPerformance
{
    /// <summary>
    /// Gets or sets commander names detected in the workspace.
    /// </summary>
    public List<string> CommanderNames { get; set; } = [];

    /// <summary>
    /// Gets or sets commander cast probability by turn.
    /// </summary>
    public List<PerformanceProbability> CastByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets commander-plus-protection probability by turn.
    /// </summary>
    public List<PerformanceProbability> ProtectedByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets the average earliest commander cast turn for successful runs.
    /// </summary>
    public double? AverageEarliestCastTurn { get; set; }
}

/// <summary>
/// Summarizes command-zone deployment timing.
/// </summary>
public sealed class CommandZonePerformance
{
    /// <summary>
    /// Gets or sets command-zone card names detected in the workspace.
    /// </summary>
    public List<string> CommandZoneNames { get; set; } = [];

    /// <summary>
    /// Gets or sets non-Background commander names detected in the workspace.
    /// </summary>
    public List<string> CommanderNames { get; set; } = [];

    /// <summary>
    /// Gets or sets Background names detected in the workspace.
    /// </summary>
    public List<string> BackgroundNames { get; set; } = [];

    /// <summary>
    /// Gets or sets non-Background commander cast probability by turn.
    /// </summary>
    public List<PerformanceProbability> CommanderCastByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets Background cast probability by turn.
    /// </summary>
    public List<PerformanceProbability> BackgroundCastByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets commander-plus-Background-online probability by turn.
    /// </summary>
    public List<PerformanceProbability> CommanderWithBackgroundOnlineByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets the average earliest non-Background commander cast turn for successful runs.
    /// </summary>
    public double? AverageCommanderCastTurn { get; set; }

    /// <summary>
    /// Gets or sets the average earliest Background cast turn for successful runs.
    /// </summary>
    public double? AverageBackgroundCastTurn { get; set; }

    /// <summary>
    /// Gets or sets the average earliest turn where commander and Background were both online.
    /// </summary>
    public double? AverageCommanderWithBackgroundOnlineTurn { get; set; }
}

/// <summary>
/// Summarizes combo piece and tutor-assisted assembly timing.
/// </summary>
public sealed class ComboAssemblyPerformance
{
    /// <summary>
    /// Gets or sets cards treated as combo pieces, enablers, tutors, or finishers.
    /// </summary>
    public List<string> RelevantCards { get; set; } = [];

    /// <summary>
    /// Gets or sets two-piece combo assembly probability by turn.
    /// </summary>
    public List<PerformanceProbability> AssemblyByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets tutor-assisted assembly probability by turn.
    /// </summary>
    public List<PerformanceProbability> TutorAssistedAssemblyByTurn { get; set; } = [];

    /// <summary>
    /// Gets or sets the average earliest combo assembly turn for successful runs.
    /// </summary>
    public double? AverageEarliestAssemblyTurn { get; set; }
}

/// <summary>
/// Describes how often a card remained uncastable by the final simulated turn.
/// </summary>
public sealed class StrandedCardPerformance
{
    /// <summary>
    /// Gets or sets the stranded card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card's cached mana value.
    /// </summary>
    public double ManaValue { get; set; }

    /// <summary>
    /// Gets or sets the overall stranded rate.
    /// </summary>
    public double StrandedRate { get; set; }

    /// <summary>
    /// Gets or sets the stranded rate caused by insufficient mana.
    /// </summary>
    public double ManaStrandedRate { get; set; }

    /// <summary>
    /// Gets or sets the stranded rate caused by missing colors.
    /// </summary>
    public double ColorStrandedRate { get; set; }

    /// <summary>
    /// Gets or sets the number of simulation runs.
    /// </summary>
    public int SampleSize { get; set; }
}

/// <summary>
/// Describes a named deckbuilder scenario evaluated from simulation runs.
/// </summary>
public sealed class ScenarioPerformance
{
    /// <summary>
    /// Gets or sets the scenario name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the scenario target turn.
    /// </summary>
    public int TargetTurn { get; set; }

    /// <summary>
    /// Gets or sets the observed success or risk rate.
    /// </summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// Gets or sets the lower bound of the approximate 95 percent interval.
    /// </summary>
    public double LowConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the upper bound of the approximate 95 percent interval.
    /// </summary>
    public double HighConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the sample size used for the estimate.
    /// </summary>
    public int SampleSize { get; set; }

    /// <summary>
    /// Gets or sets cards most relevant to this scenario.
    /// </summary>
    public List<string> RelevantCards { get; set; } = [];

    /// <summary>
    /// Gets or sets common causes of scenario failure or risk.
    /// </summary>
    public List<string> FailureDrivers { get; set; } = [];

    /// <summary>
    /// Gets or sets observed failure driver counts from simulation runs.
    /// </summary>
    public Dictionary<string, int> FailureDriverCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets scenario-specific assumptions.
    /// </summary>
    public List<string> Assumptions { get; set; } = [];
}

/// <summary>
/// Represents one before/after metric delta.
/// </summary>
public sealed class PerformanceDelta
{
    /// <summary>
    /// Gets or sets the compared metric name.
    /// </summary>
    public string Metric { get; set; } = "";

    /// <summary>
    /// Gets or sets the baseline value.
    /// </summary>
    public double Before { get; set; }

    /// <summary>
    /// Gets or sets the post-plan value.
    /// </summary>
    public double After { get; set; }

    /// <summary>
    /// Gets or sets the post-plan minus baseline delta.
    /// </summary>
    public double Delta { get; set; }

    /// <summary>
    /// Gets or sets the baseline lower confidence bound when available.
    /// </summary>
    public double? BeforeLowConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the baseline upper confidence bound when available.
    /// </summary>
    public double? BeforeHighConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the post-plan lower confidence bound when available.
    /// </summary>
    public double? AfterLowConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the post-plan upper confidence bound when available.
    /// </summary>
    public double? AfterHighConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets whether confidence intervals overlap.
    /// </summary>
    public bool? ConfidenceIntervalsOverlap { get; set; }
}
