using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Playgroup;

/// <summary>
/// Owns the fixed public origin, bearer authentication, pacing, retries, and safe HTTP failures.
/// </summary>
internal sealed class PlaygroupTransport : IDisposable
{
    /// <summary>Caps one provider response so list endpoints cannot consume unbounded memory or model context.</summary>
    internal const int MaximumResponseBytes = 2 * 1024 * 1024;

    /// <summary>Caps the documented all-commander dataset before exact local row selection.</summary>
    internal const int MaximumTurnDamageResponseBytes = 16 * 1024 * 1024;

    /// <summary>Serializes documented snake-case request fields.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Sends provider requests.</summary>
    private readonly HttpClient httpClient;

    /// <summary>Reports whether this transport owns the injected client.</summary>
    private readonly bool ownsHttpClient;

    /// <summary>Stores one privately loaded key for request construction.</summary>
    private readonly PlaygroupCredentials.CredentialLoad credentials;

    /// <summary>Applies shared conservative provider pacing.</summary>
    private readonly PlaygroupRequestPacer pacer;

    /// <summary>Creates a production transport against the fixed official public API origin.</summary>
    internal PlaygroupTransport(PlaygroupOptions options, string packageVersion)
        : this(CreateHttpClient(packageVersion), ownsHttpClient: true, options)
    {
    }

