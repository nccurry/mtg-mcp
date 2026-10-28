using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve;

/// <summary>
/// Checks a resolved request against every fixed limit and engine rule, and compiles it into a plan. Every failure
/// names the JSON path of the value the caller can change.
/// </summary>
internal sealed class SimulationPlanner
{
    /// <summary>
    /// Lists the color names a group may declare.
    /// </summary>
    private static readonly string[] ColorNames = ["W", "U", "B", "R", "G", "C", "any"];

    /// <summary>
    /// Holds the request being checked.
    /// </summary>
    private readonly SimulationRequest request;

    /// <summary>
    /// Maps each entry ID to its index in the deck.
    /// </summary>
    private readonly Dictionary<Guid, int> entryIndex = [];

    /// <summary>
    /// Maps each group ID to its index.
    /// </summary>
    private readonly Dictionary<string, int> groupIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// Holds the group index of each entry, or -1.
    /// </summary>
    private int[] entryGroup = [];

    /// <summary>
    /// Holds the position of each entry inside its group's listed entries.
    /// </summary>
    private int[] entryRank = [];

    /// <summary>
    /// Holds the unit of mana each entry adds.
    /// </summary>
    private OnCurveActiveSource[] entrySource = [];

    /// <summary>
    /// Collects the leaf checks of every condition.
    /// </summary>
    private readonly List<SimulationLeafPlan> leaves = [];

    /// <summary>
    /// Creates a planner for one request.
    /// </summary>
    private SimulationPlanner(SimulationRequest request)
    {
        this.request = request;
    }

    /// <summary>
    /// Checks and compiles a request, or returns the first problem found.
    /// </summary>
    internal static OperationResult<SimulationPlan> Plan(SimulationRequest? request)
    {
        if (request is null)
        {
            return Invalid("invalid-simulation-value", "The request is missing.", "$");
        }

        return new SimulationPlanner(request).Build();
    }

    /// <summary>
    /// Runs every check in a fixed order, so the same bad request always gets the same message.
    /// </summary>
    private OperationResult<SimulationPlan> Build()
    {
        OperationInvalidInput? problem = CheckSizes() ?? CheckDeck() ?? CheckWork() ?? CheckGroups();
        if (problem is not null)
        {
            return problem;
        }

        OperationResult<SimulationMulliganPlan?> mulligan = CompileMulligan();
        if (mulligan is not OperationSuccess<SimulationMulliganPlan?> mulliganPlan)
        {
            return OperationFailure.Forward<SimulationMulliganPlan?, SimulationPlan>(mulligan);
        }

        OperationResult<SimulationRulePlan[]> rules = CompileRules();
        if (rules is not OperationSuccess<SimulationRulePlan[]> rulePlans)
        {
            return OperationFailure.Forward<SimulationRulePlan[], SimulationPlan>(rules);
        }

        OperationResult<SimulationCheckNode[]> conditions = CompileConditions();
        if (conditions is not OperationSuccess<SimulationCheckNode[]> roots)
        {
            return OperationFailure.Forward<SimulationCheckNode[], SimulationPlan>(conditions);
        }

        OperationInvalidInput? budget = CheckOperations(rulePlans.Data);
        if (budget is not null)
        {
            return budget;
        }

        return new OperationSuccess<SimulationPlan>(new SimulationPlan(
            request,
            [.. request.Deck.Select(entry => entry.EntryId)],
            OrdinalRanks(),
            CardEntries(),
            entryGroup,
            entryRank,
            entrySource,
            [.. request.Groups.Select(group => group.Id)],
            rulePlans.Data,
            mulliganPlan.Data,
            [.. request.Conditions.Select(condition => condition.Id)],
            roots.Data,
            [.. leaves]));
    }

    /// <summary>
    /// Checks the numbers that bound the work, so the later arithmetic cannot overflow.
    /// </summary>
    private OperationInvalidInput? CheckSizes()
    {
        if (request.Samples is < 1 or > SimulationLimits.MaximumSamples)
        {
            return Value($"samples must be from 1 to {SimulationLimits.MaximumSamples}.", "samples");
        }

        if (request.SampleHands < 0 || request.SampleHands > SimulationLimits.MaximumSampleHands ||
            request.SampleHands > request.Samples)
        {
            return Value(
                $"sampleHands must be from 0 to {SimulationLimits.MaximumSampleHands}, and no more than samples.",
                "sampleHands");
        }

        if (request.Turns is < 1 or > SimulationLimits.MaximumTurns)
        {
            return Value($"turns must be from 1 to {SimulationLimits.MaximumTurns}.", "turns");
        }

        if (request.Groups.Count > SimulationLimits.MaximumGroups)
        {
            return Value($"At most {SimulationLimits.MaximumGroups} groups are allowed.", "groups");
        }

        if (request.EachTurn.Count > SimulationLimits.MaximumRules)
        {
            return Value($"At most {SimulationLimits.MaximumRules} eachTurn rules are allowed.", "eachTurn");
        }

        if (request.Conditions.Count == 0 || request.Conditions.Count > SimulationLimits.MaximumConditions)
        {
            return Value($"conditions needs 1 to {SimulationLimits.MaximumConditions} conditions.", "conditions");
        }

        return null;
    }

