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
        SimulationProfile profile)
    {
        Random random = new(seed);
        PerformanceOpeningHand opening = DrawPerformanceOpeningHand(
            libraryTemplate,
            random,
            includeMulligans,
            mulliganContext,
            cardFacts);
        List<DeckCard> hand = opening.Hand;
        List<DeckCard> library = opening.Library;
        List<PerformancePermanent> battlefield = [];
        List<DeckCard> graveyard = [];
        List<IReadOnlyList<string>> virtualManaSources = [];
        PerformanceRun run = new()
        {
            Mulligans = opening.Mulligans,
            KeptHandSize = opening.Hand.Count,
            KeptOpeningLands = CountPerformanceRole(opening.Hand, DeckRoles.Lands, cardFacts),
            OpeningSevenLands = opening.OpeningSevenLands,
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

            DeckCard? landPlayed = ChoosePerformanceLand(hand, battlefield, virtualManaSources, deckColors, cardFacts);
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
            }

            List<PerformanceManaSource> availableSources = GetPerformanceManaSources(
                battlefield,
                virtualManaSources,
                unavailablePermanent,
                cardFacts);
            List<PerformanceManaSource> turnStartSources = availableSources.ToList();
            int totalManaSources = GetPerformanceManaSources(
                    battlefield,
                    virtualManaSources,
                    unavailablePermanent: null,
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
                    ref availableSources,
                    ref rampCastByTurn,
                    ref drawCastByTurn);
                CastPerformanceCommandZoneCards(
                    commandZone,
                    turn,
                    battlefield,
                    cardFacts,
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

}
