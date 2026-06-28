using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Calibration;

/// <summary>
/// Runs the offline Stats Lab calibration corpus and evaluates expectations.
/// </summary>
public sealed class StatsLabCalibrationRunner
{
    /// <summary>
    /// Defines the absolute margin considered close enough to call out in diagnostics.
    /// </summary>
    private const double NearMissMargin = 0.02;

    /// <summary>
    /// Names the scorecard dimension used for early pressure development checks.
    /// </summary>
    private const string EarlyDevelopmentMetric = "early-development";

    /// <summary>
    /// Names the scorecard dimension used for interaction pressure checks.
    /// </summary>
    private const string InteractionReadinessMetric = "interaction-readiness";

    /// <summary>
    /// Names the scorecard dimension used for route-assembly pressure checks.
    /// </summary>
    private const string RouteAssemblyMetric = "route-assembly";

    /// <summary>
    /// Names the scorecard dimension used for stranded-card pressure checks.
    /// </summary>
    private const string StrandedResilienceMetric = "stranded-resilience";

    /// <summary>
    /// Runs calibration with the supplied options.
    /// </summary>
    public StatsLabCalibrationReport Run(CalibrationOptions options)
    {
        if (options.ValidateOnly)
        {
            throw new InvalidOperationException("Validate-only mode should call Validate instead of Run.");
        }

        (List<CalibrationFixture> fixtures, List<CalibrationExpectation> expectations) = BuildCorpus(options);
        Dictionary<string, CalibrationFixtureResult> byId = new(StringComparer.OrdinalIgnoreCase);
        StatsLabCalibrationReport report = new()
        {
            Settings = new CalibrationRunSettings
            {
                Simulations = options.Simulations,
                MaxTurn = options.MaxTurn,
                Seed = options.Seed,
                IncludeMulligans = options.IncludeMulligans,
                BaselinePath = options.BaselinePath ?? "",
                CorpusPath = options.SyntheticOnly
                    ? ""
                    : Path.GetFullPath(options.CorpusPath ?? CalibrationCorpusLoader.DefaultCorpusPath),
                SyntheticOnly = options.SyntheticOnly,
                ProfileSweepIds = options.ProfileSweepIds.ToList(),
            },
        };

        foreach (CalibrationFixture fixture in fixtures)
        {
            CalibrationFixtureResult result = AnalyzeFixture(fixture, options);
            report.Fixtures.Add(result);
            byId[result.FixtureId] = result;
        }

        Dictionary<string, CalibrationFixture> sourceById = fixtures.ToDictionary(
            fixture => fixture.FixtureId,
            StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationExpectation expectation in expectations)
        {
            if (IsPressureExpectation(expectation))
            {
                report.PressureDiagnostics.Add(EvaluatePressureExpectation(expectation, byId));
            }
            else if (IsBracketRangeExpectation(expectation))
            {
                report.BracketDiagnostics.Add(EvaluateBracketExpectation(expectation, sourceById));
            }
            else
            {
                report.Expectations.Add(EvaluateExpectation(expectation, byId));
            }
        }

        AddProfileSweepResults(report, fixtures, byId, expectations, options);
        AddDriftResults(report, options);
        BuildSummary(report);
        AddReportNotes(report, options);
        return report;
    }

    /// <summary>
    /// Loads and validates the corpus without running simulations.
    /// </summary>
    public CalibrationCorpusValidationResult Validate(CalibrationOptions options)
    {
        (List<CalibrationFixture> fixtures, List<CalibrationExpectation> expectations) = BuildCorpus(options);
        CalibrationCorpusValidationResult result = new()
        {
            FixtureCount = fixtures.Count,
            ExpectationCount = expectations.Count,
        };

        foreach (CalibrationExpectation expectation in expectations)
        {
            if (IsRequired(expectation.Severity))
            {
                result.RequiredExpectationCount++;
            }
            else
            {
                result.AdvisoryExpectationCount++;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the run corpus from built-in synthetic fixtures and optional JSON benchmark files.
    /// </summary>
    private static (List<CalibrationFixture> Fixtures, List<CalibrationExpectation> Expectations) BuildCorpus(
        CalibrationOptions options)
    {
        List<CalibrationFixture> fixtures = CalibrationCorpus.BuildFixtures();
        List<CalibrationExpectation> expectations = CalibrationCorpus.BuildExpectations();
        if (!options.SyntheticOnly)
        {
            string corpusPath = options.CorpusPath ?? CalibrationCorpusLoader.DefaultCorpusPath;
            CalibrationCorpusLoadResult loaded = CalibrationCorpusLoader.Load(corpusPath);
            fixtures.AddRange(loaded.Fixtures);
            expectations.AddRange(loaded.Expectations);
        }

        ValidateCorpusReferences(fixtures, expectations);
        ValidateCorpusContent(fixtures, expectations);
        ValidateProfileSweepOptions(options.ProfileSweepIds);
        return (fixtures, expectations);
    }

    /// <summary>
    /// Ensures fixture and expectation ids are unique and resolvable.
    /// </summary>
    private static void ValidateCorpusReferences(
        IReadOnlyList<CalibrationFixture> fixtures,
        IReadOnlyList<CalibrationExpectation> expectations)
    {
        HashSet<string> fixtureIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationFixture fixture in fixtures)
        {
            if (!fixtureIds.Add(fixture.FixtureId))
            {
                throw new InvalidOperationException($"Calibration fixture '{fixture.FixtureId}' is defined more than once.");
            }
        }

        HashSet<string> expectationIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationExpectation expectation in expectations)
        {
            if (!expectationIds.Add(expectation.ExpectationId))
            {
                throw new InvalidOperationException($"Calibration expectation '{expectation.ExpectationId}' is defined more than once.");
            }

            if (IsPressureExpectation(expectation))
            {
                if (!fixtureIds.Contains(expectation.TargetFixtureId))
                {
                    throw new InvalidOperationException(
                        $"Calibration expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.TargetFixtureId}'.");
                }

                if (!fixtureIds.Contains(expectation.PressureSourceFixtureId))
                {
                    throw new InvalidOperationException(
                        $"Calibration expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.PressureSourceFixtureId}'.");
                }

                continue;
            }

            if (IsBracketRangeExpectation(expectation))
            {
                if (!fixtureIds.Contains(expectation.TargetFixtureId))
                {
                    throw new InvalidOperationException(
                        $"Calibration expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.TargetFixtureId}'.");
                }

                continue;
            }

            if (!fixtureIds.Contains(expectation.PreferredFixtureId))
            {
                throw new InvalidOperationException(
                    $"Calibration expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.PreferredFixtureId}'.");
            }

            if (!fixtureIds.Contains(expectation.OtherFixtureId))
            {
                throw new InvalidOperationException(
                    $"Calibration expectation '{expectation.ExpectationId}' references unknown fixture '{expectation.OtherFixtureId}'.");
            }
        }
    }

    /// <summary>
    /// Validates deck shape, source metadata, profile ids, and expectation severity.
    /// </summary>
    private static void ValidateCorpusContent(
        IReadOnlyList<CalibrationFixture> fixtures,
        IReadOnlyList<CalibrationExpectation> expectations)
    {
        SimulationProfileCatalog profiles = SimulationProfileCatalog.CreateDefault();
        foreach (CalibrationFixture fixture in fixtures)
        {
            ValidateFixtureContent(fixture, profiles);
        }

        foreach (CalibrationExpectation expectation in expectations)
        {
            ValidateExpectationContent(expectation);
        }
    }

    /// <summary>
    /// Validates one fixture's Commander shape and profile id.
    /// </summary>
    private static void ValidateFixtureContent(CalibrationFixture fixture, SimulationProfileCatalog profiles)
    {
        if (!fixture.Workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase)
            && !fixture.Workspace.Format.Equals("edh", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Calibration fixture '{fixture.FixtureId}' must be a Commander deck; found format '{fixture.Workspace.Format}'.");
        }

        List<DeckCard> included = IncludedCards(fixture.Workspace);
        int includedCount = 0;
        int commanderCount = 0;
        foreach (DeckCard card in included)
        {
            includedCount += Math.Max(0, card.Quantity);
            if (DeckCategoryOrdering.PrimaryCategory(card).Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase))
            {
                commanderCount += Math.Max(0, card.Quantity);
            }
        }

        if (includedCount != 100)
        {
            throw new InvalidOperationException(
                $"Calibration fixture '{fixture.FixtureId}' must have exactly 100 active cards; found {includedCount}.");
        }

        if (commanderCount < 1)
        {
            throw new InvalidOperationException(
                $"Calibration fixture '{fixture.FixtureId}' must include a Commander category card.");
        }

        if (!IsKnownProfile(profiles, fixture.Profile))
        {
            throw new InvalidOperationException(
                $"Calibration fixture '{fixture.FixtureId}' references unknown profile id '{fixture.Profile}'.");
        }
    }

