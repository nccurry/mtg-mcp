namespace MtgMcp.Core;

/// <summary>
/// Contains goldfish projection, command-zone, and win-estimate summary builders.
/// </summary>
public sealed partial class DeckSimulationService
{
    /// <summary>
    /// Adds 0-100 summary metrics that distinguish board shape from detected kill confidence.
    /// </summary>
    private static void AddGoldfishSummaryMetrics(
        GoldfishSimulationResult result,
        IReadOnlyList<GoldfishRun> runs,
        int targetTurn)
    {
        ProjectedTurnState target = result.TurnSummaries.FirstOrDefault(summary => summary.Turn == targetTurn)
            ?? result.TurnSummaries.LastOrDefault()
            ?? new ProjectedTurnState();
        result.BoardDevelopmentScore = Math.Clamp(
            (target.MedianLands * 8)
                + (target.MedianManaSources * 4)
                + (target.MedianNonlandPermanents * 8)
                + (Math.Min(target.MedianCardsInHand, 7) * 3)
                + (target.MedianTokens * 3),
            0,
            100);

        List<GoldfishTurnSnapshot> targetSnapshots = runs
            .SelectMany(run => run.Turns.Where(snapshot => snapshot.Turn == targetTurn))
            .ToList();
        int medianThreat = Median(targetSnapshots.Select(snapshot => snapshot.ThreatPressure));
        result.PressureOnlyProgress = Math.Clamp(medianThreat, 0, 100);
        double routePressure = result.WinEstimate.Routes.Count == 0
            ? 0
            : result.WinEstimate.Routes.Max(route => route.Probability) * 100;
        double turnWinRate = result.WinEstimate.WinByTurnRates.TryGetValue(targetTurn, out double rate)
            ? rate * 100
            : 0;
        result.ThreatPressure = Math.Clamp(
            (int)Math.Round(Math.Max(medianThreat, Math.Max(routePressure, turnWinRate))),
            0,
            100);

        result.EngineOnlineRate = targetSnapshots.Count == 0
            ? 0
            : Math.Clamp(
                (int)Math.Round(targetSnapshots.Count(snapshot => snapshot.EngineOnline) * 100.0 / targetSnapshots.Count),
                0,
                100);
        result.EnginePressure = BuildEnginePressureSummary(targetSnapshots);
        result.SorceryFinisherPressure = BuildSorceryFinisherPressureSummary(targetSnapshots);

        double confidence = result.WinEstimate.RouteEvidence.Count == 0
            ? 0
            : result.WinEstimate.RouteEvidence.Max(evidence => evidence.Confidence) * 100;
        result.WinDetectionConfidence = Math.Clamp((int)Math.Round(confidence), 0, 100);
        result.LethalConfidence = Math.Clamp(
            (int)Math.Round(result.WinEstimate.ObservedWinRate * confidence),
            0,
            100);
        result.WinEstimate.PressureOnlyProgress = result.PressureOnlyProgress;
        result.WinEstimate.LethalConfidence = result.LethalConfidence;
        result.Notes.Add(
            "Summary metrics use 0-100 scales: boardDevelopmentScore measures board shape, "
                + "threatPressure measures combat/drain/route pressure, engineOnlineRate measures repeatable engines, "
                + "pressureOnlyProgress is non-lethal pressure, and lethalConfidence combines win rate with route evidence.");
    }

    /// <summary>
    /// Builds a target-turn activated commander engine summary.
    /// </summary>
    private static ActivatedCommanderEnginePressure BuildEnginePressureSummary(IReadOnlyList<GoldfishTurnSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return new ActivatedCommanderEnginePressure();
        }

