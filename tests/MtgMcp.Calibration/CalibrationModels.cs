using System.Text.Json.Serialization;
using MtgMcp.Core;

namespace MtgMcp.Calibration;

/// <summary>
/// Reports one complete offline Stats Lab calibration run.
/// </summary>
public sealed class StatsLabCalibrationReport
{
    /// <summary>
    /// Gets or sets the calibration contract version.
    /// </summary>
    public int SchemaVersion { get; set; } = 6;

    /// <summary>
    /// Gets or sets the deterministic run settings used for every fixture.
    /// </summary>
    public CalibrationRunSettings Settings { get; set; } = new();

    /// <summary>
    /// Gets or sets aggregate pass/fail counts.
    /// </summary>
    public CalibrationSummary Summary { get; set; } = new();

    /// <summary>
    /// Gets or sets fixture-level analysis summaries.
    /// </summary>
    public List<CalibrationFixtureResult> Fixtures { get; set; } = [];

    /// <summary>
    /// Gets or sets pairwise expectation outcomes.
    /// </summary>
    public List<CalibrationExpectationResult> Expectations { get; set; } = [];

    /// <summary>
    /// Gets or sets opponent-pressure diagnostic outcomes.
    /// </summary>
    public List<CalibrationPressureDiagnosticResult> PressureDiagnostics { get; set; } = [];

    /// <summary>
    /// Gets or sets Commander bracket benchmark diagnostics.
    /// </summary>
    public List<CalibrationBracketDiagnosticResult> BracketDiagnostics { get; set; } = [];

    /// <summary>
    /// Gets or sets optional alternate-profile analyses for calibration diagnostics.
    /// </summary>
    public List<CalibrationProfileSweepResult> ProfileSweeps { get; set; } = [];

    /// <summary>
    /// Gets or sets profile-sensitive expectation diagnostics.
    /// </summary>
    public List<CalibrationProfileSensitivityResult> ProfileSensitivity { get; set; } = [];

    /// <summary>
    /// Gets or sets saved-baseline drift outcomes.
    /// </summary>
    public List<CalibrationDriftResult> Drift { get; set; } = [];

    /// <summary>
    /// Gets or sets report-level notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Captures deterministic settings shared by all fixtures in one calibration run.
/// </summary>
public sealed class CalibrationRunSettings
{
    /// <summary>
    /// Gets or sets the requested simulation count.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the requested maximum turn.
    /// </summary>
    public int MaxTurn { get; set; }

    /// <summary>
    /// Gets or sets the shared random seed.
    /// </summary>
    public int Seed { get; set; }

    /// <summary>
    /// Gets or sets whether mulligans were included.
    /// </summary>
    public bool IncludeMulligans { get; set; }

    /// <summary>
    /// Gets or sets the optional baseline path used for drift checks.
    /// </summary>
    public string BaselinePath { get; set; } = "";

    /// <summary>
    /// Gets or sets the corpus path used for benchmark fixtures.
    /// </summary>
    public string CorpusPath { get; set; } = "";

    /// <summary>
    /// Gets or sets whether only built-in synthetic fixtures were used.
    /// </summary>
    public bool SyntheticOnly { get; set; }

    /// <summary>
    /// Gets or sets profile ids requested for sensitivity sweeps.
    /// </summary>
    public List<string> ProfileSweepIds { get; set; } = [];
}

/// <summary>
/// Counts calibration fixtures, expectation checks, and drift failures.
/// </summary>
public sealed class CalibrationSummary
{
    /// <summary>
    /// Gets or sets the number of fixture analyses.
    /// </summary>
    public int FixtureCount { get; set; }

    /// <summary>
    /// Gets or sets the number of pairwise expectations.
    /// </summary>
    public int ExpectationCount { get; set; }

    /// <summary>
    /// Gets or sets the number of passing pairwise expectations.
    /// </summary>
    public int PassedExpectations { get; set; }

    /// <summary>
    /// Gets or sets the number of failing pairwise expectations.
    /// </summary>
    public int FailedExpectations { get; set; }

    /// <summary>
    /// Gets or sets the number of required expectations.
    /// </summary>
    public int RequiredExpectationCount { get; set; }

