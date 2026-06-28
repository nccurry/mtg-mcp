using MtgMcp.Core;

namespace MtgMcp.Playgroup;

/// <summary>
/// Sends Playgroup.gg public API requests and maps responses to Core models.
/// </summary>
public sealed partial class PlaygroupGateway
{
    /// <summary>
    /// Gets redacted Playgroup authentication status.
    /// </summary>
    public Task<PlaygroupAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        PlaygroupCredentials loaded = LoadCredentials();
        string? credentialsFile = GetCredentialsFilePath();
        PlaygroupAuthStatus status = new()
        {
            BaseAddress = httpClient.BaseAddress?.ToString() ?? options.BaseAddress.ToString(),
            HasApiKey = !string.IsNullOrWhiteSpace(loaded.ApiKey),
            HasCredentialsFile =
                !string.IsNullOrWhiteSpace(credentialsFile)
                && File.Exists(credentialsFile),
            CredentialsFileError = credentialsFileError,
        };

        return Task.FromResult(status);
    }

    /// <summary>
    /// Loads API-key credentials from options, direct environment fallback, or a credentials file.
    /// </summary>
    private PlaygroupCredentials LoadCredentials()
    {
        if (credentials is not null)
        {
            return credentials;
        }

        PlaygroupCredentials loaded = new()
        {
            ApiKey = MtgMcpText.FirstNonEmpty(
                options.ApiKey,
                Environment.GetEnvironmentVariable("PLAYGROUP_API_KEY")
            ),
        };

        string? credentialsFile = GetCredentialsFilePath();
        if (!string.IsNullOrWhiteSpace(credentialsFile) && File.Exists(credentialsFile))
        {
            try
            {
                PlaygroupCredentials fromFile = LoadCredentialsFile(credentialsFile);
                loaded.ApiKey = MtgMcpText.FirstNonEmpty(
                    loaded.ApiKey,
                    fromFile.ApiKey,
                    fromFile.AccessToken
                );
            }
            catch (InvalidDataException exception)
            {
                credentialsFileError = exception.Message;
            }
        }

        credentials = loaded;
        return loaded;
    }

    /// <summary>
    /// Gets the configured credentials-file path.
    /// </summary>
    private string? GetCredentialsFilePath()
    {
        return MtgMcpText.FirstNonEmpty(
            options.CredentialsFile,
            Environment.GetEnvironmentVariable("PLAYGROUP_CREDENTIALS_FILE")
        );
    }

    /// <summary>
    /// Loads Playgroup credentials from JSON or key-value text.
    /// </summary>
    private static PlaygroupCredentials LoadCredentialsFile(string credentialsFile)
    {
        IReadOnlyDictionary<string, string> values = MtgMcpCredentialsFile.Read(
            credentialsFile,
            providerName: "Playgroup",
            keyValueExample: "apiKey=value, accessToken=value, or token=value",
            jsonObjectRequirement: "must contain a JSON object or key=value lines.",
            jsonArrayLooksLikeJson: true,
            requireJsonStringValues: false);
        PlaygroupCredentials credentials = new();
        foreach ((string key, string value) in values)
        {
            ApplyCredentialValue(credentials, key, value);
        }

        return credentials;
    }

    /// <summary>
    /// Applies one credential key to the normalized credential object.
    /// </summary>
    private static void ApplyCredentialValue(
        PlaygroupCredentials credentials,
        string key,
        string value
    )
    {
        string normalized = key.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        if (normalized.Equals("apikey", StringComparison.OrdinalIgnoreCase))
        {
            credentials.ApiKey = value;
        }
        else if (
            normalized.Equals("accesstoken", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("access", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("token", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("bearer", StringComparison.OrdinalIgnoreCase)
        )
        {
            credentials.AccessToken = value;
        }
    }
}
