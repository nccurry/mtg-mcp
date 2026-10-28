using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Describes one filter of a list query: a field, an operator, and the value to compare with.
/// </summary>
/// <param name="Field">The field name.</param>
/// <param name="Op">One of eq, ne, lt, lte, gt, gte, in, or contains.</param>
/// <param name="Value">The JSON value to compare with; an array for <c>in</c>.</param>
public sealed record ScryfallListCondition(string Field, string Op, JsonElement Value);

/// <summary>
/// Describes one sort key of the rows of a list query.
/// </summary>
/// <param name="Field">The field name.</param>
/// <param name="Direction">Either asc or desc.</param>
public sealed record ScryfallListSort(string Field, string Direction);

/// <summary>
/// Asks for per-card rows: the fields to return, the sort, and the part of the rows to read.
/// </summary>
/// <param name="Fields">The field names to return, in order.</param>
/// <param name="SortBy">The sort keys; empty means name, then Oracle ID.</param>
/// <param name="Offset">The number of rows to skip.</param>
/// <param name="PageSize">The most rows to return.</param>
public sealed record ScryfallListRowOptions(
    IReadOnlyList<string> Fields,
    IReadOnlyList<ScryfallListSort> SortBy,
    int Offset,
    int PageSize);

/// <summary>
/// Describes one list query: filters, up to two group fields, metrics, and optional rows.
/// </summary>
/// <param name="Where">Filters, all of which must match.</param>
/// <param name="GroupBy">Zero to two field names to group by.</param>
/// <param name="Metrics">Metric names; empty means quantity and cards.</param>
/// <param name="Rows">Per-card rows, or null for grouped totals only.</param>
/// <param name="HasDeckFields">Whether the list has deck entries, so zone, category, and quantity exist.</param>
public sealed record ScryfallListQueryRequest(
    IReadOnlyList<ScryfallListCondition> Where,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<string> Metrics,
    ScryfallListRowOptions? Rows,
    bool HasDeckFields);

/// <summary>
/// Counts the cards that matched the filters.
/// </summary>
/// <param name="Cards">The number of distinct cards.</param>
/// <param name="Quantity">The number of copies.</param>
public sealed record ScryfallListMatched(
    [property: JsonPropertyName("cards")] int Cards,
    [property: JsonPropertyName("quantity")] long Quantity);

/// <summary>
/// Reports how many cards had no value for a field and were left out of what depended on it.
/// </summary>
/// <param name="Field">The field name.</param>
/// <param name="Cards">The number of distinct cards without a value.</param>
public sealed record ScryfallMissingValues(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("cards")] int Cards);

/// <summary>
/// Holds the rows of a list query that were read, and how many rows there were in all.
/// </summary>
/// <param name="Items">The rows read, each with an Oracle ID and the requested fields.</param>
/// <param name="TotalRows">The number of rows that matched the filters.</param>
public sealed record ScryfallListRows(
    [property: JsonPropertyName("items")] IReadOnlyList<ScryfallOrderedObject> Items,
    [property: JsonPropertyName("totalRows")] int TotalRows);

/// <summary>
/// Holds the answer of a list query before paging cursors and size limits are applied.
/// </summary>
/// <param name="Matched">How many cards matched.</param>
/// <param name="Groups">The groups in key order; one group with an empty key when nothing is grouped.</param>
/// <param name="MultiValued">Whether a card counted in more than one group.</param>
/// <param name="MissingValues">The fields some cards had no value for.</param>
/// <param name="Rows">The rows, or null when none were asked for.</param>
/// <param name="Limits">Notes on what the numbers do not cover.</param>
public sealed record ScryfallListQueryResult(
    ScryfallListMatched Matched,
    IReadOnlyList<ScryfallOrderedObject> Groups,
    bool MultiValued,
    IReadOnlyList<ScryfallMissingValues> MissingValues,
    ScryfallListRows? Rows,
    IReadOnlyList<string> Limits);

/// <summary>
/// Filters, groups, sums, and sorts a list of cards. It does no input or output, so it can be tested alone.
/// </summary>
public static class ScryfallListQuery
{
    /// <summary>
    /// Gives the most entries one query reads, so one request cannot make the server do unbounded work.
    /// </summary>
    public const int MaximumEntries = 20_000;

    /// <summary>
    /// Names the group that holds cards with no value for the group field, as it is written in the result.
    /// </summary>
    private const string NoneKey = "none";

    /// <summary>
    /// Stands for a missing group value, so it never merges with a real value that is the text <c>none</c>.
    /// </summary>
    private sealed class MissingValue
    {
        /// <summary>
        /// Gets the one instance.
        /// </summary>
        internal static MissingValue Instance { get; } = new();
    }

