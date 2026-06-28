using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MtgMcp.Core;

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
    /// Enumerates common array response envelopes.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateCollection(JsonElement root)
    {
        JsonElement collection;
        if (root.ValueKind == JsonValueKind.Array)
        {
            collection = root;
        }
        else if (
            root.TryGetProperty("results", out JsonElement results)
            && results.ValueKind == JsonValueKind.Array
        )
        {
            collection = results;
        }
        else if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
        {
            collection = data;
        }
        else
        {
            yield break;
        }

        foreach (JsonElement item in collection.EnumerateArray())
        {
            yield return item;
        }
    }

    /// <summary>
    /// Reads a string-like JSON property.
    /// </summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    /// <summary>
    /// Reads an integer JSON property as a long.
    /// </summary>
    private static long? GetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long value))
        {
            return value;
        }

        return
            property.ValueKind == JsonValueKind.String
            && long.TryParse(
                property.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Reads an integer JSON property within Int32 bounds.
    /// </summary>
    private static int? GetInt(JsonElement element, string propertyName)
    {
        long? value = GetLong(element, propertyName);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    /// <summary>
    /// Reads a numeric JSON property as a double.
    /// </summary>
    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double value))
        {
            return value;
        }

        return
            property.ValueKind == JsonValueKind.String
            && double.TryParse(
                property.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Reads a boolean JSON property.
    /// </summary>
    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out bool value) => value,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an array of string-like JSON values.
    /// </summary>
    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
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
