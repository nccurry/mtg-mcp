using System.Text.Json;
using System.Text.RegularExpressions;

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
    /// Selects the server safety mode; plan is the least-privilege default.
    /// </summary>
    public string OperationMode { get; set; } = "plan";

    /// <summary>
    /// Gets or sets comma-separated MCP toolsets to advertise; blank keeps the compatibility profile.
    /// </summary>
    public string Toolsets { get; set; } = "";

    /// <summary>
    /// Gets or sets intelligence and recommendation tuning options.
    /// </summary>
    public MtgMcpIntelligenceOptions Intelligence { get; set; } = new();

    /// <summary>
    /// Gets or sets deterministic simulation profile options.
    /// </summary>
    public MtgMcpSimulationOptions Simulation { get; set; } = new();

    /// <summary>
    /// Resolves the default persisted data directory for this user.
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
/// Configures deterministic simulation profile loading.
/// </summary>
public sealed class MtgMcpSimulationOptions
{
    /// <summary>
    /// Gets or sets optional JSON profile files or glob patterns.
    /// </summary>
    public List<string> ProfilePaths { get; set; } = [];

    /// <summary>
    /// Gets or sets whether external profiles can replace built-in profile ids.
    /// </summary>
    public bool AllowExternalProfileOverrides { get; set; } = true;
}

/// <summary>
/// Configures recommendation analysis depth and corpus usage.
/// </summary>
public sealed class MtgMcpIntelligenceOptions
{
    /// <summary>
    /// Gets or sets the default analysis depth for corpus-aware tools.
    /// </summary>
    public string AnalysisDepth { get; set; } = AnalysisDepths.Balanced;

    /// <summary>
    /// Gets or sets source-fact cache options.
    /// </summary>
    public MtgMcpCorpusCacheOptions Cache { get; set; } = new();

    /// <summary>
    /// Gets or sets corpus source options keyed by source name.
    /// </summary>
    public Dictionary<string, MtgMcpCorpusSourceOptions> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Configures shared source-fact caching for corpus providers.
/// </summary>
public sealed class MtgMcpCorpusCacheOptions
{
    /// <summary>
    /// Gets or sets the cache implementation mode.
    /// </summary>
    public string Mode { get; set; } = CorpusCacheModes.Persisted;

    /// <summary>
    /// Gets or sets the maximum persisted cache size in bytes.
    /// </summary>
    public long MaxBytes { get; set; } = 104_857_600;

    /// <summary>
    /// Gets or sets the maximum number of cache entries.
    /// </summary>
    public int MaxEntries { get; set; } = 5_000;

    /// <summary>
    /// Gets or sets per-source cache TTL values.
    /// </summary>
    public MtgMcpCorpusCacheTtlOptions Ttls { get; set; } = new();
}

/// <summary>
/// Configures source-specific cache TTLs using compact duration strings.
/// </summary>
public sealed class MtgMcpCorpusCacheTtlOptions
{
    /// <summary>
    /// Gets or sets Scryfall card metadata TTL.
    /// </summary>
    public string ScryfallCardMetadata { get; set; } = "7d";

    /// <summary>
    /// Gets or sets Scryfall search and rank TTL.
    /// </summary>
    public string ScryfallSearch { get; set; } = "24h";

    /// <summary>
    /// Gets or sets Commander Spellbook lookup TTL.
    /// </summary>
    public string CommanderSpellbook { get; set; } = "24h";

    /// <summary>
    /// Gets or sets deck search API TTL.
    /// </summary>
    public string DeckSearch { get; set; } = "6h";

    /// <summary>
    /// Gets or sets individual deck detail TTL.
    /// </summary>
    public string DeckDetails { get; set; } = "7d";

    /// <summary>
    /// Gets or sets normalized corpus signal report TTL.
    /// </summary>
    public string CorpusSignals { get; set; } = "6h";
}

/// <summary>
/// Configures one corpus source.
/// </summary>
public sealed class MtgMcpCorpusSourceOptions
{
    /// <summary>
    /// Gets or sets whether the source may be queried.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional source API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether unofficial structured APIs are allowed; null uses the source-specific default.
    /// </summary>
    public bool? AllowUnofficialApi { get; set; }

