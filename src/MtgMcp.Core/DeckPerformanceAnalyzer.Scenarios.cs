namespace MtgMcp.Core;

/// <summary>
/// Contains named scenario and failure-driver calculations.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Builds named deckbuilder scenarios from the simulated run set.
    /// </summary>
    private static List<ScenarioPerformance> BuildScenarioPerformance(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlySet<string> deckColors,
        bool colorIdentityKnown,
        int maxTurn,
        SimulationProfile profile,
        DeckIntent? intent,
        PerformanceCardFactsCache cardFacts)
    {
        PerformanceScenarioDefaults defaults = BuildScenarioDefaults(maxTurn, profile, intent);
        List<string> commanderDrivers =
            ["Missing early land drops", "Missing required commander colors", "Insufficient early ramp"];
        List<string> commanderProtectionDrivers =
            ["Protection density is low", "Protection is present but not castable after commander development"];
        List<string> graveyardHateDrivers =
            ["Graveyard hate density is low", "Graveyard hate appears after the target turn"];
        List<string> colorDrivers =
            ["Missing color sources", "Tapped lands delayed early color access"];
        List<string> interactionDrivers =
            ["Interaction density is low", "Early development spends mana before interaction can be held up"];
        List<string> comboDrivers =
            ["Combo density is low", "Tutors or pieces are not seen by the target turn"];
        List<string> strandedDrivers =
            ["High mana-value cards outpace available mana", "Colored costs are missing matching sources"];
        List<ScenarioPerformance> scenarios =
        [
            BuildScenario(
                "commander-by-turn-4",
                defaults.CommanderTurn,
                runs.Count(run => run.CommanderCastTurn <= defaults.CommanderTurn),
                runs.Count,
                RelevantPerformanceCards(included, DeckRoles.Commander, cardFacts),
                commanderDrivers,
                defaults.IntentAdjusted
                    ? ["Commander is treated as always available from the command zone.", "Deck intent adjusted the target turn."]
                    : ["Commander is treated as always available from the command zone."],
                BuildCommanderFailureDriverCounts(included, runs, defaults.CommanderTurn, commanderDrivers, cardFacts)),
            BuildScenario(
                "commander-with-protection-by-turn-5",
                defaults.ProtectionTurn,
                runs.Count(run => run.CommanderProtectedTurn <= defaults.ProtectionTurn),
                runs.Count,
                RelevantPerformanceCards(included, DeckRoles.Protection, cardFacts),
                commanderProtectionDrivers,
                ["Protection includes held-up protection spells and protection permanents."],
                BuildProtectionFailureDriverCounts(runs, defaults.ProtectionTurn, commanderProtectionDrivers)),
            BuildScenario(
                "graveyard-hate-by-turn-3",
                defaults.HateTurn,
                runs.Count(run => StateAt(run, defaults.HateTurn)?.GraveyardHateSeenByTurn == true),
                runs.Count,
                RelevantPerformanceTaggedCards(included, DeckTags.GraveyardHate, cardFacts),
                graveyardHateDrivers,
                ["Scenario measures access, not whether the hate is tactically correct to deploy."],
                BuildGraveyardHateFailureDriverCounts(included, runs, defaults.HateTurn, graveyardHateDrivers, cardFacts)),
            BuildScenario(
                "all-colors-by-turn-3",
                defaults.ColorTurn,
                colorIdentityKnown
                    ? runs.Count(run => StateAt(run, defaults.ColorTurn)?.AllDeckColorsAvailable == true)
                    : 0,
                runs.Count,
                ManaSourcePerformanceCards(included, deckColors, cardFacts),
                colorDrivers,
                ["Uses cached produced_mana plus basic-land name fallbacks."],
                BuildColorFailureDriverCounts(runs, defaults.ColorTurn, colorDrivers)),
            BuildScenario(
                "hold-up-interaction-by-turn-4",
                defaults.InteractionTurn,
                runs.Count(run => StateAt(run, defaults.InteractionTurn)?.InteractionHeldUp == true),
                runs.Count,
                RelevantPerformanceCards(included, DeckRoles.Interaction, cardFacts),
                interactionDrivers,
                ["Held-up interaction means a classified interaction spell remains in hand and is castable."],
                BuildInteractionFailureDriverCounts(runs, defaults.InteractionTurn, interactionDrivers)),
            BuildScenario(
                "combo-or-tutor-assembly-by-turn-5",
                defaults.ComboTurn,
                runs.Count(run => run.TutorAssistedComboTurn <= defaults.ComboTurn),
                runs.Count,
                RelevantComboPerformanceCards(included, cardFacts),
                comboDrivers,
                ["Assembly means two combo cards, or a combo card plus a tutor, have been seen."],
                BuildComboFailureDriverCounts(runs, defaults.ComboTurn, comboDrivers)),
            BuildScenario(
                "stranded-high-mana-risk-by-max-turn",
                maxTurn,
                runs.Count(run => run.StrandedCards.Count > 0),
                runs.Count,
                runs
                    .SelectMany(run => run.StrandedCards.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .ToList(),
                strandedDrivers,
                ["This is a risk rate; lower is better."],
                BuildStrandedFailureDriverCounts(runs, strandedDrivers)),
        ];

        if (!colorIdentityKnown)
        {
            ScenarioPerformance colorScenario = scenarios
                .First(scenario => scenario.Name.Equals("all-colors-by-turn-3", StringComparison.OrdinalIgnoreCase));
            colorScenario.FailureDrivers.Add("Deck color identity could not be inferred.");
            colorScenario.FailureDriverCounts["Deck color identity could not be inferred."] = runs.Count;
        }

        return scenarios;
    }

    /// <summary>
    /// Builds target turns for named scenarios from the profile and deck intent.
    /// </summary>
    private static PerformanceScenarioDefaults BuildScenarioDefaults(
        int maxTurn,
        SimulationProfile profile,
        DeckIntent? intent)
    {
        bool intentAdjusted = intent is not null
            && (!string.IsNullOrWhiteSpace(intent.SimulationProfile)
                || !string.IsNullOrWhiteSpace(intent.PowerTarget)
                || !string.IsNullOrWhiteSpace(intent.PowerLevel)
                || intent.TargetGoldfishTurn.HasValue);
        return new PerformanceScenarioDefaults
        {
            CommanderTurn = ClampScenarioTurn(profile.Scenarios.CommanderTurn, maxTurn),
            ProtectionTurn = ClampScenarioTurn(profile.Scenarios.ProtectionTurn, maxTurn),
            HateTurn = ClampScenarioTurn(profile.Scenarios.HateTurn, maxTurn),
            ColorTurn = ClampScenarioTurn(profile.Scenarios.ColorTurn, maxTurn),
            InteractionTurn = ClampScenarioTurn(profile.Scenarios.InteractionTurn, maxTurn),
            ComboTurn = ClampScenarioTurn(profile.Scenarios.ComboTurn, maxTurn),
            IntentAdjusted = intentAdjusted,
        };
    }

    /// <summary>
    /// Clamps a scenario target turn to the simulated horizon.
    /// </summary>
    private static int ClampScenarioTurn(int turn, int maxTurn)
    {
        return Math.Clamp(turn, 1, maxTurn);
    }

    /// <summary>
    /// Creates one scenario result with interval data.
    /// </summary>
    private static ScenarioPerformance BuildScenario(
        string name,
        int targetTurn,
        int successes,
        int sampleSize,
        List<string> relevantCards,
        List<string> failureDrivers,
        List<string> assumptions,
        Dictionary<string, int>? failureDriverCounts = null)
    {
        (double low, double high) = PerformanceStatistics.ConfidenceInterval(successes, sampleSize);
        return new ScenarioPerformance
        {
            Name = name,
            TargetTurn = targetTurn,
            SuccessRate = PerformanceStatistics.Rate(successes, sampleSize),
            LowConfidenceInterval = low,
            HighConfidenceInterval = high,
            SampleSize = sampleSize,
            RelevantCards = relevantCards,
            FailureDrivers = failureDrivers,
            FailureDriverCounts = failureDriverCounts
                ?? BuildFailureDriverCounts(name, successes, sampleSize, failureDrivers),
            Assumptions = assumptions,
        };
    }

    /// <summary>
    /// Counts likely commander deployment failure causes from run states.
    /// </summary>
    private static Dictionary<string, int> BuildCommanderFailureDriverCounts(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers,
        PerformanceCardFactsCache cardFacts)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        DeckCard? commander = included.FirstOrDefault(card => cardFacts.Get(card).IsCommander);
        PerformanceCardFacts? commanderFacts = commander is null ? null : cardFacts.Get(commander);
        int commanderCost = commanderFacts?.ManaValue ?? 0;
        PerformanceCostRequirement? commanderRequirement = commander is null
            ? null
            : commanderFacts!.CostRequirement;
        foreach (PerformanceRun run in runs.Where(run => run.CommanderCastTurn is null || run.CommanderCastTurn > targetTurn))
        {
            PerformanceTurnState? state = StateAt(run, targetTurn);
            if (state is null)
            {
                continue;
            }

            if (state.LandsInPlay < Math.Min(targetTurn, Math.Max(1, commanderCost)))
            {
                Increment(counts, drivers[0]);
            }

            if (commanderRequirement is not null
                && !PerformanceMana.CanSatisfyRequirement(commanderRequirement, state.UntappedManaSources))
            {
                Increment(counts, drivers[1]);
            }

            if (!state.RampCastByTurn && state.ManaSources < commanderCost)
            {
                Increment(counts, drivers[2]);
            }
        }

        return counts;
    }

    /// <summary>
    /// Counts likely commander protection scenario failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildProtectionFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceRun run in runs.Where(run => run.CommanderProtectedTurn is null || run.CommanderProtectedTurn > targetTurn))
        {
            PerformanceTurnState? state = StateAt(run, targetTurn);
            if (state is null || !state.ProtectionSeenByTurn)
            {
                Increment(counts, drivers[0]);
                continue;
            }

            Increment(counts, drivers[1]);
        }

        return counts;
    }

    /// <summary>
    /// Counts likely graveyard hate access failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildGraveyardHateFailureDriverCounts(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers,
        PerformanceCardFactsCache cardFacts)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        bool hasGraveyardHate = RelevantPerformanceTaggedCards(included, DeckTags.GraveyardHate, cardFacts).Count > 0;
        foreach (PerformanceRun run in runs.Where(run => StateAt(run, targetTurn)?.GraveyardHateSeenByTurn != true))
        {
            Increment(counts, hasGraveyardHate ? drivers[1] : drivers[0]);
        }

        return counts;
    }

    /// <summary>
    /// Counts likely color access failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildColorFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceTurnState state in StatesForTurn(runs, targetTurn).Where(state => !state.AllDeckColorsAvailable))
        {
            Increment(counts, drivers[0]);
            if (!state.OnCurveUntappedMana)
            {
                Increment(counts, drivers[1]);
            }
        }

        return counts;
    }

    /// <summary>
    /// Counts likely interaction hold-up failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildInteractionFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceTurnState state in StatesForTurn(runs, targetTurn).Where(state => !state.InteractionHeldUp))
        {
            Increment(counts, state.InteractionSeenByTurn ? drivers[1] : drivers[0]);
        }

        return counts;
    }

    /// <summary>
    /// Counts likely combo assembly failure causes.
    /// </summary>
    private static Dictionary<string, int> BuildComboFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        int targetTurn,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceTurnState state in StatesForTurn(runs, targetTurn).Where(state => !state.TutorAssistedComboByTurn))
        {
            Increment(counts, state.ComboPiecesSeen == 0 ? drivers[0] : drivers[1]);
        }

        return counts;
    }

    /// <summary>
    /// Counts why high-mana cards were stranded in risky runs.
    /// </summary>
    private static Dictionary<string, int> BuildStrandedFailureDriverCounts(
        IReadOnlyList<PerformanceRun> runs,
        IReadOnlyList<string> drivers)
    {
        Dictionary<string, int> counts = EmptyDriverCounts(drivers);
        foreach (PerformanceStrandedRun stranded in runs.SelectMany(run => run.StrandedCards.Values))
        {
            if (stranded.ManaStranded)
            {
                Increment(counts, drivers[0]);
            }

            if (stranded.ColorStranded)
            {
                Increment(counts, drivers[1]);
            }
        }

        return counts;
    }

    /// <summary>
    /// Creates a zero-filled failure-driver counter.
    /// </summary>
    private static Dictionary<string, int> EmptyDriverCounts(IEnumerable<string> drivers)
    {
        return drivers.ToDictionary(driver => driver, _ => 0, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Increments a named counter.
    /// </summary>
    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    /// <summary>
    /// Converts headline failure drivers into observed count buckets for the scenario result.
    /// </summary>
    private static Dictionary<string, int> BuildFailureDriverCounts(
        string scenarioName,
        int successes,
        int sampleSize,
        IEnumerable<string> failureDrivers)
    {
        int observedFailures = scenarioName.StartsWith("stranded-", StringComparison.OrdinalIgnoreCase)
            ? successes
            : Math.Max(0, sampleSize - successes);
        return failureDrivers.ToDictionary(
            driver => driver,
            _ => observedFailures,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lists representative cards for a role-based scenario.
    /// </summary>
    private static List<string> RelevantPerformanceCards(
        IEnumerable<DeckCard> cards,
        string roleName,
        PerformanceCardFactsCache cardFacts)
    {
        return cards
            .Where(card => cardFacts.Get(card).HasRole(roleName))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Lists representative cards for a tag-based scenario.
    /// </summary>
    private static List<string> RelevantPerformanceTaggedCards(
        IEnumerable<DeckCard> cards,
        string tag,
        PerformanceCardFactsCache cardFacts)
    {
        return cards
            .Where(card => cardFacts.Get(card).HasTag(tag))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Lists cards that represent combo pieces, combo enablers, or tutors.
    /// </summary>
    private static List<string> RelevantComboPerformanceCards(
        IEnumerable<DeckCard> cards,
        PerformanceCardFactsCache cardFacts)
    {
        return cards
            .Where(card =>
            {
                PerformanceCardFacts facts = cardFacts.Get(card);
                return facts.HasComboPieceOrEnabler || facts.IsTutor;
            })
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// Lists mana sources relevant to the deck's inferred color identity.
    /// </summary>
    private static List<string> ManaSourcePerformanceCards(
        IEnumerable<DeckCard> cards,
        IReadOnlySet<string> deckColors,
        PerformanceCardFactsCache cardFacts)
    {
        return cards
            .Where(card => cardFacts.Get(card).IsManaSource)
            .Where(card => deckColors.Count == 0 || cardFacts.Get(card).ProducedMana.Any(deckColors.Contains))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

}