    /// <summary>
    /// Checks the deck entries and the opening-hand size against the deck.
    /// </summary>
    private OperationInvalidInput? CheckDeck()
    {
        if (request.Deck.Count < 1 || request.Deck.Count > SimulationLimits.MaximumDeckEntries)
        {
            return Value($"The deck needs 1 to {SimulationLimits.MaximumDeckEntries} entries.", "deck");
        }

        int copies = 0;
        for (int index = 0; index < request.Deck.Count; index++)
        {
            SimulationDeckEntry entry = request.Deck[index];
            if (entry.EntryId == Guid.Empty || !entryIndex.TryAdd(entry.EntryId, index) || entry.Quantity < 1 ||
                entry.Quantity > SimulationLimits.MaximumDeckCopies - copies)
            {
                return Value(
                    $"Deck entries need unique IDs and positive quantities, with at most " +
                    $"{SimulationLimits.MaximumDeckCopies} cards in all.",
                    "deck");
            }

            copies += entry.Quantity;
        }

        if (request.OpeningHand.Size < 1 || request.OpeningHand.Size > SimulationLimits.MaximumOpeningHand ||
            request.OpeningHand.Size > copies)
        {
            return Value(
                $"openingHand.size must be from 1 to {SimulationLimits.MaximumOpeningHand}, and no more than the " +
                $"{copies} cards in the deck.",
                "openingHand.size");
        }

        return null;
    }

    /// <summary>
    /// Refuses a request whose samples times turns times deck cards is over the work limit.
    /// </summary>
    private OperationInvalidInput? CheckWork()
    {
        long copies = request.Deck.Sum(entry => (long)entry.Quantity);
        long work = (long)request.Samples * request.Turns * copies;
        if (work <= SimulationLimits.MaximumWork)
        {
            return null;
        }

        return Invalid(
            "simulation-too-large",
            $"samples x turns x deck cards is {work} " +
            $"({request.Samples} x {request.Turns} x {copies}), over the limit of " +
            $"{SimulationLimits.MaximumWork}. " +
            "Lower samples or turns.",
            "samples");
    }

    /// <summary>
    /// Refuses a request that could mean too much work per game even when samples times turns times deck cards is
    /// small: too many units of mana in play, or too many payment tests per turn. The estimate counts, for each
    /// turn, a scan of the hand for each rule and a payment test for each <c>can-cast</c> check and for each
    /// candidate of a <c>target-first</c> rule, each costing about symbols times units of mana.
    /// </summary>
    private OperationInvalidInput? CheckOperations(SimulationRulePlan[] rules)
    {
        long copies = request.Deck.Sum(entry => (long)entry.Quantity);
        long units = 0;
        foreach (SimulationRulePlan rule in rules)
        {
            if (rule.Kind != SimulationRuleKind.DrawCards)
            {
                units += Math.Min((long)rule.MaxPerTurn * request.Turns, GroupCards(rule.Group)) * rule.AddsMana;
            }
        }

        if (units > SimulationLimits.MaximumManaUnits)
        {
            return Invalid(
                "simulation-too-large",
                $"The rules could put {units} units of mana in play, over the limit of " +
                $"{SimulationLimits.MaximumManaUnits}. Lower maxPerTurn, addsMana, or turns.",
                "eachTurn");
        }

        long payment = units + 1;
        long perTurn = copies * (1 + rules.Length);
        foreach (SimulationLeafPlan leaf in leaves)
        {
            if (leaf.Kind == SimulationLeafKind.CanCast)
            {
                perTurn += (leaf.Cost!.SpecificSymbolCount + 1) * payment;
            }
        }

        foreach (SimulationRulePlan rule in rules)
        {
            if (rule.Pick == SimulationPick.TargetFirst)
            {
                perTurn += request.Groups[rule.Group].EntryIds.Count *
                    (rule.TargetCost!.SpecificSymbolCount + 1) * payment;
            }
        }

        long mulligans = (request.OpeningHand.Mulligan?.MaxMulligans ?? 0) + 1;
        long operations = request.Samples * ((request.Turns * perTurn) + (mulligans * copies));
        if (operations <= SimulationLimits.MaximumOperations)
        {
            return null;
        }

        return Invalid(
            "simulation-too-large",
            $"This request needs about {operations} elementary operations, over the limit of " +
            $"{SimulationLimits.MaximumOperations}. Lower samples or turns, or use fewer can-cast checks, " +
            "target-first rules, rules, or units of mana.",
            "samples");
    }

    /// <summary>
    /// Counts the cards of the deck that are in one group.
    /// </summary>
    private long GroupCards(int group)
    {
        long cards = 0;
        foreach (Guid id in request.Groups[group].EntryIds)
        {
            cards += request.Deck[entryIndex[id]].Quantity;
        }

        return cards;
    }

