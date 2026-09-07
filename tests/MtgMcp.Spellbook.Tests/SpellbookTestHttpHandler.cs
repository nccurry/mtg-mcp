using System.Net;
using System.Net.Http.Headers;

namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Captures exact source requests and serves queued, deterministic source responses.
/// </summary>
internal sealed class SpellbookTestHttpHandler : HttpMessageHandler
{
    /// <summary>
    /// Stores queued responses and transport failures in source-call order.
    /// </summary>
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responses = new();

    /// <summary>
    /// Gets redaction-safe request observations in send order.
    /// </summary>
    internal List<CapturedSpellbookRequest> Requests { get; } = [];

    /// <summary>
    /// Queues one JSON response with an optional source status.
    /// </summary>
    internal void AddJson(string json = "{}", HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json),
        }));
    }

    /// <summary>
    /// Queues one source status with an optional Retry-After header.
    /// </summary>
    internal void AddStatus(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
    {
        responses.Enqueue((_, _) =>
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent("{\"private\":\"hidden\"}"),
            };
            if (retryAfter is not null)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
            }

            return Task.FromResult(response);
        });
    }

    /// <summary>
    /// Queues one network or stream failure.
    /// </summary>
    internal void AddFailure(Exception exception)
    {
        responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    /// <summary>
    /// Queues one custom asynchronous source response for cancellation tests.
    /// </summary>
    internal void AddResponse(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        ArgumentNullException.ThrowIfNull(response);
        responses.Enqueue(response);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new CapturedSpellbookRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.UserAgent.ToString(),
            request.Headers.Accept.ToString(),
            body));
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No deterministic Commander Spellbook response was queued.");
        }

        return await responses.Dequeue()(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Preserves the safe portions of one Commander Spellbook HTTP request for assertions.
/// </summary>
internal sealed record CapturedSpellbookRequest(
    HttpMethod Method,
    Uri Uri,
    string UserAgent,
    string Accept,
    string? Body);
