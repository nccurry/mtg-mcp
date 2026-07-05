using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns authentication, HTTP, conservative retries, pacing, and the observed Archidekt route contract.
/// </summary>
internal sealed class ArchidektTransport : IDisposable
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
    /// Provides validated provider and safety configuration.
    /// </summary>
    private readonly ArchidektOptions options;

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
    /// Creates a production transport with an honestly identified HTTP client.
    /// </summary>
    internal ArchidektTransport(ArchidektOptions options, string packageVersion)
        : this(CreateHttpClient(options, packageVersion), ownsHttpClient: true, options)
    {
    }

    /// <summary>
    /// Creates a deterministic transport over an injected HTTP client.
    /// </summary>
    internal ArchidektTransport(
        HttpClient httpClient,
        bool ownsHttpClient,
        ArchidektOptions options,
        ArchidektRequestPacer? pacer = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.ownsHttpClient = ownsHttpClient;
        this.options = options ?? throw new ArgumentNullException(nameof(options));
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
    /// Fetches one authenticated page of the configured user's decks.
    /// </summary>
    internal async Task<RemoteDeckPage> ListDecksAsync(
        string? cursor,
        int pageSize,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        (int offset, string? expectedChecksum) = ParseDeckListCursor(cursor, pageSize);
        ArchidektCredentials.CredentialLoad loaded = credentials.Load();
        if (!loaded.IsUsable)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unavailable,
                "credentials-unavailable",
                loaded.Message);
        }

        string path = $"api/decks/v3/?ownerUsername={Uri.EscapeDataString(loaded.Username!)}";
        ProviderResponse response = await SendAsync(
            HttpMethod.Get,
            path,
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        RemoteDeckPage complete = ArchidektContractMapper.MapDeckPage(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/v3/");
        if (expectedChecksum is not null && !string.Equals(
                expectedChecksum,
                complete.Evidence.SourceChecksum,
                StringComparison.Ordinal))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Conflict,
                "deck-list-changed",
                "The Archidekt deck list changed after the previous page.");
        }

        if (offset > complete.Items.Count)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-deck-list-cursor",
                "The Archidekt deck-list cursor is invalid.");
        }

        RemoteDeckSummary[] items = complete.Items.Skip(offset).Take(pageSize).ToArray();
        int nextOffset = offset + items.Length;
        string? nextCursor = nextOffset < complete.Items.Count
            ? FormatDeckListCursor(nextOffset, complete.Evidence.SourceChecksum)
            : null;
        return new RemoteDeckPage(items, nextCursor, complete.Evidence);
    }

    /// <summary>
    /// Fetches one public or private deck through the observed detail route.
    /// </summary>
    internal async Task<RemoteDeckSnapshot> GetDeckAsync(
        string deckId,
        bool requireAuthentication,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        ProviderResponse response = await SendAsync(
            HttpMethod.Get,
            $"api/decks/{Uri.EscapeDataString(deckId)}/",
            payload: null,
            requireAuthentication,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        return ArchidektContractMapper.MapDeck(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/{deckId}/");
    }

    /// <summary>
    /// Creates one private-by-default empty deck shell.
    /// </summary>
    internal async Task<RemoteDeckSnapshot> CreateDeckAsync(
        ArchidektDeckCreateRequest request,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = ArchidektContract.Required(request.Name, nameof(request.Name));
        string visibility = NormalizeVisibility(request.Visibility);
        object payload = new
        {
            name,
            description = ArchidektContract.Optional(request.Description),
            deckFormat = MapFormatId(request.Format),
            edhBracket = (int?)null,
            parentFolder = ParseProviderId(request.ParentFolderId),
            @private = visibility == "private",
            unlisted = visibility == "unlisted",
            theorycrafted = false,
            game = (string?)null,
            cardPackage = (string?)null,
            extras = new
            {
                decksToInclude = Array.Empty<int>(),
                commandersToAdd = Array.Empty<int>(),
                forceCardsToSingleton = false,
                ignoreCardsOutOfCommanderIdentity = true,
            },
        };
        ProviderResponse response = await SendAsync(
            HttpMethod.Post,
            "api/decks/v2/",
            payload,
            requiresAuthentication: true,
            idempotentRead: false,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        return ArchidektContractMapper.MapDeck(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "POST /api/decks/v2/");
    }

    /// <summary>
    /// Deletes one exact deck through the observed provider route without retrying ambiguous failure.
    /// </summary>
    internal async Task DeleteDeckAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        await SendAsync(
            HttpMethod.Delete,
            $"api/decks/{Uri.EscapeDataString(deckId)}/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: false,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates caller-selected deck metadata fields through one primitive mutation.
    /// </summary>
    internal Task SendDeckMetadataAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Patch,
            $"api/decks/{Uri.EscapeDataString(deckId)}/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Creates one provider category using an explicit deck ID.
    /// </summary>
    internal Task SendCategoryCreateAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Post,
            "api/decks/createCategory/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Updates one exact provider category.
    /// </summary>
    internal Task SendCategoryUpdateAsync(
        string categoryId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Patch,
            $"api/decks/category/{Uri.EscapeDataString(categoryId)}/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Deletes one exact provider category without retrying ambiguous failure.
    /// </summary>
    internal Task SendCategoryDeleteAsync(
        string categoryId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Delete,
            $"api/decks/category/{Uri.EscapeDataString(categoryId)}/",
            payload: null,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Sends exactly one card-relation mutation through the observed v2 route.
    /// </summary>
    internal Task SendCardMutationAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Patch,
            $"api/decks/{Uri.EscapeDataString(deckId)}/modifyCards/v2/",
            new { cards = new[] { payload } },
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Resolves one exact Archidekt printing ID for a new relation without fuzzy fallback.
    /// </summary>
    internal async Task<string> ResolveCardIdAsync(
        RemoteDeckEntry entry,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        string path = $"api/cards/v2/?name={Uri.EscapeDataString(entry.CardName)}&pageSize=25";
        ProviderResponse response = await SendAsync(
            HttpMethod.Get,
            path,
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        List<JsonElement> candidates = [];
        JsonElement root = document.RootElement;
        JsonElement collection = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("results", out JsonElement results)
                ? results
                : default;
        if (collection.ValueKind == JsonValueKind.Array)
        {
            candidates.AddRange(collection.EnumerateArray());
        }

        JsonElement? match = SelectExactCard(candidates, entry);
        if (match is null || !TryReadId(match.Value, "id", out string? cardId))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unsupported,
                "printing-resolution-unavailable",
                "Archidekt could not resolve the exact requested printing.");
        }

        return cardId!;
    }

    /// <summary>
    /// Fetches the complete authenticated folder tree.
    /// </summary>
    internal async Task<RemoteFolderTree> ListFoldersAsync(
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ProviderResponse response = await SendAsync(
            HttpMethod.Get,
            "api/decks/folderTree/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        return ArchidektContractMapper.MapFolderTree(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/folderTree/");
    }

    /// <summary>
    /// Fetches one authenticated folder detail.
    /// </summary>
    internal async Task<RemoteFolderTree> GetFolderAsync(
        string folderId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        folderId = ArchidektContract.Required(folderId, nameof(folderId));
        ProviderResponse response = await SendAsync(
            HttpMethod.Get,
            $"api/decks/folders/{Uri.EscapeDataString(folderId)}/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        return ArchidektContractMapper.MapFolderDetail(
            document.RootElement,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/folders/{folderId}/");
    }

    /// <summary>
    /// Creates one folder with explicit visibility and parent identity.
    /// </summary>
    internal async Task<string> CreateFolderAsync(
        ArchidektFolderCreateRequest request,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        ProviderResponse response = await SendAsync(
            HttpMethod.Post,
            "api/decks/folders/",
            new
            {
                name = ArchidektContract.Required(request.Name, nameof(request.Name)),
                @private = NormalizeVisibility(request.Visibility) == "private",
                parent_folder = ParseProviderId(request.ParentFolderId),
            },
            requiresAuthentication: true,
            idempotentRead: false,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        JsonElement root = document.RootElement;
        string? folderId = root.ValueKind switch
        {
            JsonValueKind.String => root.GetString(),
            JsonValueKind.Number => root.GetRawText(),
            JsonValueKind.Object when TryReadId(root, "id", out string? id) => id,
            _ => null,
        };
        return !string.IsNullOrWhiteSpace(folderId)
            ? folderId
            : throw new ArchidektProviderException(
                ArchidektFailureKind.Unsupported,
                "provider-contract-unsupported",
                "Archidekt did not return the created folder identity.");
    }

    /// <summary>
    /// Updates one folder through the observed detail route.
    /// </summary>
    internal Task SendFolderUpdateAsync(
        string folderId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Patch,
            "api/massUpdate/",
            new
            {
                items = new[]
                {
                    new
                    {
                        type = "folder",
                        id = ParseProviderId(folderId),
                        patch = payload,
                    },
                },
            },
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Moves exact typed items through the observed mass-update route.
    /// </summary>
    internal Task SendFolderMoveAsync(
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Patch,
            "api/massUpdate/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Deletes exactly one preflighted empty folder through the observed item-delete route.
    /// </summary>
    internal Task SendFolderDeleteAsync(
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Post,
            "api/decks/folders/deleteItems/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Fetches named snapshot metadata for one exact deck.
    /// </summary>
    internal async Task<RemoteNamedSnapshotPage> ListSnapshotsAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        ProviderResponse response = await SendAsync(
            HttpMethod.Get,
            $"api/decks/{Uri.EscapeDataString(deckId)}/snapshots/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        return ArchidektContractMapper.MapSnapshotPage(
            document.RootElement,
            deckId,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/{deckId}/snapshots/");
    }

    /// <summary>
    /// Fetches one complete named snapshot and its saved deck state.
    /// </summary>
    internal async Task<RemoteNamedSnapshot> GetSnapshotAsync(
        string deckId,
        string snapshotId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        deckId = ArchidektContract.Required(deckId, nameof(deckId));
        snapshotId = ArchidektContract.Required(snapshotId, nameof(snapshotId));
        ProviderResponse response = await SendAsync(
            HttpMethod.Get,
            $"api/decks/snapshots/{Uri.EscapeDataString(snapshotId)}/",
            payload: null,
            requiresAuthentication: true,
            idempotentRead: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ParseJson(response.Json);
        return ArchidektContractMapper.MapSnapshot(
            document.RootElement,
            deckId,
            response.Json,
            response.RetrievedAtUtc,
            "GET /api/decks/snapshots/{snapshotId}/");
    }

    /// <summary>
    /// Creates one named snapshot through the observed deck-scoped route.
    /// </summary>
    internal Task SendSnapshotCreateAsync(
        string deckId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Post,
            $"api/decks/{Uri.EscapeDataString(deckId)}/snapshots/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Updates one named snapshot's supported metadata.
    /// </summary>
    internal Task SendSnapshotUpdateAsync(
        string snapshotId,
        object payload,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Patch,
            $"api/decks/snapshots/{Uri.EscapeDataString(snapshotId)}/",
            payload,
            budget,
            cancellationToken);
    }

    /// <summary>
    /// Deletes one exact named snapshot without retrying ambiguous failure.
    /// </summary>
    internal Task SendSnapshotDeleteAsync(
        string snapshotId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        return SendWithoutResultAsync(
            HttpMethod.Delete,
            $"api/decks/snapshots/{Uri.EscapeDataString(snapshotId)}/",
            payload: null,
            budget,
            cancellationToken);
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
    /// Sends one authenticated mutation when no response mapping is required.
    /// </summary>
    private async Task SendWithoutResultAsync(
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
    private async Task<ProviderResponse> SendAsync(
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
    /// Parses provider JSON or returns a fail-closed drift outcome.
    /// </summary>
    private static JsonDocument ParseJson(string json)
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
    /// Builds one bounded authenticated list route without accepting an arbitrary URL.
    /// </summary>
    private static (int Offset, string? ExpectedChecksum) ParseDeckListCursor(
        string? cursor,
        int pageSize)
    {
        if (pageSize is < 1 or > 100)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-page-size",
                "Archidekt deck page size must be between 1 and 100.");
        }

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (0, null);
        }

        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor.Trim()));
            DeckListCursor? parsed = JsonSerializer.Deserialize<DeckListCursor>(
                json,
                ArchidektContract.JsonOptions);
            if (parsed is null || parsed.Offset <= 0 || parsed.Checksum.Length != 64)
            {
                throw new FormatException("Invalid cursor payload.");
            }

            return (parsed.Offset, parsed.Checksum);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-deck-list-cursor",
                "The Archidekt deck-list cursor is invalid.");
        }
    }

    /// <summary>
    /// Creates one opaque offset bound to the exact provider list bytes observed on the first page.
    /// </summary>
    private static string FormatDeckListCursor(int offset, string checksum)
    {
        string json = JsonSerializer.Serialize(
            new DeckListCursor(offset, checksum),
            ArchidektContract.JsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Maps the supported local format vocabulary to Archidekt's observed numeric IDs.
    /// </summary>
    internal static int MapFormatId(string value)
    {
        return ArchidektContract.Required(value, nameof(value)).ToLowerInvariant() switch
        {
            "standard" => 1,
            "modern" => 2,
            "commander" or "edh" => 3,
            "legacy" => 4,
            "vintage" => 5,
            "pauper" => 6,
            "pioneer" => 7,
            "brawl" => 8,
            "historic" => 9,
            "oathbreaker" => 10,
            _ => throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "unsupported-deck-format",
                "The requested deck format is not mapped to Archidekt."),
        };
    }

    /// <summary>
    /// Accepts only the explicit provider visibility vocabulary.
    /// </summary>
    private static string NormalizeVisibility(string value)
    {
        return ArchidektContract.Required(value, nameof(value)).ToLowerInvariant() switch
        {
            "private" => "private",
            "unlisted" => "unlisted",
            "public" => "public",
            _ => throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-visibility",
                "Archidekt visibility must be private, unlisted, or public."),
        };
    }

    /// <summary>
    /// Preserves numeric provider IDs as numbers and all other explicit IDs as strings.
    /// </summary>
    internal static object? ParseProviderId(string? value)
    {
        string? normalized = ArchidektContract.Optional(value);
        return long.TryParse(normalized, out long number) ? number : normalized;
    }

    /// <summary>
    /// Carries an exact provider body and the UTC time at which it was accepted.
    /// </summary>
    private sealed record ProviderResponse(string Json, DateTimeOffset RetrievedAtUtc);

    /// <summary>
    /// Carries a local page offset and immutable source checksum inside the opaque continuation.
    /// </summary>
    private sealed record DeckListCursor(int Offset, string Checksum);

    /// <summary>
    /// Selects one exact name and printing match from a bounded provider card search.
    /// </summary>
    private static JsonElement? SelectExactCard(
        IReadOnlyList<JsonElement> candidates,
        RemoteDeckEntry entry)
    {
        List<JsonElement> nameMatches = [];
        foreach (JsonElement candidate in candidates)
        {
            string? name = candidate.TryGetProperty("oracleCard", out JsonElement oracle) &&
                oracle.ValueKind == JsonValueKind.Object &&
                oracle.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString()
                : null;
            if (!string.Equals(name, entry.CardName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.PrintingId is not null &&
                candidate.TryGetProperty("uid", out JsonElement uid) &&
                Guid.TryParse(uid.GetString(), out Guid candidateId) &&
                candidateId == entry.PrintingId)
            {
                return candidate;
            }

            string? setCode = candidate.TryGetProperty("setCode", out JsonElement set)
                ? set.GetString()
                : null;
            string? collectorNumber = candidate.TryGetProperty("collectorNumber", out JsonElement collector)
                ? collector.GetString()
                : null;
            if (entry.SetCode is not null && entry.CollectorNumber is not null &&
                string.Equals(setCode, entry.SetCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(collectorNumber, entry.CollectorNumber, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            nameMatches.Add(candidate);
        }

        return entry.PrintingId is null && entry.SetCode is null && nameMatches.Count == 1
            ? nameMatches[0]
            : null;
    }

    /// <summary>
    /// Reads a provider string-or-number identity without exposing its containing payload.
    /// </summary>
    private static bool TryReadId(JsonElement value, string propertyName, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        result = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
        return !string.IsNullOrWhiteSpace(result);
    }
}
