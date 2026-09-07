using System.Net;

namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Verifies fixed-origin source transport behavior, response bounds, timeout, and cancellation.
/// </summary>
public sealed class SpellbookTransportTests
{
    /// <summary>
    /// Verifies the transport uses its fixed source origin, sends the required headers, and performs one attempt.
    /// </summary>
    [Fact]
    public async Task Transport_UsesFixedOriginHeadersAndOneAttempt()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddJson("{\"unknown\":true}");
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookTransport transport = SpellbookTestFactory.CreateTransport(handler, clock);

        SpellbookNetworkResponse response = await transport.SendAsync(
            HttpMethod.Get,
            "variants/?q=Sol%20Ring&limit=20&offset=0&groupByCombo=true",
            body: null,
            TestContext.Current.CancellationToken);

        CapturedSpellbookRequest request = Assert.Single(handler.Requests);
        Assert.Equal("https://backend.commanderspellbook.com", request.Uri.GetLeftPart(UriPartial.Authority));
        Assert.Equal("/variants/?q=Sol%20Ring&limit=20&offset=0&groupByCombo=true", request.Uri.PathAndQuery);
        Assert.Contains("mtg-mcp/", request.UserAgent, StringComparison.Ordinal);
        Assert.Contains("application/json", request.Accept, StringComparison.Ordinal);
        Assert.Equal(SpellbookHash.Compute("{\"unknown\":true}"), response.SourceChecksum);
        Assert.Equal(clock.GetUtcNow(), response.RetrievedAtUtc);
    }

    /// <summary>
    /// Verifies network exceptions become a safe unavailable failure without retaining provider exception text.
    /// </summary>
    [Fact]
    public async Task Transport_NetworkFailureIsSanitized()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddFailure(new HttpRequestException("private network address"));
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookTransport transport = SpellbookTestFactory.CreateTransport(handler, clock);

        SpellbookProviderException failure = await Assert.ThrowsAsync<SpellbookProviderException>(() => transport.SendAsync(
            HttpMethod.Get,
            "variants/?q=Sol%20Ring&limit=20&offset=0&groupByCombo=true",
            body: null,
            TestContext.Current.CancellationToken));

        Assert.Equal(SpellbookFailureKind.Unavailable, failure.Kind);
        Assert.DoesNotContain("private", failure.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies only the fixed transport timeout becomes a safe unavailable result.
    /// </summary>
    [Fact]
    public async Task Transport_MapsOwnTimeoutToUnavailable()
    {
        SpellbookTestHttpHandler handler = new();
        handler.AddResponse(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookTransport transport = SpellbookTestFactory.CreateTransport(
            handler,
            clock,
            TimeSpan.FromMilliseconds(25));

        SpellbookProviderException failure = await Assert.ThrowsAsync<SpellbookProviderException>(() => transport.SendAsync(
            HttpMethod.Get,
            "variants/?q=Sol%20Ring&limit=20&offset=0&groupByCombo=true",
            body: null,
            TestContext.Current.CancellationToken));

        Assert.Equal("source-timeout", failure.ReasonCode);
        Assert.Equal(SpellbookFailureKind.Unavailable, failure.Kind);
    }

    /// <summary>
    /// Verifies caller cancellation passes through rather than being changed into a source result.
    /// </summary>
    [Fact]
    public async Task Transport_PropagatesCallerCancellation()
    {
        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SpellbookTestHttpHandler handler = new();
        handler.AddResponse(async (_, cancellationToken) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        using SpellbookTransport transport = SpellbookTestFactory.CreateTransport(handler, clock);
        using CancellationTokenSource cancellation = new();

        Task<SpellbookNetworkResponse> pending = transport.SendAsync(
            HttpMethod.Get,
            "variants/?q=Sol%20Ring&limit=20&offset=0&groupByCombo=true",
            body: null,
            cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }
}