    /// <summary>
    /// Validates expectation shape shared by built-in and JSON corpus sources.
    /// </summary>
    private static void ValidateExpectationContent(CalibrationExpectation expectation)
    {
        if (!IsRequired(expectation.Severity)
            && !expectation.Severity.Equals(CalibrationExpectationSeverity.Advisory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Calibration expectation '{expectation.ExpectationId}' severity must be 'required' or 'advisory'.");
        }

        if (IsPressureExpectation(expectation))
        {
            if (string.IsNullOrWhiteSpace(expectation.TargetFixtureId)
                || string.IsNullOrWhiteSpace(expectation.PressureSourceFixtureId)
                || string.IsNullOrWhiteSpace(expectation.PressureProfileId))
            {
                throw new InvalidOperationException(
                    $"Calibration pressure expectation '{expectation.ExpectationId}' is missing pressure fixture or profile fields.");
            }

            if (expectation.Threshold is <= 0 or > 1)
            {
                throw new InvalidOperationException(
                    $"Calibration pressure expectation '{expectation.ExpectationId}' threshold must be greater than 0 and at most 1.");
            }

            return;
        }

        if (IsBracketRangeExpectation(expectation))
        {
            if (string.IsNullOrWhiteSpace(expectation.TargetFixtureId))
            {
                throw new InvalidOperationException(
                    $"Calibration bracket expectation '{expectation.ExpectationId}' is missing targetFixtureId.");
            }

            if (expectation.MinimumBracket is < 1 or > 4 || expectation.MaximumBracket is < 1 or > 4)
            {
                throw new InvalidOperationException(
                    $"Calibration bracket expectation '{expectation.ExpectationId}' bracket range must stay between 1 and 4.");
            }

            if (expectation.MinimumBracket > expectation.MaximumBracket)
            {
                throw new InvalidOperationException(
                    $"Calibration bracket expectation '{expectation.ExpectationId}' minimumBracket cannot exceed maximumBracket.");
            }

            return;
        }

        if (!expectation.Kind.Equals(CalibrationExpectationKind.Pairwise, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Calibration expectation '{expectation.ExpectationId}' kind must be 'pairwise', 'pressure', or 'bracket-range'.");
        }

        if (string.IsNullOrWhiteSpace(expectation.Metric)
            || string.IsNullOrWhiteSpace(expectation.PreferredFixtureId)
            || string.IsNullOrWhiteSpace(expectation.OtherFixtureId))
        {
            throw new InvalidOperationException(
                $"Calibration pairwise expectation '{expectation.ExpectationId}' is missing metric or fixture fields.");
        }

        if (expectation.MinimumDelta < 0)
        {
            throw new InvalidOperationException(
                $"Calibration pairwise expectation '{expectation.ExpectationId}' minimumDelta must be nonnegative.");
        }
    }

    /// <summary>
    /// Enumerates cards included in the active deck using Core's category semantics.
    /// </summary>
    private static List<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        Dictionary<string, DeckCategory> categories = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCategory category in workspace.Categories)
        {
            if (!string.IsNullOrWhiteSpace(category.Name) && !categories.ContainsKey(category.Name))
            {
                categories[category.Name] = category;
            }
        }

        List<DeckCard> included = [];
        foreach (DeckCard card in workspace.Cards)
        {
            string primaryCategory = DeckCategoryOrdering.PrimaryCategory(card);
            if (!categories.TryGetValue(primaryCategory, out DeckCategory? category) || category.IncludedInDeck)
            {
                included.Add(card);
            }
        }

        return included;
    }

    /// <summary>
    /// Checks whether a profile id can be resolved without falling back from an unknown explicit id.
    /// </summary>
    private static bool IsKnownProfile(SimulationProfileCatalog profiles, string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return false;
        }