    /// <summary>
    /// Gives the number of decimal places an average is rounded to.
    /// </summary>
    private const int AverageDecimals = 6;

    /// <summary>
    /// Lists the operators in the order they are published.
    /// </summary>
    public static IReadOnlyList<string> Operators { get; } = ["eq", "ne", "lt", "lte", "gt", "gte", "in", "contains"];

    /// <summary>
    /// Lists every field name a query accepts, in the order they are published.
    /// </summary>
    public static IReadOnlyList<string> FieldNames => ScryfallListFields.Names;

    /// <summary>
    /// Runs one query over the entries of a list.
    /// </summary>
    public static OperationResult<ScryfallListQueryResult> Run(
        IReadOnlyList<ScryfallListEntry> entries,
        ScryfallListQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(request);
        OperationResult<Plan> planned = Validate(request);
        if (planned is not OperationSuccess<Plan> success)
        {
            return OperationFailure.Forward<Plan, ScryfallListQueryResult>(planned);
        }

        if (entries.Count > MaximumEntries)
        {
            return new OperationInvalidInput(
                "list-too-large",
                $"The list has {entries.Count} cards, which is more than the limit of {MaximumEntries}. " +
                "Narrow the source, for example with a more specific search.");
        }

        Plan plan = success.Data;
        List<ScryfallListEntry> matched = Filter(entries, plan);
        Dictionary<string, Group> groups = Build(matched, plan, out bool multiValued);
        List<ScryfallOrderedObject> output = [];
        foreach (Group group in groups.Values.OrderBy(value => value, new GroupComparer()))
        {
            output.Add(Describe(group, plan));
        }

        ScryfallListRows? rows = plan.Rows is null ? null : ReadRows(matched, plan);
        return new OperationSuccess<ScryfallListQueryResult>(new ScryfallListQueryResult(
            new ScryfallListMatched(DistinctCards(matched), matched.Sum(entry => (long)entry.Quantity)),
            output,
            multiValued,
            MissingFor(entries, matched, plan),
            rows,
            Limits(plan)));
    }

    /// <summary>
    /// Checks every part of a request and resolves field names, so the run itself cannot fail.
    /// </summary>
    private static OperationResult<Plan> Validate(ScryfallListQueryRequest request)
    {
        List<Check> checks = [];
        for (int index = 0; index < request.Where.Count; index++)
        {
            OperationResult<Check> check = ValidateCondition(request.Where[index], index, request.HasDeckFields);
            if (check is not OperationSuccess<Check> parsed)
            {
                return OperationFailure.Forward<Check, Plan>(check);
            }

            checks.Add(parsed.Data);
        }

        if (request.GroupBy.Count > 2)
        {
            return new OperationInvalidInput(
                "too-many-group-fields",
                "groupBy takes at most two fields.",
                "groupBy");
        }

        List<ScryfallListField> groupFields = [];
        for (int index = 0; index < request.GroupBy.Count; index++)
        {
            OperationResult<ScryfallListField> field = ResolveField(
                request.GroupBy[index],
                $"groupBy[{index}]",
                request.HasDeckFields,
                allowLongText: false);
            if (field is not OperationSuccess<ScryfallListField> resolved)
            {
                return OperationFailure.Forward<ScryfallListField, Plan>(field);
            }

            if (groupFields.Any(existing => existing.Name == resolved.Data.Name))
            {
                return new OperationInvalidInput(
                    "invalid-field",
                    $"groupBy names the field '{resolved.Data.Name}' more than once.",
                    $"groupBy[{index}]");
            }

            groupFields.Add(resolved.Data);
        }

        OperationResult<List<MetricPlan>> metrics = ValidateMetrics(request);
        if (metrics is not OperationSuccess<List<MetricPlan>> metricPlans)
        {
            return OperationFailure.Forward<List<MetricPlan>, Plan>(metrics);
        }

        OperationResult<RowPlan?> rows = ValidateRows(request);
        if (rows is not OperationSuccess<RowPlan?> rowPlan)
        {
            return OperationFailure.Forward<RowPlan?, Plan>(rows);
        }

        return new OperationSuccess<Plan>(new Plan(checks, groupFields, metricPlans.Data, rowPlan.Data));
    }

    /// <summary>
    /// Finds a field by name and checks that this list and this use accept it.
    /// </summary>
    private static OperationResult<ScryfallListField> ResolveField(
        string name,
        string path,
        bool hasDeckFields,
        bool allowLongText)
    {
        ScryfallListField? field = ScryfallListFields.Find(name);
        if (field is null || (field.DeckOnly && !hasDeckFields) || (field.TextOperatorsOnly && !allowLongText))
        {
            return new OperationInvalidInput(
                "invalid-field",
                $"Unknown or unavailable field '{name}'. Allowed fields: " +
                    $"{AllowedFields(hasDeckFields, allowLongText)}.",
                path);
        }

        return new OperationSuccess<ScryfallListField>(field);
    }

