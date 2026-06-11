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
        IReadOnlyList<PerformanceScheduledManaSource> virtualManaSources,
        IReadOnlySet<string> deckColors,
        int turn,
        PerformanceCardFactsCache cardFacts)
    {
        HashSet<string> existingColors = ExtractColoredSymbols(GetPerformanceManaSources(
            battlefield,
            virtualManaSources,
            unavailablePermanent: null,
            turn,
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
    /// Casts command-zone cards in plan order while mana and target turns allow.
    /// </summary>
    private static void CastPerformanceCommandZoneCards(
        CommandZoneRunState commandZone,
        int turn,
        List<PerformancePermanent> battlefield,
        PerformanceCardFactsCache cardFacts,
        List<PerformanceDecisionEvent>? decisionEvents,
        ref List<PerformanceManaSource> availableSources)
    {
        while (true)
        {
            CommandZoneCardPlan? next = commandZone.NextPending();
            if (next is null)
            {
                return;
            }

            if (turn < next.TargetTurn)
            {
                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "sequencing",
                    turn,
                    "command-zone",
                    "held",
                    next.Card.Name,
                    "profile target turn delayed this command-zone card.",
                    $"target turn: {next.TargetTurn}");
                return;
            }

            if (!TryPay(cardFacts.Get(next.Card), availableSources, out List<PerformanceManaSource> afterSources))
            {
                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "sequencing",
                    turn,
                    "command-zone",
                    "skipped",
                    next.Card.Name,
                    "available mana could not pay the command-zone card this turn.",
                    $"available sources: {availableSources.Count}",
                    $"mana value: {cardFacts.Get(next.Card).ManaValue}");
                return;
            }

            availableSources = afterSources;
            battlefield.Add(new PerformancePermanent { Card = next.Card });
            commandZone.MarkCast(next, turn);
            AddPerformanceDecisionEvent(
                decisionEvents,
                "sequencing",
                turn,
                "command-zone",
                "cast",
                next.Card.Name,
                "profile target turn and available mana allowed command-zone deployment.",
                $"remaining sources: {availableSources.Count}");
        }
    }

    /// <summary>
    /// Casts hand spells for one sequencing window.
    /// </summary>
    private static void CastPerformanceHandSpells(
        List<DeckCard> hand,
        List<DeckCard> library,
        List<PerformancePermanent> battlefield,
        List<DeckCard> graveyard,
        List<PerformanceScheduledManaSource> virtualManaSources,
        PerformanceTurnState state,
        PerformanceCardFactsCache cardFacts,
        IReadOnlySet<string> deckColors,
        int turn,
        SimulationProfile profile,
        PerformanceSpellWindow window,
        bool commanderCast,
        List<PerformanceDecisionEvent>? decisionEvents,
        ref List<PerformanceManaSource> availableSources,
        ref bool rampCastByTurn,
        ref bool drawCastByTurn)
    {
        foreach (DeckCard spell in hand
            .Where(card => !cardFacts.Get(card).IsCommander)
            .Where(card => !cardFacts.Get(card).IsLand)
            .OrderBy(card => PerformanceCastPriority(cardFacts.Get(card), turn, profile))
            .ThenBy(card => cardFacts.Get(card).ManaValue)
            .ToList())
        {
            PerformanceCardFacts facts = cardFacts.Get(spell);
            if (!UsePerformanceSpellInWindow(facts, window))
            {
                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "sequencing",
                    turn,
                    "cast-window",
                    "held",
                    spell.Name,
                    "delayed-command-zone sequencing reserved this spell for a later window.",
                    $"window: {window}");
                continue;
            }

            if (ShouldHoldPerformanceSpell(facts, turn, commanderCast, profile))
            {
                string holdReason = (facts.IsInteraction || facts.IsBoardWipe)
                    ? "profile preserved instant-speed interaction instead of spending it proactively."
                    : "profile preserved protection while the commander plan was online.";
                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "interaction-hold-up",
                    turn,
                    "cast-spell",
                    "held",
                    spell.Name,
                    holdReason,
                    $"available sources: {availableSources.Count}",
                    $"mana value: {facts.ManaValue}");
                continue;
            }

            if (!TryPay(facts, availableSources, out List<PerformanceManaSource> afterSpellSources))
            {
                AddPerformanceDecisionEvent(
                    decisionEvents,
                    "cast-skip",
                    turn,
                    "cast-spell",
                    "skipped",
                    spell.Name,
                    "available mana sources could not satisfy the card cost this turn.",
                    $"available sources: {availableSources.Count}",
                    $"mana value: {facts.ManaValue}");
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

            AddPerformanceDecisionEvent(
                decisionEvents,
                "sequencing",
                turn,
                "cast-spell",
                "cast",
                spell.Name,
                "spell matched the current sequencing window and was payable.",
                $"role: {facts.Role.PrimaryRole}",
                $"priority: {PerformanceCastPriority(facts, turn, profile)}");

            if (facts.IsRamp)
            {
                rampCastByTurn = true;
                state.RampCastByTurn = true;
                PerformanceScheduledManaSource? scheduledRamp = BuildPerformanceRampSource(facts, deckColors, turn);
                if (scheduledRamp is not null)
                {
                    virtualManaSources.Add(scheduledRamp);
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
    }

    /// <summary>
    /// Checks whether a hand spell belongs in the current delayed-command-zone sequencing window.
    /// </summary>
    private static bool UsePerformanceSpellInWindow(
        PerformanceCardFacts facts,
        PerformanceSpellWindow window)
    {
        return window switch
        {
            PerformanceSpellWindow.All => true,
            PerformanceSpellWindow.SetupOnly => IsPerformanceSetupSpell(facts),
            PerformanceSpellWindow.NonSetup => !IsPerformanceSetupSpell(facts),
            _ => true,
        };
    }

    /// <summary>
    /// Checks whether a hand spell should be sequenced before delayed command-zone deployment.
    /// </summary>
    private static bool IsPerformanceSetupSpell(PerformanceCardFacts facts)
    {
        return facts.IsRamp
            || facts.IsDraw
            || facts.IsTutor
            || facts.HasTag(DeckTags.Engines)
            || facts.HasComboPieceOrEnabler;
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
        IReadOnlyList<PerformanceScheduledManaSource> virtualManaSources,
        PerformancePermanent? unavailablePermanent,
        int turn,
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

        foreach (PerformanceScheduledManaSource source in virtualManaSources)
        {
            if (source.AvailableTurn <= turn)
            {
                sources.Add(new PerformanceManaSource(source.Symbols));
            }
        }

        return sources;
    }

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
    private static PerformanceScheduledManaSource? BuildPerformanceRampSource(
        PerformanceCardFacts facts,
        IReadOnlySet<string> deckColors,
        int turn)
    {
        RampOperationalFacts? ramp = facts.RampFacts;
        if (ramp is null || ramp.Kind.Equals("unknownShape", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ramp.Kind.Equals("manaRock", StringComparison.OrdinalIgnoreCase)
            || ramp.Kind.Equals("manaDork", StringComparison.OrdinalIgnoreCase)
            || ramp.Kind.Equals("permanentManaSource", StringComparison.OrdinalIgnoreCase)
            || ramp.Kind.Equals("ritual", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        List<string> symbols = RampSourceSymbols(facts, ramp, deckColors);
        if (symbols.Count == 0)
        {
            return null;
        }

        return new PerformanceScheduledManaSource
        {
            Symbols = symbols,
            AvailableTurn = RampAvailableTurn(ramp, turn),
        };
    }

    /// <summary>
    /// Reads the mana symbols created by a shaped ramp effect.
    /// </summary>
    private static List<string> RampSourceSymbols(
        PerformanceCardFacts facts,
        RampOperationalFacts ramp,
        IReadOnlySet<string> deckColors)
    {
        if (ramp.ProducedMana.Count > 0)
        {
            return ramp.ProducedMana.ToList();
        }

        if (facts.ProducedMana.Count > 0)
        {
            return facts.ProducedMana.ToList();
        }

        return deckColors.Count > 0 ? deckColors.ToList() : [];
    }

    /// <summary>
    /// Computes when a shaped persistent ramp effect becomes available after it was cast this turn.
    /// </summary>
    private static int RampAvailableTurn(RampOperationalFacts ramp, int turn)
    {
        int delay = 1;
        if (ramp.Kind.Equals("spellLandRampUntapped", StringComparison.OrdinalIgnoreCase)
            || ramp.Kind.Equals("treasureBurst", StringComparison.OrdinalIgnoreCase))
        {
            delay = 0;
        }

        if (ramp.Kind.Equals("activatedLandRamp", StringComparison.OrdinalIgnoreCase)
            || ramp.Kind.Equals("creatureSacrificeLandRamp", StringComparison.OrdinalIgnoreCase))
        {
            delay = 0;
            if (ramp.ActivationMana > 0 || ramp.RequiresTap)
            {
                delay++;
            }

            if (ramp.EntersTapped == true)
            {
                delay++;
            }
        }

        return Math.Max(1, turn + delay);
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
    /// Lists hand-spell sequencing windows around delayed command-zone deployment.
    /// </summary>
    private enum PerformanceSpellWindow
    {
        /// <summary>
        /// Cast every eligible spell.
        /// </summary>
        All,

        /// <summary>
        /// Cast only setup spells before delayed command-zone deployment.
        /// </summary>
        SetupOnly,

        /// <summary>
        /// Cast only non-setup spells after delayed command-zone deployment.
        /// </summary>
        NonSetup,
    }

}
