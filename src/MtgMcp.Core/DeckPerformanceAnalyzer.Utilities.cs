namespace MtgMcp.Core;

/// <summary>
/// Contains shared card facts, utility helpers, and nested simulation state types.
/// </summary>
internal static partial class DeckPerformanceAnalyzer
{
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
        if (MulliganHeuristics.UsesFreeFirstMulligan(workspace.Format))
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

        if (MulliganHeuristics.UsesCommanderDeckConstruction(workspace.Format) && analysis.DeckSize != 100)
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