        if (profiles.TryGet(profile, out _))
        {
            return true;
        }

        return profile.Equals(SimulationProfileIds.Auto, StringComparison.OrdinalIgnoreCase)
            || profile.Equals("commander-default", StringComparison.OrdinalIgnoreCase)
            || profile.Equals("midrange", StringComparison.OrdinalIgnoreCase)
            || profile.Equals("prison", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates requested profile sweep ids before simulations start.
    /// </summary>
    private static void ValidateProfileSweepOptions(IReadOnlyList<string> profileSweepIds)
    {
        if (profileSweepIds.Count == 0)
        {
            return;
        }

        SimulationProfileCatalog profiles = SimulationProfileCatalog.CreateDefault();
        foreach (string profileId in profileSweepIds)
        {
            if (!IsKnownProfile(profiles, profileId))
            {
                throw new InvalidOperationException($"Profile sweep references unknown profile id '{profileId}'.");
            }
        }
    }

    /// <summary>
    /// Analyzes one fixture through the pure Stats Lab analyzer.
    /// </summary>
    private static CalibrationFixtureResult AnalyzeFixture(CalibrationFixture fixture, CalibrationOptions options)
    {
        return AnalyzeFixture(fixture, fixture.Profile, options);
    }

    /// <summary>
    /// Analyzes one fixture with an explicit profile override.
    /// </summary>
    private static CalibrationFixtureResult AnalyzeFixture(
        CalibrationFixture fixture,
        string profile,
        CalibrationOptions options)
    {
        DeckPerformanceAnalysis analysis = DeckPerformanceAnalyzer.Analyze(
            fixture.Workspace,
            profile,
            options.Simulations,
            options.MaxTurn,
            options.Seed,
            options.IncludeMulligans,
            CancellationToken.None);
        CalibrationFixtureResult result = new()
        {
            FixtureId = fixture.FixtureId,
            Name = fixture.Name,
            Label = fixture.Label,
            GroupId = fixture.GroupId,
            Profile = analysis.Profile,
            SourceNote = fixture.SourceNote,
            SourceKind = fixture.SourceKind,
            SourceUri = fixture.SourceUri,
            CapturedAt = fixture.CapturedAt,
            ModelVersion = analysis.ModelVersion,
            DeckFingerprint = analysis.DeckFingerprint,
            CardDataFingerprint = analysis.CardDataFingerprint,
            ProfileFingerprint = analysis.ProfileFingerprint,
            RngKind = analysis.RngKind,
            ProfileDiagnostics = BuildProfileDiagnostics(analysis.ProfileResolution),
            DeckSize = analysis.DeckSize,
            Warnings = analysis.Warnings.ToList(),
        };

        foreach (PerformanceScorecardDimension dimension in analysis.Scorecard.Dimensions)
        {
            result.Scorecard[dimension.Name] = dimension.Score;
        }

        foreach (ScenarioPerformance scenario in analysis.Scenarios)
        {
            result.Scenarios[scenario.Name] = new CalibrationScenarioValue
            {
                Value = scenario.SuccessRate,
                LowConfidenceInterval = scenario.LowConfidenceInterval,
                HighConfidenceInterval = scenario.HighConfidenceInterval,
            };
        }

        return result;
    }

    /// <summary>
    /// Adds non-failing alternate-profile analyses and sensitivity diagnostics.
    /// </summary>
    private static void AddProfileSweepResults(
        StatsLabCalibrationReport report,
        IReadOnlyList<CalibrationFixture> fixtures,
        IReadOnlyDictionary<string, CalibrationFixtureResult> assignedById,
        IReadOnlyList<CalibrationExpectation> expectations,
        CalibrationOptions options)
    {
        if (options.ProfileSweepIds.Count == 0)
        {
            return;
        }

        Dictionary<string, List<CalibrationProfileSweepResult>> sweepsByFixture = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationFixture fixture in fixtures)
        {
            CalibrationFixtureResult assigned = assignedById[fixture.FixtureId];
            AddProfileSweepResult(
                report,
                sweepsByFixture,
                CreateProfileSweepResult(assigned, assigned.Profile, assigned.Profile, true));

            foreach (string profileId in options.ProfileSweepIds)
            {
                CalibrationFixtureResult swept = AnalyzeFixture(fixture, profileId, options);
                if (swept.Profile.Equals(assigned.Profile, StringComparison.OrdinalIgnoreCase)
                    || HasSweep(sweepsByFixture[fixture.FixtureId], swept.Profile))
                {
                    continue;
                }

                AddProfileSweepResult(
                    report,
                    sweepsByFixture,
                    CreateProfileSweepResult(swept, profileId, assigned.Profile, false));
            }
        }

        AddProfileSensitivityDiagnostics(report, expectations, sweepsByFixture);
    }

    /// <summary>
    /// Adds one profile sweep row to the report and fixture lookup.
    /// </summary>
    private static void AddProfileSweepResult(
        StatsLabCalibrationReport report,
        Dictionary<string, List<CalibrationProfileSweepResult>> sweepsByFixture,
        CalibrationProfileSweepResult sweep)
    {
        report.ProfileSweeps.Add(sweep);
        if (!sweepsByFixture.TryGetValue(sweep.FixtureId, out List<CalibrationProfileSweepResult>? fixtureSweeps))
        {
            fixtureSweeps = [];
            sweepsByFixture[sweep.FixtureId] = fixtureSweeps;
        }

        fixtureSweeps.Add(sweep);
    }

