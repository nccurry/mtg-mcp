using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Shapes performance-analysis MCP output without changing Core analysis models.
/// </summary>
internal static class PerformanceOutputPresenter
{
    /// <summary>
    /// Caps scenario rows in summary output.
    /// </summary>
    private const int SummaryScenarioLimit = 5;

    /// <summary>
    /// Caps scenario rows in normal output.
    /// </summary>
    private const int NormalScenarioLimit = 12;

    /// <summary>
    /// Caps stranded-card rows in summary output.
    /// </summary>
    private const int SummaryStrandedCardLimit = 5;

    /// <summary>
    /// Caps stranded-card rows in normal output.
    /// </summary>
    private const int NormalStrandedCardLimit = 10;

    /// <summary>
    /// Caps scorecard rows in summary output.
    /// </summary>
    private const int SummaryScorecardLimit = 6;

    /// <summary>
    /// Caps scorecard rows in normal output.
    /// </summary>
    private const int NormalScorecardLimit = 12;

    /// <summary>
    /// Caps warnings in summary output.
    /// </summary>
    private const int SummaryWarningLimit = 8;

    /// <summary>
    /// Caps warnings in normal output.
    /// </summary>
    private const int NormalWarningLimit = 16;

    /// <summary>
    /// Presents one performance analysis at the requested detail level.
    /// </summary>
    public static object Present(DeckPerformanceAnalysis result, string? detailLevel)
    {
        DetailLevel normalized = DetailLevelParser.Parse(detailLevel, DetailLevel.Full);
        if (normalized == DetailLevel.Full)
        {
            return result;
        }

        return PresentAnalysis(result, normalized);
    }

    /// <summary>
    /// Presents a before/after performance comparison at the requested detail level.
    /// </summary>
    public static object Present(DeckPerformanceComparison result, string? detailLevel)
    {
        DetailLevel normalized = DetailLevelParser.Parse(detailLevel, DetailLevel.Full);
        if (normalized == DetailLevel.Full)
        {
            return result;
        }
        string normalizedName = normalized.ToWireName();

        return new
        {
            detailLevel = normalizedName,
            planId = result.PlanId,
            workspaceId = result.WorkspaceId,
            before = PresentAnalysis(result.Before, normalized),
            after = PresentAnalysis(result.After, normalized),
            deltas = PresentDeltas(result.Deltas, normalized),
            warnings = Limit(result.Warnings, normalized, SummaryWarningLimit, NormalWarningLimit),
        };
    }

    /// <summary>
    /// Presents bounded analysis output.
    /// </summary>
    private static object PresentAnalysis(DeckPerformanceAnalysis result, DetailLevel detailLevel)
    {
        string detailLevelName = detailLevel.ToWireName();
        return new
        {
            detailLevel = detailLevelName,
            workspaceId = result.WorkspaceId,
            modelLabel = result.ModelLabel,
            schemaVersion = result.SchemaVersion,
            modelVersion = result.ModelVersion,
            profile = result.Profile,
            profileResolution = new
            {
                id = result.ProfileResolution.Profile.Id,
                name = result.ProfileResolution.Profile.Name,
                source = result.ProfileResolution.Source,
                warnings = Limit(result.ProfileResolution.Warnings, detailLevel, 4, 8),
            },
            replay = new
            {
                deckFingerprint = result.DeckFingerprint,
                cardDataFingerprint = result.CardDataFingerprint,
                profileFingerprint = result.ProfileFingerprint,
                rngKind = result.RngKind,
                seed = result.Seed,
            },
            settings = new
            {
                simulations = result.Simulations,
                maxTurn = result.MaxTurn,
                includeMulligans = result.IncludeMulligans,
                deckSize = result.DeckSize,
            },
            commanderContext = PresentCommanderContext(result),
            keyMetrics = PresentKeyMetrics(result),
            scorecard = PresentScorecard(result.Scorecard, detailLevel),
            failedScenarios = PresentFailedScenarios(result.Scenarios, detailLevel),
            topStrandedCards = PresentStrandedCards(result.StrandedCards, detailLevel),
            warnings = Limit(result.Warnings, detailLevel, SummaryWarningLimit, NormalWarningLimit),
            assumptions = Limit(result.Assumptions, detailLevel, 6, 12),
            traceSummary = detailLevel == DetailLevel.Normal
                ? PresentTraceSummary(result.TraceSummary)
                : null,
        };
    }

