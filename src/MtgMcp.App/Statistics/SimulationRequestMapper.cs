using MtgMcp.App.CardLists;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.OnCurve;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Holds the rule parts of a simulation request that the caller wrote, or that the on-curve preset expanded to.
/// </summary>
internal sealed record SimulationScenario(
    IReadOnlyList<SimulationGroupInput> Groups,
    SimulationOpeningHandInput OpeningHand,
    IReadOnlyList<SimulationRuleInput> EachTurn,
    int Turns,
    IReadOnlyList<SimulationConditionInput> Conditions);

/// <summary>
/// Holds an engine request together with what the mapper found out while building it.
/// </summary>
internal sealed record SimulationMapping(
    SimulationRequest Request,
    IReadOnlyList<SimulationGroupView> Groups,
    IReadOnlyList<SimulationSkipped> Skipped);

/// <summary>
/// Turns the tool's input records into the engine's request: it resolves groups to main-zone entries, reads the
/// closed text values, and refuses anything else with the JSON path of the value.
/// </summary>
internal static class SimulationRequestMapper
{
    /// <summary>
    /// Names the one deck zone the engine shuffles.
    /// </summary>
    internal const string MainZone = "main";

    /// <summary>
    /// Maps a scenario over the main-zone entries of a deck.
    /// </summary>
    internal static OperationResult<SimulationMapping> Map(
        SimulationScenario scenario,
        DeckDocument deck,
        IReadOnlyList<DeckEntry> library,
        int samples,
        int sampleHands,
        ulong seed,
        bool limitIds = true)
    {
        OperationInvalidInput? idProblem = limitIds ? CheckIdCounts(scenario) : null;
        if (idProblem is not null)
        {
            return idProblem;
        }

        OperationResult<ResolvedGroups> resolved = ResolveGroups(scenario.Groups, deck, library);
        if (resolved is not OperationSuccess<ResolvedGroups> groups)
        {
            return OperationFailure.Forward<ResolvedGroups, SimulationMapping>(resolved);
        }

        OperationResult<SimulationOpeningHand> hand = MapOpeningHand(scenario.OpeningHand);
        if (hand is not OperationSuccess<SimulationOpeningHand> openingHand)
        {
            return OperationFailure.Forward<SimulationOpeningHand, SimulationMapping>(hand);
        }

        OperationResult<List<SimulationRule>> rules = MapRules(scenario.EachTurn);
        if (rules is not OperationSuccess<List<SimulationRule>> eachTurn)
        {
            return OperationFailure.Forward<List<SimulationRule>, SimulationMapping>(rules);
        }

        OperationResult<List<SimulationCondition>> mapped = MapConditions(scenario.Conditions);
        if (mapped is not OperationSuccess<List<SimulationCondition>> conditions)
        {
            return OperationFailure.Forward<List<SimulationCondition>, SimulationMapping>(mapped);
        }

        SimulationRequest request = new(
            [.. library.Select(entry => new SimulationDeckEntry(entry.EntryId, entry.Quantity))],
            groups.Data.Groups,
            openingHand.Data,
            eachTurn.Data,
            scenario.Turns,
            conditions.Data,
            samples,
            sampleHands,
            seed,
            $"deck {deck.DeckId:D} revision {deck.Revision}");
        return new OperationSuccess<SimulationMapping>(
            new SimulationMapping(request, groups.Data.Views, groups.Data.Skipped));
    }

    /// <summary>
    /// Holds the groups after their selects were resolved.
    /// </summary>
    private sealed record ResolvedGroups(
        List<SimulationGroup> Groups,
        List<SimulationGroupView> Views,
        List<SimulationSkipped> Skipped);

    /// <summary>
    /// Refuses a request that lists more IDs than a request may hold, so the echoed rules stay small.
    /// </summary>
    private static OperationInvalidInput? CheckIdCounts(SimulationScenario scenario)
    {
        int total = 0;
        for (int index = 0; index < scenario.Groups.Count; index++)
        {
            SimulationGroupInput? group = scenario.Groups[index];
            int own = CountIds(group?.Select) +
                (group?.Produces?.ByEntry?.Sum(item => item?.EntryIds?.Count ?? 0) ?? 0);
            total += own;
            if (CountIds(group?.Select) > CardListResolver.MaximumIdsPerSource)
            {
                return TooManyIds(
                    $"A select lists at most {CardListResolver.MaximumIdsPerSource} IDs.",
                    $"groups[{index}].select.ids");
            }
        }

        foreach (SimulationRuleInput? rule in scenario.EachTurn)
        {
            total += (rule as SimulationManaRuleInput)?.ManaFromByEntry?.Sum(item => item?.EntryIds?.Count ?? 0) ?? 0;
        }

        return total > CardListResolver.MaximumIdsPerRequest
            ? TooManyIds(
                $"A request lists at most {CardListResolver.MaximumIdsPerRequest} entry and category IDs.",
                "groups")
            : null;
    }

