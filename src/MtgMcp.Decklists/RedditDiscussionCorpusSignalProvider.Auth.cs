using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Contains Reddit credential loading and OAuth token helpers.
/// </summary>
public sealed partial class RedditDiscussionCorpusSignalProvider
{
    /// <summary>
    /// Refreshes access tokens shortly before Reddit expires them.
    /// </summary>
    private static readonly TimeSpan AccessTokenRefreshBuffer = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Coordinates token acquisition for parallel Reddit requests on this provider instance.
    /// </summary>
    private readonly SemaphoreSlim authLock = new(1, 1);

    /// <summary>
    /// Caches an access token acquired from Reddit OAuth.
    /// </summary>
    private string? sessionAccessToken;

    /// <summary>
    /// Caches when the acquired access token expires.
    /// </summary>
    private DateTimeOffset? sessionExpiresAtUtc;

    /// <summary>
    /// Stores credentials loaded from configuration and an optional file.
    /// </summary>
    private RedditCredentials? credentials;

    /// <summary>
    /// Stores a non-secret credential file error for status output.
    /// </summary>
    private string? credentialsFileError;

    /// <summary>
    /// Gets redacted Reddit OAuth credential availability.
    /// </summary>
    public Task<RedditAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        RedditCredentials loaded = LoadCredentials();
        string? credentialsFile = GetCredentialsFilePath();
        RedditAuthStatus status = new()
        {
            BaseAddress = httpClient.BaseAddress?.ToString() ?? "",
            OAuthBaseAddress = redditOptions.OAuthBaseAddress.ToString(),
            TokenEndpoint = redditOptions.TokenEndpoint.ToString(),
            HasClientId = !string.IsNullOrWhiteSpace(loaded.ClientId),
            HasClientSecret = !string.IsNullOrWhiteSpace(loaded.ClientSecret),
            HasRefreshToken = !string.IsNullOrWhiteSpace(loaded.RefreshToken),
            HasAccessToken = HasTemporaryAccessToken(loaded),
            HasDeviceId = !string.IsNullOrWhiteSpace(loaded.DeviceId),
            HasCredentialsFile = !string.IsNullOrWhiteSpace(credentialsFile) && File.Exists(credentialsFile),
            CredentialsFileError = credentialsFileError,
            UserAgent = EffectiveUserAgent(loaded),
            Scope = EffectiveScope(loaded),
        };

        if (!status.CanUseOAuth)
        {
            status.Notes.Add("No Reddit OAuth credentials configured; mtg-mcp will use public JSON only when AllowUnofficialApi permits it.");
        }

