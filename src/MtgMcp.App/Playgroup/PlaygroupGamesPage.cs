using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Playgroup;

/// <summary>
/// Carries one page of playgroup games with the cursor for the next page.
/// </summary>
internal sealed record PlaygroupGamesPage(
    [property: JsonPropertyName("source")] PlaygroupEvidence Source,
    [property: JsonPropertyName("nextCursor")] string? NextCursor,
    [property: JsonPropertyName("truncated")] bool Truncated);

/// <summary>
/// Cuts a provider page of games to the result size limit and writes the cursor for the next page. A cursor holds the
/// zero-based provider page and the number of rows of that page already shown, so a page that was cut continues with
/// the rows that were left out.
/// </summary>
internal static class PlaygroupGamesPaging
{
    /// <summary>
    /// Reads the provider page and the rows to skip from a cursor, or the first page without one.
    /// </summary>
    internal static OperationResult<PageCursorPosition> Position(string? cursor, PageCursorBinding binding)
    {
        return PageCursor.Decode(cursor, binding, twoLists: true);
    }

    /// <summary>
    /// Builds the page a caller sees from one provider page: skips the rows already shown, keeps the leading rows that
    /// fit the result size limit, and returns a cursor after every page that has rows left or that the provider filled.
    /// </summary>
    /// <param name="evidence">The provider page as acquired.</param>
    /// <param name="position">The provider page and rows already shown that the cursor named.</param>
    /// <param name="pageSize">The page size the provider was asked for.</param>
    /// <param name="binding">The state the cursor is bound to.</param>
    internal static PlaygroupGamesPage Shape(
        PlaygroupEvidence evidence,
        PageCursorPosition position,
        int pageSize,
        PageCursorBinding binding)
    {
        if (evidence.Data.ValueKind != JsonValueKind.Array)
        {
            return new PlaygroupGamesPage(evidence, null, false);
        }

        List<JsonElement> rows = [.. evidence.Data.EnumerateArray()];
        int skip = Math.Min(position.Second ?? 0, rows.Count);
        PlaygroupEvidence skeleton = evidence with { Data = JsonSerializer.SerializeToElement(Array.Empty<int>()) };
        int allowance = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes - ResultSize.Measure(skeleton);
        IEnumerable<int> sizes = rows.Skip(skip).Select(row => Encoding.UTF8.GetByteCount(row.GetRawText()) + 1);
        int kept = Math.Max(ResultSizeLimit.CountWithin(sizes, allowance, out _), rows.Count > skip ? 1 : 0);
        bool cut = skip + kept < rows.Count;
        string? next = cut
            ? PageCursor.Encode(new PageCursorPosition(position.First, skip + kept), binding)
            : rows.Count >= pageSize ? PageCursor.Encode(new PageCursorPosition(position.First + 1, 0), binding) : null;
        PlaygroupEvidence shown = skip == 0 && !cut
            ? evidence
            : evidence with { Data = JsonSerializer.SerializeToElement(rows.Skip(skip).Take(kept)) };
        return new PlaygroupGamesPage(shown, next, cut);
    }
}