    /// <summary>
    /// Gets or sets an optional source base address override.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Gets or sets an optional User-Agent override for this corpus source.
    /// </summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// Lists corpus source-fact cache modes.
/// </summary>
public static class CorpusCacheModes
{
    /// <summary>
    /// Stores fresh source facts on disk.
    /// </summary>
    public const string Persisted = "persisted";

    /// <summary>
    /// Stores fresh source facts for the current process only.
    /// </summary>
    public const string Memory = "memory";

    /// <summary>
    /// Disables source-fact caching.
    /// </summary>
    public const string Off = "off";
}

/// <summary>
/// Lists analysis-depth profile names.
/// </summary>
public static class AnalysisDepths
{
    /// <summary>
    /// Uses compact high-signal analysis.
    /// </summary>
    public const string Minimal = "minimal";

    /// <summary>
    /// Uses the default balance of source breadth and compact output.
    /// </summary>
    public const string Balanced = "balanced";

    /// <summary>
    /// Uses the widest enabled source set and richer evidence.
    /// </summary>
    public const string Best = "best";
}

/// <summary>
/// Provides secret redactor behavior.
/// </summary>
public static class SecretRedactor
{
    /// <summary>
    /// Replaces authorization header values while preserving the auth scheme for diagnostics.
    /// </summary>
    private static readonly Regex AuthorizationTokenPattern = new(
        @"\b(?<scheme>Bearer|JWT)\s+(?<secret>[^\s""',;<>]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Replaces compact JWTs even when they appear without an authorization scheme.
    /// </summary>
    private static readonly Regex JwtPattern = new(
        @"(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}(?![A-Za-z0-9_-])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Replaces credentials embedded in absolute URLs while preserving the target host.
    /// </summary>
    private static readonly Regex UrlCredentialsPattern = new(
        @"(?<scheme>[A-Za-z][A-Za-z0-9+.-]*://)(?<userinfo>[^/\s@]+)@",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Replaces long base64/base64url-like strings that are likely bearer API tokens.
    /// </summary>
    private static readonly Regex HighEntropyTokenPattern = new(
        @"(?<![A-Za-z0-9+/=_-])(?=[A-Za-z0-9+/=_-]{32,})(?=[A-Za-z0-9+/=_-]*[A-Za-z])(?=[A-Za-z0-9+/=_-]*\d)[A-Za-z0-9+/=_-]{32,}(?![A-Za-z0-9+/=_-])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Stores the secret names.
    /// </summary>
    private static readonly string[] SecretNames =
    [
        "jwt",
        "token",
        "refresh",
        "password",
        "apikey",
        "api_key",
        "api-key",
        "secret",
        "authorization",
        "cookie",
    ];

    /// <summary>
    /// Redacts a raw value when it appears to contain a secret.
    /// </summary>
    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (TryRedactJson(value, out string? redactedJson))
        {
            return RedactStringContent(redactedJson!);
        }

        return RedactStringContent(value);
    }

    /// <summary>
    /// Redacts secret-keyed values from a dictionary.
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
    /// Redacts secret-keyed fields from a JSON document.
    /// </summary>
    public static JsonDocument Redact(JsonDocument document)
    {
        object? value = RedactElement(document.RootElement, key: null);
        string json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Redacts a JSON element while preserving non-secret structure.
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
            JsonValueKind.String => RedactStringContent(element.GetString() ?? ""),
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
    /// Redacts secret-keyed properties from a JSON object.
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
    /// Redacts secret-keyed JSON when a raw response body is passed as text.
    /// </summary>
    private static bool TryRedactJson(string value, out string? redactedJson)
    {
        redactedJson = null;
        string trimmed = value.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            object? redacted = RedactElement(document.RootElement, key: null);
            redactedJson = JsonSerializer.Serialize(redacted);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Redacts known secret shapes while preserving non-secret diagnostic text.
    /// </summary>
    private static string RedactStringContent(string value)
    {
        string redacted = AuthorizationTokenPattern.Replace(
            value,
            match => $"{match.Groups["scheme"].Value} ***REDACTED***");
        redacted = JwtPattern.Replace(redacted, "***REDACTED***");
        redacted = UrlCredentialsPattern.Replace(
            redacted,
            match => $"{match.Groups["scheme"].Value}***REDACTED***@");
        return HighEntropyTokenPattern.Replace(redacted, "***REDACTED***");
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
