using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Paging;
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
/// the rows that were left out. A cursor inside a provider page is also bound to the checksum of that page, so a game
/// added or removed since the cursor was issued makes it stale instead of skipping or repeating a row.
/// </summary>
internal static class PlaygroupGamesPaging
{
    /// <summary>
    /// Reads the provider page and the rows to skip from a cursor, or the first page without one, before the provider
    /// is asked. A cursor that starts a provider page is checked against the request now; a cursor inside a page is
    /// checked by <see cref="Continue"/> once that page is read.
    /// </summary>
    internal static OperationResult<PageCursorPosition> Position(
        string? cursor,
        PageCursorBinding request,
        int pageSize)
    {
        OperationResult<PageCursorPosition> peeked = PageCursor.Peek(cursor, PageCursorShape.ProviderPage);
        if (peeked is not OperationSuccess<PageCursorPosition> { Data: PageCursorPosition position })
        {
            return peeked;
        }

        if (position.RowsShown > pageSize || position.Page == int.MaxValue)
        {
            return PageCursor.Invalid();
        }

        return position.RowsShown == 0
            ? PageCursor.Decode(cursor, request, PageCursorShape.ProviderPage)
            : peeked;
    }

    /// <summary>
    /// Checks a cursor inside a provider page against the page as read now, and refuses it as stale when the page
    /// changed since the cursor was issued.
    /// </summary>
    internal static OperationResult<PageCursorPosition> Continue(
        string? cursor,
        PageCursorPosition position,
        PageCursorBinding request,
        PlaygroupEvidence evidence)
    {
        return position.RowsShown == 0
            ? new OperationSuccess<PageCursorPosition>(position)
            : PageCursor.Decode(cursor, PageBinding(request, evidence), PageCursorShape.ProviderPage);
    }

    /// <summary>
    /// Builds the page a caller sees from one provider page: skips the rows already shown, keeps the leading rows that
    /// fit the result size limit, and returns a cursor after every page that has rows left or that the provider filled.
    /// </summary>
    /// <param name="evidence">The provider page as acquired.</param>
    /// <param name="position">The provider page and rows already shown that the cursor named.</param>
    /// <param name="pageSize">The page size the provider was asked for.</param>
    /// <param name="request">The request arguments the cursor is bound to.</param>
    internal static PlaygroupGamesPage Shape(
        PlaygroupEvidence evidence,
        PageCursorPosition position,
        int pageSize,
        PageCursorBinding request)
    {
        if (evidence.Data.ValueKind != JsonValueKind.Array)
        {
            return new PlaygroupGamesPage(evidence, null, false);
        }

        List<JsonElement> rows = [.. evidence.Data.EnumerateArray()];
        int skip = Math.Min(position.RowsShown, rows.Count);
        PlaygroupEvidence skeleton = evidence with { Data = JsonSerializer.SerializeToElement(Array.Empty<int>()) };
        int allowance = ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes -
            ResultSize.Measure(skeleton);
        IEnumerable<int> sizes = rows.Skip(skip).Select(row => Encoding.UTF8.GetByteCount(row.GetRawText()) + 1);
        int fitting = ResultSizeLimit.CountWithin(sizes, allowance, out _);
        int kept = Math.Max(fitting, rows.Count > skip ? 1 : 0);
        bool cut = skip + kept < rows.Count;
        string? next = null;
        if (cut)
        {
            next = PageCursor.Encode(
                PageCursorPosition.AtProviderPage(position.Page, skip + kept),
                PageBinding(request, evidence));
        }
        else if (rows.Count >= pageSize)
        {
            next = PageCursor.Encode(PageCursorPosition.AtProviderPage(position.Page + 1, 0), request);
        }

        PlaygroupEvidence shown = skip == 0 && !cut
            ? evidence
            : evidence with { Data = JsonSerializer.SerializeToElement(rows.Skip(skip).Take(kept)) };
        return new PlaygroupGamesPage(shown, next, cut);
    }

    /// <summary>
    /// Binds a cursor inside a provider page to the request and to the checksum of that page.
    /// </summary>
    private static PageCursorBinding PageBinding(PageCursorBinding request, PlaygroupEvidence evidence)
    {
        return request.And(PageCursorBinding.ForSource(evidence.SourceChecksum));
    }
}
