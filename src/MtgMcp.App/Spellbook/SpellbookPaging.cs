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
    /// Names the last source offset a cursor may hold, the adapter's offset cap.
    /// </summary>
    private const int MaximumOffset = 1_000;

    /// <summary>
    /// Reads the source offset a cursor holds, or 0 without a cursor, after checking the cursor was issued for this
    /// request.
    /// </summary>
    internal static OperationResult<int> Offset(string? cursor, PageCursorBinding binding)
    {
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(cursor, binding);
        return decoded switch
        {
            OperationSuccess<PageCursorPosition> success => new OperationSuccess<int>(success.Data.Offset),
            _ => OperationFailure.Forward<PageCursorPosition, int>(decoded),
        };
    }

    /// <summary>
    /// Cuts the rows of a source page that do not fit the result size limit and writes the cursor for the next page.
    /// A page that was cut reports it, and a cut of the grouped lists of a deck result ends the cursor because the
    /// source offset no longer names the first row that was left out.
    /// </summary>
    /// <param name="source">The source page as acquired.</param>
    /// <param name="otherBytes">The bytes the rest of the tool result takes besides the source page.</param>
    /// <param name="binding">The state the cursor is bound to.</param>
    internal static SpellbookShapedPage Shape(SpellbookEvidence source, int otherBytes, PageCursorBinding binding)
    {
        int budget = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes - otherBytes;
        SpellbookPageRequest page = source.Request.Page!;
        bool providerHasMore = source.Data.TryGetProperty("next", out JsonElement next) &&
            next.ValueKind == JsonValueKind.String;
        if (ResultSize.Measure(source) <= budget)
        {
            string? following = providerHasMore ? Next(page.Offset + page.Limit, binding) : null;
            return new SpellbookShapedPage(source, following, false);
        }

        JsonNode root = JsonNode.Parse(source.Data.GetRawText())!;
        List<JsonArray> lists = Lists(root);
        List<List<JsonNode?>> rows = [.. lists.Select(list => list.ToList())];
        foreach (JsonArray list in lists)
        {
            list.Clear();
        }

        SpellbookEvidence skeleton = source with { Data = JsonSerializer.SerializeToElement(root) };
        int allowance = budget - ResultSize.Measure(skeleton);
        bool single = lists.Count == 1 && root["results"] is JsonArray;
        int firstKept = 0;
        for (int index = 0; index < lists.Count; index++)
        {
            IEnumerable<int> sizes = rows[index]
                .Select(row => Encoding.UTF8.GetByteCount(row?.ToJsonString() ?? "null") + 1);
            int kept = ResultSizeLimit.CountWithin(sizes, allowance, out int used);
            kept = Math.Max(kept, single && rows[index].Count > 0 ? 1 : 0);
            allowance -= used;
            foreach (JsonNode? row in rows[index].Take(kept))
            {
                lists[index].Add(row);
            }

            firstKept = kept;
        }

        SpellbookEvidence cut = source with { Data = JsonSerializer.SerializeToElement(root) };
        string? nextCursor = single && (firstKept < rows[0].Count || providerHasMore)
            ? Next(page.Offset + firstKept, binding)
            : null;
        return new SpellbookShapedPage(cut, nextCursor, true);
    }

    /// <summary>
    /// Writes the cursor for the page that starts at an offset, or null when the offset passes the source cap.
    /// </summary>
    private static string? Next(int offset, PageCursorBinding binding)
    {
        return offset > MaximumOffset ? null : PageCursor.Encode(PageCursorPosition.AtOffset(offset), binding);
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
