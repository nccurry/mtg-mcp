using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns shared Archidekt HTTP, authentication, pacing, retry, and client-disposal behavior.
/// </summary>
internal sealed class ArchidektSession : IDisposable
{
    /// <summary>
    /// Sends provider requests through an injected or owned HTTP client.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Reports whether disposal owns the injected client.
    /// </summary>
    private readonly bool ownsHttpClient;

    /// <summary>
    /// Provides one process-local secret credential source.
    /// </summary>
    private readonly ArchidektCredentials credentials;

    /// <summary>
    /// Applies one shared account pacing timeline to every provider request.
    /// </summary>
    private readonly ArchidektRequestPacer pacer;

    /// <summary>
    /// Serializes login and one-time 401 refresh behavior.
    /// </summary>
    private readonly SemaphoreSlim authenticationGate = new(1, 1);

    /// <summary>
    /// Stores the current process-local bearer value only in memory.
    /// </summary>
    private string? token;

    /// <summary>
    /// Creates a production session with an honestly identified HTTP client.
    /// </summary>
    internal ArchidektSession(ArchidektOptions options, string packageVersion)
        : this(CreateHttpClient(options, packageVersion), ownsHttpClient: true, options)
    {
    }

    /// <summary>
    /// Creates a deterministic session over an injected HTTP client.
    /// </summary>
    internal ArchidektSession(
        HttpClient httpClient,
        bool ownsHttpClient,
        ArchidektOptions options,
        ArchidektRequestPacer? pacer = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        credentials = new ArchidektCredentials(options);
        this.pacer = pacer ?? new ArchidektRequestPacer(credentials.Load().PacingKey, options);
    }

    /// <summary>
    /// Gets redacted credential and session readiness without attempting login or provider I/O.
    /// </summary>
    internal ArchidektAuthStatus GetAuthStatus()
    {
        ArchidektCredentials.CredentialLoad loaded = credentials.Load();
        return new ArchidektAuthStatus(
            loaded.State,
            loaded.IsUsable,
            token is not null,
            loaded.Message);
    }

