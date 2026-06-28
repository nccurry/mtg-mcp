using System.Net.Http.Headers;
using FluentAssertions;
using MtgMcp.Core;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Covers shared HTTP retry timing helpers.
/// </summary>
public sealed class HttpRetryTests
{
    /// <summary>
    /// Verifies that Retry-After delta headers win over body and fallback hints.
    /// </summary>
    [Fact]
    public void GetRetryDelay_UsesRetryAfterDelta()
    {
        using HttpResponseMessage response = new();
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));

        TimeSpan delay = MtgMcpHttpRetry.GetRetryDelay(
            response,
            "available in 9 seconds",
            "available in ",
            TimeSpan.FromSeconds(5));

        delay.Should().Be(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Verifies that body markers provide retry timing when headers are absent.
    /// </summary>
    [Fact]
    public void GetRetryDelay_UsesBodyMarkerWhenHeaderIsAbsent()
    {
        using HttpResponseMessage response = new();

        TimeSpan delay = MtgMcpHttpRetry.GetRetryDelay(
            response,
            """{"detail":"request was throttled; available in 7 seconds"}""",
            "available in ",
            TimeSpan.FromSeconds(5));

        delay.Should().Be(TimeSpan.FromSeconds(7));
    }

    /// <summary>
    /// Verifies that negative delays are clamped before callers pass them to Task.Delay.
    /// </summary>
    [Fact]
    public void GetRetryDelay_ClampsNegativeHeaderAndFallback()
    {
        using HttpResponseMessage response = new();
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(-1));
        using HttpResponseMessage fallbackResponse = new();

        MtgMcpHttpRetry.GetRetryDelay(response, null, "after ", TimeSpan.FromSeconds(5))
            .Should().Be(TimeSpan.Zero);
        MtgMcpHttpRetry.GetRetryDelay(
                fallbackResponse,
                null,
                "after ",
                TimeSpan.FromSeconds(-5))
            .Should().Be(TimeSpan.Zero);
    }

    /// <summary>
    /// Verifies that missing or malformed body hints fall back safely.
    /// </summary>
    [Fact]
    public void GetRetryDelay_UsesFallbackForMalformedBodyMarker()
    {
        using HttpResponseMessage response = new();

        TimeSpan delay = MtgMcpHttpRetry.GetRetryDelay(
            response,
            "available in soon",
            "available in ",
            TimeSpan.FromSeconds(5));

        delay.Should().Be(TimeSpan.FromSeconds(5));
    }
}
