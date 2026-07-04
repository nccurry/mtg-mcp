using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Scryfall;

/// <summary>
/// Performs bounded official Scryfall HTTP reads with shared pacing, headers, retry limits, and host validation.
/// </summary>
internal sealed class ScryfallProviderClient : IDisposable
{
    /// <summary>
    /// Keeps provider request starts conservatively below the published ceiling.
    /// </summary>
    private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Stores the official or explicitly configured API origin.
    /// </summary>
    private readonly Uri apiBaseUri;

    /// <summary>
    /// Owns provider HTTP connections.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Coordinates pacing through the shared database.
    /// </summary>
    private readonly ScryfallDatabase database;

    /// <summary>
    /// Supplies deterministic time in tests.
    /// </summary>
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Creates a provider client with explicit configuration and optional fake transport.
    /// </summary>
    internal ScryfallProviderClient(
        Uri apiBaseUri,
        string userAgent,
        ScryfallDatabase database,
        TimeProvider timeProvider,
        HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!apiBaseUri.IsAbsoluteUri ||
            (apiBaseUri.Scheme != Uri.UriSchemeHttps && apiBaseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Scryfall API base URI must be absolute HTTP or HTTPS.", nameof(apiBaseUri));
        }

        this.apiBaseUri = EnsureTrailingSlash(apiBaseUri);
        this.database = database;
        this.timeProvider = timeProvider;
        httpClient = handler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true)
            : new HttpClient(handler, disposeHandler: true);
        httpClient.BaseAddress = this.apiBaseUri;
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent.Trim());
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 0.9));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
        httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Acquires one complete paginated provider response.
    /// </summary>
    internal async Task<ProviderAcquisition> GetPagedAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        Uri? current = ResolveApiUri(relativePath);
        List<string> pages = [];
        List<string> members = [];
        List<string> warnings = [];
        while (current is not null)
        {
            Uri pageUri = current;
            using HttpResponseMessage response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pageUri),
                cancellationToken).ConfigureAwait(false);
            string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            pages.Add(raw);
            using JsonDocument document = JsonDocument.Parse(raw);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            {
                throw new ScryfallProviderException("invalid-provider-payload", "Scryfall returned an invalid paginated response.");
            }

            foreach (JsonElement item in data.EnumerateArray())
            {
                members.Add(item.GetRawText());
            }

            AddWarnings(root, warnings);

            bool hasMore = root.TryGetProperty("has_more", out JsonElement hasMoreValue) &&
                hasMoreValue.ValueKind == JsonValueKind.True;
            if (!hasMore)
            {
                break;
            }

            string nextPage = ScryfallMapper.RequiredString(root, "next_page");
            Uri nextUri = new(nextPage, UriKind.Absolute);
            if (!SameOrigin(nextUri, apiBaseUri))
            {
                throw new ScryfallProviderException("unexpected-provider-host", "Scryfall pagination changed to an unexpected host.");
            }

            current = nextUri;
        }

        return new ProviderAcquisition(pages, members, warnings);
    }

    /// <summary>
    /// Acquires one non-paginated JSON object as a single snapshot member.
    /// </summary>
    internal async Task<ProviderAcquisition> GetSingleAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, ResolveApiUri(relativePath)),
            cancellationToken).ConfigureAwait(false);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(raw);
        return new ProviderAcquisition([raw], [document.RootElement.GetRawText()], []);
    }

    /// <summary>
    /// Acquires one non-paginated object containing an ordered data array.
    /// </summary>
    internal async Task<ProviderAcquisition> GetDataArrayAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, ResolveApiUri(relativePath)),
            cancellationToken).ConfigureAwait(false);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(raw);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new ScryfallProviderException("invalid-provider-payload", "Scryfall returned an invalid data response.");
        }

        List<string> members = [];
        foreach (JsonElement item in data.EnumerateArray())
        {
            members.Add(item.GetRawText());
        }

        return new ProviderAcquisition([raw], members, []);
    }

    /// <summary>
    /// Acquires an ordered collection request while retaining the complete provider response page.
    /// </summary>
    internal async Task<ProviderAcquisition> PostCollectionAsync(
        string requestJson,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, ResolveApiUri("cards/collection"))
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
            },
            cancellationToken).ConfigureAwait(false);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(raw);
        if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new ScryfallProviderException("invalid-provider-payload", "Scryfall returned an invalid collection response.");
        }

        List<string> members = [];
        foreach (JsonElement item in data.EnumerateArray())
        {
            members.Add(item.GetRawText());
        }

        return new ProviderAcquisition([raw], members, []);
    }

    /// <summary>
    /// Opens one official compressed JSONL download after validating its origin.
    /// </summary>
    internal async Task<ProviderDownload> OpenDownloadAsync(
        string downloadUri,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(downloadUri, UriKind.Absolute, out Uri? uri) || !AllowedDownloadOrigin(uri))
        {
            throw new ScryfallProviderException("unexpected-provider-host", "Scryfall bulk metadata named an unexpected download host.");
        }

        HttpResponseMessage response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);
        Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new ProviderDownload(response, stream);
    }

    /// <summary>
    /// Releases owned HTTP resources.
    /// </summary>
    public void Dispose()
    {
        httpClient.Dispose();
    }

    /// <summary>
    /// Sends one paced request with bounded transient retries and fail-fast blocking behavior.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            TimeSpan delay = await database.ReserveProviderStartAsync(
                timeProvider.GetUtcNow(),
                RequestInterval,
                cancellationToken).ConfigureAwait(false);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            using HttpRequestMessage request = requestFactory();
            try
            {
                HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                HttpStatusCode status = response.StatusCode;
                response.Dispose();
                if (status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    throw new ScryfallProviderException(
                        "scryfall-access-blocked",
                        "Scryfall rejected the request; acquisition stopped without retry.");
                }

                if ((int)status >= 500 && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                string reason = status == HttpStatusCode.NotFound
                    ? "scryfall-not-found"
                    : status == HttpStatusCode.BadRequest
                        ? "invalid-scryfall-query"
                        : "scryfall-request-failed";
                throw new ScryfallProviderException(reason, "Scryfall could not satisfy the request.");
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                throw new ScryfallProviderException(
                    "scryfall-unavailable",
                    "Scryfall is temporarily unavailable.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ScryfallProviderException(
                    "scryfall-unavailable",
                    "Scryfall is temporarily unavailable.");
            }
        }

        throw new ScryfallProviderException("scryfall-unavailable", "Scryfall is temporarily unavailable.");
    }

    /// <summary>
    /// Resolves one provider-relative path while preventing origin replacement.
    /// </summary>
    private Uri ResolveApiUri(string relativePath)
    {
        if (!Uri.TryCreate(apiBaseUri, relativePath, out Uri? uri) || !SameOrigin(uri, apiBaseUri))
        {
            throw new ScryfallProviderException("unexpected-provider-host", "Scryfall request path changed to an unexpected host.");
        }

        return uri;
    }

    /// <summary>
    /// Allows official data hosting and same-origin fixture servers only.
    /// </summary>
    private bool AllowedDownloadOrigin(Uri uri)
    {
        return SameOrigin(uri, apiBaseUri) ||
            (string.Equals(apiBaseUri.Host, "api.scryfall.com", StringComparison.OrdinalIgnoreCase) &&
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
             string.Equals(uri.Host, "data.scryfall.io", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Compares scheme, host, and effective port for redirect and pagination safety.
    /// </summary>
    private static bool SameOrigin(Uri left, Uri right)
    {
        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
            left.Port == right.Port;
    }

    /// <summary>
    /// Copies provider warning strings without retaining malformed values.
    /// </summary>
    private static void AddWarnings(JsonElement root, ICollection<string> warnings)
    {
        if (!root.TryGetProperty("warnings", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement warning in values.EnumerateArray())
        {
            if (warning.ValueKind == JsonValueKind.String && warning.GetString() is string text)
            {
                warnings.Add(text);
            }
        }
    }

    /// <summary>
    /// Normalizes an API base path so relative endpoint resolution is stable.
    /// </summary>
    private static Uri EnsureTrailingSlash(Uri value)
    {
        string text = value.AbsoluteUri;
        return text.EndsWith('/', StringComparison.Ordinal) ? value : new Uri(text + '/', UriKind.Absolute);
    }
}

/// <summary>
/// Carries complete raw provider pages and ordered result members before persistence.
/// </summary>
internal sealed record ProviderAcquisition(
    IReadOnlyList<string> Pages,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Owns one provider download response and its streaming body.
/// </summary>
internal sealed class ProviderDownload : IAsyncDisposable
{
    /// <summary>
    /// Stores the response disposed with the streaming body.
    /// </summary>
    private readonly HttpResponseMessage response;

    /// <summary>
    /// Creates one owned provider download.
    /// </summary>
    internal ProviderDownload(HttpResponseMessage response, Stream stream)
    {
        this.response = response;
        Stream = stream;
    }

    /// <summary>
    /// Gets the response body stream.
    /// </summary>
    internal Stream Stream { get; }

    /// <summary>
    /// Releases the response body and response message.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync().ConfigureAwait(false);
        response.Dispose();
    }
}

/// <summary>
/// Reports one sanitized provider failure with a stable machine-readable reason.
/// </summary>
internal sealed class ScryfallProviderException : Exception
{
    /// <summary>
    /// Creates one sanitized provider failure.
    /// </summary>
    internal ScryfallProviderException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    /// <summary>
    /// Gets the stable failure reason.
    /// </summary>
    internal string ReasonCode { get; }
}
