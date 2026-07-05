using System.Net.Http.Headers;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Proves conservative spacing, rolling windows, cooldowns, cancellation, and hard request budgets.
/// </summary>
public sealed class ArchidektRequestPacerTests
{
    /// <summary>
    /// Verifies three starts reserve one shared timeline at zero, two, and four seconds.
    /// </summary>
    [Fact]
    public async Task WaitForPermitAsync_SpacesEveryRequestStart()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        List<TimeSpan> delays = [];
        ArchidektOptions options = Options(minimumInterval: TimeSpan.FromSeconds(2));
        ArchidektRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            options,
            () => now,
            (duration, _) =>
            {
                delays.Add(duration);
                now += duration;
                return Task.CompletedTask;
            });
        ArchidektOperationBudget budget = new(3);

        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);
        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);
        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], delays);
        Assert.Equal(3, budget.RequestCount);
    }

    /// <summary>
    /// Verifies separate adapter instances for the same account share one process-wide request lane.
    /// </summary>
    [Fact]
    public async Task SeparatePacers_ShareOneAccountTimeline()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        List<TimeSpan> secondPacerDelays = [];
        string accountKey = Guid.NewGuid().ToString("N");
        ArchidektOptions options = Options(minimumInterval: TimeSpan.FromSeconds(2));
        ArchidektRequestPacer first = new(
            accountKey,
            options,
            () => now,
            static (_, _) => Task.CompletedTask);
        ArchidektRequestPacer second = new(
            accountKey,
            options,
            () => now,
            (duration, _) =>
            {
                secondPacerDelays.Add(duration);
                now += duration;
                return Task.CompletedTask;
            });

        await first.WaitForPermitAsync(
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);
        await second.WaitForPermitAsync(
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(2)], secondPacerDelays);
    }

    /// <summary>
    /// Verifies the rolling ceiling applies even when minimum spacing is disabled for the test.
    /// </summary>
    [Fact]
    public async Task WaitForPermitAsync_EnforcesRollingWindowCeiling()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        List<TimeSpan> delays = [];
        ArchidektOptions options = Options(
            minimumInterval: TimeSpan.Zero,
            maximumRequests: 2,
            window: TimeSpan.FromMinutes(1));
        ArchidektRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            options,
            () => now,
            (duration, _) =>
            {
                delays.Add(duration);
                now += duration;
                return Task.CompletedTask;
            });
        ArchidektOperationBudget budget = new(3);

        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);
        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);
        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromMinutes(1)], delays);
    }

    /// <summary>
    /// Verifies Retry-After opens one shared cooldown and a future start waits through it.
    /// </summary>
    [Fact]
    public async Task ObserveRateLimitAsync_AppliesRetryAfterWithoutRetrying()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        List<TimeSpan> delays = [];
        ArchidektRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            Options(minimumInterval: TimeSpan.Zero),
            () => now,
            (duration, _) =>
            {
                delays.Add(duration);
                now += duration;
                return Task.CompletedTask;
            });

        await pacer.ObserveRateLimitAsync(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(17)),
            TestContext.Current.CancellationToken);
        await pacer.WaitForPermitAsync(
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(17)], delays);
    }

    /// <summary>
    /// Verifies one instance's provider cooldown blocks another instance using the same account lane.
    /// </summary>
    [Fact]
    public async Task SeparatePacers_ShareProviderCooldown()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        List<TimeSpan> delays = [];
        string accountKey = Guid.NewGuid().ToString("N");
        ArchidektOptions options = Options(minimumInterval: TimeSpan.Zero);
        ArchidektRequestPacer observer = new(
            accountKey,
            options,
            () => now,
            static (_, _) => Task.CompletedTask);
        ArchidektRequestPacer waiter = new(
            accountKey,
            options,
            () => now,
            (duration, _) =>
            {
                delays.Add(duration);
                now += duration;
                return Task.CompletedTask;
            });

        await observer.ObserveRateLimitAsync(
            new RetryConditionHeaderValue(TimeSpan.FromSeconds(23)),
            TestContext.Current.CancellationToken);
        await waiter.WaitForPermitAsync(
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(23)], delays);
    }

    /// <summary>
    /// Verifies absent and excessive Retry-After values use conservative bounded cooldowns.
    /// </summary>
    [Theory]
    [InlineData(false, 60)]
    [InlineData(true, 86_400)]
    public async Task ObserveRateLimitAsync_BoundsFallbackCooldown(bool excessive, int expectedSeconds)
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        TimeSpan observed = TimeSpan.Zero;
        ArchidektRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            Options(minimumInterval: TimeSpan.Zero),
            () => now,
            (duration, _) =>
            {
                observed = duration;
                now += duration;
                return Task.CompletedTask;
            });
        RetryConditionHeaderValue? retry = excessive
            ? new RetryConditionHeaderValue(TimeSpan.FromDays(2))
            : null;

        await pacer.ObserveRateLimitAsync(retry, TestContext.Current.CancellationToken);
        await pacer.WaitForPermitAsync(
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), observed);
    }

    /// <summary>
    /// Verifies a stale absolute Retry-After date cannot bypass the conservative fallback cooldown.
    /// </summary>
    [Fact]
    public async Task ObserveRateLimitAsync_PastDateUsesFallbackCooldown()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        TimeSpan observed = TimeSpan.Zero;
        ArchidektRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            Options(minimumInterval: TimeSpan.Zero),
            () => now,
            (duration, _) =>
            {
                observed = duration;
                now += duration;
                return Task.CompletedTask;
            });

        await pacer.ObserveRateLimitAsync(
            new RetryConditionHeaderValue(now - TimeSpan.FromMinutes(1)),
            TestContext.Current.CancellationToken);
        await pacer.WaitForPermitAsync(
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(1), observed);
    }

    /// <summary>
    /// Verifies the per-operation ceiling fails before an additional provider start can be recorded.
    /// </summary>
    [Fact]
    public async Task WaitForPermitAsync_RejectsRequestBeyondOperationBudget()
    {
        ArchidektRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            Options(minimumInterval: TimeSpan.Zero),
            static () => DateTimeOffset.UtcNow,
            static (_, _) => Task.CompletedTask);
        ArchidektOperationBudget budget = new(1);

        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);
        ArchidektProviderException exception = await Assert.ThrowsAsync<ArchidektProviderException>(
            () => pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken));

        Assert.Equal("request-limit-exceeded", exception.ReasonCode);
        Assert.Equal(1, budget.RequestCount);
    }

    /// <summary>
    /// Verifies cancellation during a pacing delay releases the shared lane for a later caller.
    /// </summary>
    [Fact]
    public async Task WaitForPermitAsync_CancelledDelayReleasesSharedLane()
    {
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        bool cancelNextDelay = true;
        using CancellationTokenSource cancellation = new();
        ArchidektRequestPacer pacer = new(
            Guid.NewGuid().ToString("N"),
            Options(minimumInterval: TimeSpan.FromSeconds(2)),
            () => now,
            (duration, token) =>
            {
                if (cancelNextDelay)
                {
                    cancelNextDelay = false;
                    cancellation.Cancel();
                    return Task.FromCanceled(token);
                }

                now += duration;
                return Task.CompletedTask;
            });
        ArchidektOperationBudget budget = new(3);

        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pacer.WaitForPermitAsync(budget, cancellation.Token));
        await pacer.WaitForPermitAsync(budget, TestContext.Current.CancellationToken);

        Assert.Equal(new DateTimeOffset(2026, 7, 4, 12, 0, 2, TimeSpan.Zero), now);
    }

    /// <summary>
    /// Creates one validated test configuration with independently selectable pacing bounds.
    /// </summary>
    private static ArchidektOptions Options(
        TimeSpan minimumInterval,
        int maximumRequests = 30,
        TimeSpan? window = null)
    {
        return ArchidektOptions.CreateDefault("user", "secret") with
        {
            MinimumRequestInterval = minimumInterval,
            MaximumRequestsPerWindow = maximumRequests,
            RequestWindow = window ?? TimeSpan.FromMinutes(1),
        };
    }
}
