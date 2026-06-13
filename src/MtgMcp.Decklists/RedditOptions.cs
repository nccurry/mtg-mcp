using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Decklists;

/// <summary>
/// Configures Reddit OAuth credentials used by bounded discussion search.
/// </summary>
public sealed class RedditOptions
{
    /// <summary>
    /// Gets or sets the OAuth Reddit API base address used for bearer-token requests.
    /// </summary>
    public Uri OAuthBaseAddress { get; set; } = new("https://oauth.reddit.com/");

    /// <summary>
    /// Gets or sets the Reddit OAuth token endpoint.
    /// </summary>
    public Uri TokenEndpoint { get; set; } = new("https://www.reddit.com/api/v1/access_token");

    /// <summary>
    /// Gets or sets the Reddit app client id.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the Reddit app client secret for confidential or script apps.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the long-lived Reddit refresh token for local use.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets a temporary Reddit bearer access token.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets a preferred alias for a temporary Reddit bearer token.
    /// </summary>
    public string? BearerToken { get; set; }

    /// <summary>
    /// Gets or sets the access-token expiration timestamp when known.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the user agent Reddit should see on discussion requests.
    /// </summary>
    public string UserAgent { get; set; } = "mtg-mcp/1.0";

    /// <summary>
    /// Gets or sets the OAuth scope requested or expected for Reddit discussion reads.
    /// </summary>
    public string Scope { get; set; } = "read";

    /// <summary>
    /// Gets or sets the stable device id used by installed-client app-only OAuth.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets a local credentials file containing Reddit OAuth fields.
    /// </summary>
    public string? CredentialsFile { get; set; }
}

/// <summary>
/// Stores Reddit credentials loaded from configuration and optional local credential files.
/// </summary>
public sealed class RedditCredentials
{
    /// <summary>
    /// Gets or sets the Reddit app client id.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the confidential Reddit app client secret.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets a long-lived Reddit refresh token.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Gets or sets a temporary Reddit bearer access token.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets a preferred alias for a temporary Reddit bearer token.
    /// </summary>
    public string? BearerToken { get; set; }

    /// <summary>
    /// Gets or sets the access-token expiration timestamp when known.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the Reddit user agent.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Gets or sets the OAuth scope.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets the installed-client device id.
    /// </summary>
    public string? DeviceId { get; set; }
}

/// <summary>
/// Reports redacted Reddit credential availability.
/// </summary>
public sealed class RedditAuthStatus
{
    /// <summary>
    /// Gets or sets the discussion API base address selected for Reddit requests.
    /// </summary>
    public string BaseAddress { get; set; } = "";

    /// <summary>
    /// Gets or sets the configured OAuth API base address.
    /// </summary>
    public string OAuthBaseAddress { get; set; } = "";

    /// <summary>
    /// Gets or sets the configured token endpoint.
    /// </summary>
    public string TokenEndpoint { get; set; } = "";

    /// <summary>
    /// Gets or sets whether a client id is available.
    /// </summary>
    public bool HasClientId { get; set; }

    /// <summary>
    /// Gets or sets whether a client secret is available.
    /// </summary>
    public bool HasClientSecret { get; set; }

    /// <summary>
    /// Gets or sets whether a refresh token is available.
    /// </summary>
    public bool HasRefreshToken { get; set; }

    /// <summary>
    /// Gets or sets whether a temporary access token or bearer token is available.
    /// </summary>
    public bool HasAccessToken { get; set; }

    /// <summary>
    /// Gets or sets whether an installed-client device id is available.
    /// </summary>
    public bool HasDeviceId { get; set; }

    /// <summary>
    /// Gets or sets whether the configured credentials file exists.
    /// </summary>
    public bool HasCredentialsFile { get; set; }

    /// <summary>
    /// Gets or sets a redacted credential file parsing error.
    /// </summary>
    public string? CredentialsFileError { get; set; }

    /// <summary>
    /// Gets or sets the configured user-agent value.
    /// </summary>
    public string UserAgent { get; set; } = "";

    /// <summary>
    /// Gets or sets the configured OAuth scope.
    /// </summary>
    public string Scope { get; set; } = "read";

