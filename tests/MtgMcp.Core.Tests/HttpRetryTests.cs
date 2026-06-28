using System.Net;
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

    /// <summary>
    /// Verifies that shared HTTP sending retries transient responses with a fresh request.
    /// </summary>
    [Fact]
    public async Task SendForStringAsync_RetriesTransientResponse()
    {
        using QueueHttpMessageHandler handler = new(
            CreateResponse(HttpStatusCode.TooManyRequests, "try later", retryAfter: TimeSpan.Zero),
            CreateResponse(HttpStatusCode.OK, """{"ok":true}"""));
        using HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://retry.test/") };

        MtgMcpHttpTextResponse response = await MtgMcpHttpRetry.SendForStringAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "resource"),
            "Retry test",
            2,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Body.Should().Contain("\"ok\"");
        handler.CallCount.Should().Be(2);
        handler.RequestUris.Should().OnlyContain(uri => uri == "https://retry.test/resource");
    }

    /// <summary>
    /// Verifies that allowed non-success statuses are returned without retrying or throwing.
    /// </summary>
    [Fact]
    public async Task SendForStringAsync_ReturnsAllowedNonSuccessStatus()
    {
        using QueueHttpMessageHandler handler = new(CreateResponse(HttpStatusCode.NotFound, "missing"));
        using HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://retry.test/") };

        MtgMcpHttpTextResponse response = await MtgMcpHttpRetry.SendForStringAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "missing"),
            "Retry test",
            2,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken,
            HttpStatusCode.NotFound);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Body.Should().Be("missing");
        handler.CallCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that provider failure messages redact secret-bearing response bodies.
    /// </summary>
    [Fact]
    public async Task SendForStringAsync_RedactsFailureBody()
    {
        using QueueHttpMessageHandler handler = new(CreateResponse(
            HttpStatusCode.InternalServerError,
            """{"authorization":"Bearer secret-token-value"}"""));
        using HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://retry.test/") };

        Func<Task> act = () => MtgMcpHttpRetry.SendForStringAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "failure"),
            "Retry test",
            0,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        HttpRequestException exception = (await act.Should()
            .ThrowAsync<HttpRequestException>()
            .Where(exception => exception.StatusCode == HttpStatusCode.InternalServerError))
            .Which;
        exception.Message.Should().Contain("***REDACTED***");
        exception.Message.Should().NotContain("secret-token-value");
    }

    /// <summary>
    /// Verifies that failure messages do not include unbounded provider response bodies.
    /// </summary>
    [Fact]
    public async Task SendForStringAsync_TruncatesFailureBody()
    {
        string longBody = new('x', 3000);
        using QueueHttpMessageHandler handler = new(CreateResponse(HttpStatusCode.BadGateway, longBody));
        using HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://retry.test/") };

        Func<Task> act = () => MtgMcpHttpRetry.SendForStringAsync(
            httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, "failure"),
            "Retry test",
            0,
            TimeSpan.Zero,
            TestContext.Current.CancellationToken);

        HttpRequestException exception = (await act.Should()
            .ThrowAsync<HttpRequestException>()
            .Where(exception => exception.StatusCode == HttpStatusCode.BadGateway))
            .Which;
        exception.Message.Should().EndWith("...");
        exception.Message.Should().NotContain(longBody);
    }

    /// <summary>
    /// Creates a queued text response for retry tests.
    /// </summary>
    private static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode,
        string body,
        TimeSpan? retryAfter = null)
    {
        HttpResponseMessage response = new(statusCode)
        {
            Content = new StringContent(body)
        };
        if (retryAfter.HasValue)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }

    /// <summary>
    /// Serves queued HTTP responses and records requested URIs.
    /// </summary>
    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>
        /// Stores responses to return to the client.
        /// </summary>
        private readonly Queue<HttpResponseMessage> responses = new();

        /// <summary>
        /// Creates a handler from queued responses.
        /// </summary>
        public QueueHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            foreach (HttpResponseMessage response in responses)
            {
                this.responses.Enqueue(response);
            }
        }

        /// <summary>
        /// Gets the number of requests handled.
        /// </summary>
        public int CallCount { get; private set; }

        /// <summary>
        /// Gets the absolute request URIs that were sent.
        /// </summary>
        public List<string> RequestUris { get; } = [];

        /// <summary>
        /// Returns the next queued response.
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri?.ToString() ?? "");
            return Task.FromResult(responses.Dequeue());
        }

        /// <summary>
        /// Releases any queued responses that were not consumed.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                while (responses.TryDequeue(out HttpResponseMessage? response))
                {
                    response.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
