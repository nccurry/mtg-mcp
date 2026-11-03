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
    /// Gets true when rows of the list come after this page.
    /// </summary>
    internal bool More => Next < Total;

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
    /// Counts the rows of one list, from <paramref name="offset"/>, that fit the bytes the page has left. A page
    /// shows at least one row when the list has one left, so a caller that follows the next offset never loops on the
    /// same rows. A row is measured as it is, unless <paramref name="rowBytes"/> gives its serialized size without its
    /// separator, for a row that is shown with other data.
    /// </summary>
    internal static ResultPageSlice Take<T>(
        IReadOnlyList<T> rows,
        int offset,
        int headerBytes,
        Func<T, int>? rowBytes = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int kept = Fit(rows, offset, Budget(headerBytes), rowBytes, out _);
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
        int firstKept = Fit(first, firstOffset, budget, null, out int used);
        int secondKept = Fit(second, secondOffset, budget - used, null, out _);
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
    /// Keeps the leading items of a list that fit a byte allowance, and takes what they use from the allowance.
    /// </summary>
    internal static List<T> CutList<T>(IReadOnlyList<T> items, ref int allowance)
    {
        int kept = Fit(items, 0, allowance, null, out int used);
        allowance -= used;
        return [.. items.Take(kept)];
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
    /// counts one byte more than its own JSON for the separator.
    /// </summary>
    private static int Fit<T>(IReadOnlyList<T> rows, int offset, int budget, Func<T, int>? rowBytes, out int used)
    {
        IEnumerable<int> sizes = rows
            .Skip(offset)
            .Select(row => (rowBytes is null ? ResultSize.Measure(row) : rowBytes(row)) + 1);
        return ResultSizeLimit.CountWithin(sizes, budget, out used);
    }
}
