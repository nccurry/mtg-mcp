using System.Globalization;
using System.Net;

namespace MtgMcp.Core;

/// <summary>
/// Stores a text HTTP response after shared retry and error handling has completed.
/// </summary>
public readonly struct MtgMcpHttpTextResponse
{
    /// <summary>
    /// Creates a text HTTP response snapshot.
    /// </summary>
    public MtgMcpHttpTextResponse(HttpStatusCode statusCode, string body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    /// <summary>
    /// Gets the final response status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Gets the response body that was read from the final response.
    /// </summary>
    public string Body { get; }
}

/// <summary>
/// Provides shared HTTP retry timing helpers for external service adapters.
/// </summary>
public static class MtgMcpHttpRetry
{
    /// <summary>
    /// Limits response-body text included in adapter failure messages.
    /// </summary>
    private const int MaxFailureBodyLength = 2048;

    /// <summary>
    /// Sends a request, retries transient response statuses, and returns the final text body.
    /// </summary>
    public static async Task<MtgMcpHttpTextResponse> SendForStringAsync(
        HttpClient httpClient,
        Func<HttpRequestMessage> createRequest,
        string serviceName,
        int maxRetries,
        TimeSpan fallbackDelay,
        CancellationToken cancellationToken,
        params HttpStatusCode[] allowedNonSuccessStatusCodes)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(createRequest);

        int safeMaxRetries = Math.Max(0, maxRetries);
        TimeSpan safeFallbackDelay = NormalizeDelay(fallbackDelay);
        for (int attempt = 0; attempt <= safeMaxRetries; attempt++)
        {
            using HttpRequestMessage request = createRequest();
            using HttpResponseMessage response = await httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            string responseBody = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode
                || IsAllowedNonSuccessStatus(response.StatusCode, allowedNonSuccessStatusCodes))
            {
                return new MtgMcpHttpTextResponse(response.StatusCode, responseBody);
            }

            if (IsTransientStatus(response.StatusCode) && attempt < safeMaxRetries)
            {
                TimeSpan delay = GetRetryAfterDelay(response) ?? safeFallbackDelay;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw CreateRequestException(serviceName, response, responseBody);
        }

        throw new InvalidOperationException($"{serviceName} request retry loop ended unexpectedly.");
    }

    /// <summary>
    /// Creates a redacted HTTP request exception for a provider response.
    /// </summary>
    public static HttpRequestException CreateRequestException(
        string serviceName,
        HttpResponseMessage response,
        string? responseBody,
        string? diagnosticHint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentNullException.ThrowIfNull(response);

        string safeBody = SummarizeFailureBody(responseBody);
        string message = $"{serviceName} request failed with {(int)response.StatusCode}";
        if (!string.IsNullOrWhiteSpace(diagnosticHint))
        {
            message += $". {diagnosticHint.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(safeBody))
        {
            message += $": {safeBody}";
        }

        return new HttpRequestException(
            message,
            inner: null,
            response.StatusCode);
    }

    /// <summary>
    /// Resolves retry delay from Retry-After headers, a body marker, or a fallback.
    /// </summary>
    public static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        string? responseBody,
        string bodyMarker,
        TimeSpan fallback)
    {
        ArgumentNullException.ThrowIfNull(response);

        return GetRetryAfterDelay(response)
            ?? TryReadDelayAfterMarker(responseBody, bodyMarker)
            ?? NormalizeDelay(fallback);
    }

    /// <summary>
    /// Reads retry delay from the standard Retry-After response header.
    /// </summary>
    public static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return NormalizeDelay(delta);
        }

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            return NormalizeDelay(date - DateTimeOffset.UtcNow);
        }

        return null;
    }

    /// <summary>
    /// Reads a second count immediately after a provider-specific body marker.
    /// </summary>
    public static TimeSpan? TryReadDelayAfterMarker(string? responseBody, string bodyMarker)
    {
        if (string.IsNullOrWhiteSpace(responseBody) || string.IsNullOrEmpty(bodyMarker))
        {
            return null;
        }

        int markerIndex = responseBody.IndexOf(bodyMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        int start = markerIndex + bodyMarker.Length;
        int end = start;
        while (end < responseBody.Length && char.IsDigit(responseBody[end]))
        {
            end++;
        }

        if (end <= start
            || !int.TryParse(
                responseBody.AsSpan(start, end - start),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int seconds))
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    /// <summary>
    /// Coerces negative retry delays to zero.
    /// </summary>
    public static TimeSpan NormalizeDelay(TimeSpan delay)
    {
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    /// <summary>
    /// Gets whether callers explicitly want a non-success status returned.
    /// </summary>
    private static bool IsAllowedNonSuccessStatus(
        HttpStatusCode statusCode,
        HttpStatusCode[] allowedNonSuccessStatusCodes)
    {
        foreach (HttpStatusCode allowedStatusCode in allowedNonSuccessStatusCodes)
        {
            if (statusCode == allowedStatusCode)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Redacts and bounds provider response bodies before they enter exceptions.
    /// </summary>
    private static string SummarizeFailureBody(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "";
        }

        string safeBody = SecretRedactor.Redact(responseBody);
        return safeBody.Length <= MaxFailureBodyLength
            ? safeBody
            : string.Concat(safeBody.AsSpan(0, MaxFailureBodyLength), "...");
    }

    /// <summary>
    /// Gets whether a response status is safe to retry for idempotent read-style adapter requests.
    /// </summary>
    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        int status = (int)statusCode;
        return statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            || status is >= 500 and <= 599;
    }
}
