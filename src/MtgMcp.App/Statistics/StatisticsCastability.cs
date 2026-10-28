using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using MtgMcp.App.CardLists;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.App.Scryfall;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;
using MtgMcp.Statistics;

namespace MtgMcp.App.Statistics;

/// <summary>
/// Chooses the entries of a mana source group by ID.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CastabilityEntryIdsSelect), "entry-ids")]
[JsonDerivedType(typeof(CastabilityCategoryIdsSelect), "category-ids")]
internal abstract record CastabilitySelectInput;

/// <summary>
/// Selects exact entry IDs.
/// </summary>
internal sealed record CastabilityEntryIdsSelect(
    [property: Description("One or more exact entry UUIDs.")] IReadOnlyList<Guid> Ids)
    : CastabilitySelectInput;

/// <summary>
/// Selects every entry assigned to one of the categories.
/// </summary>
internal sealed record CastabilityCategoryIdsSelect(
    [property: Description("One or more exact category UUIDs.")] IReadOnlyList<Guid> Ids)
    : CastabilitySelectInput;

/// <summary>
/// Gives the colors of the listed entries when they differ from the group's default colors.
/// </summary>
internal sealed record CastabilityProducesOverride(
    [property: Description("Entries of the group that make these colors instead of the default.")]
    IReadOnlyList<Guid> EntryIds,
    [property: Description("One or more of W, U, B, R, G, C that each of these entries can make.")]
    IReadOnlyList<string> Colors);

/// <summary>
/// Says which colors the cards of a group can make, one unit each.
/// </summary>
internal sealed record CastabilityProducesInput(
    [property: Description("One or more of W, U, B, R, G, C that each card of the group can make.")]
    IReadOnlyList<string> Default,
    [property: Description("Optional entries that make other colors than the default.")]
    IReadOnlyList<CastabilityProducesOverride>? ByEntry = null);

/// <summary>
/// Declares one group of mana sources chosen by the caller.
/// </summary>
internal sealed record CastabilitySourceGroupInput(
    [property: Description("Unique name for the group.")] string Id,
    [property: Description("Which entries are in the group. Groups must not overlap.")]
    CastabilitySelectInput Select,
    [property: Description("The colors the group's cards can make.")] CastabilityProducesInput Produces);

/// <summary>
/// Chooses the turn each spell is checked on.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ManaValueTurnInput), "mana-value")]
[JsonDerivedType(typeof(FixedTurnInput), "fixed")]
internal abstract record CastabilityTurnInput;

/// <summary>
/// Checks each spell on the turn equal to its mana value, at least turn 1.
/// </summary>
internal sealed record ManaValueTurnInput : CastabilityTurnInput;

/// <summary>
/// Checks every spell on one turn.
/// </summary>
internal sealed record FixedTurnInput(
    [property: Description("The turn number to check every spell on, at least 1.")] int Turn)
    : CastabilityTurnInput;

/// <summary>
/// Lists a card that was not checked and why.
/// </summary>
internal sealed record CastabilitySkipped(Guid EntryId, string Name, string Reason, string? ManaCost);

/// <summary>
/// Describes one source group as it was used.
/// </summary>
internal sealed record CastabilityGroupView(string Id, IReadOnlyList<string> Colors, int Cards);

/// <summary>
/// Gives the exact chance of paying one cost by its turn, and the cards that share the cost.
/// </summary>
internal sealed record CastabilityRowView(
    string Cost,
    int Turn,
    int CardsSeen,
    ExactProbability? Probability,
    IReadOnlyList<RuleEntryRef> Cards);