    /// <summary>
    /// Counts the IDs a select lists.
    /// </summary>
    private static int CountIds(CastabilitySelectInput? select)
    {
        return select switch
        {
            CastabilityEntryIdsSelect entries => entries.Ids?.Count ?? 0,
            CastabilityCategoryIdsSelect categories => categories.Ids?.Count ?? 0,
            _ => 0,
        };
    }

    /// <summary>
    /// Builds the failure for too many IDs.
    /// </summary>
    private static OperationInvalidInput TooManyIds(string message, string path)
    {
        return new OperationInvalidInput("too-many-ids", message, path);
    }

    /// <summary>
    /// Resolves each group's select to main-zone entries and lists the cards that were left out.
    /// </summary>
    private static OperationResult<ResolvedGroups> ResolveGroups(
        IReadOnlyList<SimulationGroupInput> inputs,
        DeckDocument deck,
        IReadOnlyList<DeckEntry> library)
    {
        Dictionary<Guid, DeckEntry> main = library.ToDictionary(entry => entry.EntryId);
        Dictionary<Guid, string> names = deck.Entries.ToDictionary(entry => entry.EntryId, entry => entry.CardName);
        ResolvedGroups resolved = new([], [], []);
        for (int index = 0; index < inputs.Count; index++)
        {
            string path = $"groups[{index}]";
            SimulationGroupInput? input = inputs[index];
            if (input is null || input.Id is null || input.Select is null)
            {
                return Missing(input is null ? path : path + (input.Id is null ? ".id" : ".select"));
            }

            OperationResult<List<Guid>> selected = CastabilityGroupPlanner.Select(input.Select, deck, path + ".select");
            if (selected is not OperationSuccess<List<Guid>> chosen)
            {
                return OperationFailure.Forward<List<Guid>, ResolvedGroups>(selected);
            }

            OperationInvalidInput? problem = KeepMainZone(
                input,
                chosen.Data,
                library,
                main,
                names,
                path,
                resolved,
                out List<Guid> members);
            if (problem is not null)
            {
                return problem;
            }

            OperationResult<SimulationProduces?> produces = MapProduces(input.Produces, path + ".produces");
            if (produces is not OperationSuccess<SimulationProduces?> colors)
            {
                return OperationFailure.Forward<SimulationProduces?, ResolvedGroups>(produces);
            }

            resolved.Groups.Add(new SimulationGroup(
                input.Id,
                members,
                input.Select is CastabilityEntryIdsSelect,
                colors.Data));
            resolved.Views.Add(new SimulationGroupView(
                input.Id,
                members.Count,
                members.Sum(id => main[id].Quantity)));
        }

        return new OperationSuccess<ResolvedGroups>(resolved);
    }

    /// <summary>
    /// Keeps the main-zone members of a group. An entry named by ID that is not in the main zone is refused; an entry
    /// that a category contains is listed as skipped, because a category can hold cards of other zones.
    /// </summary>
    private static OperationInvalidInput? KeepMainZone(
        SimulationGroupInput input,
        List<Guid> chosen,
        IReadOnlyList<DeckEntry> library,
        Dictionary<Guid, DeckEntry> main,
        Dictionary<Guid, string> names,
        string path,
        ResolvedGroups resolved,
        out List<Guid> members)
    {
        members = [];
        bool byEntry = input.Select is CastabilityEntryIdsSelect;
        foreach (Guid id in chosen)
        {
            if (main.ContainsKey(id))
            {
                members.Add(id);
            }
            else if (byEntry)
            {
                return new OperationInvalidInput(
                    "entry-not-in-main-zone",
                    $"Entry {id:D} ({names[id]}) is not in the main zone, which is the only zone the simulation " +
                    "shuffles.",
                    path + ".select.ids");
            }
            else
            {
                resolved.Skipped.Add(new SimulationSkipped(id, names[id], input.Id, "not-in-main-zone"));
            }
        }

        if (!byEntry)
        {
            HashSet<Guid> chosenIds = [.. members];
            members = [.. library.Where(entry => chosenIds.Contains(entry.EntryId)).Select(entry => entry.EntryId)];
        }

        return null;
    }

