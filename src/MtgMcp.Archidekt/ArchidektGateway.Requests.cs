using System.Net.Http.Json;
using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

public sealed partial class ArchidektGateway
{
    private async Task<JsonDocument> GetJsonAsync(string uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string uri,
        object payload,
        CancellationToken cancellationToken,
        bool authenticate = true)
    {
        if (authenticate)
        {
            await EnsureAuthenticatedAsync(required: true, cancellationToken).ConfigureAwait(false);
        }

        using HttpRequestMessage request = new(method, uri)
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(responseBody);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException($"Archidekt request failed with {(int)response.StatusCode}: {SecretRedactor.Redact(body)}");
    }

    private static IEnumerable<JsonElement> EnumerateCollection(JsonElement root)
    {
        JsonElement collection;
        if (root.ValueKind == JsonValueKind.Array)
        {
            collection = root;
        }
        else if (root.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
        {
            collection = results;
        }
        else if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
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

    private static string RequireDeckId(DeckWorkspace workspace)
    {
        return workspace.ArchidektDeckId
            ?? throw new InvalidOperationException("Workspace is not bound to an Archidekt deck.");
    }

    private static object? ParseIntOrString(string? value)
    {
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number)
            ? number
            : value;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
    }

    private static string? GetNestedString(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, nestedPropertyName)
            : null;
    }

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

        return property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value)
                ? value
                : null;
    }

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

        return property.ValueKind == JsonValueKind.String
            && double.TryParse(property.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
                ? value
                : null;
    }

    private static double? GetNestedDouble(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? GetDouble(nested, nestedPropertyName)
            : null;
    }

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
            _ => defaultValue
        };
    }

    private static DateTimeOffset? TryDate(string? value)
    {
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out DateTimeOffset date)
            ? date
            : null;
    }

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
