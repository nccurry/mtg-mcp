using System.Net;
using System.Text;
using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Contains HTTP request, throttling, and response-cache helpers for ScryfallClient.
/// </summary>
public sealed partial class ScryfallClient
{
    /// <summary>
    /// Reads a JSON document from a cached or live GET request.
    /// </summary>
    private async Task<JsonDocument?> GetJsonAsync(
        string relativeUri,
        CancellationToken cancellationToken,
        bool returnNullOnNotFound = false
    )
    {
        TimeSpan cacheTtl = GetCacheTtl(relativeUri);
        string? cachedBody = await GetCachedResponseBodyAsync(
                HttpMethod.Get.Method,
                relativeUri,
                requestBody: null,
                cacheTtl,
                cancellationToken)
            .ConfigureAwait(false);
        if (cachedBody is not null)
        {
            return JsonDocument.Parse(cachedBody);
        }

        int maxRetries = Math.Max(0, options.MaxRateLimitRetries);
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            await DelayIfNeededAsync(cancellationToken).ConfigureAwait(false);

            using HttpResponseMessage response = await httpClient
                .GetAsync(relativeUri, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                await DelayForRateLimitAsync(response, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (returnNullOnNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Scryfall request failed with {(int)response.StatusCode}: {errorBody}"
                );
            }

            string successBody = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            await SetCachedResponseBodyAsync(
                    HttpMethod.Get.Method,
                    relativeUri,
                    requestBody: null,
                    successBody,
                    cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(successBody);
        }

        throw new HttpRequestException("Scryfall request failed after rate limit retry.");
    }

    /// <summary>
    /// Posts the json.
    /// </summary>
    private async Task<JsonDocument?> PostJsonAsync(
        string relativeUri,
        object body,
        CancellationToken cancellationToken
    )
    {
        string json = JsonSerializer.Serialize(body, SerializerOptions);
        TimeSpan cacheTtl = GetCacheTtl(relativeUri);
        string? cachedBody = await GetCachedResponseBodyAsync(
                HttpMethod.Post.Method,
                relativeUri,
                json,
                cacheTtl,
                cancellationToken)
            .ConfigureAwait(false);
        if (cachedBody is not null)
        {
            return JsonDocument.Parse(cachedBody);
        }

        int maxRetries = Math.Max(0, options.MaxRateLimitRetries);
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            await DelayIfNeededAsync(cancellationToken).ConfigureAwait(false);

            using StringContent content = new(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await httpClient
                .PostAsync(relativeUri, content, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                await DelayForRateLimitAsync(response, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Scryfall request failed with {(int)response.StatusCode}: {errorBody}"
                );
            }

            string successBody = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            await SetCachedResponseBodyAsync(
                    HttpMethod.Post.Method,
                    relativeUri,
                    json,
                    successBody,
                    cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(successBody);
        }

        throw new HttpRequestException("Scryfall request failed after rate limit retry.");
    }

    /// <summary>
    /// Delays according to Scryfall rate limit guidance.
    /// </summary>
    private static async Task DelayForRateLimitAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        TimeSpan? delay = MtgMcpHttpRetry.GetRetryAfterDelay(response);
        if (delay is null)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            delay = MtgMcpHttpRetry.TryReadDelayAfterMarker(body, "after ");
        }

        await Task.Delay(delay ?? DefaultRateLimitDelay, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a cached raw Scryfall JSON body.
    /// </summary>
    private async Task<string?> GetCachedResponseBodyAsync(
        string method,
        string relativeUri,
        string? requestBody,
        TimeSpan timeToLive,
        CancellationToken cancellationToken)
    {
        if (CacheBypassDepth.Value > 0)
        {
            return null;
        }

        return await cache
            .GetAsync<string>(
                CreateCacheKey(method, relativeUri, requestBody),
                timeToLive,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Stores a raw Scryfall JSON response body as a reusable source fact.
    /// </summary>
    private async Task SetCachedResponseBodyAsync(
        string method,
        string relativeUri,
        string? requestBody,
        string responseBody,
        CancellationToken cancellationToken)
    {
        await cache
            .SetAsync(
                CreateCacheKey(method, relativeUri, requestBody),
                responseBody,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a stable cache key for a Scryfall API call.
    /// </summary>
    private static CorpusCacheKey CreateCacheKey(
        string method,
        string relativeUri,
        string? requestBody)
    {
        return new CorpusCacheKey
        {
            Source = "scryfall",
            Endpoint = method.ToUpperInvariant(),
            Query = $"{relativeUri.Trim()}|{requestBody ?? ""}",
            AdapterVersion = CacheAdapterVersion,
            Budget = "source-fact"
        };
    }

    /// <summary>
    /// Gets the configured TTL for a Scryfall API call.
    /// </summary>
    private TimeSpan GetCacheTtl(string relativeUri)
    {
        bool isSearch = relativeUri.Contains("cards/search", StringComparison.OrdinalIgnoreCase);
        return CorpusCacheFactory.ParseDuration(
            isSearch
                ? mtgOptions.Intelligence.Cache.Ttls.ScryfallSearch
                : mtgOptions.Intelligence.Cache.Ttls.ScryfallCardMetadata,
            isSearch ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7));
    }

    /// <summary>
    /// Applies process-wide Scryfall pacing before an outbound request.
    /// </summary>
    private async Task DelayIfNeededAsync(CancellationToken cancellationToken)
    {
        await RequestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - lastRequestAt;
            if (elapsed < options.MinimumDelay)
            {
                await Task.Delay(options.MinimumDelay - elapsed, cancellationToken)
                    .ConfigureAwait(false);
            }

            lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            RequestLock.Release();
        }
    }

    /// <summary>
    /// Releases resources held by the instance.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Opens a scope where Scryfall response cache reads are bypassed.
    /// </summary>
    IDisposable IScryfallCacheBypass.BypassCache()
    {
        CacheBypassDepth.Value++;
        return new CacheBypassScope();
    }

    /// <summary>
    /// Restores the previous cache-bypass depth when a refresh call completes.
    /// </summary>
    private sealed class CacheBypassScope : IDisposable
    {
        /// <summary>
        /// Closes this cache-bypass scope.
        /// </summary>
        public void Dispose()
        {
            CacheBypassDepth.Value = Math.Max(0, CacheBypassDepth.Value - 1);
        }
    }

}
