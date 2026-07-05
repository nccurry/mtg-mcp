using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace MtgMcp.Archidekt;

/// <summary>
/// Tracks the hard provider-request ceiling for one complete MCP tool operation.
/// </summary>
internal sealed class ArchidektOperationBudget
{
    /// <summary>
    /// Stores the maximum request starts permitted for this operation.
    /// </summary>
    private readonly int maximumRequests;

    /// <summary>
    /// Stores the number of request starts already reserved.
    /// </summary>
    private int requestCount;

    /// <summary>
    /// Creates an empty operation budget with one positive request ceiling.
    /// </summary>
    internal ArchidektOperationBudget(int maximumRequests)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequests);
        this.maximumRequests = maximumRequests;
    }

    /// <summary>
    /// Gets the number of request starts already consumed.
    /// </summary>
    internal int RequestCount => Volatile.Read(ref requestCount);

    /// <summary>
    /// Reserves one request start or fails before another provider call can begin.
    /// </summary>
    internal void Reserve()
    {
        int value = Interlocked.Increment(ref requestCount);
        if (value > maximumRequests)
        {
            Interlocked.Decrement(ref requestCount);
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "request-limit-exceeded",
                "The operation would exceed its provider request limit.");
        }
    }
}

/// <summary>
/// Carries one opaque provider-request budget across every adapter call made by a single MCP tool invocation.
/// </summary>
public sealed class ArchidektOperationScope
{
    /// <summary>
    /// Creates a scope owned by one configured service.
    /// </summary>
    internal ArchidektOperationScope(int maximumRequests)
    {
        Budget = new ArchidektOperationBudget(maximumRequests);
    }

    /// <summary>
    /// Gets the adapter-owned request counter without exposing it outside the assembly.
    /// </summary>
    internal ArchidektOperationBudget Budget { get; }
}

/// <summary>
/// Serializes provider starts per configured account and enforces spacing, rolling-window, and cooldown rules.
/// </summary>
internal sealed class ArchidektRequestPacer
{
    /// <summary>
    /// Shares one pacing timeline among every adapter instance in this process for the same account key.
    /// </summary>
    private static readonly ConcurrentDictionary<string, PacingState> States = new(StringComparer.Ordinal);

    /// <summary>
    /// Supplies current UTC time for production and deterministic tests.
    /// </summary>
    private readonly Func<DateTimeOffset> utcNow;

    /// <summary>
    /// Delays without blocking a request thread.
    /// </summary>
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    /// <summary>
    /// Stores the non-secret hash used to select one shared account lane.
    /// </summary>
    private readonly string accountKey;

    /// <summary>
    /// Stores conservative pacing bounds.
    /// </summary>
    private readonly ArchidektOptions options;

    /// <summary>
    /// Creates a production pacer over one non-secret account key.
    /// </summary>
    internal ArchidektRequestPacer(string accountKey, ArchidektOptions options)
        : this(
            accountKey,
            options,
            static () => DateTimeOffset.UtcNow,
            static (duration, token) => Task.Delay(duration, token))
    {
    }