    /// <summary>
    /// Gets whether the credentials file failed to parse.
    /// </summary>
    public bool HasCredentialsFileError => !string.IsNullOrWhiteSpace(CredentialsFileError);

    /// <summary>
    /// Gets whether the current configuration can use Reddit's OAuth API path.
    /// </summary>
    public bool CanUseOAuth =>
        HasRefreshToken && HasClientId
        || HasAccessToken
        || HasClientId && HasClientSecret
        || HasClientId && HasDeviceId;

    /// <summary>
    /// Gets the effective Reddit authentication mode.
    /// </summary>
    public string Mode =>
        HasCredentialsFileError ? "credentials-file-error"
        : HasRefreshToken && HasClientId ? "refresh-token"
        : HasAccessToken ? "access-token"
        : HasClientId && HasClientSecret ? "client-credentials"
        : HasClientId && HasDeviceId ? "installed-client"
        : "public-json-fallback";

    /// <summary>
    /// Gets or sets non-secret setup notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Parses Reddit credential files written by the CLI helper or by users.
/// </summary>
public static class RedditCredentialsFile
{
    /// <summary>
    /// Stores JSON options for credential files.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>
    /// Loads Reddit credentials from JSON or key-value text.
    /// </summary>
    public static RedditCredentials Load(string credentialsFile)
    {
        string text;
        try
        {
            text = File.ReadAllText(credentialsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Reddit credentials file '{credentialsFile}' could not be read: {exception.Message}",
                exception);
        }

        string trimmed = text.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new RedditCredentials();
        }

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return ParseJsonCredentials(credentialsFile, text);
        }

        return ParseKeyValueCredentials(credentialsFile, text);
    }

    /// <summary>
    /// Applies one credential value to a credentials object.
    /// </summary>
    public static void ApplyCredentialValue(RedditCredentials credentials, string key, string value)
    {
        string normalized = key.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        switch (normalized)
        {
            case "clientid":
                credentials.ClientId = EmptyToNull(value);
                break;
            case "clientsecret":
                credentials.ClientSecret = EmptyToNull(value);
                break;
            case "refreshtoken":
                credentials.RefreshToken = EmptyToNull(value);
                break;
            case "accesstoken":
                credentials.AccessToken = EmptyToNull(value);
                break;
            case "bearertoken":
                credentials.BearerToken = EmptyToNull(value);
                break;
            case "expiresatutc":
            case "expiresat":
                credentials.ExpiresAtUtc = DateTimeOffset.TryParse(value, out DateTimeOffset expiresAt)
                    ? expiresAt.ToUniversalTime()
                    : null;
                break;
            case "useragent":
                credentials.UserAgent = EmptyToNull(value);
                break;
            case "scope":
                credentials.Scope = EmptyToNull(value);
                break;
            case "deviceid":
                credentials.DeviceId = EmptyToNull(value);
                break;
        }
    }

    /// <summary>
    /// Parses JSON credentials without including secret values in parse errors.
    /// </summary>
    private static RedditCredentials ParseJsonCredentials(string credentialsFile, string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Reddit credentials file '{credentialsFile}' must contain a JSON object or key=value lines.");
            }

            RedditCredentials credentials = new();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                string? value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.GetRawText();
                ApplyCredentialValue(credentials, property.Name, value ?? "");
            }

            return credentials;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Reddit credentials file '{credentialsFile}' looks like JSON but could not be parsed. "
                    + "JSON requires double quotes around keys and string values; escape backslashes as \\\\ "
                    + "and double quotes as \\\". To avoid JSON escaping, use key=value lines instead.",
                exception);
        }
    }

    /// <summary>
    /// Parses key-value credential lines.
    /// </summary>
    private static RedditCredentials ParseKeyValueCredentials(string credentialsFile, string text)
    {
        RedditCredentials credentials = new();
        using StringReader reader = new(text);
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new InvalidDataException(
                    $"Reddit credentials file '{credentialsFile}' is not valid JSON or key=value format. "
                        + $"Line {lineNumber} must look like clientId=value, refreshToken=value, or bearerToken=value.");
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            ApplyCredentialValue(credentials, key, value);
        }

        return credentials;
    }

    /// <summary>
    /// Converts blank input to null.
    /// </summary>
    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
