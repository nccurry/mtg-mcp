using System.Net.Http.Json;
using System.Text.Json;
using MtgMcp.Core;
using static MtgMcp.Core.MtgMcpJson;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Limits retries for Archidekt write responses that report transient log creation failures.
    /// </summary>
    private const int MaxTransientWriteRetries = 5;

    /// <summary>
    /// Limits retries for Archidekt rate-limit responses.
    /// </summary>
    private const int MaxRateLimitRetries = 5;

    /// <summary>
    /// Spaces retries for Archidekt write responses that fail before committing a change log.
    /// </summary>
    private static readonly TimeSpan TransientWriteRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Sends an authenticated GET request and parses a JSON response.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        bool refreshedAfterUnauthorized = false;
        for (int attempt = 0; attempt <= MaxRateLimitRetries; attempt++)
        {
            await WaitForConfiguredRateLimitAsync(cancellationToken).ConfigureAwait(false);
            using HttpResponseMessage response = await httpClient
                .GetAsync(uri, cancellationToken)
                .ConfigureAwait(false);
            string responseBody = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(responseBody)
                    ? JsonDocument.Parse("{}")
                    : JsonDocument.Parse(responseBody);
            }

            if (!refreshedAfterUnauthorized
                && IsUnauthorized(response)
                && await TryRefreshAuthenticationAsync(cancellationToken).ConfigureAwait(false))
            {
                refreshedAfterUnauthorized = true;
                continue;
            }

            if (IsRateLimited(response, responseBody) && attempt < MaxRateLimitRetries)
            {
                await DelayForRateLimitAsync(response, responseBody, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            throw CreateRequestException(response, responseBody);
        }

        throw new InvalidOperationException("Archidekt GET retry loop ended unexpectedly.");
    }

    /// <summary>
    /// Sends a JSON request with Archidekt authentication, throttling, and retry handling.
    /// </summary>
    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string uri,
        object payload,
        CancellationToken cancellationToken,
        bool authenticate = true
    )
    {
        if (authenticate)
        {
            await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        }

        bool refreshedAfterUnauthorized = false;
        for (int attempt = 0; attempt <= MaxTransientWriteRetries; attempt++)
        {
            using HttpRequestMessage request = new(method, uri)
            {
                Content = JsonContent.Create(payload, options: SerializerOptions),
            };

            await WaitForConfiguredRateLimitAsync(cancellationToken).ConfigureAwait(false);
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            string responseBody = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(responseBody)
                    ? JsonDocument.Parse("{}")
                    : JsonDocument.Parse(responseBody);
            }

            if (authenticate
                && !refreshedAfterUnauthorized
                && IsUnauthorized(response)
                && await TryRefreshAuthenticationAsync(cancellationToken).ConfigureAwait(false))
            {
                refreshedAfterUnauthorized = true;
                continue;
            }

            if (IsRateLimited(response, responseBody) && attempt < MaxTransientWriteRetries)
            {
                await DelayForRateLimitAsync(response, responseBody, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (IsTransientWriteFailure(responseBody) && attempt < MaxTransientWriteRetries)
            {
                await Task.Delay(TransientWriteRetryDelay, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            throw CreateRequestException(response, responseBody);
        }

        throw new InvalidOperationException("Archidekt request retry loop ended unexpectedly.");
    }

    /// <summary>
    /// Sends a request without a JSON body and applies Archidekt retry behavior.
    /// </summary>
    private async Task SendAsync(
        HttpMethod method,
        string uri,
        CancellationToken cancellationToken
    )
    {
        bool refreshedAfterUnauthorized = false;
        for (int attempt = 0; attempt <= MaxRateLimitRetries; attempt++)
        {
            using HttpRequestMessage request = new(method, uri);
            await WaitForConfiguredRateLimitAsync(cancellationToken).ConfigureAwait(false);
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            string responseBody = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (!refreshedAfterUnauthorized
                && IsUnauthorized(response)
                && await TryRefreshAuthenticationAsync(cancellationToken).ConfigureAwait(false))
            {
                refreshedAfterUnauthorized = true;
                continue;
            }

            if (IsRateLimited(response, responseBody) && attempt < MaxRateLimitRetries)
            {
                await DelayForRateLimitAsync(response, responseBody, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            throw CreateRequestException(response, responseBody);
        }

        throw new InvalidOperationException("Archidekt request retry loop ended unexpectedly.");
    }

    /// <summary>
    /// Waits until the configured Archidekt request budget has room.
    /// </summary>
    private async Task WaitForConfiguredRateLimitAsync(CancellationToken cancellationToken)
    {
        ArchidektRateLimitOptions rateLimit = options.RateLimit ?? new ArchidektRateLimitOptions();
        int maxRequests = rateLimit.MaxRequests;
        if (maxRequests <= 0)
        {
            return;
        }

        TimeSpan window = TimeSpan.FromSeconds(Math.Max(1, rateLimit.WindowSeconds));
        await requestPacer.WaitForSlidingWindowAsync(maxRequests, window, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a sanitized Archidekt HTTP exception.
    /// </summary>
    private static HttpRequestException CreateRequestException(
        HttpResponseMessage response,
        string responseBody
    )
    {
        return new HttpRequestException(
            $"Archidekt request failed with {(int)response.StatusCode}: {SecretRedactor.Redact(responseBody)}"
        );
    }

    /// <summary>
    /// Detects Archidekt's transient write-log failure response.
    /// </summary>
    private static bool IsTransientWriteFailure(string responseBody)
    {
        return responseBody.Contains(
            "failed to create a log",
            StringComparison.OrdinalIgnoreCase
        );
    }

    /// <summary>
    /// Determines whether Archidekt asked the client to retry later.
    /// </summary>
    private static bool IsRateLimited(HttpResponseMessage response, string responseBody)
    {
        return response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            || responseBody.Contains("request was throttled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether Archidekt rejected the current authentication token.
    /// </summary>
    private static bool IsUnauthorized(HttpResponseMessage response)
    {
        return response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
    }

    /// <summary>
    /// Waits for Archidekt's advertised throttle window before retrying.
    /// </summary>
    private static Task DelayForRateLimitAsync(
        HttpResponseMessage response,
        string responseBody,
        CancellationToken cancellationToken
    )
    {
        TimeSpan delay = MtgMcpHttpRetry.GetRetryDelay(
            response,
            responseBody,
            "available in ",
            TimeSpan.FromSeconds(5));
        return Task.Delay(delay, cancellationToken);
    }

    /// <summary>
    /// Returns the bound Archidekt deck id or fails before a remote write.
    /// </summary>
    private static string RequireDeckId(DeckWorkspace workspace)
    {
        return workspace.ArchidektDeckId
            ?? throw new InvalidOperationException("Workspace is not bound to an Archidekt deck.");
    }

    /// <summary>
    /// Parses the int or string.
    /// </summary>
    private static object? ParseIntOrString(string? value)
    {
        return int.TryParse(
            value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int number
        )
            ? number
            : value;
    }

    /// <summary>
    /// Parses an optional date value from Archidekt JSON text.
    /// </summary>
    private static DateTimeOffset? TryDate(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            out DateTimeOffset date
        )
            ? date
            : null;
    }

}
