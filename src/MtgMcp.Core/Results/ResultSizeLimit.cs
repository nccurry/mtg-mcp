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
    /// Counts how many leading items fit the size limit. At least one item is kept when the list is not empty, so
    /// a single large item never leaves the caller without progress.
    /// </summary>
    /// <param name="itemSizes">The serialized size in bytes of each item, including its separator, in order.</param>
    /// <param name="fixedBytes">The bytes the rest of the result needs beyond the envelope reserve.</param>
    public static int CountFitting(IReadOnlyList<int> itemSizes, int fixedBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(itemSizes);
        int budget = MaximumBytes - EnvelopeReserveBytes - fixedBytes;
        int total = 0;
        int kept = 0;
        while (kept < itemSizes.Count && (kept == 0 || total + itemSizes[kept] <= budget))
        {
            total += itemSizes[kept];
            kept++;
        }

        return kept;
    }
}
