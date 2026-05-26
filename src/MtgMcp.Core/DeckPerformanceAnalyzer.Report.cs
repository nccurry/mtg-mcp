namespace MtgMcp.Core;

/// <summary>
/// Contains report-shaping helpers for performance analysis output.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Aggregates opening hand and mulligan metrics from runs.
    /// </summary>
    private static OpeningHandPerformance BuildOpeningHandPerformance(
        IReadOnlyList<PerformanceRun> runs)
    {
        OpeningHandPerformance result = new()
        {
            SevenCardKeepRate = PerformanceStatistics.Rate(runs.Count(run => run.Mulligans == 0), runs.Count),
            AverageMulligans = runs.Count == 0 ? 0 : runs.Average(run => run.Mulligans),
            AverageKeptHandSize = runs.Count == 0 ? 0 : runs.Average(run => run.KeptHandSize),
            AverageKeptLands = runs.Count == 0 ? 0 : runs.Average(run => run.KeptOpeningLands),
            NoLandSevenRate = PerformanceStatistics.Rate(runs.Count(run => run.OpeningSevenLands == 0), runs.Count),
            OneLandSevenRate = PerformanceStatistics.Rate(runs.Count(run => run.OpeningSevenLands == 1), runs.Count),
            FloodedSevenRate = PerformanceStatistics.Rate(runs.Count(run => run.OpeningSevenLands >= 6), runs.Count),
        };

        foreach (IGrouping<int, PerformanceRun> group in runs.GroupBy(run => run.Mulligans))
        {
            result.MulliganDistribution[group.Key] = group.Count();
        }

        return result;
    }

    /// <summary>
    /// Adds shared turn-by-turn probability and average metrics.
    /// </summary>
    private static void AddTurnPerformanceMetrics(
        DeckPerformanceAnalysis analysis,
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlySet<string> deckColors,
        bool colorIdentityKnown,
        int maxTurn)
    {
        for (int turn = 1; turn <= maxTurn; turn++)
        {
            List<PerformanceTurnState> states = StatesForTurn(runs, turn);
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "land-drop-by-turn",
                turn,
                states.Count(state => state.LandsInPlay >= Math.Min(turn, 10)),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "ramp-seen-by-turn",
                turn,
                states.Count(state => state.RampSeenByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "ramp-cast-by-turn",
                turn,
                states.Count(state => state.RampCastByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "draw-seen-by-turn",
                turn,
                states.Count(state => state.DrawSeenByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "draw-cast-by-turn",
                turn,
                states.Count(state => state.DrawCastByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "interaction-seen-by-turn",
                turn,
                states.Count(state => state.InteractionSeenByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "interaction-held-up-by-turn",
                turn,
                states.Count(state => state.InteractionHeldUp),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "on-curve-untapped-mana-by-turn",
                turn,
                states.Count(state => state.OnCurveUntappedMana),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "background-cast-by-turn",
                turn,
                states.Count(state => state.BackgroundCastByTurn),
                states.Count));
            analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                "commander-with-background-online-by-turn",
                turn,
                states.Count(state => state.CommanderWithBackgroundOnlineByTurn),
                states.Count));
            if (colorIdentityKnown && deckColors.Count > 0)
            {
                analysis.TurnProbabilities.Add(PerformanceStatistics.BuildProbability(
                    "all-deck-colors-by-turn",
                    turn,
                    states.Count(state => state.AllDeckColorsAvailable),
                    states.Count));
            }

            analysis.TurnAverages.Add(PerformanceStatistics.BuildAverage(
                "available-mana-after-development",
                turn,
                states.Select(state => (double)state.AvailableMana).ToList()));
            analysis.TurnAverages.Add(PerformanceStatistics.BuildAverage(
                "cards-in-hand",
                turn,
                states.Select(state => (double)state.CardsInHand).ToList()));
        }
    }

    /// <summary>
    /// Builds spell castability and color reliability metrics.
    /// </summary>
    private static CastabilityPerformance BuildCastabilityPerformance(
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlySet<string> deckColors,
        bool colorIdentityKnown,
        int maxTurn)
    {
        CastabilityPerformance result = new();
        for (int turn = 1; turn <= maxTurn; turn++)
        {
            List<PerformanceTurnState> states = StatesForTurn(runs, turn);
            result.SpellCastabilityByTurn.Add(PerformanceStatistics.BuildAverage(
                "castable-nonland-hand-rate",
                turn,
                states.Select(state => state.CastableHandRate).ToList()));

            if (!colorIdentityKnown)
            {
                continue;
            }

            foreach (string color in deckColors.Order(StringComparer.OrdinalIgnoreCase))
            {
                result.ColorSourceReliability.Add(PerformanceStatistics.BuildProbability(
                    $"source-{color}-by-turn",
                    turn,
                    states.Count(state => state.ColorSources.Contains(color)),
                    states.Count));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds commander cast and protection timing metrics.
    /// </summary>
    private static CommanderPerformance BuildCommanderPerformance(
        IReadOnlyList<PerformanceRun> runs,
        int maxTurn,
        CommandZonePlan commandZonePlan)
    {
        CommanderPerformance result = new()
        {
            CommanderNames = commandZonePlan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Commander)
                .Select(card => card.Card.Name)
                .ToList(),
        };
        if (result.CommanderNames.Count == 0)
        {
            return result;
        }

        List<int> castTurns = runs
            .Where(run => run.CommanderCastTurn.HasValue)
            .Select(run => run.CommanderCastTurn!.Value)
            .ToList();
        result.AverageEarliestCastTurn = castTurns.Count == 0 ? null : castTurns.Average();

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            result.CastByTurn.Add(PerformanceStatistics.BuildProbability(
                "commander-cast-by-turn",
                turn,
                runs.Count(run => run.CommanderCastTurn <= turn),
                runs.Count));
            result.ProtectedByTurn.Add(PerformanceStatistics.BuildProbability(
                "commander-protected-by-turn",
                turn,
                runs.Count(run => run.CommanderProtectedTurn <= turn),
                runs.Count));
        }

        return result;
    }

    /// <summary>
    /// Builds command-zone timing metrics.
    /// </summary>
    private static CommandZonePerformance BuildCommandZonePerformance(
        IReadOnlyList<PerformanceRun> runs,
        int maxTurn,
        CommandZonePlan commandZonePlan)
    {
        CommandZonePerformance result = new()
        {
            CommandZoneNames = commandZonePlan.Cards.Select(card => card.Card.Name).ToList(),
            CommanderNames = commandZonePlan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Commander)
                .Select(card => card.Card.Name)
                .ToList(),
            BackgroundNames = commandZonePlan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Background)
                .Select(card => card.Card.Name)
                .ToList(),
            AverageCommanderCastTurn = AveragePerformanceTurn(runs.Select(run => run.CommanderCastTurn)),
            AverageBackgroundCastTurn = AveragePerformanceTurn(runs.Select(run => run.BackgroundCastTurn)),
            AverageCommanderWithBackgroundOnlineTurn = AveragePerformanceTurn(runs.Select(run => run.CommanderWithBackgroundOnlineTurn)),
        };

        if (commandZonePlan.Cards.Count == 0)
        {
            return result;
        }

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            result.CommanderCastByTurn.Add(PerformanceStatistics.BuildProbability(
                "commander-cast-by-turn",
                turn,
                runs.Count(run => run.CommanderCastTurn <= turn),
                runs.Count));
            result.BackgroundCastByTurn.Add(PerformanceStatistics.BuildProbability(
                "background-cast-by-turn",
                turn,
                runs.Count(run => run.BackgroundCastTurn <= turn),
                runs.Count));
            result.CommanderWithBackgroundOnlineByTurn.Add(PerformanceStatistics.BuildProbability(
                "commander-with-background-online-by-turn",
                turn,
                runs.Count(run => run.CommanderWithBackgroundOnlineTurn <= turn),
                runs.Count));
        }

        return result;
    }

    /// <summary>
    /// Averages observed turn values while ignoring runs where the event did not occur.
    /// </summary>
    private static double? AveragePerformanceTurn(IEnumerable<int?> turns)
    {
        List<int> observed = turns
            .Where(turn => turn.HasValue)
            .Select(turn => turn!.Value)
            .ToList();
        return observed.Count == 0 ? null : observed.Average();
    }

    /// <summary>
    /// Builds combo-piece and tutor-assisted assembly metrics.
    /// </summary>
    private static ComboAssemblyPerformance BuildComboAssemblyPerformance(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int maxTurn,
        PerformanceCardFactsCache cardFacts)
    {
        ComboAssemblyPerformance result = new()
        {
            RelevantCards = included
                .Where(card =>
                {
                    PerformanceCardFacts facts = cardFacts.Get(card);
                    return facts.HasComboPieceOrEnabler || facts.IsTutor;
                })
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList(),
        };

        List<int> assemblyTurns = runs
            .Where(run => run.ComboAssemblyTurn.HasValue)
            .Select(run => run.ComboAssemblyTurn!.Value)
            .ToList();
        result.AverageEarliestAssemblyTurn = assemblyTurns.Count == 0 ? null : assemblyTurns.Average();

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            result.AssemblyByTurn.Add(PerformanceStatistics.BuildProbability(
                "combo-assembly-by-turn",
                turn,
                runs.Count(run => run.ComboAssemblyTurn <= turn),
                runs.Count));
            result.TutorAssistedAssemblyByTurn.Add(PerformanceStatistics.BuildProbability(
                "tutor-assisted-combo-by-turn",
                turn,
                runs.Count(run => run.TutorAssistedComboTurn <= turn),
                runs.Count));
        }

        return result;
    }

    /// <summary>
    /// Aggregates stranded-card risk rows across all runs.
    /// </summary>
    private static List<StrandedCardPerformance> BuildStrandedCardPerformance(
        IReadOnlyList<PerformanceRun> runs)
    {
        int sampleSize = runs.Count;
        return runs
            .SelectMany(run => run.StrandedCards.Values)
            .GroupBy(card => card.CardName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                int stranded = group.Count();
                return new StrandedCardPerformance
                {
                    CardName = group.Key,
                    ManaValue = group.Max(card => card.ManaValue),
                    StrandedRate = PerformanceStatistics.Rate(stranded, sampleSize),
                    ManaStrandedRate = PerformanceStatistics.Rate(group.Count(card => card.ManaStranded), sampleSize),
                    ColorStrandedRate = PerformanceStatistics.Rate(group.Count(card => card.ColorStranded), sampleSize),
                    SampleSize = sampleSize,
                };
            })
            .Where(card => card.StrandedRate >= 0.03)
            .OrderByDescending(card => card.StrandedRate)
            .ThenByDescending(card => card.ManaValue)
            .Take(10)
            .ToList();
    }

}
