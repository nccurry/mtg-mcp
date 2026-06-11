namespace MtgMcp.Core;

/// <summary>
/// Contains mulligan, sequencing, and turn simulation helpers.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Builds the deck-level context used by opening-hand mulligan decisions.
    /// </summary>
    private static PerformanceMulliganContext BuildPerformanceMulliganContext(
        DeckWorkspace workspace,
        CommandZonePlan commandZonePlan,
        IReadOnlySet<string> deckColors,
        SimulationProfile profile)
    {
        return new PerformanceMulliganContext
        {
            FreeFirstMulligan = MulliganHeuristics.UsesFreeFirstMulligan(workspace.Format),
            DeckColors = new HashSet<string>(deckColors, StringComparer.OrdinalIgnoreCase),
            Commander = commandZonePlan.PrimaryCommander,
            CommanderTargetTurn = Math.Max(1, profile.Sequencing.PreferredCommanderTurn ?? profile.Scenarios.CommanderTurn),
            Mulligan = profile.Mulligan,
        };
    }

    /// <summary>
    /// Simulates one heuristic game from opening hand through the target turn.
    /// </summary>
    private static PerformanceRun RunPerformanceGame(
        IReadOnlyList<DeckCard> libraryTemplate,
        CommandZonePlan commandZonePlan,
        PerformanceMulliganContext mulliganContext,
        PerformanceCardFactsCache cardFacts,
        IReadOnlySet<string> deckColors,
        int maxTurn,
        int seed,
        bool includeMulligans,
        SimulationProfile profile,
        bool collectDecisionEvents)
    {
        DeterministicSimulationRandom random = new(seed);
        PerformanceOpeningHand opening = DrawPerformanceOpeningHand(
            libraryTemplate,
            random,
            includeMulligans,
            mulliganContext,
            cardFacts,
            collectDecisionEvents);
        List<DeckCard> hand = opening.Hand;
        List<DeckCard> library = opening.Library;
        List<PerformancePermanent> battlefield = [];
        List<DeckCard> graveyard = [];
        List<PerformanceScheduledManaSource> virtualManaSources = [];
        List<PerformanceDecisionEvent>? decisionEvents = collectDecisionEvents ? opening.DecisionEvents : null;
        PerformanceRun run = new()
        {
            Seed = seed,
            Mulligans = opening.Mulligans,
            KeptHandSize = opening.Hand.Count,
            KeptOpeningLands = CountPerformanceRole(opening.Hand, DeckRoles.Lands, cardFacts),
            OpeningSevenLands = opening.OpeningSevenLands,
            DecisionEvents = decisionEvents ?? [],
        };
        bool rampCastByTurn = false;
        bool drawCastByTurn = false;
        CommandZoneRunState commandZone = new(commandZonePlan);

        for (int turn = 1; turn <= maxTurn; turn++)
        {
            if (library.Count > 0)
            {
                PerformanceDrawOne(hand, library);
            }

            DeckCard? landPlayed = ChoosePerformanceLand(hand, battlefield, virtualManaSources, deckColors, turn, cardFacts);
            PerformancePermanent? unavailablePermanent = null;
            if (landPlayed is not null)
            {
                hand.Remove(landPlayed);
                PerformancePermanent permanent = new() { Card = landPlayed };
                battlefield.Add(permanent);
                if (cardFacts.Get(landPlayed).LooksTapped)
                {
                    unavailablePermanent = permanent;
                }

                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "sequencing",
                    turn,
                    "land-drop",
                    "played",
                    landPlayed.Name,
                    "selected the land drop that best improved current color access and tempo.",
                    $"lands in hand before play: {hand.Count(card => cardFacts.Get(card).IsLand) + 1}",
                    $"enters tapped: {cardFacts.Get(landPlayed).LooksTapped}");
            }
            else
            {
                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "sequencing",
                    turn,
                    "land-drop",
                    "skipped",
                    "land drop",
                    "no land in hand was available to play.");
            }

            List<PerformanceManaSource> availableSources = GetPerformanceManaSources(
                battlefield,
                virtualManaSources,
                unavailablePermanent,
                turn,
                cardFacts);
            List<PerformanceManaSource> turnStartSources = availableSources.ToList();
            int totalManaSources = GetPerformanceManaSources(
                    battlefield,
                    virtualManaSources,
                    unavailablePermanent: null,
                    turn,
                    cardFacts)
                .Count;
            PerformanceTurnState state = new()
            {
                Turn = turn,
                LandsInPlay = CountPerformanceRole(
                    battlefield.Select(permanent => permanent.Card),
                    DeckRoles.Lands,
                    cardFacts),
                ManaSources = totalManaSources,
                AvailableMana = availableSources.Count,
                ColorSources = new HashSet<string>(
                    ExtractColoredSymbols(availableSources),
                    StringComparer.OrdinalIgnoreCase),
                UntappedManaSources = turnStartSources,
                LandDropMade = landPlayed is not null,
                OnCurveUntappedMana = availableSources.Count >= turn,
            };

            if (profile.Sequencing.PreferCommanderOnCurve)
            {
                CastPerformanceCommandZoneCards(
                    commandZone,
                    turn,
                    battlefield,
                    cardFacts,
                    decisionEvents,
                    ref availableSources);
            }

            if (profile.Sequencing.PreferCommanderOnCurve)
            {
                CastPerformanceHandSpells(
                    hand,
                    library,
                    battlefield,
                    graveyard,
                    virtualManaSources,
                    state,
                    cardFacts,
                    deckColors,
                    turn,
                    profile,
                    PerformanceSpellWindow.All,
                    commandZone.CommanderOnline,
                    decisionEvents,
                    ref availableSources,
                    ref rampCastByTurn,
                    ref drawCastByTurn);
            }
            else
            {
                CastPerformanceHandSpells(
                    hand,
                    library,
                    battlefield,
                    graveyard,
                    virtualManaSources,
                    state,
                    cardFacts,
                    deckColors,
                    turn,
                    profile,
                    PerformanceSpellWindow.SetupOnly,
                    commandZone.CommanderOnline,
                    decisionEvents,
                    ref availableSources,
                    ref rampCastByTurn,
                    ref drawCastByTurn);
                CastPerformanceCommandZoneCards(
                    commandZone,
                    turn,
                    battlefield,
                    cardFacts,
                    decisionEvents,
                    ref availableSources);
                CastPerformanceHandSpells(
                    hand,
                    library,
                    battlefield,
                    graveyard,
                    virtualManaSources,
                    state,
                    cardFacts,
                    deckColors,
                    turn,
                    profile,
                    PerformanceSpellWindow.NonSetup,
                    commandZone.CommanderOnline,
                    decisionEvents,
                    ref availableSources,
                    ref rampCastByTurn,
                    ref drawCastByTurn);
            }

            List<DeckCard> battlefieldCards = battlefield.Select(permanent => permanent.Card).ToList();
            List<DeckCard> seenCards = hand.Concat(battlefieldCards).Concat(graveyard).ToList();
            state.AvailableMana = availableSources.Count;
            state.RampSeenByTurn = PerformanceHasRole(seenCards, DeckRoles.Ramp, cardFacts);
            state.RampCastByTurn = state.RampCastByTurn || rampCastByTurn;
            state.DrawSeenByTurn = PerformanceHasRole(seenCards, DeckRoles.Draw, cardFacts);
            state.DrawCastByTurn = state.DrawCastByTurn || drawCastByTurn;
            state.InteractionSeenByTurn = PerformanceHasAnyRole(seenCards, cardFacts, DeckRoles.Interaction, DeckRoles.BoardWipes);
            state.ProtectionSeenByTurn = PerformanceHasRole(seenCards, DeckRoles.Protection, cardFacts);
            state.GraveyardHateSeenByTurn = PerformanceHasTag(seenCards, DeckTags.GraveyardHate, cardFacts);
            state.InteractionHeldUp = HasHeldPerformanceRole(
                hand,
                availableSources,
                DeckRoles.Interaction,
                cardFacts);
            state.ProtectionHeldUp = HasHeldPerformanceRole(
                hand,
                availableSources,
                DeckRoles.Protection,
                cardFacts);
            if (state.InteractionHeldUp)
            {
                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "interaction-hold-up",
                    turn,
                    "hold-up",
                    "held",
                    "interaction",
                    "available mana could pay at least one interaction spell remaining in hand.");
            }

            state.CastableHandRate = CalculatePerformanceCastableHandRate(
                hand,
                turnStartSources,
                cardFacts);
            state.CardsInHand = hand.Count;
            state.AllDeckColorsAvailable = deckColors.Count == 0
                || deckColors.All(color => state.ColorSources.Contains(color));
            state.CommanderCastByTurn = commandZone.CommanderOnline;
            state.BackgroundCastByTurn = commandZone.BackgroundOnline;
            state.CommanderWithBackgroundOnlineByTurn = commandZone.CommanderWithBackgroundOnlineTurn.HasValue;
            state.CommanderProtectedByTurn = commandZone.CommanderOnline
                && (state.ProtectionHeldUp
                    || PerformanceHasRole(battlefieldCards, DeckRoles.Protection, cardFacts)
                    || PerformanceHasTag(battlefieldCards, DeckTags.CombatProtection, cardFacts));

            if (state.CommanderProtectedByTurn && !run.CommanderProtectedTurn.HasValue)
            {
                run.CommanderProtectedTurn = turn;
            }

            int comboPiecesSeen = CountPerformanceComboCards(seenCards, includeTutors: false, cardFacts);
            bool tutorSeen = PerformanceHasRole(seenCards, DeckRoles.Tutors, cardFacts);
            state.ComboPiecesSeen = comboPiecesSeen;
            state.ComboAssemblyByTurn = comboPiecesSeen >= 2;
            state.TutorAssistedComboByTurn = comboPiecesSeen >= 1 && tutorSeen;
            AddPerformanceDecisionEvent(
                decisionEvents,
                "route-check",
                turn,
                "combo-assembly",
                state.ComboAssemblyByTurn || state.TutorAssistedComboByTurn ? "matched" : "missing",
                "combo route",
                "checked whether seen cards satisfied two-piece or tutor-assisted combo pressure.",
                $"combo pieces seen: {comboPiecesSeen}",
                $"tutor seen: {tutorSeen}");
            if (state.ComboAssemblyByTurn && !run.ComboAssemblyTurn.HasValue)
            {
                run.ComboAssemblyTurn = turn;
            }

            if ((state.ComboAssemblyByTurn || state.TutorAssistedComboByTurn)
                && !run.TutorAssistedComboTurn.HasValue)
            {
                run.TutorAssistedComboTurn = turn;
            }

            run.Turns.Add(state);
        }

        run.CommanderCastTurn = commandZone.CommanderCastTurn;
        run.BackgroundCastTurn = commandZone.BackgroundCastTurn;
        run.CommanderWithBackgroundOnlineTurn = commandZone.CommanderWithBackgroundOnlineTurn;
        AddPerformanceStrandedCards(run, hand, run.Turns.LastOrDefault(), maxTurn, cardFacts);
        return run;
    }

    /// <summary>
    /// Adds a bounded decision event to sampled trace runs.
    /// </summary>
    private static void AddPerformanceDecisionEvent(
        List<PerformanceDecisionEvent>? events,
        string phase,
        int? turn,
        string decision,
        string outcome,
        string subject,
        string rationale,
        params string[] evidence)
    {
        if (events is null)
        {
            return;
        }

        if (events.Count >= PerformanceDecisionEventLimit)
        {
            return;
        }

        PerformanceDecisionEvent decisionEvent = new()
        {
            Phase = phase,
            Turn = turn,
            Decision = decision,
            Outcome = outcome,
            Subject = subject,
            Rationale = rationale,
        };
        foreach (string line in evidence)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                decisionEvent.Evidence.Add(line);
            }
        }

        events.Add(decisionEvent);
    }

}
