namespace MtgMcp.Core;

/// <summary>
/// Coordinates request pacing for one adapter registration without process-global state.
/// </summary>
public class MtgMcpRequestPacer : IDisposable
{
    /// <summary>
    /// Serializes updates to request timestamps.
    /// </summary>
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Stores the most recent paced request timestamp.
    /// </summary>
    private DateTimeOffset lastRequestAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Stores recent request timestamps for sliding-window pacing.
    /// </summary>
    private readonly Queue<DateTimeOffset> requestTimestamps = new();

    /// <summary>
    /// Waits until the configured minimum delay has elapsed since the previous request.
    /// </summary>
    public async Task WaitForMinimumDelayAsync(
        TimeSpan minimumDelay,
        CancellationToken cancellationToken)
    {
        if (minimumDelay <= TimeSpan.Zero)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - lastRequestAt;
            if (elapsed < minimumDelay)
            {
                await Task.Delay(minimumDelay - elapsed, cancellationToken)
                    .ConfigureAwait(false);
            }

            lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Waits until the configured sliding-window request budget has room.
    /// </summary>
    public async Task WaitForSlidingWindowAsync(
        int maxRequests,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        if (maxRequests <= 0)
        {
            return;
        }

        TimeSpan safeWindow = window <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : window;
        while (true)
        {
            TimeSpan delay;
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                while (requestTimestamps.TryPeek(out DateTimeOffset oldest)
                    && now - oldest >= safeWindow)
                {
                    requestTimestamps.Dequeue();
                }

                if (requestTimestamps.Count < maxRequests)
                {
                    requestTimestamps.Enqueue(now);
                    return;
                }

                delay = safeWindow - (now - requestTimestamps.Peek()) + TimeSpan.FromMilliseconds(50);
            }
            finally
            {
                gate.Release();
            }

            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases pacing synchronization resources.
    /// </summary>
    public void Dispose()
    {
        gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
