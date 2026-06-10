namespace MtgMcp.Core;

/// <summary>
/// Contains bounded trace-summary helpers for Stats Lab reports.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Limits sampled traces so large simulations stay compact.
    /// </summary>
    private const int SampledTraceRunCount = 3;

    /// <summary>
    /// Limits decision events per sampled run so trace payloads remain bounded.
    /// </summary>
    private const int PerformanceDecisionEventLimit = 80;

    /// <summary>
    /// Builds aggregate and sampled trace summaries from completed runs.
    /// </summary>
    private static PerformanceTraceSummary BuildTraceSummary(
        IReadOnlyList<PerformanceRun> runs,
        int seed)
    {
        int mulliganedRuns = 0;
        int noMulliganRuns = 0;
        int keptSevenRuns = 0;
        int landDropsMade = 0;
        int interactionHeldUpTurns = 0;
        int commanderCastRuns = 0;
        int comboAssemblyRuns = 0;
        int strandedRiskRuns = 0;

        foreach (PerformanceRun run in runs)
        {
            if (run.Mulligans > 0)
            {
                mulliganedRuns++;
            }
            else
            {
                noMulliganRuns++;
            }

            if (run.KeptHandSize == 7)
            {
                keptSevenRuns++;
            }

            if (run.CommanderCastTurn.HasValue)
            {
                commanderCastRuns++;
            }

            if (run.ComboAssemblyTurn.HasValue)
            {
                comboAssemblyRuns++;
            }

            if (run.StrandedCards.Count > 0)
            {
                strandedRiskRuns++;
            }

            foreach (PerformanceTurnState turn in run.Turns)
            {
                if (turn.LandDropMade)
                {
                    landDropsMade++;
                }

                if (turn.InteractionHeldUp)
                {
                    interactionHeldUpTurns++;
                }
            }
        }

        PerformanceTraceSummary summary = new()
        {
            AggregateCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["total-runs"] = runs.Count,
                ["mulliganed-runs"] = mulliganedRuns,
                ["no-mulligan-runs"] = noMulliganRuns,
                ["kept-seven-runs"] = keptSevenRuns,
                ["land-drops-made"] = landDropsMade,
                ["interaction-held-up-turns"] = interactionHeldUpTurns,
                ["commander-cast-runs"] = commanderCastRuns,
                ["combo-assembly-runs"] = comboAssemblyRuns,
                ["stranded-risk-runs"] = strandedRiskRuns,
            },
            Notes =
            [
                "Trace summaries are bounded deterministic samples, aggregate counters, and compact decision events, not full play logs.",
            ],
        };

        for (int index = 0; index < Math.Min(SampledTraceRunCount, runs.Count); index++)
        {
            PerformanceRun run = runs[index];
            int landDropsMadeForRun = 0;
            List<int> interactionHeldUpTurnsForRun = [];
            foreach (PerformanceTurnState turn in run.Turns)
            {
                if (turn.LandDropMade)
                {
                    landDropsMadeForRun++;
                }

                if (turn.InteractionHeldUp)
                {
                    interactionHeldUpTurnsForRun.Add(turn.Turn);
                }
            }

            summary.SampledRuns.Add(new PerformanceTraceRunSummary
            {
                RunIndex = index,
                Seed = run.Seed,
                Mulligans = run.Mulligans,
                KeptHandSize = run.KeptHandSize,
                KeptOpeningLands = run.KeptOpeningLands,
                LandDropsMade = landDropsMadeForRun,
                CommanderCastTurn = run.CommanderCastTurn,
                BackgroundCastTurn = run.BackgroundCastTurn,
                ComboAssemblyTurn = run.ComboAssemblyTurn,
                TutorAssistedComboTurn = run.TutorAssistedComboTurn,
                StrandedCardCount = run.StrandedCards.Count,
                InteractionHeldUpTurns = interactionHeldUpTurnsForRun,
                DecisionEvents = run.DecisionEvents.Take(PerformanceDecisionEventLimit).ToList(),
            });
        }

        return summary;
    }
}
