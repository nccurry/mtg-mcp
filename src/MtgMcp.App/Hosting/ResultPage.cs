using MtgMcp.Core.Results;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Names which rows of one list a page shows: <see cref="Count"/> rows from <see cref="Offset"/>, out of
/// <see cref="Total"/> rows in the list.
/// </summary>
internal readonly record struct ResultPageSlice(int Offset, int Count, int Total)
{
    /// <summary>
    /// Gets the offset of the first row the next page shows.
    /// </summary>
    internal int Next => Offset + Count;

    /// <summary>
    /// Gets the offset of the next page while the result goes on, even when this list has ended, in which case the
    /// offset is the list's row count. A result with two lists needs both next offsets on every page that is not the
    /// last, or a caller that leaves one out starts that list again from row 0.
    /// </summary>
    /// <param name="resultContinues">True when any list of the result has rows left.</param>
    internal int? NextOffsetIf(bool resultContinues)
    {
        return resultContinues ? Next : null;
    }

    /// <summary>
    /// Gets true when rows of the list come after this page.
    /// </summary>
    internal bool More => Next < Total;

    /// <summary>
    /// Gets the offset of the next page, or null when this page reaches the end of the list.
    /// </summary>
    internal int? NextOffset => More ? Next : null;

    /// <summary>
    /// Copies the rows this page shows out of the whole list.
    /// </summary>
    internal List<T> Rows<T>(IReadOnlyList<T> rows)
    {
        List<T> shown = new(Count);
        for (int index = Offset; index < Next; index++)
        {
            shown.Add(rows[index]);
        }

        return shown;
    }
}

/// <summary>
/// Pages the lists of a result by the shared size limit with one offset rule and one progress rule, so every paged
/// tool treats an offset, a row that is bigger than the page, and the end of a list the same way.
/// </summary>
internal static class ResultPage
{
    /// <summary>
    /// Refuses an offset outside its list. An offset from 0 up to the number of rows is valid: an offset equal to the
    /// number of rows is how a caller keeps a list that already ended while it pages another list of the same result,
    /// and it shows no rows of that list.
    /// </summary>
    /// <param name="parameter">The name of the offset argument.</param>
    /// <param name="offset">The offset the caller passed.</param>
    /// <param name="count">The number of rows in the list.</param>
    /// <param name="list">What the rows are, in the plural, such as "entries".</param>
    internal static OperationInvalidInput? CheckOffset(string parameter, int offset, int count, string list)
    {
        return offset < 0 || offset > count
            ? new OperationInvalidInput(
                "invalid-offset",
                $"{parameter} must be from 0 to {count} for this result, which has {count} {list}.",
                parameter)
            : null;
    }

    /// <summary>
    /// Refuses a request for a later page of a result that is no longer the one the earlier pages came from.
    /// </summary>
    /// <param name="expected">The fingerprint the caller read from an earlier page, or null to skip the check.</param>
    /// <param name="current">The fingerprint of the result as it is now.</param>
    /// <param name="laterPage">True when the request starts after the first row of any list.</param>
    internal static OperationConflict? CheckSameResult(string? expected, string? current, bool laterPage)
    {
        return laterPage &&
            expected is not null &&
            current is not null &&
            !string.Equals(expected, current, StringComparison.Ordinal)
                ? new OperationConflict(
                    "preview-changed",
                    "The result changed since the earlier page was read, so this page would not line up with it. " +
                    "Call again without an offset to start from the first page.")
                : null;
    }

    /// <summary>
    /// Counts the rows of one list, from <paramref name="offset"/>, that fit the bytes the page has left. A page
    /// shows at least one row when the list has one left, so a caller
    /// that follows the next offset never loops on the same rows.
    /// </summary>
    internal static ResultPageSlice Take<T>(IReadOnlyList<T> rows, int offset, int headerBytes)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int kept = Fit(rows, offset, Budget(headerBytes), out _);
        return new ResultPageSlice(offset, Math.Max(kept, rows.Count > offset ? 1 : 0), rows.Count);
    }

    /// <summary>
    /// Pages two lists of one result: the first list fills the page, and the second list takes what is left. A page
    /// that fits no row still shows one, from the first list if it has rows left, or else from the second.
    /// </summary>
    internal static (ResultPageSlice First, ResultPageSlice Second) TakeTwo<TFirst, TSecond>(
        IReadOnlyList<TFirst> first,
        int firstOffset,
        IReadOnlyList<TSecond> second,
        int secondOffset,
        int headerBytes)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        int budget = Budget(headerBytes);
        int firstKept = Fit(first, firstOffset, budget, out int used);
        int secondKept = Fit(second, secondOffset, budget - used, out _);
        if (firstKept == 0 && secondKept == 0)
        {
            if (first.Count > firstOffset)
            {
                firstKept = 1;
            }
            else if (second.Count > secondOffset)
            {
                secondKept = 1;
            }
        }

        return (
            new ResultPageSlice(firstOffset, firstKept, first.Count),
            new ResultPageSlice(secondOffset, secondKept, second.Count));
    }

    /// <summary>
    /// Gives the bytes the rows of a page may take once the rest of the result and the envelope are paid for.
    /// </summary>
    private static int Budget(int headerBytes)
    {
        return ResultSizeLimit.MaximumBytes - ResultSizeLimit.EnvelopeReserveBytes - headerBytes;
    }

    /// <summary>
    /// Counts how many rows from <paramref name="offset"/> fit the byte budget, and how many bytes they take. Each row
    /// counts one byte more than its own JSON for the separator, and rows after the first that does not fit are not
    /// measured.
    /// </summary>
    internal static int Fit<T>(IReadOnlyList<T> rows, int offset, int budget, out int used)
    {
        used = 0;
        int kept = 0;
        for (int index = offset; index < rows.Count; index++)
        {
            int size = ResultSize.Measure(rows[index]) + 1;
            if (used + size > budget)
            {
                break;
            }

            used += size;
            kept++;
        }

        return kept;
    }
}
