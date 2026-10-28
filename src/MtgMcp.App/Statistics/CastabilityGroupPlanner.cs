using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Names the group an entry belongs to, as the calculator sees it and as the caller declared it.
/// </summary>
/// <param name="CalculatorName">The calculator's group name: the caller's ID, plus the colors when they differ.</param>
/// <param name="InputId">The caller's group ID.</param>
internal sealed record CastabilityMembership(string CalculatorName, string InputId);

/// <summary>
/// Holds the expanded source groups and which one each entry belongs to.
/// </summary>
internal sealed record CastabilityGroupPlan(
    IReadOnlyList<ManaSourceInput> Sources,
    Dictionary<Guid, CastabilityMembership> MemberOf,
    IReadOnlyList<CastabilityGroupView> Views);

/// <summary>
/// Expands the caller's source groups into named groups the calculator can use, one for each distinct color set,
/// and checks them against the deck.
/// </summary>
internal static class CastabilityGroupPlanner
{
    /// <summary>
    /// Plans the groups for the entries in the chosen zones.
    /// </summary>
    internal static OperationResult<CastabilityGroupPlan> Plan(
        IReadOnlyList<CastabilitySourceGroupInput>? inputs,
        DeckDocument deck,
        List<DeckEntry> entries)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return new OperationInvalidInput(
                "invalid-sources",
                "sources needs at least one mana source group.",
                "sources");
        }

        Dictionary<Guid, int> quantities = entries.ToDictionary(entry => entry.EntryId, entry => entry.Quantity);
        List<ManaSourceInput> manaSources = [];
        Dictionary<Guid, CastabilityMembership> memberOf = [];
        List<CastabilityGroupView> views = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        for (int index = 0; index < inputs.Count; index++)
        {
            CastabilitySourceGroupInput input = inputs[index];
            string path = $"sources[{index}]";
            if (string.IsNullOrWhiteSpace(input.Id) || !names.Add(input.Id))
            {
                return new OperationInvalidInput(
                    "invalid-sources",
                    "Each group needs a unique, non-blank id.",
                    path + ".id");
            }

            OperationResult<List<Guid>> selected = Select(input.Select, deck, path + ".select");
            if (selected is not OperationSuccess<List<Guid>> chosen)
            {
                return OperationFailure.Forward<List<Guid>, CastabilityGroupPlan>(selected);
            }

            OperationInvalidInput? colorProblem = CheckColors(input.Produces?.Default, path + ".produces.default");
            if (colorProblem is not null)
            {
                return colorProblem;
            }

            List<Guid> members = [.. chosen.Data.Where(quantities.ContainsKey)];
            OperationResult<Dictionary<Guid, string[]>> overridden = ReadOverrides(input, chosen.Data, path);
            if (overridden is not OperationSuccess<Dictionary<Guid, string[]>> overrides)
            {
                return OperationFailure.Forward<Dictionary<Guid, string[]>, CastabilityGroupPlan>(overridden);
            }

            string[] defaultColors = SortedColors(input.Produces!.Default!);
            string defaultKey = string.Join(string.Empty, defaultColors);
            Dictionary<string, List<Guid>> byColors = new(StringComparer.Ordinal);
            foreach (Guid id in members)
            {
                string[] colors = overrides.Data.TryGetValue(id, out string[]? replaced) ? replaced : defaultColors;
                string key = string.Join(string.Empty, colors);
                if (!byColors.TryGetValue(key, out List<Guid>? list))
                {
                    list = [];
                    byColors[key] = list;
                }

                list.Add(id);
            }

            foreach ((string key, List<Guid> ids) in byColors
                .OrderBy(pair => pair.Key == defaultKey ? 0 : 1)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string name = key == defaultKey ? input.Id : $"{input.Id}:{key}";
                manaSources.Add(new ManaSourceInput(name, [.. key.Select(color => color.ToString())]));
                foreach (Guid id in ids)
                {
                    if (!memberOf.TryAdd(id, new CastabilityMembership(name, input.Id)))
                    {
                        return new OperationInvalidInput(
                            "overlapping-source-groups",
                            $"Entry {id:D} is in both {memberOf[id].InputId} and {input.Id}. Groups must not overlap.",
                            path + ".select");
                    }
                }
            }

            views.Add(new CastabilityGroupView(input.Id, defaultColors, members.Sum(id => quantities[id])));
        }

        return new OperationSuccess<CastabilityGroupPlan>(new CastabilityGroupPlan(manaSources, memberOf, views));
    }

    /// <summary>
    /// Reads the colors that single entries of a group make instead of the group's default.
    /// </summary>
    private static OperationResult<Dictionary<Guid, string[]>> ReadOverrides(
        CastabilitySourceGroupInput input,
        List<Guid> chosen,
        string path)
    {
        Dictionary<Guid, string[]> overrides = [];
        IReadOnlyList<CastabilityProducesOverride> byEntry = input.Produces!.ByEntry ?? [];
        for (int position = 0; position < byEntry.Count; position++)
        {
            CastabilityProducesOverride? value = byEntry[position];
            OperationInvalidInput? problem = CheckColors(value?.Colors, $"{path}.produces.byEntry[{position}].colors");
            if (problem is not null)
            {
                return problem;
            }

            foreach (Guid id in value!.EntryIds ?? [])
            {
                if (!chosen.Contains(id) || !overrides.TryAdd(id, SortedColors(value.Colors)))
                {
                    return new OperationInvalidInput(
                        "invalid-sources",
                        "Each entry ID in byEntry must be one of this group's entries and appear only once.",
                        $"{path}.produces.byEntry[{position}].entryIds");
                }
            }
        }

        return new OperationSuccess<Dictionary<Guid, string[]>>(overrides);
    }

    /// <summary>
    /// Lists the entry IDs a select chooses and checks that every ID exists in the deck.
    /// </summary>
    private static OperationResult<List<Guid>> Select(CastabilitySelectInput? select, DeckDocument deck, string path)
    {
        switch (select)
        {
            case CastabilityEntryIdsSelect { Ids.Count: > 0 } byEntry:
                HashSet<Guid> known = [.. deck.Entries.Select(entry => entry.EntryId)];
                Guid? missing = byEntry.Ids.Cast<Guid?>().FirstOrDefault(id => !known.Contains(id!.Value));
                return missing is null
                    ? new OperationSuccess<List<Guid>>([.. byEntry.Ids])
                    : new OperationInvalidInput(
                        "unknown-entry-id",
                        $"The deck has no entry {missing:D}.",
                        path + ".ids");
            case CastabilityCategoryIdsSelect { Ids.Count: > 0 } byCategory:
                HashSet<Guid> categories = [.. deck.Categories.Select(category => category.CategoryId)];
                Guid? unknown = byCategory.Ids.Cast<Guid?>().FirstOrDefault(id => !categories.Contains(id!.Value));
                if (unknown is not null)
                {
                    return new OperationInvalidInput(
                        "unknown-category-id",
                        $"The deck has no category {unknown:D}.",
                        path + ".ids");
                }

                return new OperationSuccess<List<Guid>>(
                [
                    .. deck.CategoryAssignments
                        .Where(assignment => byCategory.Ids.Contains(assignment.CategoryId))
                        .Select(assignment => assignment.EntryId)
                        .Distinct(),
                ]);
            default:
                return new OperationInvalidInput("invalid-sources", "select needs at least one ID.", path);
        }
    }

    /// <summary>
    /// Checks a list of mana colors.
    /// </summary>
    private static OperationInvalidInput? CheckColors(IReadOnlyList<string>? colors, string path)
    {
        if (colors is null || colors.Count == 0 ||
            colors.Any(color => !ScryfallCardFaces.ManaColors.Contains(color, StringComparer.Ordinal)))
        {
            return new OperationInvalidInput(
                "invalid-colors",
                $"Colors must be one or more of {string.Join(", ", ScryfallCardFaces.ManaColors)}.",
                path);
        }

        return null;
    }

    /// <summary>
    /// Sorts and de-duplicates a list of colors.
    /// </summary>
    private static string[] SortedColors(IReadOnlyList<string> colors)
    {
        List<string> order = [.. ScryfallCardFaces.ManaColors];
        return [.. colors.Distinct(StringComparer.Ordinal).OrderBy(order.IndexOf)];
    }
}
