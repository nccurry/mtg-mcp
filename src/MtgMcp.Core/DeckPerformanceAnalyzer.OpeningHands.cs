namespace MtgMcp.Core;

/// <summary>
/// Contains opening-hand, mulligan, and bottoming heuristics.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
    /// <summary>
    /// Draws an opening hand and applies the London mulligan heuristic.
    /// </summary>
    private static PerformanceOpeningHand DrawPerformanceOpeningHand(
        IReadOnlyList<DeckCard> libraryTemplate,
        DeterministicSimulationRandom random,
        bool includeMulligans,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts,
        bool collectDecisionEvents)
    {
        int mulligans = 0;
        int maximumMulligans = MulliganHeuristics.MaximumMulligans(context.FreeFirstMulligan);
        List<DeckCard> firstSeven = [];
        List<PerformanceDecisionEvent>? decisionEvents = collectDecisionEvents ? [] : null;
        while (mulligans <= maximumMulligans)
        {
            int targetHandSize = MulliganHeuristics.TargetHandSize(mulligans, context.FreeFirstMulligan);
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

            bool heuristicKeep = IsKeepablePerformanceHand(hand, targetHandSize, mulligans, context, cardFacts);
            bool forcedFloorKeep = targetHandSize <= 5;
            bool keep = !includeMulligans
                || heuristicKeep
                || targetHandSize <= 5;
            AddPerformanceMulliganDecisionEvent(
                decisionEvents,
                hand,
                targetHandSize,
                mulligans,
                includeMulligans,
                context,
                cardFacts,
                keep,
                heuristicKeep,
                forcedFloorKeep);
            if (keep)
            {
                List<string> bottomedCards = BottomPerformanceCards(hand, targetHandSize, context, cardFacts);
                if (bottomedCards.Count > 0)
                {
                    AddPerformanceDecisionEvent(
                        decisionEvents,
                        "bottoming",
                        null,
                        "london-mulligan-bottom",
                        "bottomed",
                        string.Join(", ", bottomedCards),
                        "bottomed the cards that preserved the highest kept-hand score.",
                        $"target hand size: {targetHandSize}");
                }

                return new PerformanceOpeningHand
                {
                    Hand = hand,
                    Library = library,
                    Mulligans = mulligans,
                    OpeningSevenLands = CountPerformanceRole(firstSeven, DeckRoles.Lands, cardFacts),
                    DecisionEvents = decisionEvents ?? [],
                };
            }

            mulligans++;
        }

        throw new InvalidOperationException("Mulligan heuristic failed to keep a hand by five cards.");
    }

    /// <summary>
    /// Adds a compact explanation for one opening-hand keep or mulligan decision.
    /// </summary>
    private static void AddPerformanceMulliganDecisionEvent(
        List<PerformanceDecisionEvent>? decisionEvents,
        IReadOnlyList<DeckCard> hand,
        int targetHandSize,
        int mulligans,
        bool includeMulligans,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts,
        bool keep,
        bool heuristicKeep,
        bool forcedFloorKeep)
    {
        double score = ScorePerformanceOpeningHand(hand, context, cardFacts);
        double threshold = MinimumPerformanceKeepScore(targetHandSize, mulligans, context);
        int lands = CountPerformanceRole(hand, DeckRoles.Lands, cardFacts);
        int earlyRamp = CountEarlyPerformanceRole(hand, DeckRoles.Ramp, maxManaValue: 2, cardFacts);
        int earlyDraw = CountEarlyPerformanceRole(hand, DeckRoles.Draw, maxManaValue: 3, cardFacts);
        string rationale = keep switch
        {
            true when !includeMulligans => "mulligans were disabled, so the simulator kept the first hand.",
            true when forcedFloorKeep => "London mulligan floor reached five cards, so the simulator kept the hand.",
            true when heuristicKeep => "opening-hand score met the profile keep threshold.",
            _ => "opening-hand score or land mix missed the profile keep threshold.",
        };
        AddPerformanceDecisionEvent(
            decisionEvents,
            "mulligan",
            null,
            "opening-hand",
            keep ? "keep" : "reject",
            $"mulligan {mulligans}",
            rationale,
            $"target hand size: {targetHandSize}",
            $"score: {score:0.0}",
            $"threshold: {threshold:0.0}",
            $"lands: {lands}",
            $"early ramp: {earlyRamp}",
            $"early draw: {earlyDraw}");
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
    /// Checks whether opening resources plausibly cast the commander by the profile target turn.
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
        int targetTurn = Math.Max(1, context.CommanderTargetTurn);
        int expectedLandDrops = lands >= 2 ? Math.Min(targetTurn, lands + 1) : lands;
        int expectedMana = expectedLandDrops + Math.Min(earlyRamp, 2);
        if (expectedMana < commanderFacts.ManaValue)
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
    private static List<string> BottomPerformanceCards(
        List<DeckCard> hand,
        int targetHandSize,
        PerformanceMulliganContext context,
        PerformanceCardFactsCache cardFacts)
    {
        int removeCount = hand.Count - targetHandSize;
        if (removeCount <= 0)
        {
            return [];
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

        List<string> bottomedCards = bestIndexes
            .Select(index => hand[index].Name)
            .ToList();
        foreach (int index in bestIndexes.OrderByDescending(index => index))
        {
            hand.RemoveAt(index);
        }

        return bottomedCards;
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

}
