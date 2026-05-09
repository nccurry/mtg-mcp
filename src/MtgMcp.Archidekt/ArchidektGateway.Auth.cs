using System.Net.Http.Headers;
using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

public sealed partial class ArchidektGateway
{
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        ArchidektCredentials loaded = LoadCredentials();
        AuthStatus status = new()
        {
            HasJwt = !string.IsNullOrWhiteSpace(loaded.Jwt),
            HasRefreshToken = !string.IsNullOrWhiteSpace(loaded.RefreshToken),
            HasUsernamePassword = !string.IsNullOrWhiteSpace(loaded.Username) && !string.IsNullOrWhiteSpace(loaded.Password),
            HasCredentialsFile = !string.IsNullOrWhiteSpace(options.CredentialsFile) && File.Exists(options.CredentialsFile)
        };

        return Task.FromResult(status);
    }

    private ArchidektCredentials LoadCredentials()
    {
        if (credentials is not null)
        {
            return credentials;
        }

        ArchidektCredentials loaded = new()
        {
            Jwt = FirstNonEmpty(options.Jwt, Environment.GetEnvironmentVariable("ARCHIDEKT_JWT")),
            RefreshToken = FirstNonEmpty(options.RefreshToken, Environment.GetEnvironmentVariable("ARCHIDEKT_REFRESH_TOKEN")),
            Username = FirstNonEmpty(options.Username, Environment.GetEnvironmentVariable("ARCHIDEKT_USERNAME")),
            Password = FirstNonEmpty(options.Password, Environment.GetEnvironmentVariable("ARCHIDEKT_PASSWORD"))
        };

        string? credentialsFile = FirstNonEmpty(options.CredentialsFile, Environment.GetEnvironmentVariable("ARCHIDEKT_CREDENTIALS_FILE"));
        if (!string.IsNullOrWhiteSpace(credentialsFile) && File.Exists(credentialsFile))
        {
            using FileStream stream = File.OpenRead(credentialsFile);
            ArchidektCredentials? fromFile = JsonSerializer.Deserialize<ArchidektCredentials>(stream, SerializerOptions);
            loaded.Jwt = FirstNonEmpty(loaded.Jwt, fromFile?.Jwt);
            loaded.RefreshToken = FirstNonEmpty(loaded.RefreshToken, fromFile?.RefreshToken);
            loaded.Username = FirstNonEmpty(loaded.Username, fromFile?.Username);
            loaded.Password = FirstNonEmpty(loaded.Password, fromFile?.Password);
        }

        credentials = loaded;
        return loaded;
    }

    private async Task EnsureAuthenticatedAsync(bool required, CancellationToken cancellationToken)
    {
        ArchidektCredentials loaded = LoadCredentials();
        if (!string.IsNullOrWhiteSpace(loaded.Jwt))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(options.AuthScheme, loaded.Jwt);
            return;
        }

        await authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(loaded.Jwt))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(options.AuthScheme, loaded.Jwt);
                return;
            }

            if (!string.IsNullOrWhiteSpace(loaded.RefreshToken))
            {
                string? refreshed = await TryRefreshJwtAsync(loaded.RefreshToken, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(refreshed))
                {
                    loaded.Jwt = refreshed;
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(options.AuthScheme, refreshed);
                    return;
                }
            }

            if (options.EnableUsernamePasswordLogin
                && !string.IsNullOrWhiteSpace(loaded.Username)
                && !string.IsNullOrWhiteSpace(loaded.Password))
            {
                string? jwt = await TryLoginAsync(loaded.Username, loaded.Password, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(jwt))
                {
                    loaded.Jwt = jwt;
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(options.AuthScheme, jwt);
                    return;
                }
            }

            if (required)
            {
                throw new InvalidOperationException("Archidekt credentials are required for this operation.");
            }
        }
        finally
        {
            authLock.Release();
        }
    }

    private async Task<string?> TryRefreshJwtAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using JsonDocument document = await SendJsonAsync(
                HttpMethod.Post,
                "api/rest-auth/token/refresh/",
                new { refresh = refreshToken },
                cancellationToken,
                authenticate: false).ConfigureAwait(false);

            return GetString(document.RootElement, "access")
                ?? GetString(document.RootElement, "token")
                ?? GetString(document.RootElement, "jwt");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<string?> TryLoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Post,
            "api/rest-auth/login/",
            new { username, password },
            cancellationToken,
            authenticate: false).ConfigureAwait(false);

        ArchidektCredentials loaded = LoadCredentials();
        loaded.RefreshToken = GetString(document.RootElement, "refresh") ?? loaded.RefreshToken;
        return GetString(document.RootElement, "access")
            ?? GetString(document.RootElement, "token")
            ?? GetString(document.RootElement, "key")
            ?? GetString(document.RootElement, "jwt");
    }
}
