using System.Net.Http.Headers;
using System.Text.Json;
using MtgMcp.Core;
using static MtgMcp.Core.MtgMcpJson;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Refreshes decoded JWTs shortly before expiry so requests do not race token lifetime.
    /// </summary>
    private static readonly TimeSpan JwtRefreshSkew = TimeSpan.FromMinutes(1);

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
    /// Loads credentials from options, environment variables, or a configured credentials file.
    /// </summary>
    private ArchidektCredentials LoadCredentials()
    {
        if (credentials is not null)
        {
            return credentials;
        }

        ArchidektCredentials loaded = new()
        {
            Username = MtgMcpText.FirstNonEmpty(
                options.Username,
                Environment.GetEnvironmentVariable("ARCHIDEKT_USERNAME")
            ),
            Password = MtgMcpText.FirstNonEmpty(
                options.Password,
                Environment.GetEnvironmentVariable("ARCHIDEKT_PASSWORD")
            ),
        };

        string? credentialsFile = MtgMcpText.FirstNonEmpty(
            options.CredentialsFile,
            Environment.GetEnvironmentVariable("ARCHIDEKT_CREDENTIALS_FILE")
        );
        if (!string.IsNullOrWhiteSpace(credentialsFile) && File.Exists(credentialsFile))
        {
            try
            {
                ArchidektCredentials fromFile = LoadCredentialsFile(credentialsFile);
                loaded.Username = MtgMcpText.FirstNonEmpty(loaded.Username, fromFile.Username);
                loaded.Password = MtgMcpText.FirstNonEmpty(loaded.Password, fromFile.Password);
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
    /// Adds an Archidekt bearer token when credentials are available or required.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(bool required, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(required, forceRefresh: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Adds or refreshes an Archidekt bearer token when credentials are available or required.
    /// </summary>
    private async Task<bool> EnsureAuthenticatedAsync(
        bool required,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArchidektCredentials loaded = LoadCredentials();
        if (!forceRefresh && HasUsableAuthorizationHeader())
        {
            return false;
        }

        await authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The gateway may receive parallel requests, so only one request should
            // create a token while the rest reuse the cached result.
            if (!forceRefresh && HasUsableAuthorizationHeader())
            {
                return false;
            }

            bool sessionRequiresRefresh =
                !string.IsNullOrWhiteSpace(sessionJwt) && SessionJwtRequiresRefresh();
            if (!forceRefresh && !string.IsNullOrWhiteSpace(sessionJwt) && !sessionRequiresRefresh)
            {
                ApplySessionAuthorization(sessionJwt);
                return false;
            }

            string? username = loaded.Username;
            string? password = loaded.Password;
            bool canLogin = options.EnableUsernamePasswordLogin
                && !string.IsNullOrWhiteSpace(username)
                && !string.IsNullOrWhiteSpace(password);
            if (forceRefresh && !canLogin)
            {
                return false;
            }

            if (sessionRequiresRefresh && !canLogin)
            {
                return false;
            }

            if (forceRefresh || sessionRequiresRefresh)
            {
                ClearSessionAuthorization();
            }

            if (canLogin)
            {
                string usernameOrEmail = username!;
                string loginPassword = password!;
                string? jwt = null;
                try
                {
                    jwt = await TryLoginAsync(
                            usernameOrEmail,
                            loginPassword,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (!required && exception is not OperationCanceledException)
                {
                    jwt = null;
                }

                if (!string.IsNullOrWhiteSpace(jwt))
                {
                    ApplySessionJwt(jwt);
                    return true;
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

            return false;
        }
        finally
        {
            authLock.Release();
        }
    }

    /// <summary>
    /// Attempts one re-login after an authenticated request receives an unauthorized response.
    /// </summary>
    private async Task<bool> TryRefreshAuthenticationAsync(CancellationToken cancellationToken)
    {
        return await EnsureAuthenticatedAsync(
                required: false,
                forceRefresh: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a newly issued Archidekt token to future requests.
    /// </summary>
    private void ApplySessionJwt(string jwt)
    {
        sessionJwt = jwt;
        sessionJwtExpiresAt = TryReadJwtExpiration(jwt);
        ApplySessionAuthorization(jwt);
    }

    /// <summary>
    /// Sets the HTTP Authorization header from a cached token.
    /// </summary>
    private void ApplySessionAuthorization(string jwt)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            options.AuthScheme,
            jwt
        );
    }

    /// <summary>
    /// Clears cached authentication before a forced re-login.
    /// </summary>
    private void ClearSessionAuthorization()
    {
        sessionJwt = null;
        sessionJwtExpiresAt = null;
        httpClient.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>
    /// Determines whether the current Authorization header can be reused.
    /// </summary>
    private bool HasUsableAuthorizationHeader()
    {
        AuthenticationHeaderValue? authorization = httpClient.DefaultRequestHeaders.Authorization;
        if (authorization is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sessionJwt)
            || !string.Equals(authorization.Parameter, sessionJwt, StringComparison.Ordinal))
        {
            return true;
        }

        return !SessionJwtRequiresRefresh();
    }

    /// <summary>
    /// Determines whether the cached Archidekt JWT should be refreshed.
    /// </summary>
    private bool SessionJwtRequiresRefresh()
    {
        return sessionJwtExpiresAt.HasValue
            && DateTimeOffset.UtcNow >= sessionJwtExpiresAt.Value - JwtRefreshSkew;
    }

    /// <summary>
    /// Reads the expiry timestamp from a compact JWT payload.
    /// </summary>
    private static DateTimeOffset? TryReadJwtExpiration(string jwt)
    {
        string[] parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            byte[] payload = DecodeBase64Url(parts[1]);
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("exp", out JsonElement exp)
                && exp.TryGetInt64(out long seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }
        catch (Exception exception) when (
            exception is FormatException
                or JsonException
                or ArgumentException
                or ArgumentOutOfRangeException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Decodes a JWT base64url segment.
    /// </summary>
    private static byte[] DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        int padding = base64.Length % 4;
        if (padding == 1)
        {
            throw new FormatException("Invalid base64url length.");
        }

        if (padding > 0)
        {
            base64 = base64.PadRight(base64.Length + 4 - padding, '=');
        }

        return Convert.FromBase64String(base64);
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
        IReadOnlyDictionary<string, string> values = MtgMcpCredentialsFile.Read(
            credentialsFile,
            providerName: "Archidekt",
            keyValueExample: "username=value or password=value",
            jsonObjectRequirement: "must contain a JSON object.",
            jsonArrayLooksLikeJson: false,
            requireJsonStringValues: true);
        ArchidektCredentials credentials = new();
        foreach ((string key, string value) in values)
        {
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
