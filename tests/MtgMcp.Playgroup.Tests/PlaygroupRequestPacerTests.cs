using System.Net.Http.Headers;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Proves shared 250 ms request spacing, bounded cooldowns, and cancellation behavior.
/// </summary>
public sealed class PlaygroupRequestPacerTests
{
    /// <summary>Verifies request starts are serialized at the conservative minimum interval.</summary>
    [Fact]
    public async Task WaitForPermitAsync_SpacesSharedRequestStarts()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        List<TimeSpan> delays = [];
        string lane = Guid.NewGuid().ToString("N");
        PlaygroupOptions options = PlaygroupOptions.CreateDefault(null);
        PlaygroupRequestPacer first = CreatePacer(lane, options, () => now, Delay);
        PlaygroupRequestPacer second = CreatePacer(lane, options, () => now, Delay);

        await first.WaitForPermitAsync(TestContext.Current.CancellationToken);
        await second.WaitForPermitAsync(TestContext.Current.CancellationToken);
        await first.WaitForPermitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250)],
            delays);

        Task Delay(TimeSpan duration, CancellationToken _)
        {
            delays.Add(duration);
            now += duration;
            return Task.CompletedTask;
        }
    }

    /// <summary>Verifies only present future Retry-After values inside the bound permit a retry.</summary>
    [Fact]
    public async Task ObserveRetryAfterAsync_ReportsBoundedValuesAndAppliesCooldown()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        List<TimeSpan> delays = [];
        PlaygroupOptions options = PlaygroupOptions.CreateDefault(null) with
        {
            MaximumRetryAfter = TimeSpan.FromSeconds(30),
        };
        PlaygroupRequestPacer pacer = CreatePacer(
            Guid.NewGuid().ToString("N"),
            options,
            () => now,
            (duration, _) =>
            {
                delays.Add(duration);
                now += duration;
                return Task.CompletedTask;
            });

        Assert.True(await pacer.ObserveRetryAfterAsync(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken));
        await pacer.WaitForPermitAsync(TestContext.Current.CancellationToken);
        Assert.Equal([TimeSpan.FromSeconds(5)], delays);

        Assert.False(await pacer.ObserveRetryAfterAsync(null, TestContext.Current.CancellationToken));
        Assert.False(await pacer.ObserveRetryAfterAsync(
            new RetryConditionHeaderValue(TimeSpan.FromMinutes(1)),
            TestContext.Current.CancellationToken));
    }

    /// <summary>Verifies cancellation from a pacing wait propagates to the caller.</summary>
    [Fact]
    public async Task WaitForPermitAsync_PropagatesCancellation()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using CancellationTokenSource cancellation = new();
        PlaygroupRequestPacer pacer = CreatePacer(
            Guid.NewGuid().ToString("N"),
            PlaygroupOptions.CreateDefault(null),
            () => now,
            (_, _) => Task.FromCanceled(cancellation.Token));
        await pacer.WaitForPermitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => pacer.WaitForPermitAsync(cancellation.Token));
    }

    /// <summary>Creates one deterministic pacer.</summary>
    private static PlaygroupRequestPacer CreatePacer(
        string lane,
        PlaygroupOptions options,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        return new PlaygroupRequestPacer(lane, options, utcNow, delay);
    }
}
