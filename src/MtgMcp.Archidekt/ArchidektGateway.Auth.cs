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
    /// Gets the auth status.
    /// </summary>
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        ArchidektCredentials loaded = LoadCredentials();
        AuthStatus status = new()
        {
            HasJwt = !string.IsNullOrWhiteSpace(loaded.Jwt),
            HasRefreshToken = !string.IsNullOrWhiteSpace(loaded.RefreshToken),
            HasUserId = !string.IsNullOrWhiteSpace(loaded.UserId),
            HasEmailPassword =
                !string.IsNullOrWhiteSpace(loaded.Email)
                && !string.IsNullOrWhiteSpace(loaded.Password),
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
            Jwt = FirstNonEmpty(options.Jwt, Environment.GetEnvironmentVariable("ARCHIDEKT_JWT")),
            RefreshToken = FirstNonEmpty(
                options.RefreshToken,
                Environment.GetEnvironmentVariable("ARCHIDEKT_REFRESH_TOKEN")
            ),
            UserId = FirstNonEmpty(
                options.UserId,
                Environment.GetEnvironmentVariable("ARCHIDEKT_USER_ID")
            ),
            Email = FirstNonEmpty(
                options.Email,
                Environment.GetEnvironmentVariable("ARCHIDEKT_EMAIL")
            ),
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
                loaded.Jwt = FirstNonEmpty(loaded.Jwt, fromFile.Jwt, fromFile.AccessToken);
                loaded.RefreshToken = FirstNonEmpty(loaded.RefreshToken, fromFile.RefreshToken);
                loaded.UserId = FirstNonEmpty(loaded.UserId, fromFile.UserId);
                loaded.Email = FirstNonEmpty(loaded.Email, fromFile.Email);
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
        if (!string.IsNullOrWhiteSpace(loaded.Jwt))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                options.AuthScheme,
                loaded.Jwt
            );
            return;
        }

        await authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The gateway may receive parallel requests, so only one request should
            // refresh or create a token while the rest reuse the cached result.
            if (!string.IsNullOrWhiteSpace(loaded.Jwt))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    options.AuthScheme,
                    loaded.Jwt
                );
                return;
            }

            if (!string.IsNullOrWhiteSpace(loaded.RefreshToken))
            {
                // Prefer refresh tokens over username/password credentials so
                // configured secrets stay dormant unless the refresh path fails.
                string? refreshed = await TryRefreshJwtAsync(loaded.RefreshToken, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(refreshed))
                {
                    loaded.Jwt = refreshed;
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        options.AuthScheme,
                        refreshed
                    );
                    return;
                }
            }

            if (
                options.EnableUsernamePasswordLogin
                && (
                    !string.IsNullOrWhiteSpace(loaded.Email)
                    || !string.IsNullOrWhiteSpace(loaded.Username)
                )
                && !string.IsNullOrWhiteSpace(loaded.Password)
            )
            {
                string loginIdentifier = FirstNonEmpty(loaded.Email, loaded.Username)!;
                string? jwt = await TryLoginAsync(
                        loginIdentifier,
                        loaded.Password,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(jwt))
                {
                    loaded.Jwt = jwt;
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
    /// Attempts to refresh the jwt.
    /// </summary>
    private async Task<string?> TryRefreshJwtAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using JsonDocument document = await SendJsonAsync(
                    HttpMethod.Post,
                    "api/rest-auth/token/refresh/",
                    new { refresh = refreshToken },
                    cancellationToken,
                    authenticate: false
                )
                .ConfigureAwait(false);

            return GetString(document.RootElement, "access")
                ?? GetString(document.RootElement, "token")
                ?? GetString(document.RootElement, "jwt");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to log in with an email or username password pair.
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

        ArchidektCredentials loaded = LoadCredentials();
        loaded.RefreshToken = GetString(document.RootElement, "refresh_token")
            ?? GetString(document.RootElement, "refresh")
            ?? loaded.RefreshToken;
        loaded.UserId = GetNestedString(document.RootElement, "user", "id") ?? loaded.UserId;
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
                return JsonSerializer.Deserialize<ArchidektCredentials>(text, SerializerOptions)
                    ?? new ArchidektCredentials();
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
                        + $"Line {lineNumber} must look like username=value, password=value, jwt=value, "
                        + "accessToken=value, or refreshToken=value."
                );
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            ApplyCredentialValue(credentials, key, value);
        }

        return credentials;
    }

    /// <summary>
    /// Applies a credential file key-value pair to credentials.
    /// </summary>
    private static void ApplyCredentialValue(
        ArchidektCredentials credentials,
        string key,
        string value
    )
    {
        string normalized = key.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        if (normalized.Equals("jwt", StringComparison.OrdinalIgnoreCase))
        {
            credentials.Jwt = value;
        }
        else if (
            normalized.Equals("accesstoken", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("access", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("token", StringComparison.OrdinalIgnoreCase)
        )
        {
            credentials.AccessToken = value;
        }
        else if (
            normalized.Equals("refreshtoken", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("refresh", StringComparison.OrdinalIgnoreCase)
        )
        {
            credentials.RefreshToken = value;
        }
        else if (
            normalized.Equals("userid", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("user", StringComparison.OrdinalIgnoreCase)
        )
        {
            credentials.UserId = value;
        }
        else if (normalized.Equals("email", StringComparison.OrdinalIgnoreCase))
        {
            credentials.Email = value;
        }
        else if (normalized.Equals("username", StringComparison.OrdinalIgnoreCase))
        {
            credentials.Username = value;
        }
        else if (normalized.Equals("password", StringComparison.OrdinalIgnoreCase))
        {
            credentials.Password = value;
        }
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
