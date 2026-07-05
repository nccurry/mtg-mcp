using System.Net;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Captures exact HTTP attempts and serves deterministic queued provider behavior.
/// </summary>
internal sealed class PlaygroupTestHttpHandler : HttpMessageHandler
{
    /// <summary>Stores queued response or failure factories.</summary>
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responses = new();

    /// <summary>Gets captured requests in start order.</summary>
    internal List<CapturedRequest> Requests { get; } = [];

    /// <summary>Queues one JSON response.</summary>
    internal void AddJson(string json = "{}", HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json),
        }));
    }

    /// <summary>Queues one response with an optional Retry-After delta.</summary>
    internal void AddStatus(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
    {
        responses.Enqueue((_, _) =>
        {
            HttpResponseMessage response = new(statusCode) { Content = new StringContent("{\"private\":\"hidden\"}") };
            if (retryAfter is not null)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            }

            return Task.FromResult(response);
        });
    }

    /// <summary>Queues one ambiguous transport failure.</summary>
    internal void AddFailure(Exception exception)
    {
        responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    /// <summary>Queues one caller-constructed response for stream and header edge cases.</summary>
    internal void AddResponse(HttpResponseMessage response)
    {
        responses.Enqueue((_, _) => Task.FromResult(response));
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body));
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No deterministic Playgroup response was queued.");
        }

        return await responses.Dequeue()(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Preserves one redaction-safe HTTP request observation for assertions.
/// </summary>
internal sealed record CapturedRequest(
    HttpMethod Method,
    string PathAndQuery,
    string? AuthScheme,
    string? AuthParameter,
    string? Body);

/// <summary>
/// Simulates a provider connection that fails after successful response headers.
/// </summary>
internal sealed class ThrowingReadStream : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new IOException("private body failure");
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromException<int>(new IOException("private body failure"));
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