        ActivatedCommanderEnginePressure strongest = snapshots
            .Select(snapshot => snapshot.EnginePressure)
            .OrderByDescending(pressure => pressure.Pressure)
            .FirstOrDefault()
            ?? new ActivatedCommanderEnginePressure();
        return new ActivatedCommanderEnginePressure
        {
            CommanderOnline = snapshots.Any(snapshot => snapshot.EnginePressure.CommanderOnline),
            ActivationManaAvailable = snapshots.Any(snapshot => snapshot.EnginePressure.ActivationManaAvailable),
            TopdeckSetup = snapshots.Any(snapshot => snapshot.EnginePressure.TopdeckSetup),
            LibraryRevealCheat = snapshots.Any(snapshot => snapshot.EnginePressure.LibraryRevealCheat),
            HighCmcHitDensity = Math.Round(snapshots.Select(snapshot => snapshot.EnginePressure.HighCmcHitDensity).DefaultIfEmpty(0).Average(), 3),
            RepeatableActivation = snapshots.Any(snapshot => snapshot.EnginePressure.RepeatableActivation),
            Pressure = Median(snapshots.Select(snapshot => snapshot.EnginePressure.Pressure)),
            Evidence = strongest.Evidence.Take(6).ToList()
        };
    }

    /// <summary>
    /// Builds a target-turn sorcery finisher pressure summary.
    /// </summary>
    private static SorceryFinisherPressure BuildSorceryFinisherPressureSummary(IReadOnlyList<GoldfishTurnSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return new SorceryFinisherPressure();
        }

        SorceryFinisherPressure strongest = snapshots
            .Select(snapshot => snapshot.SorceryFinisherPressure)
            .OrderByDescending(pressure => pressure.Pressure)
            .FirstOrDefault()
            ?? new SorceryFinisherPressure();
        return new SorceryFinisherPressure
        {
            SorceryFinisherHeld = snapshots.Any(snapshot => snapshot.SorceryFinisherPressure.SorceryFinisherHeld),
            CastableFinisher = snapshots.Any(snapshot => snapshot.SorceryFinisherPressure.CastableFinisher),
            BoardPowerBeforeFinisher = Median(snapshots.Select(snapshot => snapshot.SorceryFinisherPressure.BoardPowerBeforeFinisher)),
            ProjectedDamage = Median(snapshots.Select(snapshot => snapshot.SorceryFinisherPressure.ProjectedDamage)),
            Pressure = strongest.Pressure,
            Evidence = strongest.Evidence.Take(6).ToList()
        };
    }

    /// <summary>
    /// Builds one projected turn summary.
    /// </summary>
    private static ProjectedTurnState BuildProjectedTurnState(int turn, IReadOnlyList<GoldfishRun> runs)
    {
        List<GoldfishTurnSnapshot> snapshots = runs.SelectMany(run => run.Turns.Where(snapshot => snapshot.Turn == turn)).ToList();
        int lands = Median(snapshots.Select(snapshot => snapshot.Lands));
        int manaSources = Median(snapshots.Select(snapshot => snapshot.ManaSources));
        int permanents = Median(snapshots.Select(snapshot => snapshot.NonlandPermanents));
        int hand = Median(snapshots.Select(snapshot => snapshot.CardsInHand));
        int power = Median(snapshots.Select(snapshot => snapshot.Power));
        int tokens = Median(snapshots.Select(snapshot => snapshot.Tokens));
        return new ProjectedTurnState
        {
            Turn = turn,
            ModelLabel = BoardProjectionModelLabel,
            RngKind = GoldfishRngKind,
            MedianLands = lands,
            MedianManaSources = manaSources,
            MedianNonlandPermanents = permanents,
            MedianCardsInHand = hand,
            MedianPower = power,
            MedianTokens = tokens,
            LikelyBoard = $"{lands} lands, {manaSources} mana sources, {permanents} nonland permanents, about {power} pressure, {hand} cards in hand.",
            Confidence = Math.Clamp(0.45 + Math.Min(0.35, runs.Count / 2000.0), 0, 0.85),
            Notes =
            [
                "Model label heuristic-board-projection: derived from optimistic goldfish runs and intended for board-state shape, not strict castability proof.",
                "Opponent interaction and full Magic rules are not simulated.",
            ],
        };
    }

    /// <summary>
    /// Builds command-zone timing metrics from goldfish runs.
    /// </summary>
    private static CommandZonePerformance BuildCommandZonePerformance(
        IReadOnlyList<GoldfishRun> runs,
        int maxTurn,
        CommandZonePlan plan)
    {
        CommandZonePerformance result = new()
        {
            CommandZoneNames = plan.Cards.Select(card => card.Card.Name).ToList(),
            CommanderNames = plan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Commander)
                .Select(card => card.Card.Name)
                .ToList(),
            AverageCommanderCastTurn = AverageTurn(runs.Select(run => run.CommanderCastTurn)),
        };
        if (plan.HasBackgroundPair)
        {
            result.BackgroundNames = plan.Cards
                .Where(card => card.Kind == CommandZoneCardKind.Background)
                .Select(card => card.Card.Name)
                .ToList();
            result.AverageBackgroundCastTurn = AverageTurn(runs.Select(run => run.BackgroundCastTurn));
            result.AverageCommanderWithBackgroundOnlineTurn = AverageTurn(runs.Select(run => run.CommanderWithBackgroundOnlineTurn));
        }

        if (plan.Cards.Count == 0)
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
            if (plan.HasBackgroundPair)
            {
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
        }

        return result;
    }

    /// <summary>
    /// Averages observed turn values while ignoring runs where the event did not occur.
    /// </summary>
    private static double? AverageTurn(IEnumerable<int?> turns)
    {
        List<int> observed = turns
            .Where(turn => turn.HasValue)
            .Select(turn => turn!.Value)
            .ToList();
        return observed.Count == 0 ? null : observed.Average();
    }

    /// <summary>
    /// Builds a win-turn estimate from goldfish runs.
    /// </summary>
    private static WinTurnEstimate BuildWinEstimate(DeckWorkspace workspace, IReadOnlyList<GoldfishRun> runs, int maxTurn)
    {
        List<int> wins = runs.Where(run => run.WinTurn.HasValue).Select(run => run.WinTurn!.Value).Order().ToList();
        WinTurnEstimate estimate = new()
        {
            WorkspaceId = workspace.Id,
            ModelLabel = GoldfishModelLabel,
            RngKind = GoldfishRngKind,
            Simulations = runs.Count,
            ObservedWins = wins.Count,
            ObservedWinRate = runs.Count == 0 ? 0 : wins.Count / (double)runs.Count,
            MedianObservedWinTurn = Percentile(wins, 0.50),
            P25ObservedWinTurn = Percentile(wins, 0.25),
            P75ObservedWinTurn = Percentile(wins, 0.75)
        };
        estimate.Notes.Add("RNG kind mtgmcp-splitmix64-v1: results use the stable deterministic random source shared with Stats Lab.");

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            estimate.WinByTurnRates[turn] = runs.Count == 0 ? 0 : runs.Count(run => run.WinTurn <= turn) / (double)runs.Count;
        }

        foreach (IGrouping<string, GoldfishRun> route in runs.Where(run => run.WinRoute is not null).GroupBy(run => run.WinRoute!))
        {
            List<SimulationRouteEvidence> evidence = route
                .SelectMany(run => run.RouteEvidence)
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(5)
                .ToList();
            estimate.Routes.Add(new WinRoute
            {
                Name = route.Key,
                Kind = route.Key,
                EarliestTurn = route.Min(run => run.WinTurn),
                Probability = route.Count() / (double)runs.Count,
                Cards = RouteCards(workspace, route.Key),
                Rationale = BuildRouteRationale(route.Key, evidence),
                Evidence = evidence,
            });
        }

        estimate.RouteEvidence = runs
            .SelectMany(run => run.RouteEvidence)
            .GroupBy(item => $"{item.Source}:{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToList();
        estimate.PressureOnlyProgress = EstimateFallbackPressureProgress(estimate.Routes);
        estimate.LethalConfidence = EstimateLethalConfidence(estimate);

        if (estimate.MedianObservedWinTurn is null)
        {
            estimate.Notes.Add($"No likely win was found by turn {maxTurn} in the goldfish runs.");
        }

        if (BuildPartialCommanderDeckWarning(workspace) is string partialDeckWarning)
        {
            estimate.Notes.Add(partialDeckWarning);
        }

        estimate.Notes.Add("Win timing is probabilistic and assumes no interaction.");
        estimate.Notes.Add(
            "Model label optimistic-goldfish-model: route evidence combines deterministic route predicates "
                + "with fallback board-pressure heuristics.");
        estimate.Notes.Add(
            "deck_analyze_performance can report different timing because it uses strict-sequencing-model "
                + "scenario probabilities instead of heuristic win-pressure detection.");
        estimate.Notes.Add(
            "Observed win-turn percentiles only include runs that reached a heuristic win; winByTurnRates "
                + "and observedWinRate are measured against all runs.");
        estimate.Notes.Add("Pressure-only progress is reported separately from lethal confidence.");
        return estimate;
    }

    /// <summary>
    /// Estimates how much of the win estimate came from fallback pressure instead of deterministic routes.
    /// </summary>
    private static int EstimateFallbackPressureProgress(IReadOnlyList<WinRoute> routes)
    {
        double progress = 0;
        foreach (WinRoute route in routes)
        {
            bool fallbackOnly = route.Evidence.Count > 0
                && route.Evidence.All(evidence => evidence.Source.Equals("fallback", StringComparison.OrdinalIgnoreCase));
            if (fallbackOnly)
            {
                progress = Math.Max(progress, route.Probability * 100);
            }
        }

        return Math.Clamp((int)Math.Round(progress), 0, 100);
    }

    /// <summary>
    /// Combines observed win rate with the strongest route evidence confidence.
    /// </summary>
    private static int EstimateLethalConfidence(WinTurnEstimate estimate)
    {
        double confidence = estimate.RouteEvidence.Count == 0
            ? 0
            : estimate.RouteEvidence.Max(evidence => evidence.Confidence) * 100;
        return Math.Clamp((int)Math.Round(estimate.ObservedWinRate * confidence), 0, 100);
    }

    /// <summary>
    /// Labels deterministic route evidence separately from fallback heuristic pressure.
    /// </summary>
    private static string BuildRouteRationale(string route, IReadOnlyList<SimulationRouteEvidence> evidence)
    {
        bool deterministicEvidence = evidence.Any(item => !item.Source.Equals("fallback", StringComparison.OrdinalIgnoreCase));
        if (deterministicEvidence)
        {
            return $"The simulator found {route} through deterministic route evidence.";
        }

        if (evidence.Count > 0)
        {
            return $"The simulator found {route} through fallback heuristic pressure.";
        }

        return $"The simulator found {route} through fallback pressure heuristics.";
    }

    /// <summary>
    /// Gets representative cards for a win route.
    /// </summary>
    private static List<string> RouteCards(DeckWorkspace workspace, string route)
    {
        return DeckServiceHelpers.IncludedCards(workspace)
            .Where(card =>
            {
                CardRoleAssignment role = DeckRoleClassifier.Classify(card);
                return route switch
                {
                    "combo" => role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler),
                    "finisher" => role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers),
                    "combat" => IsCombatRouteCard(card),
                    _ => false
                };
            })
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    /// <summary>
    /// Checks whether a value contains any of the supplied phrases.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        return DeckAnalysisMetrics.ContainsAny(value, needles);
    }

    /// <summary>
    /// Calculates an integer median.
    /// </summary>
    private static int Median(IEnumerable<int> values)
    {
        List<int> sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    /// <summary>
    /// Calculates a percentile turn.
    /// </summary>
    private static int? Percentile(IReadOnlyList<int> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        int index = Math.Clamp((int)Math.Round((sortedValues.Count - 1) * percentile), 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}
