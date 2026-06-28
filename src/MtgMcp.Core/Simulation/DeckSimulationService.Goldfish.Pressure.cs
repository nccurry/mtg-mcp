namespace MtgMcp.Core;

/// <summary>
/// Contains board-pressure, route-evidence, and combat helper heuristics for goldfish runs.
/// </summary>
public sealed partial class DeckSimulationService
{
    /// <summary>
    /// Gets a human-readable command-zone role label for representative lines.
    /// </summary>
    private static string CommandZoneLabel(CommandZoneCardPlan card)
    {
        return card.Kind == CommandZoneCardKind.Background ? "background" : "commander";
    }

    /// <summary>
    /// Creates low-confidence evidence for a fallback heuristic win.
    /// </summary>
    private static SimulationRouteEvidence FallbackRouteEvidence(
        string name,
        string kind,
        string source,
        int earliestTurn,
        params string[] evidence)
    {
        List<string> evidenceLines = [];
        foreach (string line in evidence)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                evidenceLines.Add(line);
            }
        }

        return new SimulationRouteEvidence
        {
            Name = name,
            Kind = kind,
            Source = source,
            Matched = true,
            EarliestTurn = earliestTurn,
            Confidence = 0.35,
            Evidence = evidenceLines,
        };
    }

    /// <summary>
    /// Builds human-readable combat or finisher evidence without listing incidental utility creatures as closers.
    /// </summary>
    private static string[] BuildFallbackPressureEvidence(
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int power,
        int winPressure,
        int threshold,
        string route)
    {
        List<string> evidence =
        [
            $"battlefield pressure {power} met fallback {route} threshold {threshold}",
            $"token count {tokens}",
        ];
        if (winPressure > 0)
        {
            evidence.Add($"finisher pressure score {winPressure}");
        }

        AddNamedCardEvidence(evidence, "closers", battlefield.Where(IsFinisherRouteCard));
        AddNamedCardEvidence(evidence, "trample or evasion sources", battlefield.Where(IsEvasionRouteCard));
        AddNamedCardEvidence(evidence, "pump or overrun sources", battlefield.Where(IsPumpRouteCard));
        evidence.Add($"lethal pressure threshold used by this heuristic: {threshold}");
        return evidence.ToArray();
    }

    /// <summary>
    /// Adds a labeled card-name evidence row when matching cards exist.
    /// </summary>
    private static void AddNamedCardEvidence(List<string> evidence, string label, IEnumerable<DeckCard> cards)
    {
        List<string> names = cards
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (names.Count > 0)
        {
            evidence.Add($"{label}: {string.Join(", ", names)}");
        }
    }

    /// <summary>
    /// Checks whether a card should be named as a likely closer for fallback win evidence.
    /// </summary>
    private static bool IsFinisherRouteCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        return role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a card supplies combat evasion or trample-like reach.
    /// </summary>
    private static bool IsEvasionRouteCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.Evasion, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(text, "trample", "flying", "menace", "can't be blocked", "unblockable");
    }

    /// <summary>
    /// Checks whether a card looks like an anthem, pump, or overrun effect.
    /// </summary>
    private static bool IsPumpRouteCard(DeckCard card)
    {
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return ContainsAny(
            text,
            "creatures you control get",
            "gets +",
            "get +",
            "+1/+1",
            "+2/+2",
            "double strike",
            "until end of turn and gains trample",
            "gain trample",
            "gains trample");
    }

    /// <summary>
    /// Checks whether a card is specific enough to represent a combat route.
    /// </summary>
    private static bool IsCombatRouteCard(DeckCard card)
    {
        return IsFinisherRouteCard(card)
            || IsEvasionRouteCard(card)
            || IsPumpRouteCard(card)
            || (ContainsAny(DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "", "Creature")
                && GoldfishManaValue(card) >= 5);
    }

    /// <summary>
    /// Calculates a simple cast priority.
    /// </summary>
    private static int CastPriority(DeckCard card, int turn, SimulationProfile profile)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        return CastPriorityFromRole(role, turn, profile);
    }

    /// <summary>
    /// Calculates a simple cast priority from a cached role assignment.
    /// </summary>
    private static int CastPriorityFromRole(CardRoleAssignment role, int turn, SimulationProfile profile)
    {
        if (turn <= 3 && role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            return profile.Sequencing.EarlyRampPriority;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Draw, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Engines))
        {
            return profile.Sequencing.DrawPriority;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase))
        {
            return profile.Sequencing.TutorPriority;
        }

        if (role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler))
        {
            return profile.Sequencing.ComboPriority;
        }

        if (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase) || role.Tags.Contains(DeckTags.Finishers))
        {
            return profile.Sequencing.WinconPriority;
        }

        return profile.Sequencing.DefaultPriority;
    }

    /// <summary>
    /// Checks whether a card stays on the battlefield.
    /// </summary>
    private static bool IsPermanent(DeckCard card)
    {
        string typeLine = DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "";
        return ContainsAny(typeLine, "Creature", "Artifact", "Enchantment", "Planeswalker", "Battle", "Land");
    }

    /// <summary>
    /// Checks whether a card is a creature spell for commander-specific mana rules.
    /// </summary>
    private static bool IsCreatureSpell(DeckCard card)
    {
        return ContainsAny(DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "", "Creature");
    }

    /// <summary>
    /// Checks whether the card is Sam, Loyal Attendant.
    /// </summary>
    private static bool IsSamLoyalAttendant(DeckCard card)
    {
        return card.Name.Equals("Sam, Loyal Attendant", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Counts battlefield mana sources.
    /// </summary>
    private static int CountManaSources(IReadOnlyList<DeckCard> battlefield)
    {
        return battlefield.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Adds newly available Inga-granted creature mana to the restricted pool for the current turn.
    /// </summary>
    private static void RefreshIngaGrantedCreatureMana(
        IReadOnlyList<DeckCard> battlefield,
        bool commanderOnline,
        CommanderSpecificSimulationRules commanderRules,
        ref bool initialized,
        ref int availableMana,
        ref int restrictedCreatureMana)
    {
        if (initialized || !commanderOnline || !commanderRules.HasIngaAndEsika)
        {
            return;
        }

        int detectedCreatureMana = CountIngaGrantedCreatureManaSources(battlefield);
        availableMana += detectedCreatureMana;
        restrictedCreatureMana += detectedCreatureMana;
        initialized = true;
    }

    /// <summary>
    /// Counts creature permanents that become creature-spell-only mana sources from Inga and Esika.
    /// </summary>
    private static int CountIngaGrantedCreatureManaSources(IReadOnlyList<DeckCard> battlefield)
    {
        int count = 0;
        foreach (DeckCard card in battlefield)
        {
            if (!IsCreatureSpell(card))
            {
                continue;
            }

            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            if (role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Estimates battlefield power.
    /// </summary>
    private static int EstimateBattlefieldPower(IReadOnlyList<DeckCard> battlefield, int tokens)
    {
        int permanentPower = 0;
        foreach (DeckCard card in battlefield)
        {
            if (!ContainsAny(DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "", "Creature"))
            {
                continue;
            }

            permanentPower += Math.Max(1, (int)Math.Ceiling(DeckServiceHelpers.GetSnapshot(card).ManaValue ?? 2));
            if (IsEvasionRouteCard(card))
            {
                permanentPower += 1;
            }
        }

        int finisherBoost = battlefield.Count(card => DeckRoleClassifier.Classify(card).Tags.Contains(DeckTags.Finishers)) * 4;
        int pumpBoost = battlefield.Where(IsPumpRouteCard).Sum(EstimatePumpPressure);
        int drainBoost = EstimateDrainPressure(battlefield, tokens);
        int commanderBoost = battlefield.Any(IsCommanderCard) ? 3 : 0;
        return permanentPower + tokens + finisherBoost + pumpBoost + drainBoost + commanderBoost;
    }

    /// <summary>
    /// Estimates a 0-100 pressure score from board power and route-specific reach.
    /// </summary>
    private static int EstimateThreatPressure(
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens,
        int lifeGainAvailable,
        int power,
        int winPressure,
        bool commanderOnline)
    {
        int evasion = battlefield.Count(IsEvasionRouteCard) * 4;
        int pump = battlefield.Where(IsPumpRouteCard).Sum(EstimatePumpPressure) * 3;
        int drain = EstimateDrainPressure(battlefield, tokens) * 4;
        int foodDrain = EstimateFoodLifegainPressure(battlefield, artifactTokens, foodTokens, lifeGainAvailable);
        int commander = EstimateCommanderPressure(battlefield, commanderOnline);
        return Math.Clamp(power * 2 + winPressure * 5 + evasion + pump + drain + foodDrain + commander, 0, 100);
    }

    /// <summary>
    /// Estimates lifegain that can still be converted from banked Food this turn.
    /// </summary>
    private static int EstimateLifeGainAvailable(
        int foodTokens,
        int lifeGainEvents,
        int availableMana,
        bool samLoyalAttendantOnline)
    {
        int activationCost = samLoyalAttendantOnline ? 1 : 2;
        int spendableFood = activationCost <= 0 ? foodTokens : Math.Min(foodTokens, Math.Max(0, availableMana) / activationCost);
        return Math.Clamp(lifeGainEvents + (spendableFood * 3), 0, 30);
    }

    /// <summary>
    /// Estimates drain pressure from banked Food, lifegain, and artifact-token death payoffs.
    /// </summary>
    private static int EstimateFoodLifegainPressure(
        IReadOnlyList<DeckCard> battlefield,
        int artifactTokens,
        int foodTokens,
        int lifeGainAvailable)
    {
        if (foodTokens == 0 && artifactTokens == 0 && lifeGainAvailable == 0)
        {
            return 0;
        }

        int pressure = 0;
        if (lifeGainAvailable >= 3 && battlefield.Any(IsLifegainDrainPayoff))
        {
            pressure += Math.Min(18, lifeGainAvailable * 2);
        }

        if ((foodTokens > 0 || artifactTokens > 0) && battlefield.Any(IsArtifactLeavesDrainPayoff))
        {
            pressure += Math.Min(18, Math.Max(foodTokens, artifactTokens) * 4);
        }

        if (foodTokens >= 3 && battlefield.Any(IsFoodCombatPayoff))
        {
            pressure += Math.Min(12, foodTokens * 2);
        }

        return pressure;
    }

    /// <summary>
    /// Separates commander presence pressure from actual commander-damage support.
    /// </summary>
    private static int EstimateCommanderPressure(IReadOnlyList<DeckCard> battlefield, bool commanderOnline)
    {
        if (!commanderOnline)
        {
            return 0;
        }

        int support = battlefield.Count(card => !IsCommanderCard(card) && IsCommanderDamageSupport(card));
        return Math.Clamp(3 + (support * 5), 0, 18);
    }

    /// <summary>
    /// Identifies pump, evasion, or Voltron text that can turn commander presence into pressure.
    /// </summary>
    private static bool IsCommanderDamageSupport(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.Voltron, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Evasion, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(
                text,
                "equipped creature gets",
                "enchanted creature gets",
                "commander creatures you own have",
                "double strike",
                "can't be blocked",
                "unblockable",
                "trample");
    }

    /// <summary>
    /// Builds deterministic activated commander engine pressure from cached card text.
    /// </summary>
    private static ActivatedCommanderEnginePressure BuildActivatedCommanderEnginePressure(
        DeckWorkspace workspace,
        IReadOnlyList<DeckCard> battlefield,
        IReadOnlyList<DeckCard> hand,
        int availableMana,
        bool commanderOnline)
    {
        DeckCard? commander = battlefield.FirstOrDefault(IsActivatedLibraryCheatCommander);
        double highCmcHitDensity = HighCmcCreatureHitDensity(workspace);
        bool topdeckSetup = battlefield.Concat(hand).Any(IsTopdeckSetupCard);
        bool libraryRevealCheat = commander is not null;
        int activationCost = commander is null ? int.MaxValue : EstimateActivationCost(DeckServiceHelpers.GetSnapshot(commander).OracleText ?? "");
        bool activationManaAvailable = commanderOnline && libraryRevealCheat && availableMana >= activationCost;
        bool repeatableActivation = commander is not null
            && !ContainsAny(DeckServiceHelpers.GetSnapshot(commander).OracleText ?? "", "sacrifice", "exile this", "activate only once");
        int pressure = 0;
        if (commanderOnline && libraryRevealCheat)
        {
            pressure += 25;
        }

        if (activationManaAvailable)
        {
            pressure += 25;
        }

        if (topdeckSetup)
        {
            pressure += 15;
        }

        if (repeatableActivation)
        {
            pressure += 15;
        }

        pressure += Math.Clamp((int)Math.Round(highCmcHitDensity * 20), 0, 20);

        ActivatedCommanderEnginePressure result = new()
        {
            CommanderOnline = commanderOnline && commander is not null,
            ActivationManaAvailable = activationManaAvailable,
            TopdeckSetup = topdeckSetup,
            LibraryRevealCheat = libraryRevealCheat,
            HighCmcHitDensity = Math.Round(highCmcHitDensity, 3),
            RepeatableActivation = repeatableActivation,
            Pressure = Math.Clamp(pressure, 0, 100)
        };
        if (commander is not null)
        {
            result.Evidence.Add($"{commander.Name} has activated library/topdeck cheat text in cached snapshot.");
        }

        if (activationManaAvailable)
        {
            result.Evidence.Add($"Available mana {availableMana} met estimated activation cost {activationCost}.");
        }

        if (topdeckSetup)
        {
            result.Evidence.Add("Cached battlefield or hand text contains deterministic topdeck setup language.");
        }

        if (highCmcHitDensity > 0)
        {
            result.Evidence.Add($"High-CMC creature hit density is {highCmcHitDensity:0.###}.");
        }

        return result;
    }

    /// <summary>
    /// Builds deterministic sorcery finisher pressure from cached card text.
    /// </summary>
    private static SorceryFinisherPressure BuildSorceryFinisherPressure(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<DeckCard> castThisTurn,
        IReadOnlyList<DeckCard> battlefield,
        int tokens,
        int artifactTokens,
        int foodTokens,
        int availableMana,
        bool commanderOnline,
        int boardPower)
    {
        DeckCard? heldFinisher = hand.FirstOrDefault(IsSorceryFinisherCard);
        DeckCard? castFinisher = castThisTurn.LastOrDefault(IsSorceryFinisherCard);
        DeckCard? finisher = heldFinisher ?? castFinisher;
        bool held = finisher is not null;
        GoldfishCastCost? heldCost = heldFinisher is null
            ? null
            : EstimateGoldfishCastCost(
                heldFinisher,
                battlefield,
                tokens,
                artifactTokens,
                foodTokens,
                availableMana,
                commanderOnline);
        bool castable = castFinisher is not null
            || (heldCost is not null && heldCost.TotalManaSpent <= availableMana);
        int projectedDamage = castable
            ? EstimateProjectedFinisherDamage(finisher!, boardPower)
            : boardPower;
        int pressure = castable && boardPower >= 6
            ? Math.Clamp(projectedDamage * 3, 0, 100)
            : 0;
        SorceryFinisherPressure result = new()
        {
            SorceryFinisherHeld = held,
            CastableFinisher = castable,
            BoardPowerBeforeFinisher = boardPower,
            ProjectedDamage = Math.Clamp(projectedDamage, 0, 200),
            Pressure = pressure
        };
        if (heldFinisher is not null)
        {
            result.Evidence.Add($"{heldFinisher.Name} matched deterministic sorcery finisher text in hand.");
        }

        if (castFinisher is not null)
        {
            result.Evidence.Add($"{castFinisher.Name} was cast this turn and matched deterministic sorcery finisher text.");
        }

        if (castable)
        {
            string costText = heldCost is null
                ? "the finisher was already cast"
                : $"effective cost {heldCost.TotalManaSpent}";
            result.Evidence.Add($"Available mana {availableMana} can support {costText}.");
        }

        if (pressure > 0)
        {
            result.Evidence.Add($"Projected damage pressure {projectedDamage} from board power {boardPower}.");
        }

        return result;
    }

    /// <summary>
    /// Identifies commanders with activated library/topdeck cheat text.
    /// </summary>
    private static bool IsActivatedLibraryCheatCommander(DeckCard card)
    {
        if (!IsCommanderCard(card))
        {
            return false;
        }

        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return text.Contains(':', StringComparison.Ordinal)
            && ContainsAny(text, "top", "library", "reveal")
            && ContainsAny(text, "put", "battlefield", "cast");
    }

    /// <summary>
    /// Identifies deterministic topdeck setup text in cached snapshots.
    /// </summary>
    private static bool IsTopdeckSetupCard(DeckCard card)
    {
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return ContainsAny(text, "scry", "surveil", "look at the top", "rearrange", "put on top", "put that card on top");
    }

    /// <summary>
    /// Estimates high-CMC creature density among included non-commander cards.
    /// </summary>
    private static double HighCmcCreatureHitDensity(DeckWorkspace workspace)
    {
        int creatures = 0;
        int highCmcCreatures = 0;
        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace).Where(card => !IsCommanderCard(card)))
        {
            if (!ContainsAny(DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "", "Creature"))
            {
                continue;
            }

            creatures += Math.Max(0, card.Quantity);
            if ((DeckServiceHelpers.GetSnapshot(card).ManaValue ?? 0) >= 5)
            {
                highCmcCreatures += Math.Max(0, card.Quantity);
            }
        }

        return creatures == 0 ? 0 : highCmcCreatures / (double)creatures;
    }

    /// <summary>
    /// Estimates the first activated ability mana cost from mana symbols before a colon.
    /// </summary>
    private static int EstimateActivationCost(string text)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return 0;
        }

        string costText = text[..colon];
        int cost = 0;
        for (int index = 0; index < costText.Length; index++)
        {
            if (costText[index] != '{')
            {
                continue;
            }

            int close = costText.IndexOf('}', index + 1);
            if (close < 0)
            {
                break;
            }

            string symbol = costText[(index + 1)..close];
            if (int.TryParse(symbol, out int generic))
            {
                cost += generic;
            }
            else if (!symbol.Equals("T", StringComparison.OrdinalIgnoreCase)
                && !symbol.Equals("Q", StringComparison.OrdinalIgnoreCase))
            {
                cost += 1;
            }

            index = close;
        }

        return Math.Max(0, cost);
    }

    /// <summary>
    /// Identifies sorceries that convert a board into immediate combat or draw pressure.
    /// </summary>
    private static bool IsSorceryFinisherCard(DeckCard card)
    {
        string typeLine = DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "";
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return typeLine.Contains("Sorcery", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(text, "creatures you control", "target creatures", "additional combat", "extra combat", "draw cards equal to")
            && ContainsAny(text, "+x/+x", "+1/+1", "+2/+2", "+3/+3", "trample", "additional combat", "extra combat", "greatest power", "power among creatures");
    }

    /// <summary>
    /// Estimates bounded damage pressure after resolving a sorcery finisher.
    /// </summary>
    private static int EstimateProjectedFinisherDamage(DeckCard finisher, int boardPower)
    {
        string text = DeckServiceHelpers.GetSnapshot(finisher).OracleText ?? "";
        int damage = boardPower;
        if (ContainsAny(text, "+x/+x", "greatest power", "power among creatures"))
        {
            damage += boardPower;
        }
        else if (ContainsAny(text, "+3/+3"))
        {
            damage += 9;
        }
        else if (ContainsAny(text, "+2/+2"))
        {
            damage += 6;
        }
        else if (ContainsAny(text, "+1/+1"))
        {
            damage += 3;
        }

        if (ContainsAny(text, "additional combat", "extra combat"))
        {
            damage *= 2;
        }

        if (ContainsAny(text, "trample"))
        {
            damage += Math.Max(2, boardPower / 3);
        }

        return damage;
    }

    /// <summary>
    /// Estimates how much a pump, equipment, aura, or anthem permanent increases pressure.
    /// </summary>
    private static int EstimatePumpPressure(DeckCard card)
    {
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        string typeLine = DeckServiceHelpers.GetSnapshot(card).TypeLine ?? "";
        int pressure = 0;
        if (ContainsAny(typeLine, "Equipment", "Aura"))
        {
            pressure += 2;
        }

        if (ContainsAny(text, "+1/+1"))
        {
            pressure += 2;
        }

        if (ContainsAny(text, "+2/+2"))
        {
            pressure += 4;
        }

        if (ContainsAny(text, "+3/+3"))
        {
            pressure += 6;
        }

        if (ContainsAny(text, "double strike"))
        {
            pressure += 4;
        }

        if (ContainsAny(text, "trample", "flying", "menace", "can't be blocked", "unblockable"))
        {
            pressure += 2;
        }

        return Math.Max(1, pressure);
    }

    /// <summary>
    /// Estimates recurring life-loss pressure from aristocrats and drain boards.
    /// </summary>
    private static int EstimateDrainPressure(IReadOnlyList<DeckCard> battlefield, int tokens)
    {
        int drainPayoffs = battlefield.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Aristocrats, StringComparer.OrdinalIgnoreCase);
        });
        if (drainPayoffs == 0)
        {
            return 0;
        }

        int sacrificeSupport = battlefield.Count(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            return role.Tags.Contains(DeckTags.SacOutlet, StringComparer.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.SacrificeFodder, StringComparer.OrdinalIgnoreCase);
        });
        return drainPayoffs * Math.Max(1, Math.Min(4, tokens + sacrificeSupport));
    }

    /// <summary>
    /// Identifies payoffs that turn lifegain into opponent life loss or win pressure.
    /// </summary>
    private static bool IsLifegainDrainPayoff(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
            || (ContainsAny(text, "whenever you gain life", "whenever you gained life")
                && ContainsAny(text, "each opponent loses", "opponent loses", "loses that much life", "you win the game"));
    }

    /// <summary>
    /// Identifies payoffs for sacrificing or losing artifact tokens such as Food.
    /// </summary>
    private static bool IsArtifactLeavesDrainPayoff(DeckCard card)
    {
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return ContainsAny(
                text,
                "whenever an artifact is put into a graveyard",
                "whenever one or more artifacts",
                "whenever you sacrifice an artifact",
                "whenever you sacrifice a food",
                "whenever one or more tokens you control leave")
            && ContainsAny(text, "each opponent loses", "opponent loses", "damage to each opponent", "drain");
    }

    /// <summary>
    /// Identifies combat payoffs that can convert a banked Food/token board into an alpha strike.
    /// </summary>
    private static bool IsFoodCombatPayoff(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
        return role.Tags.Contains(DeckTags.CombatPayoff, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(
                text,
                "creatures you control get +",
                "creatures you control gain trample",
                "creatures you control have trample",
                "creatures you control can't be blocked",
                "for each artifact you control",
                "for each food you control");
    }

    /// <summary>
    /// Checks whether the battlefield has a repeatable engine permanent online.
    /// </summary>
    private static bool HasGoldfishEngineOnline(IReadOnlyList<DeckCard> battlefield)
    {
        return battlefield.Any(card =>
        {
            CardRoleAssignment role = DeckRoleClassifier.Classify(card);
            string text = DeckServiceHelpers.GetSnapshot(card).OracleText ?? "";
            return role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase)
                || (ContainsAny(text, "whenever", "at the beginning")
                    && ContainsAny(text, "draw", "create", "return", "each opponent loses", "opponent loses"));
        });
    }
}