    /// <summary>
    /// Maps a group's colors, or returns null when the group adds generic mana only.
    /// </summary>
    private static OperationResult<SimulationProduces?> MapProduces(SimulationProducesInput? input, string path)
    {
        if (input is null)
        {
            return new OperationSuccess<SimulationProduces?>(null);
        }

        if (input.Default is null)
        {
            return Missing(path + ".default");
        }

        List<SimulationProducesOverride> overrides = [];
        IReadOnlyList<SimulationProducesOverrideInput> byEntry = input.ByEntry ?? [];
        for (int position = 0; position < byEntry.Count; position++)
        {
            SimulationProducesOverrideInput? item = byEntry[position];
            if (item is null || item.EntryIds is null || item.Colors is null)
            {
                return Missing($"{path}.byEntry[{position}]");
            }

            overrides.Add(new SimulationProducesOverride(item.EntryIds, item.Colors));
        }

        return new OperationSuccess<SimulationProduces?>(new SimulationProduces(input.Default, overrides));
    }

    /// <summary>
    /// Maps the opening hand and its mulligan.
    /// </summary>
    private static OperationResult<SimulationOpeningHand> MapOpeningHand(SimulationOpeningHandInput input)
    {
        const string path = "openingHand";
        if (input is null)
        {
            return Missing(path);
        }

        SimulationMulliganInput? mulligan = input.Mulligan;
        if (mulligan is null)
        {
            return new OperationSuccess<SimulationOpeningHand>(
                new SimulationOpeningHand(input.Size, input.OnThePlay, null));
        }

        string mulliganPath = path + ".mulligan";
        if (mulligan.Rule != "london")
        {
            return Closed("rule", mulligan.Rule, ["london"], mulliganPath + ".rule");
        }

        if (mulligan.KeepWhen is null || mulligan.Bottom?.PreferGroups is null)
        {
            return Missing(mulligan.KeepWhen is null ? mulliganPath + ".keepWhen" : mulliganPath + ".bottom");
        }

        if (mulligan.Bottom.Otherwise != "random")
        {
            return Closed("otherwise", mulligan.Bottom.Otherwise, ["random"], mulliganPath + ".bottom.otherwise");
        }

        OperationResult<SimulationCheck> keep = MapKeep(mulligan.KeepWhen, mulliganPath + ".keepWhen");
        if (keep is not OperationSuccess<SimulationCheck> keepRule)
        {
            return OperationFailure.Forward<SimulationCheck, SimulationOpeningHand>(keep);
        }

        return new OperationSuccess<SimulationOpeningHand>(new SimulationOpeningHand(
            input.Size,
            input.OnThePlay,
            new SimulationMulligan(
                mulligan.MaxMulligans,
                mulligan.FreeFirstMulligan,
                keepRule.Data,
                mulligan.Bottom.PreferGroups)));
    }

    /// <summary>
    /// Maps a keep check.
    /// </summary>
    private static OperationResult<SimulationCheck> MapKeep(SimulationKeepInput input, string path)
    {
        switch (input)
        {
            case SimulationKeepCountBetweenInput count when count.Group is not null:
                return new OperationSuccess<SimulationCheck>(
                    new SimulationCountBetween(count.Group, count.Min, count.Max));
            case SimulationKeepAllInput { Checks: not null } all:
                return MapChildren(all.Checks, MapKeep, children => new SimulationAll(children), path);
            case SimulationKeepAnyInput { Checks: not null } any:
                return MapChildren(any.Checks, MapKeep, children => new SimulationAny(children), path);
            default:
                return Missing(path);
        }
    }

    /// <summary>
    /// Maps the children of an <c>all</c> or <c>any</c> check, whichever union they belong to.
    /// </summary>
    private static OperationResult<SimulationCheck> MapChildren<T>(
        IReadOnlyList<T> inputs,
        Func<T, string, OperationResult<SimulationCheck>> map,
        Func<List<SimulationCheck>, SimulationCheck> combine,
        string path)
    {
        List<SimulationCheck> children = [];
        for (int position = 0; position < inputs.Count; position++)
        {
            string childPath = $"{path}.checks[{position}]";
            if (inputs[position] is null)
            {
                return Missing(childPath);
            }

            OperationResult<SimulationCheck> child = map(inputs[position], childPath);
            if (child is not OperationSuccess<SimulationCheck> check)
            {
                return child;
            }

            children.Add(check.Data);
        }

        return new OperationSuccess<SimulationCheck>(combine(children));
    }

