using System.Diagnostics;
using FluentAssertions;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Contains tests for adapter request pacing primitives.
/// </summary>
public sealed class RequestPacingTests
{
    /// <summary>
    /// Verifies that minimum-delay pacing waits before a second request.
    /// </summary>
    [Fact]
    public async Task WaitForMinimumDelayAsync_DelaysSecondRequest()
    {
        using MtgMcpRequestPacer requestPacer = new();
        Stopwatch stopwatch = Stopwatch.StartNew();

        await requestPacer.WaitForMinimumDelayAsync(
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);
        await requestPacer.WaitForMinimumDelayAsync(
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(35));
    }

    /// <summary>
    /// Verifies that sliding-window pacing waits when the request budget is exhausted.
    /// </summary>
    [Fact]
    public async Task WaitForSlidingWindowAsync_DelaysWhenWindowIsFull()
    {
        using MtgMcpRequestPacer requestPacer = new();
        Stopwatch stopwatch = Stopwatch.StartNew();

        await requestPacer.WaitForSlidingWindowAsync(
            1,
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);
        await requestPacer.WaitForSlidingWindowAsync(
            1,
            TimeSpan.FromMilliseconds(50),
            TestContext.Current.CancellationToken);

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(35));
    }
}