    /// <summary>
    /// Gets or sets the number of passing required expectations.
    /// </summary>
    public int PassedRequiredExpectations { get; set; }

    /// <summary>
    /// Gets or sets the number of failing required expectations.
    /// </summary>
    public int FailedRequiredExpectations { get; set; }

    /// <summary>
    /// Gets or sets the number of advisory expectations.
    /// </summary>
    public int AdvisoryExpectationCount { get; set; }

    /// <summary>
    /// Gets or sets the number of passing advisory expectations.
    /// </summary>
    public int PassedAdvisoryExpectations { get; set; }

    /// <summary>
    /// Gets or sets the number of failing advisory expectations.
    /// </summary>
    public int FailedAdvisoryExpectations { get; set; }

    /// <summary>
    /// Gets or sets the number of expectations close to their threshold.
    /// </summary>
    public int NearMissExpectations { get; set; }

    /// <summary>
    /// Gets or sets the number of profile-sweep fixture analyses.
    /// </summary>
    public int ProfileSweepCount { get; set; }

    /// <summary>
    /// Gets or sets the number of profile sensitivity diagnostics.
    /// </summary>
    public int ProfileSensitivityCount { get; set; }

    /// <summary>
    /// Gets or sets the number of pressure diagnostic checks.
    /// </summary>
    public int PressureDiagnosticCount { get; set; }

    /// <summary>
    /// Gets or sets the number of bracket benchmark diagnostics.
    /// </summary>
    public int BracketDiagnosticCount { get; set; }

    /// <summary>
    /// Gets or sets the number of drift checks that exceeded tolerance.
    /// </summary>
    public int DriftFailures { get; set; }

    /// <summary>
    /// Gets or sets the Stats Lab model version observed in analyzed fixtures.
    /// </summary>
    public string ModelVersion { get; set; } = "";
}

/// <summary>
/// Summarizes one analyzed fixture deck.
/// </summary>
public sealed class CalibrationFixtureResult
{
    /// <summary>
    /// Gets or sets the stable fixture id.
    /// </summary>
    public string FixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the advisory strength label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulation profile used.
    /// </summary>
    public string Profile { get; set; } = "";

    /// <summary>
    /// Gets or sets the source or construction note for the fixture.
    /// </summary>
    public string SourceNote { get; set; } = "";

    /// <summary>
    /// Gets or sets the source kind, such as synthetic, edhrec, or cedh-decklist-database.
    /// </summary>
    public string SourceKind { get; set; } = "";

    /// <summary>
    /// Gets or sets the public source URI used to construct the fixture.
    /// </summary>
    public string SourceUri { get; set; } = "";

    /// <summary>
    /// Gets or sets the date when the offline snapshot was captured.
    /// </summary>
    public string CapturedAt { get; set; } = "";