    /// <summary>
    /// Checks groups: unique IDs, entries in the deck, no overlap, and valid colors, then builds the per-entry data.
    /// </summary>
    private OperationInvalidInput? CheckGroups()
    {
        entryGroup = new int[request.Deck.Count];
        entryRank = new int[request.Deck.Count];
        Array.Fill(entryGroup, -1);
        List<IReadOnlyList<OnCurveManaSymbol>> symbols = [];
        for (int index = 0; index < request.Deck.Count; index++)
        {
            symbols.Add([]);
        }

        for (int index = 0; index < request.Groups.Count; index++)
        {
            SimulationGroup group = request.Groups[index];
            string path = $"groups[{index}]";
            if (string.IsNullOrWhiteSpace(group.Id) || group.Id.Length > SimulationLimits.MaximumIdLength ||
                !groupIndex.TryAdd(group.Id, index))
            {
                return Invalid(
                    "invalid-simulation-group",
                    $"Each group needs a unique id of 1 to {SimulationLimits.MaximumIdLength} characters.",
                    path + ".id");
            }

            OperationInvalidInput? problem = PlaceMembers(group, index, path);
            problem ??= ReadProduces(group, path, symbols);
            if (problem is not null)
            {
                return problem;
            }
        }

        entrySource = [.. symbols.Select(list => new OnCurveActiveSource(list))];
        return null;
    }

    /// <summary>
    /// Puts the entries of one group in the per-entry tables and refuses an entry that is in two groups.
    /// </summary>
    private OperationInvalidInput? PlaceMembers(SimulationGroup group, int index, string path)
    {
        for (int position = 0; position < group.EntryIds.Count; position++)
        {
            Guid id = group.EntryIds[position];
            if (!entryIndex.TryGetValue(id, out int entry))
            {
                return Invalid("unknown-entry-id", $"The deck has no entry {id:D}.", path + ".select");
            }

            if (entryGroup[entry] == index)
            {
                return Invalid(
                    "invalid-simulation-group",
                    $"Entry {id:D} is listed more than once in group {group.Id}.",
                    path + ".select");
            }

            if (entryGroup[entry] >= 0)
            {
                return Invalid(
                    "overlapping-groups",
                    $"Entry {id:D} is in both group {request.Groups[entryGroup[entry]].Id} and group " +
                    $"{group.Id}. Groups must not overlap.",
                    path + ".select");
            }

            entryGroup[entry] = index;
            entryRank[entry] = position;
        }

        return null;
    }

    /// <summary>
    /// Reads the colors a group's cards add and stores the unit of mana of each member.
    /// </summary>
    private OperationInvalidInput? ReadProduces(
        SimulationGroup group,
        string path,
        List<IReadOnlyList<OnCurveManaSymbol>> symbols)
    {
        if (group.Produces is null)
        {
            return null;
        }

        OperationInvalidInput? problem = CheckColors(group.Produces.Default, path + ".produces.default");
        if (problem is not null)
        {
            return problem;
        }

        IReadOnlyList<OnCurveManaSymbol> defaultSymbols = ToSymbols(group.Produces.Default);
        foreach (Guid id in group.EntryIds)
        {
            symbols[entryIndex[id]] = defaultSymbols;
        }

        HashSet<Guid> overridden = [];
        for (int position = 0; position < group.Produces.ByEntry.Count; position++)
        {
            SimulationProducesOverride value = group.Produces.ByEntry[position];
            string overridePath = $"{path}.produces.byEntry[{position}]";
            problem = CheckColors(value.Colors, overridePath + ".colors");
            if (problem is not null)
            {
                return problem;
            }

            foreach (Guid id in value.EntryIds)
            {
                if (!group.EntryIds.Contains(id) || !overridden.Add(id))
                {
                    return Invalid(
                        "invalid-simulation-group",
                        "Each entry ID in byEntry must be one of this group's entries and appear only once.",
                        overridePath + ".entryIds");
                }

                symbols[entryIndex[id]] = ToSymbols(value.Colors);
            }
        }

        return null;
    }