    /// <summary>
    /// Creates a deterministic pacer with injected time and delay behavior.
    /// </summary>
    internal ArchidektRequestPacer(
        string accountKey,
        ArchidektOptions options,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        this.accountKey = ArchidektContract.Required(accountKey, nameof(accountKey));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    /// <summary>
    /// Waits until one provider start is safe, then atomically records it on the shared timeline.
    /// </summary>
    internal async Task WaitForPermitAsync(
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Reserve();
        PacingState state = States.GetOrAdd(accountKey, static _ => new PacingState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                DateTimeOffset now = utcNow().ToUniversalTime();
                RemoveExpiredStarts(state, now);
                DateTimeOffset earliest = EarliestStart(state, now);
                if (earliest <= now)
                {
                    state.Starts.Enqueue(now);
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
    /// Opens a shared cooldown after a provider throttle response without retrying that response.
    /// </summary>
    internal async Task ObserveRateLimitAsync(
        RetryConditionHeaderValue? retryAfter,
        CancellationToken cancellationToken)
    {
        PacingState state = States.GetOrAdd(accountKey, static _ => new PacingState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = utcNow().ToUniversalTime();
            DateTimeOffset cooldown = ResolveCooldown(now, retryAfter);
            if (cooldown > state.CooldownUntilUtc)
            {
                state.CooldownUntilUtc = cooldown;
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    /// <summary>
    /// Removes retained starts that no longer affect the rolling request window.
    /// </summary>
    private void RemoveExpiredStarts(PacingState state, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - options.RequestWindow;
        while (state.Starts.TryPeek(out DateTimeOffset value) && value <= cutoff)
        {
            state.Starts.Dequeue();
        }
    }

    /// <summary>
    /// Computes the latest applicable spacing, rolling-window, or provider cooldown boundary.
    /// </summary>
    private DateTimeOffset EarliestStart(PacingState state, DateTimeOffset now)
    {
        DateTimeOffset earliest = now;
        if (state.Starts.TryPeek(out DateTimeOffset oldest) &&
            state.Starts.Count >= options.MaximumRequestsPerWindow)
        {
            earliest = Max(earliest, oldest + options.RequestWindow);
        }

        if (state.Starts.Count > 0)
        {
            DateTimeOffset latest = state.Starts.Last();
            earliest = Max(earliest, latest + options.MinimumRequestInterval);
        }

        return Max(earliest, state.CooldownUntilUtc);
    }

    /// <summary>
    /// Resolves a valid delta/date Retry-After value or applies a conservative one-minute fallback.
    /// </summary>
    private DateTimeOffset ResolveCooldown(
        DateTimeOffset now,
        RetryConditionHeaderValue? retryAfter)
    {
        DateTimeOffset? requested = retryAfter?.Date?.ToUniversalTime();
        if (requested is null && retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            requested = now + delta;
        }

        DateTimeOffset cooldown = requested > now
            ? requested.Value
            : now + options.RequestWindow;
        DateTimeOffset maximum = now + TimeSpan.FromHours(24);
        return cooldown > maximum ? maximum : cooldown;
    }

    /// <summary>
    /// Selects the later of two UTC timestamps.
    /// </summary>
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left >= right ? left : right;
    }

    /// <summary>
    /// Stores one shared serialized request timeline without account identity or provider payloads.
    /// </summary>
    private sealed class PacingState
    {
        /// <summary>
        /// Serializes calculations and provider start reservations.
        /// </summary>
        internal SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        /// Retains request starts that can still affect the rolling window.
        /// </summary>
        internal Queue<DateTimeOffset> Starts { get; } = new();

        /// <summary>
        /// Gets or sets the provider-directed earliest future request time.
        /// </summary>
        internal DateTimeOffset CooldownUntilUtc { get; set; } = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// Distinguishes sanitized provider failures before they are mapped to common operation outcomes.
/// </summary>
internal enum ArchidektFailureKind
{
    /// <summary>
    /// The caller supplied invalid or unsafe input.
    /// </summary>
    InvalidInput,

    /// <summary>
    /// The requested provider entity was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// Current provider or local state conflicts with immutable guards.
    /// </summary>
    Conflict,

    /// <summary>
    /// The observed provider contract no longer supports the operation safely.
    /// </summary>
    Unsupported,

    /// <summary>
    /// A supported provider dependency cannot currently answer safely.
    /// </summary>
    Unavailable,
}

/// <summary>
/// Carries a sanitized failure classification without status bodies, secrets, or local paths.
/// </summary>
internal sealed class ArchidektProviderException : Exception
{
    /// <summary>
    /// Creates one sanitized provider failure.
    /// </summary>
    internal ArchidektProviderException(
        ArchidektFailureKind kind,
        string reasonCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ReasonCode = reasonCode;
    }

    /// <summary>
    /// Gets the common outcome class selected at the adapter boundary.
    /// </summary>
    internal ArchidektFailureKind Kind { get; }

    /// <summary>
    /// Gets the stable lowercase kebab-case reason code.
    /// </summary>
    internal string ReasonCode { get; }
}
