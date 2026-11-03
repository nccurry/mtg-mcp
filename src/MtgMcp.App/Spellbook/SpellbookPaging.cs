using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Spellbook;

/// <summary>
/// Carries one Commander Spellbook variant search page with the cursor for the next source page.
/// </summary>
internal sealed record SpellbookVariantPage(
    [property: JsonPropertyName("source")] SpellbookEvidence Source,
    [property: JsonPropertyName("nextCursor")] string? NextCursor,
    [property: JsonPropertyName("truncated")] bool Truncated);

/// <summary>
/// Holds one source page after the result size cut, and the cursor that continues from it.
/// </summary>
internal sealed record SpellbookShapedPage(SpellbookEvidence Source, string? NextCursor, bool Truncated);

/// <summary>
/// Turns the cursor argument into a source offset and cuts a source page to the result size limit, so the Commander
/// Spellbook tools page like every other paged tool.
/// </summary>
internal static class SpellbookPaging
{
    /// <summary>
    /// Reads the source offset a cursor holds, or 0 without a cursor, after checking the cursor was issued for this
    /// request and does not pass the source offset cap.
    /// </summary>
    internal static OperationResult<int> Offset(string? cursor, PageCursorBinding binding)
    {
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(cursor, binding);
        return decoded switch
        {
            OperationSuccess<PageCursorPosition> { Data.Offset: <= SpellbookPageOptions.MaximumOffset } success =>
                new OperationSuccess<int>(success.Data.Offset),
            OperationSuccess<PageCursorPosition> => PageCursor.Invalid(),
            _ => OperationFailure.Forward<PageCursorPosition, int>(decoded),
        };
    }

    /// <summary>
    /// Cuts the rows of a variant search page that do not fit the result size limit and writes the cursor that
    /// continues after the last row shown. At least one row is always shown.
    /// </summary>
    /// <param name="source">The source page as acquired.</param>
    /// <param name="binding">The state the cursor is bound to.</param>
    internal static SpellbookShapedPage ShapeVariants(SpellbookEvidence source, PageCursorBinding binding)
    {
        int budget = Budget(0);
        if (ResultSize.Measure(source) <= budget)
        {
            return new SpellbookShapedPage(source, NextPage(source, binding), false);
        }

        SpellbookEvidence cut = Cut(source, budget, 1);
        int kept = RowCount(cut);
        string? next = kept > 0 && (kept < RowCount(source) || HasMore(source))
            ? Next(source.Request.Page!.Offset + kept, binding)
            : null;
        return new SpellbookShapedPage(cut, next, true);
    }

    /// <summary>
    /// Cuts the grouped lists of a deck combo page that do not fit the result size limit. A cut page has no cursor
    /// because the source offset counts combos, not the grouped rows shown, so no offset names the first row left out.
    /// </summary>
    /// <param name="source">The source page as acquired.</param>
    /// <param name="otherBytes">The bytes the rest of the tool result takes besides the source page.</param>
    /// <param name="binding">The state the cursor is bound to.</param>
    internal static SpellbookShapedPage ShapeDeckCombos(
        SpellbookEvidence source,
        int otherBytes,
        PageCursorBinding binding)
    {
        int budget = Budget(otherBytes);
        return ResultSize.Measure(source) <= budget
            ? new SpellbookShapedPage(source, NextPage(source, binding), false)
            : new SpellbookShapedPage(Cut(source, budget, 0), null, true);
    }

    /// <summary>
    /// Counts the bytes a source page may take once the rest of the tool result is written.
    /// </summary>
    private static int Budget(int otherBytes)
    {
        return ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes - otherBytes;
    }

    /// <summary>
    /// Tells whether the source says it has a page after this one.
    /// </summary>
    private static bool HasMore(SpellbookEvidence source)
    {
        return source.Data.TryGetProperty("next", out JsonElement next) && next.ValueKind == JsonValueKind.String;
    }

    /// <summary>
    /// Writes the cursor for the source page after a page that was shown whole, or null on the last page.
    /// </summary>
    private static string? NextPage(SpellbookEvidence source, PageCursorBinding binding)
    {
        SpellbookPageRequest page = source.Request.Page!;
        return HasMore(source) ? Next(page.Offset + page.Limit, binding) : null;
    }

    /// <summary>
    /// Counts the rows in the results array of a variant page, or 0 when the results are not an array.
    /// </summary>
    private static int RowCount(SpellbookEvidence source)
    {
        return source.Data.TryGetProperty("results", out JsonElement results) &&
            results.ValueKind == JsonValueKind.Array
            ? results.GetArrayLength()
            : 0;
    }

    /// <summary>
    /// Keeps the leading rows of each list that fit the budget, and at least <paramref name="minimumKept"/> rows of
    /// each list that has them.
    /// </summary>
    private static SpellbookEvidence Cut(SpellbookEvidence source, int budget, int minimumKept)
    {
        JsonNode root = JsonNode.Parse(source.Data.GetRawText())!;
        List<JsonArray> lists = Lists(root);
        List<List<JsonNode?>> rows = [.. lists.Select(list => list.ToList())];
        foreach (JsonArray list in lists)
        {
            list.Clear();
        }

        SpellbookEvidence skeleton = source with { Data = JsonSerializer.SerializeToElement(root) };
        int allowance = budget - ResultSize.Measure(skeleton);
        for (int index = 0; index < lists.Count; index++)
        {
            IEnumerable<int> sizes = rows[index]
                .Select(row => Encoding.UTF8.GetByteCount(row?.ToJsonString() ?? "null") + 1);
            int kept = ResultSizeLimit.CountWithin(sizes, allowance, out int used);
            allowance -= used;
            kept = Math.Max(kept, Math.Min(minimumKept, rows[index].Count));
            foreach (JsonNode? row in rows[index].Take(kept))
            {
                lists[index].Add(row);
            }
        }

        return source with { Data = JsonSerializer.SerializeToElement(root) };
    }

    /// <summary>
    /// Writes the cursor for the page that starts at an offset, or null when the offset passes the source cap.
    /// </summary>
    private static string? Next(int offset, PageCursorBinding binding)
    {
        return offset > SpellbookPageOptions.MaximumOffset
            ? null
            : PageCursor.Encode(PageCursorPosition.AtOffset(offset), binding);
    }

    /// <summary>
    /// Finds the lists of a source page that hold its rows: the results array of a variant page, or every array of
    /// the results object of a deck page.
    /// </summary>
    private static List<JsonArray> Lists(JsonNode root)
    {
        return root["results"] switch
        {
            JsonArray array => [array],
            JsonObject results => [.. results.Select(pair => pair.Value).OfType<JsonArray>()],
            _ => [],
        };
    }
}