    /// <summary>
    /// Compiles the mulligan, or returns null when the request keeps its first hand.
    /// </summary>
    private OperationResult<SimulationMulliganPlan?> CompileMulligan()
    {
        SimulationMulligan? mulligan = request.OpeningHand.Mulligan;
        if (mulligan is null)
        {
            return new OperationSuccess<SimulationMulliganPlan?>(null);
        }

        const string path = "openingHand.mulligan";
        int bottomsAtLimit = mulligan.FreeFirstMulligan
            ? Math.Max(0, mulligan.MaxMulligans - 1)
            : mulligan.MaxMulligans;
        if (mulligan.MaxMulligans is < 0 or > SimulationLimits.MaximumMulligans ||
            request.OpeningHand.Size - bottomsAtLimit < 1)
        {
            return Value(
                $"maxMulligans must be from 0 to {SimulationLimits.MaximumMulligans}, and must leave at least one " +
                "card in the kept hand.",
                path + ".maxMulligans");
        }

        List<int> prefer = [];
        for (int position = 0; position < mulligan.PreferGroups.Count; position++)
        {
            if (!groupIndex.TryGetValue(mulligan.PreferGroups[position], out int index))
            {
                return UnknownGroup(mulligan.PreferGroups[position], $"{path}.bottom.preferGroups[{position}]");
            }

            prefer.Add(index);
        }

        int nodeCount = 0;
        OperationResult<SimulationCheckNode> keep = CompileCheck(
            mulligan.KeepWhen,
            isKeep: true,
            path + ".keepWhen",
            1,
            ref nodeCount);
        if (keep is not OperationSuccess<SimulationCheckNode> keepNode)
        {
            return OperationFailure.Forward<SimulationCheckNode, SimulationMulliganPlan?>(keep);
        }

        return new OperationSuccess<SimulationMulliganPlan?>(new SimulationMulliganPlan(
            mulligan.MaxMulligans,
            mulligan.FreeFirstMulligan,
            keepNode.Data,
            prefer));
    }

    /// <summary>
    /// Compiles the per-turn rules in the order given.
    /// </summary>
    private OperationResult<SimulationRulePlan[]> CompileRules()
    {
        List<SimulationRulePlan> plans = [];
        for (int index = 0; index < request.EachTurn.Count; index++)
        {
            string path = $"eachTurn[{index}]";
            OperationResult<SimulationRulePlan> plan = request.EachTurn[index].Value switch
            {
                SimulationPlay play => CompilePlay(play, path),
                SimulationCastIfAble cast => CompileCast(cast, path),
                SimulationDrawCards draw => CompileDraw(draw, path),
                _ => throw new InvalidOperationException("The rule has no active case."),
            };
            if (plan is not OperationSuccess<SimulationRulePlan> success)
            {
                return OperationFailure.Forward<SimulationRulePlan, SimulationRulePlan[]>(plan);
            }

            plans.Add(success.Data);
        }

        return new OperationSuccess<SimulationRulePlan[]>([.. plans]);
    }

    /// <summary>
    /// Compiles a <c>play</c> rule.
    /// </summary>
    private OperationResult<SimulationRulePlan> CompilePlay(SimulationPlay play, string path)
    {
        OperationInvalidInput? problem = CheckRuleNumbers(play.MaxPerTurn, play.AddsMana, 0, path, hasCost: false);
        if (problem is not null)
        {
            return problem;
        }

        OperationResult<int> group = FindGroup(play.Group, path + ".group");
        if (group is not OperationSuccess<int> groupFound)
        {
            return OperationFailure.Forward<int, SimulationRulePlan>(group);
        }

        problem = CheckPick(play.Pick, groupFound.Data, path, allowTarget: true);
        if (problem is not null)
        {
            return problem;
        }

        OnCurveManaCost? target = null;
        if (play.Pick == SimulationPick.TargetFirst)
        {
            if (string.IsNullOrWhiteSpace(play.TargetCost))
            {
                return Invalid(
                    "missing-field",
                    "targetCost is required with pick target-first.",
                    path + ".targetCost");
            }

            OperationResult<OnCurveManaCost> cost = ParseCost(play.TargetCost, path + ".targetCost");
            if (cost is not OperationSuccess<OnCurveManaCost> parsed)
            {
                return OperationFailure.Forward<OnCurveManaCost, SimulationRulePlan>(cost);
            }

            target = parsed.Data;
        }
        else if (play.TargetCost is not null)
        {
            return Invalid(
                "field-not-allowed",
                "targetCost is only for pick target-first.",
                path + ".targetCost");
        }

        OperationResult<bool[]> sameTurn = ReadManaFrom(play.ManaFrom, play.ManaFromByEntry, groupFound.Data, path);
        if (sameTurn is not OperationSuccess<bool[]> times)
        {
            return OperationFailure.Forward<bool[], SimulationRulePlan>(sameTurn);
        }

        return new OperationSuccess<SimulationRulePlan>(new SimulationRulePlan(
            SimulationRuleKind.Play,
            groupFound.Data,
            play.Pick,
            play.MaxPerTurn,
            0,
            play.AddsMana,
            0,
            SimulationDrawEffect.Once,
            times.Data,
            target));
    }