    /// <summary>
    /// Presents command-zone context without the full turn tables.
    /// </summary>
    private static object PresentCommanderContext(DeckPerformanceAnalysis result)
    {
        return new
        {
            commandZoneNames = result.CommandZone.CommandZoneNames,
            commanderNames = result.CommandZone.CommanderNames.Count > 0
                ? result.CommandZone.CommanderNames
                : result.Commander.CommanderNames,
            backgroundNames = result.CommandZone.BackgroundNames,
            averageCommanderCastTurn = result.CommandZone.AverageCommanderCastTurn
                ?? result.Commander.AverageEarliestCastTurn,
            averageBackgroundCastTurn = result.CommandZone.AverageBackgroundCastTurn,
            averageCommanderWithBackgroundOnlineTurn = result.CommandZone.AverageCommanderWithBackgroundOnlineTurn,
        };
    }

    /// <summary>
    /// Presents headline metrics that are useful in compact output.
    /// </summary>
    private static object PresentKeyMetrics(DeckPerformanceAnalysis result)
    {
        return new
        {
            sevenCardKeepRate = result.OpeningHands.SevenCardKeepRate,
            averageMulligans = result.OpeningHands.AverageMulligans,
            averageKeptLands = result.OpeningHands.AverageKeptLands,
            commanderCastByMaxTurn = LastProbability(result.Commander.CastByTurn),
            commanderProtectedByMaxTurn = LastProbability(result.Commander.ProtectedByTurn),
            comboAssemblyByMaxTurn = LastProbability(result.ComboAssembly.AssemblyByTurn),
            tutorAssistedComboByMaxTurn = LastProbability(result.ComboAssembly.TutorAssistedAssemblyByTurn),
            finalCastableHandRate = LastAverage(result.Castability.SpellCastabilityByTurn),
            finalCardsInHand = LastAverage(result.TurnAverages, "cards-in-hand"),
            finalAvailableMana = LastAverage(result.TurnAverages, "available-mana-after-development"),
        };
    }

