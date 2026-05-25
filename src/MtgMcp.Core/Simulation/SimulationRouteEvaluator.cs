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
            || text.StartsWith("card:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("role:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("tag:", StringComparison.OrdinalIgnoreCase)
            || HasNumericPredicate(text, "mana>=")
            || HasNumericPredicate(text, "tokens>=")
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

        string numeric = trimmed
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase);
        if (numeric.StartsWith("mana>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("tokens>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("interactionheld>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("dungeonprogress>=", StringComparison.OrdinalIgnoreCase)
            || numeric.StartsWith("turn>=", StringComparison.OrdinalIgnoreCase))
        {
            return numeric.ToLowerInvariant();
        }

        return trimmed;
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