        return Task.FromResult(status);
    }

    /// <summary>
    /// Loads Reddit credentials from options, direct environment fallback, and a credentials file.
    /// </summary>
    private RedditCredentials LoadCredentials()
    {
        if (credentials is not null)
        {
            return credentials;
        }

        RedditCredentials loaded = new()
        {
            ClientId = FirstNonEmpty(redditOptions.ClientId, Environment.GetEnvironmentVariable("REDDIT_CLIENT_ID")),
            ClientSecret = FirstNonEmpty(redditOptions.ClientSecret, Environment.GetEnvironmentVariable("REDDIT_CLIENT_SECRET")),
            RefreshToken = FirstNonEmpty(redditOptions.RefreshToken, Environment.GetEnvironmentVariable("REDDIT_REFRESH_TOKEN")),
            AccessToken = FirstNonEmpty(redditOptions.AccessToken, Environment.GetEnvironmentVariable("REDDIT_ACCESS_TOKEN")),
            BearerToken = FirstNonEmpty(redditOptions.BearerToken, Environment.GetEnvironmentVariable("REDDIT_BEARER_TOKEN")),
            ExpiresAtUtc = redditOptions.ExpiresAtUtc,
            UserAgent = FirstNonEmpty(redditOptions.UserAgent, Environment.GetEnvironmentVariable("REDDIT_USER_AGENT")),
            Scope = FirstNonEmpty(redditOptions.Scope, Environment.GetEnvironmentVariable("REDDIT_SCOPE")),
            DeviceId = FirstNonEmpty(redditOptions.DeviceId, Environment.GetEnvironmentVariable("REDDIT_DEVICE_ID")),
        };

        string? credentialsFile = GetCredentialsFilePath();
        if (!string.IsNullOrWhiteSpace(credentialsFile) && File.Exists(credentialsFile))
        {
            try
            {
                RedditCredentials fromFile = RedditCredentialsFile.Load(credentialsFile);
                loaded.ClientId = FirstNonEmpty(loaded.ClientId, fromFile.ClientId);
                loaded.ClientSecret = FirstNonEmpty(loaded.ClientSecret, fromFile.ClientSecret);
                loaded.RefreshToken = FirstNonEmpty(loaded.RefreshToken, fromFile.RefreshToken);
                loaded.AccessToken = FirstNonEmpty(loaded.AccessToken, fromFile.AccessToken);
                loaded.BearerToken = FirstNonEmpty(loaded.BearerToken, fromFile.BearerToken);
                loaded.ExpiresAtUtc ??= fromFile.ExpiresAtUtc;
                loaded.UserAgent = FirstNonEmpty(loaded.UserAgent, fromFile.UserAgent);
                loaded.Scope = FirstNonEmpty(loaded.Scope, fromFile.Scope);
                loaded.DeviceId = FirstNonEmpty(loaded.DeviceId, fromFile.DeviceId);
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
    /// Gets the configured Reddit credentials file path.
    /// </summary>
    private string? GetCredentialsFilePath()
    {
        return FirstNonEmpty(
            redditOptions.CredentialsFile,
            Environment.GetEnvironmentVariable("REDDIT_CREDENTIALS_FILE"));
    }

    /// <summary>
    /// Gets a bearer token when Reddit OAuth credentials are configured.
    /// </summary>
    private async Task<string?> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        RedditCredentials loaded = LoadCredentials();
        if (!string.IsNullOrWhiteSpace(loaded.RefreshToken) && !string.IsNullOrWhiteSpace(loaded.ClientId))
        {
            return await GetAcquiredBearerTokenAsync(
                    loaded,
                    "refresh_token",
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = loaded.RefreshToken
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string? temporaryToken = FirstNonEmpty(loaded.AccessToken, loaded.BearerToken);
        if (!string.IsNullOrWhiteSpace(temporaryToken) && !IsExpired(loaded.ExpiresAtUtc))
        {
            return temporaryToken;
        }

        if (!string.IsNullOrWhiteSpace(loaded.ClientId) && !string.IsNullOrWhiteSpace(loaded.ClientSecret))
        {
            return await GetAcquiredBearerTokenAsync(
                    loaded,
                    "client_credentials",
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials"
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(loaded.ClientId) && !string.IsNullOrWhiteSpace(loaded.DeviceId))
        {
            return await GetAcquiredBearerTokenAsync(
                    loaded,
                    "installed_client",
                    new Dictionary<string, string>
                    {
                        ["grant_type"] = "https://oauth.reddit.com/grants/installed_client",
                        ["device_id"] = loaded.DeviceId
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Gets an acquired Reddit OAuth token, refreshing it when needed.
    /// </summary>
    private async Task<string?> GetAcquiredBearerTokenAsync(
        RedditCredentials loaded,
        string flow,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionAccessToken) && !IsExpired(sessionExpiresAtUtc))
        {
            return sessionAccessToken;
        }

        await authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(sessionAccessToken) && !IsExpired(sessionExpiresAtUtc))
            {
                return sessionAccessToken;
            }

            RedditTokenResponse token = await RequestAccessTokenAsync(loaded, flow, form, cancellationToken)
                .ConfigureAwait(false);
            sessionAccessToken = token.AccessToken;
            sessionExpiresAtUtc = token.ExpiresAtUtc;
            return sessionAccessToken;
        }
        finally
        {
            authLock.Release();
        }
    }

    /// <summary>
    /// Requests a short-lived Reddit OAuth access token.
    /// </summary>
    private async Task<RedditTokenResponse> RequestAccessTokenAsync(
        RedditCredentials loaded,
        string flow,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, redditOptions.TokenEndpoint);
        request.Headers.Authorization = BuildBasicAuthorization(loaded);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(EffectiveUserAgent(loaded));
        request.Content = new FormUrlEncodedContent(form);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new RedditOAuthException(
                response.StatusCode,
                $"Reddit OAuth {flow} token request returned HTTP {(int)response.StatusCode}.");
        }

        using JsonDocument document = JsonDocument.Parse(payload);
        string? accessToken = ReadString(document.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new RedditOAuthException(
                HttpStatusCode.OK,
                $"Reddit OAuth {flow} token response did not include access_token.");
        }

        int expiresIn = ReadInt32(document.RootElement, "expires_in") ?? 3600;
        return new RedditTokenResponse(
            accessToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresIn)));
    }

    /// <summary>
    /// Creates a Reddit API request with an OAuth bearer token when available.
    /// </summary>
    private async Task<HttpRequestMessage> CreateRedditRequestAsync(string path, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, path);
        string? bearerToken = await GetBearerTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", bearerToken);
        }

        return request;
    }

    /// <summary>
    /// Checks whether any configured credential can use Reddit OAuth.
    /// </summary>
    private static bool HasOAuthCredential(RedditCredentials credentials)
    {
        return HasTemporaryAccessToken(credentials)
            || !string.IsNullOrWhiteSpace(credentials.ClientId)
            && (!string.IsNullOrWhiteSpace(credentials.RefreshToken)
                || !string.IsNullOrWhiteSpace(credentials.ClientSecret)
                || !string.IsNullOrWhiteSpace(credentials.DeviceId));
    }

    /// <summary>
    /// Checks whether a temporary access token or bearer-token alias is configured.
    /// </summary>
    private static bool HasTemporaryAccessToken(RedditCredentials credentials)
    {
        return !string.IsNullOrWhiteSpace(credentials.AccessToken)
            || !string.IsNullOrWhiteSpace(credentials.BearerToken);
    }

    /// <summary>
    /// Checks whether an access token is expired or inside the refresh buffer.
    /// </summary>
    private static bool IsExpired(DateTimeOffset? expiresAtUtc)
    {
        return expiresAtUtc.HasValue
            && expiresAtUtc.Value <= DateTimeOffset.UtcNow.Add(AccessTokenRefreshBuffer);
    }

    /// <summary>
    /// Builds Reddit's Basic authorization header for OAuth token requests.
    /// </summary>
    private static AuthenticationHeaderValue BuildBasicAuthorization(RedditCredentials credentials)
    {
        string clientId = credentials.ClientId ?? "";
        string clientSecret = credentials.ClientSecret ?? "";
        string value = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}"));
        return new AuthenticationHeaderValue("Basic", value);
    }

    /// <summary>
    /// Gets the Reddit user agent from credentials or defaults.
    /// </summary>
    private string EffectiveUserAgent(RedditCredentials credentials)
    {
        return FirstNonEmpty(credentials.UserAgent, redditOptions.UserAgent, "mtg-mcp/1.0")
            ?? "mtg-mcp/1.0";
    }

    /// <summary>
    /// Gets the Reddit OAuth scope from credentials or defaults.
    /// </summary>
    private string EffectiveScope(RedditCredentials credentials)
    {
        return FirstNonEmpty(credentials.Scope, redditOptions.Scope, "read") ?? "read";
    }

    /// <summary>
    /// Gets the first non-empty value.
    /// </summary>
    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Carries the Reddit OAuth token and expiration.
    /// </summary>
    private sealed record RedditTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

    /// <summary>
    /// Reports sanitized Reddit OAuth token acquisition failures.
    /// </summary>
    private sealed class RedditOAuthException : Exception
    {
        /// <summary>
        /// Creates a sanitized Reddit OAuth exception.
        /// </summary>
        public RedditOAuthException(HttpStatusCode statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        /// <summary>
        /// Gets the HTTP status code returned by Reddit.
        /// </summary>
        public HttpStatusCode StatusCode { get; }
    }
}
