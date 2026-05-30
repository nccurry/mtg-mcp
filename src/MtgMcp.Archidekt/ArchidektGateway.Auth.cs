using System.Net.Http.Headers;
using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Returns redacted Archidekt credential availability.
    /// </summary>
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        ArchidektCredentials loaded = LoadCredentials();
        AuthStatus status = new()
        {
            HasUsernamePassword =
                !string.IsNullOrWhiteSpace(loaded.Username)
                && !string.IsNullOrWhiteSpace(loaded.Password),
            HasCredentialsFile =
                !string.IsNullOrWhiteSpace(options.CredentialsFile)
                && File.Exists(options.CredentialsFile),
            CredentialsFileError = credentialsFileError,
        };

        return Task.FromResult(status);
    }

    /// <summary>
    /// Loads the credentials.
    /// </summary>
    private ArchidektCredentials LoadCredentials()
    {
        if (credentials is not null)
        {
            return credentials;
        }

        ArchidektCredentials loaded = new()
        {
            Username = FirstNonEmpty(
                options.Username,
                Environment.GetEnvironmentVariable("ARCHIDEKT_USERNAME")
            ),
            Password = FirstNonEmpty(
                options.Password,
                Environment.GetEnvironmentVariable("ARCHIDEKT_PASSWORD")
            ),
        };

        string? credentialsFile = FirstNonEmpty(
            options.CredentialsFile,
            Environment.GetEnvironmentVariable("ARCHIDEKT_CREDENTIALS_FILE")
        );
        if (!string.IsNullOrWhiteSpace(credentialsFile) && File.Exists(credentialsFile))
        {
            try
            {
                ArchidektCredentials fromFile = LoadCredentialsFile(credentialsFile);
                loaded.Username = FirstNonEmpty(loaded.Username, fromFile.Username);
                loaded.Password = FirstNonEmpty(loaded.Password, fromFile.Password);
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
    /// Ensures the authenticated.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(bool required, CancellationToken cancellationToken)
    {
        ArchidektCredentials loaded = LoadCredentials();
        if (httpClient.DefaultRequestHeaders.Authorization is not null)
        {
            return;
        }

        await authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The gateway may receive parallel requests, so only one request should
            // create a token while the rest reuse the cached result.
            if (!string.IsNullOrWhiteSpace(sessionJwt))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    options.AuthScheme,
                    sessionJwt
                );
                return;
            }

            if (
                required
                && options.EnableUsernamePasswordLogin
                && !string.IsNullOrWhiteSpace(loaded.Username)
                && !string.IsNullOrWhiteSpace(loaded.Password)
            )
            {
                string? jwt = await TryLoginAsync(
                        loaded.Username,
                        loaded.Password,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(jwt))
                {
                    sessionJwt = jwt;
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        options.AuthScheme,
                        jwt
                    );
                    return;
                }
            }

            if (required)
            {
                if (!string.IsNullOrWhiteSpace(credentialsFileError))
                {
                    throw new InvalidOperationException(credentialsFileError);
                }

                throw new InvalidOperationException(
                    "Archidekt credentials are required for this operation."
                );
            }
        }
        finally
        {
            authLock.Release();
        }
    }

    /// <summary>
    /// Attempts to log in with a username or account email and password.
    /// </summary>
    private async Task<string?> TryLoginAsync(
        string usernameOrEmail,
        string password,
        CancellationToken cancellationToken
    )
    {
        object payload = LooksLikeEmail(usernameOrEmail)
            ? new { email = usernameOrEmail, password }
            : new { username = usernameOrEmail, password };

        using JsonDocument document = await SendJsonAsync(
                HttpMethod.Post,
                "api/rest-auth/login/",
                payload,
                cancellationToken,
                authenticate: false
            )
            .ConfigureAwait(false);

        sessionUserId = GetNestedString(document.RootElement, "user", "id") ?? sessionUserId;
        return GetString(document.RootElement, "access_token")
            ?? GetString(document.RootElement, "access")
            ?? GetString(document.RootElement, "token")
            ?? GetString(document.RootElement, "key")
            ?? GetString(document.RootElement, "jwt");
    }

    /// <summary>
    /// Loads credentials from a JSON or key-value credentials file.
    /// </summary>
    private static ArchidektCredentials LoadCredentialsFile(string credentialsFile)
    {
        string text;
        try
        {
            text = File.ReadAllText(credentialsFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Archidekt credentials file '{credentialsFile}' could not be read: {exception.Message}",
                exception
            );
        }

        string trimmed = text.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new ArchidektCredentials();
        }

        if (trimmed.StartsWith('{'))
        {
            try
            {
                return LoadJsonCredentialsFile(credentialsFile, text);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Archidekt credentials file '{credentialsFile}' looks like JSON but could not be parsed. "
                        + "JSON requires double quotes around keys and string values; escape backslashes as \\\\ "
                        + "and double quotes as \\\". To avoid JSON escaping, use key=value lines instead.",
                    exception
                );
            }
        }

        return ParseKeyValueCredentials(credentialsFile, text);
    }

    /// <summary>
    /// Loads username/password credentials from a JSON credentials file.
    /// </summary>
    private static ArchidektCredentials LoadJsonCredentialsFile(string credentialsFile, string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Archidekt credentials file '{credentialsFile}' must contain a JSON object."
            );
        }

        ArchidektCredentials credentials = new();
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            string value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                    ?? throw new InvalidDataException(
                        $"Archidekt credentials file '{credentialsFile}' fields must be strings."
                    )
                : throw new InvalidDataException(
                    $"Archidekt credentials file '{credentialsFile}' fields must be strings."
                );

            if (!ApplyCredentialValue(credentials, property.Name, value))
            {
                throw new InvalidDataException(
                    $"Archidekt credentials file '{credentialsFile}' only supports username and password fields."
                );
            }
        }

        return credentials;
    }

    /// <summary>
    /// Parses key-value credentials text.
    /// </summary>
    private static ArchidektCredentials ParseKeyValueCredentials(string credentialsFile, string text)
    {
        ArchidektCredentials credentials = new();
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
                    $"Archidekt credentials file '{credentialsFile}' is not valid JSON or key=value format. "
                        + $"Line {lineNumber} must look like username=value or password=value."
                );
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (!ApplyCredentialValue(credentials, key, value))
            {
                throw new InvalidDataException(
                    $"Archidekt credentials file '{credentialsFile}' only supports username and password fields."
                );
            }
        }

        return credentials;
    }

    /// <summary>
    /// Applies a credential file key-value pair to credentials.
    /// </summary>
    private static bool ApplyCredentialValue(
        ArchidektCredentials credentials,
        string key,
        string value
    )
    {
        string normalized = key.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        if (normalized.Equals("username", StringComparison.OrdinalIgnoreCase))
        {
            credentials.Username = value;
            return true;
        }

        if (normalized.Equals("password", StringComparison.OrdinalIgnoreCase))
        {
            credentials.Password = value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a value looks like an email address.
    /// </summary>
    private static bool LooksLikeEmail(string value)
    {
        int at = value.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at < value.Length - 1;
    }
}