    /// <summary>
    /// Gets or sets the Stats Lab model version.
    /// </summary>
    public string ModelVersion { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck construction fingerprint.
    /// </summary>
    public string DeckFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the card-data fingerprint.
    /// </summary>
    public string CardDataFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved profile fingerprint.
    /// </summary>
    public string ProfileFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the deterministic RNG label.
    /// </summary>
    public string RngKind { get; set; } = "";

    /// <summary>
    /// Gets or sets key profile settings used to make deterministic decisions.
    /// </summary>
    public CalibrationProfileDiagnostics ProfileDiagnostics { get; set; } = new();

    /// <summary>
    /// Gets or sets the analyzed deck size.
    /// </summary>
    public int DeckSize { get; set; }

    /// <summary>
    /// Gets or sets scorecard dimensions keyed by name.
    /// </summary>
    public Dictionary<string, double> Scorecard { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets scenario values keyed by scenario name.
    /// </summary>
    public Dictionary<string, CalibrationScenarioValue> Scenarios { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets warnings surfaced by the analyzer.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Captures one scenario rate and confidence interval.
/// </summary>
public sealed class CalibrationScenarioValue
{
    /// <summary>
    /// Gets or sets the observed rate.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Gets or sets the lower confidence bound.
    /// </summary>
    public double LowConfidenceInterval { get; set; }

    /// <summary>
    /// Gets or sets the upper confidence bound.
    /// </summary>
    public double HighConfidenceInterval { get; set; }
}

/// <summary>
/// Captures key resolved profile settings that affect simulation decisions.
/// </summary>
public sealed class CalibrationProfileDiagnostics
{
    /// <summary>
    /// Gets or sets the profile resolution source.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets whether command-zone cards are preferred as soon as affordable.
    /// </summary>
    public bool PreferCommanderOnCurve { get; set; }

    /// <summary>
    /// Gets or sets the preferred commander deployment turn.
    /// </summary>
    public int? PreferredCommanderTurn { get; set; }

    /// <summary>
    /// Gets or sets the preferred Background deployment turn.
    /// </summary>
    public int? PreferredBackgroundTurn { get; set; }

    /// <summary>
    /// Gets or sets the first turn where interaction is usually held.
    /// </summary>
    public int HoldInteractionFromTurn { get; set; }

    /// <summary>
    /// Gets or sets the minimum interaction count held by the profile.
    /// </summary>
    public int MinimumInteractionHeld { get; set; }

    /// <summary>
    /// Gets or sets the early ramp priority. Lower values are cast first.
    /// </summary>
    public int EarlyRampPriority { get; set; }

    /// <summary>
    /// Gets or sets the tutor priority. Lower values are cast first.
    /// </summary>
    public int TutorPriority { get; set; }

    /// <summary>
    /// Gets or sets the combo priority. Lower values are cast first.
    /// </summary>
    public int ComboPriority { get; set; }

    /// <summary>
    /// Gets or sets the ordinary seven-card keep threshold.
    /// </summary>
    public double SevenCardKeepScore { get; set; }
}

/// <summary>
/// Reports one pairwise expectation check.
/// </summary>
public sealed class CalibrationExpectationResult
{
    /// <summary>
    /// Gets or sets the stable expectation id.
    /// </summary>
    public string ExpectationId { get; set; } = "";

    /// <summary>
    /// Gets or sets the metric key, such as scorecard:mana-stability.
    /// </summary>
    public string Metric { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the expectation is required or advisory.
    /// </summary>
    public string Severity { get; set; } = CalibrationExpectationSeverity.Required;

    /// <summary>
    /// Gets or sets tags that describe the benchmark relationship.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the preferred fixture should be higher or lower on the metric.
    /// </summary>
    public string Direction { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture expected to perform better by this metric.
    /// </summary>
    public string PreferredFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the preferred fixture's advisory label.
    /// </summary>
    public string PreferredFixtureLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the preferred fixture's resolved profile id.
    /// </summary>
    public string PreferredProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the preferred fixture's resolved profile fingerprint.
    /// </summary>
    public string PreferredProfileFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the comparison fixture.
    /// </summary>
    public string OtherFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the comparison fixture's advisory label.
    /// </summary>
    public string OtherFixtureLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the comparison fixture's resolved profile id.
    /// </summary>
    public string OtherProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the comparison fixture's resolved profile fingerprint.
    /// </summary>
    public string OtherProfileFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the preferred fixture metric value.
    /// </summary>
    public double PreferredValue { get; set; }

    /// <summary>
    /// Gets or sets the comparison fixture metric value.
    /// </summary>
    public double OtherValue { get; set; }

    /// <summary>
    /// Gets or sets the direction-adjusted metric delta.
    /// </summary>
    public double Delta { get; set; }

    /// <summary>
    /// Gets or sets the required minimum delta.
    /// </summary>
    public double MinimumDelta { get; set; }

    /// <summary>
    /// Gets or sets the direction-adjusted distance from the required threshold.
    /// </summary>
    public double MarginToThreshold { get; set; }

    /// <summary>
    /// Gets or sets whether the expectation passed.
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Gets or sets whether the result sits close to the configured threshold.
    /// </summary>
    public bool NearMiss { get; set; }

    /// <summary>
    /// Gets or sets whether scenario confidence intervals overlapped.
    /// </summary>
    public bool? ConfidenceIntervalsOverlap { get; set; }

    /// <summary>
    /// Gets or sets the expectation rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports one fixture analyzed under one profile during a profile sweep.
/// </summary>
public sealed class CalibrationProfileSweepResult
{
    /// <summary>
    /// Gets or sets the stable fixture id.
    /// </summary>
    public string FixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets the advisory benchmark label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture's assigned profile.
    /// </summary>
    public string AssignedProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the requested sweep profile id.
    /// </summary>
    public string RequestedProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved profile id used for analysis.
    /// </summary>
    public string SweptProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the resolved profile fingerprint.
    /// </summary>
    public string ProfileFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this row uses the fixture's assigned profile.
    /// </summary>
    public bool IsAssignedProfile { get; set; }

    /// <summary>
    /// Gets or sets scorecard dimensions keyed by name.
    /// </summary>
    public Dictionary<string, double> Scorecard { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets scenario rates keyed by scenario name.
    /// </summary>
    public Dictionary<string, CalibrationScenarioValue> Scenarios { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets warnings surfaced by the analyzer.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Reports profile-sensitive metric ordering without affecting pass/fail.
/// </summary>
public sealed class CalibrationProfileSensitivityResult
{
    /// <summary>
    /// Gets or sets the diagnostic kind.
    /// </summary>
    public string DiagnosticType { get; set; } = "";

    /// <summary>
    /// Gets or sets the related expectation id.
    /// </summary>
    public string ExpectationId { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets the metric key.
    /// </summary>
    public string Metric { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture most directly involved in the diagnostic.
    /// </summary>
    public string FixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the comparison fixture when the diagnostic is pairwise.
    /// </summary>
    public string OtherFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the assigned profile id.
    /// </summary>
    public string AssignedProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the alternate profile id.
    /// </summary>
    public string AlternateProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the assigned-profile metric value or delta.
    /// </summary>
    public double AssignedValue { get; set; }

    /// <summary>
    /// Gets or sets the alternate-profile metric value or delta.
    /// </summary>
    public double AlternateValue { get; set; }

    /// <summary>
    /// Gets or sets the alternate value minus assigned value.
    /// </summary>
    public double Difference { get; set; }

    /// <summary>
    /// Gets or sets a concise human-readable diagnostic.
    /// </summary>
    public string Message { get; set; } = "";
}

/// <summary>
/// Captures a benchmark-derived pressure profile used for calibration diagnostics.
/// </summary>
public sealed class CalibrationPressureProfile
{
    /// <summary>
    /// Gets or sets the corpus-defined pressure profile id.
    /// </summary>
    public string ProfileId { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture that supplied the pressure metrics.
    /// </summary>
    public string SourceFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group of the source fixture.
    /// </summary>
    public string SourceGroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets the advisory label of the source fixture.
    /// </summary>
    public string SourceLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets a stable fingerprint of the pressure inputs.
    /// </summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the estimated race turn implied by the source route-assembly metric.
    /// </summary>
    public double ComboRaceTurn { get; set; }

    /// <summary>
    /// Gets or sets the source fixture interaction-readiness score.
    /// </summary>
    public double InteractionDensity { get; set; }

    /// <summary>
    /// Gets or sets the source fixture early-development score.
    /// </summary>
    public double EarlyDevelopment { get; set; }

    /// <summary>
    /// Gets or sets the source fixture stranded-resilience score.
    /// </summary>
    public double StrandedResilience { get; set; }

    /// <summary>
    /// Gets or sets the source fixture route-assembly score.
    /// </summary>
    public double RouteAssembly { get; set; }
}

/// <summary>
/// Reports one heuristic opponent-pressure diagnostic.
/// </summary>
public sealed class CalibrationPressureDiagnosticResult
{
    /// <summary>
    /// Gets or sets the stable expectation id.
    /// </summary>
    public string ExpectationId { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the expectation should fail the CLI or only warn.
    /// </summary>
    public string Severity { get; set; } = CalibrationExpectationSeverity.Required;

    /// <summary>
    /// Gets or sets tags that describe the pressure diagnostic.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the target fixture being evaluated.
    /// </summary>
    public string TargetFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the target fixture's advisory label.
    /// </summary>
    public string TargetFixtureLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the target fixture's resolved profile id.
    /// </summary>
    public string TargetProfile { get; set; } = "";

    /// <summary>
    /// Gets or sets the target fixture's resolved profile fingerprint.
    /// </summary>
    public string TargetProfileFingerprint { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture used to derive the pressure profile.
    /// </summary>
    public string PressureSourceFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the pressure source fixture's advisory label.
    /// </summary>
    public string PressureSourceLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the derived pressure profile.
    /// </summary>
    public CalibrationPressureProfile PressureProfile { get; set; } = new();

    /// <summary>
    /// Gets or sets the share of pressure thresholds satisfied by the target fixture.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the required diagnostic score.
    /// </summary>
    public double Threshold { get; set; }

    /// <summary>
    /// Gets or sets whether the diagnostic passed.
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Gets or sets metric and scenario names affected by the pressure profile.
    /// </summary>
    public List<string> AffectedScenarios { get; set; } = [];

    /// <summary>
    /// Gets or sets threshold-level pressure checks.
    /// </summary>
    public List<CalibrationPressureThresholdResult> Thresholds { get; set; } = [];

    /// <summary>
    /// Gets or sets failed threshold names for compact reporting.
    /// </summary>
    public List<string> FailedThresholds { get; set; } = [];

    /// <summary>
    /// Gets or sets the diagnostic rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports one Commander bracket benchmark range check.
/// </summary>
public sealed class CalibrationBracketDiagnosticResult
{
    /// <summary>
    /// Gets or sets the stable expectation id.
    /// </summary>
    public string ExpectationId { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the expectation should fail the CLI or only warn.
    /// </summary>
    public string Severity { get; set; } = CalibrationExpectationSeverity.Required;

    /// <summary>
    /// Gets or sets tags that describe the bracket benchmark.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the fixture being evaluated.
    /// </summary>
    public string TargetFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the target fixture's advisory label.
    /// </summary>
    public string TargetFixtureLabel { get; set; } = "";

    /// <summary>
    /// Gets or sets the minimum acceptable bracket.
    /// </summary>
    public int MinimumBracket { get; set; }

    /// <summary>
    /// Gets or sets the maximum acceptable bracket.
    /// </summary>
    public int MaximumBracket { get; set; }

    /// <summary>
    /// Gets or sets the estimated bracket produced by the current model.
    /// </summary>
    public int EstimatedBracket { get; set; }

    /// <summary>
    /// Gets or sets the hard-signal floor produced by the current model.
    /// </summary>
    public int BracketFloor { get; set; }

    /// <summary>
    /// Gets or sets the estimate confidence.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets the number of Game Changers found by the bracket model.
    /// </summary>
    public int GameChangerCount { get; set; }

    /// <summary>
    /// Gets or sets compact signal labels emitted by the bracket model.
    /// </summary>
    public List<string> Signals { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the estimated bracket fell within the expected range.
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Gets or sets why this bracket range is expected.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Captures one pressure threshold comparison.
/// </summary>
public sealed class CalibrationPressureThresholdResult
{
    /// <summary>
    /// Gets or sets the threshold name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the target fixture value.
    /// </summary>
    public double TargetValue { get; set; }

    /// <summary>
    /// Gets or sets the required value derived from the pressure source.
    /// </summary>
    public double RequiredValue { get; set; }

    /// <summary>
    /// Gets or sets the comparison operator, such as greater-or-equal.
    /// </summary>
    public string Comparison { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the target satisfied this threshold.
    /// </summary>
    public bool Passed { get; set; }
}

/// <summary>
/// Reports one metric drift check against a saved baseline.
/// </summary>
public sealed class CalibrationDriftResult
{
    /// <summary>
    /// Gets or sets the fixture id.
    /// </summary>
    public string FixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the metric key.
    /// </summary>
    public string Metric { get; set; } = "";

    /// <summary>
    /// Gets or sets the baseline value.
    /// </summary>
    public double BaselineValue { get; set; }

    /// <summary>
    /// Gets or sets the current value.
    /// </summary>
    public double CurrentValue { get; set; }

    /// <summary>
    /// Gets or sets the absolute drift.
    /// </summary>
    public double AbsoluteDelta { get; set; }

    /// <summary>
    /// Gets or sets the allowed absolute drift.
    /// </summary>
    public double Tolerance { get; set; }

    /// <summary>
    /// Gets or sets whether the drift is within tolerance.
    /// </summary>
    public bool Passed { get; set; }
}

/// <summary>
/// Stores stable metric values for drift comparisons.
/// </summary>
public sealed class StatsLabCalibrationBaseline
{
    /// <summary>
    /// Gets or sets the baseline schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// Gets or sets the expected model version.
    /// </summary>
    public string ModelVersion { get; set; } = "";

    /// <summary>
    /// Gets or sets the default metric tolerance.
    /// </summary>
    public double Tolerance { get; set; } = 0.000001;

    /// <summary>
    /// Gets or sets baseline fixture metric values.
    /// </summary>
    public List<StatsLabCalibrationBaselineFixture> Fixtures { get; set; } = [];

    /// <summary>
    /// Builds a baseline from a current calibration report.
    /// </summary>
    public static StatsLabCalibrationBaseline FromReport(StatsLabCalibrationReport report)
    {
        StatsLabCalibrationBaseline baseline = new()
        {
            ModelVersion = report.Summary.ModelVersion,
        };

        foreach (CalibrationFixtureResult fixture in report.Fixtures)
        {
            StatsLabCalibrationBaselineFixture baselineFixture = new()
            {
                FixtureId = fixture.FixtureId,
                GroupId = fixture.GroupId,
                SourceKind = fixture.SourceKind,
                SourceUri = fixture.SourceUri,
                CapturedAt = fixture.CapturedAt,
                SourceNote = fixture.SourceNote,
            };

            foreach ((string key, double value) in FixtureMetrics(fixture))
            {
                baselineFixture.Metrics[key] = value;
            }

            baseline.Fixtures.Add(baselineFixture);
        }

        return baseline;
    }

    /// <summary>
    /// Enumerates calibration metrics for a fixture.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, double>> FixtureMetrics(CalibrationFixtureResult fixture)
    {
        foreach (KeyValuePair<string, double> score in fixture.Scorecard)
        {
            yield return new KeyValuePair<string, double>($"scorecard:{score.Key}", score.Value);
        }

        foreach (KeyValuePair<string, CalibrationScenarioValue> scenario in fixture.Scenarios)
        {
            yield return new KeyValuePair<string, double>($"scenario:{scenario.Key}", scenario.Value.Value);
        }
    }
}

/// <summary>
/// Stores baseline metrics for one fixture.
/// </summary>
public sealed class StatsLabCalibrationBaselineFixture
{
    /// <summary>
    /// Gets or sets the fixture id.
    /// </summary>
    public string FixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id when available.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets the source kind when available.
    /// </summary>
    public string SourceKind { get; set; } = "";

    /// <summary>
    /// Gets or sets the public source URI when available.
    /// </summary>
    public string SourceUri { get; set; } = "";

    /// <summary>
    /// Gets or sets the snapshot capture date when available.
    /// </summary>
    public string CapturedAt { get; set; } = "";

    /// <summary>
    /// Gets or sets source notes when available.
    /// </summary>
    public string SourceNote { get; set; } = "";

    /// <summary>
    /// Gets or sets metric values keyed by metric name.
    /// </summary>
    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Defines a corpus fixture and its advisory label.
/// </summary>
internal sealed class CalibrationFixture
{
    /// <summary>
    /// Gets or sets the stable fixture id.
    /// </summary>
    public string FixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the advisory strength label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets the simulation profile.
    /// </summary>
    public string Profile { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture source note.
    /// </summary>
    public string SourceNote { get; set; } = "";

    /// <summary>
    /// Gets or sets the source kind, such as synthetic, edhrec, or cedh-decklist-database.
    /// </summary>
    public string SourceKind { get; set; } = "";

    /// <summary>
    /// Gets or sets the public source URI used to construct the fixture.
    /// </summary>
    public string SourceUri { get; set; } = "";

    /// <summary>
    /// Gets or sets the date when the offline snapshot was captured.
    /// </summary>
    public string CapturedAt { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace to analyze.
    /// </summary>
    [JsonIgnore]
    public DeckWorkspace Workspace { get; set; } = new();
}

/// <summary>
/// Defines one expected calibration relationship.
/// </summary>
internal sealed class CalibrationExpectation
{
    /// <summary>
    /// Gets or sets the expectation id.
    /// </summary>
    public string ExpectationId { get; set; } = "";

    /// <summary>
    /// Gets or sets the expectation kind.
    /// </summary>
    public string Kind { get; set; } = CalibrationExpectationKind.Pairwise;

    /// <summary>
    /// Gets or sets the metric key.
    /// </summary>
    public string Metric { get; set; } = "";

    /// <summary>
    /// Gets or sets the benchmark group id.
    /// </summary>
    public string GroupId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the expectation should fail the CLI or only warn.
    /// </summary>
    public string Severity { get; set; } = CalibrationExpectationSeverity.Required;

    /// <summary>
    /// Gets or sets tags that describe the expectation's benchmark purpose.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the expected direction, higher or lower.
    /// </summary>
    public string Direction { get; set; } = "higher";

    /// <summary>
    /// Gets or sets the fixture expected to be better by this metric.
    /// </summary>
    public string PreferredFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the comparison fixture id.
    /// </summary>
    public string OtherFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture evaluated by a pressure expectation.
    /// </summary>
    public string TargetFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the fixture used to derive a pressure profile.
    /// </summary>
    public string PressureSourceFixtureId { get; set; } = "";

    /// <summary>
    /// Gets or sets the corpus-defined pressure profile id.
    /// </summary>
    public string PressureProfileId { get; set; } = "";

    /// <summary>
    /// Gets or sets the minimum acceptable Commander bracket for a bracket-range expectation.
    /// </summary>
    public int MinimumBracket { get; set; }

    /// <summary>
    /// Gets or sets the maximum acceptable Commander bracket for a bracket-range expectation.
    /// </summary>
    public int MaximumBracket { get; set; }

    /// <summary>
    /// Gets or sets offline Game Changer names used by a bracket-range expectation.
    /// </summary>
    public List<string> GameChangers { get; set; } = [];

    /// <summary>
    /// Gets or sets the required pressure diagnostic score.
    /// </summary>
    public double Threshold { get; set; }

    /// <summary>
    /// Gets or sets the minimum direction-adjusted delta.
    /// </summary>
    public double MinimumDelta { get; set; }

    /// <summary>
    /// Gets or sets why this comparison is expected.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Lists supported calibration expectation kinds.
/// </summary>
internal static class CalibrationExpectationKind
{
    /// <summary>
    /// Compares one metric between two fixtures.
    /// </summary>
    public const string Pairwise = "pairwise";

    /// <summary>
    /// Compares a fixture against a benchmark-derived pressure profile.
    /// </summary>
    public const string Pressure = "pressure";

    /// <summary>
    /// Checks one fixture's Commander bracket estimate against an expected range.
    /// </summary>
    public const string BracketRange = "bracket-range";
}

/// <summary>
/// Lists supported calibration expectation severities.
/// </summary>
internal static class CalibrationExpectationSeverity
{
    /// <summary>
    /// Marks an expectation that should fail the CLI when unmet.
    /// </summary>
    public const string Required = "required";

    /// <summary>
    /// Marks an expectation that should be reported but not fail the CLI.
    /// </summary>
    public const string Advisory = "advisory";
}

/// <summary>
/// Reports corpus validation results without running simulations.
/// </summary>
public sealed class CalibrationCorpusValidationResult
{
    /// <summary>
    /// Gets or sets the number of loaded fixtures.
    /// </summary>
    public int FixtureCount { get; set; }

    /// <summary>
    /// Gets or sets the number of loaded expectations.
    /// </summary>
    public int ExpectationCount { get; set; }

    /// <summary>
    /// Gets or sets the number of required expectations.
    /// </summary>
    public int RequiredExpectationCount { get; set; }

    /// <summary>
    /// Gets or sets the number of advisory expectations.
    /// </summary>
    public int AdvisoryExpectationCount { get; set; }
}
