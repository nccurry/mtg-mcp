namespace MtgMcp.Core.Results;

/// <summary>
/// Names the size limit for results of tools whose output grows with the caller's data, so every such
/// tool pages or truncates at the same point.
/// </summary>
public static class ResultSizeLimit
{
    /// <summary>
    /// Gets the most UTF-8 bytes a limited tool result may carry. The value is about 10,000 tokens, which keeps
    /// one result below the output cap clients apply to a tool call.
    /// </summary>
    public const int MaximumBytes = 40_000;

    /// <summary>
    /// Gets the bytes kept free for the parts of a result that sit outside its list of items, such as the
    /// snapshot reference, warnings, and limit notes.
    /// </summary>
    public const int EnvelopeReserveBytes = 3_000;

    /// <summary>
    /// Gets the hint returned when a card result was cut short to stay within the limit.
    /// </summary>
    public const string CardNarrowHint =
        "The page was cut to stay under the result size limit. Use the nextCursor to read the rest, or ask for " +
        "responseFormat concise with fields, a smaller pageSize, or a narrower query.";
}
