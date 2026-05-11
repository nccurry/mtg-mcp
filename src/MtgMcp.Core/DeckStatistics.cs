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
            Simulations = safeSimulations
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

        Random random = new(StableSeed(seed, target));
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
