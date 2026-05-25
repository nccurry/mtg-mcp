namespace MtgMcp.Core;

/// <summary>
/// Contains aggregate castability and stranded-card metrics.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
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

}
