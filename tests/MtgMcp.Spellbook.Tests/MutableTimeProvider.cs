namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Supplies a deterministic UTC clock for cache, pacing, and source evidence tests.
/// </summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    /// <summary>
    /// Stores the test-controlled current UTC instant.
    /// </summary>
    private DateTimeOffset utcNow;

    /// <summary>
    /// Creates the clock at one exact UTC instant.
    /// </summary>
    internal MutableTimeProvider(DateTimeOffset utcNow)
    {
        this.utcNow = utcNow.ToUniversalTime();
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }

    /// <summary>
    /// Advances the test-controlled instant without waiting on wall-clock time.
    /// </summary>
    internal void Advance(TimeSpan duration)
    {
        utcNow += duration;
    }
}
