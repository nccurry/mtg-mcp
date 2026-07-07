using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Verifies every pinned operation route, query, validation boundary, and write payload.
/// </summary>
public sealed class PlaygroupServiceTests
{
    /// <summary>Verifies all thirteen GET operations map one-to-one without hidden fan-out.</summary>
    [Fact]
    public async Task ReadOperations_MapEveryPinnedRouteExactlyOnce()
    {
        PlaygroupTestHttpHandler handler = new();
        for (int index = 0; index < 13; index++)
        {
            handler.AddJson(index == 3
                ? "[{\"id\":7,\"average_turn_data\":[],\"extension\":null}]"
                : $"{{\"index\":{index},\"extension\":null}}");
        }

        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);
        CancellationToken token = TestContext.Current.CancellationToken;
        List<OperationResult<PlaygroupEvidence>> results =
        [
            await service.GetCurrentUserAsync(token),
            await service.GetCommanderAsync(7, token),
            await service.GetCommanderByNameAsync("Y'shtola, Night's Blessed", token),
            await service.GetCommanderTurnDamageAsync(7, token),
            await service.GetDeckAsync(8, includeArchived: true, token),
            await service.GetDeckEloHistoryAsync(8, 2, 3, includeArchived: true, token),
            await service.GetUserAsync(9, token),
            await service.ListUserDecksAsync(9, includeArchived: false, token),
            await service.ListUserPlaygroupsAsync(9, token),
            await service.GetUserPlaygroupAsync(9, 2, token),
            await service.ListPlaygroupMembersAsync(2, token),
            await service.ListPlaygroupGamesAsync(2, 3, 100, includeEvents: true, token),
            await service.GetPlaygroupGameAsync(2, 11, includeEvents: true, token),
        ];