    /// <summary>
    /// Gets the configured provider username or raises the existing sanitized credential failure.
    /// </summary>
    internal string GetConfiguredUsername()
    {
        ArchidektCredentials.CredentialLoad loaded = credentials.Load();
        if (!loaded.IsUsable)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "credentials-unavailable",
                loaded.Message);
        }

        return loaded.Username!;
    }

    /// <summary>
    /// Sends one authenticated mutation when no response mapping is required.
    /// </summary>
    internal async Task SendMutationAsync(
        HttpMethod method,
        string path,
        object? payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        await SendAsync(
            method,
            path,
            payload,
            requiresAuthentication: true,
            idempotentRead: false,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends one request with shared pacing, one authenticated refresh, safe read retries, and fail-closed statuses.
    /// </summary>
    internal async Task<ProviderResponse> SendAsync(
        HttpMethod method,
        string path,
        object? payload,
        bool requiresAuthentication,
        bool idempotentRead,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        if (requiresAuthentication)
        {
            await EnsureAuthenticatedAsync(forceRefresh: false, budget, cancellationToken)
                .ConfigureAwait(false);
        }

        bool refreshed = false;
        int transientAttempt = 0;
        while (true)
        {
            await pacer.WaitForPermitAsync(budget, cancellationToken).ConfigureAwait(false);
            using HttpRequestMessage request = CreateRequest(method, path, payload, requiresAuthentication);
            using HttpResponseMessage response = await SendHttpAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && requiresAuthentication && !refreshed)
            {
                refreshed = true;
                await EnsureAuthenticatedAsync(forceRefresh: true, budget, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.RequestTimeout ||
                 (int)response.StatusCode >= 500) &&
                idempotentRead &&
                transientAttempt < 2)
            {
                transientAttempt++;
                await Task.Delay(TimeSpan.FromSeconds(transientAttempt), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            await ThrowForFailureAsync(response, cancellationToken).ConfigureAwait(false);
            string json = response.Content is null
                ? "{}"
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ProviderResponse(
                string.IsNullOrWhiteSpace(json) ? "{}" : json,
                DateTimeOffset.UtcNow);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        authenticationGate.Dispose();
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    /// <summary>
    /// Parses provider JSON or returns a fail-closed drift outcome.
    /// </summary>
    internal static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unsupported,
                "provider-contract-unsupported",
                "Archidekt returned malformed JSON.",
                exception);
        }
    }

    /// <summary>
    /// Sends the HTTP message while translating transport faults into a sanitized availability state.
    /// </summary>
    private async Task<HttpResponseMessage> SendHttpAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "provider-timeout",
                "Archidekt did not answer before the request timeout.");
        }
        catch (HttpRequestException exception)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "provider-unavailable",
                "Archidekt could not be reached.",
                exception);
        }
    }

    /// <summary>
    /// Maps provider status classes without retaining response bodies or guessing unsupported semantics.
    /// </summary>
    private async Task ThrowForFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if ((int)response.StatusCode == 429)
        {
            await pacer.ObserveRateLimitAsync(response.Headers.RetryAfter, cancellationToken)
                .ConfigureAwait(false);
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "provider-rate-limited",
                "Archidekt rate-limited the operation; no automatic retry was attempted.");
        }

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "authentication-failed",
                "Archidekt authentication failed."),
            HttpStatusCode.Forbidden => new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "provider-forbidden",
                "Archidekt refused the operation."),
            HttpStatusCode.NotFound => new ArchidektProviderException(
                ArchidektFailureKind.NotFound,
                "provider-entity-not-found",
                "The requested Archidekt entity was not found."),
            HttpStatusCode.BadRequest => new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "provider-request-rejected",
                "Archidekt rejected the request; the adapter did not infer why."),
            _ => new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "provider-unavailable",
                "Archidekt could not complete the operation."),
        };
    }

    /// <summary>
    /// Ensures one usable process-local login token, refreshing at most once after a 401.
    /// </summary>
    private async Task EnsureAuthenticatedAsync(
        bool forceRefresh,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && token is not null)
        {
            return;
        }

        await authenticationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && token is not null)
            {
                return;
            }

            token = null;
            ArchidektCredentials.CredentialLoad loaded = credentials.Load();
            if (!loaded.IsUsable)
            {
                throw new ArchidektProviderException(
                    ArchidektFailureKind.Unavailable,
                    "credentials-unavailable",
                    loaded.Message);
            }

            object payload = loaded.Username!.Contains('@', StringComparison.Ordinal)
                ? new { email = loaded.Username, password = loaded.Password }
                : new { username = loaded.Username, password = loaded.Password };
            await pacer.WaitForPermitAsync(budget, cancellationToken).ConfigureAwait(false);
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Post,
                "api/rest-auth/login/",
                payload,
                includeToken: false);
            using HttpResponseMessage response = await SendHttpAsync(request, cancellationToken)
                .ConfigureAwait(false);
            await ThrowForFailureAsync(response, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = ParseJson(json);
            token = ReadToken(document.RootElement)
                ?? throw new ArchidektProviderException(
                    ArchidektFailureKind.Unsupported,
                    "provider-contract-unsupported",
                    "Archidekt login no longer returns a recognized token field.");
        }
        finally
        {
            authenticationGate.Release();
        }
    }

    /// <summary>
    /// Creates one request message without placing credentials in a URI or serialized error.
    /// </summary>
    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        object? payload,
        bool includeToken)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (includeToken && token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("JWT", token);
        }

        if (payload is not null)
        {
            string json = JsonSerializer.Serialize(payload, ArchidektContract.JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>
    /// Creates the owned production client with an honest package user agent and bounded timeout.
    /// </summary>
    private static HttpClient CreateHttpClient(ArchidektOptions options, string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        string version = ArchidektContract.Required(packageVersion, nameof(packageVersion));
        HttpClient client = new()
        {
            BaseAddress = options.BaseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"mtg-mcp/{version}");
        return client;
    }

    /// <summary>
    /// Reads the current observed login token field plus older accepted field names.
    /// </summary>
    private static string? ReadToken(JsonElement root)
    {
        foreach (string name in new[] { "token", "access_token", "access", "jwt", "key" })
        {
            if (root.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Carries an exact provider body and the UTC time at which it was accepted.
    /// </summary>
    internal sealed record ProviderResponse(string Json, DateTimeOffset RetrievedAtUtc);
}
