using System.Text.Json;

namespace MtgMcp.Core;

public sealed class MtgMcpOptions
{
    public string DataDir { get; set; } = DefaultDataDir();
    public string OperationMode { get; set; } = "apply";

    private static string DefaultDataDir()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mtg-mcp");
        }

        return Path.Combine(root, "mtg-mcp");
    }
}

public static class SecretRedactor
{
    private static readonly string[] SecretNames =
    [
        "jwt",
        "token",
        "refresh",
        "password",
        "secret",
        "authorization",
        "cookie"
    ];

    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        foreach (string secretName in SecretNames)
        {
            if (value.Contains(secretName, StringComparison.OrdinalIgnoreCase))
            {
                return "***REDACTED***";
            }
        }

        return value;
    }

    public static Dictionary<string, object?> Redact(IDictionary<string, object?> values)
    {
        Dictionary<string, object?> redacted = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object?> pair in values)
        {
            redacted[pair.Key] = IsSecretKey(pair.Key) ? "***REDACTED***" : pair.Value;
        }

        return redacted;
    }

    public static JsonDocument Redact(JsonDocument document)
    {
        object? value = RedactElement(document.RootElement, key: null);
        string json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json);
    }

    private static object? RedactElement(JsonElement element, string? key)
    {
        if (key is not null && IsSecretKey(key))
        {
            return "***REDACTED***";
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(item => RedactElement(item, key: null)).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static Dictionary<string, object?> RedactObject(JsonElement element)
    {
        Dictionary<string, object?> redacted = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            redacted[property.Name] = RedactElement(property.Value, property.Name);
        }

        return redacted;
    }

    private static bool IsSecretKey(string key)
    {
        foreach (string secretName in SecretNames)
        {
            if (key.Contains(secretName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
