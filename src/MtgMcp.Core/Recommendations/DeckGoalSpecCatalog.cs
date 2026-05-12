namespace MtgMcp.Core;

/// <summary>
/// Maps common natural-language deck goals into query-first recommendation constraints.
/// </summary>
internal static class DeckGoalSpecCatalog
{
    /// <summary>
    /// Builds query constraints from a natural-language deckbuilding goal.
    /// </summary>
    public static DeckGoalSpec Build(string goal, string format, decimal maxPrice, string strategy)
    {
        string normalized = goal.ToLowerInvariant();
        string legal = $"legal:{NormalizeFormat(format)} usd<={maxPrice:0.##}";
        if (ContainsAny(normalized, "commander protection", "protect commander", "protection", "protect my commander"))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Protection,
                Queries:
                [
                    $"(o:equipped o:hexproof or o:equipped o:shroud or (o:target o:creature o:hexproof) or (o:permanents o:control o:hexproof)) {legal}",
                    $"((o:\"creature you control\" o:indestructible) or (o:target o:creature o:protection) or o:\"phase out\") {legal}"
                ],
                RequiredRoles: [DeckRoles.Protection],
                RequiredTags: [],
                ExcludedRoles: [],
                ExcludedTags: [],
                Rationale: "Adds cards that can protect the commander or another important permanent.");
        }

        if (IsDrawDiscardGoal(normalized))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Draw,
                Queries:
                [
                    $"(o:draw or o:\"draw a card\" or o:\"each opponent discards\" or o:\"each player discards\" or o:\"whenever an opponent discards\" or o:\"whenever you discard\" or o:\"discard a card\") {legal}"
                ],
                RequiredRoles: [DeckRoles.Draw, DeckRoles.Payoffs, DeckRoles.Synergy, DeckRoles.Ramp],
                RequiredTags: [DeckTags.Discard],
                ExcludedRoles: [DeckRoles.Wincons],
                ExcludedTags: [DeckTags.Aristocrats, DeckTags.Drain],
                Rationale: "Adds card-advantage and discard-synergy cards while excluding unrelated aristocrats or drain packages.");
        }

        if (ContainsAny(normalized, "card advantage", "draw cards", "draw more", "more draw", "card draw", "draw"))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Draw,
                Queries: [$"(o:draw or o:\"draw a card\" or o:\"draw cards\") {legal}"],
                RequiredRoles: [DeckRoles.Draw],
                RequiredTags: [],
                ExcludedRoles: [],
                ExcludedTags: [],
                Rationale: "Adds cards that increase card draw or card advantage.");
        }

        if (ContainsAny(normalized, "discard synergy", "discard payoffs", "discard cards", "make opponents discard", "opponents discard", "discard"))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Payoffs,
                Queries:
                [
                    $"(o:\"each opponent discards\" or o:\"each player discards\" or o:\"target player discards\" or o:\"whenever an opponent discards\" or o:\"whenever you discard\") {legal}"
                ],
                RequiredRoles: [],
                RequiredTags: [DeckTags.Discard],
                ExcludedRoles: [],
                ExcludedTags: [DeckTags.Aristocrats],
                Rationale: "Adds discard enablers and discard payoffs that directly support the deck's discard plan.");
        }

        if (ContainsAny(normalized, "politics", "goad", "tempt", "tempting", "monarch", "vote", "council"))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Interaction,
                Queries:
                [
                    $"(o:goad or o:monarch or o:vote or o:\"council's dilemma\" or o:\"will of the council\" or o:\"tempting offer\") {legal}",
                    $"(o:\"each opponent\" or o:\"opponents choose\" or o:\"each player votes\") {legal}"
                ],
                RequiredRoles: [],
                RequiredTags: [DeckTags.Politics, DeckTags.TableInteraction],
                ExcludedRoles: [],
                ExcludedTags: [],
                Rationale: "Adds political or table-wide effects that create choices and affect multiple opponents.");
        }

        if (normalized.Contains("whole table", StringComparison.OrdinalIgnoreCase) || normalized.Contains("table", StringComparison.OrdinalIgnoreCase))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Interaction,
                Queries:
                [
                    $"(o:goad or o:monarch or o:vote or o:\"tempting offer\" or o:\"each opponent\") {legal}",
                    $"(o:\"each player\" or o:\"opponents choose\" or o:\"each creature\") {legal}"
                ],
                RequiredRoles: [],
                RequiredTags: [DeckTags.TableInteraction, DeckTags.Politics],
                ExcludedRoles: [],
                ExcludedTags: [],
                Rationale: "Adds effects that touch multiple opponents or the whole battlefield.");
        }

        if (normalized.Contains("token", StringComparison.OrdinalIgnoreCase) || normalized.Contains("go wide", StringComparison.OrdinalIgnoreCase))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Interaction,
                Queries:
                [
                    $"(o:\"destroy all tokens\" or o:\"each creature gets -1/-1\" or o:\"prevent all combat damage\") {legal}",
                    $"(o:\"creatures can't attack you\" or o:\"unless their controller pays\") {legal}"
                ],
                RequiredRoles: [],
                RequiredTags: [DeckTags.TokenHate, DeckTags.GoWideProtection, DeckTags.Pillowfort],
                ExcludedRoles: [],
                ExcludedTags: [],
                Rationale: "Adds defenses and sweepers against go-wide token pressure.");
        }

        if (normalized.Contains("graveyard", StringComparison.OrdinalIgnoreCase))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Interaction,
                Queries:
                [
                    $"(o:\"exile target card from a graveyard\" or o:\"exile all graveyards\" or o:\"cards in graveyards\") {legal}"
                ],
                RequiredRoles: [],
                RequiredTags: [DeckTags.GraveyardHate],
                ExcludedRoles: [],
                ExcludedTags: [],
                Rationale: "Adds graveyard hate that can answer recursion and reanimation decks.");
        }

        if (normalized.Contains("finisher", StringComparison.OrdinalIgnoreCase) || normalized.Contains("win", StringComparison.OrdinalIgnoreCase))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Wincons,
                Queries: [$"(o:\"each opponent loses\" or o:\"damage to each opponent\" or o:\"win the game\" or o:\"extra combat\") {legal}"],
                RequiredRoles: [DeckRoles.Wincons],
                RequiredTags: [],
                ExcludedRoles: [],
                ExcludedTags: [],
                Rationale: "Adds clearer closing cards and win routes.");
        }

        if (IsLessSaltyGoal(goal))
        {
            return new DeckGoalSpec(
                Strategy: "casual",
                Category: DeckRoles.Utility,
                Queries: [$"(o:create or o:draw or o:gain) {legal}"],
                RequiredRoles: [DeckRoles.Draw, DeckRoles.Synergy],
                RequiredTags: [],
                ExcludedRoles: [DeckRoles.Tutors, DeckRoles.Wincons],
                ExcludedTags: [DeckTags.Stax, DeckTags.ComboPiece, DeckTags.ComboEnabler],
                Rationale: "Adds lower-pressure value cards rather than tutors, fast mana, stax, or combo pieces.");
        }

        return new DeckGoalSpec(
            Strategy: NormalizeFocus(strategy),
            Category: DeckRoles.Utility,
            Queries: [$"{legal}", $"(o:draw or o:\"destroy target\" or o:add) {legal}"],
            RequiredRoles: [DeckRoles.Draw, DeckRoles.Interaction, DeckRoles.Ramp],
            RequiredTags: [],
            ExcludedRoles: [],
            ExcludedTags: [],
            Rationale: "Adds broadly useful cards that improve weak role coverage.");
    }

    /// <summary>
    /// Checks whether a goal asks to reduce salt or power.
    /// </summary>
    public static bool IsLessSaltyGoal(string goal)
    {
        return goal.Contains("less salty", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("less salt", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("less power", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("power down", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a goal asks for cards that bridge draw and discard.
    /// </summary>
    private static bool IsDrawDiscardGoal(string normalizedGoal)
    {
        return ContainsAny(normalizedGoal, "draw/discard", "discard/draw", "draw discard", "discard draw")
            || (ContainsAny(normalizedGoal, "draw", "card advantage") && ContainsAny(normalizedGoal, "discard", "tinybones"));
    }

    /// <summary>
    /// Normalizes a focus value.
    /// </summary>
    private static string NormalizeFocus(string? focus)
    {
        return string.IsNullOrWhiteSpace(focus)
            ? "balanced"
            : focus.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes a format name.
    /// </summary>
    private static string NormalizeFormat(string? format)
    {
        string normalized = format?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "" => "commander",
            "edh" => "commander",
            _ => normalized
        };
    }

    /// <summary>
    /// Checks whether text contains any needles.
    /// </summary>
    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Describes generated query constraints for a natural-language goal.
/// </summary>
internal sealed record DeckGoalSpec(
    string Strategy,
    string Category,
    string[] Queries,
    string[] RequiredRoles,
    string[] RequiredTags,
    string[] ExcludedRoles,
    string[] ExcludedTags,
    string Rationale);