        string[] expectedPaths =
        [
            "/api/public/v1/me",
            "/api/public/v1/commanders/7",
            "/api/public/v1/commanders/by_name/Y%27shtola%2C%20Night%27s%20Blessed",
            "/api/public/v1/commanders/turn_damage",
            "/api/public/v1/decks/8?include_archived=true",
            "/api/public/v1/decks/8/elo_history?include_archived=true&playgroup_id=2&league_id=3",
            "/api/public/v1/users/9",
            "/api/public/v1/users/9/decks?include_archived=false",
            "/api/public/v1/users/9/playgroups",
            "/api/public/v1/users/9/playgroups/2",
            "/api/public/v1/playgroups/2/members",
            "/api/public/v1/playgroups/2/games?page=3&limit=100&include_events=true",
            "/api/public/v1/playgroups/2/games/11?include_events=true",
        ];
        string[] expectedOperationIds =
        [
            "getCurrentUser",
            "getCommanderById",
            "getCommanderByName",
            "getCommandersTurnDamage",
            "getDeckById",
            "getDeckEloHistory",
            "getUserById",
            "listUserDecks",
            "listUserPlaygroups",
            "getUserPlaygroup",
            "listPlaygroupMembers",
            "listPlaygroupGames",
            "getPlaygroupGame",
        ];
        Assert.Equal(expectedPaths, handler.Requests.Select(value => value.PathAndQuery));
        Assert.All(handler.Requests, request => Assert.Equal("Bearer", request.AuthScheme));
        Assert.All(handler.Requests, request => Assert.Equal("test-key", request.AuthParameter));
        for (int index = 0; index < results.Count; index++)
        {
            PlaygroupEvidence evidence = Success(results[index]);
            Assert.Equal(expectedOperationIds[index], evidence.OperationId);
            if (index == 3)
            {
                Assert.Equal(7, evidence.Data.GetProperty("id").GetInt32());
            }
            else
            {
                Assert.Equal(index, evidence.Data.GetProperty("index").GetInt32());
            }

            Assert.Equal(JsonValueKind.Null, evidence.Data.GetProperty("extension").ValueKind);
            Assert.Equal("1.0.0", evidence.ApiVersion);
            Assert.Equal(PlaygroupContract.OpenApiChecksum, evidence.ContractChecksum);
            Assert.Equal(64, evidence.SourceChecksum.Length);
            Assert.Equal(TimeSpan.Zero, evidence.RetrievedAtUtc.Offset);
            Assert.Contains(evidence.Limitations, value => value.Contains("deck updates", StringComparison.Ordinal));
        }
    }

    /// <summary>Verifies the large aggregate endpoint returns only the exact caller-selected provider row.</summary>
    [Fact]
    public async Task TurnDamageSelection_BoundsAggregateAndReturnsExactRow()
    {
        PlaygroupTestHttpHandler handler = new();
        string padding = new('x', PlaygroupTransport.MaximumResponseBytes);
        handler.AddJson(
            $"[{{\"id\":1,\"padding\":\"{padding}\"}},{{\"id\":7,\"average_turn_data\":[{{\"turn\":3,\"damage\":2.5}}]}}]");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        PlaygroupEvidence evidence = Success(await service.GetCommanderTurnDamageAsync(
            7,
            TestContext.Current.CancellationToken));

        Assert.Equal(7, evidence.Data.GetProperty("id").GetInt32());
        Assert.False(evidence.Data.TryGetProperty("padding", out _));
        Assert.Contains(evidence.Limitations, value => value.Contains("caller-supplied", StringComparison.Ordinal));
        Assert.Single(handler.Requests);
    }

    /// <summary>Verifies an absent commander row returns not-found evidence after one aggregate request.</summary>
    [Fact]
    public async Task TurnDamageSelection_MissingCommander_ReturnsNotFound()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddJson("[{\"id\":1,\"average_turn_data\":[]}]");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.GetCommanderTurnDamageAsync(
            7,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationNotFound>(result.Value);
        Assert.Single(handler.Requests);
    }

    /// <summary>Verifies both writes preserve documented snake-case fields and use one POST each.</summary>
    [Fact]
    public async Task WriteOperations_SendOneDocumentedPayloadEach()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddJson("{\"status\":\"success\",\"imported_count\":1,\"events\":[]}");
        handler.AddJson("{\"url\":\"https://playgroup.gg/live_sessions/test\"}");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);
        using JsonDocument metadata = JsonDocument.Parse("{\"tag\":\"good_sport\"}");

        OperationResult<PlaygroupEvidence> batch = await service.CreateGameEventsBatchAsync(
            31,
            [new PlaygroupEventImport(
                "Endorsement",
                "0",
                Id: 12,
                TargetPlayerId: "1",
                Time: 100,
                Turn: 4,
                Amount: 1,
                CommanderId: 3186,
                Metadata: metadata.RootElement)],
            TestContext.Current.CancellationToken);
        OperationResult<PlaygroupEvidence> session = await service.CreateLiveSessionAsync(
            new PlaygroupLiveSessionCreateRequest(
                PlayerAmount: 4,
                LifeAmount: 40,
                Bracket: 3,
                PlaygroupId: 2,
                LeagueId: 5,
                Discoverable: false,
                LanguageIds: [1, 2],
                ClientIdentifier: "mtg-mcp-test"),
            TestContext.Current.CancellationToken);

        Assert.Equal("batchImportEvents", Success(batch).OperationId);
        Assert.Equal("createLiveSession", Success(session).OperationId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Post, request.Method));
        Assert.Equal("/api/public/v1/games/31/events/batch", handler.Requests[0].PathAndQuery);
        Assert.Equal("/api/public/v1/live_sessions", handler.Requests[1].PathAndQuery);
        Assert.Contains("\"source_player_id\":\"0\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"commander_id\":3186", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("\"player_amount\":4", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("\"language_ids\":[1,2]", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    /// <summary>Verifies absent optional provider fields are omitted rather than sent as explicit nulls.</summary>
    [Fact]
    public async Task WriteSerialization_OmitsAbsentOptionalFields()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddJson("{\"url\":\"https://playgroup.gg/live_sessions/test\"}");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.CreateLiveSessionAsync(
            new PlaygroupLiveSessionCreateRequest(),
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationSuccess<PlaygroupEvidence>>(result.Value);
        string body = Assert.Single(handler.Requests).Body!;
        Assert.DoesNotContain("bracket", body, StringComparison.Ordinal);
        Assert.DoesNotContain("playgroup_id", body, StringComparison.Ordinal);
        Assert.DoesNotContain("language_ids", body, StringComparison.Ordinal);
        Assert.DoesNotContain("client_identifier", body, StringComparison.Ordinal);
    }

    /// <summary>Verifies invalid read parameters fail before provider I/O.</summary>
    [Theory]
    [InlineData("commander-id")]
    [InlineData("turn-damage-commander-id")]
    [InlineData("commander-name")]
    [InlineData("elo-scope")]
    [InlineData("game-page")]
    [InlineData("game-limit")]
    [InlineData("playgroup-id")]
    public async Task ReadValidation_RejectsInvalidInputBeforeHttp(string scenario)
    {
        PlaygroupTestHttpHandler handler = new();
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);
        CancellationToken token = TestContext.Current.CancellationToken;
        OperationResult<PlaygroupEvidence> result = scenario switch
        {
            "commander-id" => await service.GetCommanderAsync(0, token),
            "turn-damage-commander-id" => await service.GetCommanderTurnDamageAsync(0, token),
            "commander-name" => await service.GetCommanderByNameAsync(" ", token),
            "elo-scope" => await service.GetDeckEloHistoryAsync(1, null, 2, false, token),
            "game-page" => await service.ListPlaygroupGamesAsync(1, 0, 10, false, token),
            "game-limit" => await service.ListPlaygroupGamesAsync(1, 1, 101, false, token),
            "playgroup-id" => await service.GetUserPlaygroupAsync(1, -1, token),
            _ => throw new Xunit.Sdk.XunitException("Unknown validation scenario."),
        };

        Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Verifies malformed event batches fail before any remote mutation.</summary>
    [Fact]
    public async Task EventValidation_RejectsMalformedBatchesBeforeHttp()
    {
        PlaygroupTestHttpHandler handler = new();
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);
        CancellationToken token = TestContext.Current.CancellationToken;

        Assert.IsType<OperationInvalidInput>((await service.CreateGameEventsBatchAsync(1, [], token)).Value);
        Assert.IsType<OperationInvalidInput>((await service.CreateGameEventsBatchAsync(1, null!, token)).Value);
        Assert.IsType<OperationInvalidInput>((await service.CreateGameEventsBatchAsync(
            1,
            [new PlaygroupEventImport(" ", "0")],
            token)).Value);
        Assert.IsType<OperationInvalidInput>((await service.CreateGameEventsBatchAsync(
            1,
            [new PlaygroupEventImport("Damage", "6")],
            token)).Value);
        Assert.IsType<OperationInvalidInput>((await service.CreateGameEventsBatchAsync(
            1,
            [new PlaygroupEventImport("Damage", "0", TargetPlayerId: "bad")],
            token)).Value);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Verifies malformed session combinations fail before remote creation.</summary>
    [Theory]
    [InlineData("players")]
    [InlineData("bracket")]
    [InlineData("league")]
    [InlineData("visibility")]
    [InlineData("languages")]
    [InlineData("client")]
    public async Task LiveSessionValidation_RejectsInvalidInputBeforeHttp(string scenario)
    {
        PlaygroupTestHttpHandler handler = new();
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);
        PlaygroupLiveSessionCreateRequest request = scenario switch
        {
            "players" => new(PlayerAmount: 7),
            "bracket" => new(Bracket: 6),
            "league" => new(LeagueId: 2),
            "visibility" => new(PlaygroupId: 1, Discoverable: true),
            "languages" => new(LanguageIds: [1, 1]),
            "client" => new(ClientIdentifier: " "),
            _ => throw new Xunit.Sdk.XunitException("Unknown validation scenario."),
        };

        OperationResult<PlaygroupEvidence> result = await service.CreateLiveSessionAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Verifies an absent session request is a structured caller error.</summary>
    [Fact]
    public async Task LiveSessionValidation_RejectsNullRequestBeforeHttp()
    {
        PlaygroupTestHttpHandler handler = new();
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupEvidence> result = await service.CreateLiveSessionAsync(
            null!,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Verifies redacted auth status and production construction perform no provider call.</summary>
    [Fact]
    public void AuthStatus_IsLocalAndRedacted()
    {
        PlaygroupTestHttpHandler handler = new();
        using PlaygroupService configured = PlaygroupTestFactory.CreateService(handler);
        using PlaygroupService missing = PlaygroupTestFactory.CreateService(new PlaygroupTestHttpHandler(), null);
        using PlaygroupService production = new(PlaygroupOptions.CreateDefault(null), "0.9.0-preview.1");

        PlaygroupAuthStatus available = Assert.IsType<OperationSuccess<PlaygroupAuthStatus>>(
            configured.GetAuthStatus().Value).Data;
        PlaygroupAuthStatus unavailable = Assert.IsType<OperationSuccess<PlaygroupAuthStatus>>(
            missing.GetAuthStatus().Value).Data;
        Assert.True(available.CredentialsConfigured);
        Assert.Equal("configured", available.State);
        Assert.False(unavailable.CredentialsConfigured);
        Assert.Equal("not-configured", unavailable.State);
        Assert.DoesNotContain("test-key", available.Message, StringComparison.Ordinal);
        Assert.IsType<OperationSuccess<PlaygroupAuthStatus>>(production.GetAuthStatus().Value);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Extracts one successful provider observation.</summary>
    private static PlaygroupEvidence Success(OperationResult<PlaygroupEvidence> result)
    {
        return Assert.IsType<OperationSuccess<PlaygroupEvidence>>(result.Value).Data;
    }
}
