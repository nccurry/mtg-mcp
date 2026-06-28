namespace MtgMcp.Core;

/// <summary>
/// Calculates deck statistics and draw odds.
/// </summary>
public static class DeckStatistics
{
    /// <summary>
    /// Analyzes draw odds for the requested targets.
    /// </summary>
    public static DeckOddsAnalysis AnalyzeDrawOdds(
        DeckWorkspace workspace,
        IReadOnlyList<string> targets,
        int turn,
        int openingHandSize,
        int simulations,
        int seed)
    {
        List<DeckCard> includedCards = IncludedCards(workspace).ToList();
        int deckSize = includedCards.Sum(card => Math.Max(0, card.Quantity));
        int cardsSeen = Math.Clamp(openingHandSize + Math.Max(0, turn - 1), 0, deckSize);
        int safeSimulations = Math.Clamp(simulations, 100, 100_000);

        DeckOddsAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            DeckSize = deckSize,
            OpeningHandSize = openingHandSize,
            Turn = turn,
            CardsSeen = cardsSeen,
            Simulations = safeSimulations,
            RngKind = DeterministicSimulationRandom.Kind
        };

        foreach (string target in targets)
        {
            int successes = includedCards
                .Where(card => DeckRoleClassifier.MatchesTarget(card, target))
                .Sum(card => Math.Max(0, card.Quantity));

            analysis.Rows.Add(new DeckOddsRow
            {
                Target = target,
                SuccessesInDeck = successes,
                HypergeometricAtLeastOne = HypergeometricAtLeast(deckSize, successes, cardsSeen, 1),
                HypergeometricAtLeastTwo = HypergeometricAtLeast(deckSize, successes, cardsSeen, 2),
                MonteCarloAtLeastOne = MonteCarloAtLeastOne(includedCards, target, cardsSeen, safeSimulations, seed)
            });
        }

