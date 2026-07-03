using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace MtgMcp.Calibration.Tests;

/// <summary>
/// Contains tests for the offline Stats Lab calibration harness.
/// </summary>
public sealed class StatsLabCalibrationTests
{
    /// <summary>
    /// Verifies that the built-in corpus produces a usable report with passing pairwise expectations.
    /// </summary>
    [Fact]
    public void CalibrationRunner_DefaultCorpusPassesPairwiseExpectations()
    {
        StatsLabCalibrationRunner runner = new();

        StatsLabCalibrationReport report = runner.Run(new CalibrationOptions
        {
            Simulations = 2_000,
            MaxTurn = 8,
            Seed = 2026,
        });

        report.SchemaVersion.Should().Be(6);
        report.Summary.FixtureCount.Should().Be(20);
        report.Summary.ExpectationCount.Should().BeGreaterThan(0);
        report.Summary.FailedRequiredExpectations.Should().Be(0);
        report.Summary.AdvisoryExpectationCount.Should().BeGreaterThan(0);
        report.Summary.PressureDiagnosticCount.Should().Be(4);
        report.Summary.BracketDiagnosticCount.Should().Be(4);
        report.Summary.ProfileSweepCount.Should().Be(0);
        report.Summary.ProfileSensitivityCount.Should().Be(0);
        report.PressureDiagnostics.Should().HaveCount(4);
        report.PressureDiagnostics.Should().OnlyContain(diagnostic => diagnostic.PressureProfile.Fingerprint.Length == 64);
        report.PressureDiagnostics.Should().OnlyContain(diagnostic => diagnostic.AffectedScenarios.Count > 0);
        report.PressureDiagnostics.Should().OnlyContain(diagnostic => diagnostic.Thresholds.Count == 5);
        report.PressureDiagnostics
            .Where(diagnostic => diagnostic.Severity == "required")
            .Should()
            .OnlyContain(diagnostic => diagnostic.Passed);
        report.BracketDiagnostics.Should().HaveCount(4);
        report.BracketDiagnostics.Should().OnlyContain(diagnostic => diagnostic.Passed);
        report.BracketDiagnostics.Should().Contain(diagnostic =>
            diagnostic.TargetFixtureId == "bracket-cedh-density"
            && diagnostic.EstimatedBracket == 4);
        report.ProfileSweeps.Should().BeEmpty();
        report.ProfileSensitivity.Should().BeEmpty();
        report.Fixtures.Should().OnlyContain(fixture => fixture.DeckSize == 100);
        report.Fixtures.Should().OnlyContain(fixture => fixture.DeckFingerprint.Length == 64);
        report.Fixtures.Should().OnlyContain(fixture => fixture.Warnings.Count == 0);
        report.Expectations.Should().OnlyContain(expectation => !string.IsNullOrWhiteSpace(expectation.GroupId));
        report.Expectations.Should().OnlyContain(expectation => !string.IsNullOrWhiteSpace(expectation.Severity));
        report.Expectations.Should().OnlyContain(expectation => expectation.MarginToThreshold == expectation.Delta - expectation.MinimumDelta);
        report.Notes.Should().Contain(note => note.Contains("not true multiplayer win rates", StringComparison.OrdinalIgnoreCase));
        report.Notes.Should().Contain(note => note.Contains("Pressure diagnostics", StringComparison.OrdinalIgnoreCase));

        List<CalibrationFixtureResult> benchmarkFixtures = [];
        foreach (CalibrationFixtureResult fixture in report.Fixtures)
        {
            if (!fixture.SourceKind.Equals("synthetic", StringComparison.OrdinalIgnoreCase))
            {
                benchmarkFixtures.Add(fixture);
            }
        }

        benchmarkFixtures.Should().NotBeEmpty();
        benchmarkFixtures.Select(fixture => fixture.GroupId).Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(6);
        benchmarkFixtures.Should().OnlyContain(fixture => !string.IsNullOrWhiteSpace(fixture.GroupId));
        benchmarkFixtures.Should().OnlyContain(fixture => !string.IsNullOrWhiteSpace(fixture.SourceKind));
        benchmarkFixtures.Should().OnlyContain(fixture => !string.IsNullOrWhiteSpace(fixture.SourceUri));
        benchmarkFixtures.Should().OnlyContain(fixture => !string.IsNullOrWhiteSpace(fixture.CapturedAt));
    }

