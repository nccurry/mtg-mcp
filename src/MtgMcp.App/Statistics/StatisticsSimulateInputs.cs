using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Names the saved deck whose main zone <c>stats_simulate</c> shuffles.
/// </summary>
internal sealed record SimulationDeckInput
{
    /// <summary>
    /// Gets the stable local deck UUID.
    /// </summary>
    [Description("Stable local deck UUID returned by local_deck_get.")]
    public required Guid DeckId { get; init; }

    /// <summary>
    /// Gets the optional exact deck revision.
    /// </summary>
    [Description(
        "Optional exact deck revision. A different current revision returns conflict with the current one. Omit it " +
        "to use the current revision; the result then names the revision that was read.")]
    public long? ExpectedRevision { get; init; }
}

/// <summary>
/// Gives colors for the listed entries of a group when they differ from the group's default colors.
/// </summary>
internal sealed record SimulationProducesOverrideInput
{
    /// <summary>
    /// Gets the entries that add these colors.
    /// </summary>
    [Description("Entries of the group that add these colors instead of the default.")]
    public required IReadOnlyList<Guid> EntryIds { get; init; }

    /// <summary>
    /// Gets the colors.
    /// </summary>
    [Description("One or more of W, U, B, R, G, C, or any (one of W, U, B, R, G) that each of these entries adds.")]
    public required IReadOnlyList<string> Colors { get; init; }
}

/// <summary>
/// Says which colors the cards of a group add when they enter play, one unit of mana for each unit they add.
/// </summary>
internal sealed record SimulationProducesInput
{
    /// <summary>
    /// Gets the default colors of the group.
    /// </summary>
    [Description("One or more of W, U, B, R, G, C, or any (one of W, U, B, R, G) that each card of the group adds.")]
    public required IReadOnlyList<string> Default { get; init; }

    /// <summary>
    /// Gets the entries that add other colors.
    /// </summary>
    [Description("Optional entries that add other colors than the default.")]
    public IReadOnlyList<SimulationProducesOverrideInput>? ByEntry { get; init; }
}

/// <summary>
/// Declares one group of deck entries that rules and checks refer to by ID.
/// </summary>
internal sealed record SimulationGroupInput
{
    /// <summary>
    /// Gets the group's unique name.
    /// </summary>
    [Description("Unique name for the group. Rules and checks refer to it.")]
    public required string Id { get; init; }

    /// <summary>
    /// Gets how the group's entries are chosen.
    /// </summary>
    [Description("Which main-zone entries are in the group, by entry ID or category ID. Groups must not overlap.")]
    public required CastabilitySelectInput Select { get; init; }

    /// <summary>
    /// Gets the colors of mana the group's cards add.
    /// </summary>
    [Description("Colors of mana the group's cards add. Without it the group adds generic mana only.")]
    public SimulationProducesInput? Produces { get; init; }
}

/// <summary>
/// Chooses the cards a mulligan keeps; only these three kinds are allowed there.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SimulationKeepCountBetweenInput), "count-between")]
[JsonDerivedType(typeof(SimulationKeepAllInput), "all")]
[JsonDerivedType(typeof(SimulationKeepAnyInput), "any")]
internal abstract record SimulationKeepInput;

/// <summary>
/// Keeps a hand that holds a number of cards of a group within a range.
/// </summary>
internal sealed record SimulationKeepCountBetweenInput : SimulationKeepInput
{
    /// <summary>
    /// Gets the group to count.
    /// </summary>
    [Description("Group ID whose cards in the hand are counted.")]
    public required string Group { get; init; }

    /// <summary>
    /// Gets the least number of cards.
    /// </summary>
    [Description("Least number of cards of the group in the hand, from 0 to 99.")]
    public required int Min { get; init; }

    /// <summary>
    /// Gets the greatest number of cards.
    /// </summary>
    [Description("Greatest number of cards of the group in the hand, from min to 99.")]
    public required int Max { get; init; }
}

/// <summary>
/// Keeps a hand when every listed check passes.
/// </summary>
internal sealed record SimulationKeepAllInput : SimulationKeepInput
{
    /// <summary>
    /// Gets the checks that must all pass.
    /// </summary>
    [Description("One or more keep checks that must all pass.")]
    public required IReadOnlyList<SimulationKeepInput> Checks { get; init; }
}

/// <summary>
/// Keeps a hand when at least one listed check passes.
/// </summary>
internal sealed record SimulationKeepAnyInput : SimulationKeepInput
{
    /// <summary>
    /// Gets the checks of which one must pass.
    /// </summary>
    [Description("One or more keep checks of which at least one must pass.")]
    public required IReadOnlyList<SimulationKeepInput> Checks { get; init; }
}