    /// <summary>
    /// Lists the field names allowed for a use, for error messages.
    /// </summary>
    private static string AllowedFields(bool hasDeckFields, bool allowLongText)
    {
        return string.Join(
            ", ",
            ScryfallListFields.All
                .Where(field => (!field.DeckOnly || hasDeckFields) && (!field.TextOperatorsOnly || allowLongText))
                .Select(field => field.Name));
    }

    /// <summary>
    /// Checks one filter: its field, its operator for that field, and the type of its value.
    /// </summary>
    private static OperationResult<Check> ValidateCondition(ScryfallListCondition condition, int index, bool deck)
    {
        string path = $"where[{index}]";
        OperationResult<ScryfallListField> found = ResolveField(condition.Field, path + ".field", deck, true);
        if (found is not OperationSuccess<ScryfallListField> resolved)
        {
            return OperationFailure.Forward<ScryfallListField, Check>(found);
        }

        ScryfallListField field = resolved.Data;
        string[] allowed = AllowedOperators(field);
        if (!allowed.Contains(condition.Op, StringComparer.Ordinal))
        {
            return new OperationInvalidInput(
                "invalid-operator",
                $"Field '{field.Name}' accepts only these operators: {string.Join(", ", allowed)}.",
                path + ".op");
        }

        if (condition.Op == "in" && condition.Value.ValueKind != JsonValueKind.Array)
        {
            return new OperationInvalidInput("invalid-value", "The operator in needs an array of values.", path
                + ".value");
        }

        List<object> values = [];
        IEnumerable<JsonElement> items = condition.Op == "in" && condition.Value.ValueKind == JsonValueKind.Array
            ? condition.Value.EnumerateArray().ToArray()
            : [condition.Value];
        foreach (JsonElement item in items)
        {
            object? value = ReadValue(field, item);
            if (value is null)
            {
                return new OperationInvalidInput(
                    "invalid-value",
                    $"The value for field '{field.Name}' must be {DescribeKind(field.Kind)}" +
                    (condition.Op == "in" ? ", or an array of them for in." : "."),
                    path + ".value");
            }

            values.Add(value);
        }

        if (values.Count == 0)
        {
            return new OperationInvalidInput("invalid-value", "The operator in needs at least one value.", path
                + ".value");
        }

        return new OperationSuccess<Check>(new Check(field, ParseOperator(condition.Op), values));
    }