    /// <summary>
    /// Verifies that synthetic-only mode keeps the original smoke corpus fast and isolated.
    /// </summary>
    [Fact]
    public void CalibrationRunner_SyntheticOnlyPassesPairwiseExpectations()
    {
        StatsLabCalibrationRunner runner = new();

        StatsLabCalibrationReport report = runner.Run(new CalibrationOptions
        {
            Simulations = 2_000,
            MaxTurn = 8,
            Seed = 2026,
            SyntheticOnly = true,
        });

        report.Summary.FixtureCount.Should().Be(4);
        report.Summary.ExpectationCount.Should().Be(6);
        report.Summary.FailedRequiredExpectations.Should().Be(0);
        report.Summary.AdvisoryExpectationCount.Should().Be(0);
        report.Summary.PressureDiagnosticCount.Should().Be(0);
        report.PressureDiagnostics.Should().BeEmpty();
        report.Settings.SyntheticOnly.Should().BeTrue();
        report.Settings.CorpusPath.Should().BeEmpty();
        report.Fixtures.Should().OnlyContain(fixture => fixture.SourceKind == "synthetic");
    }

    /// <summary>
    /// Verifies that profile sweeps add non-failing alternate-profile diagnostics.
    /// </summary>
    [Fact]
    public void CalibrationRunner_ProfileSweepReportsAlternateProfileDiagnostics()
    {
        StatsLabCalibrationRunner runner = new();

        StatsLabCalibrationReport report = runner.Run(new CalibrationOptions
        {
            Simulations = 500,
            MaxTurn = 8,
            Seed = 2026,
            SyntheticOnly = true,
            ProfileSweepIds = { "value", "combo" },
        });

        report.SchemaVersion.Should().Be(6);
        report.Summary.FailedRequiredExpectations.Should().Be(0);
        report.ProfileSweeps.Should().HaveCountGreaterThan(report.Fixtures.Count);
        report.Summary.ProfileSweepCount.Should().Be(report.ProfileSweeps.Count);
        report.ProfileSweeps.Should().OnlyContain(sweep => sweep.ProfileFingerprint.Length == 64);
        report.ProfileSweeps.Where(sweep => sweep.IsAssignedProfile).Should().HaveCount(report.Fixtures.Count);
        report.ProfileSweeps.Should().Contain(sweep =>
            sweep.FixtureId == "azorius-battlecruiser"
            && sweep.SweptProfile == "big-mana"
            && sweep.IsAssignedProfile);
        report.ProfileSweeps.Should().Contain(sweep =>
            sweep.FixtureId == "azorius-battlecruiser"
            && sweep.SweptProfile == "combo"
            && !sweep.IsAssignedProfile);
        report.ProfileSensitivity.Should().NotBeEmpty();
        report.Summary.ProfileSensitivityCount.Should().Be(report.ProfileSensitivity.Count);
        report.Notes.Should().Contain(note => note.Contains("Profile sweeps are diagnostics only", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that validate-only mode checks corpus shape without running analyses.
    /// </summary>
    [Fact]
    public void CalibrationRunner_ValidateOnlyChecksExpandedCorpus()
    {
        StatsLabCalibrationRunner runner = new();

        CalibrationCorpusValidationResult result = runner.Validate(new CalibrationOptions
        {
            ValidateOnly = true,
        });

        result.FixtureCount.Should().Be(20);
        result.ExpectationCount.Should().Be(27);
        result.RequiredExpectationCount.Should().Be(22);
        result.AdvisoryExpectationCount.Should().Be(5);
    }

    /// <summary>
    /// Verifies that checked-in corpus fixtures can be loaded from a file or directory.
    /// </summary>
    [Fact]
    public void CalibrationCorpusLoader_LoadsFileAndDirectoryInput()
    {
        string corpusPath = CalibrationCorpusLoader.DefaultCorpusPath;
        string filePath = Path.Combine(corpusPath, "kinnan-benchmark.json");

        CalibrationCorpusLoadResult fileResult = CalibrationCorpusLoader.Load(filePath);
        CalibrationCorpusLoadResult directoryResult = CalibrationCorpusLoader.Load(corpusPath);

        fileResult.Fixtures.Should().HaveCount(2);
        fileResult.Expectations.Should().HaveCount(4);
        directoryResult.Fixtures.Should().HaveCountGreaterThan(fileResult.Fixtures.Count);
        directoryResult.Expectations.Should().HaveCountGreaterThan(fileResult.Expectations.Count);

        foreach (CalibrationFixture fixture in directoryResult.Fixtures)
        {
            fixture.Workspace.Format.Should().Be("commander");
            IncludedCardCount(fixture).Should().Be(100);
            fixture.SourceUri.Should().NotBeNullOrWhiteSpace();
            fixture.CapturedAt.Should().NotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// Verifies that invalid corpus files fail with actionable fixture-reference messages.
    /// </summary>
    [Fact]
    public void CalibrationCorpusLoader_RejectsUnknownExpectationReferences()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string corpusFile = Path.Combine(outputDirectory, "invalid.json");
            File.WriteAllText(
                corpusFile,
                """
                {
                  "schemaVersion": 1,
                  "fixtures": [],
                  "expectations": [
                    {
                      "expectationId": "missing-reference",
                      "groupId": "invalid",
                      "metric": "scorecard:route-assembly",
                      "direction": "higher",
                      "preferredFixtureId": "missing-preferred",
                      "otherFixtureId": "missing-other",
                      "minimumDelta": 0.1,
                      "rationale": "Invalid corpus reference."
                    }
                  ]
                }
                """);

            Action act = () => CalibrationCorpusLoader.Load(corpusFile);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*missing-reference*missing-preferred*");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that invalid pressure expectations fail with actionable fixture-reference messages.
    /// </summary>
    [Fact]
    public void CalibrationCorpusLoader_RejectsUnknownPressureExpectationReferences()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string corpusFile = Path.Combine(outputDirectory, "invalid-pressure.json");
            File.WriteAllText(
                corpusFile,
                """
                {
                  "schemaVersion": 2,
                  "fixtures": [],
                  "expectations": [
                    {
                      "expectationId": "missing-pressure-reference",
                      "kind": "pressure",
                      "groupId": "invalid",
                      "severity": "required",
                      "targetFixtureId": "missing-target",
                      "pressureSourceFixtureId": "missing-source",
                      "pressureProfileId": "invalid-pressure",
                      "threshold": 0.5,
                      "rationale": "Invalid pressure corpus reference."
                    }
                  ]
                }
                """);

            Action act = () => CalibrationCorpusLoader.Load(corpusFile);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*missing-pressure-reference*missing-target*");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that unsupported corpus schemas fail before fixtures are analyzed.
    /// </summary>
    [Fact]
    public void CalibrationCorpusLoader_RejectsUnsupportedSchemaVersion()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string corpusFile = Path.Combine(outputDirectory, "unsupported.json");
            File.WriteAllText(
                corpusFile,
                """
                {
                  "schemaVersion": 99,
                  "fixtures": [],
                  "expectations": []
                }
                """);

            Action act = () => CalibrationCorpusLoader.Load(corpusFile);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*unsupported schemaVersion 99*supported versions are 1 and 2*");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that v1 corpus expectations receive required severity defaults.
    /// </summary>
    [Fact]
    public void CalibrationCorpusLoader_V1ExpectationsDefaultToRequired()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string sourceCorpus = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json");
            string v1Corpus = Path.Combine(outputDirectory, "kinnan-v1.json");
            JsonNode document = JsonNode.Parse(File.ReadAllText(sourceCorpus))!;
            document["schemaVersion"] = 1;
            foreach (JsonNode? expectationNode in document["expectations"]!.AsArray())
            {
                JsonObject expectation = expectationNode!.AsObject();
                expectation.Remove("severity");
                expectation.Remove("tags");
            }

            File.WriteAllText(v1Corpus, document.ToJsonString(StatsLabCalibrationReportWriter.JsonOptions));

            CalibrationCorpusLoadResult result = CalibrationCorpusLoader.Load(v1Corpus);

            result.Expectations.Should().OnlyContain(expectation => expectation.Severity == "required");
            result.Expectations.Should().OnlyContain(expectation => expectation.Tags.Count == 0);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that validate-only options are routed through the validation method.
    /// </summary>
    [Fact]
    public void CalibrationRunner_RunRejectsValidateOnlyOptions()
    {
        StatsLabCalibrationRunner runner = new();

        Action act = () => runner.Run(new CalibrationOptions
        {
            ValidateOnly = true,
            SyntheticOnly = true,
        });

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Validate-only mode should call Validate*");
    }

    /// <summary>
    /// Verifies that unknown sweep profiles fail before simulations run.
    /// </summary>
    [Fact]
    public void CalibrationRunner_ValidateRejectsUnknownProfileSweepId()
    {
        StatsLabCalibrationRunner runner = new();

        Action act = () => runner.Validate(new CalibrationOptions
        {
            SyntheticOnly = true,
            ProfileSweepIds = { "not-a-profile" },
        });

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Profile sweep*unknown profile id*not-a-profile*");
    }

    /// <summary>
    /// Verifies that validation fails clearly for unknown fixture profiles.
    /// </summary>
    [Fact]
    public void CalibrationRunner_ValidateRejectsUnknownProfileId()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string sourceCorpus = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json");
            string invalidCorpus = Path.Combine(outputDirectory, "unknown-profile.json");
            string json = File.ReadAllText(sourceCorpus)
                .Replace("\"profile\": \"big-mana\"", "\"profile\": \"not-a-profile\"", StringComparison.Ordinal);
            File.WriteAllText(invalidCorpus, json);

            StatsLabCalibrationRunner runner = new();
            Action act = () => runner.Validate(new CalibrationOptions { CorpusPath = invalidCorpus });

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*kinnan-edhrec-average*unknown profile id*not-a-profile*");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that validation fails clearly for non-Commander benchmark fixtures.
    /// </summary>
    [Fact]
    public void CalibrationRunner_ValidateRejectsNonCommanderFixture()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string sourceCorpus = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json");
            string invalidCorpus = Path.Combine(outputDirectory, "wrong-format.json");
            string json = File.ReadAllText(sourceCorpus)
                .Replace("\"format\": \"commander\"", "\"format\": \"standard\"", StringComparison.Ordinal);
            File.WriteAllText(invalidCorpus, json);

            StatsLabCalibrationRunner runner = new();
            Action act = () => runner.Validate(new CalibrationOptions { CorpusPath = invalidCorpus });

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*kinnan-edhrec-average*must be a Commander deck*standard*");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that intentionally inverted benchmark expectations are reported as failures.
    /// </summary>
    [Fact]
    public void CalibrationRunner_FailsInvertedPairwiseExpectation()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string sourceCorpus = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json");
            string invertedCorpus = Path.Combine(outputDirectory, "inverted-kinnan.json");
            string json = File.ReadAllText(sourceCorpus);
            json = json.Replace(
                "\"preferredFixtureId\": \"kinnan-cedh-template\"",
                "\"preferredFixtureId\": \"kinnan-edhrec-average\"",
                StringComparison.Ordinal);
            json = json.Replace(
                "\"otherFixtureId\": \"kinnan-edhrec-average\"",
                "\"otherFixtureId\": \"kinnan-cedh-template\"",
                StringComparison.Ordinal);
            File.WriteAllText(invertedCorpus, json);

            StatsLabCalibrationRunner runner = new();
            StatsLabCalibrationReport report = runner.Run(new CalibrationOptions
            {
                CorpusPath = invertedCorpus,
                Simulations = 1_000,
                MaxTurn = 8,
                Seed = 2026,
            });

            report.Summary.FailedRequiredExpectations.Should().BeGreaterThan(0);
            report.Expectations.Should().Contain(expectation =>
                !expectation.Passed
                && expectation.PreferredFixtureId == "kinnan-edhrec-average"
                && expectation.OtherFixtureId == "kinnan-cedh-template");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that advisory failures are visible without counting as required failures.
    /// </summary>
    [Fact]
    public void CalibrationRunner_AdvisoryFailuresDoNotFailRequiredSummary()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string sourceCorpus = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json");
            string invertedCorpus = Path.Combine(outputDirectory, "advisory-inverted-kinnan.json");
            string json = File.ReadAllText(sourceCorpus);
            json = json.Replace(
                "\"preferredFixtureId\": \"kinnan-cedh-template\"",
                "\"preferredFixtureId\": \"kinnan-edhrec-average\"",
                StringComparison.Ordinal);
            json = json.Replace(
                "\"otherFixtureId\": \"kinnan-edhrec-average\"",
                "\"otherFixtureId\": \"kinnan-cedh-template\"",
                StringComparison.Ordinal);
            json = json.Replace("\"severity\": \"required\"", "\"severity\": \"advisory\"", StringComparison.Ordinal);
            File.WriteAllText(invertedCorpus, json);

            StatsLabCalibrationRunner runner = new();
            StatsLabCalibrationReport report = runner.Run(new CalibrationOptions
            {
                CorpusPath = invertedCorpus,
                Simulations = 1_000,
                MaxTurn = 8,
                Seed = 2026,
            });

            report.Summary.FailedRequiredExpectations.Should().Be(0);
            report.Summary.FailedAdvisoryExpectations.Should().BeGreaterThan(0);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that required pressure diagnostics affect the required failure summary.
    /// </summary>
    [Fact]
    public void CalibrationRunner_FailsRequiredPressureExpectation()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string sourceCorpus = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json");
            string pressureCorpus = Path.Combine(outputDirectory, "required-pressure-kinnan.json");
            JsonNode document = JsonNode.Parse(File.ReadAllText(sourceCorpus))!;
            foreach (JsonNode? expectationNode in document["expectations"]!.AsArray())
            {
                JsonObject expectation = expectationNode!.AsObject();
                if (expectation["expectationId"]!.GetValue<string>() == "kinnan-average-under-cedh-pressure")
                {
                    expectation["expectationId"] = "kinnan-average-under-cedh-pressure-required";
                    expectation["severity"] = "required";
                    expectation["threshold"] = 1.0;
                }
            }

            File.WriteAllText(pressureCorpus, document.ToJsonString(StatsLabCalibrationReportWriter.JsonOptions));

            StatsLabCalibrationRunner runner = new();
            StatsLabCalibrationReport report = runner.Run(new CalibrationOptions
            {
                CorpusPath = pressureCorpus,
                Simulations = 1_000,
                MaxTurn = 8,
                Seed = 2026,
            });

            report.Summary.FailedRequiredExpectations.Should().BeGreaterThan(0);
            report.PressureDiagnostics.Should().Contain(diagnostic =>
                diagnostic.ExpectationId == "kinnan-average-under-cedh-pressure-required"
                && !diagnostic.Passed
                && diagnostic.FailedThresholds.Count > 0);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that typoed expectation metrics fail with fixture-specific diagnostics.
    /// </summary>
    [Fact]
    public void CalibrationRunner_RejectsUnknownExpectationMetric()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string sourceCorpus = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json");
            string invalidCorpus = Path.Combine(outputDirectory, "invalid-metric-kinnan.json");
            string json = File.ReadAllText(sourceCorpus)
                .Replace("scorecard:route-assembly", "scorecard:not-a-real-dimension", StringComparison.Ordinal);
            File.WriteAllText(invalidCorpus, json);

            StatsLabCalibrationRunner runner = new();
            Action act = () => runner.Run(new CalibrationOptions
            {
                CorpusPath = invalidCorpus,
                Simulations = 200,
                MaxTurn = 8,
                Seed = 2026,
            });

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*scorecard:not-a-real-dimension*kinnan-cedh-template*");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that report writing emits JSON, Markdown, and reusable baseline artifacts.
    /// </summary>
    [Fact]
    public void CalibrationReportWriter_WritesReportArtifacts()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            StatsLabCalibrationRunner runner = new();
            StatsLabCalibrationReport report = runner.Run(new CalibrationOptions
            {
                Simulations = 500,
                MaxTurn = 8,
                Seed = 2026,
                CorpusPath = Path.Combine(CalibrationCorpusLoader.DefaultCorpusPath, "kinnan-benchmark.json"),
            });
            report.Fixtures[0].Label = "synthetic | fixture";

            StatsLabCalibrationReportWriter.Write(report, outputDirectory);

            File.Exists(Path.Combine(outputDirectory, "report.json")).Should().BeTrue();
            File.Exists(Path.Combine(outputDirectory, "report.md")).Should().BeTrue();
            File.Exists(Path.Combine(outputDirectory, "baseline.json")).Should().BeTrue();
            string reportText = File.ReadAllText(Path.Combine(outputDirectory, "report.md"));
            reportText.Should().Contain("Stats Lab Calibration Report");
            reportText.Should().Contain("Expectations By Group");
            reportText.Should().Contain("Profile Diagnostics");
            reportText.Should().Contain("Required expectations");
            reportText.Should().Contain("Near misses");
            reportText.Should().Contain("Pressure Diagnostics");
            reportText.Should().Contain("synthetic \\| fixture");
            string baselineJson = File.ReadAllText(Path.Combine(outputDirectory, "baseline.json"));
            StatsLabCalibrationBaseline? baseline = JsonSerializer.Deserialize<StatsLabCalibrationBaseline>(
                baselineJson,
                StatsLabCalibrationReportWriter.JsonOptions);
            baseline.Should().NotBeNull();
            baseline!.SchemaVersion.Should().Be(2);
            baseline!.Fixtures.Should().HaveCount(report.Fixtures.Count);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that a baseline created from the current report produces no drift failures.
    /// </summary>
    [Fact]
    public void CalibrationRunner_CurrentBaselineProducesNoDriftFailures()
    {
        StatsLabCalibrationRunner runner = new();
        CalibrationOptions options = new()
        {
            Simulations = 500,
            MaxTurn = 8,
            Seed = 2026,
            SyntheticOnly = true,
        };
        StatsLabCalibrationReport report = runner.Run(options);
        StatsLabCalibrationBaseline baseline = StatsLabCalibrationBaseline.FromReport(report);

        StatsLabCalibrationRunner.AddDriftResults(report, baseline);

        report.Drift.Should().NotBeEmpty();
        report.Drift.Should().OnlyContain(drift => drift.Passed);
    }

    /// <summary>
    /// Verifies that v1 baselines remain readable for drift comparisons.
    /// </summary>
    [Fact]
    public void CalibrationRunner_V1BaselineStillProducesDriftResults()
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "mtg-mcp-calibration-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            CalibrationOptions options = new()
            {
                Simulations = 500,
                MaxTurn = 8,
                Seed = 2026,
                SyntheticOnly = true,
            };
            StatsLabCalibrationRunner runner = new();
            StatsLabCalibrationReport baselineReport = runner.Run(options);
            StatsLabCalibrationBaseline baseline = StatsLabCalibrationBaseline.FromReport(baselineReport);
            baseline.SchemaVersion = 1;

            string baselinePath = Path.Combine(outputDirectory, "baseline-v1.json");
            File.WriteAllText(
                baselinePath,
                JsonSerializer.Serialize(baseline, StatsLabCalibrationReportWriter.JsonOptions));

            StatsLabCalibrationReport currentReport = runner.Run(new CalibrationOptions
            {
                Simulations = 500,
                MaxTurn = 8,
                Seed = 2026,
                SyntheticOnly = true,
                BaselinePath = baselinePath,
            });

            currentReport.Drift.Should().NotBeEmpty();
            currentReport.Drift.Should().OnlyContain(drift => drift.Passed);
            currentReport.Summary.DriftFailures.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies calibration CLI switches for deterministic run settings.
    /// </summary>
    [Fact]
    public void CalibrationOptions_ParseSupportsControllableRunSettings()
    {
        CalibrationOptions options = CalibrationOptions.Parse(
            [
                "--simulations",
                "1234",
                "--max-turn",
                "6",
                "--seed",
                "-7",
                "--corpus",
                "custom-corpus",
                "--no-mulligans",
                "--allow-failures",
            ]);

        options.Simulations.Should().Be(1234);
        options.MaxTurn.Should().Be(6);
        options.Seed.Should().Be(-7);
        options.CorpusPath.Should().Be("custom-corpus");
        options.SyntheticOnly.Should().BeFalse();
        options.ValidateOnly.Should().BeFalse();
        options.IncludeMulligans.Should().BeFalse();
        options.AllowFailures.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the validate-only CLI switch parses as a non-simulation mode.
    /// </summary>
    [Fact]
    public void CalibrationOptions_ParseSupportsValidateOnly()
    {
        CalibrationOptions options = CalibrationOptions.Parse(["--validate-only", "--synthetic-only"]);

        options.ValidateOnly.Should().BeTrue();
        options.SyntheticOnly.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that profile-sweep ids parse as a de-duplicated comma-separated list.
    /// </summary>
    [Fact]
    public void CalibrationOptions_ParseSupportsProfileSweep()
    {
        CalibrationOptions options = CalibrationOptions.Parse(["--profile-sweep", "value, combo, value"]);

        options.ProfileSweepIds.Should().Equal("value", "combo");
    }

    /// <summary>
    /// Verifies that mutually exclusive corpus switches fail clearly.
    /// </summary>
    [Fact]
    public void CalibrationOptions_ParseRejectsSyntheticOnlyWithCorpus()
    {
        Action act = () => CalibrationOptions.Parse(["--synthetic-only", "--corpus", "custom-corpus"]);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("Options '--synthetic-only' and '--corpus' cannot be combined.");
    }

    /// <summary>
    /// Verifies calibration CLI parse errors are actionable.
    /// </summary>
    [Fact]
    public void CalibrationOptions_ParseRejectsInvalidNumbers()
    {
        Action act = () => CalibrationOptions.Parse(["--simulations", "many"]);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("Option '--simulations' requires an integer value.");
    }

    /// <summary>
    /// Counts active deck cards in a corpus fixture.
    /// </summary>
    private static int IncludedCardCount(CalibrationFixture fixture)
    {
        int count = 0;
        foreach (MtgMcp.Core.DeckCard card in fixture.Workspace.Cards)
        {
            count += card.Quantity;
        }

        return count;
    }
}
