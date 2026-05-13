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
        if (ContainsAny(normalized, "commander protection", "protect commander", "protection", "protect my commander"))
        {
            return new DeckGoalSpec(
                Strategy: NormalizeFocus(strategy),
                Category: DeckRoles.Protection,
                Searches:
                [
                    Request(CardSearchPreset.CommanderProtectionEquipment, format, maxPrice),
                    Request(CardSearchPreset.CommanderProtectionSpell, format, maxPrice)
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
                Searches:
                [
                    Request(CardSearchPreset.DrawDiscard, format, maxPrice)
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
                Searches: [Request(CardSearchPreset.CardDraw, format, maxPrice)],
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
                Searches:
                [
                    Request(CardSearchPreset.DiscardSynergy, format, maxPrice)
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
                Searches:
                [
                    Request(CardSearchPreset.PoliticalChoices, format, maxPrice),
                    Request(CardSearchPreset.PoliticalTableEffects, format, maxPrice)
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
                Searches:
                [
                    Request(CardSearchPreset.WholeTablePolitics, format, maxPrice),
                    Request(CardSearchPreset.WholeTableEffects, format, maxPrice)
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
                Searches:
                [
                    Request(CardSearchPreset.TokenDefenseSweepers, format, maxPrice),
                    Request(CardSearchPreset.TokenDefensePillowfort, format, maxPrice)
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
                Searches:
                [
                    Request(CardSearchPreset.GraveyardHate, format, maxPrice)
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
                Searches: [Request(CardSearchPreset.Finishers, format, maxPrice)],
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
                Searches: [Request(CardSearchPreset.LessSaltyValue, format, maxPrice)],
                RequiredRoles: [DeckRoles.Draw, DeckRoles.Synergy],
                RequiredTags: [],
                ExcludedRoles: [DeckRoles.Tutors, DeckRoles.Wincons],
                ExcludedTags: [DeckTags.Stax, DeckTags.ComboPiece, DeckTags.ComboEnabler],
                Rationale: "Adds lower-pressure value cards rather than tutors, fast mana, stax, or combo pieces.");
        }

        return new DeckGoalSpec(
            Strategy: NormalizeFocus(strategy),
            Category: DeckRoles.Utility,
            Searches:
            [
                Request(CardSearchPreset.BroadUseful, format, maxPrice),
                Request(CardSearchPreset.BroadUsefulFallback, format, maxPrice)
            ],
            RequiredRoles: [DeckRoles.Draw, DeckRoles.Interaction, DeckRoles.Ramp],
            RequiredTags: [],
            ExcludedRoles: [],
            ExcludedTags: [],
            Rationale: "Adds broadly useful cards that improve weak role coverage.");
    }

    /// <summary>
    /// Creates a filtered catalog search request for a goal preset.
    /// </summary>
    private static CardSearchRequest Request(CardSearchPreset preset, string format, decimal maxPrice)
    {
        return CardSearchRequest.ForPreset(preset, format, maxPrice);
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
    CardSearchRequest[] Searches,
    string[] RequiredRoles,
    string[] RequiredTags,
    string[] ExcludedRoles,
    string[] ExcludedTags,
    string Rationale);