    /// <summary>
    /// Turns an operator name that has been checked against the allowed list into its enum value.
    /// </summary>
    private static ListOperator ParseOperator(string name)
    {
        return name switch
        {
            "eq" => ListOperator.Eq,
            "ne" => ListOperator.Ne,
            "lt" => ListOperator.Lt,
            "lte" => ListOperator.Lte,
            "gt" => ListOperator.Gt,
            "gte" => ListOperator.Gte,
            "in" => ListOperator.In,
            "contains" => ListOperator.Contains,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Turns a metric name that has been checked into its enum value.
    /// </summary>
    private static MetricFunction ParseFunction(string name)
    {
        return name switch
        {
            "quantity" => MetricFunction.Quantity,
            "cards" => MetricFunction.Cards,
            "pips" => MetricFunction.Pips,
            "sources" => MetricFunction.Sources,
            "sum" => MetricFunction.Sum,
            "avg" => MetricFunction.Avg,
            "min" => MetricFunction.Min,
            "max" => MetricFunction.Max,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Lists the operators a field accepts, by the kind of value it holds.
    /// </summary>
    private static string[] AllowedOperators(ScryfallListField field)
    {
        if (field.TextOperatorsOnly)
        {
            return ["contains"];
        }

        return field.Kind switch
        {
            ScryfallListFieldKind.Text => ["eq", "ne", "in", "contains"],
            ScryfallListFieldKind.Number => ["eq", "ne", "lt", "lte", "gt", "gte", "in"],
            ScryfallListFieldKind.Bool => ["eq", "ne"],
            _ => ["in", "contains"],
        };
    }

    /// <summary>
    /// Reads a JSON value as the type a field compares with, or null when the type is wrong.
    /// </summary>
    private static object? ReadValue(ScryfallListField field, JsonElement item)
    {
        switch (field.Kind)
        {
            case ScryfallListFieldKind.Number:
                return item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out decimal number)
                    ? number
                    : null;
            case ScryfallListFieldKind.Bool:
                return item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : null;
            default:
                return item.ValueKind == JsonValueKind.String ? item.GetString() : null;
        }
    }

    /// <summary>
    /// Names a value kind for error messages.
    /// </summary>
    private static string DescribeKind(ScryfallListFieldKind kind)
    {
        return kind switch
        {
            ScryfallListFieldKind.Number => "a number",
            ScryfallListFieldKind.Bool => "true or false",
            _ => "text",
        };
    }

    /// <summary>
    /// Checks the metric names and resolves the numeric fields they read.
    /// </summary>
    private static OperationResult<List<MetricPlan>> ValidateMetrics(ScryfallListQueryRequest request)
    {
        IReadOnlyList<string> names = request.Metrics.Count == 0 ? ["quantity", "cards"] : request.Metrics;
        List<MetricPlan> plans = [];
        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            if (plans.Any(existing => existing.Name == name))
            {
                return new OperationInvalidInput(
                    "invalid-metric",
                    $"The metric '{name}' is named more than once.",
                    $"metrics[{index}]");
            }

            if (name is "quantity" or "cards" or "pips" or "sources")
            {
                plans.Add(new MetricPlan(name, ParseFunction(name), null));
                continue;
            }

            int colon = name.IndexOf(':', StringComparison.Ordinal);
            string function = colon < 0 ? string.Empty : name[..colon];
            if (function is not ("sum" or "avg" or "min" or "max"))
            {
                return new OperationInvalidInput(
                    "invalid-metric",
                    $"Unknown metric '{name}'. Allowed metrics: quantity, cards, pips, sources, and " +
                    "sum:, avg:, min:, or max: followed by a number field name.",
                    $"metrics[{index}]");
            }

            ScryfallListField? field = ScryfallListFields.Find(name[(colon + 1)..]);
            if (field is null || field.Kind != ScryfallListFieldKind.Number || (field.DeckOnly
                && !request.HasDeckFields))
            {
                return new OperationInvalidInput(
                    "invalid-metric",
                    $"Metric '{name}' needs a number field. Number fields: {NumberFields(request.HasDeckFields)}.",
                    $"metrics[{index}]");
            }

            plans.Add(new MetricPlan(name, ParseFunction(function), field));
        }

        return new OperationSuccess<List<MetricPlan>>(plans);
    }

    /// <summary>
    /// Lists the number fields, for error messages.
    /// </summary>
    private static string NumberFields(bool hasDeckFields)
    {
        return string.Join(
            ", ",
            ScryfallListFields.All
                .Where(field => field.Kind == ScryfallListFieldKind.Number && (!field.DeckOnly || hasDeckFields))
                .Select(field => field.Name));
    }

    /// <summary>
    /// Checks the row fields, sort keys, and paging of a request.
    /// </summary>
    private static OperationResult<RowPlan?> ValidateRows(ScryfallListQueryRequest request)
    {
        if (request.Rows is null)
        {
            return new OperationSuccess<RowPlan?>(null);
        }

        List<ScryfallListField> fields = [];
        IReadOnlyList<string> requested = request.Rows.Fields.Count == 0 ? ["name"] : request.Rows.Fields;
        for (int index = 0; index < requested.Count; index++)
        {
            OperationResult<ScryfallListField> field = ResolveField(
                requested[index],
                $"rows.fields[{index}]",
                request.HasDeckFields,
                allowLongText: true);
            if (field is not OperationSuccess<ScryfallListField> resolved)
            {
                return OperationFailure.Forward<ScryfallListField, RowPlan?>(field);
            }

            if (fields.Any(existing => existing.Name == resolved.Data.Name))
            {
                return new OperationInvalidInput(
                    "invalid-field",
                    $"rows.fields names the field '{resolved.Data.Name}' more than once.",
                    $"rows.fields[{index}]");
            }

            fields.Add(resolved.Data);
        }

        List<(ScryfallListField Field, bool Descending)> sorts = [];
        for (int index = 0; index < request.Rows.SortBy.Count; index++)
        {
            ScryfallListSort sort = request.Rows.SortBy[index];
            OperationResult<ScryfallListField> field = ResolveField(
                sort.Field,
                $"rows.sortBy[{index}].field",
                request.HasDeckFields,
                allowLongText: false);
            if (field is not OperationSuccess<ScryfallListField> resolved)
            {
                return OperationFailure.Forward<ScryfallListField, RowPlan?>(field);
            }

            if (resolved.Data.Kind == ScryfallListFieldKind.TextList || sort.Direction is not ("asc" or "desc"))
            {
                return new OperationInvalidInput(
                    "invalid-sort",
                    "A sort key needs a field that holds one value (not a list) and the direction asc or desc.",
                    $"rows.sortBy[{index}]");
            }

            sorts.Add((resolved.Data, sort.Direction == "desc"));
        }

        return new OperationSuccess<RowPlan?>(new RowPlan(fields, sorts, request.Rows.Offset, request.Rows.PageSize));
    }

    /// <summary>
    /// Keeps the entries that satisfy every filter. A card with no value for a filtered field never matches.
    /// </summary>
    private static List<ScryfallListEntry> Filter(IReadOnlyList<ScryfallListEntry> entries, Plan plan)
    {
        List<ScryfallListEntry> matched = [];
        foreach (ScryfallListEntry entry in entries)
        {
            bool keep = true;
            foreach (Check check in plan.Checks)
            {
                if (!Matches(check, check.Field.Read(entry)))
                {
                    keep = false;
                    break;
                }
            }

            if (keep)
            {
                matched.Add(entry);
            }
        }

        return matched;
    }

    /// <summary>
    /// Applies one filter to one value.
    /// </summary>
    private static bool Matches(Check check, object? actual)
    {
        if (actual is null)
        {
            return false;
        }

        if (actual is IReadOnlyList<string> list)
        {
            return check.Values.Any(value => list.Contains((string)value, StringComparer.OrdinalIgnoreCase));
        }

        return check.Op switch
        {
            ListOperator.In => check.Values.Any(value => Compare(actual, value) == 0),
            ListOperator.Contains => ((string)actual).Contains(
                (string)check.Values[0],
                StringComparison.OrdinalIgnoreCase),
            ListOperator.Eq => Compare(actual, check.Values[0]) == 0,
            ListOperator.Ne => Compare(actual, check.Values[0]) != 0,
            ListOperator.Lt => Compare(actual, check.Values[0]) < 0,
            ListOperator.Lte => Compare(actual, check.Values[0]) <= 0,
            ListOperator.Gt => Compare(actual, check.Values[0]) > 0,
            ListOperator.Gte => Compare(actual, check.Values[0]) >= 0,
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Compares two values of the same kind: numbers by value, text without regard to letter case, then true after
    /// false.
    /// </summary>
    private static int Compare(object first, object second)
    {
        return (first, second) switch
        {
            (decimal a, decimal b) => a.CompareTo(b),
            (bool a, bool b) => a.CompareTo(b),
            (string a, string b) => StringComparer.OrdinalIgnoreCase.Compare(a, b),
            _ => 0,
        };
    }

    /// <summary>
    /// Adds each matched entry to every group it belongs to.
    /// </summary>
    private static Dictionary<string, Group> Build(List<ScryfallListEntry> matched, Plan plan, out bool multiValued)
    {
        multiValued = false;
        Dictionary<string, Group> groups = new(StringComparer.Ordinal);
        foreach (ScryfallListEntry entry in matched)
        {
            List<object?[]> keys = Keys(entry, plan.GroupFields);
            if (keys.Count > 1)
            {
                multiValued = true;
            }

            foreach (object?[] key in keys)
            {
                string id = string.Join('\u001f', key.Select(KeyText));
                if (!groups.TryGetValue(id, out Group? group))
                {
                    group = new Group(key);
                    groups[id] = group;
                }

                group.Add(entry, plan);
            }
        }

        if (plan.GroupFields.Count == 0 && groups.Count == 0)
        {
            groups[string.Empty] = new Group([]);
        }

        return groups;
    }

    /// <summary>
    /// Lists the group keys one entry belongs to: the combinations of its values for each group field, with
    /// <c>none</c> standing for a field with no value.
    /// </summary>
    private static List<object?[]> Keys(ScryfallListEntry entry, IReadOnlyList<ScryfallListField> fields)
    {
        List<object?[]> keys = [[]];
        foreach (ScryfallListField field in fields)
        {
            List<object> values = ValuesOf(field.Read(entry));
            List<object?[]> next = [];
            foreach (object?[] prefix in keys)
            {
                foreach (object value in values)
                {
                    next.Add([.. prefix, value]);
                }
            }

            keys = next;
        }

        return keys;
    }

    /// <summary>
    /// Lists the values an entry has for a field, or the single value <c>none</c> when it has none.
    /// </summary>
    private static List<object> ValuesOf(object? value)
    {
        if (value is IReadOnlyList<string> list)
        {
            return list.Count == 0 ? [MissingValue.Instance] : [.. list.Select(item => (object)item)];
        }

        return [value ?? MissingValue.Instance];
    }

    /// <summary>
    /// Writes a group key part as text that tells different values apart.
    /// </summary>
    private static string KeyText(object? part)
    {
        return part switch
        {
            decimal number => "n" + number.ToString(CultureInfo.InvariantCulture),
            bool flag => flag ? "b1" : "b0",
            string text => "s" + text,
            MissingValue => "m",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Builds the output object of one group.
    /// </summary>
    private static ScryfallOrderedObject Describe(Group group, Plan plan)
    {
        ScryfallOrderedObject key = new();
        for (int index = 0; index < plan.GroupFields.Count; index++)
        {
            key.Add(plan.GroupFields[index].Name, KeyValue(group.Key[index]));
        }

        ScryfallOrderedObject output = new ScryfallOrderedObject().Add("key", key);
        foreach (MetricPlan metric in plan.Metrics)
        {
            output.Add(metric.Name, group.Read(metric));
        }

        return output;
    }

    /// <summary>
    /// Converts a key part or a row value to the value written in the result: whole numbers as integers.
    /// </summary>
    private static object? KeyValue(object? part)
    {
        return part switch
        {
            MissingValue => NoneKey,
            decimal number when decimal.Truncate(number) == number => (long)number,
            _ => part,
        };
    }

    /// <summary>
    /// Counts the distinct cards in a list of entries.
    /// </summary>
    private static int DistinctCards(IReadOnlyList<ScryfallListEntry> entries)
    {
        return entries.Select(entry => CardKey(entry.Card)).Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>
    /// Gives the text that says which card an entry is: its Oracle ID, or its name when it has none.
    /// </summary>
    private static string CardKey(ScryfallCard card)
    {
        return ScryfallCardSets.KeyOf(card, ScryfallMatchBy.Oracle);
    }

    /// <summary>
    /// Reports the fields some cards had no value for. A filtered field is counted over the whole list, because a
    /// card without a value can never match; a summed field is counted over the matched cards.
    /// </summary>
    private static List<ScryfallMissingValues> MissingFor(
        IReadOnlyList<ScryfallListEntry> all,
        IReadOnlyList<ScryfallListEntry> matched,
        Plan plan)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        List<string> order = [];
        foreach (Check check in plan.Checks)
        {
            Record(check.Field, all, counts, order);
        }

        foreach (MetricPlan metric in plan.Metrics)
        {
            if (metric.Field is not null && !counts.ContainsKey(metric.Field.Name))
            {
                Record(metric.Field, matched, counts, order);
            }
        }

        List<ScryfallMissingValues> result = [];
        foreach (string name in order)
        {
            if (counts[name] > 0)
            {
                result.Add(new ScryfallMissingValues(name, counts[name]));
            }
        }

        return result;
    }

    /// <summary>
    /// Counts the distinct cards of a list that have no value for one field.
    /// </summary>
    private static void Record(
        ScryfallListField field,
        IReadOnlyList<ScryfallListEntry> entries,
        Dictionary<string, int> counts,
        List<string> order)
    {
        if (counts.ContainsKey(field.Name))
        {
            return;
        }

        order.Add(field.Name);
        counts[field.Name] = entries
            .Where(entry => field.Read(entry) is null)
            .Select(entry => CardKey(entry.Card))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>
    /// Builds the sorted rows and reads one page of them.
    /// </summary>
    private static ScryfallListRows ReadRows(List<ScryfallListEntry> matched, Plan plan)
    {
        RowPlan rows = plan.Rows!;
        List<ScryfallListEntry> sorted = [.. matched];
        sorted.Sort((first, second) => CompareRows(first, second, rows));
        List<ScryfallOrderedObject> items = [];
        foreach (ScryfallListEntry entry in sorted.Skip(rows.Offset).Take(rows.PageSize))
        {
            ScryfallOrderedObject row = new ScryfallOrderedObject()
                .Add("oracleId", ScryfallConciseFieldTable.EffectiveOracleId(entry.Card));
            foreach (ScryfallListField field in rows.Fields)
            {
                row.Add(field.Name, KeyValue(field.Read(entry)));
            }

            items.Add(row);
        }

        return new ScryfallListRows(items, sorted.Count);
    }

    /// <summary>
    /// Orders rows by the sort keys, with missing values last, then by name, Oracle ID, and printing ID.
    /// </summary>
    private static int CompareRows(ScryfallListEntry first, ScryfallListEntry second, RowPlan rows)
    {
        foreach ((ScryfallListField field, bool descending) in rows.Sorts)
        {
            object? a = field.Read(first);
            object? b = field.Read(second);
            if (a is null || b is null)
            {
                if (a is null && b is null)
                {
                    continue;
                }

                return a is null ? 1 : -1;
            }

            int result = Compare(a, b);
            if (result != 0)
            {
                return descending ? -result : result;
            }
        }

        return ScryfallCardSets.Compare(first.Card, second.Card);
    }

    /// <summary>
    /// Lists the notes on what the numbers do not cover.
    /// </summary>
    private static List<string> Limits(Plan plan)
    {
        List<string> limits = [];
        if (plan.Metrics.Any(metric => metric.Function == MetricFunction.Sources))
        {
            limits.Add(
                "Sources count the colors Scryfall lists as produced mana. Whether a land enters tapped is not " +
                "modeled, and cards that only fetch or search are counted under noProducedMana.");
        }

        if (plan.Metrics.Any(metric => metric.Function == MetricFunction.Avg))
        {
            limits.Add($"Averages are rounded to {AverageDecimals} decimal places.");
        }

        if (plan.Metrics.Any(metric => metric.Function == MetricFunction.Pips))
        {
            limits.Add("Pips count printed mana symbols only; cost changes and alternate costs are not modeled.");
        }

        return limits;
    }

    /// <summary>
    /// Holds a request after its names and values were checked.
    /// </summary>
    private sealed record Plan(
        IReadOnlyList<Check> Checks,
        IReadOnlyList<ScryfallListField> GroupFields,
        IReadOnlyList<MetricPlan> Metrics,
        RowPlan? Rows)
    {
        /// <summary>
        /// Gets the number fields the metrics read, each once, so an entry is added to a total only once.
        /// </summary>
        internal IReadOnlyList<ScryfallListField> NumberFields { get; } =
            [
                .. Metrics
                    .Where(metric => metric.Field is not null)
                    .Select(metric => metric.Field!)
                    .DistinctBy(field => field.Name),
            ];
    }

    /// <summary>
    /// Holds one checked filter.
    /// </summary>
    private sealed record Check(ScryfallListField Field, ListOperator Op, IReadOnlyList<object> Values);

    /// <summary>
    /// Holds one checked metric: its published name, its function, and the number field it reads.
    /// </summary>
    private sealed record MetricPlan(string Name, MetricFunction Function, ScryfallListField? Field);

    /// <summary>
    /// Names the operators of a filter, parsed once when the request is checked.
    /// </summary>
    private enum ListOperator
    {
        Eq,
        Ne,
        Lt,
        Lte,
        Gt,
        Gte,
        In,
        Contains,
    }

    /// <summary>
    /// Names the metrics, parsed once when the request is checked.
    /// </summary>
    private enum MetricFunction
    {
        Quantity,
        Cards,
        Pips,
        Sources,
        Sum,
        Avg,
        Min,
        Max,
    }

    /// <summary>
    /// Holds the checked row options.
    /// </summary>
    private sealed record RowPlan(
        IReadOnlyList<ScryfallListField> Fields,
        IReadOnlyList<(ScryfallListField Field, bool Descending)> Sorts,
        int Offset,
        int PageSize);

    /// <summary>
    /// Orders groups by key: numbers by value, text without regard to letter case, and the none group last.
    /// </summary>
    private sealed class GroupComparer : IComparer<Group>
    {
        /// <inheritdoc/>
        public int Compare(Group? first, Group? second)
        {
            for (int index = 0; index < first!.Key.Length; index++)
            {
                object? a = first.Key[index];
                object? b = second!.Key[index];
                bool aNone = a is MissingValue;
                bool bNone = b is MissingValue;
                if (aNone || bNone)
                {
                    if (aNone && bNone)
                    {
                        continue;
                    }

                    return aNone ? 1 : -1;
                }

                int result = ScryfallListQuery.Compare(a!, b!);
                if (result != 0)
                {
                    return result;
                }
            }

            return 0;
        }
    }

    /// <summary>
    /// Collects the metrics of one group while entries are added to it.
    /// </summary>
    private sealed class Group
    {
        /// <summary>
        /// Holds the distinct cards of the group.
        /// </summary>
        private readonly HashSet<string> cards = new(StringComparer.Ordinal);

        /// <summary>
        /// Holds the running sums, counts, minimums, and maximums of each numeric metric field.
        /// </summary>
        private readonly Dictionary<string, Totals> totals = new(StringComparer.Ordinal);

        /// <summary>
        /// Holds the colored symbol counts of the group.
        /// </summary>
        private readonly ManaPips pips = new();

        /// <summary>
        /// Holds the produced-mana counts of the group, by color then the two special counts.
        /// </summary>
        private readonly Dictionary<string, long> sources = new(StringComparer.Ordinal);

        /// <summary>
        /// Counts the copies in the group.
        /// </summary>
        private long quantity;

        /// <summary>
        /// Creates a group with its key.
        /// </summary>
        internal Group(object?[] key)
        {
            Key = key;
        }

        /// <summary>
        /// Gets the key parts, one for each group field.
        /// </summary>
        internal object?[] Key { get; }

        /// <summary>
        /// Adds one entry to the group.
        /// </summary>
        internal void Add(ScryfallListEntry entry, Plan plan)
        {
            quantity += entry.Quantity;
            cards.Add(CardKey(entry.Card));
            foreach (MetricPlan metric in plan.Metrics)
            {
                switch (metric.Function)
                {
                    case MetricFunction.Pips:
                        pips.AddScaled(ScryfallListFields.CountPips(ScryfallListFields.ManaCostOf(entry.Card)),
                            entry.Quantity);
                        break;
                    case MetricFunction.Sources:
                        AddSources(entry);
                        break;
                }
            }

            foreach (ScryfallListField field in plan.NumberFields)
            {
                AddNumber(field, entry);
            }
        }

        /// <summary>
        /// Reads the value of one metric for the group.
        /// </summary>
        internal object? Read(MetricPlan metric)
        {
            switch (metric.Function)
            {
                case MetricFunction.Quantity:
                    return quantity;
                case MetricFunction.Cards:
                    return cards.Count;
                case MetricFunction.Pips:
                    return PipsObject();
                case MetricFunction.Sources:
                    return SourcesObject();
                default:
                    return totals.TryGetValue(metric.Field!.Name, out Totals? value) ? value.Read(metric.Function)
                        : null;
            }
        }

        /// <summary>
        /// Adds the value of a number field, multiplied by the copies, unless the card has no value.
        /// </summary>
        private void AddNumber(ScryfallListField field, ScryfallListEntry entry)
        {
            if (field.Read(entry) is not decimal value)
            {
                return;
            }

            if (!totals.TryGetValue(field.Name, out Totals? current))
            {
                current = new Totals();
                totals[field.Name] = current;
            }

            current.Add(value, entry.Quantity);
        }

        /// <summary>
        /// Counts the copies of a card for each color it produces, or under the two special counts.
        /// </summary>
        private void AddSources(ScryfallListEntry entry)
        {
            IReadOnlyList<string> produced = ScryfallListFields.ProducedColors(entry.Card);
            foreach (string color in produced.Distinct(StringComparer.Ordinal))
            {
                sources[color] = sources.GetValueOrDefault(color) + entry.Quantity;
            }

            if (ScryfallCardFaces.ManaColors.Take(5).All(color => produced.Contains(color, StringComparer.Ordinal)))
            {
                sources["anyColor"] = sources.GetValueOrDefault("anyColor") + entry.Quantity;
            }

            bool isLand = ScryfallCardFaces.IsLand(entry.Card, frontOnly: false);
            if (produced.Count == 0 && isLand)
            {
                sources["noProducedMana"] = sources.GetValueOrDefault("noProducedMana") + entry.Quantity;
            }
        }

        /// <summary>
        /// Builds the pips object with the nonzero counts in display order.
        /// </summary>
        private ScryfallOrderedObject PipsObject()
        {
            ScryfallOrderedObject result = new();
            foreach (string color in ScryfallCardFaces.ManaColors)
            {
                if (pips.Get(color) > 0)
                {
                    result.Add(color, pips.Get(color));
                }
            }

            if (pips.Hybrid > 0)
            {
                result.Add("hybrid", pips.Hybrid);
            }

            if (pips.Phyrexian > 0)
            {
                result.Add("phyrexian", pips.Phyrexian);
            }

            return result;
        }

        /// <summary>
        /// Builds the sources object with the nonzero counts in display order.
        /// </summary>
        private ScryfallOrderedObject SourcesObject()
        {
            ScryfallOrderedObject result = new();
            foreach (string name in (string[])["W", "U", "B", "R", "G", "C", "anyColor", "noProducedMana"])
            {
                if (sources.TryGetValue(name, out long count) && count > 0)
                {
                    result.Add(name, count);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Keeps the exact sums and extremes of one number field.
    /// </summary>
    private sealed class Totals
    {
        /// <summary>
        /// Holds the sum of value times copies.
        /// </summary>
        private decimal sum;

        /// <summary>
        /// Holds the number of copies that had a value.
        /// </summary>
        private long copies;

        /// <summary>
        /// Holds the smallest value seen, or null before any.
        /// </summary>
        private decimal? minimum;

        /// <summary>
        /// Holds the largest value seen, or null before any.
        /// </summary>
        private decimal? maximum;

        /// <summary>
        /// Adds one value that stands for a number of copies.
        /// </summary>
        internal void Add(decimal value, int quantity)
        {
            sum += value * quantity;
            copies += quantity;
            minimum = minimum is null ? value : Math.Min(minimum.Value, value);
            maximum = maximum is null ? value : Math.Max(maximum.Value, value);
        }

        /// <summary>
        /// Reads one function of the totals as an exact decimal string.
        /// </summary>
        internal string? Read(MetricFunction function)
        {
            return function switch
            {
                MetricFunction.Sum => sum.ToString(CultureInfo.InvariantCulture),
                MetricFunction.Avg => copies == 0
                    ? null
                    : Math.Round(sum / copies, AverageDecimals, MidpointRounding.ToEven)
                        .ToString(CultureInfo.InvariantCulture),
                MetricFunction.Min => minimum?.ToString(CultureInfo.InvariantCulture),
                MetricFunction.Max => maximum?.ToString(CultureInfo.InvariantCulture),
                _ => throw new UnreachableException(),
            };
        }
    }
}
