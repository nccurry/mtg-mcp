namespace MtgMcp.Core;

/// <summary>
/// Contains derived scorecard helpers for Stats Lab reports.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Builds metric dimensions that can be compared without implying a universal power score.
    /// </summary>
    private static PerformanceScorecard BuildPerformanceScorecard(DeckPerformanceAnalysis analysis)
    {
        int earlyTurn = Math.Min(3, analysis.MaxTurn);
        int interactionTurn = Math.Min(4, analysis.MaxTurn);
        PerformanceScorecard scorecard = new();
        scorecard.Dimensions.Add(new PerformanceScorecardDimension
        {
            Name = "mana-stability",
            Score = AverageKnown(
                Probability(analysis, "land-drop-by-turn", earlyTurn),
                Probability(analysis, "on-curve-untapped-mana-by-turn", earlyTurn),
                Probability(analysis, "all-deck-colors-by-turn", earlyTurn)),
            SourceMetric = $"land-drop/on-curve/colors-by-turn-{earlyTurn}",
            Rationale = "Tracks early land drops, untapped mana, and color access when color identity is known."
        });
        scorecard.Dimensions.Add(new PerformanceScorecardDimension
        {
            Name = "early-development",
            Score = AverageKnown(
                Probability(analysis, "ramp-cast-by-turn", earlyTurn),
                Probability(analysis, "draw-cast-by-turn", earlyTurn),
                AverageRatio(analysis, "cards-in-hand", earlyTurn, denominator: 7)),
            SourceMetric = $"ramp/draw/cards-by-turn-{earlyTurn}",
            Rationale = "Tracks whether the deck converts early turns into ramp, card flow, or retained resources."
        });
        scorecard.Dimensions.Add(new PerformanceScorecardDimension
        {
            Name = "interaction-readiness",
            Score = Probability(analysis, "interaction-held-up-by-turn", interactionTurn) ?? 0,
            SourceMetric = $"interaction-held-up-by-turn-{interactionTurn}",
            Rationale = "Tracks whether interaction remains in hand and payable after development."
        });
        scorecard.Dimensions.Add(new PerformanceScorecardDimension
        {
            Name = "route-assembly",
            Score = ScenarioRate(analysis, "combo-or-tutor-assembly-by-turn-5")
                ?? Probability(analysis, "tutor-assisted-combo-by-turn", analysis.MaxTurn)
                ?? 0,
            SourceMetric = "combo-or-tutor-assembly",
            Rationale = "Tracks access to configured or heuristic combo/tutor assembly, not all possible win routes."
        });
        scorecard.Dimensions.Add(new PerformanceScorecardDimension
        {
            Name = "castability",
            Score = Average(analysis, "castable-nonland-hand-rate", analysis.MaxTurn) ?? 0,
            SourceMetric = $"castable-nonland-hand-rate-turn-{analysis.MaxTurn}",
            Rationale = "Tracks the share of nonland hand cards payable with available mana sources."
        });
        scorecard.Dimensions.Add(new PerformanceScorecardDimension
        {
            Name = "stranded-resilience",
            Score = 1 - (ScenarioRate(analysis, "stranded-high-mana-risk-by-max-turn") ?? 0),
            SourceMetric = "stranded-high-mana-risk-by-max-turn",
            Rationale = "Higher is better; this inverts the final-turn stranded-card risk scenario."
        });
        foreach (PerformanceScorecardDimension dimension in scorecard.Dimensions)
        {
            dimension.Score = Math.Clamp(dimension.Score, 0, 1);
        }

        return scorecard;
    }

    /// <summary>
    /// Reads a probability row by metric and turn.
    /// </summary>
    private static double? Probability(DeckPerformanceAnalysis analysis, string metric, int turn)
    {
        return analysis.TurnProbabilities
            .FirstOrDefault(row => row.Name.Equals(metric, StringComparison.OrdinalIgnoreCase) && row.Turn == turn)
            ?.Probability;
    }

    /// <summary>
    /// Reads an average row by metric and turn.
    /// </summary>
    private static double? Average(DeckPerformanceAnalysis analysis, string metric, int turn)
    {
        PerformanceAverage? row = analysis.TurnAverages
            .FirstOrDefault(row => row.Name.Equals(metric, StringComparison.OrdinalIgnoreCase) && row.Turn == turn);
        row ??= analysis.Castability.SpellCastabilityByTurn
            .FirstOrDefault(candidate => candidate.Name.Equals(metric, StringComparison.OrdinalIgnoreCase) && candidate.Turn == turn);
        return row?.Average;
    }

    /// <summary>
    /// Reads an average row and normalizes it against a denominator.
    /// </summary>
    private static double? AverageRatio(
        DeckPerformanceAnalysis analysis,
        string metric,
        int turn,
        double denominator)
    {
        double? value = Average(analysis, metric, turn);
        return value.HasValue && denominator > 0 ? value.Value / denominator : null;
    }

    /// <summary>
    /// Reads a scenario success rate by name.
    /// </summary>
    private static double? ScenarioRate(DeckPerformanceAnalysis analysis, string scenarioName)
    {
        return analysis.Scenarios
            .FirstOrDefault(row => row.Name.Equals(scenarioName, StringComparison.OrdinalIgnoreCase))
            ?.SuccessRate;
    }

    /// <summary>
    /// Averages non-null values, returning zero when no values are available.
    /// </summary>
    private static double AverageKnown(params double?[] values)
    {
        double total = 0;
        int count = 0;
        foreach (double? value in values)
        {
            if (value.HasValue)
            {
                total += value.Value;
                count++;
            }
        }

        return count == 0 ? 0 : total / count;
    }
}