    /// <summary>
    /// Maps the per-turn rules.
    /// </summary>
    private static OperationResult<List<SimulationRule>> MapRules(IReadOnlyList<SimulationRuleInput> inputs)
    {
        List<SimulationRule> rules = [];
        for (int index = 0; index < inputs.Count; index++)
        {
            string path = $"eachTurn[{index}]";
            OperationResult<SimulationRule> rule = MapRule(inputs[index], path);
            if (rule is not OperationSuccess<SimulationRule> mapped)
            {
                return OperationFailure.Forward<SimulationRule, List<SimulationRule>>(rule);
            }

            rules.Add(mapped.Data);
        }

        return new OperationSuccess<List<SimulationRule>>(rules);
    }

    /// <summary>
    /// Maps one per-turn rule.
    /// </summary>
    private static OperationResult<SimulationRule> MapRule(SimulationRuleInput? input, string path)
    {
        if (input is null || input.Group is null)
        {
            return Missing(input is null ? path : path + ".group");
        }

        OperationResult<SimulationPick> pick = ParsePick(input.Pick, path + ".pick");
        if (pick is not OperationSuccess<SimulationPick> picked)
        {
            return OperationFailure.Forward<SimulationPick, SimulationRule>(pick);
        }

        switch (input)
        {
            case SimulationPlayInput play:
                return MapPlay(play, picked.Data, path);
            case SimulationCastIfAbleInput cast:
                return MapMana(cast, path, (timing, overrides) => new SimulationCastIfAble(
                    cast.Group, picked.Data, cast.MaxPerTurn, cast.Cost, cast.AddsMana, timing, overrides));
            case SimulationDrawCardsInput draw:
                return MapDraw(draw, picked.Data, path);
            default:
                return Missing(path);
        }
    }

    /// <summary>
    /// Maps a <c>play</c> rule.
    /// </summary>
    private static OperationResult<SimulationRule> MapPlay(SimulationPlayInput play, SimulationPick pick, string path)
    {
        return MapMana(play, path, (timing, overrides) => new SimulationPlay(
            play.Group, pick, play.MaxPerTurn, play.AddsMana, timing, overrides, play.TargetCost));
    }

    /// <summary>
    /// Maps the mana fields shared by <c>play</c> and <c>cast-if-able</c>, then builds the rule.
    /// </summary>
    private static OperationResult<SimulationRule> MapMana(
        SimulationManaRuleInput input,
        string path,
        Func<SimulationManaFrom, List<SimulationManaFromOverride>, SimulationRule> build)
    {
        OperationResult<SimulationManaFrom> from = ParseManaFrom(input.ManaFrom, path + ".manaFrom");
        if (from is not OperationSuccess<SimulationManaFrom> manaFrom)
        {
            return OperationFailure.Forward<SimulationManaFrom, SimulationRule>(from);
        }

        List<SimulationManaFromOverride> overrides = [];
        IReadOnlyList<SimulationManaFromOverrideInput> byEntry = input.ManaFromByEntry ?? [];
        for (int position = 0; position < byEntry.Count; position++)
        {
            string itemPath = $"{path}.manaFromByEntry[{position}]";
            SimulationManaFromOverrideInput? item = byEntry[position];
            if (item is null || item.EntryIds is null)
            {
                return Missing(itemPath);
            }

            OperationResult<SimulationManaFrom> itemFrom = ParseManaFrom(item.ManaFrom, itemPath + ".manaFrom");
            if (itemFrom is not OperationSuccess<SimulationManaFrom> itemManaFrom)
            {
                return OperationFailure.Forward<SimulationManaFrom, SimulationRule>(itemFrom);
            }

            overrides.Add(new SimulationManaFromOverride(item.EntryIds, itemManaFrom.Data));
        }

        return new OperationSuccess<SimulationRule>(build(manaFrom.Data, overrides));
    }

    /// <summary>
    /// Maps a <c>draw-cards</c> rule.
    /// </summary>
    private static OperationResult<SimulationRule> MapDraw(
        SimulationDrawCardsInput draw,
        SimulationPick pick,
        string path)
    {
        SimulationDrawEffect effect;
        switch (draw.Effect)
        {
            case "once":
                effect = SimulationDrawEffect.Once;
                break;
            case "each-turn":
                effect = SimulationDrawEffect.EachTurn;
                break;
            default:
                return Closed("effect", draw.Effect, ["once", "each-turn"], path + ".effect");
        }

        return new OperationSuccess<SimulationRule>(
            new SimulationDrawCards(draw.Group, pick, draw.MaxPerTurn, draw.Cost, draw.Cards, effect));
    }

