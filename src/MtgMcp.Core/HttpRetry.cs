using System.Globalization;

namespace MtgMcp.Core;

/// <summary>
/// Provides shared HTTP retry timing helpers for external service adapters.
/// </summary>
public static class MtgMcpHttpRetry
{
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
}
