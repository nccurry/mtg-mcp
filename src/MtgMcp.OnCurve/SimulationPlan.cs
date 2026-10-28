namespace MtgMcp.OnCurve;

/// <summary>
/// Names the kind of a compiled per-turn rule.
/// </summary>
internal enum SimulationRuleKind
{
    /// <summary>
    /// Moves a card to play at no cost.
    /// </summary>
    Play,

    /// <summary>
    /// Spends mana to move a card to play.
    /// </summary>
    CastIfAble,

    /// <summary>
    /// Spends mana to cast a card that draws.
    /// </summary>
    DrawCards,
}

/// <summary>
/// Names the shape of a compiled check node.
/// </summary>
internal enum SimulationCheckKind
{
    /// <summary>
    /// Counts cards of a group in hand.
    /// </summary>
    CountBetween,

    /// <summary>
    /// Passes when every child passes.
    /// </summary>
    All,

    /// <summary>
    /// Passes when any child passes.
    /// </summary>
    Any,

    /// <summary>
    /// Reads the result of one leaf check evaluated during the turns.
    /// </summary>
    Leaf,
}

/// <summary>
/// Names the kind of a leaf check that is evaluated at the end of a turn.
/// </summary>
internal enum SimulationLeafKind
{
    /// <summary>
    /// Unspent mana on one turn.
    /// </summary>
    ManaAtLeast,

    /// <summary>
    /// Cards of a group seen.
    /// </summary>
    DrawnAtLeast,

    /// <summary>
    /// Cards of a group in play.
    /// </summary>
    OnBattlefieldAtLeast,

    /// <summary>
    /// A mana cost that the available mana can pay.
    /// </summary>
    CanCast,
}

/// <summary>
/// Holds one compiled per-turn rule with its group and entries resolved to indexes.
/// </summary>
/// <param name="Kind">The kind of rule.</param>
/// <param name="Group">Index of the group the rule takes cards from.</param>
/// <param name="Pick">How the card is chosen.</param>
/// <param name="MaxPerTurn">The most cards moved each turn.</param>
/// <param name="Cost">Generic mana spent for each card.</param>
/// <param name="AddsMana">Units of mana each card adds.</param>
/// <param name="Cards">Cards drawn by a draw rule.</param>
/// <param name="Effect">What a draw rule's card does afterward.</param>
/// <param name="SameTurn">For each entry index, true when its mana is usable on the turn it enters play.</param>
/// <param name="TargetCost">The cost a <c>target-first</c> pick ranks by.</param>
internal sealed record SimulationRulePlan(
    SimulationRuleKind Kind,
    int Group,
    SimulationPick Pick,
    int MaxPerTurn,
    int Cost,
    int AddsMana,
    int Cards,
    SimulationDrawEffect Effect,
    bool[] SameTurn,
    OnCurveManaCost? TargetCost);

/// <summary>
/// Holds one compiled leaf check and the turns on which it is evaluated.
/// </summary>
/// <param name="Kind">The kind of leaf.</param>
/// <param name="Group">Index of the group, when the leaf counts a group.</param>
/// <param name="Count">The count or amount to reach.</param>
/// <param name="FirstTurn">The first turn on which the leaf is evaluated.</param>
/// <param name="LastTurn">The last turn on which the leaf is evaluated.</param>
/// <param name="Cost">The cost a <c>can-cast</c> leaf pays.</param>
/// <param name="InHandEntry">Index of the entry that must be in hand, or -1.</param>
internal sealed record SimulationLeafPlan(
    SimulationLeafKind Kind,
    int Group,
    int Count,
    int FirstTurn,
    int LastTurn,
    OnCurveManaCost? Cost,
    int InHandEntry);

/// <summary>
/// Holds one node of a compiled check tree.
/// </summary>
/// <param name="Kind">The shape of the node.</param>
/// <param name="Children">The children of an <c>all</c> or <c>any</c> node.</param>
/// <param name="Group">Index of the group of a count node.</param>
/// <param name="Min">The least count of a count node.</param>
/// <param name="Max">The greatest count of a count node.</param>
/// <param name="Leaf">Index of the leaf a leaf node reads.</param>
internal sealed record SimulationCheckNode(
    SimulationCheckKind Kind,
    IReadOnlyList<SimulationCheckNode> Children,
    int Group,
    int Min,
    int Max,
    int Leaf);

/// <summary>
/// Holds a compiled mulligan.
/// </summary>
/// <param name="MaxMulligans">The most mulligans.</param>
/// <param name="FreeFirst">True when the first mulligan bottoms no card.</param>
/// <param name="KeepWhen">The keep rule.</param>
/// <param name="PreferGroups">Group indexes to bottom from first.</param>
internal sealed record SimulationMulliganPlan(
    int MaxMulligans,
    bool FreeFirst,
    SimulationCheckNode KeepWhen,
    IReadOnlyList<int> PreferGroups);

/// <summary>
/// Holds a validated request compiled into arrays of indexes, so each sample runs without lookups by ID.
/// </summary>
/// <param name="Request">The request that was compiled.</param>
/// <param name="EntryIds">Entry ID of each entry index.</param>
/// <param name="OrdinalRank">Rank of each entry when entry IDs are sorted as text, for tie-breaks.</param>
/// <param name="CardEntry">Entry index of each physical card, in deck order.</param>
/// <param name="EntryGroup">Group index of each entry index, or -1.</param>
/// <param name="EntryRank">Position of each entry index inside its group's listed entries.</param>
/// <param name="EntrySource">The unit of mana each entry index adds, with its colors.</param>
/// <param name="GroupIds">Group ID of each group index.</param>
/// <param name="Rules">The compiled per-turn rules.</param>
/// <param name="Mulligan">The compiled mulligan, or null.</param>
/// <param name="ConditionIds">The condition IDs in request order.</param>
/// <param name="ConditionRoots">The compiled condition trees in request order.</param>
/// <param name="Leaves">The leaf checks of every condition.</param>
internal sealed record SimulationPlan(
    SimulationRequest Request,
    Guid[] EntryIds,
    int[] OrdinalRank,
    int[] CardEntry,
    int[] EntryGroup,
    int[] EntryRank,
    OnCurveActiveSource[] EntrySource,
    string[] GroupIds,
    SimulationRulePlan[] Rules,
    SimulationMulliganPlan? Mulligan,
    string[] ConditionIds,
    SimulationCheckNode[] ConditionRoots,
    SimulationLeafPlan[] Leaves);
