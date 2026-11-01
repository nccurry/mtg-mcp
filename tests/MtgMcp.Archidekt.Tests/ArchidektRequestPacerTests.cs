using System.Net.Http.Headers;
using System.Text.Json;

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
    /// Verifies the rolling limit applies even when minimum spacing is disabled for the test.
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
    /// Verifies the per-operation limit fails before an additional provider start can be recorded.
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
    /// Verifies a planned total over the limit is refused with the limit and the predicted count for an operation that
    /// is not a push, and that a total at the limit passes.
    /// </summary>
    [Fact]
    public void EnsureRequestBound_OverTheLimit_NamesTheLimitAndTheCount()
    {
        ArchidektOperationBudget budget = new(100);

        ArchidektProviderException refused = Assert.Throws<ArchidektProviderException>(
            () => budget.EnsureRequestBound(130));

        Assert.Equal("request-limit-exceeded", refused.ReasonCode);
        Assert.Equal(
            "The operation needs about 130 requests to Archidekt, over the 100-request limit for one call; no remote " +
            "writes were attempted.",
            refused.Message);
        budget.EnsureRequestBound(100);
    }

    /// <summary>
    /// Verifies a push over the limit says how many of its changes fit and that it can be split, with numbers worked
    /// out by hand for 400 batched adds: 16 batches of 25 and a reserve of 25 cards to send one batch again card by
    /// card make 41 requests, so with 119 other requests under a limit of 150, 31 of the 41 fit, which is 302 of the
    /// 400 changes (400 * 31 / 41, rounded down).
    /// </summary>
    [Fact]
    public void EnsurePushRequestBound_WithBatchedAdds_CountsRequestsNotChanges()
    {
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCards(400);
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
        ArchidektOperationBudget budget = new(150);

        ArchidektProviderException refused = Assert.Throws<ArchidektProviderException>(
            () => budget.EnsurePushRequestBound(160, plan.PlannedOperations.Count, plan.OperationRequestCount));

        Assert.Equal(400, plan.PlannedOperations.Count);
        Assert.Equal(41, plan.OperationRequestCount);
        Assert.Equal("request-limit-exceeded", refused.ReasonCode);
        Assert.Equal(
            "This push needs about 160 requests to Archidekt, over the 150-request limit for one " +
            "archidekt_push_apply call. About 302 of the 400 changes fit (an estimate). Split the push: apply about " +
            "302 changes first, then preview and push the rest. No remote writes were attempted.",
            refused.Message);
        budget.EnsurePushRequestBound(150, plan.PlannedOperations.Count, plan.OperationRequestCount);
    }

    /// <summary>
    /// Builds the canonical test deck and a target that adds the given number of cards that carry an Archidekt card ID.
    /// </summary>
    private static (RemoteDeckSnapshot Current, RemoteDeckSnapshot Target) DeckWithNewCards(int count)
    {
        using JsonDocument document = JsonDocument.Parse(ArchidektTestPayloads.Deck);
        RemoteDeckSnapshot current = ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            ArchidektTestPayloads.Deck,
            DateTimeOffset.UtcNow,
            "GET");
        IEnumerable<RemoteDeckEntry> added = Enumerable.Range(1, count).Select(number => new RemoteDeckEntry(
            string.Empty,
            (5000 + number).ToString(System.Globalization.CultureInfo.InvariantCulture),
            1,
            $"Card {number:000}",
            OracleId: null,
            PrintingId: null,
            SetCode: null,
            CollectorNumber: null,
            "en",
            "nonfoil",
            "main",
            ["Mainboard"],
            "Mainboard",
            number));
        return (current, current with { Entries = [.. current.Entries, .. added] });
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
