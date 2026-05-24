namespace MtgMcp.Core;

/// <summary>
/// Builds before-and-after performance deltas for previewed deck edit plans.
/// </summary>
internal static class DeckPerformanceComparisonBuilder
{
    /// <summary>
    /// Builds headline before-and-after deltas for plan comparison.
    /// </summary>
    public static List<PerformanceDelta> BuildDeltas(
        DeckPerformanceAnalysis before,
        DeckPerformanceAnalysis after)
    {
        List<PerformanceDelta> deltas =
        [
            BuildDelta("seven-card-keep-rate", before.OpeningHands.SevenCardKeepRate, after.OpeningHands.SevenCardKeepRate),
            BuildDelta("average-mulligans", before.OpeningHands.AverageMulligans, after.OpeningHands.AverageMulligans),
            BuildDelta("average-kept-hand-size", before.OpeningHands.AverageKeptHandSize, after.OpeningHands.AverageKeptHandSize),
            BuildScenarioDelta("commander-by-turn-4", before, after, "commander-by-turn-4"),
            BuildScenarioDelta("all-colors-by-turn-3", before, after, "all-colors-by-turn-3"),
            BuildProbabilityDelta("ramp-cast-by-turn-3", before, after, "ramp-cast-by-turn", 3),
            BuildProbabilityDelta("draw-cast-by-turn-4", before, after, "draw-cast-by-turn", 4),
            BuildScenarioDelta("interaction-held-up-by-turn-4", before, after, "hold-up-interaction-by-turn-4"),
            BuildScenarioDelta("combo-or-tutor-by-turn-5", before, after, "combo-or-tutor-assembly-by-turn-5"),
            BuildScenarioDelta("stranded-high-mana-risk", before, after, "stranded-high-mana-risk-by-max-turn"),
        ];

        return deltas;
    }

    /// <summary>
    /// Creates one numeric performance delta.
    /// </summary>
    private static PerformanceDelta BuildDelta(
        string metric,
        double before,
        double after,
        double? beforeLow = null,
        double? beforeHigh = null,
        double? afterLow = null,
        double? afterHigh = null)
    {
        return new PerformanceDelta
        {
            Metric = metric,
            Before = before,
            After = after,
            Delta = after - before,
            BeforeLowConfidenceInterval = beforeLow,
            BeforeHighConfidenceInterval = beforeHigh,
            AfterLowConfidenceInterval = afterLow,
            AfterHighConfidenceInterval = afterHigh,
            ConfidenceIntervalsOverlap = IntervalsOverlap(beforeLow, beforeHigh, afterLow, afterHigh),
        };
    }

    /// <summary>
    /// Creates a delta row for a named scenario metric.
    /// </summary>
    private static PerformanceDelta BuildScenarioDelta(
        string metric,
        DeckPerformanceAnalysis before,
        DeckPerformanceAnalysis after,
        string scenarioName)
    {
        ScenarioPerformance? beforeScenario = FindScenario(before, scenarioName);
        ScenarioPerformance? afterScenario = FindScenario(after, scenarioName);
        return BuildDelta(
            metric,
            beforeScenario?.SuccessRate ?? 0,
            afterScenario?.SuccessRate ?? 0,
            beforeScenario?.LowConfidenceInterval,
            beforeScenario?.HighConfidenceInterval,
            afterScenario?.LowConfidenceInterval,
            afterScenario?.HighConfidenceInterval);
    }

    /// <summary>
    /// Creates a delta row for a named turn probability metric.
    /// </summary>
    private static PerformanceDelta BuildProbabilityDelta(
        string metric,
        DeckPerformanceAnalysis before,
        DeckPerformanceAnalysis after,
        string probabilityName,
        int turn)
    {
        PerformanceProbability? beforeProbability = FindProbability(before, probabilityName, turn);
        PerformanceProbability? afterProbability = FindProbability(after, probabilityName, turn);
        return BuildDelta(
            metric,
            beforeProbability?.Probability ?? 0,
            afterProbability?.Probability ?? 0,
            beforeProbability?.LowConfidenceInterval,
            beforeProbability?.HighConfidenceInterval,
            afterProbability?.LowConfidenceInterval,
            afterProbability?.HighConfidenceInterval);
    }

    /// <summary>
    /// Checks whether two confidence intervals overlap when all bounds are known.
    /// </summary>
    private static bool? IntervalsOverlap(
        double? beforeLow,
        double? beforeHigh,
        double? afterLow,
        double? afterHigh)
    {
        if (!beforeLow.HasValue || !beforeHigh.HasValue || !afterLow.HasValue || !afterHigh.HasValue)
        {
            return null;
        }

        return beforeLow <= afterHigh && afterLow <= beforeHigh;
    }

    /// <summary>
    /// Reads a scenario row from an analysis result.
    /// </summary>
    private static ScenarioPerformance? FindScenario(DeckPerformanceAnalysis analysis, string name)
    {
        return analysis.Scenarios
            .FirstOrDefault(scenario => scenario.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a turn probability row from an analysis result.
    /// </summary>
    private static PerformanceProbability? FindProbability(DeckPerformanceAnalysis analysis, string name, int turn)
    {
        int clampedTurn = Math.Min(turn, analysis.MaxTurn);
        return analysis.TurnProbabilities
            .FirstOrDefault(probability =>
                probability.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && probability.Turn == clampedTurn);
    }
}