/// <summary>
/// Says which cards to put on the bottom after a mulligan.
/// </summary>
internal sealed record SimulationBottomInput
{
    /// <summary>
    /// Gets the groups to bottom from first.
    /// </summary>
    [Description("Group IDs to bottom cards from first, in order; random within a group. May be empty.")]
    public required IReadOnlyList<string> PreferGroups { get; init; }

    /// <summary>
    /// Gets how any remaining card is chosen.
    /// </summary>
    [Description("How to choose a card when no preferred group has one in hand. Only random is supported."),
     AllowedValues("random")]
    public required string Otherwise { get; init; }
}

/// <summary>
/// Describes a London mulligan.
/// </summary>
internal sealed record SimulationMulliganInput
{
    /// <summary>
    /// Gets the mulligan rule.
    /// </summary>
    [Description("The mulligan rule. Only london is supported: draw a new hand, then put cards on the bottom."),
     AllowedValues("london")]
    public required string Rule { get; init; }

    /// <summary>
    /// Gets whether the first mulligan is free.
    /// </summary>
    [Description("True makes the first mulligan free: it puts no card on the bottom, as in Commander.")]
    public required bool FreeFirstMulligan { get; init; }

    /// <summary>
    /// Gets the most mulligans.
    /// </summary>
    [Description("The most mulligans, from 0 to 10. At the limit the last hand is kept.")]
    public required int MaxMulligans { get; init; }

    /// <summary>
    /// Gets the keep rule.
    /// </summary>
    [Description("The rule a hand must pass to be kept.")]
    public required SimulationKeepInput KeepWhen { get; init; }

    /// <summary>
    /// Gets how cards are bottomed.
    /// </summary>
    [Description("Which cards go on the bottom after a mulligan.")]
    public required SimulationBottomInput Bottom { get; init; }
}

/// <summary>
/// Describes the opening hand.
/// </summary>
internal sealed record SimulationOpeningHandInput
{
    /// <summary>
    /// Gets the hand size.
    /// </summary>
    [Description("Number of cards in the opening hand, from 1 to 15.")]
    public required int Size { get; init; }

    /// <summary>
    /// Gets whether the player goes first.
    /// </summary>
    [Description("True if the player goes first and skips the draw on turn 1.")]
    public required bool OnThePlay { get; init; }

    /// <summary>
    /// Gets the mulligan.
    /// </summary>
    [Description("The mulligan. Omit it to keep the first hand.")]
    public SimulationMulliganInput? Mulligan { get; init; }
}

/// <summary>
/// Gives the time at which the mana of the listed entries becomes usable, when it differs from the rule's default.
/// </summary>
internal sealed record SimulationManaFromOverrideInput
{
    /// <summary>
    /// Gets the entries.
    /// </summary>
    [Description("Entries of the rule's group whose mana becomes usable at this time.")]
    public required IReadOnlyList<Guid> EntryIds { get; init; }

    /// <summary>
    /// Gets when the mana becomes usable.
    /// </summary>
    [Description("same-turn or next-turn."), AllowedValues("same-turn", "next-turn")]
    public required string ManaFrom { get; init; }
}

/// <summary>
/// Declares one rule applied each turn, in the order listed: <c>play</c>, <c>cast-if-able</c>, or
/// <c>draw-cards</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SimulationPlayInput), "play")]
[JsonDerivedType(typeof(SimulationCastIfAbleInput), "cast-if-able")]
[JsonDerivedType(typeof(SimulationDrawCardsInput), "draw-cards")]
internal abstract record SimulationRuleInput
{
    /// <summary>
    /// Gets the group the rule takes cards from.
    /// </summary>
    [Description("Group ID of the cards the rule moves.")]
    public required string Group { get; init; }

    /// <summary>
    /// Gets how the card is chosen.
    /// </summary>
    [Description(
        "How to choose among the group's cards in hand: random (uniform, seeded), listed-order (first entry in the " +
        "group's entryIds; only for entry-ids groups), or target-first (play rules only; needs targetCost)."),
     AllowedValues("random", "listed-order", "target-first")]
    public required string Pick { get; init; }

    /// <summary>
    /// Gets the most cards moved each turn.
    /// </summary>
    [Description("The most cards the rule moves each turn, from 1 to 99.")]
    public required int MaxPerTurn { get; init; }
}

/// <summary>
/// Declares the mana fields shared by the rules that put a card in play.
/// </summary>
internal abstract record SimulationManaRuleInput : SimulationRuleInput
{
    /// <summary>
    /// Gets how many units of mana each card adds.
    /// </summary>
    [Description("Units of mana each card adds, from 0 to 99. Default: 1.")]
    public int AddsMana { get; init; } = 1;

