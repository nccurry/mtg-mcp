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
        IReadOnlyList<DeckCard> included,
        PerformanceCardFactsCache cardFacts,
        IReadOnlySet<string> deckColors,
        SimulationProfile profile)
    {
        return new PerformanceMulliganContext
        {
            FreeFirstMulligan = MulliganHeuristics.UsesFreeFirstMulligan(workspace.Format),
            DeckColors = new HashSet<string>(deckColors, StringComparer.OrdinalIgnoreCase),
            Commander = included.FirstOrDefault(card => cardFacts.Get(card).IsCommander),
            Mulligan = profile.Mulligan,
        };
    }

    /// <summary>
    /// Simulates one heuristic game from opening hand through the target turn.
    /// </summary>
    private static PerformanceRun RunPerformanceGame(
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<DeckCard> libraryTemplate,
        DeckCard? commander,
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
        bool commanderCast = false;

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

            if (!commanderCast
                && commander is not null
                && TryPay(cardFacts.Get(commander), availableSources, out List<PerformanceManaSource> afterCommanderSources))
            {
                commanderCast = true;
                availableSources = afterCommanderSources;
                battlefield.Add(new PerformancePermanent { Card = commander });
                run.CommanderCastTurn = turn;
            }

            foreach (DeckCard spell in hand
                .Where(card => !cardFacts.Get(card).IsCommander)
                .Where(card => !cardFacts.Get(card).IsLand)
                .OrderBy(card => PerformanceCastPriority(cardFacts.Get(card), turn, profile))
                .ThenBy(card => cardFacts.Get(card).ManaValue)
                .ToList())
            {
                PerformanceCardFacts facts = cardFacts.Get(spell);
                if (ShouldHoldPerformanceSpell(facts, turn, commanderCast, profile))
                {
                    continue;
                }

                if (!TryPay(facts, availableSources, out List<PerformanceManaSource> afterSpellSources))
                {
                    continue;
                }

                availableSources = afterSpellSources;
                hand.Remove(spell);
                if (facts.IsPermanent)
                {
                    battlefield.Add(new PerformancePermanent { Card = spell });
                }
                else
                {
                    graveyard.Add(spell);
                }

                if (facts.IsRamp)
                {
                    rampCastByTurn = true;
                    state.RampCastByTurn = true;
                    if (!facts.IsPermanent)
                    {
                        virtualManaSources.Add(BuildPerformanceRampSource(facts, deckColors));
                    }
                }

                if (facts.IsDraw)
                {
                    drawCastByTurn = true;
                    state.DrawCastByTurn = true;
                    if (library.Count > 0)
                    {
                        PerformanceDrawOne(hand, library);
                    }
                }
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
            state.CommanderCastByTurn = commanderCast;
            state.CommanderProtectedByTurn = commanderCast
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

        AddPerformanceStrandedCards(run, hand, run.Turns.LastOrDefault(), maxTurn, cardFacts);
        return run;
    }

}
