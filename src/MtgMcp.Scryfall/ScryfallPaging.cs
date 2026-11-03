using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Reads and writes the offset cursors of Scryfall result pages. A cursor names one scope, which is an identity, and
/// the checksum of the data inside it, so a cursor for another scope is invalid and one for changed data is stale.
/// </summary>
internal static class ScryfallPaging
{
    /// <summary>
    /// Reads the offset a cursor names against the scope and checksum of the result now. A missing cursor starts at
    /// the first row, and an offset past the <paramref name="total"/> rows is invalid input at <c>cursor</c>.
    /// </summary>
    internal static OperationResult<int> ReadOffset(string? cursor, string scope, string checksum, int total)
    {
        OperationResult<PageCursorPosition> decoded = PageCursor.Decode(
            cursor,
            PageCursorBinding.ForScope(scope, checksum));
        if (decoded is not OperationSuccess<PageCursorPosition> position)
        {
            return OperationFailure.Forward<PageCursorPosition, int>(decoded);
        }

        return position.Data.Offset <= total
            ? new OperationSuccess<int>(position.Data.Offset)
            : PageCursor.Invalid();
    }

    /// <summary>
    /// Reports whether a cursor was issued for <paramref name="scope"/>, whatever data it held, so a reader on another
    /// source can tell a cursor whose source changed from one that belongs to another request.
    /// </summary>
    internal static bool IsCursorOfScope(string? cursor, string scope)
    {
        return cursor is not null &&
            PageCursor.Decode(cursor, PageCursorBinding.ForScope(scope, "unread")) is OperationConflict;
    }

    /// <summary>
    /// Writes the cursor for the page that starts at <paramref name="next"/>, or null when no rows are left.
    /// </summary>
    internal static string? NextCursor(string scope, string checksum, int next, int total)
    {
        return next < total
            ? PageCursor.Encode(PageCursorPosition.AtOffset(next), PageCursorBinding.ForScope(scope, checksum))
            : null;
    }
}

/// <summary>
/// Tells how many leading rows of a page fit under the result size limit. The page is cut inside the adapter because
/// only the adapter holds the cursor binding, so the cursor can continue at the first row that was cut.
/// </summary>
public delegate int ScryfallRowFit<T>(IReadOnlyList<T> rows);