    /// <summary>
    /// Gets when the mana becomes usable.
    /// </summary>
    [Description(
        "same-turn: usable on the turn the card is played. next-turn: usable from the next turn."),
     AllowedValues("same-turn", "next-turn")]
    public required string ManaFrom { get; init; }

    /// <summary>
    /// Gets entries whose mana becomes usable at another time.
    /// </summary>
    [Description("Optional entries whose mana becomes usable at another time than manaFrom.")]
    public IReadOnlyList<SimulationManaFromOverrideInput>? ManaFromByEntry { get; init; }
}

/// <summary>
/// Moves a card of a group from hand to play at no mana cost.
/// </summary>
internal sealed record SimulationPlayInput : SimulationManaRuleInput
{
    /// <summary>
    /// Gets the cost that target-first ranks cards by.
    /// </summary>
    [Description(
        "A mana cost such as {1}{U}{U}. Required with pick target-first, which plays the card that lets the " +
        "available mana pay the most colored symbols of this cost, then the most symbols, then prefers mana usable " +
        "this turn, then the lowest entry ID. Not allowed with another pick.")]
    public string? TargetCost { get; init; }
}

/// <summary>
/// Spends generic mana to move a card of a group from hand to play.
/// </summary>
internal sealed record SimulationCastIfAbleInput : SimulationManaRuleInput
{
    /// <summary>
    /// Gets the generic mana spent.
    /// </summary>
    [Description("Generic mana spent for each card, from 0 to 99. The rule fires only while this much is unspent.")]
    public required int Cost { get; init; }
}

/// <summary>
/// Spends generic mana to cast a card that draws cards.
/// </summary>
internal sealed record SimulationDrawCardsInput : SimulationRuleInput
{
    /// <summary>
    /// Gets the generic mana spent.
    /// </summary>
    [Description("Generic mana spent for each card, from 0 to 99. The rule fires only while this much is unspent.")]
    public required int Cost { get; init; }

    /// <summary>
    /// Gets the cards drawn.
    /// </summary>
    [Description("Cards drawn at once, and at each later draw step for an each-turn card, from 1 to 99.")]
    public required int Cards { get; init; }

    /// <summary>
    /// Gets what the card does afterward.
    /// </summary>
    [Description(
        "once: the card leaves play after drawing, like a spell. each-turn: the card stays in play and draws again " +
        "at every later draw step, like a permanent."),
     AllowedValues("once", "each-turn")]
    public required string Effect { get; init; }
}

/// <summary>
/// Declares one check whose success rate the result reports.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SimulationAllInput), "all")]
[JsonDerivedType(typeof(SimulationAnyInput), "any")]
[JsonDerivedType(typeof(SimulationManaAtLeastInput), "mana-at-least")]
[JsonDerivedType(typeof(SimulationDrawnAtLeastInput), "drawn-at-least")]
[JsonDerivedType(typeof(SimulationOnBattlefieldAtLeastInput), "on-battlefield-at-least")]
[JsonDerivedType(typeof(SimulationCanCastInput), "can-cast")]
internal abstract record SimulationCheckInput;

/// <summary>
/// Passes when every listed check passes.
/// </summary>
internal sealed record SimulationAllInput : SimulationCheckInput
{
    /// <summary>
    /// Gets the checks that must all pass.
    /// </summary>
    [Description("One or more checks that must all pass.")]
    public required IReadOnlyList<SimulationCheckInput> Checks { get; init; }
}

/// <summary>
/// Passes when at least one listed check passes.
/// </summary>
internal sealed record SimulationAnyInput : SimulationCheckInput
{
    /// <summary>
    /// Gets the checks of which one must pass.
    /// </summary>
    [Description("One or more checks of which at least one must pass.")]
    public required IReadOnlyList<SimulationCheckInput> Checks { get; init; }
}

/// <summary>
/// Passes when at least this much mana is unspent on a turn, after that turn's rules.
/// </summary>
internal sealed record SimulationManaAtLeastInput : SimulationCheckInput
{
    /// <summary>
    /// Gets the amount.
    /// </summary>
    [Description("Least mana still unspent after the turn's rules, from 0 to 99.")]
    public required int Amount { get; init; }

    /// <summary>
    /// Gets the turn.
    /// </summary>
    [Description("The turn to check, from 1 to turns.")]
    public required int Turn { get; init; }
}

/// <summary>
/// Passes when at least this many cards of a group have been seen by a turn.
/// </summary>
internal sealed record SimulationDrawnAtLeastInput : SimulationCheckInput
{
    /// <summary>
    /// Gets the group.
    /// </summary>
    [Description("Group ID whose cards are counted.")]
    public required string Group { get; init; }