    /// <summary>Creates a deterministic transport over an injected HTTP client.</summary>
    internal PlaygroupTransport(
        HttpClient httpClient,
        bool ownsHttpClient,
        PlaygroupOptions options,
        PlaygroupRequestPacer? pacer = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        credentials = new PlaygroupCredentials(options).Load();
        string laneKey = credentials.ApiKey is null
            ? "anonymous"
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.ApiKey)));
        this.pacer = pacer ?? new PlaygroupRequestPacer(laneKey, options);
    }

    /// <summary>Reports key readiness without provider I/O or identity disclosure.</summary>
    internal PlaygroupAuthStatus GetAuthStatus()
    {
        return new PlaygroupAuthStatus(credentials.State, credentials.IsUsable, credentials.Message);
    }

    /// <summary>Sends one provider operation and returns lossless response evidence.</summary>
    internal async Task<PlaygroupEvidence> SendAsync(
        HttpMethod method,
        string pathAndQuery,
        string operationId,
        object? payload,
        bool requiresAuthentication,
        bool idempotentRead,
        CancellationToken cancellationToken,
        int maximumResponseBytes = MaximumResponseBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumResponseBytes);
        if (requiresAuthentication && credentials.ApiKey is null)
        {
            throw new PlaygroupProviderException(
                PlaygroupFailureKind.Unavailable,
                "playgroup-auth-required",
                "This Playgroup operation requires a configured API key.");
        }

        int transientRetries = 0;
        bool rateLimitRetried = false;
        while (true)
        {
            await pacer.WaitForPermitAsync(cancellationToken).ConfigureAwait(false);
            using HttpRequestMessage request = CreateRequest(method, pathAndQuery, payload, credentials.ApiKey);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or TimeoutException)
            {
                if (idempotentRead && transientRetries < 2)
                {
                    transientRetries++;
                    continue;
                }

                throw TransportFailure(idempotentRead, exception);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    bool bounded = await pacer.ObserveRetryAfterAsync(
                        response.Headers.RetryAfter,
                        cancellationToken).ConfigureAwait(false);
                    if (idempotentRead && bounded && !rateLimitRetried)
                    {
                        rateLimitRetried = true;
                        continue;
                    }

                    throw Unavailable("provider-rate-limited", "Playgroup rate-limited the request.");
                }

                if (IsTransient(response.StatusCode) && idempotentRead && transientRetries < 2)
                {
                    transientRetries++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw MapStatus(response.StatusCode, idempotentRead);
                }

                string json;
                try
                {
                    json = await ReadBoundedJsonAsync(
                        response.Content,
                        maximumResponseBytes,
                        cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (PlaygroupProviderException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or IOException or TimeoutException)
                {
                    if (idempotentRead && transientRetries < 2)
                    {
                        transientRetries++;
                        continue;
                    }

                    throw TransportFailure(idempotentRead, exception);
                }
                DateTimeOffset retrievedAtUtc = DateTimeOffset.UtcNow;
                return new PlaygroupEvidence(
                    operationId,
                    $"{method.Method} /{pathAndQuery.Split('?', 2)[0].TrimStart('/')}",
                    PlaygroupContract.ApiVersion,
                    PlaygroupContract.OpenApiChecksum,
                    retrievedAtUtc,
                    PlaygroupContract.Checksum(json),
                    PlaygroupContract.Limitations,
                    PlaygroupContract.ParseData(json));
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    /// <summary>Creates one request without permitting caller-controlled origins or headers.</summary>
    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string pathAndQuery,
        object? payload,
        string? apiKey)
    {
        HttpRequestMessage request = new(method, pathAndQuery);
        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (payload is not null)
        {
            string json = JsonSerializer.Serialize(payload, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>Maps a terminal status without reading potentially sensitive response bodies.</summary>
    private static PlaygroupProviderException MapStatus(HttpStatusCode statusCode, bool idempotentRead)
    {
        return statusCode switch
        {
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new PlaygroupProviderException(
                PlaygroupFailureKind.InvalidInput,
                "provider-request-rejected",
                "Playgroup rejected the request as invalid."),
            HttpStatusCode.Unauthorized => Unavailable(
                "provider-unauthorized",
                "Playgroup rejected the configured authentication."),
            HttpStatusCode.Forbidden => Unavailable(
                "provider-forbidden",
                "Playgroup denied this operation."),
            HttpStatusCode.NotFound => new PlaygroupProviderException(
                PlaygroupFailureKind.NotFound,
                "provider-entity-not-found",
                "Playgroup did not find the requested entity."),
            _ when !idempotentRead => Unavailable(
                "provider-write-acceptance-unknown",
                "Playgroup write acceptance is unknown; inspect provider state before retrying."),
            _ => Unavailable("provider-unavailable", "Playgroup could not satisfy the request."),
        };
    }

    /// <summary>Reports whether a response is eligible for bounded idempotent-read retry.</summary>
    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;
    }

    /// <summary>Reads one UTF-8 provider document while enforcing the adapter response ceiling.</summary>
    private static async Task<string> ReadBoundedJsonAsync(
        HttpContent content,
        int maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumResponseBytes)
        {
            throw new PlaygroupProviderException(
                PlaygroupFailureKind.Unavailable,
                "provider-response-too-large",
                "Playgroup returned a response larger than the supported evidence limit.");
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

            if (destination.Length + read > maximumResponseBytes)
            {
                throw new PlaygroupProviderException(
                    PlaygroupFailureKind.Unavailable,
                    "provider-response-too-large",
                    "Playgroup returned a response larger than the supported evidence limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    /// <summary>Creates one standardized unavailable failure.</summary>
    private static PlaygroupProviderException Unavailable(string reasonCode, string message)
    {
        return new PlaygroupProviderException(PlaygroupFailureKind.Unavailable, reasonCode, message);
    }

    /// <summary>Maps a terminal transport failure without retaining exception text.</summary>
    private static PlaygroupProviderException TransportFailure(bool idempotentRead, Exception exception)
    {
        return idempotentRead
            ? new PlaygroupProviderException(
                PlaygroupFailureKind.Unavailable,
                "provider-unavailable",
                "Playgroup could not be reached after bounded read retries.",
                exception)
            : new PlaygroupProviderException(
                PlaygroupFailureKind.Unavailable,
                "provider-write-acceptance-unknown",
                "Playgroup write acceptance is unknown; inspect provider state before retrying.",
                exception);
    }

    /// <summary>Creates the production HTTP client with a fixed origin and honest user agent.</summary>
    private static HttpClient CreateHttpClient(string packageVersion)
    {
        string version = PlaygroupContract.Required(packageVersion, nameof(packageVersion), 100);
        HttpClient client = new()
        {
            BaseAddress = new Uri("https://playgroup.gg/api/public/v1/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("mtg-mcp", version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
