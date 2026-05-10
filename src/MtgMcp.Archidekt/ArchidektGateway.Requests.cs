using System.Net.Http.Json;
using System.Text.Json;
using MtgMcp.Core;

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
    /// Spaces retries for Archidekt write responses that fail before committing a change log.
    /// </summary>
    private static readonly TimeSpan TransientWriteRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the json.
    /// </summary>
    private async Task<JsonDocument> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient
            .GetAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using Stream stream = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Handles send json.
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

        for (int attempt = 0; attempt <= MaxTransientWriteRetries; attempt++)
        {
            using HttpRequestMessage request = new(method, uri)
            {
                Content = JsonContent.Create(payload, options: SerializerOptions),
            };

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

            if (IsTransientWriteFailure(responseBody) && attempt < MaxTransientWriteRetries)
            {
                await Task.Delay(TransientWriteRetryDelay, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            throw new HttpRequestException(
                $"Archidekt request failed with {(int)response.StatusCode}: {SecretRedactor.Redact(responseBody)}"
            );
        }

        throw new InvalidOperationException("Archidekt request retry loop ended unexpectedly.");
    }

    /// <summary>
    /// Ensures the success.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response
            .Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        throw new HttpRequestException(
            $"Archidekt request failed with {(int)response.StatusCode}: {SecretRedactor.Redact(body)}"
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
    /// Handles enumerate collection.
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
        else if (
            root.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.Array
        )
        {
            collection = data;
        }
        else if (root.TryGetProperty("decks", out JsonElement decks) && decks.ValueKind == JsonValueKind.Array)
        {
            collection = decks;
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
    /// Handles require deck id.
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
    /// Gets the string.
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
    /// Gets the nested string.
    /// </summary>
    private static string? GetNestedString(
        JsonElement element,
        string propertyName,
        string nestedPropertyName
    )
    {
        return
            element.TryGetProperty(propertyName, out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, nestedPropertyName)
            : null;
    }

    /// <summary>
    /// Gets the int.
    /// </summary>
    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
        {
            return value;
        }

        return
            property.ValueKind == JsonValueKind.String
            && int.TryParse(
                property.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Gets the long.
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
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Gets the nested int.
    /// </summary>
    private static int? GetNestedInt(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? GetInt(nested, nestedPropertyName)
            : null;
    }

    /// <summary>
    /// Gets the nested long.
    /// </summary>
    private static long? GetNestedLong(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? GetLong(nested, nestedPropertyName)
            : null;
    }

    /// <summary>
    /// Gets the double.
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
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value
            )
            ? value
            : null;
    }

    /// <summary>
    /// Gets the nested double.
    /// </summary>
    private static double? GetNestedDouble(
        JsonElement element,
        string propertyName,
        string nestedPropertyName
    )
    {
        return
            element.TryGetProperty(propertyName, out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object
            ? GetDouble(nested, nestedPropertyName)
            : null;
    }

    /// <summary>
    /// Gets the bool.
    /// </summary>
    private static bool GetBool(JsonElement element, string propertyName, bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return defaultValue;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out bool value) => value,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Handles try date.
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

    /// <summary>
    /// Handles first non empty.
    /// </summary>
    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
