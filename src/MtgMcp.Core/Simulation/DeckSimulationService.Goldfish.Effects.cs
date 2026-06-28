namespace MtgMcp.Core;

/// <summary>
/// Contains spell-effect and sequencing-window helpers for goldfish runs.
/// </summary>
public sealed partial class DeckSimulationService
{
    /// <summary>
    /// Estimates token production while preserving artifact and Food subcounts.
    /// </summary>
    private static GoldfishTokenProduction EstimateTokenProduction(
        DeckCard spell,
        CardRoleAssignment role,
        int xValue)
    {
        string text = DeckServiceHelpers.GetSnapshot(spell).OracleText ?? "";
        int food = EstimateNamedTokenCount(text, "Food");
        int artifact = food;
        artifact += EstimateNamedTokenCount(text, "Treasure");
        artifact += EstimateNamedTokenCount(text, "Clue");
        artifact += EstimateNamedTokenCount(text, "Blood");
        artifact += EstimateNamedTokenCount(text, "Map");
        artifact += EstimateArtifactTokenCount(text);

        int total = Math.Max(food, artifact);
        if (role.Tags.Contains(DeckTags.Tokens, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.SacrificeFodder, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.ArtifactTokens, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Food, StringComparer.OrdinalIgnoreCase))
        {
            total = Math.Max(total, 2 + EstimateTokenScaling(spell, xValue));
        }

        if (xValue > 0 && ContainsAny(text, "create X"))
        {
            total = Math.Max(total, Math.Min(8, xValue));
            if (ContainsAny(text, "artifact token", "artifact tokens", "Food", "Treasure", "Clue", "Blood", "Map"))
            {
                artifact = Math.Max(artifact, Math.Min(8, xValue));
            }
        }

        return new GoldfishTokenProduction(
            Total: Math.Clamp(total, 0, 12),
            ArtifactTokens: Math.Clamp(artifact, 0, 12),
            FoodTokens: Math.Clamp(food, 0, 12));
    }

