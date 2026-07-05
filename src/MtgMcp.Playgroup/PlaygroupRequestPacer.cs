using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace MtgMcp.Playgroup;

/// <summary>
/// Serializes provider request starts across adapter instances sharing one credential lane.
/// </summary>
internal sealed class PlaygroupRequestPacer
{
    /// <summary>
    /// Shares one pacing state for each non-secret credential fingerprint.
    /// </summary>
    private static readonly ConcurrentDictionary<string, PacingState> States = new(StringComparer.Ordinal);

    /// <summary>
    /// Identifies the shared lane without retaining the credential.
    /// </summary>
    private readonly string laneKey;

    /// <summary>
    /// Stores validated pacing bounds.
    /// </summary>
    private readonly PlaygroupOptions options;

    /// <summary>
    /// Supplies UTC time for production or deterministic tests.
    /// </summary>
    private readonly Func<DateTimeOffset> utcNow;

    /// <summary>
    /// Delays without blocking request threads.
    /// </summary>
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    /// <summary>
    /// Creates a production pacer.
    /// </summary>
    internal PlaygroupRequestPacer(string laneKey, PlaygroupOptions options)
        : this(laneKey, options, static () => DateTimeOffset.UtcNow,
            static (duration, token) => Task.Delay(duration, token))
    {
    }

    /// <summary>
    /// Creates a deterministic pacer with injected time and delay behavior.
    /// </summary>
    internal PlaygroupRequestPacer(
        string laneKey,
        PlaygroupOptions options,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        this.laneKey = PlaygroupContract.Required(laneKey, nameof(laneKey));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    /// <summary>
    /// Waits for shared spacing and cooldown boundaries before recording a request start.
    /// </summary>
    internal async Task WaitForPermitAsync(CancellationToken cancellationToken)
    {
        PacingState state = States.GetOrAdd(laneKey, static _ => new PacingState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                DateTimeOffset now = utcNow().ToUniversalTime();
                DateTimeOffset earliest = state.LastStartUtc is { } lastStart
                    ? lastStart + options.MinimumRequestInterval
                    : now;
                if (state.CooldownUntilUtc > earliest)
                {
                    earliest = state.CooldownUntilUtc;
                }

                if (earliest <= now)
                {
                    state.LastStartUtc = now;
                    return;
                }

                await delay(earliest - now, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Applies a bounded shared Retry-After cooldown and reports whether one GET retry is safe.
    /// </summary>
    internal async Task<bool> ObserveRetryAfterAsync(
        RetryConditionHeaderValue? retryAfter,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = utcNow().ToUniversalTime();
        DateTimeOffset? requested = retryAfter?.Date?.ToUniversalTime();
        if (requested is null && retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            requested = now + delta;
        }

        bool bounded = requested > now && requested <= now + options.MaximumRetryAfter;
        DateTimeOffset cooldown = bounded && requested is { } retryAt
            ? retryAt
            : now + options.MaximumRetryAfter;
        PacingState state = States.GetOrAdd(laneKey, static _ => new PacingState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cooldown > state.CooldownUntilUtc)
            {
                state.CooldownUntilUtc = cooldown;
            }
        }
        finally
        {
            state.Gate.Release();
        }

        return bounded;
    }

    /// <summary>
    /// Stores one request timeline without credentials or provider payloads.
    /// </summary>
    private sealed class PacingState
    {
        /// <summary>
        /// Serializes start and cooldown calculations.
        /// </summary>
        internal SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        /// Stores the most recent request start.
        /// </summary>
        internal DateTimeOffset? LastStartUtc { get; set; }

        /// <summary>
        /// Stores the latest provider-requested cooldown boundary.
        /// </summary>
        internal DateTimeOffset CooldownUntilUtc { get; set; } = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// Classifies sanitized Playgroup adapter failures.
/// </summary>
internal enum PlaygroupFailureKind
{
    /// <summary>Caller input was invalid before provider I/O.</summary>
    InvalidInput,
    /// <summary>The requested provider entity was not found.</summary>
    NotFound,
    /// <summary>The operation is outside the pinned provider contract.</summary>
    Unsupported,
    /// <summary>The provider or required authentication is currently unavailable.</summary>
    Unavailable,
}

/// <summary>
/// Carries only sanitized provider failure details across the adapter boundary.
/// </summary>
internal sealed class PlaygroupProviderException : Exception
{
    /// <summary>
    /// Creates one sanitized provider failure.
    /// </summary>
    internal PlaygroupProviderException(
        PlaygroupFailureKind kind,
        string reasonCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ReasonCode = reasonCode;
    }

    /// <summary>Gets the structured failure category.</summary>
    internal PlaygroupFailureKind Kind { get; }

    /// <summary>Gets the stable machine-readable reason.</summary>
    internal string ReasonCode { get; }
}
