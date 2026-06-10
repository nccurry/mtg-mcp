using System.Text;
using System.Text.Json;
using System.Globalization;

namespace MtgMcp.Calibration;

/// <summary>
/// Writes Stats Lab calibration reports as generated artifacts.
/// </summary>
public static class StatsLabCalibrationReportWriter
{
    /// <summary>
    /// Gets JSON options shared by report and baseline files.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Writes JSON, Markdown, and current-baseline artifacts.
    /// </summary>
    public static void Write(StatsLabCalibrationReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string reportJson = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(Path.Combine(outputDirectory, "report.json"), reportJson);
        File.WriteAllText(Path.Combine(outputDirectory, "report.md"), BuildMarkdown(report));
        StatsLabCalibrationBaseline baseline = StatsLabCalibrationBaseline.FromReport(report);
        File.WriteAllText(
            Path.Combine(outputDirectory, "baseline.json"),
            JsonSerializer.Serialize(baseline, JsonOptions));
    }

    /// <summary>
    /// Builds a compact Markdown report for humans.
    /// </summary>
    private static string BuildMarkdown(StatsLabCalibrationReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Stats Lab Calibration Report");
        builder.AppendLine();
        AppendInvariant(builder, $"- Schema version: {report.SchemaVersion}");
        AppendInvariant(builder, $"- Model version: `{report.Summary.ModelVersion}`");
        AppendInvariant(builder, $"- Fixtures: {report.Summary.FixtureCount}");
        AppendInvariant(builder, $"- Expectations: {report.Summary.PassedExpectations}/{report.Summary.ExpectationCount} passed");
        AppendInvariant(
            builder,
            $"- Required expectations: {report.Summary.PassedRequiredExpectations}/{report.Summary.RequiredExpectationCount} passed");
        AppendInvariant(
            builder,
            $"- Advisory expectations: {report.Summary.PassedAdvisoryExpectations}/{report.Summary.AdvisoryExpectationCount} passed");
        AppendInvariant(builder, $"- Near misses: {report.Summary.NearMissExpectations}");
        AppendInvariant(builder, $"- Pressure diagnostics: {report.Summary.PressureDiagnosticCount}");
        AppendInvariant(builder, $"- Profile sweeps: {report.Summary.ProfileSweepCount}");
        AppendInvariant(builder, $"- Profile sensitivity diagnostics: {report.Summary.ProfileSensitivityCount}");
        AppendInvariant(builder, $"- Drift failures: {report.Summary.DriftFailures}");
        if (report.Settings.SyntheticOnly)
        {
            builder.AppendLine("- Corpus: built-in synthetic fixtures only");
        }
        else
        {
            AppendInvariant(builder, $"- Corpus: `{report.Settings.CorpusPath}`");
        }

        builder.AppendLine();

        builder.AppendLine("## Fixtures");
        builder.AppendLine();
        builder.AppendLine("| Fixture | Group | Label | Source | Profile | Profile source | Deck size | Mana | Development | Interaction | Routes | Stranded |");
        builder.AppendLine("|---|---|---|---|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (CalibrationFixtureResult fixture in report.Fixtures)
        {
            AppendFixtureRow(builder, fixture);
        }

        builder.AppendLine();
        builder.AppendLine("## Profile Diagnostics");
        builder.AppendLine();
        builder.AppendLine("| Fixture | Profile fingerprint | Prefer commander | Hold interaction | Min held | Ramp priority | Tutor priority | Combo priority | Seven keep |");
        builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (CalibrationFixtureResult fixture in report.Fixtures)
        {
            AppendProfileDiagnosticsRow(builder, fixture);
        }

        AppendExpectationGroups(builder, report.Expectations);
        AppendPressureDiagnostics(builder, report.PressureDiagnostics);
        AppendProfileSweeps(builder, report.ProfileSweeps);
        AppendProfileSensitivity(builder, report.ProfileSensitivity);

        if (report.Drift.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Drift");
            builder.AppendLine();
            builder.AppendLine("| Status | Fixture | Metric | Baseline | Current | Delta | Tolerance |");
            builder.AppendLine("|---|---|---|---:|---:|---:|---:|");
            foreach (CalibrationDriftResult drift in report.Drift)
            {
                string status = drift.Passed ? "pass" : "fail";
                AppendDriftRow(builder, drift, status);
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine();
        foreach (string note in report.Notes)
        {
            AppendInvariant(builder, $"- {note}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends one fixture row to the Markdown report.
    /// </summary>
    private static void AppendFixtureRow(StringBuilder builder, CalibrationFixtureResult fixture)
    {
        string manaStability = Score(fixture, "mana-stability");
        string earlyDevelopment = Score(fixture, "early-development");
        string interactionReadiness = Score(fixture, "interaction-readiness");
        string routeAssembly = Score(fixture, "route-assembly");
        string strandedResilience = Score(fixture, "stranded-resilience");
        string[] columns =
        [
            $"`{fixture.FixtureId}`",
            $"`{fixture.GroupId}`",
            fixture.Label,
            SourceLabel(fixture),
            $"`{fixture.Profile}`",
            fixture.ProfileDiagnostics.Source,
            fixture.DeckSize.ToString(CultureInfo.InvariantCulture),
            manaStability,
            earlyDevelopment,
            interactionReadiness,
            routeAssembly,
            strandedResilience,
        ];

        AppendMarkdownRow(builder, columns);
    }

    /// <summary>
    /// Appends one profile diagnostic row.
    /// </summary>
    private static void AppendProfileDiagnosticsRow(StringBuilder builder, CalibrationFixtureResult fixture)
    {
        CalibrationProfileDiagnostics diagnostics = fixture.ProfileDiagnostics;
        string[] columns =
        [
            $"`{fixture.FixtureId}`",
            $"`{fixture.ProfileFingerprint}`",
            diagnostics.PreferCommanderOnCurve.ToString(CultureInfo.InvariantCulture),
            diagnostics.HoldInteractionFromTurn.ToString(CultureInfo.InvariantCulture),
            diagnostics.MinimumInteractionHeld.ToString(CultureInfo.InvariantCulture),
            diagnostics.EarlyRampPriority.ToString(CultureInfo.InvariantCulture),
            diagnostics.TutorPriority.ToString(CultureInfo.InvariantCulture),
            diagnostics.ComboPriority.ToString(CultureInfo.InvariantCulture),
            diagnostics.SevenCardKeepScore.ToString("0.0##", CultureInfo.InvariantCulture),
        ];

        AppendMarkdownRow(builder, columns);
    }

    /// <summary>
    /// Appends expectations grouped by benchmark group.
    /// </summary>
    private static void AppendExpectationGroups(StringBuilder builder, List<CalibrationExpectationResult> expectations)
    {
        Dictionary<string, List<CalibrationExpectationResult>> byGroup = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationExpectationResult expectation in expectations)
        {
            string groupId = string.IsNullOrWhiteSpace(expectation.GroupId) ? "ungrouped" : expectation.GroupId;
            if (!byGroup.TryGetValue(groupId, out List<CalibrationExpectationResult>? group))
            {
                group = [];
                byGroup[groupId] = group;
            }

            group.Add(expectation);
        }

        List<string> groupIds = byGroup.Keys.ToList();
        groupIds.Sort(StringComparer.OrdinalIgnoreCase);
        builder.AppendLine();
        builder.AppendLine("## Expectations By Group");
        foreach (string groupId in groupIds)
        {
            builder.AppendLine();
            AppendInvariant(builder, $"### `{groupId}`");
            builder.AppendLine();
            builder.AppendLine("| Status | Severity | Expectation | Tags | Metric | Preferred | Other | Delta | Minimum | Margin | Near miss | CI overlap |");
            builder.AppendLine("|---|---|---|---|---|---|---|---:|---:|---:|---|---|");
            foreach (CalibrationExpectationResult expectation in byGroup[groupId])
            {
                string status = ExpectationStatus(expectation);
                string overlap = expectation.ConfidenceIntervalsOverlap.HasValue
                    ? expectation.ConfidenceIntervalsOverlap.Value.ToString(CultureInfo.InvariantCulture)
                    : "";
                AppendExpectationRow(builder, expectation, status, overlap);
            }
        }
    }

    /// <summary>
    /// Appends profile sweep rows grouped by benchmark group and fixture.
    /// </summary>
    private static void AppendProfileSweeps(StringBuilder builder, List<CalibrationProfileSweepResult> sweeps)
    {
        if (sweeps.Count == 0)
        {
            return;
        }

        Dictionary<string, List<CalibrationProfileSweepResult>> byGroup = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationProfileSweepResult sweep in sweeps)
        {
            string groupId = string.IsNullOrWhiteSpace(sweep.GroupId) ? "ungrouped" : sweep.GroupId;
            if (!byGroup.TryGetValue(groupId, out List<CalibrationProfileSweepResult>? group))
            {
                group = [];
                byGroup[groupId] = group;
            }

            group.Add(sweep);
        }

        List<string> groupIds = byGroup.Keys.ToList();
        groupIds.Sort(StringComparer.OrdinalIgnoreCase);
        builder.AppendLine();
        builder.AppendLine("## Profile Sweeps");
        foreach (string groupId in groupIds)
        {
            builder.AppendLine();
            AppendInvariant(builder, $"### `{groupId}`");
            Dictionary<string, List<CalibrationProfileSweepResult>> byFixture = GroupSweepsByFixture(byGroup[groupId]);
            List<string> fixtureIds = byFixture.Keys.ToList();
            fixtureIds.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string fixtureId in fixtureIds)
            {
                builder.AppendLine();
                AppendInvariant(builder, $"#### `{fixtureId}`");
                builder.AppendLine();
                builder.AppendLine("| Profile | Assigned | Fingerprint | Mana | Development | Interaction | Routes | Stranded | Warnings |");
                builder.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
                foreach (CalibrationProfileSweepResult sweep in byFixture[fixtureId])
                {
                    AppendProfileSweepRow(builder, sweep);
                }
            }
        }
    }

    /// <summary>
    /// Groups sweep rows by fixture id.
    /// </summary>
    private static Dictionary<string, List<CalibrationProfileSweepResult>> GroupSweepsByFixture(
        List<CalibrationProfileSweepResult> sweeps)
    {
        Dictionary<string, List<CalibrationProfileSweepResult>> byFixture = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationProfileSweepResult sweep in sweeps)
        {
            if (!byFixture.TryGetValue(sweep.FixtureId, out List<CalibrationProfileSweepResult>? fixtureSweeps))
            {
                fixtureSweeps = [];
                byFixture[sweep.FixtureId] = fixtureSweeps;
            }

            fixtureSweeps.Add(sweep);
        }

        foreach (List<CalibrationProfileSweepResult> fixtureSweeps in byFixture.Values)
        {
            fixtureSweeps.Sort((left, right) =>
            {
                int assigned = right.IsAssignedProfile.CompareTo(left.IsAssignedProfile);
                return assigned != 0
                    ? assigned
                    : string.Compare(left.SweptProfile, right.SweptProfile, StringComparison.OrdinalIgnoreCase);
            });
        }

        return byFixture;
    }

    /// <summary>
    /// Appends one profile sweep row.
    /// </summary>
    private static void AppendProfileSweepRow(StringBuilder builder, CalibrationProfileSweepResult sweep)
    {
        string[] columns =
        [
            $"`{sweep.SweptProfile}`",
            sweep.IsAssignedProfile.ToString(CultureInfo.InvariantCulture),
            $"`{sweep.ProfileFingerprint}`",
            Score(sweep.Scorecard, "mana-stability"),
            Score(sweep.Scorecard, "early-development"),
            Score(sweep.Scorecard, "interaction-readiness"),
            Score(sweep.Scorecard, "route-assembly"),
            Score(sweep.Scorecard, "stranded-resilience"),
            sweep.Warnings.Count.ToString(CultureInfo.InvariantCulture),
        ];

        AppendMarkdownRow(builder, columns);
    }

    /// <summary>
    /// Appends profile sensitivity diagnostics.
    /// </summary>
    private static void AppendProfileSensitivity(
        StringBuilder builder,
        List<CalibrationProfileSensitivityResult> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine("## Profile Sensitivity Diagnostics");
        builder.AppendLine();
        builder.AppendLine("| Type | Group | Expectation | Fixture | Other | Metric | Assigned profile | Alternate profile | Assigned | Alternate | Difference | Message |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|---:|---:|---:|---|");
        foreach (CalibrationProfileSensitivityResult diagnostic in diagnostics)
        {
            AppendProfileSensitivityRow(builder, diagnostic);
        }
    }

    /// <summary>
    /// Appends one profile sensitivity diagnostic row.
    /// </summary>
    private static void AppendProfileSensitivityRow(
        StringBuilder builder,
        CalibrationProfileSensitivityResult diagnostic)
    {
        string[] columns =
        [
            diagnostic.DiagnosticType,
            $"`{diagnostic.GroupId}`",
            $"`{diagnostic.ExpectationId}`",
            $"`{diagnostic.FixtureId}`",
            $"`{diagnostic.OtherFixtureId}`",
            $"`{diagnostic.Metric}`",
            $"`{diagnostic.AssignedProfile}`",
            $"`{diagnostic.AlternateProfile}`",
            diagnostic.AssignedValue.ToString("0.000", CultureInfo.InvariantCulture),
            diagnostic.AlternateValue.ToString("0.000", CultureInfo.InvariantCulture),
            diagnostic.Difference.ToString("0.000", CultureInfo.InvariantCulture),
            diagnostic.Message,
        ];

        AppendMarkdownRow(builder, columns);
    }

    /// <summary>
    /// Appends pressure diagnostics grouped by benchmark group.
    /// </summary>
    private static void AppendPressureDiagnostics(
        StringBuilder builder,
        List<CalibrationPressureDiagnosticResult> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        Dictionary<string, List<CalibrationPressureDiagnosticResult>> byGroup = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationPressureDiagnosticResult diagnostic in diagnostics)
        {
            string groupId = string.IsNullOrWhiteSpace(diagnostic.GroupId) ? "ungrouped" : diagnostic.GroupId;
            if (!byGroup.TryGetValue(groupId, out List<CalibrationPressureDiagnosticResult>? group))
            {
                group = [];
                byGroup[groupId] = group;
            }

            group.Add(diagnostic);
        }

        List<string> groupIds = byGroup.Keys.ToList();
        groupIds.Sort(StringComparer.OrdinalIgnoreCase);
        builder.AppendLine();
        builder.AppendLine("## Pressure Diagnostics");
        foreach (string groupId in groupIds)
        {
            builder.AppendLine();
            AppendInvariant(builder, $"### `{groupId}`");
            builder.AppendLine();
            builder.AppendLine("| Status | Severity | Expectation | Tags | Target | Pressure source | Profile | Fingerprint | Score | Threshold | Failed thresholds | Affected scenarios |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|---:|---:|---|---|");
            foreach (CalibrationPressureDiagnosticResult diagnostic in byGroup[groupId])
            {
                AppendPressureDiagnosticRow(builder, diagnostic);
            }
        }
    }

    /// <summary>
    /// Appends one pressure diagnostic row.
    /// </summary>
    private static void AppendPressureDiagnosticRow(
        StringBuilder builder,
        CalibrationPressureDiagnosticResult diagnostic)
    {
        string status = PressureDiagnosticStatus(diagnostic);
        string failedThresholds = diagnostic.FailedThresholds.Count == 0
            ? ""
            : string.Join(", ", diagnostic.FailedThresholds);
        string[] columns =
        [
            status,
            diagnostic.Severity,
            $"`{diagnostic.ExpectationId}`",
            string.Join(", ", diagnostic.Tags),
            ExpectationFixtureLabel(
                diagnostic.TargetFixtureId,
                diagnostic.TargetFixtureLabel,
                diagnostic.TargetProfile,
                diagnostic.TargetProfileFingerprint),
            $"{diagnostic.PressureSourceFixtureId} ({diagnostic.PressureSourceLabel})",
            $"`{diagnostic.PressureProfile.ProfileId}`",
            $"`{diagnostic.PressureProfile.Fingerprint}`",
            diagnostic.Score.ToString("0.000", CultureInfo.InvariantCulture),
            diagnostic.Threshold.ToString("0.000", CultureInfo.InvariantCulture),
            failedThresholds,
            string.Join(", ", diagnostic.AffectedScenarios),
        ];

        AppendMarkdownRow(builder, columns);
    }

    /// <summary>
    /// Appends one pairwise expectation row to the Markdown report.
    /// </summary>
    private static void AppendExpectationRow(
        StringBuilder builder,
        CalibrationExpectationResult expectation,
        string status,
        string overlap)
    {
        string[] columns =
        [
            status,
            expectation.Severity,
            $"`{expectation.ExpectationId}`",
            string.Join(", ", expectation.Tags),
            $"`{expectation.Metric}`",
            ExpectationFixtureLabel(
                expectation.PreferredFixtureId,
                expectation.PreferredFixtureLabel,
                expectation.PreferredProfile,
                expectation.PreferredProfileFingerprint),
            ExpectationFixtureLabel(
                expectation.OtherFixtureId,
                expectation.OtherFixtureLabel,
                expectation.OtherProfile,
                expectation.OtherProfileFingerprint),
            expectation.Delta.ToString("0.000", CultureInfo.InvariantCulture),
            expectation.MinimumDelta.ToString("0.000", CultureInfo.InvariantCulture),
            expectation.MarginToThreshold.ToString("0.000", CultureInfo.InvariantCulture),
            expectation.NearMiss.ToString(CultureInfo.InvariantCulture),
            overlap,
        ];

        AppendMarkdownRow(builder, columns);
    }

    /// <summary>
    /// Appends one baseline drift row to the Markdown report.
    /// </summary>
    private static void AppendDriftRow(StringBuilder builder, CalibrationDriftResult drift, string status)
    {
        string[] columns =
        [
            status,
            $"`{drift.FixtureId}`",
            $"`{drift.Metric}`",
            drift.BaselineValue.ToString("0.000000", CultureInfo.InvariantCulture),
            drift.CurrentValue.ToString("0.000000", CultureInfo.InvariantCulture),
            drift.AbsoluteDelta.ToString("0.000000", CultureInfo.InvariantCulture),
            drift.Tolerance.ToString("0.000000", CultureInfo.InvariantCulture),
        ];

        AppendMarkdownRow(builder, columns);
    }

    /// <summary>
    /// Appends one preformatted Markdown table row.
    /// </summary>
    private static void AppendMarkdownRow(StringBuilder builder, IEnumerable<string> columns)
    {
        List<string> escaped = [];
        foreach (string column in columns)
        {
            escaped.Add(EscapeMarkdownCell(column));
        }

        builder.Append("| ");
        builder.Append(string.Join(" | ", escaped));
        builder.AppendLine(" |");
    }

    /// <summary>
    /// Escapes Markdown table control characters from generated cells.
    /// </summary>
    private static string EscapeMarkdownCell(string value)
    {
        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a compact source label for fixture tables.
    /// </summary>
    private static string SourceLabel(CalibrationFixtureResult fixture)
    {
        if (string.IsNullOrWhiteSpace(fixture.SourceUri))
        {
            return fixture.SourceKind;
        }

        return $"[{fixture.SourceKind}]({fixture.SourceUri})";
    }

    /// <summary>
    /// Formats a fixture reference with the profile details needed for diagnostics.
    /// </summary>
    private static string ExpectationFixtureLabel(
        string fixtureId,
        string label,
        string profile,
        string profileFingerprint)
    {
        return $"{fixtureId} ({label}, {profile}, {profileFingerprint})";
    }

    /// <summary>
    /// Formats pass/fail state while distinguishing advisory warnings.
    /// </summary>
    private static string ExpectationStatus(CalibrationExpectationResult expectation)
    {
        if (expectation.Passed)
        {
            return "pass";
        }

        if (expectation.Severity.Equals(CalibrationExpectationSeverity.Advisory, StringComparison.OrdinalIgnoreCase))
        {
            return "warn";
        }

        return "fail";
    }

    /// <summary>
    /// Formats pressure diagnostic pass/fail state while distinguishing advisory warnings.
    /// </summary>
    private static string PressureDiagnosticStatus(CalibrationPressureDiagnosticResult diagnostic)
    {
        if (diagnostic.Passed)
        {
            return "pass";
        }

        if (diagnostic.Severity.Equals(CalibrationExpectationSeverity.Advisory, StringComparison.OrdinalIgnoreCase))
        {
            return "warn";
        }

        return "fail";
    }

    /// <summary>
    /// Appends an interpolated line using invariant formatting.
    /// </summary>
    private static void AppendInvariant(StringBuilder builder, FormattableString value)
    {
        builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Formats one scorecard value.
    /// </summary>
    private static string Score(CalibrationFixtureResult fixture, string name)
    {
        return Score(fixture.Scorecard, name);
    }

    /// <summary>
    /// Formats one scorecard value from a score dictionary.
    /// </summary>
    private static string Score(IReadOnlyDictionary<string, double> scorecard, string name)
    {
        return scorecard.TryGetValue(name, out double value)
            ? value.ToString("0.000", CultureInfo.InvariantCulture)
            : "";
    }
}
