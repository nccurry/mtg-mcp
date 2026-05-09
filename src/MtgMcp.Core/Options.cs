using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Configures mtg mcp options settings.
/// </summary>
public sealed class MtgMcpOptions
{
    /// <summary>
    /// Gets or sets the data dir.
    /// </summary>
    public string DataDir { get; set; } = DefaultDataDir();

    /// <summary>
    /// Gets or sets the operation mode.
    /// </summary>
    public string OperationMode { get; set; } = "apply";

    /// <summary>
    /// Handles default data dir.
    /// </summary>
    private static string DefaultDataDir()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".mtg-mcp"
            );
        }

        return Path.Combine(root, "mtg-mcp");
    }
}

/// <summary>
/// Provides secret redactor behavior.
/// </summary>
public static class SecretRedactor
{
    /// <summary>
    /// Stores the secret names.
    /// </summary>
    private static readonly string[] SecretNames =
    [
        "jwt",
        "token",
        "refresh",
        "password",
        "secret",
        "authorization",
        "cookie",
    ];

    /// <summary>
    /// Handles redact.
    /// </summary>
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

    /// <summary>
    /// Handles redact.
    /// </summary>
    public static Dictionary<string, object?> Redact(IDictionary<string, object?> values)
    {
        Dictionary<string, object?> redacted = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, object?> pair in values)
        {
            redacted[pair.Key] = IsSecretKey(pair.Key) ? "***REDACTED***" : pair.Value;
        }

        return redacted;
    }

    /// <summary>
    /// Handles redact.
    /// </summary>
    public static JsonDocument Redact(JsonDocument document)
    {
        object? value = RedactElement(document.RootElement, key: null);
        string json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Handles redact element.
    /// </summary>
    private static object? RedactElement(JsonElement element, string? key)
    {
        if (key is not null && IsSecretKey(key))
        {
            return "***REDACTED***";
        }

        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactObject(element),
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(item => RedactElement(item, key: null))
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long longValue)
                ? longValue
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
    }

    /// <summary>
    /// Handles redact object.
    /// </summary>
    private static Dictionary<string, object?> RedactObject(JsonElement element)
    {
        Dictionary<string, object?> redacted = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            redacted[property.Name] = RedactElement(property.Value, property.Name);
        }

        return redacted;
    }

    /// <summary>
    /// Determines whether secret key.
    /// </summary>
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
