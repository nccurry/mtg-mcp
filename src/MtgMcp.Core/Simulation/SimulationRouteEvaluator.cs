namespace MtgMcp.Core;

/// <summary>
/// Evaluates safe deterministic win-route predicates against a simulated state.
/// </summary>
public static class SimulationRouteEvaluator
{
    /// <summary>
    /// Checks whether a route requirement uses the supported bounded predicate vocabulary.
    /// </summary>
    public static bool IsSupportedRequirement(string requirement)
    {
        string text = NormalizeRequirement(requirement);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Equals("commander", StringComparison.OrdinalIgnoreCase)
            || text.Equals("repeatable-blink", StringComparison.OrdinalIgnoreCase)
            || text.Equals("reanimation-target", StringComparison.OrdinalIgnoreCase)
            || text.Equals("sac-outlet", StringComparison.OrdinalIgnoreCase)
            || text.Equals("drain-payoff", StringComparison.OrdinalIgnoreCase)
            || text.Equals("recursive-creature", StringComparison.OrdinalIgnoreCase)
            || text.Equals("enchantment-recursion", StringComparison.OrdinalIgnoreCase)
            || text.Equals("repeatable-graveyard-recursion", StringComparison.OrdinalIgnoreCase)
            || text.Equals("enchantress-engine", StringComparison.OrdinalIgnoreCase)
            || text.Equals("engine-payoff", StringComparison.OrdinalIgnoreCase)
            || text.Equals("drain-clock", StringComparison.OrdinalIgnoreCase)
            || text.Equals("treasure-engine", StringComparison.OrdinalIgnoreCase)
            || text.Equals("treasure-payoff", StringComparison.OrdinalIgnoreCase)
            || text.Equals("commander-damage-pressure", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("card:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("role:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("tag:", StringComparison.OrdinalIgnoreCase)
            || HasNumericPredicate(text, "mana>=")
            || HasNumericPredicate(text, "tokens>=")
            || HasNumericPredicate(text, "graveyard>=")
            || HasNumericPredicate(text, "interactionheld>=")
            || HasNumericPredicate(text, "dungeonprogress>=")
            || HasNumericPredicate(text, "turn>=")
            || (!text.Contains(':', StringComparison.Ordinal)
                && !text.Contains(">=", StringComparison.Ordinal));
    }

    /// <summary>
    /// Evaluates all routes and returns evidence for each route.
    /// </summary>
    public static List<SimulationRouteEvidence> EvaluateRoutes(
        IEnumerable<SimulationRouteDefinition> routes,
        SimulationRouteState state)
    {
        return routes
            .Select(route => EvaluateRoute(route, state))
            .ToList();
    }

    /// <summary>
    /// Evaluates one route and records matched or missing predicates.
    /// </summary>
    private static SimulationRouteEvidence EvaluateRoute(
        SimulationRouteDefinition route,
        SimulationRouteState state)
    {
        SimulationRouteEvidence evidence = new()
        {
            Name = route.Name,
            Kind = route.Kind,
            Source = route.Source,
            EarliestTurn = route.EarliestTurn,
            Confidence = route.Source.Equals("deck-intent", StringComparison.OrdinalIgnoreCase) ? 0.82 : 0.7,
        };

        if (state.Turn < route.EarliestTurn)
        {
            evidence.MissingRequirements.Add($"turn {state.Turn} is before earliest turn {route.EarliestTurn}");
        }

        foreach (string requirement in route.Requirements)
        {
            if (MatchesRequirement(requirement, state, out string message))
            {
                evidence.Evidence.Add(message);
            }
            else
            {
                evidence.MissingRequirements.Add(message);
            }
        }

        evidence.Matched = evidence.MissingRequirements.Count == 0;
        return evidence;
    }

    /// <summary>
    /// Checks one bounded route requirement against a simulated state.
    /// </summary>
    private static bool MatchesRequirement(
        string requirement,
        SimulationRouteState state,
        out string message)
    {
        string text = NormalizeRequirement(requirement);
        if (text.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            message = state.CommanderOnBattlefield
                ? "commander is on the battlefield"
                : "commander is not on the battlefield";
            return state.CommanderOnBattlefield;
        }

        if (text.Equals("repeatable-blink", StringComparison.OrdinalIgnoreCase))
        {
            bool matched = state.Battlefield.Any(IsRepeatableBlinkCard);
            message = matched
                ? "a repeatable blink permanent is on the battlefield"
                : "no repeatable blink permanent is on the battlefield";
            return matched;
        }

        if (text.Equals("reanimation-target", StringComparison.OrdinalIgnoreCase))
        {
            bool matched = state.Graveyard.Any(IsReanimationTarget);
            message = matched
                ? "graveyard has a plausible reanimation target"
                : "graveyard lacks a plausible reanimation target";
            return matched;
        }

        if (text.Equals("sac-outlet", StringComparison.OrdinalIgnoreCase))
        {
            bool matched = state.Battlefield.Any(IsSacOutlet);
            message = matched
                ? "battlefield has a sacrifice outlet"
                : "battlefield lacks a sacrifice outlet";
            return matched;
        }

        if (text.Equals("drain-payoff", StringComparison.OrdinalIgnoreCase))
        {
            bool matched = state.Battlefield.Any(IsDrainPayoff);
            message = matched
                ? "battlefield has a drain payoff"
                : "battlefield lacks a drain payoff";
            return matched;
        }

        if (text.Equals("recursive-creature", StringComparison.OrdinalIgnoreCase))
        {
            bool matched = state.Battlefield.Any(IsRecursiveCreature)
                || state.Graveyard.Any(IsRecursiveCreature);
            message = matched
                ? "a recursive creature is available"
                : "no recursive creature is available";
            return matched;
        }

        if (text.Equals("enchantment-recursion", StringComparison.OrdinalIgnoreCase))
        {
            return MatchZonePredicate(
                state.Battlefield,
                IsEnchantmentRecursionCard,
                "battlefield has enchantment recursion",
                "battlefield lacks enchantment recursion",
                out message);
        }

        if (text.Equals("repeatable-graveyard-recursion", StringComparison.OrdinalIgnoreCase))
        {
            bool battlefieldMatched = MatchZonePredicate(
                state.Battlefield,
                IsRepeatableGraveyardRecursionCard,
                "battlefield has repeatable graveyard recursion",
                "battlefield lacks repeatable graveyard recursion",
                out message);
            if (battlefieldMatched)
            {
                return true;
            }

            return MatchZonePredicate(
                state.Graveyard,
                IsRepeatableGraveyardRecursionCard,
                "graveyard has repeatable graveyard recursion",
                "battlefield and graveyard lack repeatable graveyard recursion",
                out message);
        }

        if (text.Equals("enchantress-engine", StringComparison.OrdinalIgnoreCase))
        {
            return MatchZonePredicate(
                state.Battlefield,
                IsEnchantressEngine,
                "battlefield has an enchantress engine",
                "battlefield lacks an enchantress engine",
                out message);
        }

        if (text.Equals("engine-payoff", StringComparison.OrdinalIgnoreCase))
        {
            return MatchZonePredicate(
                state.Battlefield,
                IsEnginePayoff,
                "battlefield has an engine payoff",
                "battlefield lacks an engine payoff",
                out message);
        }

        if (text.Equals("drain-clock", StringComparison.OrdinalIgnoreCase))
        {
            bool matched = state.Battlefield.Any(IsDrainPayoff)
                && (state.Tokens >= 2 || state.Battlefield.Any(IsSacOutlet) || state.Battlefield.Any(IsRecursiveCreature));
            message = matched
                ? "battlefield has a drain clock with fodder, recursion, or sacrifice support"
                : "battlefield lacks a drain clock plus support";
            return matched;
        }

        if (text.Equals("treasure-engine", StringComparison.OrdinalIgnoreCase))
        {
            return MatchZonePredicate(
                state.Battlefield,
                IsTreasureEngine,
                "battlefield has a treasure engine",
                "battlefield lacks a treasure engine",
                out message);
        }

        if (text.Equals("treasure-payoff", StringComparison.OrdinalIgnoreCase))
        {
            return MatchZonePredicate(
                state.Battlefield,
                IsTreasurePayoff,
                "battlefield has a treasure payoff",
                "battlefield lacks a treasure payoff",
                out message);
        }

        if (text.Equals("commander-damage-pressure", StringComparison.OrdinalIgnoreCase))
        {
            return MatchCommanderDamagePressure(state, out message);
        }

        if (TryReadPrefixedValue(text, "card:", out string cardName))
        {
            return MatchCardName(cardName, state.Battlefield, "battlefield", out message);
        }

        if (TryReadPrefixedValue(text, "role:", out string roleName))
        {
            bool matched = state.Battlefield.Any(card => DeckRoleClassifier.Classify(card).PrimaryRole.Equals(roleName, StringComparison.OrdinalIgnoreCase));
            message = matched
                ? $"battlefield has role '{roleName}'"
                : $"battlefield lacks role '{roleName}'";
            return matched;
        }

        if (TryReadPrefixedValue(text, "tag:", out string tagName))
        {
            bool matched = state.Battlefield.Any(card => DeckRoleClassifier.Classify(card).Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase));
            message = matched
                ? $"battlefield has tag '{tagName}'"
                : $"battlefield lacks tag '{tagName}'";
            return matched;
        }

        if (TryReadNumericPredicate(text, "mana>=", out int mana))
        {
            message = state.AvailableMana >= mana
                ? $"available mana {state.AvailableMana} >= {mana}"
                : $"available mana {state.AvailableMana} < {mana}";
            return state.AvailableMana >= mana;
        }

        if (TryReadNumericPredicate(text, "tokens>=", out int tokens))
        {
            message = state.Tokens >= tokens
                ? $"tokens {state.Tokens} >= {tokens}"
                : $"tokens {state.Tokens} < {tokens}";
            return state.Tokens >= tokens;
        }

        if (TryReadNumericPredicate(text, "graveyard>=", out int graveyardCount))
        {
            message = state.Graveyard.Count >= graveyardCount
                ? $"graveyard count {state.Graveyard.Count} >= {graveyardCount}"
                : $"graveyard count {state.Graveyard.Count} < {graveyardCount}";
            return state.Graveyard.Count >= graveyardCount;
        }

        if (TryReadNumericPredicate(text, "interactionheld>=", out int interactionHeld))
        {
            message = state.InteractionHeld >= interactionHeld
                ? $"interaction held {state.InteractionHeld} >= {interactionHeld}"
                : $"interaction held {state.InteractionHeld} < {interactionHeld}";
            return state.InteractionHeld >= interactionHeld;
        }

        if (TryReadNumericPredicate(text, "dungeonprogress>=", out int dungeonProgress))
        {
            message = state.DungeonProgress >= dungeonProgress
                ? $"dungeon progress {state.DungeonProgress} >= {dungeonProgress}"
                : $"dungeon progress {state.DungeonProgress} < {dungeonProgress}";
            return state.DungeonProgress >= dungeonProgress;
        }

        if (TryReadNumericPredicate(text, "turn>=", out int turn))
        {
            message = state.Turn >= turn
                ? $"turn {state.Turn} >= {turn}"
                : $"turn {state.Turn} < {turn}";
            return state.Turn >= turn;
        }

        return MatchCardName(text, state.Battlefield, "battlefield", out message);
    }

    /// <summary>
    /// Checks whether a named card is present in the inspected zone.
    /// </summary>
    private static bool MatchCardName(
        string cardName,
        IReadOnlyList<DeckCard> cards,
        string zone,
        out string message)
    {
        bool matched = cards.Any(card => card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase));
        message = matched
            ? $"{zone} has {cardName}"
            : $"{zone} lacks {cardName}";
        return matched;
    }

