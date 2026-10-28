namespace MtgMcp.OnCurve;

/// <summary>
/// States how a rule chooses among the qualifying cards of its group that are in hand.
/// </summary>
public enum SimulationPick
{
    /// <summary>
    /// Chooses uniformly with the seeded generator.
    /// </summary>
    Random,

    /// <summary>
    /// Takes the card whose entry comes first in the group's listed entry IDs.
    /// </summary>
    ListedOrder,

    /// <summary>
    /// Takes the card that lets the available mana pay the most of a target cost, as the on-curve land choice does.
    /// </summary>
    TargetFirst,
}

/// <summary>
/// States when the mana of a card that enters play becomes usable.
/// </summary>
public enum SimulationManaFrom
{
    /// <summary>
    /// Usable on the turn the card is played.
    /// </summary>
    SameTurn,

    /// <summary>
    /// Usable from the next turn.
    /// </summary>
    NextTurn,
}

/// <summary>
/// States what a drawing card does after it is cast.
/// </summary>
public enum SimulationDrawEffect
{
    /// <summary>
    /// The card leaves play after it draws once, like a spell.
    /// </summary>
    Once,

    /// <summary>
    /// The card stays in play and draws again at every later draw step, like a permanent.
    /// </summary>
    EachTurn,
}

/// <summary>
/// Holds every fixed limit of the sampling engine.
/// </summary>
public static class SimulationLimits
{
    /// <summary>
    /// Gets the most sampled games in one request.
    /// </summary>
    public const int MaximumSamples = 100_000;

    /// <summary>
    /// Gets the most samples that can be returned in full.
    /// </summary>
    public const int MaximumSampleHands = 10;

    /// <summary>
    /// Gets the last turn a request can model.
    /// </summary>
    public const int MaximumTurns = 20;

    /// <summary>
    /// Gets the most work one request may need: samples times turns times cards in the deck.
    /// </summary>
    public const long MaximumWork = 50_000_000;

    /// <summary>
    /// Gets the most distinct entries in a deck.
    /// </summary>
    public const int MaximumDeckEntries = 150;

    /// <summary>
    /// Gets the most card copies in a deck.
    /// </summary>
    public const int MaximumDeckCopies = 500;

    /// <summary>
    /// Gets the most groups in one request.
    /// </summary>
    public const int MaximumGroups = 20;

    /// <summary>
    /// Gets the most per-turn rules in one request.
    /// </summary>
    public const int MaximumRules = 20;

    /// <summary>
    /// Gets the most conditions in one request.
    /// </summary>
    public const int MaximumConditions = 20;

    /// <summary>
    /// Gets the most checks, counting those inside <c>all</c> and <c>any</c>, in one condition or keep rule.
    /// </summary>
    public const int MaximumChecksPerCondition = 20;

    /// <summary>
    /// Gets the deepest nesting of <c>all</c> and <c>any</c>.
    /// </summary>
    public const int MaximumCheckDepth = 4;

    /// <summary>
    /// Gets the largest opening hand.
    /// </summary>
    public const int MaximumOpeningHand = 15;

    /// <summary>
    /// Gets the most mulligans a request may allow.
    /// </summary>
    public const int MaximumMulligans = 10;

    /// <summary>
    /// Gets the largest value of a small count such as <c>maxPerTurn</c>, <c>cost</c>, <c>cards</c>, or
    /// <c>addsMana</c>, and of the numbers in a check.
    /// </summary>
    public const int MaximumSmallCount = 99;
}

/// <summary>
/// Names one deck entry and its number of copies.
/// </summary>
public sealed record SimulationDeckEntry(Guid EntryId, int Quantity);

/// <summary>
/// Gives the colors of the listed entries of a group when they differ from the group default.
/// </summary>
public sealed record SimulationProducesOverride(IReadOnlyList<Guid> EntryIds, IReadOnlyList<string> Colors);

/// <summary>
/// Says which colors each card of a group can add. Colors are W, U, B, R, G, C, or <c>any</c> (W, U, B, R, or G).
/// </summary>
public sealed record SimulationProduces(
    IReadOnlyList<string> Default,
    IReadOnlyList<SimulationProducesOverride> ByEntry);

/// <summary>
/// Declares one group of deck entries that rules and checks refer to by ID.
/// </summary>
/// <param name="Id">The unique group name.</param>
/// <param name="EntryIds">The entries in the group, in the order the caller gave them.</param>
/// <param name="Listed">True when the caller listed the entries, so <c>listed-order</c> may use that order.</param>
/// <param name="Produces">The colors the cards of the group add, or null for generic mana only.</param>
public sealed record SimulationGroup(
    string Id,
    IReadOnlyList<Guid> EntryIds,
    bool Listed,
    SimulationProduces? Produces);