    /// <summary>
    /// Checks whether a fixture already has a sweep row for a resolved profile.
    /// </summary>
    private static bool HasSweep(IReadOnlyList<CalibrationProfileSweepResult> sweeps, string profile)
    {
        foreach (CalibrationProfileSweepResult sweep in sweeps)
        {
            if (sweep.SweptProfile.Equals(profile, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Maps an analysis result into the profile sweep artifact shape.
    /// </summary>
    private static CalibrationProfileSweepResult CreateProfileSweepResult(
        CalibrationFixtureResult analysis,
        string requestedProfile,
        string assignedProfile,
        bool isAssignedProfile)
    {
        CalibrationProfileSweepResult sweep = new()
        {
            FixtureId = analysis.FixtureId,
            Name = analysis.Name,
            GroupId = analysis.GroupId,
            Label = analysis.Label,
            AssignedProfile = assignedProfile,
            RequestedProfile = requestedProfile,
            SweptProfile = analysis.Profile,
            ProfileFingerprint = analysis.ProfileFingerprint,
            IsAssignedProfile = isAssignedProfile,
            Warnings = analysis.Warnings.ToList(),
        };

        foreach (KeyValuePair<string, double> score in analysis.Scorecard)
        {
            sweep.Scorecard[score.Key] = score.Value;
        }

        foreach (KeyValuePair<string, CalibrationScenarioValue> scenario in analysis.Scenarios)
        {
            sweep.Scenarios[scenario.Key] = scenario.Value;
        }

        return sweep;
    }

    /// <summary>
    /// Adds diagnostics that identify profile-dependent expectations and better alternate-profile metric rows.
    /// </summary>
    private static void AddProfileSensitivityDiagnostics(
        StatsLabCalibrationReport report,
        IReadOnlyList<CalibrationExpectation> expectations,
        IReadOnlyDictionary<string, List<CalibrationProfileSweepResult>> sweepsByFixture)
    {
        Dictionary<string, CalibrationExpectationResult> assignedExpectations = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationExpectationResult expectation in report.Expectations)
        {
            assignedExpectations[expectation.ExpectationId] = expectation;
        }

        foreach (CalibrationExpectation expectation in expectations)
        {
            if (IsPressureExpectation(expectation) || IsBracketRangeExpectation(expectation))
            {
                continue;
            }

            AddProfileDependentExpectationDiagnostics(report, expectation, assignedExpectations, sweepsByFixture);
            AddAssignedProfileLagDiagnostics(report, expectation, sweepsByFixture);
        }
    }

    /// <summary>
    /// Reports expectations whose outcome changes when both fixtures use the same alternate profile.
    /// </summary>
    private static void AddProfileDependentExpectationDiagnostics(
        StatsLabCalibrationReport report,
        CalibrationExpectation expectation,
        IReadOnlyDictionary<string, CalibrationExpectationResult> assignedExpectations,
        IReadOnlyDictionary<string, List<CalibrationProfileSweepResult>> sweepsByFixture)
    {
        if (!assignedExpectations.TryGetValue(expectation.ExpectationId, out CalibrationExpectationResult? assigned)
            || !sweepsByFixture.TryGetValue(expectation.PreferredFixtureId, out List<CalibrationProfileSweepResult>? preferredSweeps)
            || !sweepsByFixture.TryGetValue(expectation.OtherFixtureId, out List<CalibrationProfileSweepResult>? otherSweeps))
        {
            return;
        }

        foreach (CalibrationProfileSweepResult preferred in preferredSweeps)
        {
            if (preferred.IsAssignedProfile)
            {
                continue;
            }

            CalibrationProfileSweepResult? other = FindSweep(otherSweeps, preferred.SweptProfile);
            if (other is null)
            {
                continue;
            }

            double preferredValue = ReadMetric(preferred, expectation.Metric);
            double otherValue = ReadMetric(other, expectation.Metric);
            double delta = DirectionalDelta(preferredValue, otherValue, expectation.Direction);
            bool? confidenceOverlap = ConfidenceIntervalsOverlap(preferred, other, expectation.Metric);
            bool sweptPassed = delta >= expectation.MinimumDelta && confidenceOverlap != true;
            if (sweptPassed == assigned.Passed)
            {
                continue;
            }

            report.ProfileSensitivity.Add(new CalibrationProfileSensitivityResult
            {
                DiagnosticType = "expectation-profile-dependent",
                ExpectationId = expectation.ExpectationId,
                GroupId = expectation.GroupId,
                Metric = expectation.Metric,
                FixtureId = expectation.PreferredFixtureId,
                OtherFixtureId = expectation.OtherFixtureId,
                AssignedProfile = assigned.PreferredProfile,
                AlternateProfile = preferred.SweptProfile,
                AssignedValue = assigned.Delta,
                AlternateValue = delta,
                Difference = delta - assigned.Delta,
                Message = $"Expectation pass/fail changes under profile '{preferred.SweptProfile}'.",
            });
        }
    }

    /// <summary>
    /// Reports preferred fixtures whose assigned profile underperforms a swept profile for the expectation metric.
    /// </summary>
    private static void AddAssignedProfileLagDiagnostics(
        StatsLabCalibrationReport report,
        CalibrationExpectation expectation,
        IReadOnlyDictionary<string, List<CalibrationProfileSweepResult>> sweepsByFixture)
    {
        if (!sweepsByFixture.TryGetValue(expectation.PreferredFixtureId, out List<CalibrationProfileSweepResult>? sweeps))
        {
            return;
        }

        CalibrationProfileSweepResult? assigned = sweeps.FirstOrDefault(sweep => sweep.IsAssignedProfile);
        if (assigned is null)
        {
            return;
        }

        double assignedValue = ReadMetric(assigned, expectation.Metric);
        CalibrationProfileSweepResult? bestAlternate = null;
        double bestImprovement = 0;
        foreach (CalibrationProfileSweepResult alternate in sweeps)
        {
            if (alternate.IsAssignedProfile)
            {
                continue;
            }

            double alternateValue = ReadMetric(alternate, expectation.Metric);
            double improvement = expectation.Direction.Equals("lower", StringComparison.OrdinalIgnoreCase)
                ? assignedValue - alternateValue
                : alternateValue - assignedValue;
            if (improvement > bestImprovement)
            {
                bestAlternate = alternate;
                bestImprovement = improvement;
            }
        }

        if (bestAlternate is null || bestImprovement <= NearMissMargin)
        {
            return;
        }

        double alternateMetricValue = ReadMetric(bestAlternate, expectation.Metric);
        report.ProfileSensitivity.Add(new CalibrationProfileSensitivityResult
        {
            DiagnosticType = "assigned-profile-lag",
            ExpectationId = expectation.ExpectationId,
            GroupId = expectation.GroupId,
            Metric = expectation.Metric,
            FixtureId = expectation.PreferredFixtureId,
            OtherFixtureId = expectation.OtherFixtureId,
            AssignedProfile = assigned.SweptProfile,
            AlternateProfile = bestAlternate.SweptProfile,
            AssignedValue = assignedValue,
            AlternateValue = alternateMetricValue,
            Difference = bestImprovement,
            Message = $"Assigned profile trails swept profile '{bestAlternate.SweptProfile}' for this metric.",
        });
    }

    /// <summary>
    /// Captures the resolved profile settings most relevant to deterministic decisions.
    /// </summary>
    private static CalibrationProfileDiagnostics BuildProfileDiagnostics(ResolvedSimulationProfile resolution)
    {
        SimulationProfile profile = resolution.Profile;
        return new CalibrationProfileDiagnostics
        {
            Source = resolution.Source,
            PreferCommanderOnCurve = profile.Sequencing.PreferCommanderOnCurve,
            PreferredCommanderTurn = profile.Sequencing.PreferredCommanderTurn,
            PreferredBackgroundTurn = profile.Sequencing.PreferredBackgroundTurn,
            HoldInteractionFromTurn = profile.Sequencing.HoldInteractionFromTurn,
            MinimumInteractionHeld = profile.Sequencing.MinimumInteractionHeld,
            EarlyRampPriority = profile.Sequencing.EarlyRampPriority,
            TutorPriority = profile.Sequencing.TutorPriority,
            ComboPriority = profile.Sequencing.ComboPriority,
            SevenCardKeepScore = profile.Mulligan.SevenCardKeepScore,
        };
    }

    /// <summary>
    /// Evaluates one pairwise expectation.
    /// </summary>
    private static CalibrationExpectationResult EvaluateExpectation(
        CalibrationExpectation expectation,
        IReadOnlyDictionary<string, CalibrationFixtureResult> fixtures)
    {
        CalibrationFixtureResult preferred = fixtures[expectation.PreferredFixtureId];
        CalibrationFixtureResult other = fixtures[expectation.OtherFixtureId];
        double preferredValue = ReadMetric(preferred, expectation.Metric);
        double otherValue = ReadMetric(other, expectation.Metric);
        double delta = DirectionalDelta(preferredValue, otherValue, expectation.Direction);
        bool? confidenceOverlap = ConfidenceIntervalsOverlap(preferred, other, expectation.Metric);
        double marginToThreshold = delta - expectation.MinimumDelta;

        return new CalibrationExpectationResult
        {
            ExpectationId = expectation.ExpectationId,
            Metric = expectation.Metric,
            GroupId = expectation.GroupId,
            Severity = expectation.Severity,
            Tags = expectation.Tags.ToList(),
            Direction = expectation.Direction,
            PreferredFixtureId = expectation.PreferredFixtureId,
            PreferredFixtureLabel = preferred.Label,
            PreferredProfile = preferred.Profile,
            PreferredProfileFingerprint = preferred.ProfileFingerprint,
            OtherFixtureId = expectation.OtherFixtureId,
            OtherFixtureLabel = other.Label,
            OtherProfile = other.Profile,
            OtherProfileFingerprint = other.ProfileFingerprint,
            PreferredValue = preferredValue,
            OtherValue = otherValue,
            Delta = delta,
            MinimumDelta = expectation.MinimumDelta,
            MarginToThreshold = marginToThreshold,
            Passed = delta >= expectation.MinimumDelta && confidenceOverlap != true,
            NearMiss = Math.Abs(marginToThreshold) <= NearMissMargin,
            ConfidenceIntervalsOverlap = confidenceOverlap,
            Rationale = expectation.Rationale,
        };
    }

    /// <summary>
    /// Evaluates one pressure expectation against benchmark-derived metric thresholds.
    /// </summary>
    private static CalibrationPressureDiagnosticResult EvaluatePressureExpectation(
        CalibrationExpectation expectation,
        IReadOnlyDictionary<string, CalibrationFixtureResult> fixtures)
    {
        CalibrationFixtureResult target = fixtures[expectation.TargetFixtureId];
        CalibrationFixtureResult source = fixtures[expectation.PressureSourceFixtureId];
        CalibrationPressureProfile pressureProfile = BuildPressureProfile(expectation.PressureProfileId, source);
        List<CalibrationPressureThresholdResult> thresholds = BuildPressureThresholds(target, pressureProfile);

        int passedThresholds = 0;
        List<string> failedThresholds = [];
        foreach (CalibrationPressureThresholdResult threshold in thresholds)
        {
            if (threshold.Passed)
            {
                passedThresholds++;
            }
            else
            {
                failedThresholds.Add(threshold.Name);
            }
        }

        double score = thresholds.Count == 0 ? 0 : passedThresholds / (double)thresholds.Count;
        return new CalibrationPressureDiagnosticResult
        {
            ExpectationId = expectation.ExpectationId,
            GroupId = expectation.GroupId,
            Severity = expectation.Severity,
            Tags = expectation.Tags.ToList(),
            TargetFixtureId = target.FixtureId,
            TargetFixtureLabel = target.Label,
            TargetProfile = target.Profile,
            TargetProfileFingerprint = target.ProfileFingerprint,
            PressureSourceFixtureId = source.FixtureId,
            PressureSourceLabel = source.Label,
            PressureProfile = pressureProfile,
            Score = score,
            Threshold = expectation.Threshold,
            Passed = score >= expectation.Threshold,
            AffectedScenarios = BuildPressureAffectedScenarios(),
            Thresholds = thresholds,
            FailedThresholds = failedThresholds,
            Rationale = expectation.Rationale,
        };
    }

    /// <summary>
    /// Evaluates one Commander bracket expectation against an expected range.
    /// </summary>
    private static CalibrationBracketDiagnosticResult EvaluateBracketExpectation(
        CalibrationExpectation expectation,
        IReadOnlyDictionary<string, CalibrationFixture> fixtures)
    {
        CalibrationFixture target = fixtures[expectation.TargetFixtureId];
        HashSet<string> gameChangers = expectation.GameChangers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        DeckAnalysisMetrics metrics = new(EmptyCardCatalog.Instance);
        CommanderBracketEstimate estimate = metrics.EstimateCommanderBracket(target.Workspace, gameChangers);
        List<string> signals = [];
        foreach (BracketSignal signal in estimate.Signals)
        {
            signals.Add(string.IsNullOrWhiteSpace(signal.CardName)
                ? signal.Signal
                : $"{signal.Signal}:{signal.CardName}");
        }

        bool passed = estimate.EstimatedBracket >= expectation.MinimumBracket
            && estimate.EstimatedBracket <= expectation.MaximumBracket;
        return new CalibrationBracketDiagnosticResult
        {
            ExpectationId = expectation.ExpectationId,
            GroupId = expectation.GroupId,
            Severity = expectation.Severity,
            Tags = expectation.Tags.ToList(),
            TargetFixtureId = target.FixtureId,
            TargetFixtureLabel = target.Label,
            MinimumBracket = expectation.MinimumBracket,
            MaximumBracket = expectation.MaximumBracket,
            EstimatedBracket = estimate.EstimatedBracket,
            BracketFloor = estimate.BracketFloor,
            Confidence = estimate.Confidence,
            GameChangerCount = estimate.GameChangerCount,
            Signals = signals,
            Passed = passed,
            Rationale = expectation.Rationale,
        };
    }

    /// <summary>
    /// Builds a pressure profile from already-computed benchmark fixture metrics.
    /// </summary>
    private static CalibrationPressureProfile BuildPressureProfile(string pressureProfileId, CalibrationFixtureResult source)
    {
        double routeAssembly = ReadScore(source, RouteAssemblyMetric);
        CalibrationPressureProfile profile = new()
        {
            ProfileId = pressureProfileId,
            SourceFixtureId = source.FixtureId,
            SourceGroupId = source.GroupId,
            SourceLabel = source.Label,
            ComboRaceTurn = EstimateComboRaceTurn(routeAssembly),
            InteractionDensity = ReadScore(source, InteractionReadinessMetric),
            EarlyDevelopment = ReadScore(source, EarlyDevelopmentMetric),
            StrandedResilience = ReadScore(source, StrandedResilienceMetric),
            RouteAssembly = routeAssembly,
        };

        profile.Fingerprint = BuildPressureProfileFingerprint(profile);
        return profile;
    }

    /// <summary>
    /// Converts route assembly into an advisory race-turn estimate for pressure reports.
    /// </summary>
    private static double EstimateComboRaceTurn(double routeAssembly)
    {
        return Math.Clamp(6.0 - (routeAssembly * 3.0), 2.0, 6.0);
    }

    /// <summary>
    /// Builds threshold checks that describe whether a target remains healthy under pressure.
    /// </summary>
    private static List<CalibrationPressureThresholdResult> BuildPressureThresholds(
        CalibrationFixtureResult target,
        CalibrationPressureProfile pressureProfile)
    {
        double targetComboRaceTurn = EstimateComboRaceTurn(ReadScore(target, RouteAssemblyMetric));
        return
        [
            GreaterOrEqualThreshold(
                "early-development",
                ReadScore(target, EarlyDevelopmentMetric),
                pressureProfile.EarlyDevelopment * 0.75),
            GreaterOrEqualThreshold(
                "interaction-density",
                ReadScore(target, InteractionReadinessMetric),
                pressureProfile.InteractionDensity * 0.65),
            GreaterOrEqualThreshold(
                "route-assembly",
                ReadScore(target, RouteAssemblyMetric),
                pressureProfile.RouteAssembly * 0.75),
            GreaterOrEqualThreshold(
                "stranded-resilience",
                ReadScore(target, StrandedResilienceMetric),
                Math.Min(0.95, pressureProfile.StrandedResilience * 0.60)),
            LessOrEqualThreshold(
                "combo-race-turn",
                targetComboRaceTurn,
                pressureProfile.ComboRaceTurn + 1.0),
        ];
    }

    /// <summary>
    /// Creates a greater-or-equal pressure threshold result.
    /// </summary>
    private static CalibrationPressureThresholdResult GreaterOrEqualThreshold(
        string name,
        double targetValue,
        double requiredValue)
    {
        return new CalibrationPressureThresholdResult
        {
            Name = name,
            TargetValue = targetValue,
            RequiredValue = requiredValue,
            Comparison = "greater-or-equal",
            Passed = targetValue >= requiredValue,
        };
    }

    /// <summary>
    /// Creates a less-or-equal pressure threshold result.
    /// </summary>
    private static CalibrationPressureThresholdResult LessOrEqualThreshold(
        string name,
        double targetValue,
        double requiredValue)
    {
        return new CalibrationPressureThresholdResult
        {
            Name = name,
            TargetValue = targetValue,
            RequiredValue = requiredValue,
            Comparison = "less-or-equal",
            Passed = targetValue <= requiredValue,
        };
    }

    /// <summary>
    /// Lists scenario and scorecard keys affected by the pressure profile.
    /// </summary>
    private static List<string> BuildPressureAffectedScenarios()
    {
        return
        [
            "scorecard:early-development",
            "scorecard:interaction-readiness",
            "scorecard:route-assembly",
            "scorecard:stranded-resilience",
            "scenario:combo-or-tutor-assembly-by-turn-5",
            "scenario:hold-up-interaction-by-turn-4",
            "scenario:stranded-high-mana-risk-by-max-turn",
        ];
    }

    /// <summary>
    /// Builds a stable fingerprint for a pressure profile.
    /// </summary>
    private static string BuildPressureProfileFingerprint(CalibrationPressureProfile profile)
    {
        string payload = string.Join(
            "|",
            profile.ProfileId,
            profile.SourceFixtureId,
            profile.SourceGroupId,
            profile.ComboRaceTurn.ToString("0.000000", CultureInfo.InvariantCulture),
            profile.InteractionDensity.ToString("0.000000", CultureInfo.InvariantCulture),
            profile.EarlyDevelopment.ToString("0.000000", CultureInfo.InvariantCulture),
            profile.StrandedResilience.ToString("0.000000", CultureInfo.InvariantCulture),
            profile.RouteAssembly.ToString("0.000000", CultureInfo.InvariantCulture));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Reads a metric key from a fixture result.
    /// </summary>
    private static double ReadMetric(CalibrationFixtureResult fixture, string metric)
    {
        return ReadMetric(fixture.Scorecard, fixture.Scenarios, fixture.FixtureId, metric);
    }

    /// <summary>
    /// Reads a metric key from a profile sweep result.
    /// </summary>
    private static double ReadMetric(CalibrationProfileSweepResult fixture, string metric)
    {
        return ReadMetric(fixture.Scorecard, fixture.Scenarios, fixture.FixtureId, metric);
    }

    /// <summary>
    /// Reads a metric key from scorecard and scenario dictionaries.
    /// </summary>
    private static double ReadMetric(
        IReadOnlyDictionary<string, double> scorecard,
        IReadOnlyDictionary<string, CalibrationScenarioValue> scenarios,
        string fixtureId,
        string metric)
    {
        if (metric.StartsWith("scorecard:", StringComparison.OrdinalIgnoreCase))
        {
            string name = metric["scorecard:".Length..];
            if (scorecard.TryGetValue(name, out double value))
            {
                return value;
            }

            throw new InvalidOperationException(
                $"Calibration metric '{metric}' was not found for fixture '{fixtureId}'.");
        }

        if (metric.StartsWith("scenario:", StringComparison.OrdinalIgnoreCase))
        {
            string name = metric["scenario:".Length..];
            if (scenarios.TryGetValue(name, out CalibrationScenarioValue? value))
            {
                return value.Value;
            }

            throw new InvalidOperationException(
                $"Calibration metric '{metric}' was not found for fixture '{fixtureId}'.");
        }

        throw new InvalidOperationException($"Unknown calibration metric '{metric}'.");
    }

    /// <summary>
    /// Reads one scorecard dimension.
    /// </summary>
    private static double ReadScore(CalibrationFixtureResult fixture, string name)
    {
        if (fixture.Scorecard.TryGetValue(name, out double value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Calibration scorecard dimension '{name}' was not found for fixture '{fixture.FixtureId}'.");
    }

    /// <summary>
    /// Computes a direction-adjusted metric delta.
    /// </summary>
    private static double DirectionalDelta(double preferredValue, double otherValue, string direction)
    {
        return direction.Equals("lower", StringComparison.OrdinalIgnoreCase)
            ? otherValue - preferredValue
            : preferredValue - otherValue;
    }

    /// <summary>
    /// Checks scenario confidence interval overlap when a metric has intervals.
    /// </summary>
    private static bool? ConfidenceIntervalsOverlap(
        CalibrationFixtureResult preferred,
        CalibrationFixtureResult other,
        string metric)
    {
        if (!metric.StartsWith("scenario:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name = metric["scenario:".Length..];
        CalibrationScenarioValue left = preferred.Scenarios[name];
        CalibrationScenarioValue right = other.Scenarios[name];
        return left.LowConfidenceInterval <= right.HighConfidenceInterval
            && right.LowConfidenceInterval <= left.HighConfidenceInterval;
    }

    /// <summary>
    /// Checks scenario confidence interval overlap for profile sweep rows.
    /// </summary>
    private static bool? ConfidenceIntervalsOverlap(
        CalibrationProfileSweepResult preferred,
        CalibrationProfileSweepResult other,
        string metric)
    {
        if (!metric.StartsWith("scenario:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name = metric["scenario:".Length..];
        CalibrationScenarioValue left = preferred.Scenarios[name];
        CalibrationScenarioValue right = other.Scenarios[name];
        return left.LowConfidenceInterval <= right.HighConfidenceInterval
            && right.LowConfidenceInterval <= left.HighConfidenceInterval;
    }

    /// <summary>
    /// Finds a sweep row by resolved profile id.
    /// </summary>
    private static CalibrationProfileSweepResult? FindSweep(
        IReadOnlyList<CalibrationProfileSweepResult> sweeps,
        string profile)
    {
        foreach (CalibrationProfileSweepResult sweep in sweeps)
        {
            if (sweep.SweptProfile.Equals(profile, StringComparison.OrdinalIgnoreCase))
            {
                return sweep;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds drift results when a saved baseline was supplied.
    /// </summary>
    private static void AddDriftResults(StatsLabCalibrationReport report, CalibrationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaselinePath))
        {
            return;
        }

        if (!File.Exists(options.BaselinePath))
        {
            report.Notes.Add($"Baseline file '{options.BaselinePath}' was not found; drift comparison was skipped.");
            return;
        }

        string json = File.ReadAllText(options.BaselinePath);
        StatsLabCalibrationBaseline? baseline = JsonSerializer.Deserialize<StatsLabCalibrationBaseline>(
            json,
            StatsLabCalibrationReportWriter.JsonOptions);
        if (baseline is null)
        {
            report.Notes.Add($"Baseline file '{options.BaselinePath}' could not be read; drift comparison was skipped.");
            return;
        }

        AddDriftResults(report, baseline);
    }

    /// <summary>
    /// Adds drift results for a loaded baseline.
    /// </summary>
    public static void AddDriftResults(StatsLabCalibrationReport report, StatsLabCalibrationBaseline baseline)
    {
        Dictionary<string, CalibrationFixtureResult> currentById = new(StringComparer.OrdinalIgnoreCase);
        foreach (CalibrationFixtureResult fixture in report.Fixtures)
        {
            currentById[fixture.FixtureId] = fixture;
        }

        foreach (StatsLabCalibrationBaselineFixture baselineFixture in baseline.Fixtures)
        {
            if (!currentById.TryGetValue(baselineFixture.FixtureId, out CalibrationFixtureResult? currentFixture))
            {
                continue;
            }

            Dictionary<string, double> currentMetrics = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> metric in StatsLabCalibrationBaseline.FixtureMetrics(currentFixture))
            {
                currentMetrics[metric.Key] = metric.Value;
            }

            foreach (KeyValuePair<string, double> baselineMetric in baselineFixture.Metrics)
            {
                if (!currentMetrics.TryGetValue(baselineMetric.Key, out double currentValue))
                {
                    continue;
                }

                double delta = Math.Abs(currentValue - baselineMetric.Value);
                report.Drift.Add(new CalibrationDriftResult
                {
                    FixtureId = baselineFixture.FixtureId,
                    Metric = baselineMetric.Key,
                    BaselineValue = baselineMetric.Value,
                    CurrentValue = currentValue,
                    AbsoluteDelta = delta,
                    Tolerance = baseline.Tolerance,
                    Passed = delta <= baseline.Tolerance,
                });
            }
        }

        BuildSummary(report);
    }

    /// <summary>
    /// Builds summary counts for a report.
    /// </summary>
    private static void BuildSummary(StatsLabCalibrationReport report)
    {
        int passedExpectations = 0;
        int requiredExpectations = 0;
        int passedRequiredExpectations = 0;
        int advisoryExpectations = 0;
        int passedAdvisoryExpectations = 0;
        int nearMissExpectations = 0;
        foreach (CalibrationExpectationResult expectation in report.Expectations)
        {
            if (expectation.Passed)
            {
                passedExpectations++;
            }

            if (IsRequired(expectation.Severity))
            {
                requiredExpectations++;
                if (expectation.Passed)
                {
                    passedRequiredExpectations++;
                }
            }
            else
            {
                advisoryExpectations++;
                if (expectation.Passed)
                {
                    passedAdvisoryExpectations++;
                }
            }

            if (expectation.NearMiss)
            {
                nearMissExpectations++;
            }
        }

        foreach (CalibrationPressureDiagnosticResult diagnostic in report.PressureDiagnostics)
        {
            if (diagnostic.Passed)
            {
                passedExpectations++;
            }

            if (IsRequired(diagnostic.Severity))
            {
                requiredExpectations++;
                if (diagnostic.Passed)
                {
                    passedRequiredExpectations++;
                }
            }
            else
            {
                advisoryExpectations++;
                if (diagnostic.Passed)
                {
                    passedAdvisoryExpectations++;
                }
            }
        }

        foreach (CalibrationBracketDiagnosticResult diagnostic in report.BracketDiagnostics)
        {
            if (diagnostic.Passed)
            {
                passedExpectations++;
            }

            if (IsRequired(diagnostic.Severity))
            {
                requiredExpectations++;
                if (diagnostic.Passed)
                {
                    passedRequiredExpectations++;
                }
            }
            else
            {
                advisoryExpectations++;
                if (diagnostic.Passed)
                {
                    passedAdvisoryExpectations++;
                }
            }
        }

        int driftFailures = 0;
        foreach (CalibrationDriftResult drift in report.Drift)
        {
            if (!drift.Passed)
            {
                driftFailures++;
            }
        }

        report.Summary = new CalibrationSummary
        {
            FixtureCount = report.Fixtures.Count,
            ExpectationCount = report.Expectations.Count + report.PressureDiagnostics.Count + report.BracketDiagnostics.Count,
            PassedExpectations = passedExpectations,
            FailedExpectations = report.Expectations.Count
                + report.PressureDiagnostics.Count
                + report.BracketDiagnostics.Count
                - passedExpectations,
            RequiredExpectationCount = requiredExpectations,
            PassedRequiredExpectations = passedRequiredExpectations,
            FailedRequiredExpectations = requiredExpectations - passedRequiredExpectations,
            AdvisoryExpectationCount = advisoryExpectations,
            PassedAdvisoryExpectations = passedAdvisoryExpectations,
            FailedAdvisoryExpectations = advisoryExpectations - passedAdvisoryExpectations,
            NearMissExpectations = nearMissExpectations,
            ProfileSweepCount = report.ProfileSweeps.Count,
            ProfileSensitivityCount = report.ProfileSensitivity.Count,
            PressureDiagnosticCount = report.PressureDiagnostics.Count,
            BracketDiagnosticCount = report.BracketDiagnostics.Count,
            DriftFailures = driftFailures,
            ModelVersion = report.Fixtures.Count == 0 ? "" : report.Fixtures[0].ModelVersion,
        };
    }

    /// <summary>
    /// Adds explanatory notes that protect calibration output from overclaiming.
    /// </summary>
    private static void AddReportNotes(StatsLabCalibrationReport report, CalibrationOptions options)
    {
        report.Notes.Add("Calibration labels are advisory benchmark labels, not ground truth win-rate labels.");
        report.Notes.Add("Pairwise expectations compare Stats Lab metric behavior, not true multiplayer win rates.");
        report.Notes.Add("Pressure diagnostics compare heuristic metric resilience against source-fixture pressure profiles, not game outcomes.");
        report.Notes.Add("Bracket diagnostics compare advisory Commander bracket ranges, not official bracket determinations.");
        report.Notes.Add("Required expectation failures fail the CLI by default; advisory expectation failures are diagnostic warnings.");
        if (options.SyntheticOnly)
        {
            report.Notes.Add("Synthetic-only mode skipped checked-in benchmark corpus files.");
        }
        else
        {
            report.Notes.Add("Benchmark corpus fixtures are source-attributed offline snapshots; calibration does not fetch live deck data.");
        }

        if (string.IsNullOrWhiteSpace(options.BaselinePath))
        {
            report.Notes.Add("No saved baseline was supplied; drift comparison was skipped.");
        }

        if (options.ProfileSweepIds.Count > 0)
        {
            report.Notes.Add("Profile sweeps are diagnostics only; they do not affect expectation pass/fail.");
            report.Notes.Add("Profile sensitivity diagnostics compare existing SimulationProfile behavior, not a new policy layer.");
        }
    }

    /// <summary>
    /// Checks whether an expectation severity should affect CLI pass/fail.
    /// </summary>
    private static bool IsRequired(string severity)
    {
        return severity.Equals(CalibrationExpectationSeverity.Required, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether an expectation uses the pressure diagnostic shape.
    /// </summary>
    private static bool IsPressureExpectation(CalibrationExpectation expectation)
    {
        return expectation.Kind.Equals(CalibrationExpectationKind.Pressure, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether an expectation uses the Commander bracket range shape.
    /// </summary>
    private static bool IsBracketRangeExpectation(CalibrationExpectation expectation)
    {
        return expectation.Kind.Equals(CalibrationExpectationKind.BracketRange, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Provides the unused catalog dependency required by DeckAnalysisMetrics.
    /// </summary>
    private sealed class EmptyCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Shared empty catalog instance.
        /// </summary>
        public static readonly EmptyCardCatalog Instance = new();

        /// <summary>
        /// Prevents callers from creating redundant empty catalog instances.
        /// </summary>
        private EmptyCardCatalog()
        {
        }

        /// <summary>
        /// Returns no cards for text searches because bracket calibration only needs deck-level metrics.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns no cards for structured searches because bracket calibration supplies cards directly.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns no card detail because bracket calibration does not look up catalog metadata.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<CardInfo?>(null);
        }

        /// <summary>
        /// Returns no card details for batch lookups because calibration fixtures already contain names.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(
                new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns no rulings because bracket calibration does not evaluate card rules text.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Returns no print variants because calibration fixtures are already normalized.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Returns no suggestions because bracket calibration never asks the catalog to recommend cards.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }
    }
}