    /// <summary>
    /// Reads a pick name.
    /// </summary>
    private static OperationResult<SimulationPick> ParsePick(string? text, string path)
    {
        return text switch
        {
            "random" => new OperationSuccess<SimulationPick>(SimulationPick.Random),
            "listed-order" => new OperationSuccess<SimulationPick>(SimulationPick.ListedOrder),
            "target-first" => new OperationSuccess<SimulationPick>(SimulationPick.TargetFirst),
            _ => Closed("pick", text, ["random", "listed-order", "target-first"], path),
        };
    }

    /// <summary>
    /// Reads a mana timing name.
    /// </summary>
    private static OperationResult<SimulationManaFrom> ParseManaFrom(string? text, string path)
    {
        return text switch
        {
            "same-turn" => new OperationSuccess<SimulationManaFrom>(SimulationManaFrom.SameTurn),
            "next-turn" => new OperationSuccess<SimulationManaFrom>(SimulationManaFrom.NextTurn),
            _ => Closed("manaFrom", text, ["same-turn", "next-turn"], path),
        };
    }

    /// <summary>
    /// Maps the conditions.
    /// </summary>
    private static OperationResult<List<SimulationCondition>> MapConditions(
        IReadOnlyList<SimulationConditionInput> inputs)
    {
        List<SimulationCondition> conditions = [];
        for (int index = 0; index < inputs.Count; index++)
        {
            string path = $"conditions[{index}]";
            SimulationConditionInput? input = inputs[index];
            if (input is null || input.Id is null || input.Check is null)
            {
                return Missing(input is null ? path : path + (input.Id is null ? ".id" : ".check"));
            }

            OperationResult<SimulationCheck> check = MapCheck(input.Check, path + ".check");
            if (check is not OperationSuccess<SimulationCheck> mapped)
            {
                return OperationFailure.Forward<SimulationCheck, List<SimulationCondition>>(check);
            }

            conditions.Add(new SimulationCondition(input.Id, mapped.Data));
        }

        return new OperationSuccess<List<SimulationCondition>>(conditions);
    }

    /// <summary>
    /// Maps one condition check.
    /// </summary>
    private static OperationResult<SimulationCheck> MapCheck(SimulationCheckInput input, string path)
    {
        switch (input)
        {
            case SimulationAllInput { Checks: not null } all:
                return MapChildren(all.Checks, MapCheck, children => new SimulationAll(children), path);
            case SimulationAnyInput { Checks: not null } any:
                return MapChildren(any.Checks, MapCheck, children => new SimulationAny(children), path);
            case SimulationManaAtLeastInput mana:
                return new OperationSuccess<SimulationCheck>(new SimulationManaAtLeast(mana.Amount, mana.Turn));
            case SimulationDrawnAtLeastInput { Group: not null } drawn:
                return new OperationSuccess<SimulationCheck>(
                    new SimulationDrawnAtLeast(drawn.Group, drawn.Count, drawn.ByTurn));
            case SimulationOnBattlefieldAtLeastInput { Group: not null } field:
                return new OperationSuccess<SimulationCheck>(
                    new SimulationOnBattlefieldAtLeast(field.Group, field.Count, field.ByTurn));
            case SimulationCanCastInput { ManaCost: not null } cast:
                return new OperationSuccess<SimulationCheck>(
                    new SimulationCanCast(cast.ManaCost, cast.Turn, cast.ByTurn, cast.InHand));
            default:
                return Missing(path);
        }
    }

    /// <summary>
    /// Builds the failure for a value that must be one of a closed list of names.
    /// </summary>
    private static OperationInvalidInput Closed(string field, string? value, string[] allowed, string path)
    {
        return new OperationInvalidInput(
            "invalid-value",
            $"{field} must be {string.Join(" or ", allowed)}" + (value is null ? "." : $", not '{value}'."),
            path);
    }

    /// <summary>
    /// Builds the failure for a required value that is missing or null.
    /// </summary>
    private static OperationInvalidInput Missing(string path)
    {
        return new OperationInvalidInput(KindFields.MissingField, $"A required value is missing at {path}.", path);
    }
}