    /// <summary>
    /// Presents bounded scorecard dimensions, lowest score first.
    /// </summary>
    private static List<object> PresentScorecard(PerformanceScorecard scorecard, DetailLevel detailLevel)
    {
        List<PerformanceScorecardDimension> dimensions = scorecard.Dimensions.ToList();
        dimensions.Sort(static (left, right) =>
        {
            int scoreComparison = left.Score.CompareTo(right.Score);
            return scoreComparison != 0
                ? scoreComparison
                : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        int limit = detailLevel == DetailLevel.Normal ? NormalScorecardLimit : SummaryScorecardLimit;
        List<object> result = [];
        foreach (PerformanceScorecardDimension dimension in dimensions.Take(limit))
        {
            result.Add(new
            {
                dimension.Name,
                dimension.Score,
                dimension.SourceMetric,
                dimension.Rationale,
            });
        }

        return result;
    }

    /// <summary>
    /// Presents failed scenarios and risk scenarios with bounded failure evidence.
    /// </summary>
    private static List<object> PresentFailedScenarios(IReadOnlyList<ScenarioPerformance> scenarios, DetailLevel detailLevel)
    {
        List<ScenarioPerformance> failed = [];
        foreach (ScenarioPerformance scenario in scenarios)
        {
            if (IsScenarioFailure(scenario))
            {
                failed.Add(scenario);
            }
        }

        failed.Sort(static (left, right) =>
        {
            int severityComparison = ScenarioSeverity(right).CompareTo(ScenarioSeverity(left));
            return severityComparison != 0
                ? severityComparison
                : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        int limit = detailLevel == DetailLevel.Normal ? NormalScenarioLimit : SummaryScenarioLimit;
        List<object> result = [];
        foreach (ScenarioPerformance scenario in failed.Take(limit))
        {
            result.Add(new
            {
                scenario.Name,
                scenario.TargetTurn,
                scenario.SuccessRate,
                scenario.LowConfidenceInterval,
                scenario.HighConfidenceInterval,
                scenario.SampleSize,
                relevantCards = scenario.RelevantCards.Take(6).ToList(),
                failureDrivers = scenario.FailureDrivers.Take(5).ToList(),
                failureDriverCounts = TopFailureDriverCounts(scenario),
            });
        }

        return result;
    }

    /// <summary>
    /// Checks whether a scenario should be treated as a compact-output failure or risk.
    /// </summary>
    private static bool IsScenarioFailure(ScenarioPerformance scenario)
    {
        if (scenario.Name.Contains("risk", StringComparison.OrdinalIgnoreCase))
        {
            return scenario.SuccessRate > 0;
        }

        return scenario.SuccessRate < 0.5;
    }

    /// <summary>
    /// Scores compact scenario severity so risks and missed success targets sort together.
    /// </summary>
    private static double ScenarioSeverity(ScenarioPerformance scenario)
    {
        return scenario.Name.Contains("risk", StringComparison.OrdinalIgnoreCase)
            ? scenario.SuccessRate
            : 1 - scenario.SuccessRate;
    }

    /// <summary>
    /// Returns the largest failure-driver counts without exposing every counter.
    /// </summary>
    private static Dictionary<string, int> TopFailureDriverCounts(ScenarioPerformance scenario)
    {
        List<KeyValuePair<string, int>> counts = scenario.FailureDriverCounts.ToList();
        counts.Sort(static (left, right) =>
        {
            int countComparison = right.Value.CompareTo(left.Value);
            return countComparison != 0
                ? countComparison
                : string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase);
        });

        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> count in counts.Take(5))
        {
            result[count.Key] = count.Value;
        }

        return result;
    }

    /// <summary>
    /// Presents the most frequently stranded cards.
    /// </summary>
    private static List<object> PresentStrandedCards(IReadOnlyList<StrandedCardPerformance> strandedCards, DetailLevel detailLevel)
    {
        List<StrandedCardPerformance> cards = strandedCards.ToList();
        cards.Sort(static (left, right) =>
        {
            int rateComparison = right.StrandedRate.CompareTo(left.StrandedRate);
            return rateComparison != 0
                ? rateComparison
                : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
        });

        int limit = detailLevel == DetailLevel.Normal ? NormalStrandedCardLimit : SummaryStrandedCardLimit;
        List<object> result = [];
        foreach (StrandedCardPerformance card in cards.Take(limit))
        {
            result.Add(new
            {
                card.CardName,
                card.ManaValue,
                card.StrandedRate,
                card.ManaStrandedRate,
                card.ColorStrandedRate,
                card.SampleSize,
            });
        }

        return result;
    }

    /// <summary>
    /// Presents bounded deltas for before/after comparisons.
    /// </summary>
    private static List<object> PresentDeltas(IReadOnlyList<PerformanceDelta> deltas, DetailLevel detailLevel)
    {
        List<PerformanceDelta> sorted = deltas.ToList();
        sorted.Sort(static (left, right) =>
        {
            int deltaComparison = Math.Abs(right.Delta).CompareTo(Math.Abs(left.Delta));
            return deltaComparison != 0
                ? deltaComparison
                : string.Compare(left.Metric, right.Metric, StringComparison.OrdinalIgnoreCase);
        });

        int limit = detailLevel == DetailLevel.Normal ? 16 : 8;
        List<object> result = [];
        foreach (PerformanceDelta delta in sorted.Take(limit))
        {
            result.Add(new
            {
                delta.Metric,
                delta.Before,
                delta.After,
                delta.Delta,
                delta.ConfidenceIntervalsOverlap,
            });
        }

        return result;
    }

    /// <summary>
    /// Presents aggregate trace counters without sampled decision logs.
    /// </summary>
    private static object PresentTraceSummary(PerformanceTraceSummary traceSummary)
    {
        return new
        {
            aggregateCounters = traceSummary.AggregateCounters,
            sampledRunCount = traceSummary.SampledRuns.Count,
            notes = traceSummary.Notes.Take(5).ToList(),
        };
    }

    /// <summary>
    /// Finds the last probability row by turn.
    /// </summary>
    private static PerformanceProbability? LastProbability(IReadOnlyList<PerformanceProbability> probabilities)
    {
        PerformanceProbability? result = null;
        foreach (PerformanceProbability probability in probabilities)
        {
            if (result is null || probability.Turn > result.Turn)
            {
                result = probability;
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the last average row by turn.
    /// </summary>
    private static PerformanceAverage? LastAverage(IReadOnlyList<PerformanceAverage> averages)
    {
        PerformanceAverage? result = null;
        foreach (PerformanceAverage average in averages)
        {
            if (result is null || average.Turn > result.Turn)
            {
                result = average;
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the last average row for a metric by turn.
    /// </summary>
    private static PerformanceAverage? LastAverage(IReadOnlyList<PerformanceAverage> averages, string name)
    {
        PerformanceAverage? result = null;
        foreach (PerformanceAverage average in averages)
        {
            if (!average.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (result is null || average.Turn > result.Turn)
            {
                result = average;
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a bounded copy of a string list for the requested detail level.
    /// </summary>
    private static List<string> Limit(
        IReadOnlyList<string> values,
        DetailLevel detailLevel,
        int summaryLimit,
        int normalLimit)
    {
        int limit = detailLevel == DetailLevel.Normal ? normalLimit : summaryLimit;
        return values.Take(limit).ToList();
    }
}
