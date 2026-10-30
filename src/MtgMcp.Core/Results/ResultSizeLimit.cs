namespace MtgMcp.Core.Results;

/// <summary>
/// Names the size limit for results of tools whose output grows with the caller's data, so every such
/// tool pages or truncates at the same point.
/// </summary>
public static class ResultSizeLimit
{
    /// <summary>
    /// Gets the most UTF-8 bytes the structured part of a limited tool result may carry. The value is about
    /// 10,000 tokens. The text block of a tool result repeats the structured JSON, and a client passes only
    /// one of the two to the model.
    /// </summary>
    public const int MaximumBytes = 40_000;

    /// <summary>
    /// Gets the bytes kept free for the parts of a result that sit outside its list of items, such as the
    /// snapshot reference, warnings, and limit notes.
    /// </summary>
    public const int EnvelopeReserveBytes = 3_000;

    /// <summary>
    /// Counts how many leading items fit a byte budget, and how many bytes they take. Items are read one at a time,
    /// so the items after the first one that does not fit are never measured. This is the one fitting loop; callers
    /// that must show a row anyway use <see cref="CountFitting"/>.
    /// </summary>
    /// <param name="itemSizes">The serialized size in bytes of each item, including its separator, in order.</param>
    /// <param name="budgetBytes">The bytes the items may take together.</param>
    /// <param name="usedBytes">The bytes the counted items take.</param>
    public static int CountWithin(IEnumerable<int> itemSizes, int budgetBytes, out int usedBytes)
    {
        ArgumentNullException.ThrowIfNull(itemSizes);
        usedBytes = 0;
        int kept = 0;
        foreach (int size in itemSizes)
        {
            if (usedBytes + size > budgetBytes)
            {
                break;
            }

            usedBytes += size;
            kept++;
        }

        return kept;
    }

    /// <summary>
    /// Counts how many leading items fit the size limit. At least one item is kept when the list is not empty, so
    /// a single large item never leaves the caller without progress.
    /// </summary>
    /// <param name="itemSizes">The serialized size in bytes of each item, including its separator, in order.</param>
    /// <param name="fixedBytes">The bytes the rest of the result needs beyond the envelope reserve.</param>
    public static int CountFitting(IReadOnlyList<int> itemSizes, int fixedBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(itemSizes);
        int kept = CountWithin(itemSizes, MaximumBytes - EnvelopeReserveBytes - fixedBytes, out _);
        return Math.Max(kept, Math.Min(1, itemSizes.Count));
    }
}