/// <summary>
/// Overrides when the mana of the listed entries becomes usable.
/// </summary>
public sealed record SimulationManaFromOverride(IReadOnlyList<Guid> EntryIds, SimulationManaFrom ManaFrom);

/// <summary>
/// Moves a card of a group from hand to play at no mana cost.
/// </summary>
/// <param name="Group">The group to take cards from.</param>
/// <param name="Pick">How the card is chosen.</param>
/// <param name="MaxPerTurn">The most cards the rule moves each turn.</param>
/// <param name="AddsMana">How many units of mana each card adds.</param>
/// <param name="ManaFrom">When that mana becomes usable.</param>
/// <param name="ManaFromByEntry">Entries whose mana becomes usable at another time.</param>
/// <param name="TargetCost">The mana cost <c>target-first</c> ranks cards by; required for that pick only.</param>
public sealed record SimulationPlay(
    string Group,
    SimulationPick Pick,
    int MaxPerTurn,
    int AddsMana,
    SimulationManaFrom ManaFrom,
    IReadOnlyList<SimulationManaFromOverride> ManaFromByEntry,
    string? TargetCost);

/// <summary>
/// Spends generic mana to move a card of a group from hand to play.
/// </summary>
/// <param name="Group">The group to take cards from.</param>
/// <param name="Pick">How the card is chosen.</param>
/// <param name="MaxPerTurn">The most cards the rule moves each turn.</param>
/// <param name="Cost">The generic mana spent for each card.</param>
/// <param name="AddsMana">How many units of mana each card adds.</param>
/// <param name="ManaFrom">When that mana becomes usable.</param>
/// <param name="ManaFromByEntry">Entries whose mana becomes usable at another time.</param>
public sealed record SimulationCastIfAble(
    string Group,
    SimulationPick Pick,
    int MaxPerTurn,
    int Cost,
    int AddsMana,
    SimulationManaFrom ManaFrom,
    IReadOnlyList<SimulationManaFromOverride> ManaFromByEntry);

/// <summary>
/// Spends generic mana to cast a card of a group that draws cards.
/// </summary>
/// <param name="Group">The group to take cards from.</param>
/// <param name="Pick">How the card is chosen.</param>
/// <param name="MaxPerTurn">The most cards the rule casts each turn.</param>
/// <param name="Cost">The generic mana spent for each card.</param>
/// <param name="Cards">How many cards each cast draws now, and at each later draw step for an each-turn card.</param>
/// <param name="Effect">Whether the card leaves play or stays and draws again every turn.</param>
public sealed record SimulationDrawCards(
    string Group,
    SimulationPick Pick,
    int MaxPerTurn,
    int Cost,
    int Cards,
    SimulationDrawEffect Effect);

/// <summary>
/// One per-turn rule: <c>play</c>, <c>cast-if-able</c>, or <c>draw-cards</c>.
/// </summary>
public readonly union SimulationRule(SimulationPlay, SimulationCastIfAble, SimulationDrawCards);

/// <summary>
/// Passes when the hand holds a number of cards of a group within a range.
/// </summary>
public sealed record SimulationCountBetween(string Group, int Min, int Max);

/// <summary>
/// Passes when every listed check passes.
/// </summary>
public sealed record SimulationAll(IReadOnlyList<SimulationCheck> Checks);

/// <summary>
/// Passes when at least one listed check passes.
/// </summary>
public sealed record SimulationAny(IReadOnlyList<SimulationCheck> Checks);

/// <summary>
/// Passes when at least this much mana is still unspent on a turn, after that turn's rules.
/// </summary>
public sealed record SimulationManaAtLeast(int Amount, int Turn);

/// <summary>
/// Passes when at least this many cards of a group have been seen by a turn.
/// </summary>
public sealed record SimulationDrawnAtLeast(string Group, int Count, int ByTurn);

/// <summary>
/// Passes when at least this many cards of a group are in play by a turn.
/// </summary>
public sealed record SimulationOnBattlefieldAtLeast(string Group, int Count, int ByTurn);

/// <summary>
/// Passes when the available mana can pay a mana cost together with all mana the rules spent that turn.
/// </summary>
/// <param name="ManaCost">A Scryfall mana cost string such as <c>{2}{B}{G}</c>.</param>
/// <param name="Turn">The one turn to check, or null when <paramref name="ByTurn"/> is set.</param>
/// <param name="ByTurn">Passes if any turn up to this one passes, or null when <paramref name="Turn"/> is set.</param>
/// <param name="InHand">An entry that must also be in hand on the checked turn, or null.</param>
public sealed record SimulationCanCast(string ManaCost, int? Turn, int? ByTurn, Guid? InHand);

