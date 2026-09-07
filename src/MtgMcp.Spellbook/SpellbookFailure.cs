namespace MtgMcp.Spellbook;

/// <summary>
/// Classifies the small set of safe Commander Spellbook failures the adapter can return.
/// </summary>
internal enum SpellbookFailureKind
{
    /// <summary>
    /// The caller or source rejected the request as invalid.
    /// </summary>
    InvalidInput,

    /// <summary>
    /// The requested source entity was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The source response is outside the pinned contract.
    /// </summary>
    Unsupported,

    /// <summary>
    /// The source cannot currently serve the request.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The source or local pacer is rate-limiting requests.
    /// </summary>
    RateLimited,
}

/// <summary>
/// Carries a sanitized expected provider failure across the adapter boundary.
/// </summary>
internal sealed class SpellbookProviderException : Exception
{
    /// <summary>
    /// Creates one safe adapter failure with an optional provider cooldown.
    /// </summary>
    internal SpellbookProviderException(
        SpellbookFailureKind kind,
        string reasonCode,
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ReasonCode = reasonCode;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets the structured category used to create an operation result.
    /// </summary>
    internal SpellbookFailureKind Kind { get; }

    /// <summary>
    /// Gets the stable machine-readable failure code.
    /// </summary>
    internal string ReasonCode { get; }

    /// <summary>
    /// Gets an optional source-supplied cooldown before local bounding.
    /// </summary>
    internal TimeSpan? RetryAfter { get; }
}
