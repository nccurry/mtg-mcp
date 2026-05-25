namespace MtgMcp.Core;

/// <summary>
/// Analyzes deck performance without repository, MCP, or backend concerns.
/// </summary>
internal static class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Builds the complete performance report from repeated deterministic runs.
    /// </summary>
    public static DeckPerformanceAnalysis Analyze(
        DeckWorkspace workspace,
        string profile,
        int simulations,
        int maxTurn,
        int seed,
        bool includeMulligans,
        CancellationToken cancellationToken,
        SimulationProfileCatalog? simulationProfiles = null)
    {
        int safeSimulations = Math.Clamp(simulations, 100, 100_000);
        int safeMaxTurn = Math.Clamp(maxTurn, 1, 20);
        DeckIntentResult intentResult = DeckIntentText.Extract(workspace.Description, workspace.Id);
        DeckIntent? intent = intentResult.Intent;
        ResolvedSimulationProfile profileResolution = (simulationProfiles ?? SimulationProfileCatalog.CreateDefault())
            .Resolve(workspace, profile, intent);
        SimulationProfile resolvedProfile = profileResolution.Profile;
        List<DeckCard> included = IncludedCards(workspace).ToList();
        PerformanceCardFactsCache cardFacts = new(included);
        int deckSize = included.Sum(card => Math.Max(0, card.Quantity));
        (bool colorIdentityKnown, HashSet<string> deckColors) = GetDeckColorIdentity(included, cardFacts);
        List<DeckCard> libraryTemplate = ExpandPerformanceLibrary(included, cardFacts);
        DeckCard? commander = included.FirstOrDefault(card => cardFacts.Get(card).IsCommander);
        PerformanceMulliganContext mulliganContext = BuildPerformanceMulliganContext(
            workspace,
            included,
            cardFacts,
            deckColors,
            resolvedProfile);
        List<PerformanceRun> runs = [];

        for (int index = 0; index < safeSimulations; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runs.Add(RunPerformanceGame(
                included,
                libraryTemplate,
                commander,
                mulliganContext,
                cardFacts,
                deckColors,
                safeMaxTurn,
                seed + index,
                includeMulligans,
                resolvedProfile));
        }

        DeckPerformanceAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            Profile = resolvedProfile.Id,
            ProfileResolution = profileResolution,
            Simulations = safeSimulations,
            MaxTurn = safeMaxTurn,
            Seed = seed,
            IncludeMulligans = includeMulligans,
            DeckSize = deckSize,
            OpeningHands = BuildOpeningHandPerformance(runs),
            Castability = BuildCastabilityPerformance(runs, deckColors, colorIdentityKnown, safeMaxTurn),
            Commander = BuildCommanderPerformance(included, runs, safeMaxTurn, cardFacts),
            ComboAssembly = BuildComboAssemblyPerformance(included, runs, safeMaxTurn, cardFacts),
            StrandedCards = BuildStrandedCardPerformance(runs),
        };

        AddTurnPerformanceMetrics(analysis, runs, deckColors, colorIdentityKnown, safeMaxTurn);
        analysis.Scenarios = BuildScenarioPerformance(
            included,
            runs,
            deckColors,
            colorIdentityKnown,
            safeMaxTurn,
            resolvedProfile,
            intent,
            cardFacts);
        AddPerformanceNotes(analysis, workspace, included, colorIdentityKnown, profileResolution, intent, cardFacts);
        analysis.Warnings.AddRange(profileResolution.Warnings);
        return analysis;
    }

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
            FreeFirstMulligan = UsesFreeFirstMulligan(workspace.Format),
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

    /// <summary>
    /// Draws an opening hand and applies the London mulligan heuristic.
    /// </summary>
    private static PerformanceOpeningHand DrawPerformanceOpeningHand(
        IReadOnlyList<DeckCard> libraryTemplate,
        Random random,
        bool includeMulligans,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts)
    {
        int mulligans = 0;
        int maximumMulligans = context.FreeFirstMulligan ? 3 : 2;
        List<DeckCard> firstSeven = [];
        while (mulligans <= maximumMulligans)
        {
            int targetHandSize = PerformanceTargetHandSize(mulligans, context);
            List<DeckCard> library = [.. libraryTemplate];
            Shuffle(library, random);
            int handCount = Math.Min(7, library.Count);
            List<DeckCard> hand = new(handCount);
            for (int index = 0; index < handCount; index++)
            {
                hand.Add(library[index]);
            }

            library.RemoveRange(0, handCount);
            if (mulligans == 0)
            {
                firstSeven = hand.ToList();
            }

            bool keep = !includeMulligans
                || IsKeepablePerformanceHand(hand, targetHandSize, mulligans, context, cardFacts)
                || targetHandSize <= 5;
            if (keep)
            {
                BottomPerformanceCards(hand, targetHandSize, context, cardFacts);
                return new PerformanceOpeningHand
                {
                    Hand = hand,
                    Library = library,
                    Mulligans = mulligans,
                    OpeningSevenLands = CountPerformanceRole(firstSeven, DeckRoles.Lands, cardFacts),
                };
            }

            mulligans++;
        }

        throw new InvalidOperationException("Mulligan heuristic failed to keep a hand by five cards.");
    }

    /// <summary>
    /// Expands included non-commander cards into individual library entries.
    /// </summary>
    private static List<DeckCard> ExpandPerformanceLibrary(
        IReadOnlyList<DeckCard> included,
        PerformanceCardFactsCache cardFacts)
    {
        List<DeckCard> cards = [];
        foreach (DeckCard card in included)
        {
            if (cardFacts.Get(card).IsCommander)
            {
                continue;
            }

            for (int copy = 0; copy < Math.Max(0, card.Quantity); copy++)
            {
                cards.Add(card);
            }
        }

        return cards;
    }

    /// <summary>
    /// Computes kept hand size after actual mulligans, including free first mulligans.
    /// </summary>
    private static int PerformanceTargetHandSize(
        int mulligans,
        PerformanceMulliganContext context)
    {
        int paidMulligans = context.FreeFirstMulligan && mulligans > 0
            ? mulligans - 1
            : mulligans;
        return Math.Max(0, 7 - paidMulligans);
    }

    /// <summary>
    /// Determines whether a candidate opening hand should be kept.
    /// </summary>
    private static bool IsKeepablePerformanceHand(
        IReadOnlyList<DeckCard> hand,
        int targetHandSize,
        int mulligans,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts)
    {
        int lands = CountPerformanceRole(hand, DeckRoles.Lands, cardFacts);
        if (lands == 0)
        {
            return false;
        }

        if (targetHandSize >= 6 && lands >= 6)
        {
            return false;
        }

        int earlyRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 2, cardFacts);
        if (targetHandSize >= 6 && lands == 1 && earlyRamp < 2)
        {
            return false;
        }

        double score = ScorePerformanceOpeningHand(hand, context, cardFacts);
        return score >= MinimumPerformanceKeepScore(targetHandSize, mulligans, context);
    }

    /// <summary>
    /// Scores how well an opening hand supports functional early development.
    /// </summary>
    private static double ScorePerformanceOpeningHand(
        IReadOnlyList<DeckCard> hand,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts)
    {
        int lands = CountPerformanceRole(hand, DeckRoles.Lands, cardFacts);
        int earlyRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 2, cardFacts);
        int oneManaRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 1, cardFacts);
        int earlyDraw = CountEarlyPerformanceRole(hand, DeckRoles.Draw, maxManaValue: 3, cardFacts);
        int cheapPlays = CountCheapPerformancePlays(hand, cardFacts);
        int earlyInteraction = CountEarlyPerformanceRole(hand, DeckRoles.Interaction, maxManaValue: 2, cardFacts)
            + CountEarlyPerformanceRole(hand, DeckRoles.Protection, maxManaValue: 2, cardFacts);
        int expensiveCards = hand.Count(card =>
            !cardFacts.Get(card).IsLand
            && cardFacts.Get(card).ManaValue >= 6);
        HashSet<string> colors = BuildOpeningHandColorAccess(hand, cardFacts);
        bool hasCastableEarlySpell = HasCastableEarlyPerformanceSpell(hand, cardFacts);
        bool hasCommanderPlan = HasPerformanceCommanderPlan(hand, context, cardFacts);
        bool hasEarlyPlan = earlyRamp > 0
            || earlyDraw > 0
            || cheapPlays >= 2
            || hasCastableEarlySpell
            || hasCommanderPlan;

        double score = lands switch
        {
            0 => -8,
            1 => earlyRamp >= 2 ? 2 : -4,
            2 => 4,
            3 => 5,
            4 => 3,
            5 => 1,
            _ => -4,
        };

        score += Math.Min(earlyRamp, 2) * context.Mulligan.EarlyRampWeight;
        score += oneManaRamp > 0 ? context.Mulligan.OneManaRampWeight : 0;
        score += Math.Min(earlyDraw, 2) * context.Mulligan.EarlyDrawWeight;
        score += Math.Min(cheapPlays, 3) * context.Mulligan.CheapPlayWeight;
        score += Math.Min(earlyInteraction, 2) * context.Mulligan.EarlyInteractionWeight;
        score += hasCastableEarlySpell ? 1 : -1;
        score += hasCommanderPlan ? context.Mulligan.CommanderPlanWeight : 0;
        score -= Math.Min(expensiveCards, 3);

        if (context.DeckColors.Count > 0)
        {
            int coveredColors = context.DeckColors.Count(color => colors.Contains(color));
            if (coveredColors == context.DeckColors.Count)
            {
                score += 1.5;
            }
            else if (coveredColors > 0)
            {
                score += 0.5;
            }
            else
            {
                score -= 2;
            }
        }

        if (lands >= 5 && !hasEarlyPlan)
        {
            score -= 2;
        }

        if (lands == 2 && earlyRamp == 0 && earlyDraw == 0 && !hasCommanderPlan)
        {
            score -= 1;
        }

        if (!hasEarlyPlan)
        {
            score -= 1;
        }

        return score;
    }

    /// <summary>
    /// Returns the minimum hand score needed before accepting a mulligan decision.
    /// </summary>
    private static double MinimumPerformanceKeepScore(
        int targetHandSize,
        int mulligans,
        PerformanceMulliganContext context)
    {
        if (targetHandSize <= 5)
        {
            return context.Mulligan.FiveCardKeepScore;
        }

        if (targetHandSize == 6)
        {
            return context.Mulligan.SixCardKeepScore;
        }

        return context.FreeFirstMulligan && mulligans == 0
            ? context.Mulligan.SevenCardFreeKeepScore
            : context.Mulligan.SevenCardKeepScore;
    }

    /// <summary>
    /// Counts cheap cards for opening-hand development.
    /// </summary>
    private static int CountCheapPerformancePlays(
        IReadOnlyList<DeckCard> hand,
        PerformanceCardFactsCache cardFacts)
    {
        return hand.Count(card =>
            !cardFacts.Get(card).IsLand
            && cardFacts.Get(card).ManaValue <= 2);
    }

    /// <summary>
    /// Counts early plays with a specific primary role.
    /// </summary>
    private static int CountEarlyPerformanceRole(
        IReadOnlyList<DeckCard> hand,
        string roleName,
        int maxManaValue,
        PerformanceCardFactsCache cardFacts)
    {
        return hand.Count(card =>
            cardFacts.Get(card).HasRole(roleName)
            && cardFacts.Get(card).ManaValue <= maxManaValue);
    }

    /// <summary>
    /// Checks whether the hand can deploy at least one cheap nonland card.
    /// </summary>
    private static bool HasCastableEarlyPerformanceSpell(
        IReadOnlyList<DeckCard> hand,
        PerformanceCardFactsCache cardFacts)
    {
        List<PerformanceManaSource> sources = BuildOpeningLandManaSources(hand, cardFacts);
        return hand.Any(card =>
            !cardFacts.Get(card).IsLand
            && cardFacts.Get(card).ManaValue <= 2
            && CanPay(cardFacts.Get(card), sources));
    }

    /// <summary>
    /// Checks whether opening resources plausibly cast the commander by turn four.
    /// </summary>
    private static bool HasPerformanceCommanderPlan(
        IReadOnlyList<DeckCard> hand,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts)
    {
        if (context.Commander is null)
        {
            return false;
        }

        PerformanceCardFacts commanderFacts = cardFacts.Get(context.Commander);
        int lands = CountPerformanceRole(hand, DeckRoles.Lands, cardFacts);
        int earlyRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 2, cardFacts);
        int expectedLandDropsByTurnFour = lands >= 2 ? Math.Min(4, lands + 1) : lands;
        int expectedManaByTurnFour = expectedLandDropsByTurnFour + Math.Min(earlyRamp, 2);
        if (expectedManaByTurnFour < commanderFacts.ManaValue)
        {
            return false;
        }

        List<PerformanceManaSource> sources = BuildOpeningHandManaSources(hand, cardFacts);
        return PerformanceMana.CanSatisfyRequirement(
            commanderFacts.CostRequirement,
            sources);
    }

    /// <summary>
    /// Builds mana sources available from lands and castable cheap fixing.
    /// </summary>
    private static List<PerformanceManaSource> BuildOpeningHandManaSources(
        IReadOnlyList<DeckCard> hand,
        PerformanceCardFactsCache cardFacts)
    {
        List<PerformanceManaSource> sources = BuildOpeningLandManaSources(hand, cardFacts);
        foreach (DeckCard card in hand)
        {
            PerformanceCardFacts facts = cardFacts.Get(card);
            if (facts.IsLand || facts.ManaValue > 2 || facts.ProducedMana.Count == 0)
            {
                continue;
            }

            if (CanPay(facts, sources))
            {
                sources.Add(new PerformanceManaSource(facts.ProducedMana));
            }
        }

        return sources;
    }

    /// <summary>
    /// Builds mana sources available from lands in the opening hand.
    /// </summary>
    private static List<PerformanceManaSource> BuildOpeningLandManaSources(
        IReadOnlyList<DeckCard> hand,
        PerformanceCardFactsCache cardFacts)
    {
        List<PerformanceManaSource> sources = [];
        foreach (DeckCard card in hand)
        {
            PerformanceCardFacts facts = cardFacts.Get(card);
            if (facts.IsLand && facts.ProducedMana.Count > 0)
            {
                sources.Add(new PerformanceManaSource(facts.ProducedMana));
            }
        }

        return sources;
    }

    /// <summary>
    /// Reads color symbols reachable from opening lands and castable cheap fixing.
    /// </summary>
    private static HashSet<string> BuildOpeningHandColorAccess(
        IReadOnlyList<DeckCard> hand,
        PerformanceCardFactsCache cardFacts)
    {
        return BuildOpeningHandManaSources(hand, cardFacts)
            .SelectMany(source => source.Symbols)
            .Where(symbol => PerformanceMana.ColoredSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bottoms cards after mulligans by preserving the highest-scoring kept hand.
    /// </summary>
    private static void BottomPerformanceCards(
        List<DeckCard> hand,
        int targetHandSize,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts)
    {
        int removeCount = hand.Count - targetHandSize;
        if (removeCount <= 0)
        {
            return;
        }

        List<int> bestIndexes = [];
        double bestScore = double.NegativeInfinity;
        int bestPriority = int.MinValue;
        foreach (List<int> indexes in EnumerateIndexCombinations(hand.Count, removeCount))
        {
            HashSet<int> bottomIndexes = [.. indexes];
            List<DeckCard> kept = hand
                .Where((_, index) => !bottomIndexes.Contains(index))
                .ToList();
            double score = ScorePerformanceOpeningHand(kept, context, cardFacts);
            int priority = indexes.Sum(index => PerformanceBottomPriority(hand[index], hand, cardFacts));
            if (score > bestScore || (score.Equals(bestScore) && priority > bestPriority))
            {
                bestScore = score;
                bestPriority = priority;
                bestIndexes = indexes;
            }
        }

        foreach (int index in bestIndexes.OrderByDescending(index => index))
        {
            hand.RemoveAt(index);
        }
    }

    /// <summary>
    /// Enumerates index combinations for choosing cards to bottom.
    /// </summary>
    private static IEnumerable<List<int>> EnumerateIndexCombinations(int count, int choose)
    {
        List<int> current = [];
        foreach (List<int> combination in EnumerateIndexCombinations(count, choose, start: 0, current))
        {
            yield return combination;
        }
    }

    /// <summary>
    /// Recursively enumerates index combinations in increasing order.
    /// </summary>
    private static IEnumerable<List<int>> EnumerateIndexCombinations(
        int count,
        int choose,
        int start,
        List<int> current)
    {
        if (current.Count == choose)
        {
            yield return [.. current];
            yield break;
        }

        for (int index = start; index <= count - (choose - current.Count); index++)
        {
            current.Add(index);
            foreach (List<int> combination in EnumerateIndexCombinations(count, choose, index + 1, current))
            {
                yield return combination;
            }

            current.RemoveAt(current.Count - 1);
        }
    }

    /// <summary>
    /// Scores a card for bottoming during the mulligan process.
    /// </summary>
    private static int PerformanceBottomPriority(
        DeckCard card,
        IReadOnlyList<DeckCard> hand,
        PerformanceCardFactsCache cardFacts)
    {
        PerformanceCardFacts facts = cardFacts.Get(card);
        int lands = CountPerformanceRole(hand, DeckRoles.Lands, cardFacts);
        if (facts.IsLand)
        {
            return lands > 3 ? 5 : 0;
        }

        if (facts.ManaValue >= 6)
        {
            return 4;
        }

        if (facts.IsWincon)
        {
            return 3;
        }

        if (facts.IsUtility)
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// Chooses the land drop that improves untapped color access when possible.
    /// </summary>
    private static DeckCard? ChoosePerformanceLand(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<PerformancePermanent> battlefield,
        IReadOnlyList<IReadOnlyList<string>> virtualManaSources,
        IReadOnlySet<string> deckColors,
        PerformanceCardFactsCache cardFacts)
    {
        HashSet<string> existingColors = ExtractColoredSymbols(GetPerformanceManaSources(
            battlefield,
            virtualManaSources,
            unavailablePermanent: null,
            cardFacts));
        List<DeckCard> lands = hand
            .Where(card => cardFacts.Get(card).IsLand)
            .ToList();
        return lands
            .OrderByDescending(card => cardFacts.Get(card).ProducedMana.Count(color => deckColors.Contains(color) && !existingColors.Contains(color)))
            .ThenBy(card => cardFacts.Get(card).LooksTapped ? 1 : 0)
            .ThenByDescending(card => cardFacts.Get(card).ProducedMana.Count)
            .FirstOrDefault();
    }

    /// <summary>
    /// Scores spell sequencing priority for one turn of heuristic development.
    /// </summary>
    private static int PerformanceCastPriority(
        PerformanceCardFacts facts,
        int turn,
        SimulationProfile profile)
    {
        if (turn <= 3 && facts.IsRamp)
        {
            return profile.Sequencing.EarlyRampPriority;
        }

        if (facts.IsDraw || facts.HasTag(DeckTags.Engines))
        {
            return profile.Sequencing.DrawPriority;
        }

        if (facts.IsTutor)
        {
            return profile.Sequencing.TutorPriority;
        }

        if (facts.HasComboPieceOrEnabler)
        {
            return profile.Sequencing.ComboPriority;
        }

        if (facts.IsWincon || facts.HasTag(DeckTags.Finishers))
        {
            return profile.Sequencing.WinconPriority;
        }

        return profile.Sequencing.DefaultPriority;
    }

    /// <summary>
    /// Determines whether a nonpermanent spell should be held for interaction or protection.
    /// </summary>
    private static bool ShouldHoldPerformanceSpell(
        PerformanceCardFacts facts,
        int turn,
        bool commanderCast,
        SimulationProfile profile)
    {
        if (facts.IsPermanent)
        {
            return false;
        }

        bool interaction = facts.IsInteraction || facts.IsBoardWipe;
        if (interaction && turn >= profile.Sequencing.HoldInteractionFromTurn && profile.Sequencing.MinimumInteractionHeld > 0)
        {
            return true;
        }

        return commanderCast
            && profile.Sequencing.HoldProtectionWhenCommanderOnline
            && facts.IsProtection;
    }

    /// <summary>
    /// Checks whether cached card facts can be paid from the available sources.
    /// </summary>
    private static bool CanPay(
        PerformanceCardFacts facts,
        IReadOnlyList<PerformanceManaSource> availableSources)
    {
        return PerformanceMana.CanPay(facts.CostRequirement, facts.ManaValue, availableSources);
    }

    /// <summary>
    /// Attempts to pay cached card facts from the available sources.
    /// </summary>
    private static bool TryPay(
        PerformanceCardFacts facts,
        IReadOnlyList<PerformanceManaSource> availableSources,
        out List<PerformanceManaSource> remainingSources)
    {
        return PerformanceMana.TryPay(facts.CostRequirement, facts.ManaValue, availableSources, out remainingSources);
    }

    /// <summary>
    /// Lists battlefield and virtual mana sources available for a turn.
    /// </summary>
    private static List<PerformanceManaSource> GetPerformanceManaSources(
        IReadOnlyList<PerformancePermanent> battlefield,
        IReadOnlyList<IReadOnlyList<string>> virtualManaSources,
        PerformancePermanent? unavailablePermanent,
        PerformanceCardFactsCache cardFacts)
    {
        List<PerformanceManaSource> sources = [];
        foreach (PerformancePermanent permanent in battlefield)
        {
            PerformanceCardFacts facts = cardFacts.Get(permanent.Card);
            if (ReferenceEquals(permanent, unavailablePermanent) || !facts.IsManaSource)
            {
                continue;
            }

            sources.Add(new PerformanceManaSource(facts.ProducedMana));
        }

        sources.AddRange(virtualManaSources.Select(source => new PerformanceManaSource(source)));
        return sources;
    }

    /// <summary>
    /// Checks whether a card contributes mana in the heuristic simulation.
    /// </summary>
    /// <summary>
    /// Gets color symbols from currently available mana sources.
    /// </summary>
    private static HashSet<string> ExtractColoredSymbols(IEnumerable<PerformanceManaSource> sources)
    {
        return sources
            .SelectMany(source => source.Symbols)
            .Where(symbol => PerformanceMana.ColoredSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts a nonpermanent ramp spell into a future virtual mana source.
    /// </summary>
    private static IReadOnlyList<string> BuildPerformanceRampSource(
        PerformanceCardFacts facts,
        IReadOnlySet<string> deckColors)
    {
        if (facts.ProducedMana.Count > 0)
        {
            return facts.ProducedMana;
        }

        return deckColors.Count > 0 ? deckColors.ToList() : [];
    }

    /// <summary>
    /// Moves the top library card into hand.
    /// </summary>
    private static void PerformanceDrawOne(List<DeckCard> hand, List<DeckCard> library)
    {
        hand.Add(library[0]);
        library.RemoveAt(0);
    }

    /// <summary>
    /// Calculates what share of nonland hand cards are castable with current resources.
    /// </summary>
    private static double CalculatePerformanceCastableHandRate(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<PerformanceManaSource> availableSources,
        PerformanceCardFactsCache cardFacts)
    {
        int spells = 0;
        int castable = 0;
        foreach (DeckCard card in hand)
        {
            PerformanceCardFacts facts = cardFacts.Get(card);
            if (facts.IsLand || facts.IsCommander)
            {
                continue;
            }

            spells++;
            if (CanPay(facts, availableSources))
            {
                castable++;
            }
        }

        if (spells == 0)
        {
            return 1;
        }

        return castable / (double)spells;
    }

    /// <summary>
    /// Checks whether a role card is held in hand and castable with unused mana.
    /// </summary>
    private static bool HasHeldPerformanceRole(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<PerformanceManaSource> availableSources,
        string roleName,
        PerformanceCardFactsCache cardFacts)
    {
        return hand.Any(card =>
            cardFacts.Get(card).HasRole(roleName)
            && CanPay(cardFacts.Get(card), availableSources));
    }

    /// <summary>
    /// Checks whether any card in a zone has the requested primary role.
    /// </summary>
    private static bool PerformanceHasRole(
        IEnumerable<DeckCard> cards,
        string roleName,
        PerformanceCardFactsCache cardFacts)
    {
        return cards.Any(card => cardFacts.Get(card).HasRole(roleName));
    }

    /// <summary>
    /// Checks whether any card in a zone has any requested primary role.
    /// </summary>
    private static bool PerformanceHasAnyRole(
        IEnumerable<DeckCard> cards,
        PerformanceCardFactsCache cardFacts,
        params string[] roleNames)
    {
        return cards.Any(card =>
            roleNames.Any(roleName => cardFacts.Get(card).HasRole(roleName)));
    }

    /// <summary>
    /// Checks whether any card in a zone has the requested tag.
    /// </summary>
    private static bool PerformanceHasTag(
        IEnumerable<DeckCard> cards,
        string tag,
        PerformanceCardFactsCache cardFacts)
    {
        return cards.Any(card => cardFacts.Get(card).HasTag(tag));
    }

    /// <summary>
    /// Counts cards in a zone with the requested primary role.
    /// </summary>
    private static int CountPerformanceRole(
        IEnumerable<DeckCard> cards,
        string roleName,
        PerformanceCardFactsCache cardFacts)
    {
        return cards.Count(card => cardFacts.Get(card).HasRole(roleName));
    }

    /// <summary>
    /// Counts distinct seen cards that contribute to combo assembly.
    /// </summary>
    private static int CountPerformanceComboCards(
        IEnumerable<DeckCard> cards,
        bool includeTutors,
        PerformanceCardFactsCache cardFacts)
    {
        return cards
            .Where(card =>
            {
                PerformanceCardFacts facts = cardFacts.Get(card);
                return facts.HasComboPieceOrEnabler || (includeTutors && facts.IsTutor);
            })
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>
    /// Records high-mana cards still stranded by the final simulated turn.
    /// </summary>
    private static void AddPerformanceStrandedCards(
        PerformanceRun run,
        IReadOnlyList<DeckCard> hand,
        PerformanceTurnState? lastTurn,
        int maxTurn,
        PerformanceCardFactsCache cardFacts)
    {
        if (lastTurn is null)
        {
            return;
        }

        foreach (DeckCard card in hand)
        {
            PerformanceCardFacts facts = cardFacts.Get(card);
            if (facts.IsLand || facts.IsCommander || facts.ManaValue < 4)
            {
                continue;
            }

            bool manaStranded = facts.ManaValue > lastTurn.ManaSources;
            bool colorStranded = !PerformanceMana.CanSatisfyRequirement(facts.CostRequirement, lastTurn.UntappedManaSources);
            if (!manaStranded && !colorStranded)
            {
                continue;
            }

            run.StrandedCards[card.Name] = new PerformanceStrandedRun
            {
                CardName = card.Name,
                ManaValue = facts.ManaValue,
                ManaStranded = manaStranded,
                ColorStranded = colorStranded,
                FinalTurn = maxTurn,
            };
        }
    }

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
        IReadOnlyList<DeckCard> included,
        IReadOnlyList<PerformanceRun> runs,
        int maxTurn,
        PerformanceCardFactsCache cardFacts)
    {
        CommanderPerformance result = new()
        {
            CommanderNames = included
                .Where(card => cardFacts.Get(card).IsCommander)
                .Select(card => card.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>
    /// Enumerates cards included in deck construction analysis.
    /// </summary>
    private static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        return DeckCategoryInclusion.IncludedCards(workspace);
    }

    /// <summary>
    /// Gets the deck color identity from commanders, falling back to included cards.
    /// </summary>
    private static (bool IsKnown, HashSet<string> Colors) GetDeckColorIdentity(
        IReadOnlyList<DeckCard> included,
        PerformanceCardFactsCache cardFacts)
    {
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        bool foundCommander = false;
        foreach (DeckCard card in included)
        {
            PerformanceCardFacts facts = cardFacts.Get(card);
            if (!facts.IsCommander)
            {
                continue;
            }

            foundCommander = true;
            AddDeckColors(colors, facts.Snapshot.ColorIdentity);
        }

        if (foundCommander)
        {
            return (true, colors);
        }

        foreach (DeckCard card in included)
        {
            AddDeckColors(colors, cardFacts.Get(card).Snapshot.ColorIdentity);
        }

        return (colors.Count > 0, colors);
    }

    /// <summary>
    /// Shuffles cards in place using Fisher-Yates.
    /// </summary>
    private static void Shuffle(List<DeckCard> cards, Random random)
    {
        for (int index = cards.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (cards[index], cards[swapIndex]) = (cards[swapIndex], cards[index]);
        }
    }

    /// <summary>
    /// Adds color identity symbols to a set.
    /// </summary>
    private static void AddDeckColors(HashSet<string> colors, IEnumerable<string> colorIdentity)
    {
        foreach (string color in colorIdentity)
        {
            if (PerformanceMana.ColoredSymbols.Contains(color, StringComparer.OrdinalIgnoreCase))
            {
                colors.Add(color);
            }
        }
    }

    /// <summary>
    /// Checks whether text contains any supplied phrase.
    /// </summary>
    private static bool ContainsAny(string value, params ReadOnlySpan<string> needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a format normally receives a free first mulligan.
    /// </summary>
    private static bool UsesFreeFirstMulligan(string format)
    {
        return ContainsAny(format, "commander", "brawl");
    }

    /// <summary>
    /// Checks whether the format uses Commander deck construction limits.
    /// </summary>
    private static bool IsCommanderFormat(string format)
    {
        return format.Trim().Equals("commander", StringComparison.OrdinalIgnoreCase)
            || format.Trim().Equals("edh", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds assumptions and warnings that explain simulator boundaries.
    /// </summary>
    private static void AddPerformanceNotes(
        DeckPerformanceAnalysis analysis,
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> included,
        bool colorIdentityKnown,
        ResolvedSimulationProfile profileResolution,
        DeckIntent? intent,
        PerformanceCardFactsCache cardFacts)
    {
        analysis.Assumptions.Add("Simulation uses cached Scryfall snapshots and local role/tag heuristics.");
        analysis.Assumptions.Add($"Each run draws one card per turn, plays one land per turn, and sequences spells with the '{profileResolution.Profile.Id}' simulation profile.");
        analysis.Assumptions.Add($"Simulation profile source: {profileResolution.Source}.");
        analysis.Assumptions.Add("Opponent interaction, stack timing, replacement effects, activated abilities, and full Magic rules are not simulated.");
        analysis.Assumptions.Add("London mulligans draw seven and bottom cards using a deterministic plan-aware keep heuristic.");
        if (UsesFreeFirstMulligan(workspace.Format))
        {
            analysis.Assumptions.Add("Commander and Brawl performance treats the first mulligan as free.");
        }

        analysis.Assumptions.Add("Nonpermanent ramp becomes one future mana source; draw spells draw one card.");
        if (intent is not null)
        {
            analysis.Assumptions.Add("Saved deck intent can adjust the active heuristic profile and scenario target turns.");
        }

        if (!workspace.Format.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            analysis.Warnings.Add("Default scenarios are Commander-oriented; interpret non-Commander output as generic heuristic sampling.");
        }

        if (!included.Any(card => cardFacts.Get(card).IsCommander))
        {
            analysis.Warnings.Add("No commander category was detected, so commander timing scenarios will be zero.");
        }

        if (!colorIdentityKnown)
        {
            analysis.Warnings.Add("Deck color identity could not be inferred from commander or included card snapshots.");
        }

        if (IsCommanderFormat(workspace.Format) && analysis.DeckSize != 100)
        {
            analysis.Warnings.Add(
                $"Commander workspace has {analysis.DeckSize} included cards instead of 100; excluded categories such as Sideboard and Maybeboard are not sampled, so performance probabilities reflect a partial active deck.");
        }

        if (analysis.DeckSize < 60)
        {
            analysis.Warnings.Add("Included deck size is below most constructed deck sizes; probability estimates may be unusual.");
        }
    }

    /// <summary>
    /// Caches per-card facts that are reused throughout one performance analysis.
    /// </summary>
    private sealed class PerformanceCardFactsCache
    {
        /// <summary>
        /// Stores cached facts by the deck card references used during simulation.
        /// </summary>
        private readonly Dictionary<DeckCard, PerformanceCardFacts> facts = [];

        /// <summary>
        /// Initializes the cache with the deck's included cards.
        /// </summary>
        public PerformanceCardFactsCache(IEnumerable<DeckCard> cards)
        {
            foreach (DeckCard card in cards)
            {
                Get(card);
            }
        }

        /// <summary>
        /// Gets cached facts for a card, creating them for late-discovered references when needed.
        /// </summary>
        public PerformanceCardFacts Get(DeckCard card)
        {
            if (!facts.TryGetValue(card, out PerformanceCardFacts? cardFacts))
            {
                cardFacts = new PerformanceCardFacts(card);
                facts[card] = cardFacts;
            }

            return cardFacts;
        }
    }

    /// <summary>
    /// Stores expensive role, mana, and text-derived facts for one card.
    /// </summary>
    private sealed class PerformanceCardFacts
    {
        /// <summary>
        /// Builds reusable facts for performance simulation and reporting.
        /// </summary>
        public PerformanceCardFacts(DeckCard card)
        {
            Snapshot = PerformanceMana.GetSnapshot(card);
            Role = DeckRoleClassifier.Classify(card);
            ManaValue = PerformanceMana.ManaValue(card);
            CostRequirement = PerformanceMana.BuildCostRequirement(card);
            ProducedMana = PerformanceMana.ReadProducedMana(card);
            IsCommander = DeckCategoryOrdering.PrimaryCategory(card).Equals(
                DeckRoles.Commander,
                StringComparison.OrdinalIgnoreCase);
            IsLand = HasRole(DeckRoles.Lands);
            IsRamp = HasRole(DeckRoles.Ramp);
            IsDraw = HasRole(DeckRoles.Draw);
            IsTutor = HasRole(DeckRoles.Tutors);
            IsInteraction = HasRole(DeckRoles.Interaction);
            IsBoardWipe = HasRole(DeckRoles.BoardWipes);
            IsProtection = HasRole(DeckRoles.Protection);
            IsWincon = HasRole(DeckRoles.Wincons);
            IsUtility = HasRole(DeckRoles.Utility);
            IsPermanent = ContainsAny(
                Snapshot.TypeLine ?? "",
                "Creature",
                "Artifact",
                "Enchantment",
                "Planeswalker",
                "Battle",
                "Land");
            IsManaSource = IsLand || IsRamp || ProducedMana.Count > 0;
            LooksTapped = PerformanceMana.LooksTapped(Snapshot);
            HasComboPieceOrEnabler = HasTag(DeckTags.ComboPiece) || HasTag(DeckTags.ComboEnabler);
        }

        /// <summary>
        /// Gets the cached card snapshot used by performance heuristics.
        /// </summary>
        public CardSnapshot Snapshot { get; }

        /// <summary>
        /// Gets the cached role classifier output.
        /// </summary>
        public CardRoleAssignment Role { get; }

        /// <summary>
        /// Gets the nonnegative mana value used for payment checks.
        /// </summary>
        public int ManaValue { get; }

        /// <summary>
        /// Gets the parsed colored and colorless cost requirements.
        /// </summary>
        public PerformanceCostRequirement CostRequirement { get; }

        /// <summary>
        /// Gets the mana symbols this card can produce.
        /// </summary>
        public IReadOnlyList<string> ProducedMana { get; }

        /// <summary>
        /// Gets whether the card is in the Commander category.
        /// </summary>
        public bool IsCommander { get; }

        /// <summary>
        /// Gets whether the card is classified as a land.
        /// </summary>
        public bool IsLand { get; }

        /// <summary>
        /// Gets whether the card is classified as ramp.
        /// </summary>
        public bool IsRamp { get; }

        /// <summary>
        /// Gets whether the card is classified as card draw.
        /// </summary>
        public bool IsDraw { get; }

        /// <summary>
        /// Gets whether the card is classified as a tutor.
        /// </summary>
        public bool IsTutor { get; }

        /// <summary>
        /// Gets whether the card is classified as interaction.
        /// </summary>
        public bool IsInteraction { get; }

        /// <summary>
        /// Gets whether the card is classified as a board wipe.
        /// </summary>
        public bool IsBoardWipe { get; }

        /// <summary>
        /// Gets whether the card is classified as protection.
        /// </summary>
        public bool IsProtection { get; }

        /// <summary>
        /// Gets whether the card is classified as a win condition.
        /// </summary>
        public bool IsWincon { get; }

        /// <summary>
        /// Gets whether the card is classified as utility.
        /// </summary>
        public bool IsUtility { get; }

        /// <summary>
        /// Gets whether the card type normally remains on the battlefield after casting.
        /// </summary>
        public bool IsPermanent { get; }

        /// <summary>
        /// Gets whether the card can contribute mana in the simulation.
        /// </summary>
        public bool IsManaSource { get; }

        /// <summary>
        /// Gets whether the card appears to enter tapped.
        /// </summary>
        public bool LooksTapped { get; }

        /// <summary>
        /// Gets whether the card is tagged as a combo piece or enabler.
        /// </summary>
        public bool HasComboPieceOrEnabler { get; }

        /// <summary>
        /// Checks whether the cached primary role matches a role name.
        /// </summary>
        public bool HasRole(string role)
        {
            return Role.PrimaryRole.Equals(role, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks whether the cached secondary tags include a tag name.
        /// </summary>
        public bool HasTag(string tag)
        {
            return Role.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Gets the latest recorded state for each run at a requested turn.
    /// </summary>
    private static List<PerformanceTurnState> StatesForTurn(
        IReadOnlyList<PerformanceRun> runs,
        int turn)
    {
        return runs
            .Select(run => StateAt(run, turn))
            .Where(state => state is not null)
            .Cast<PerformanceTurnState>()
            .ToList();
    }

    /// <summary>
    /// Gets the latest recorded state for a run at a requested turn.
    /// </summary>
    private static PerformanceTurnState? StateAt(PerformanceRun run, int turn)
    {
        return run.Turns.LastOrDefault(state => state.Turn <= turn);
    }

}