    /// <summary>
    /// Gets the count.
    /// </summary>
    [Description("Least number of the group's cards seen, from 1 to 99. Seen cards are those in the kept opening " +
        "hand, including any put on the bottom, and every card drawn later.")]
    public required int Count { get; init; }

    /// <summary>
    /// Gets the last turn.
    /// </summary>
    [Description("The last turn to count, from 1 to turns.")]
    public required int ByTurn { get; init; }
}

/// <summary>
/// Passes when at least this many cards of a group are in play by a turn.
/// </summary>
internal sealed record SimulationOnBattlefieldAtLeastInput : SimulationCheckInput
{
    /// <summary>
    /// Gets the group.
    /// </summary>
    [Description("Group ID whose cards are counted.")]
    public required string Group { get; init; }

    /// <summary>
    /// Gets the count.
    /// </summary>
    [Description("Least number of the group's cards in play, from 1 to 99.")]
    public required int Count { get; init; }

    /// <summary>
    /// Gets the last turn.
    /// </summary>
    [Description("The last turn to count, from 1 to turns.")]
    public required int ByTurn { get; init; }
}

/// <summary>
/// Passes when the available mana can pay a mana cost together with all mana the rules spent that turn.
/// </summary>
internal sealed record SimulationCanCastInput : SimulationCheckInput
{
    /// <summary>
    /// Gets the mana cost.
    /// </summary>
    [Description(
        "A Scryfall mana cost such as {2}{B}{G}. Hybrid symbols such as {W/U} accept either color. Phyrexian " +
        "symbols such as {G/P} need their color; life payment is not modeled. X and other symbols are refused.")]
    public required string ManaCost { get; init; }

    /// <summary>
    /// Gets the one turn to check.
    /// </summary>
    [Description("The one turn to check, from 1 to turns. Give exactly one of turn and byTurn.")]
    public int? Turn { get; init; }

    /// <summary>
    /// Gets the last of the turns to check.
    /// </summary>
    [Description("Passes if any turn from 1 up to this one passes, from 1 to turns. Give exactly one of turn and " +
        "byTurn.")]
    public int? ByTurn { get; init; }

    /// <summary>
    /// Gets the entry that must be in hand.
    /// </summary>
    [Description("Optional entry ID that must also be in hand on the checked turn.")]
    public Guid? InHand { get; init; }
}

/// <summary>
/// Names one check whose success rate the result reports.
/// </summary>
internal sealed record SimulationConditionInput
{
    /// <summary>
    /// Gets the condition's unique name.
    /// </summary>
    [Description("Unique name for the condition. The result reports it with its hits and rate.")]
    public required string Id { get; init; }

    /// <summary>
    /// Gets the check.
    /// </summary>
    [Description("The check that counts as a hit in a sample.")]
    public required SimulationCheckInput Check { get; init; }
}

/// <summary>
/// Gives the caller's rule for one land of the on-curve preset.
/// </summary>
internal sealed record SimulationLandRuleInput
{
    /// <summary>
    /// Gets the land's entry.
    /// </summary>
    [Description("Stable entry UUID of one eligible land in the main zone.")]
    public required Guid EntryId { get; init; }

    /// <summary>
    /// Gets the rule.
    /// </summary>
    [Description("one-mana-same-turn, one-mana-next-turn, or not-modeled (the land is never played)."),
     AllowedValues("one-mana-same-turn", "one-mana-next-turn", "not-modeled")]
    public required string Rule { get; init; }
}

/// <summary>
/// Names a ready-made set of rules. The only preset is <c>on-curve</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SimulationOnCurvePresetInput), "on-curve")]
internal abstract record SimulationPresetInput;

/// <summary>
/// Samples how often one card of the deck can be cast by a turn when lands are the only mana, using the land and
/// card facts installed from Scryfall.
/// </summary>
internal sealed record SimulationOnCurvePresetInput : SimulationPresetInput
{
    /// <summary>
    /// Gets the target entry.
    /// </summary>
    [Description("Stable ID of the one main-zone entry whose card is the target.")]
    public required Guid TargetEntryId { get; init; }

    /// <summary>
    /// Gets the rule for each land.
    /// </summary>
    [Description("One rule for every eligible single-faced land in the main zone, each named once.")]
    public required IReadOnlyList<SimulationLandRuleInput> LandRules { get; init; }

    /// <summary>
    /// Gets the last turn.
    /// </summary>
    [Description("Last turn to model, from 1 to 20.")]
    public required int TurnLimit { get; init; }

    /// <summary>
    /// Gets whether the player goes first.
    /// </summary>
    [Description("True skips the draw on turn 1.")]
    public required bool OnThePlay { get; init; }
}
