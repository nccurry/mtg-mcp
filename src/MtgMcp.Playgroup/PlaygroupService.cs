using System.Globalization;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Playgroup;

/// <summary>
/// Exposes every pinned Playgroup public operation through validated provider-shaped evidence.
/// </summary>
public sealed class PlaygroupService : IDisposable
{
    /// <summary>Owns provider transport and authentication concerns.</summary>
    private readonly PlaygroupTransport transport;

    /// <summary>Creates a production service over one private configuration.</summary>
    public PlaygroupService(PlaygroupOptions options, string packageVersion)
    {
        transport = new PlaygroupTransport(
            options ?? throw new ArgumentNullException(nameof(options)),
            packageVersion);
    }

    /// <summary>Creates a deterministic service over an injected transport.</summary>
    internal PlaygroupService(PlaygroupTransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>Reports key readiness without provider I/O.</summary>
    public OperationResult<PlaygroupAuthStatus> GetAuthStatus()
    {
        return new OperationSuccess<PlaygroupAuthStatus>(transport.GetAuthStatus());
    }

    /// <summary>Gets the authenticated current user.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        return GetAsync("me", "getCurrentUser", requiresAuthentication: true, cancellationToken);
    }

    /// <summary>Gets one commander by exact provider identifier.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetCommanderAsync(
        int commanderId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"commanders/{PlaygroupContract.PositiveId(commanderId, nameof(commanderId))}",
            "getCommanderById",
            requiresAuthentication: false,
            cancellationToken));
    }

    /// <summary>Gets one commander through the provider's documented name lookup.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetCommanderByNameAsync(
        string name,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"commanders/by_name/{Uri.EscapeDataString(PlaygroupContract.Required(name, nameof(name)))}",
            "getCommanderByName",
            requiresAuthentication: false,
            cancellationToken));
    }

    /// <summary>Gets one caller-selected provider-computed commander turn-damage observation.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetCommanderTurnDamageAsync(
        int commanderId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            int selectedId = PlaygroupContract.PositiveId(commanderId, nameof(commanderId));
            PlaygroupEvidence evidence = await GetCoreAsync(
                "commanders/turn_damage",
                "getCommandersTurnDamage",
                requiresAuthentication: false,
                cancellationToken,
                PlaygroupTransport.MaximumTurnDamageResponseBytes).ConfigureAwait(false);
            return SelectCommanderTurnDamage(evidence, selectedId);
        });
    }

    /// <summary>Gets one provider deck, optionally including an archived deck.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetDeckAsync(
        int deckId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"decks/{PlaygroupContract.PositiveId(deckId, nameof(deckId))}?include_archived={Bool(includeArchived)}",
            "getDeckById",
            requiresAuthentication: false,
            cancellationToken));
    }

    /// <summary>Gets provider-computed ELO history for one deck and explicit optional scope.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetDeckEloHistoryAsync(
        int deckId,
        int? playgroupId,
        int? leagueId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (leagueId is not null && playgroupId is null)
            {
                throw Invalid("invalid-elo-scope", "leagueId requires playgroupId.");
            }

            List<KeyValuePair<string, string>> query =
            [
                new("include_archived", Bool(includeArchived)),
            ];
            AddOptionalId(query, "playgroup_id", playgroupId, nameof(playgroupId));
            AddOptionalId(query, "league_id", leagueId, nameof(leagueId));
            return GetCoreAsync(
                $"decks/{PlaygroupContract.PositiveId(deckId, nameof(deckId))}/elo_history{Query(query)}",
                "getDeckEloHistory",
                requiresAuthentication: false,
                cancellationToken);
        });
    }

    /// <summary>Gets one user by provider identifier.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetUserAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"users/{PlaygroupContract.PositiveId(userId, nameof(userId))}",
            "getUserById",
            requiresAuthentication: false,
            cancellationToken));
    }

    /// <summary>Lists a user's bounded provider deck collection.</summary>
    public Task<OperationResult<PlaygroupEvidence>> ListUserDecksAsync(
        int userId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"users/{PlaygroupContract.PositiveId(userId, nameof(userId))}/decks?include_archived={Bool(includeArchived)}",
            "listUserDecks",
            requiresAuthentication: false,
            cancellationToken));
    }

    /// <summary>Lists playgroups visible to the authenticated user.</summary>
    public Task<OperationResult<PlaygroupEvidence>> ListUserPlaygroupsAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"users/{PlaygroupContract.PositiveId(userId, nameof(userId))}/playgroups",
            "listUserPlaygroups",
            requiresAuthentication: true,
            cancellationToken));
    }

    /// <summary>Gets one authenticated user's playgroup relationship.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetUserPlaygroupAsync(
        int userId,
        int playgroupId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"users/{PlaygroupContract.PositiveId(userId, nameof(userId))}/playgroups/{PlaygroupContract.PositiveId(playgroupId, nameof(playgroupId))}",
            "getUserPlaygroup",
            requiresAuthentication: true,
            cancellationToken));
    }

    /// <summary>Lists members of one authenticated playgroup.</summary>
    public Task<OperationResult<PlaygroupEvidence>> ListPlaygroupMembersAsync(
        int playgroupId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"playgroups/{PlaygroupContract.PositiveId(playgroupId, nameof(playgroupId))}/members",
            "listPlaygroupMembers",
            requiresAuthentication: true,
            cancellationToken));
    }

    /// <summary>Lists one bounded provider page of playgroup games.</summary>
    public Task<OperationResult<PlaygroupEvidence>> ListPlaygroupGamesAsync(
        int playgroupId,
        int page,
        int limit,
        bool includeEvents,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (page <= 0 || limit is < 1 or > 100)
            {
                throw Invalid(
                    "invalid-page-boundary",
                    "page must be positive and limit must be between 1 and 100.");
            }

            string query = Query(
            [
                new("page", page.ToString(CultureInfo.InvariantCulture)),
                new("limit", limit.ToString(CultureInfo.InvariantCulture)),
                new("include_events", Bool(includeEvents)),
            ]);
            return GetCoreAsync(
                $"playgroups/{PlaygroupContract.PositiveId(playgroupId, nameof(playgroupId))}/games{query}",
                "listPlaygroupGames",
                requiresAuthentication: true,
                cancellationToken);
        });
    }

    /// <summary>Gets one playgroup game with optional event evidence.</summary>
    public Task<OperationResult<PlaygroupEvidence>> GetPlaygroupGameAsync(
        int playgroupId,
        int gameId,
        bool includeEvents,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(
            $"playgroups/{PlaygroupContract.PositiveId(playgroupId, nameof(playgroupId))}/games/{PlaygroupContract.PositiveId(gameId, nameof(gameId))}?include_events={Bool(includeEvents)}",
            "getPlaygroupGame",
            requiresAuthentication: true,
            cancellationToken));
    }

    /// <summary>Submits one caller-supplied event batch without automatic retries.</summary>
    public Task<OperationResult<PlaygroupEvidence>> CreateGameEventsBatchAsync(
        int gameId,
        IReadOnlyList<PlaygroupEventImport> events,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (events is null || events.Count is < 1 or > 1_000)
            {
                throw Invalid("invalid-event-batch", "events must contain between 1 and 1000 rows.");
            }

            PlaygroupEventImport[] normalized = new PlaygroupEventImport[events.Count];
            for (int index = 0; index < events.Count; index++)
            {
                PlaygroupEventImport value = events[index] ?? throw Invalid(
                    "invalid-event-batch",
                    "events cannot contain null rows.");
                normalized[index] = value with
                {
                    Name = PlaygroupContract.Required(value.Name, $"events[{index}].name"),
                    SourcePlayerId = PlayerId(value.SourcePlayerId, $"events[{index}].sourcePlayerId"),
                    TargetPlayerId = value.TargetPlayerId is null
                        ? null
                        : PlayerId(value.TargetPlayerId, $"events[{index}].targetPlayerId"),
                };
            }

            return SendCoreAsync(
                HttpMethod.Post,
                $"games/{PlaygroupContract.PositiveId(gameId, nameof(gameId))}/events/batch",
                "batchImportEvents",
                new { events = normalized },
                cancellationToken);
        });
    }

    /// <summary>Creates one caller-configured live session without monitoring or retries.</summary>
    public Task<OperationResult<PlaygroupEvidence>> CreateLiveSessionAsync(
        PlaygroupLiveSessionCreateRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            if (request is null)
            {
                throw Invalid("invalid-live-session", "request is required.");
            }
            if (request.PlayerAmount is < 1 or > 6 || request.LifeAmount <= 0 ||
                request.Bracket is not null and (< 1 or > 5))
            {
                throw Invalid(
                    "invalid-live-session",
                    "Player amount, life amount, or bracket is outside the documented boundary.");
            }

            if (request.LeagueId is not null && request.PlaygroupId is null)
            {
                throw Invalid("invalid-live-session-scope", "leagueId requires playgroupId.");
            }

            if (request.Discoverable && request.PlaygroupId is not null)
            {
                throw Invalid(
                    "invalid-live-session-visibility",
                    "A playgroup-private session cannot also be discoverable.");
            }

            int[] languages = request.LanguageIds?.ToArray() ?? [];
            if (languages.Length > 64 || languages.Any(value => value <= 0) ||
                languages.Distinct().Count() != languages.Length)
            {
                throw Invalid(
                    "invalid-language-ids",
                    "languageIds must contain at most 64 distinct positive identifiers.");
            }

            string? clientIdentifier = request.ClientIdentifier is null
                ? null
                : PlaygroupContract.Required(request.ClientIdentifier, nameof(request.ClientIdentifier));
            object payload = new
            {
                playerAmount = request.PlayerAmount,
                lifeAmount = request.LifeAmount,
                bracket = request.Bracket,
                playgroupId = OptionalPositiveId(request.PlaygroupId, nameof(request.PlaygroupId)),
                leagueId = OptionalPositiveId(request.LeagueId, nameof(request.LeagueId)),
                discoverable = request.Discoverable,
                languageIds = request.LanguageIds is null ? null : languages,
                clientIdentifier,
            };
            return SendCoreAsync(
                HttpMethod.Post,
                "live_sessions",
                "createLiveSession",
                payload,
                cancellationToken);
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        transport.Dispose();
    }

    /// <summary>Runs a validated GET through structured failure conversion.</summary>
    private Task<OperationResult<PlaygroupEvidence>> GetAsync(
        string path,
        string operationId,
        bool requiresAuthentication,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => GetCoreAsync(path, operationId, requiresAuthentication, cancellationToken));
    }

    /// <summary>Sends one idempotent GET without adding fan-out.</summary>
    private Task<PlaygroupEvidence> GetCoreAsync(
        string path,
        string operationId,
        bool requiresAuthentication,
        CancellationToken cancellationToken,
        int maximumResponseBytes = PlaygroupTransport.MaximumResponseBytes)
    {
        return transport.SendAsync(
            HttpMethod.Get,
            path,
            operationId,
            payload: null,
            requiresAuthentication,
            idempotentRead: true,
            cancellationToken,
            maximumResponseBytes);
    }

    /// <summary>Sends one non-idempotent documented write.</summary>
    private Task<PlaygroupEvidence> SendCoreAsync(
        HttpMethod method,
        string path,
        string operationId,
        object payload,
        CancellationToken cancellationToken)
    {
        return transport.SendAsync(
            method,
            path,
            operationId,
            payload,
            requiresAuthentication: true,
            idempotentRead: false,
            cancellationToken);
    }

    /// <summary>Returns one exact commander row from the provider's documented aggregate response.</summary>
    private static PlaygroupEvidence SelectCommanderTurnDamage(
        PlaygroupEvidence evidence,
        int commanderId)
    {
        if (evidence.Data.ValueKind != JsonValueKind.Array)
        {
            throw new PlaygroupProviderException(
                PlaygroupFailureKind.Unsupported,
                "provider-contract-unsupported",
                "Playgroup returned data that does not match the pinned JSON contract.");
        }

        JsonElement? match = null;
        foreach (JsonElement row in evidence.Data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object ||
                !row.TryGetProperty("id", out JsonElement id))
            {
                throw new PlaygroupProviderException(
                    PlaygroupFailureKind.Unsupported,
                    "provider-contract-unsupported",
                    "Playgroup returned data that does not match the pinned JSON contract.");
            }

            if (!id.TryGetInt32(out int rowId))
            {
                throw new PlaygroupProviderException(
                    PlaygroupFailureKind.Unsupported,
                    "provider-contract-unsupported",
                    "Playgroup returned data that does not match the pinned JSON contract.");
            }

            if (rowId != commanderId)
            {
                continue;
            }

            if (match is not null)
            {
                throw new PlaygroupProviderException(
                    PlaygroupFailureKind.Unsupported,
                    "provider-contract-unsupported",
                    "Playgroup returned duplicate commander observations.");
            }

            match = row.Clone();
        }

        if (match is null)
        {
            throw new PlaygroupProviderException(
                PlaygroupFailureKind.NotFound,
                "provider-entity-not-found",
                "Playgroup did not find turn-damage evidence for the requested commander.");
        }

        return evidence with
        {
            Limitations = Array.AsReadOnly<string>(
            [
                .. evidence.Limitations,
                "The provider returns all commanders; this result was selected by exact caller-supplied commander ID.",
            ]),
            Data = match.Value,
        };
    }

    /// <summary>Converts only expected provider failures into the shared exhaustive result union.</summary>
    private static async Task<OperationResult<PlaygroupEvidence>> ExecuteAsync(
        Func<Task<PlaygroupEvidence>> operation)
    {
        try
        {
            return new OperationSuccess<PlaygroupEvidence>(await operation().ConfigureAwait(false));
        }
        catch (PlaygroupProviderException exception)
        {
            return exception.Kind switch
            {
                PlaygroupFailureKind.InvalidInput => new OperationInvalidInput(
                    exception.ReasonCode,
                    exception.Message),
                PlaygroupFailureKind.NotFound => new OperationNotFound(
                    exception.ReasonCode,
                    exception.Message),
                PlaygroupFailureKind.Unsupported => new OperationUnsupported(
                    exception.ReasonCode,
                    exception.Message),
                PlaygroupFailureKind.Unavailable => new OperationUnavailable(
                    exception.ReasonCode,
                    exception.Message),
                _ => new OperationUnavailable(
                    "provider-unavailable",
                    "Playgroup could not satisfy the request."),
            };
        }
    }

    /// <summary>Formats a stable lowercase Boolean query value.</summary>
    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    /// <summary>Builds one escaped deterministic query string.</summary>
    private static string Query(IEnumerable<KeyValuePair<string, string>> values)
    {
        return "?" + string.Join("&", values.Select(value =>
            $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"));
    }

    /// <summary>Adds one validated optional identifier to a query.</summary>
    private static void AddOptionalId(
        ICollection<KeyValuePair<string, string>> query,
        string key,
        int? value,
        string parameterName)
    {
        if (value is not null)
        {
            query.Add(new KeyValuePair<string, string>(
                key,
                PlaygroupContract.PositiveId(value.Value, parameterName)
                    .ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>Validates one optional positive identifier.</summary>
    private static int? OptionalPositiveId(int? value, string parameterName)
    {
        return value is null ? null : PlaygroupContract.PositiveId(value.Value, parameterName);
    }

    /// <summary>Validates one documented player HUD index.</summary>
    private static string PlayerId(string value, string parameterName)
    {
        string normalized = PlaygroupContract.Required(value, parameterName, 1);
        return normalized is "0" or "1" or "2" or "3" or "4" or "5"
            ? normalized
            : throw Invalid("invalid-player-id", "Player IDs must be HUD indexes from 0 through 5.");
    }

    /// <summary>Creates one sanitized validation failure.</summary>
    private static PlaygroupProviderException Invalid(string reasonCode, string message)
    {
        return new PlaygroupProviderException(
            PlaygroupFailureKind.InvalidInput,
            reasonCode,
            message);
    }
}