/// <summary>
/// One check: the keep rule of a mulligan uses <c>count-between</c>, <c>all</c>, and <c>any</c>; a condition uses
/// every kind except <c>count-between</c>.
/// </summary>
public readonly union SimulationCheck(
    SimulationCountBetween,
    SimulationAll,
    SimulationAny,
    SimulationManaAtLeast,
    SimulationDrawnAtLeast,
    SimulationOnBattlefieldAtLeast,
    SimulationCanCast);

/// <summary>
/// Names one check whose success rate the result reports.
/// </summary>
public sealed record SimulationCondition(string Id, SimulationCheck Check);

/// <summary>
/// Describes the mulligan: draw again while the keep rule fails, up to a limit.
/// </summary>
/// <param name="MaxMulligans">The most mulligans; the last hand is kept at the limit.</param>
/// <param name="FreeFirstMulligan">True when the first mulligan puts no card on the bottom.</param>
/// <param name="KeepWhen">The rule a hand must pass to be kept.</param>
/// <param name="PreferGroups">Groups to bottom cards from first, in order; others are bottomed at random.</param>
public sealed record SimulationMulligan(
    int MaxMulligans,
    bool FreeFirstMulligan,
    SimulationCheck KeepWhen,
    IReadOnlyList<string> PreferGroups);

/// <summary>
/// Describes the opening hand.
/// </summary>
/// <param name="Size">The number of cards drawn.</param>
/// <param name="OnThePlay">True skips the draw on turn 1.</param>
/// <param name="Mulligan">The mulligan, or null to keep the first hand.</param>
public sealed record SimulationOpeningHand(int Size, bool OnThePlay, SimulationMulligan? Mulligan);

/// <summary>
/// Holds one complete, resolved sampling request.
/// </summary>
/// <param name="Deck">The deck entries in the order that fixes each seed's meaning.</param>
/// <param name="Groups">The declared groups.</param>
/// <param name="OpeningHand">The opening hand and mulligan.</param>
/// <param name="EachTurn">The rules applied each turn, in order.</param>
/// <param name="Turns">The last turn to model.</param>
/// <param name="Conditions">The checks whose success rates are reported.</param>
/// <param name="Samples">The number of games to sample.</param>
/// <param name="SampleHands">How many of the first samples to return in full.</param>
/// <param name="Seed">The seed of the random generator.</param>
/// <param name="Context">Text that identifies the deck revision, included in the request fingerprint.</param>
public sealed record SimulationRequest(
    IReadOnlyList<SimulationDeckEntry> Deck,
    IReadOnlyList<SimulationGroup> Groups,
    SimulationOpeningHand OpeningHand,
    IReadOnlyList<SimulationRule> EachTurn,
    int Turns,
    IReadOnlyList<SimulationCondition> Conditions,
    int Samples,
    int SampleHands,
    ulong Seed,
    string Context);

/// <summary>
/// Reports how often one condition held, with a 95 percent Wilson interval.
/// </summary>
public sealed record SimulationConditionResult(
    string Id,
    int Hits,
    double Rate,
    double WilsonLower,
    double WilsonUpper);

/// <summary>
/// Counts how mulligan hands were kept.
/// </summary>
public sealed record SimulationMulliganCounts(int KeptByRule, int KeptAtLimit);

/// <summary>
/// Lists the cards of one hand drawn for the opening hand, including hands that were mulliganed.
/// </summary>
public sealed record SimulationHandDraw(int Mulligan, IReadOnlyList<Guid> EntryIds);

/// <summary>
/// Records that a rule moved one card.
/// </summary>
public sealed record SimulationMove(int Rule, Guid EntryId);

/// <summary>
/// Records what happened on one turn of a returned sample.
/// </summary>
public sealed record SimulationTurnRecord(
    int Turn,
    IReadOnlyList<Guid> Drawn,
    IReadOnlyList<SimulationMove> Moved,
    int ManaAvailable,
    int ManaSpent);

/// <summary>
/// Records one sampled game in full.
/// </summary>
public sealed record SimulationSampleRecord(
    int Sample,
    IReadOnlyList<SimulationHandDraw> Hands,
    IReadOnlyList<Guid> Kept,
    IReadOnlyList<Guid> Bottomed,
    IReadOnlyList<SimulationTurnRecord> Turns);

/// <summary>
/// Holds the complete result of one run.
/// </summary>
public sealed record SimulationReport(
    string ModelVersion,
    string RandomVersion,
    string Seed,
    int Samples,
    string Fingerprint,
    IReadOnlyList<SimulationConditionResult> Results,
    SimulationMulliganCounts? Mulligans,
    int LibraryEmpty,
    IReadOnlyList<SimulationSampleRecord> SampleHands,
    IReadOnlyList<string> Limits);
