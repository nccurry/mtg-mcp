namespace MtgMcp.Core;

/// <summary>
/// Contains land, spell, and mana-payment sequencing helpers.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
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

}