    /// <summary>
    /// Estimates explicit named-token counts from common English number words.
    /// </summary>
    private static int EstimateNamedTokenCount(string text, string tokenName)
    {
        if (!ContainsAny(text, tokenName))
        {
            return 0;
        }

        string singular = $"{tokenName} token";
        string plural = $"{tokenName} tokens";
        if (ContainsAny(text, $"three {singular}", $"three {plural}"))
        {
            return 3;
        }

        if (ContainsAny(text, $"two {singular}", $"two {plural}"))
        {
            return 2;
        }

        if (ContainsAny(text, $"a {singular}", $"one {singular}", singular, plural))
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Estimates artifact-token counts when text names artifact tokens generically.
    /// </summary>
    private static int EstimateArtifactTokenCount(string text)
    {
        if (ContainsAny(text, "three artifact tokens"))
        {
            return 3;
        }

        if (ContainsAny(text, "two artifact tokens"))
        {
            return 2;
        }

        return ContainsAny(text, "an artifact token", "a artifact token", "artifact tokens") ? 1 : 0;
    }

    /// <summary>
    /// Estimates lifegain that was explicitly created by the resolved spell.
    /// </summary>
    private static int EstimateImmediateLifeGain(DeckCard spell, GoldfishTokenProduction tokenProduction)
    {
        string text = DeckServiceHelpers.GetSnapshot(spell).OracleText ?? "";
        int life = 0;
        if (ContainsAny(text, "gain 3 life", "gain three life"))
        {
            life += 3;
        }
        else if (ContainsAny(text, "gain 2 life", "gain two life"))
        {
            life += 2;
        }
        else if (ContainsAny(text, "gain 1 life", "gain one life", "gain life"))
        {
            life += 1;
        }

        if (tokenProduction.FoodTokens > 0 && ContainsAny(text, "you gain life", "gain 3 life"))
        {
            life += tokenProduction.FoodTokens;
        }

        return Math.Clamp(life, 0, 12);
    }

    /// <summary>
    /// Estimates extra tokens produced by a cast X or token-scaling spell.
    /// </summary>
    private static int EstimateTokenScaling(DeckCard spell, int xValue)
    {
        string text = DeckServiceHelpers.GetSnapshot(spell).OracleText ?? "";
        if (xValue > 0 && ContainsAny(text, "create X"))
        {
            return Math.Min(8, xValue);
        }

        if (ContainsAny(text, "for each creature you control", "for each token you control"))
        {
            return 2;
        }

        return 0;
    }

    /// <summary>
    /// Estimates extra win pressure supplied by a resolved X payoff.
    /// </summary>
    private static int EstimateXSpellPressure(DeckCard spell, int xValue)
    {
        if (xValue <= 0)
        {
            return 0;
        }

        string text = DeckServiceHelpers.GetSnapshot(spell).OracleText ?? "";
        return ContainsAny(text, "deals X", "lose X life", "get +X/+X", "gets +X/+X")
            ? Math.Min(8, Math.Max(2, xValue / 2))
            : 0;
    }

    /// <summary>
    /// Checks whether an opening hand plausibly casts the commander by the profile target turn.
    /// </summary>
    private static bool HasGoldfishCommanderPlan(
        IReadOnlyList<DeckCard> hand,
        CommandZonePlan commandZonePlan,
        SimulationProfile profile)
    {
        DeckCard? commander = commandZonePlan.PrimaryCommander;
        if (commander is null)
        {
            return false;
        }

        int lands = CountGoldfishRole(hand, DeckRoles.Lands);
        int ramp = CountCheapGoldfishRole(hand, DeckRoles.Ramp, 2);
        int targetTurn = Math.Max(1, profile.Sequencing.PreferredCommanderTurn ?? profile.Scenarios.CommanderTurn);
        int expectedLandDrops = lands >= 2 ? Math.Min(targetTurn, lands + 1) : lands;
        int expectedMana = expectedLandDrops + Math.Min(ramp, 2);
        return expectedMana >= GoldfishManaValue(commander);
    }

    /// <summary>
    /// Counts interaction or protection that could be held with available mana.
    /// </summary>
    private static int CountHeldGoldfishInteraction(IEnumerable<DeckCard> hand, int availableMana)
    {
        return hand.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return GoldfishManaValue(card) <= availableMana
                && IsGoldfishInteraction(role);
        });
    }

    /// <summary>
    /// Casts command-zone cards in plan order while mana and target turns allow.
    /// </summary>
    private static void CastGoldfishCommandZoneCards(
        CommandZoneRunState commandZone,
        int turn,
        List<DeckCard> battlefield,
        GoldfishRun run,
        int tokens,
        int artifactTokens,
        ref int availableMana)
    {
        while (true)
        {
            CommandZoneCardPlan? next = commandZone.NextPending();
            if (next is null || turn < next.TargetTurn)
            {
                return;
            }

            int cost = EstimateGoldfishCastCost(
                next.Card,
                battlefield,
                tokens,
                artifactTokens,
                foodTokens: 0,
                availableMana,
                commanderOnline: commandZone.CommanderOnline).TotalManaSpent;
            if (cost > availableMana)
            {
                return;
            }

            availableMana -= cost;
            battlefield.Add(next.Card);
            commandZone.MarkCast(next, turn);
            run.Line.Add($"T{turn}: cast {CommandZoneLabel(next)} {next.Card.Name}.");
        }
    }

    /// <summary>
    /// Casts hand spells for one sequencing window.
    /// </summary>
    private static void CastGoldfishHandSpells(
        List<DeckCard> hand,
        List<DeckCard> deck,
        List<DeckCard> battlefield,
        List<DeckCard> graveyard,
        List<DeckCard> castThisTurn,
        GoldfishRun run,
        int turn,
        SimulationProfile profile,
        GoldfishSpellWindow window,
        bool commanderOnline,
        CommanderSpecificSimulationRules commanderRules,
        ref int restrictedCreatureMana,
        ref int availableMana,
        ref int tokens,
        ref int artifactTokens,
        ref int foodTokens,
        ref int lifeGainEvents,
        ref int winPressure,
        ref int dungeonProgress)
    {
        int orderingTokens = tokens;
        int orderingArtifactTokens = artifactTokens;
        int orderingFoodTokens = foodTokens;
        int orderingMana = availableMana;
        foreach (DeckCard spell in hand
            .OrderBy(card => CastPriority(card, turn, profile))
            .ThenBy(card => EstimateGoldfishCastCost(
                card,
                battlefield,
                orderingTokens,
                orderingArtifactTokens,
                orderingFoodTokens,
                orderingMana,
                commanderOnline).TotalManaSpent)
            .ToList())
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(spell);
            if (role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                || IsCommanderCard(spell)
                || !UseGoldfishSpellInWindow(role, window))
            {
                continue;
            }

            GoldfishCastCost castCost = EstimateGoldfishCastCost(
                spell,
                battlefield,
                tokens,
                artifactTokens,
                foodTokens,
                availableMana,
                commanderOnline);
            int cost = castCost.TotalManaSpent;
            if (ShouldHoldGoldfishInteraction(spell, role, hand, availableMana, turn, profile))
            {
                continue;
            }

            bool creatureSpell = IsCreatureSpell(spell);
            int generalMana = Math.Max(0, availableMana - restrictedCreatureMana);
            if (!creatureSpell && cost > generalMana)
            {
                continue;
            }

            if (cost > availableMana)
            {
                continue;
            }

            int creatureManaSpent = 0;
            if (creatureSpell && restrictedCreatureMana > 0)
            {
                creatureManaSpent = Math.Min(cost, restrictedCreatureMana);
                restrictedCreatureMana -= creatureManaSpent;
            }

            availableMana -= cost;
            hand.Remove(spell);
            castThisTurn.Add(spell);
            if (IsPermanent(spell))
            {
                battlefield.Add(spell);
                run.Line.Add($"T{turn}: cast {spell.Name} ({role.PrimaryRole}).");
            }
            else
            {
                graveyard.Add(spell);
                run.Line.Add($"T{turn}: used {spell.Name} ({role.PrimaryRole}).");
            }

            ApplyGoldfishGraveyardSetup(spell, deck, graveyard, run, turn);

            GoldfishTokenProduction tokenProduction = EstimateTokenProduction(spell, role, castCost.XValue);
            if (tokenProduction.Total > 0)
            {
                tokens += tokenProduction.Total;
                artifactTokens += tokenProduction.ArtifactTokens;
                foodTokens += tokenProduction.FoodTokens;
            }

            lifeGainEvents += EstimateImmediateLifeGain(spell, tokenProduction);

            if (ContainsAny(DeckServiceHelpers.GetSnapshot(spell).OracleText ?? "", "venture into the dungeon", "take the initiative"))
            {
                dungeonProgress++;
            }

            if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase) && deck.Count > 0)
            {
                hand.Add(deck[0]);
                deck.RemoveAt(0);
            }

            if (commanderOnline
                && commanderRules.HasIngaAndEsika
                && creatureSpell
                && creatureManaSpent >= 3
                && deck.Count > 0)
            {
                hand.Add(deck[0]);
                deck.RemoveAt(0);
                run.Line.Add($"T{turn}: drew a card from Inga and Esika after spending {creatureManaSpent} creature mana.");
            }

            if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers))
            {
                winPressure += 4 + EstimateXSpellPressure(spell, castCost.XValue);
            }
        }
    }

    /// <summary>
    /// Keeps the configured minimum amount of interaction available instead of spending it proactively.
    /// </summary>
    private static bool ShouldHoldGoldfishInteraction(
        DeckCard spell,
        CardRoleAssignment role,
        IReadOnlyList<DeckCard> hand,
        int availableMana,
        int turn,
        SimulationProfile profile)
    {
        if (turn < profile.Sequencing.HoldInteractionFromTurn
            || profile.Sequencing.MinimumInteractionHeld <= 0
            || !IsGoldfishInteraction(role))
        {
            return false;
        }

        return GoldfishManaValue(spell) <= availableMana
            && CountHeldGoldfishInteraction(hand, availableMana) <= profile.Sequencing.MinimumInteractionHeld;
    }

    /// <summary>
    /// Checks whether a role assignment represents instant-speed or protective interaction for goldfish holding.
    /// </summary>
    private static bool IsGoldfishInteraction(CardRoleAssignment role)
    {
        return role.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Models simple self-mill or Entomb-style setup for graveyard route predicates.
    /// </summary>
    private static void ApplyGoldfishGraveyardSetup(
        DeckCard spell,
        List<DeckCard> deck,
        List<DeckCard> graveyard,
        GoldfishRun run,
        int turn)
    {
        if (deck.Count == 0 || !SetsUpGoldfishGraveyard(spell))
        {
            return;
        }

        DeckCard target = ChooseGoldfishGraveyardTarget(deck);
        deck.Remove(target);
        graveyard.Add(target);
        run.Line.Add($"T{turn}: put {target.Name} into the graveyard for graveyard setup.");
    }

    /// <summary>
    /// Checks whether a cast spell plausibly fills the graveyard in goldfish.
    /// </summary>
    private static bool SetsUpGoldfishGraveyard(DeckCard spell)
    {
        string text = DeckServiceHelpers.GetSnapshot(spell).OracleText ?? "";
        return ContainsAny(
            text,
            "put it into your graveyard",
            "put that card into your graveyard",
            "put a card from your library into your graveyard",
            "mill",
            "surveil");
    }

    /// <summary>
    /// Chooses the most plausible graveyard target from the remaining library.
    /// </summary>
    private static DeckCard ChooseGoldfishGraveyardTarget(List<DeckCard> deck)
    {
        DeckCard? target = deck
            .Where(IsGoldfishReanimationTarget)
            .OrderByDescending(GoldfishManaValue)
            .ThenBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return target ?? deck[0];
    }

    /// <summary>
    /// Checks whether a card is a meaningful reanimation target.
    /// </summary>
    private static bool IsGoldfishReanimationTarget(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string typeLine = DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "";
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        bool meaningfulCreature = typeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase)
            && (GoldfishManaValue(card) >= 4
                || role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase));
        bool meaningfulEnchantment = typeLine.Contains("Enchantment", StringComparison.OrdinalIgnoreCase)
            && (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Payoffs, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Synergy, StringComparison.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
                || ContainsAny(text, "whenever", "at the beginning", "each opponent loses", "you win"));
        return meaningfulCreature || meaningfulEnchantment;
    }

    /// <summary>
    /// Checks whether a hand spell belongs in the current delayed-command-zone sequencing window.
    /// </summary>
    private static bool UseGoldfishSpellInWindow(
        CardRoleAssignment role,
        GoldfishSpellWindow window)
    {
        return window switch
        {
            GoldfishSpellWindow.All => true,
            GoldfishSpellWindow.SetupOnly => IsGoldfishSetupSpell(role),
            GoldfishSpellWindow.NonSetup => !IsGoldfishSetupSpell(role),
            _ => true,
        };
    }

    /// <summary>
    /// Checks whether a hand spell should be sequenced before delayed command-zone deployment.
    /// </summary>
    private static bool IsGoldfishSetupSpell(CardRoleAssignment role)
    {
        return role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Engines)
            || role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler);
    }
}