    /// <summary>
    /// Compiles a <c>cast-if-able</c> rule.
    /// </summary>
    private OperationResult<SimulationRulePlan> CompileCast(SimulationCastIfAble cast, string path)
    {
        OperationInvalidInput? problem = CheckRuleNumbers(
            cast.MaxPerTurn,
            cast.AddsMana,
            cast.Cost,
            path,
            hasCost: true);
        if (problem is not null)
        {
            return problem;
        }

        OperationResult<int> group = FindGroup(cast.Group, path + ".group");
        if (group is not OperationSuccess<int> groupFound)
        {
            return OperationFailure.Forward<int, SimulationRulePlan>(group);
        }

        problem = CheckPick(cast.Pick, groupFound.Data, path, allowTarget: false);
        if (problem is not null)
        {
            return problem;
        }

        OperationResult<bool[]> sameTurn = ReadManaFrom(cast.ManaFrom, cast.ManaFromByEntry, groupFound.Data, path);
        if (sameTurn is not OperationSuccess<bool[]> times)
        {
            return OperationFailure.Forward<bool[], SimulationRulePlan>(sameTurn);
        }

        return new OperationSuccess<SimulationRulePlan>(new SimulationRulePlan(
            SimulationRuleKind.CastIfAble,
            groupFound.Data,
            cast.Pick,
            cast.MaxPerTurn,
            cast.Cost,
            cast.AddsMana,
            0,
            SimulationDrawEffect.Once,
            times.Data,
            null));
    }

    /// <summary>
    /// Compiles a <c>draw-cards</c> rule.
    /// </summary>
    private OperationResult<SimulationRulePlan> CompileDraw(SimulationDrawCards draw, string path)
    {
        OperationInvalidInput? problem = CheckRuleNumbers(draw.MaxPerTurn, 0, draw.Cost, path, hasCost: true);
        if (problem is null && (draw.Cards < 1 || draw.Cards > SimulationLimits.MaximumSmallCount))
        {
            problem = Value($"cards must be from 1 to {SimulationLimits.MaximumSmallCount}.", path + ".cards");
        }

        if (problem is null && !Enum.IsDefined(draw.Effect))
        {
            problem = Value("effect must be once or each-turn.", path + ".effect");
        }

        if (problem is not null)
        {
            return problem;
        }

        OperationResult<int> group = FindGroup(draw.Group, path + ".group");
        if (group is not OperationSuccess<int> groupFound)
        {
            return OperationFailure.Forward<int, SimulationRulePlan>(group);
        }

        problem = CheckPick(draw.Pick, groupFound.Data, path, allowTarget: false);
        if (problem is not null)
        {
            return problem;
        }

        return new OperationSuccess<SimulationRulePlan>(new SimulationRulePlan(
            SimulationRuleKind.DrawCards,
            groupFound.Data,
            draw.Pick,
            draw.MaxPerTurn,
            draw.Cost,
            0,
            draw.Cards,
            draw.Effect,
            new bool[request.Deck.Count],
            null));
    }

    /// <summary>
    /// Checks the small counts every rule has.
    /// </summary>
    private static OperationInvalidInput? CheckRuleNumbers(
        int maxPerTurn,
        int addsMana,
        int cost,
        string path,
        bool hasCost)
    {
        if (maxPerTurn < 1 || maxPerTurn > SimulationLimits.MaximumSmallCount)
        {
            return Value($"maxPerTurn must be from 1 to {SimulationLimits.MaximumSmallCount}.", path + ".maxPerTurn");
        }

        if (addsMana < 0 || addsMana > SimulationLimits.MaximumSmallCount)
        {
            return Value($"addsMana must be from 0 to {SimulationLimits.MaximumSmallCount}.", path + ".addsMana");
        }

        if (hasCost && (cost < 0 || cost > SimulationLimits.MaximumSmallCount))
        {
            return Value($"cost must be from 0 to {SimulationLimits.MaximumSmallCount}.", path + ".cost");
        }

        return null;
    }

    /// <summary>
    /// Checks the pick against the group and the rule kind.
    /// </summary>
    private OperationInvalidInput? CheckPick(SimulationPick pick, int group, string path, bool allowTarget)
    {
        if (!Enum.IsDefined(pick))
        {
            return Value("pick must be random, listed-order, or target-first.", path + ".pick");
        }

        if (pick == SimulationPick.ListedOrder && !request.Groups[group].Listed)
        {
            return Invalid(
                "invalid-simulation-rule",
                "pick listed-order needs a group chosen with an entry-ids select, because only that select has an " +
                "order.",
                path + ".pick");
        }

        if (pick == SimulationPick.TargetFirst && !allowTarget)
        {
            return Invalid(
                "invalid-simulation-rule",
                "pick target-first is only for play rules.",
                path + ".pick");
        }

        return null;
    }

    /// <summary>
    /// Builds, for each entry, whether its mana is usable on the turn it enters play.
    /// </summary>
    private OperationResult<bool[]> ReadManaFrom(
        SimulationManaFrom manaFrom,
        IReadOnlyList<SimulationManaFromOverride> overrides,
        int group,
        string path)
    {
        if (!Enum.IsDefined(manaFrom))
        {
            return Value("manaFrom must be same-turn or next-turn.", path + ".manaFrom");
        }

        bool[] sameTurn = new bool[request.Deck.Count];
        for (int entry = 0; entry < sameTurn.Length; entry++)
        {
            sameTurn[entry] = manaFrom == SimulationManaFrom.SameTurn;
        }

        HashSet<Guid> seen = [];
        for (int position = 0; position < overrides.Count; position++)
        {
            SimulationManaFromOverride value = overrides[position];
            if (!Enum.IsDefined(value.ManaFrom))
            {
                return Value(
                    "manaFrom must be same-turn or next-turn.",
                    $"{path}.manaFromByEntry[{position}].manaFrom");
            }

            foreach (Guid id in value.EntryIds)
            {
                if (!entryIndex.TryGetValue(id, out int entry) || entryGroup[entry] != group || !seen.Add(id))
                {
                    return Invalid(
                        "invalid-simulation-rule",
                        "Each entry ID in manaFromByEntry must be an entry of the rule's group and appear only once.",
                        $"{path}.manaFromByEntry[{position}].entryIds");
                }

                sameTurn[entry] = value.ManaFrom == SimulationManaFrom.SameTurn;
            }
        }

        return new OperationSuccess<bool[]>(sameTurn);
    }

