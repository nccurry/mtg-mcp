using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MtgMcp.Core;
using static MtgMcp.Core.MtgMcpJson;

namespace MtgMcp.Playgroup;

/// <summary>
/// Sends Playgroup.gg public API requests and maps responses to Core models.
/// </summary>
public sealed partial class PlaygroupGateway
{
    /// <summary>
    /// Sends an authenticated or optionally authenticated GET request and parses JSON.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(
        string uri,
        bool requiresAuthentication,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        ApplyAuthentication(request, requiresAuthentication);

        using HttpResponseMessage response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        string responseBody = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return string.IsNullOrWhiteSpace(responseBody)
                ? JsonDocument.Parse("{}")
                : JsonDocument.Parse(responseBody);
        }

        throw CreateRequestException(response, responseBody);
    }

    /// <summary>
    /// Adds bearer authentication when available and enforces required credentials.
    /// </summary>
    private void ApplyAuthentication(HttpRequestMessage request, bool required)
    {
        PlaygroupCredentials loaded = LoadCredentials();
        if (!string.IsNullOrWhiteSpace(loaded.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                options.AuthScheme,
                loaded.ApiKey
            );
            return;
        }

        if (!required)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(credentialsFileError))
        {
            throw new InvalidOperationException(credentialsFileError);
        }

        throw new InvalidOperationException(
            "Playgroup API key credentials are required for this operation."
        );
    }

    /// <summary>
    /// Creates a sanitized Playgroup HTTP request exception.
    /// </summary>
    private static HttpRequestException CreateRequestException(
        HttpResponseMessage response,
        string responseBody
    )
    {
        return new HttpRequestException(
            $"Playgroup request failed with {(int)response.StatusCode}: {SecretRedactor.Redact(responseBody)}"
        );
    }

    /// <summary>
    /// Reads an ISO timestamp JSON property.
    /// </summary>
    private static DateTimeOffset? GetDate(JsonElement element, string propertyName)
    {
        string? value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed
        )
            ? parsed
            : null;
    }

    /// <summary>
    /// Escapes a numeric path or query value using invariant formatting.
    /// </summary>
    private static string Escape(long value)
    {
        return Uri.EscapeDataString(value.ToString(CultureInfo.InvariantCulture));
    }

}
