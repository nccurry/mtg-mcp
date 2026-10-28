using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.Scryfall;

namespace MtgMcp.App.CardLists;

/// <summary>
/// Limits a text to the field names <c>card_list_query</c> accepts for filters, groups, and sorts.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class ListFieldValuesAttribute()
    : AllowedValuesAttribute([.. ScryfallListQuery.FieldNames.Cast<object>()]);

/// <summary>
/// Limits a text to the operators of a filter.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
internal sealed class ListOperatorValuesAttribute()
    : AllowedValuesAttribute("eq", "ne", "lt", "lte", "gt", "gte", "in", "contains");

/// <summary>
/// Describes one filter of <c>card_list_query</c>.
/// </summary>
internal sealed record CardListFilterInput(
    [property: Description("Field to test. Deck fields (zone, category, quantity) work only for deck sources."),
     ListFieldValues]
    string Field,
    [property: Description(
        "Operator: eq, ne, lt, lte, gt, gte, in, or contains. Number fields take the comparisons, text fields take " +
        "eq, ne, in, and contains (substring), list fields take in and contains (the list holds the value), and " +
        "oracleText takes only contains."),
     ListOperatorValues]
    string Op,
    [property: Description("Value to compare with: text, a number, or true or false; an array of them for in.")]
    JsonElement Value);

/// <summary>
/// Describes one sort key of the rows of <c>card_list_query</c>.
/// </summary>
internal sealed record CardListSortInput(
    [property: Description("Field to sort by. A field that holds a list cannot be sorted."), ListFieldValues]
    string Field,
    [property: Description("Direction: asc or desc."), AllowedValues("asc", "desc")]
    string Direction);

/// <summary>
/// Asks <c>card_list_query</c> for per-card rows.
/// </summary>
internal sealed record CardListRowsInput(
    [property: Description("Fields to return for each card, in order. Default: name. oracleId is always returned.")]
    IReadOnlyList<string>? Fields = null,
    [property: Description("Sort keys, applied in order. Default: name, then Oracle ID. Cards with no value come " +
        "last.")]
    IReadOnlyList<CardListSortInput>? SortBy = null,
    [property: Description("Rows per page, from 1 through 100. The default is 25.")]
    int? PageSize = null);

/// <summary>
/// Counts the distinct cards on each side and in the result of a comparison.
/// </summary>
internal sealed record CardListCounts(
    [property: JsonPropertyName("left")] int Left,
    [property: JsonPropertyName("right")] int Right,
    [property: JsonPropertyName("result")] int Result,
    [property: JsonPropertyName("unmatched")] int Unmatched);

/// <summary>
/// Returns one page of a set operation over two card lists.
/// </summary>
internal sealed record CardListsCompareResult(
    [property: JsonPropertyName("counts")] CardListCounts Counts,
    [property: JsonPropertyName("page")] ScryfallPage<ScryfallCardView> Page,
    [property: JsonPropertyName("unmatched")] IReadOnlyList<CardListUnmatched> Unmatched,
    [property: JsonPropertyName("sources")] IReadOnlyList<CardListSourceInfo> Sources,
    [property: JsonPropertyName("fingerprint")] string Fingerprint)
    : ScryfallFormattedResponse
{
    /// <summary>
    /// Gets how many unmatched entries the list leaves out to keep the result small, or null when it lists all.
    /// </summary>
    [JsonPropertyName("unmatchedOmitted")]
    [JsonPropertyOrder(104)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnmatchedOmitted { get; init; }
}

/// <summary>
/// Holds one page of the per-card rows of a list query.
/// </summary>
internal sealed record CardListRowsPage(
    [property: JsonPropertyName("items")] IReadOnlyList<ScryfallOrderedObject> Items,
    [property: JsonPropertyName("totalRows")] int TotalRows,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>
/// Returns the answer of one list query: grouped totals, optional rows, and what the numbers do not cover.
/// </summary>
internal sealed record CardListQueryResult(
    [property: JsonPropertyName("sources")] IReadOnlyList<CardListSourceInfo> Sources,
    [property: JsonPropertyName("matched")] ScryfallListMatched Matched,
    [property: JsonPropertyName("groups")] IReadOnlyList<ScryfallOrderedObject> Groups,
    [property: JsonPropertyName("multiValued")] bool MultiValued,
    [property: JsonPropertyName("missingValues")] IReadOnlyList<ScryfallMissingValues> MissingValues,
    [property: JsonPropertyName("unmatched")] IReadOnlyList<CardListUnmatched> Unmatched,
    [property: JsonPropertyName("rows")] CardListRowsPage? Rows,
    [property: JsonPropertyName("limits")] IReadOnlyList<string> Limits,
    [property: JsonPropertyName("fingerprint")] string Fingerprint)
{
    /// <summary>
    /// Gets how many unmatched entries the list leaves out to keep the result small, or null when it lists all.
    /// </summary>
    [JsonPropertyName("unmatchedOmitted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnmatchedOmitted { get; init; }

    /// <summary>
    /// Gets true when groups were left out to keep the result under the size limit.
    /// </summary>
    [JsonPropertyName("truncated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to change to get a smaller result when it was cut.
    /// </summary>
    [JsonPropertyName("narrow")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Narrow { get; init; }
}