    /// <summary>
    /// Matches a card predicate and includes bounded card names in evidence.
    /// </summary>
    private static bool MatchZonePredicate(
        IReadOnlyList<DeckCard> cards,
        Func<DeckCard, bool> predicate,
        string matchedPrefix,
        string missingMessage,
        out string message)
    {
        List<string> names = cards
            .Where(predicate)
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (names.Count > 0)
        {
            message = $"{matchedPrefix}: {string.Join(", ", names)}";
            return true;
        }

        message = missingMessage;
        return false;
    }

    /// <summary>
    /// Identifies blink permanents that plausibly repeat without another card.
    /// </summary>
    private static bool IsRepeatableBlinkCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        if (!role.Tags.Contains(DeckTags.Blink, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        string typeLine = card.Snapshot?.TypeLine ?? "";
        string text = card.Snapshot?.OracleText ?? "";
        return ContainsAny(typeLine, "Creature", "Artifact", "Enchantment", "Planeswalker")
            && (ContainsAny(text, "at the beginning", "whenever", "activate", "{t}", "tap")
                || ContainsAny(card.Name, "Soulherder", "Teleportation Circle", "Conjurer's Closet"));
    }

    /// <summary>
    /// Identifies graveyard cards large enough to matter for reanimation routes.
    /// </summary>
    private static bool IsReanimationTarget(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string typeLine = card.Snapshot?.TypeLine ?? "";
        return typeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase)
            && ((card.Snapshot?.ManaValue ?? 0) >= 4
                || role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Identifies permanents that can sacrifice creatures or permanents repeatedly enough for aristocrats routes.
    /// </summary>
    private static bool IsSacOutlet(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return role.Tags.Contains(DeckTags.SacOutlet, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(text, "sacrifice a creature:", "sacrifice another creature", "sacrifice a permanent:", "sacrifice an artifact:");
    }

    /// <summary>
    /// Identifies death-trigger or drain payoff permanents.
    /// </summary>
    private static bool IsDrainPayoff(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Aristocrats, StringComparer.OrdinalIgnoreCase)
            || (ContainsAny(text, "dies", "whenever another creature dies", "whenever a creature dies")
                && ContainsAny(text, "each opponent loses", "opponent loses", "target player loses"));
    }

    /// <summary>
    /// Identifies creatures that can return from the graveyard or be cast from there.
    /// </summary>
    private static bool IsRecursiveCreature(DeckCard card)
    {
        string typeLine = card.Snapshot?.TypeLine ?? "";
        string text = card.Snapshot?.OracleText ?? "";
        return typeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(
                text,
                "return this card from your graveyard",
                "return it from your graveyard",
                "from your graveyard to the battlefield",
                "you may cast this card from your graveyard",
                "escape",
                "disturb",
                "unearth");
    }

    /// <summary>
    /// Identifies permanents that can recur enchantments from the graveyard.
    /// </summary>
    private static bool IsEnchantmentRecursionCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return ContainsAny(text, "enchantment")
            && (role.PrimaryRole.Equals(DeckRoles.Recursion, StringComparison.OrdinalIgnoreCase)
                || ContainsAny(text, "from your graveyard", "from a graveyard", "graveyard to the battlefield"))
            && ContainsAny(text, "return", "cast", "put");
    }

    /// <summary>
    /// Identifies cards that repeatedly use graveyard cards rather than one-shot recursion.
    /// </summary>
    private static bool IsRepeatableGraveyardRecursionCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string typeLine = card.Snapshot?.TypeLine ?? "";
        string text = card.Snapshot?.OracleText ?? "";
        bool permanent = ContainsAny(typeLine, "Creature", "Artifact", "Enchantment", "Planeswalker");
        return (permanent || role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase))
            && ContainsAny(text, "graveyard")
            && ContainsAny(text, "return", "cast", "play", "put")
            && ContainsAny(text, "whenever", "at the beginning", "activate", "{t}", ":");
    }

    /// <summary>
    /// Identifies enchantress-style engines that trigger from enchantments.
    /// </summary>
    private static bool IsEnchantressEngine(DeckCard card)
    {
        string text = card.Snapshot?.OracleText ?? "";
        return ContainsAny(text, "whenever you cast an enchantment", "whenever an enchantment enters", "constellation")
            && ContainsAny(text, "draw", "create", "gain", "add");
    }

    /// <summary>
    /// Identifies battlefield payoffs that convert engines into inevitability.
    /// </summary>
    private static bool IsEnginePayoff(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return role.PrimaryRole.Equals(DeckRoles.Payoffs, StringComparison.OrdinalIgnoreCase)
            || role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Drain, StringComparer.OrdinalIgnoreCase)
            || (role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase)
                && ContainsAny(text, "each opponent loses", "opponent loses", "damage to each opponent", "you win"));
    }

    /// <summary>
    /// Identifies repeatable treasure makers.
    /// </summary>
    private static bool IsTreasureEngine(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return ContainsAny(text, "treasure")
            && (role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
                || role.PrimaryRole.Equals(DeckRoles.Synergy, StringComparison.OrdinalIgnoreCase)
                || role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase)
                || ContainsAny(text, "whenever", "at the beginning", "create"));
    }

    /// <summary>
    /// Identifies treasure or artifact payoffs that can plausibly become alternate wins.
    /// </summary>
    private static bool IsTreasurePayoff(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return (role.PrimaryRole.Equals(DeckRoles.Wincons, StringComparison.OrdinalIgnoreCase)
                && ContainsAny(text, "treasure", "artifact"))
            || (ContainsAny(text, "treasure", "artifact")
                && ContainsAny(text, "you win", "each opponent loses", "opponent loses", "damage to each opponent"));
    }

    /// <summary>
    /// Identifies pump, evasion, or Voltron support that makes commander damage plausible.
    /// </summary>
    private static bool IsCommanderDamagePressureCard(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return role.Tags.Contains(DeckTags.Voltron, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Evasion, StringComparer.OrdinalIgnoreCase)
            || role.Tags.Contains(DeckTags.Finishers, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(
                text,
                "equipped creature gets",
                "enchanted creature gets",
                "commander creatures you own have",
                "double strike",
                "can't be blocked",
                "unblockable",
                "trample",
                "base power and toughness");
    }

    /// <summary>
    /// Requires real commander-damage support before a route can count as lethal pressure.
    /// </summary>
    private static bool MatchCommanderDamagePressure(
        SimulationRouteState state,
        out string message)
    {
        DeckCard? commander = state.Battlefield.FirstOrDefault(IsCommanderCard);
        if (!state.CommanderOnBattlefield || commander is null)
        {
            message = "commander-damage route needs the commander on the battlefield";
            return false;
        }

        int basePower = EstimateCommanderPower(commander);
        int supportCount = 0;
        int evasionCount = 0;
        foreach (DeckCard card in state.Battlefield)
        {
            if (ReferenceEquals(card, commander) || !IsCommanderDamagePressureCard(card))
            {
                continue;
            }

            supportCount++;
            if (IsCommanderEvasionSupport(card))
            {
                evasionCount++;
            }
        }

        int projectedHit = basePower + (supportCount * 2) + (evasionCount * 2);
        int expectedDamage = projectedHit * (evasionCount > 0 ? 3 : 2);
        bool matched = basePower >= 3
            && supportCount >= 1
            && expectedDamage >= 21
            && (evasionCount > 0 || supportCount >= 2);
        message = matched
            ? $"commander damage has base power {basePower}, support {supportCount}, evasion {evasionCount}, projected three-turn damage {expectedDamage}"
            : "commander damage needs more than commander presence: "
                + $"base power {basePower}, support {supportCount}, evasion {evasionCount}, projected damage {expectedDamage}";
        return matched;
    }

    /// <summary>
    /// Identifies command-zone creatures from categories or role classification.
    /// </summary>
    private static bool IsCommanderCard(DeckCard card)
    {
        return DeckCategoryOrdering.PrimaryCategory(card).Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase)
            || DeckRoleClassifier.Classify(card).PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Estimates commander power from cached data without parsing full printed power.
    /// </summary>
    private static int EstimateCommanderPower(DeckCard commander)
    {
        return Math.Max(1, (int)Math.Ceiling(commander.Snapshot?.ManaValue ?? 3));
    }

    /// <summary>
    /// Checks for support that makes repeated commander hits plausible.
    /// </summary>
    private static bool IsCommanderEvasionSupport(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = card.Snapshot?.OracleText ?? "";
        return role.Tags.Contains(DeckTags.Evasion, StringComparer.OrdinalIgnoreCase)
            || ContainsAny(text, "flying", "menace", "can't be blocked", "unblockable", "trample", "double strike");
    }

    /// <summary>
    /// Reads a value after a supported textual prefix.
    /// </summary>
    private static bool TryReadPrefixedValue(string text, string prefix, out string value)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = text[prefix.Length..].Trim();
            return value.Length > 0;
        }

        value = "";
        return false;
    }

    /// <summary>
    /// Checks whether text contains a supported numeric predicate.
    /// </summary>
    private static bool HasNumericPredicate(string text, string prefix)
    {
        return TryReadNumericPredicate(text, prefix, out _);
    }

    /// <summary>
    /// Reads an integer after a supported numeric predicate prefix.
    /// </summary>
    private static bool TryReadNumericPredicate(string text, string prefix, out int value)
    {
        value = 0;
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(text[prefix.Length..].Trim(), out value)
            && value >= 0;
    }

    /// <summary>
    /// Normalizes symbolic route predicates without changing card names.
    /// </summary>
    private static string NormalizeRequirement(string requirement)
    {
        string trimmed = requirement.Trim();
        if (trimmed.StartsWith("card:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("role:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        string compact = trimmed
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase);
        if (compact.Equals("commander", StringComparison.OrdinalIgnoreCase))
        {
            return "commander";
        }

        if (compact.Equals("repeatableblink", StringComparison.OrdinalIgnoreCase))
        {
            return "repeatable-blink";
        }

        if (compact.Equals("reanimationtarget", StringComparison.OrdinalIgnoreCase))
        {
            return "reanimation-target";
        }

        if (compact.Equals("sacoutlet", StringComparison.OrdinalIgnoreCase))
        {
            return "sac-outlet";
        }

        if (compact.Equals("drainpayoff", StringComparison.OrdinalIgnoreCase))
        {
            return "drain-payoff";
        }

        if (compact.Equals("recursivecreature", StringComparison.OrdinalIgnoreCase))
        {
            return "recursive-creature";
        }

        if (compact.Equals("enchantmentrecursion", StringComparison.OrdinalIgnoreCase))
        {
            return "enchantment-recursion";
        }

        if (compact.Equals("repeatablegraveyardrecursion", StringComparison.OrdinalIgnoreCase))
        {
            return "repeatable-graveyard-recursion";
        }

        if (compact.Equals("enchantressengine", StringComparison.OrdinalIgnoreCase))
        {
            return "enchantress-engine";
        }

        if (compact.Equals("enginepayoff", StringComparison.OrdinalIgnoreCase))
        {
            return "engine-payoff";
        }

        if (compact.Equals("drainclock", StringComparison.OrdinalIgnoreCase))
        {
            return "drain-clock";
        }

        if (compact.Equals("treasureengine", StringComparison.OrdinalIgnoreCase))
        {
            return "treasure-engine";
        }

        if (compact.Equals("treasurepayoff", StringComparison.OrdinalIgnoreCase))
        {
            return "treasure-payoff";
        }

        if (compact.Equals("commanderdamagepressure", StringComparison.OrdinalIgnoreCase))
        {
            return "commander-damage-pressure";
        }

        string numeric = NormalizeNumericPredicate(trimmed);
        if (numeric.StartsWith("mana>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("tokens>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("graveyard>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("interactionheld>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("dungeonprogress>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("turn>=", StringComparison.OrdinalIgnoreCase))
        {
            return numeric.ToLowerInvariant();
        }

        return trimmed;
    }

    /// <summary>
    /// Normalizes numeric predicate names while preserving signed values.
    /// </summary>
    private static string NormalizeNumericPredicate(string text)
    {
        int operatorIndex = text.IndexOf(">=", StringComparison.Ordinal);
        if (operatorIndex < 0)
        {
            return text;
        }

        string name = text[..operatorIndex]
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase);
        string value = text[(operatorIndex + 2)..].Trim();
        return $"{name}>={value}";
    }

    /// <summary>
    /// Checks whether a value contains any provided token.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Represents the parts of a simulated game state available to route predicates.
/// </summary>
public sealed class SimulationRouteState
{
    /// <summary>
    /// Gets or sets the current turn.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets cards on the battlefield.
    /// </summary>
    public IReadOnlyList<DeckCard> Battlefield { get; set; } = [];

    /// <summary>
    /// Gets or sets cards in hand.
    /// </summary>
    public IReadOnlyList<DeckCard> Hand { get; set; } = [];

    /// <summary>
    /// Gets or sets cards in the graveyard.
    /// </summary>
    public IReadOnlyList<DeckCard> Graveyard { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the commander is on the battlefield.
    /// </summary>
    public bool CommanderOnBattlefield { get; set; }

    /// <summary>
    /// Gets or sets the current token count estimate.
    /// </summary>
    public int Tokens { get; set; }

    /// <summary>
    /// Gets or sets the remaining available mana estimate.
    /// </summary>
    public int AvailableMana { get; set; }

    /// <summary>
    /// Gets or sets held interaction count estimate.
    /// </summary>
    public int InteractionHeld { get; set; }

    /// <summary>
    /// Gets or sets dungeon progress estimate.
    /// </summary>
    public int DungeonProgress { get; set; }
}
