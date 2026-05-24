using System.Text.Json;
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
            ApiKey = FirstNonEmpty(
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
                loaded.ApiKey = FirstNonEmpty(
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
        return FirstNonEmpty(
            options.CredentialsFile,
            Environment.GetEnvironmentVariable("PLAYGROUP_CREDENTIALS_FILE")
        );
    }

    /// <summary>
    /// Loads Playgroup credentials from JSON or key-value text.
    /// </summary>
    private static PlaygroupCredentials LoadCredentialsFile(string credentialsFile)
    {
        string text;
        try
        {
            text = File.ReadAllText(credentialsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Playgroup credentials file '{credentialsFile}' could not be read: {exception.Message}",
                exception
            );
        }

        string trimmed = text.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new PlaygroupCredentials();
        }

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return ParseJsonCredentials(credentialsFile, text);
        }

        return ParseKeyValueCredentials(credentialsFile, text);
    }

    /// <summary>
    /// Parses a JSON credentials file while avoiding secret-bearing parse output.
    /// </summary>
    private static PlaygroupCredentials ParseJsonCredentials(string credentialsFile, string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Playgroup credentials file '{credentialsFile}' must contain a JSON object or key=value lines."
                );
            }

            PlaygroupCredentials credentials = new();
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
                $"Playgroup credentials file '{credentialsFile}' looks like JSON but could not be parsed. "
                    + "JSON requires double quotes around keys and string values; escape backslashes as \\\\ "
                    + "and double quotes as \\\". To avoid JSON escaping, use key=value lines instead.",
                exception
            );
        }
    }

    /// <summary>
    /// Parses key-value credential lines.
    /// </summary>
    private static PlaygroupCredentials ParseKeyValueCredentials(string credentialsFile, string text)
    {
        PlaygroupCredentials credentials = new();
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
                    $"Playgroup credentials file '{credentialsFile}' is not valid JSON or key=value format. "
                        + $"Line {lineNumber} must look like apiKey=value, accessToken=value, or token=value."
                );
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
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
