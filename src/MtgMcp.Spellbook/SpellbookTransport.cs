using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MtgMcp.Spellbook;

/// <summary>
/// Sends one bounded Commander Spellbook request at the fixed public origin without retries.
/// </summary>
internal sealed class SpellbookTransport : IDisposable
{
    /// <summary>
    /// Caps one complete source response before it reaches JSON parsing or cache storage.
    /// </summary>
    internal const int MaximumResponseBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Bounds one source request independently of caller cancellation.
    /// </summary>
    internal static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Stores the fixed official source origin.
    /// </summary>
    private static readonly Uri SourceBaseUri = new("https://backend.commanderspellbook.com/", UriKind.Absolute);

    /// <summary>
    /// Sends source HTTP requests.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Reports whether this object disposes the injected client.
    /// </summary>
    private readonly bool ownsHttpClient;

    /// <summary>
    /// Supplies source retrieval times and converts HTTP-date retry headers.
    /// </summary>
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Stores the fixed production timeout or an internal test-only equivalent.
    /// </summary>
    private readonly TimeSpan requestTimeout;

    /// <summary>
    /// Creates the production transport with the fixed official origin and source-friendly headers.
    /// </summary>
    internal SpellbookTransport(string packageVersion, TimeProvider timeProvider)
        : this(CreateHttpClient(), ownsHttpClient: true, timeProvider, RequestTimeout, packageVersion)
    {
    }

    /// <summary>
    /// Creates a deterministic transport over an injected handler or client.
    /// </summary>
    internal SpellbookTransport(
        HttpClient httpClient,
        bool ownsHttpClient,
        TimeProvider timeProvider,
        TimeSpan? requestTimeout = null,
        string packageVersion = "test")
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.requestTimeout = requestTimeout ?? RequestTimeout;
        if (this.requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "The source timeout must be positive.");
        }

        ConfigureHttpClient(this.httpClient, packageVersion);
    }

    /// <summary>
    /// Sends one source request and returns its complete bounded JSON response without following pagination.
    /// </summary>
    internal async Task<SpellbookNetworkResponse> SendAsync(
        HttpMethod method,
        string pathAndQuery,
        string? body,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(requestTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            using HttpRequestMessage request = CreateRequest(method, pathAndQuery, body);
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw MapStatus(response.StatusCode, response.Headers.RetryAfter);
            }

            string json = await ReadBoundedJsonAsync(response.Content, linked.Token).ConfigureAwait(false);
            return new SpellbookNetworkResponse(
                json,
                SpellbookHash.Compute(json),
                timeProvider.GetUtcNow().ToUniversalTime());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new SpellbookProviderException(
                SpellbookFailureKind.Unavailable,
                "source-timeout",
                "Commander Spellbook did not respond before the supported timeout.",
                innerException: exception);
        }
        catch (OperationCanceledException exception)
        {
            throw Unavailable(exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TimeoutException)
        {
            throw Unavailable(exception);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    /// <summary>
    /// Creates one source request without allowing a caller-controlled origin or headers.
    /// </summary>
    private static HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery, string? body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathAndQuery);
        HttpRequestMessage request = new(method, pathAndQuery);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>
    /// Maps a terminal source status without reading an error body.
    /// </summary>
    private SpellbookProviderException MapStatus(
        HttpStatusCode statusCode,
        RetryConditionHeaderValue? retryAfter)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest => new SpellbookProviderException(
                SpellbookFailureKind.InvalidInput,
                "source-request-rejected",
                "Commander Spellbook rejected the request as invalid."),
            HttpStatusCode.NotFound => new SpellbookProviderException(
                SpellbookFailureKind.NotFound,
                "source-not-found",
                "Commander Spellbook did not find the requested variant."),
            HttpStatusCode.TooManyRequests => new SpellbookProviderException(
                SpellbookFailureKind.RateLimited,
                "source-rate-limited",
                "Commander Spellbook is temporarily rate-limiting requests.",
                RetryAfter(retryAfter)),
            _ => new SpellbookProviderException(
                SpellbookFailureKind.Unavailable,
                "source-unavailable",
                "Commander Spellbook could not satisfy the request."),
        };
    }

    /// <summary>
    /// Reads a source Retry-After header into a duration before the pacer applies its local cap.
    /// </summary>
    private TimeSpan? RetryAfter(RetryConditionHeaderValue? value)
    {
        if (value?.Delta is { } delta)
        {
            return delta;
        }

        if (value?.Date is not { } retryAtUtc)
        {
            return null;
        }

        TimeSpan duration = retryAtUtc.ToUniversalTime() - timeProvider.GetUtcNow().ToUniversalTime();
        return duration > TimeSpan.Zero ? duration : null;
    }

    /// <summary>
    /// Reads one complete UTF-8 source response while enforcing the adapter size ceiling.
    /// </summary>
    private static async Task<string> ReadBoundedJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new SpellbookProviderException(
                SpellbookFailureKind.Unavailable,
                "source-response-too-large",
                "Commander Spellbook returned a response larger than the supported evidence limit.");
        }

        await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream destination = new();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return Encoding.UTF8.GetString(destination.GetBuffer(), 0, checked((int)destination.Length));
            }

            if (destination.Length + read > MaximumResponseBytes)
            {
                throw new SpellbookProviderException(
                    SpellbookFailureKind.Unavailable,
                    "source-response-too-large",
                    "Commander Spellbook returned a response larger than the supported evidence limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// Creates a safe unavailable result for network and stream failures.
    /// </summary>
    private static SpellbookProviderException Unavailable(Exception exception)
    {
        return new SpellbookProviderException(
            SpellbookFailureKind.Unavailable,
            "source-unavailable",
            "Commander Spellbook could not be reached.",
            innerException: exception);
    }

    /// <summary>
    /// Applies the fixed source origin and identical source-friendly headers to every transport client.
    /// </summary>
    private static void ConfigureHttpClient(HttpClient client, string packageVersion)
    {
        string version = SpellbookContract.RequiredVariantId(packageVersion, nameof(packageVersion));
        client.BaseAddress = SourceBaseUri;
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("mtg-mcp", version));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/nccurry/mtg-mcp)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Creates the production HTTP client with automatic redirects disabled before transport configuration.
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true);
    }
}