        return analysis;
    }

    /// <summary>
    /// Analyzes the odds of making each land drop through a target turn.
    /// </summary>
    public static LandDropOddsAnalysis AnalyzeLandDropOdds(
        DeckWorkspace workspace,
        int turn,
        int openingHandSize,
        bool onThePlay,
        bool includeMulligans,
        int simulations,
        int seed)
    {
        List<DeckCard> includedCards = IncludedCards(workspace).ToList();
        int deckSize = includedCards.Sum(card => Math.Max(0, card.Quantity));
        int landCount = includedCards
            .Where(IsLandSource)
            .Sum(card => Math.Max(0, card.Quantity));
        int targetTurn = Math.Clamp(turn, 1, 12);
        int safeOpeningHandSize = Math.Clamp(openingHandSize, 1, Math.Max(1, deckSize));
        int safeSimulations = Math.Clamp(simulations, 100, 100_000);
        LandDropOddsAnalysis analysis = new()
        {
            WorkspaceId = workspace.Id,
            DeckSize = deckSize,
            LandCount = landCount,
            EffectiveLandSources = landCount,
            Turn = targetTurn,
            OnThePlay = onThePlay,
            IncludeMulligans = includeMulligans,
            Simulations = safeSimulations,
            RngKind = DeterministicSimulationRandom.Kind,
            Assumptions =
            [
                "Exact rows use hypergeometric no-mulligan odds.",
                onThePlay
                    ? "On the play: no draw step is counted on turn 1."
                    : "On the draw: the turn 1 draw step is counted.",
                includeMulligans
                    ? "Monte Carlo uses one deterministic London-style mulligan for hands with 0-1 or 6-7 lands, then evaluates the kept hand after one bottomed card."
                    : "Monte Carlo uses the opening hand with no mulligan."
            ]
        };

        for (int currentTurn = 1; currentTurn <= targetTurn; currentTurn++)
        {
            int cardsSeen = Math.Clamp(safeOpeningHandSize + CardsDrawnByTurn(currentTurn, onThePlay), 0, deckSize);
            double makeExact = HypergeometricAtLeast(deckSize, landCount, cardsSeen, currentTurn);
            double makeMonteCarlo = MonteCarloMakeLandDrop(
                includedCards,
                currentTurn,
                safeOpeningHandSize,
                onThePlay,
                includeMulligans,
                safeSimulations,
                seed);
            analysis.Rows.Add(new LandDropOddsRow
            {
                Turn = currentTurn,
                CardsSeen = cardsSeen,
                HypergeometricMakeLandDrop = makeExact,
                HypergeometricMissLandDrop = 1 - makeExact,
                MonteCarloMakeLandDrop = makeMonteCarlo,
                MonteCarloMissLandDrop = 1 - makeMonteCarlo
            });
        }

        AddLandDropFailureDrivers(analysis);
        return analysis;
    }

    /// <summary>
    /// Calculates hypergeometric odds of at least the requested successes.
    /// </summary>
    public static double HypergeometricAtLeast(
        int populationSize,
        int successStates,
        int draws,
        int minimumSuccesses
    )
    {
        if (populationSize <= 0 || successStates <= 0 || draws <= 0)
        {
            return 0;
        }

        int maxSuccesses = Math.Min(successStates, draws);
        if (minimumSuccesses <= 0)
        {
            return 1;
        }

        if (minimumSuccesses > maxSuccesses)
        {
            return 0;
        }

        double probability = 0;
        for (int successes = minimumSuccesses; successes <= maxSuccesses; successes++)
        {
            int failures = draws - successes;
            if (failures > populationSize - successStates)
            {
                continue;
            }

            double logProbability =
                LogCombination(successStates, successes)
                + LogCombination(populationSize - successStates, failures)
                - LogCombination(populationSize, draws);
            probability += Math.Exp(logProbability);
        }

        return Math.Clamp(probability, 0, 1);
    }

    /// <summary>
    /// Calculates at least one odds with Monte Carlo simulation.
    /// </summary>
    private static double MonteCarloAtLeastOne(
        IReadOnlyList<DeckCard> includedCards,
        string target,
        int cardsSeen,
        int simulations,
        int seed)
    {
        List<bool> deck = [];
        foreach (DeckCard card in includedCards)
        {
            bool success = DeckRoleClassifier.MatchesTarget(card, target);
            for (int count = 0; count < Math.Max(0, card.Quantity); count++)
            {
                deck.Add(success);
            }
        }

        if (deck.Count == 0 || cardsSeen <= 0)
        {
            return 0;
        }

        DeterministicSimulationRandom random = new(StableSeed(seed, target));
        int hits = 0;
        int sampleSize = Math.Min(cardsSeen, deck.Count);
        for (int simulation = 0; simulation < simulations; simulation++)
        {
            bool found = false;
            HashSet<int> seenIndexes = [];
            while (seenIndexes.Count < sampleSize)
            {
                int index = random.Next(deck.Count);
                if (!seenIndexes.Add(index))
                {
                    continue;
                }

                if (deck[index])
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                hits++;
            }
        }

        return hits / (double)simulations;
    }

    /// <summary>
    /// Runs deterministic Monte Carlo for making one land drop by a turn.
    /// </summary>
    private static double MonteCarloMakeLandDrop(
        IReadOnlyList<DeckCard> includedCards,
        int turn,
        int openingHandSize,
        bool onThePlay,
        bool includeMulligans,
        int simulations,
        int seed)
    {
        List<bool> deck = [];
        foreach (DeckCard card in includedCards)
        {
            bool isLand = IsLandSource(card);
            for (int count = 0; count < Math.Max(0, card.Quantity); count++)
            {
                deck.Add(isLand);
            }
        }

        if (deck.Count == 0)
        {
            return 0;
        }

        DeterministicSimulationRandom random = new(StableSeed(seed, $"land-drop-{turn}-{onThePlay}-{includeMulligans}"));
        int hits = 0;
        for (int simulation = 0; simulation < simulations; simulation++)
        {
            List<bool> shuffled = Shuffle(deck, random);
            int handSize = Math.Min(openingHandSize, shuffled.Count);
            List<bool> hand = shuffled.Take(handSize).ToList();
            int nextIndex = handSize;
            if (includeMulligans && ShouldMulliganByLandCount(hand))
            {
                shuffled = Shuffle(deck, random);
                handSize = Math.Min(openingHandSize, shuffled.Count);
                hand = shuffled.Take(handSize).ToList();
                nextIndex = handSize;
                if (hand.Count > 0)
                {
                    bool bottomLand = CountLands(hand) >= 4;
                    int bottomIndex = bottomLand ? hand.FindIndex(card => card) : hand.FindIndex(card => !card);
                    if (bottomIndex < 0)
                    {
                        bottomIndex = hand.Count - 1;
                    }

                    hand.RemoveAt(bottomIndex);
                }
            }

            int draws = CardsDrawnByTurn(turn, onThePlay);
            for (int draw = 0; draw < draws && nextIndex < shuffled.Count; draw++)
            {
                hand.Add(shuffled[nextIndex]);
                nextIndex++;
            }

            if (CountLands(hand) >= turn)
            {
                hits++;
            }
        }

        return hits / (double)simulations;
    }

    /// <summary>
    /// Counts card draws by a turn under play/draw assumptions.
    /// </summary>
    private static int CardsDrawnByTurn(int turn, bool onThePlay)
    {
        return onThePlay ? Math.Max(0, turn - 1) : Math.Max(0, turn);
    }

    /// <summary>
    /// Checks whether a card is treated as an early land source.
    /// </summary>
    private static bool IsLandSource(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string typeLine = card.Snapshot?.TypeLine ?? "";
        return role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            || typeLine.Contains("Land", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies a simple deterministic mulligan rule based on opening land count.
    /// </summary>
    private static bool ShouldMulliganByLandCount(IReadOnlyList<bool> hand)
    {
        int lands = CountLands(hand);
        return lands <= 1 || lands >= 6;
    }

    /// <summary>
    /// Counts land cards in a sampled hand.
    /// </summary>
    private static int CountLands(IEnumerable<bool> cards)
    {
        return cards.Count(card => card);
    }

    /// <summary>
    /// Shuffles a boolean deck using a caller-owned deterministic random source.
    /// </summary>
    private static List<bool> Shuffle(IReadOnlyList<bool> deck, DeterministicSimulationRandom random)
    {
        List<bool> shuffled = deck.ToList();
        for (int index = shuffled.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled;
    }

    /// <summary>
    /// Adds high-level deterministic failure-driver notes.
    /// </summary>
    private static void AddLandDropFailureDrivers(LandDropOddsAnalysis analysis)
    {
        if (analysis.DeckSize <= 0)
        {
            analysis.FailureDrivers.Add("The deck has no included cards.");
            return;
        }

        double landRate = analysis.LandCount / (double)analysis.DeckSize;
        if (landRate < 0.34)
        {
            analysis.FailureDrivers.Add("Land density is below 34%, which raises early land-drop miss risk.");
        }

        LandDropOddsRow? targetRow = analysis.Rows.LastOrDefault();
        if (targetRow is not null && targetRow.MonteCarloMissLandDrop >= 0.25)
        {
            analysis.FailureDrivers.Add($"The simulated miss rate for turn {targetRow.Turn} is at least 25%.");
        }
    }

    /// <summary>
    /// Calculates the log combination.
    /// </summary>
    private static double LogCombination(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return double.NegativeInfinity;
        }

        k = Math.Min(k, n - k);
        double result = 0;
        for (int index = 1; index <= k; index++)
        {
            result += Math.Log(n - (k - index)) - Math.Log(index);
        }

        return result;
    }

    /// <summary>
    /// Creates a stable random seed.
    /// </summary>
    private static int StableSeed(int seed, string target)
    {
        unchecked
        {
            int hash = 17;
            foreach (char character in target.ToUpperInvariant())
            {
                hash = (hash * 31) + character;
            }

            return seed ^ hash;
        }
    }

    /// <summary>
    /// Enumerates included cards.
    /// </summary>
    private static IEnumerable<DeckCard> IncludedCards(DeckWorkspace workspace)
    {
        return DeckCategoryInclusion.IncludedCards(workspace);
    }
}