    /// <summary>
    /// Compiles every condition and its check tree.
    /// </summary>
    private OperationResult<SimulationCheckNode[]> CompileConditions()
    {
        List<SimulationCheckNode> roots = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int index = 0; index < request.Conditions.Count; index++)
        {
            SimulationCondition condition = request.Conditions[index];
            string path = $"conditions[{index}]";
            if (string.IsNullOrWhiteSpace(condition.Id) || condition.Id.Length > SimulationLimits.MaximumIdLength ||
                !ids.Add(condition.Id))
            {
                return Invalid(
                    "invalid-simulation-check",
                    $"Each condition needs a unique id of 1 to {SimulationLimits.MaximumIdLength} characters.",
                    path + ".id");
            }

            int nodeCount = 0;
            OperationResult<SimulationCheckNode> root = CompileCheck(
                condition.Check,
                isKeep: false,
                path + ".check",
                1,
                ref nodeCount);
            if (root is not OperationSuccess<SimulationCheckNode> node)
            {
                return OperationFailure.Forward<SimulationCheckNode, SimulationCheckNode[]>(root);
            }

            roots.Add(node.Data);
        }

        return new OperationSuccess<SimulationCheckNode[]>([.. roots]);
    }

    /// <summary>
    /// Compiles one check and its children.
    /// </summary>
    private OperationResult<SimulationCheckNode> CompileCheck(
        SimulationCheck check,
        bool isKeep,
        string path,
        int depth,
        ref int nodeCount)
    {
        nodeCount++;
        if (nodeCount > SimulationLimits.MaximumChecksPerCondition)
        {
            return Invalid(
                "invalid-simulation-check",
                $"A condition or keep rule holds at most {SimulationLimits.MaximumChecksPerCondition} checks.",
                path);
        }

        switch (check.Value)
        {
            case SimulationCountBetween count:
                return CompileCount(count, isKeep, path);
            case SimulationAll all:
                return CompileList(all.Checks, SimulationCheckKind.All, isKeep, path, depth, ref nodeCount);
            case SimulationAny any:
                return CompileList(any.Checks, SimulationCheckKind.Any, isKeep, path, depth, ref nodeCount);
            default:
                return isKeep
                    ? Invalid(
                        "invalid-simulation-check",
                        "keepWhen accepts only count-between, all, and any.",
                        path)
                    : CompileLeaf(check, path);
        }
    }

    /// <summary>
    /// Compiles a <c>count-between</c> check, which only a keep rule uses.
    /// </summary>
    private OperationResult<SimulationCheckNode> CompileCount(SimulationCountBetween count, bool isKeep, string path)
    {
        if (!isKeep)
        {
            return Invalid(
                "invalid-simulation-check",
                "count-between is only for keepWhen. A condition checks drawn-at-least, on-battlefield-at-least, " +
                "mana-at-least, or can-cast.",
                path);
        }

        if (count.Min < 0 || count.Max < count.Min || count.Max > SimulationLimits.MaximumSmallCount)
        {
            return Value(
                $"min and max must satisfy 0 <= min <= max <= {SimulationLimits.MaximumSmallCount}.",
                path);
        }

        OperationResult<int> group = FindGroup(count.Group, path + ".group");
        if (group is not OperationSuccess<int> found)
        {
            return OperationFailure.Forward<int, SimulationCheckNode>(group);
        }

        return new OperationSuccess<SimulationCheckNode>(
            new SimulationCheckNode(SimulationCheckKind.CountBetween, [], found.Data, count.Min, count.Max, -1));
    }

    /// <summary>
    /// Compiles an <c>all</c> or <c>any</c> check and its children.
    /// </summary>
    private OperationResult<SimulationCheckNode> CompileList(
        IReadOnlyList<SimulationCheck> checks,
        SimulationCheckKind kind,
        bool isKeep,
        string path,
        int depth,
        ref int nodeCount)
    {
        if (checks.Count == 0 || depth > SimulationLimits.MaximumCheckDepth)
        {
            return Invalid(
                "invalid-simulation-check",
                $"all and any need at least one check and nest at most {SimulationLimits.MaximumCheckDepth} deep.",
                path);
        }

        List<SimulationCheckNode> children = [];
        for (int position = 0; position < checks.Count; position++)
        {
            OperationResult<SimulationCheckNode> child = CompileCheck(
                checks[position],
                isKeep,
                $"{path}.checks[{position}]",
                depth + 1,
                ref nodeCount);
            if (child is not OperationSuccess<SimulationCheckNode> node)
            {
                return child;
            }

            children.Add(node.Data);
        }

        return new OperationSuccess<SimulationCheckNode>(new SimulationCheckNode(kind, children, -1, 0, 0, -1));
    }

    /// <summary>
    /// Compiles a leaf check of a condition.
    /// </summary>
    private OperationResult<SimulationCheckNode> CompileLeaf(SimulationCheck check, string path)
    {
        OperationResult<SimulationLeafPlan> leaf = check.Value switch
        {
            SimulationManaAtLeast mana => CompileManaAtLeast(mana, path),
            SimulationDrawnAtLeast drawn => CompileGroupCount(
                SimulationLeafKind.DrawnAtLeast,
                drawn.Group,
                drawn.Count,
                drawn.ByTurn,
                path),
            SimulationOnBattlefieldAtLeast field => CompileGroupCount(
                SimulationLeafKind.OnBattlefieldAtLeast,
                field.Group,
                field.Count,
                field.ByTurn,
                path),
            SimulationCanCast cast => CompileCanCast(cast, path),
            _ => throw new InvalidOperationException("The check has no active case."),
        };
        if (leaf is not OperationSuccess<SimulationLeafPlan> plan)
        {
            return OperationFailure.Forward<SimulationLeafPlan, SimulationCheckNode>(leaf);
        }

        leaves.Add(plan.Data);
        return new OperationSuccess<SimulationCheckNode>(
            new SimulationCheckNode(SimulationCheckKind.Leaf, [], -1, 0, 0, leaves.Count - 1));
    }

    /// <summary>
    /// Compiles a <c>mana-at-least</c> leaf, which is checked on one turn.
    /// </summary>
    private OperationResult<SimulationLeafPlan> CompileManaAtLeast(SimulationManaAtLeast mana, string path)
    {
        if (mana.Amount < 0 || mana.Amount > SimulationLimits.MaximumSmallCount)
        {
            return Value($"amount must be from 0 to {SimulationLimits.MaximumSmallCount}.", path + ".amount");
        }

        if (mana.Turn < 1 || mana.Turn > request.Turns)
        {
            return Value($"turn must be from 1 to turns ({request.Turns}).", path + ".turn");
        }

        return new OperationSuccess<SimulationLeafPlan>(new SimulationLeafPlan(
            SimulationLeafKind.ManaAtLeast,
            -1,
            mana.Amount,
            mana.Turn,
            mana.Turn,
            null,
            -1));
    }

    /// <summary>
    /// Compiles a <c>drawn-at-least</c> or <c>on-battlefield-at-least</c> leaf.
    /// </summary>
    private OperationResult<SimulationLeafPlan> CompileGroupCount(
        SimulationLeafKind kind,
        string groupId,
        int count,
        int byTurn,
        string path)
    {
        if (count < 1 || count > SimulationLimits.MaximumSmallCount)
        {
            return Value($"count must be from 1 to {SimulationLimits.MaximumSmallCount}.", path + ".count");
        }

        if (byTurn < 1 || byTurn > request.Turns)
        {
            return Value($"byTurn must be from 1 to turns ({request.Turns}).", path + ".byTurn");
        }

        OperationResult<int> group = FindGroup(groupId, path + ".group");
        if (group is not OperationSuccess<int> found)
        {
            return OperationFailure.Forward<int, SimulationLeafPlan>(group);
        }

        return new OperationSuccess<SimulationLeafPlan>(
            new SimulationLeafPlan(kind, found.Data, count, 1, byTurn, null, -1));
    }

    /// <summary>
    /// Compiles a <c>can-cast</c> leaf.
    /// </summary>
    private OperationResult<SimulationLeafPlan> CompileCanCast(SimulationCanCast cast, string path)
    {
        if ((cast.Turn is null) == (cast.ByTurn is null))
        {
            return Invalid(
                "invalid-simulation-check",
                "can-cast needs exactly one of turn or byTurn.",
                path);
        }

        int turn = cast.Turn ?? cast.ByTurn!.Value;
        if (turn < 1 || turn > request.Turns)
        {
            return Value(
                $"turn and byTurn must be from 1 to turns ({request.Turns}).",
                path + (cast.Turn is null ? ".byTurn" : ".turn"));
        }

        OperationResult<OnCurveManaCost> cost = ParseCost(cast.ManaCost, path + ".manaCost");
        if (cost is not OperationSuccess<OnCurveManaCost> parsed)
        {
            return OperationFailure.Forward<OnCurveManaCost, SimulationLeafPlan>(cost);
        }

        int inHand = -1;
        if (cast.InHand is Guid inHandId && !entryIndex.TryGetValue(inHandId, out inHand))
        {
            return Invalid("unknown-entry-id", $"The deck has no entry {inHandId:D}.", path + ".inHand");
        }

        return new OperationSuccess<SimulationLeafPlan>(new SimulationLeafPlan(
            SimulationLeafKind.CanCast,
            -1,
            0,
            cast.Turn is null ? 1 : turn,
            turn,
            parsed.Data,
            inHand));
    }

    /// <summary>
    /// Parses a mana cost and names the value when it cannot be used.
    /// </summary>
    private static OperationResult<OnCurveManaCost> ParseCost(string? text, string path)
    {
        OperationResult<OnCurveManaCost> cost = OnCurveManaSymbols.ReadCost(text);
        if (cost is OperationSuccess<OnCurveManaCost>)
        {
            return cost;
        }

        string message = cost.Value is OperationUnsupported unsupported ? unsupported.Message : "The cost is invalid.";
        return Invalid("invalid-simulation-check", message, path);
    }

    /// <summary>
    /// Finds the index of a group by ID.
    /// </summary>
    private OperationResult<int> FindGroup(string id, string path)
    {
        return id is not null && groupIndex.TryGetValue(id, out int index)
            ? new OperationSuccess<int>(index)
            : UnknownGroup(id, path);
    }

    /// <summary>
    /// Cuts text echoed in a message to a short length.
    /// </summary>
    private static string Shorten(string? text)
    {
        return text is { Length: > 32 } ? text[..32] + "..." : text ?? string.Empty;
    }

    /// <summary>
    /// Builds the failure for a group ID that no group declares.
    /// </summary>
    private static OperationInvalidInput UnknownGroup(string? id, string path)
    {
        return Invalid(
            "invalid-simulation-group",
            $"No group has the id '{Shorten(id)}'. Declare it in groups first.",
            path);
    }

    /// <summary>
    /// Checks one list of colors a group declares.
    /// </summary>
    private static OperationInvalidInput? CheckColors(IReadOnlyList<string>? colors, string path)
    {
        if (colors is null || colors.Count == 0 || colors.Any(color => !ColorNames.Contains(color)))
        {
            return Invalid(
                "invalid-colors",
                $"Colors must be one or more of {string.Join(", ", ColorNames)}.",
                path);
        }

        return null;
    }

    /// <summary>
    /// Maps declared colors to model symbols; <c>any</c> is W, U, B, R, and G.
    /// </summary>
    private static IReadOnlyList<OnCurveManaSymbol> ToSymbols(IReadOnlyList<string> colors)
    {
        List<OnCurveManaSymbol> symbols = [];
        foreach (string color in colors)
        {
            if (color == "any")
            {
                AddDistinct(symbols, OnCurveManaSymbol.White);
                AddDistinct(symbols, OnCurveManaSymbol.Blue);
                AddDistinct(symbols, OnCurveManaSymbol.Black);
                AddDistinct(symbols, OnCurveManaSymbol.Red);
                AddDistinct(symbols, OnCurveManaSymbol.Green);
            }
            else if (OnCurveManaSymbols.TryParseProducedMana(color, out OnCurveManaSymbol symbol))
            {
                AddDistinct(symbols, symbol);
            }
        }

        return symbols;
    }

    /// <summary>
    /// Adds a symbol to a list unless it is already there.
    /// </summary>
    private static void AddDistinct(List<OnCurveManaSymbol> symbols, OnCurveManaSymbol symbol)
    {
        if (!symbols.Contains(symbol))
        {
            symbols.Add(symbol);
        }
    }

    /// <summary>
    /// Ranks the entries by their IDs written as text, the order that breaks ties.
    /// </summary>
    private int[] OrdinalRanks()
    {
        string[] keys = [.. request.Deck.Select(entry => entry.EntryId.ToString("D"))];
        int[] order = [.. Enumerable.Range(0, keys.Length)];
        Array.Sort(order, (left, right) => string.CompareOrdinal(keys[left], keys[right]));
        int[] ranks = new int[keys.Length];
        for (int rank = 0; rank < order.Length; rank++)
        {
            ranks[order[rank]] = rank;
        }

        return ranks;
    }

    /// <summary>
    /// Lists the entry index of every physical card, entry by entry in deck order.
    /// </summary>
    private int[] CardEntries()
    {
        List<int> cards = [];
        for (int index = 0; index < request.Deck.Count; index++)
        {
            for (int copy = 0; copy < request.Deck[index].Quantity; copy++)
            {
                cards.Add(index);
            }
        }

        return [.. cards];
    }

    /// <summary>
    /// Builds an invalid-input failure for a number outside its range.
    /// </summary>
    private static OperationInvalidInput Value(string message, string path)
    {
        return Invalid("invalid-simulation-value", message, path);
    }

    /// <summary>
    /// Builds an invalid-input failure with a reason code and the path of the rejected value.
    /// </summary>
    private static OperationInvalidInput Invalid(string reasonCode, string message, string path)
    {
        return new OperationInvalidInput(reasonCode, message, path);
    }
}