/// <summary>
/// Returns exact castability rows sorted by probability, then by card name. It states no target and no pass.
/// </summary>
internal sealed record CastabilityToolResult(
    IReadOnlyList<CardListSourceInfo> Sources,
    StatisticsDerivation Derivation,
    bool OnThePlay,
    bool RequireLandDrops,
    IReadOnlyList<CastabilityGroupView> Groups,
    IReadOnlyList<CastabilityRowView> Rows,
    IReadOnlyList<CastabilitySkipped> Skipped,
    IReadOnlyList<string> Limits)
{
    /// <summary>
    /// Gets how many skipped cards the list leaves out to keep the result small, or null when it lists all.
    /// </summary>
    [JsonPropertyName("skippedOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SkippedOmitted { get; init; }

    /// <summary>
    /// Gets true when rows were left out to stay under the size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to see every row when the result was cut.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}

/// <summary>
/// Reads a mana cost into the exact requirement the statistics engine understands, or says why it cannot.
/// </summary>
internal static partial class CastabilityManaCost
{
    /// <summary>
    /// Reads a mana cost. Costs with no cost, a variable symbol, a hybrid or Phyrexian symbol, a snow symbol, or two
    /// costs (split cards) cannot be expressed exactly and give a reason instead.
    /// </summary>
    internal static (ManaRequirementInput? Requirement, string? SkipReason) Parse(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return (null, "no-mana-cost");
        }

        if (manaCost.Contains("//", StringComparison.Ordinal))
        {
            return (null, "split-card-has-two-costs");
        }

        int white = 0;
        int blue = 0;
        int black = 0;
        int red = 0;
        int green = 0;
        int colorless = 0;
        int generic = 0;
        foreach (Match match in SymbolPattern().Matches(manaCost))
        {
            string symbol = match.Groups[1].Value;
            switch (symbol)
            {
                case "W":
                    white++;
                    break;
                case "U":
                    blue++;
                    break;
                case "B":
                    black++;
                    break;
                case "R":
                    red++;
                    break;
                case "G":
                    green++;
                    break;
                case "C":
                    colorless++;
                    break;
                case "X":
                    return (null, "x-cost");
                default:
                    if (symbol.Contains('/', StringComparison.Ordinal))
                    {
                        return (null, "hybrid-or-phyrexian-symbol");
                    }

                    if (!int.TryParse(symbol, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    {
                        return (null, "unsupported-symbol");
                    }

                    generic += number;
                    break;
            }
        }

        return (new ManaRequirementInput(white, blue, black, red, green, colorless, generic), null);
    }

    /// <summary>
    /// Matches one mana symbol and captures what is inside the braces.
    /// </summary>
    [GeneratedRegex(@"\{([^}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();
}

/// <summary>
/// Works out <c>stats_castability</c>: reads a local deck, builds the population from the caller's source groups,
/// calls the exact calculator, and writes sorted rows. It judges nothing.
/// </summary>
internal sealed class StatisticsCastabilityCoordinator
{
    /// <summary>
    /// Lists the mana colors a source can make.
    /// </summary>
    private static readonly string[] Colors = ["W", "U", "B", "R", "G", "C"];

    /// <summary>
    /// Gives the most skipped cards one result lists.
    /// </summary>
    private const int MaximumSkippedListed = 50;

    /// <summary>
    /// Reads the deck source.
    /// </summary>
    private readonly CardListResolver resolver;

    /// <summary>
    /// Reads installed card data.
    /// </summary>
    private readonly ScryfallService scryfall;

    /// <summary>
    /// Runs the exact calculation.
    /// </summary>
    private readonly ExactStatisticsCalculator calculator;

    /// <summary>
    /// Creates the coordinator.
    /// </summary>
    internal StatisticsCastabilityCoordinator(
        CardListResolver resolver,
        ScryfallService scryfall,
        ExactStatisticsCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(scryfall);
        ArgumentNullException.ThrowIfNull(calculator);
        this.resolver = resolver;
        this.scryfall = scryfall;
        this.calculator = calculator;
    }

    /// <summary>
    /// Calculates the castability of every distinct nonland cost of a deck.
    /// </summary>
    internal async Task<OperationResult<CastabilityToolResult>> CalculateAsync(
        CardListSourceInput source,
        IReadOnlyList<CastabilitySourceGroupInput>? sources,
        CastabilityTurnInput turn,
        bool onThePlay,
        bool requireLandDrops,
        CancellationToken cancellationToken)
    {
        if (turn is FixedTurnInput { Turn: < 1 })
        {
            return new OperationInvalidInput("invalid-turn", "turn.turn must be at least 1.", "turn.turn");
        }

        int idCount = 0;
        OperationInvalidInput? invalid = CardListResolver.Validate(source, "source", ref idCount);
        if (invalid is not null)
        {
            return invalid;
        }

        OperationResult<ResolvedDeckDocument> read = await resolver.ReadDeckDocumentAsync(source, "source",
            cancellationToken)
            .ConfigureAwait(false);
        if (read is not OperationSuccess<ResolvedDeckDocument> document)
        {
            return OperationFailure.Forward<ResolvedDeckDocument, CastabilityToolResult>(read);
        }

        DeckDocument deck = document.Data.Deck;
        List<DeckEntry> entries =
        [
            .. deck.Entries.Where(entry => document.Data.Zones is null ||
                document.Data.Zones.Count == 0 ||
                document.Data.Zones.Contains(entry.Zone, StringComparer.OrdinalIgnoreCase)),
        ];
        OperationResult<GroupPlan> planned = PlanGroups(sources, deck, entries);
        if (planned is not OperationSuccess<GroupPlan> plan)
        {
            return OperationFailure.Forward<GroupPlan, CastabilityToolResult>(planned);
        }

        OperationResult<ScryfallInstalledListCards> cardRead = await ScryfallToolExecution.RunAsync(() =>
            scryfall.ReadInstalledListCardsAsync(
                [.. entries.Select(entry => CardListResolver.LookupFor(
                    entry.PrintingId, entry.SetCode, entry.CollectorNumber, entry.OracleId))],
                includeRaw: false,
                includeFacts: false,
                cancellationToken)).ConfigureAwait(false);
        if (cardRead is not OperationSuccess<ScryfallInstalledListCards> cards)
        {
            return OperationFailure.Forward<ScryfallInstalledListCards, CastabilityToolResult>(cardRead);
        }

        if (!cards.Data.Installed)
        {
            return new OperationNotCached(
                "card-data-not-installed",
                "Scryfall card data is not installed, so spell costs cannot be read. Run scryfall_card_data_sync " +
                "to install it, then repeat the request.");
        }

        (List<SpellCost> spells, List<CastabilitySkipped> skipped) = ReadSpells(entries, cards.Data.Cards, turn);
        if (spells.Count == 0)
        {
            return new OperationInvalidInput(
                "no-spells-to-check",
                "The deck has no nonland card with a mana cost that can be checked in the chosen zones.",
                "source");
        }

        List<IGrouping<string, SpellCost>> distinct =
        [
            .. spells.GroupBy(spell => $"{spell.Cost}@{spell.Turn}", StringComparer.Ordinal),
        ];
        CastabilityRequest request = new(
            BuildPopulation(entries, plan.Data),
            plan.Data.Sources,
            [.. distinct.Select(group => new CastabilityCostInput(
                group.Key,
                group.First().Requirement,
                group.First().Turn))],
            onThePlay,
            requireLandDrops);
        OperationResult<StatisticsCalculation<CastabilityResult>> calculated = calculator.CalculateCastability(
            request,
            cancellationToken);
        if (calculated.Value is OperationSuccess<StatisticsCalculation<CastabilityResult>> success)
        {
            return success.Data.Value switch
            {
                StatisticsExact<CastabilityResult> exact => new OperationSuccess<CastabilityToolResult>(
                    Present(document.Data.Info, exact.Data, distinct, plan.Data, skipped, entries)),
                StatisticsBoundedUnsupported bounded => new OperationUnsupported(bounded.ReasonCode, bounded.Message),
                _ => new OperationUnavailable("unexpected-statistics-state", "The calculation returned an " +
                    "unexpected state."),
            };
        }

        return OperationFailure.Forward<StatisticsCalculation<CastabilityResult>, CastabilityToolResult>(calculated);
    }

    /// <summary>
    /// Expands the caller's source groups into named groups the calculator can use, one for each distinct color set.
    /// </summary>
    private static OperationResult<GroupPlan> PlanGroups(
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

        HashSet<Guid> entryIds = [.. entries.Select(entry => entry.EntryId)];
        List<ManaSourceInput> manaSources = [];
        Dictionary<Guid, string> memberOf = [];
        List<CastabilityGroupView> views = [];
        HashSet<string> names = new(StringComparer.Ordinal);
        for (int index = 0; index < inputs.Count; index++)
        {
            CastabilitySourceGroupInput input = inputs[index];
            string path = $"sources[{index}]";
            if (string.IsNullOrWhiteSpace(input.Id) || !names.Add(input.Id))
            {
                return new OperationInvalidInput("invalid-sources", "Each group needs a unique, non-blank id.", path
                    + ".id");
            }

            OperationResult<List<Guid>> selected = Select(input.Select, deck, path + ".select");
            if (selected is not OperationSuccess<List<Guid>> chosen)
            {
                return OperationFailure.Forward<List<Guid>, GroupPlan>(selected);
            }

            OperationInvalidInput? colorProblem = CheckColors(input.Produces?.Default, path + ".produces.default");
            if (colorProblem is not null)
            {
                return colorProblem;
            }

            List<Guid> members = [.. chosen.Data.Where(entryIds.Contains)];
            Dictionary<Guid, string[]> overrides = [];
            foreach ((CastabilityProducesOverride value, int position) in (input.Produces!.ByEntry
                ?? []).Select((value, position) => (value, position)))
            {
                OperationInvalidInput? problem = CheckColors(value?.Colors,
                    $"{path}.produces.byEntry[{position}].colors");
                if (problem is not null)
                {
                    return problem;
                }

                foreach (Guid id in value!.EntryIds ?? [])
                {
                    if (!chosen.Data.Contains(id) || !overrides.TryAdd(id, SortedColors(value.Colors)))
                    {
                        return new OperationInvalidInput(
                            "invalid-sources",
                            "Each byEntry entry must be in the group and listed once.",
                            $"{path}.produces.byEntry[{position}].entryIds");
                    }
                }
            }

            string[] defaultColors = SortedColors(input.Produces.Default!);
            Dictionary<string, List<Guid>> byColors = new(StringComparer.Ordinal);
            foreach (Guid id in members)
            {
                string[] colors = overrides.TryGetValue(id, out string[]? replaced) ? replaced : defaultColors;
                string key = string.Join(string.Empty, colors);
                if (!byColors.TryGetValue(key, out List<Guid>? list))
                {
                    list = [];
                    byColors[key] = list;
                }

                list.Add(id);
            }

            string defaultKey = string.Join(string.Empty, defaultColors);
            foreach ((string key, List<Guid> ids) in byColors.OrderBy(pair => pair.Key == defaultKey ? 0
                : 1).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string name = key == defaultKey ? input.Id : $"{input.Id}:{key}";
                manaSources.Add(new ManaSourceInput(name, [.. key.Select(color => color.ToString())]));
                foreach (Guid id in ids)
                {
                    if (!memberOf.TryAdd(id, name))
                    {
                        return new OperationInvalidInput(
                            "overlapping-source-groups",
                            $"Entry {id:D} is in both {memberOf[id].Split(':')[0]} and {input.Id}. Groups must not " +
                                $"overlap.",
                            path + ".select");
                    }
                }
            }

            views.Add(new CastabilityGroupView(
                input.Id,
                defaultColors,
                members.Sum(id => entries.First(entry => entry.EntryId == id).Quantity)));
        }

        return new OperationSuccess<GroupPlan>(new GroupPlan(manaSources, memberOf, views));
    }

    /// <summary>
    /// Lists the entry IDs a select chooses and checks that every ID exists in the deck.
    /// </summary>
    private static OperationResult<List<Guid>> Select(CastabilitySelectInput? select, DeckDocument deck, string path)
    {
        switch (select)
        {
            case CastabilityEntryIdsSelect { Ids.Count: > 0 } byEntry:
                Guid? missing = byEntry.Ids.Cast<Guid?>().FirstOrDefault(
                    id => deck.Entries.All(entry => entry.EntryId != id));
                return missing is null
                    ? new OperationSuccess<List<Guid>>([.. byEntry.Ids])
                    : new OperationInvalidInput("unknown-entry-id", $"The deck has no entry {missing:D}.", path
                        + ".ids");
            case CastabilityCategoryIdsSelect { Ids.Count: > 0 } byCategory:
                Guid? unknown = byCategory.Ids.Cast<Guid?>().FirstOrDefault(
                    id => deck.Categories.All(category => category.CategoryId != id));
                if (unknown is not null)
                {
                    return new OperationInvalidInput("unknown-category-id", $"The deck has no category " +
                        $"{unknown:D}.", path + ".ids");
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
        if (colors is null || colors.Count == 0 || colors.Any(color => !Colors.Contains(color, StringComparer.Ordinal)))
        {
            return new OperationInvalidInput(
                "invalid-colors",
                $"Colors must be one or more of {string.Join(", ", Colors)}.",
                path);
        }

        return null;
    }

    /// <summary>
    /// Sorts and de-duplicates a list of colors.
    /// </summary>
    private static string[] SortedColors(IReadOnlyList<string> colors)
    {
        return [.. colors.Distinct(StringComparer.Ordinal).OrderBy(color => Array.IndexOf(Colors, color))];
    }

    /// <summary>
    /// Reads the cost and turn of each nonland entry with a mana cost; the others are listed as skipped.
    /// </summary>
    private static (List<SpellCost> Spells, List<CastabilitySkipped> Skipped) ReadSpells(
        List<DeckEntry> entries,
        IReadOnlyList<ScryfallListCard?> cards,
        CastabilityTurnInput turn)
    {
        List<SpellCost> spells = [];
        List<CastabilitySkipped> skipped = [];
        for (int index = 0; index < entries.Count; index++)
        {
            if (cards[index]?.Card is not { } card ||
                CommanderCardText.FrontTypes(card).Contains("Land", StringComparer.Ordinal))
            {
                continue;
            }

            string? cost = ScryfallListFields.ManaCostOf(card);
            (ManaRequirementInput? requirement, string? reason) = CastabilityManaCost.Parse(cost);
            if (requirement is null)
            {
                skipped.Add(new CastabilitySkipped(entries[index].EntryId, card.Name, reason!, cost));
                continue;
            }

            int turnNumber = turn is FixedTurnInput fixedTurn
                ? fixedTurn.Turn
                : Math.Max(1, (int)Math.Ceiling(card.ManaValue ?? 0m));
            spells.Add(new SpellCost(entries[index], card.Name, cost!, turnNumber, requirement));
        }

        return (spells, skipped);
    }

    /// <summary>
    /// Builds the population: each entry is a bucket of its copies, in the source group it belongs to, if any.
    /// </summary>
    private static StatisticsPopulation BuildPopulation(List<DeckEntry> entries, GroupPlan plan)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (DeckEntry entry in entries)
        {
            string group = plan.MemberOf.GetValueOrDefault(entry.EntryId) ?? string.Empty;
            counts[group] = counts.GetValueOrDefault(group) + entry.Quantity;
        }

        return new StatisticsPopulation(
            [.. counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new StatisticsPopulationBucket(
                pair.Value,
                pair.Key.Length == 0 ? [] : [pair.Key]))],
            [.. plan.Sources.Select(source => source.Group)]);
    }

    /// <summary>
    /// Builds the tool result: rows sorted by probability and name, cut to the size limit.
    /// </summary>
    private static CastabilityToolResult Present(
        CardListSourceInfo info,
        CastabilityResult result,
        List<IGrouping<string, SpellCost>> distinct,
        GroupPlan plan,
        List<CastabilitySkipped> skipped,
        List<DeckEntry> entries)
    {
        Dictionary<string, SpellCost[]> byKey = distinct.ToDictionary(group => group.Key, group => group.ToArray(),
            StringComparer.Ordinal);
        List<CastabilityRowView> rows =
        [
            .. result.Rows.Select(row =>
            {
                SpellCost[] spells = byKey[row.Key];
                return new CastabilityRowView(
                    spells[0].Cost,
                    row.Turn,
                    row.CardsSeen,
                    row.Probability,
                    [.. spells.OrderBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(spell => new RuleEntryRef(spell.Entry.EntryId, spell.Name))]);
            }),
        ];
        rows.Sort(CompareRows);
        List<string> limits =
        [
            "Probabilities are exact for the declared source groups; which cards count as sources and what " +
                "probability is good enough are your judgments.",
            "Cards with no mana cost, with X, with hybrid or Phyrexian symbols, or with two costs are listed in " +
                "skipped and not checked.",
            "Whether a land enters tapped, activation costs, and alternate costs are not modeled.",
        ];
        if (result.RequireLandDrops)
        {
            limits.Add("A probability is null when the deck holds fewer sources than the turn number, so the " +
                "condition cannot happen.");
        }

        List<int> sizes = [.. rows.Select(row => Measure(row) + 1)];
        int skippedBytes = skipped.Take(MaximumSkippedListed).Sum(item => Measure(item) + 1);
        int keep = ResultSizeLimit.CountFitting(sizes, skippedBytes + Measure(limits));
        List<CastabilitySkipped> listed = [.. skipped.Take(MaximumSkippedListed)];
        CastabilityToolResult built = new(
            [info],
            result.Derivation,
            result.OnThePlay,
            result.RequireLandDrops,
            plan.Views,
            [.. rows.Take(keep)],
            listed,
            limits)
        {
            SkippedOmitted = skipped.Count > listed.Count ? skipped.Count - listed.Count : null,
            Truncated = keep < rows.Count ? true : null,
            Narrow = keep < rows.Count
                ? "Rows were left out to stay under the size limit. Choose fewer zones, or ask for a fixed turn."
                : null,
        };
        _ = entries;
        return built;
    }

    /// <summary>
    /// Orders rows by probability (lowest first, undefined last), then by the first card name.
    /// </summary>
    private static int CompareRows(CastabilityRowView first, CastabilityRowView second)
    {
        if (first.Probability is null || second.Probability is null)
        {
            if (first.Probability is null && second.Probability is null)
            {
                return CompareNames(first, second);
            }

            return first.Probability is null ? 1 : -1;
        }

        BigInteger left = BigInteger.Parse(first.Probability.Numerator, CultureInfo.InvariantCulture) *
            BigInteger.Parse(second.Probability.Denominator, CultureInfo.InvariantCulture);
        BigInteger right = BigInteger.Parse(second.Probability.Numerator, CultureInfo.InvariantCulture) *
            BigInteger.Parse(first.Probability.Denominator, CultureInfo.InvariantCulture);
        int byProbability = left.CompareTo(right);
        return byProbability != 0 ? byProbability : CompareNames(first, second);
    }

    /// <summary>
    /// Orders two rows by the name of their first card, then by cost.
    /// </summary>
    private static int CompareNames(CastabilityRowView first, CastabilityRowView second)
    {
        int byName = StringComparer.OrdinalIgnoreCase.Compare(first.Cards[0].Name, second.Cards[0].Name);
        return byName != 0 ? byName : string.CompareOrdinal(first.Cost, second.Cost);
    }

    /// <summary>
    /// Measures the serialized size of a value as the server writes it.
    /// </summary>
    private static int Measure(object value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), ToolCallPolicy.SerializerOptions).Length;
    }

    /// <summary>
    /// Holds the expanded source groups and which one each entry belongs to.
    /// </summary>
    private sealed record GroupPlan(
        IReadOnlyList<ManaSourceInput> Sources,
        Dictionary<Guid, string> MemberOf,
        IReadOnlyList<CastabilityGroupView> Views);

    /// <summary>
    /// Holds one spell with its exact requirement and turn.
    /// </summary>
    private sealed record SpellCost(DeckEntry Entry, string Name, string Cost, int Turn,
        ManaRequirementInput Requirement);
}

/// <summary>
/// Exposes the castability tool.
/// </summary>
internal sealed class StatisticsCastabilityTools
{
    /// <summary>
    /// Stands for a group placeholder in the examples.
    /// </summary>
    internal const string ExampleCategoryId = "0193e97a-ef95-7700-8000-000000000021";

    /// <summary>
    /// Stands for a second entry ID in the examples.
    /// </summary>
    internal const string ExampleEntryId2 = "0193e97a-ef95-7700-8000-000000000012";

    /// <summary>
    /// A minimal request: one group chosen by category, each spell checked on its mana value.
    /// </summary>
    internal const string ExampleMinimal =
        "{\"source\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId + "\"," +
        "\"zones\":[\"main\"]},\"sources\":[{\"id\":\"lands\",\"select\":{\"kind\":\"category-ids\",\"ids\":[\"" +
        ExampleCategoryId + "\"]},\"produces\":{\"default\":[\"G\"]}}],\"turn\":{\"kind\":\"mana-value\"}," +
        "\"onThePlay\":true,\"requireLandDrops\":true}";

    /// <summary>
    /// A full request: two groups, a color override, a fixed turn, and a backup source.
    /// </summary>
    internal const string ExampleFull =
        "{\"source\":{\"kind\":\"deck\",\"deckId\":\"" + DeckToolExamples.PlaceholderDeckId +
        "\",\"expectedRevision\":" + DeckToolExamples.PlaceholderRevision + ",\"zones\":[\"main\"]}," +
        "\"sources\":[{\"id\":\"lands\",\"select\":{\"kind\":\"category-ids\",\"ids\":[\"" + ExampleCategoryId +
        "\"]},\"produces\":{\"default\":[\"G\"],\"byEntry\":[{\"entryIds\":[\"" + DeckToolExamples.PlaceholderEntryId +
        "\"],\"colors\":[\"B\",\"G\"]}]}},{\"id\":\"rocks\",\"select\":{\"kind\":\"entry-ids\",\"ids\":[\"" +
        ExampleEntryId2 + "\"]},\"produces\":{\"default\":[\"C\"]}}]," +
        "\"turn\":{\"kind\":\"fixed\",\"turn\":4},\"onThePlay\":false,\"requireLandDrops\":false}";

    /// <summary>
    /// Runs the calculation.
    /// </summary>
    private readonly StatisticsCastabilityCoordinator coordinator;

    /// <summary>
    /// Creates the tool around one coordinator.
    /// </summary>
    internal StatisticsCastabilityTools(StatisticsCastabilityCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Calculates the exact chance that each spell of a deck can be cast on curve.
    /// </summary>
    [McpServerTool(
        Name = "stats_castability",
        Title = "Calculate Spell Castability",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Exact math: for each distinct mana cost of a deck's nonland cards, the exact probability that the cards " +
        "seen by its turn hold sources that can pay it, using at most one source per turn. You declare the source " +
        "groups (by entry or category ID) and the colors each makes, and choose the turn rule (mana-value, or " +
        "fixed), play or draw, and requireLandDrops: true conditions on having seen at least as many sources as " +
        "the turn number (as Frank Karsten's tables do); false is unconditional and equals stats_mana_availability. " +
        "Rows are sorted by probability, lowest first, then name. It states no target and no pass; what counts as a " +
        "source and what probability is good enough are your judgments. Cards with no cost, X, hybrid, or " +
        "Phyrexian symbols are listed in skipped. source is a deck or deck-backup list source and needs installed " +
        "card data." + UntrustedTextNotes.CardListEntries +
        DeckToolExamples.ExamplePrefix + "minimal): " + ExampleMinimal +
        DeckToolExamples.ExamplePrefix + "full): " + ExampleFull)]
    internal Task<OperationResult<CastabilityToolResult>> CalculateAsync(
        [Description("The deck to read: a deck or deck-backup list source. Choose zones to leave out the commander.")]
        CardListSourceInput source,
        [Description("Mana source groups. Groups must not overlap.")]
        IReadOnlyList<CastabilitySourceGroupInput> sources,
        [Description("When each spell is checked: mana-value (its mana value, at least 1) or fixed with a turn.")]
        CastabilityTurnInput turn,
        [Description("True if the player goes first (7 + turn - 1 cards seen), false on the draw (7 + turn).")]
        bool onThePlay,
        [Description("Required. True conditions on seeing at least as many sources as the turn number.")]
        bool requireLandDrops,
        CancellationToken cancellationToken = default)
    {
        return coordinator.CalculateAsync(source, sources, turn, onThePlay, requireLandDrops, cancellationToken);
    }
}
