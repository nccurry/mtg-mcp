using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Contains tests for Playgroup API gateway behavior.
/// </summary>
public sealed class PlaygroupGatewayTests
{
    /// <summary>
    /// Verifies that required-auth requests fail before sending without an API key.
    /// </summary>
    [Fact]
    public async Task RequiredAuth_ThrowsWithoutApiKey()
    {
        RecordingHandler handler = new();
        PlaygroupGateway gateway = CreateGateway(handler, new PlaygroupOptions());

        Func<Task> act = () => gateway.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Playgroup API key*");
        handler.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that current-user requests send bearer auth and map the response.
    /// </summary>
    [Fact]
    public async Task GetCurrentUser_SendsBearerApiKeyAndMapsUser()
    {
        RecordingHandler handler = new();
        handler.Get("me", """{ "id": 42, "username": "chase" }""");
        PlaygroupGateway gateway = CreateGateway(handler);

        PlaygroupUser user = await gateway.GetCurrentUserAsync(
            TestContext.Current.CancellationToken
        );

        user.Id.Should().Be(42);
        user.Username.Should().Be("chase");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Authorization.Should().Be("Bearer test-api-key");
    }

    /// <summary>
    /// Verifies playgroup summaries include source counts, timestamps, and embedded leagues.
    /// </summary>
    [Fact]
    public async Task GetUserPlaygroup_MapsSummaryAndLeagues()
    {
        RecordingHandler handler = new();
        handler.Get(
            "users/10/playgroups/20",
            """
            {
              "id": 20,
              "name": "Friday Night",
              "game_count": 12,
              "member_count": 5,
              "created_at": "2026-05-01T00:00:00Z",
              "leagues": [
                { "id": 30, "name": "Summer", "active": true },
                { "id": 31, "name": "Archive", "active": false }
              ]
            }
            """);

        PlaygroupSummary summary = await CreateGateway(handler).GetUserPlaygroupAsync(
            10,
            20,
            TestContext.Current.CancellationToken);

        summary.Name.Should().Be("Friday Night");
        summary.GameCount.Should().Be(12);
        summary.MemberCount.Should().Be(5);
        summary.CreatedAt.Should().Be(DateTimeOffset.Parse("2026-05-01T00:00:00Z", CultureInfo.InvariantCulture));
        summary.Leagues.Should().HaveCount(2);
        summary.Leagues[0].Active.Should().BeTrue();
    }

    /// <summary>
    /// Verifies public deck reads do not require credentials and map absent optional fields safely.
    /// </summary>
    [Fact]
    public async Task GetDeck_WithoutCredentials_MapsEmptyResponse()
    {
        RecordingHandler handler = new();
        handler.Get("decks/0", "");
        PlaygroupGateway gateway = CreateGateway(handler, new PlaygroupOptions
        {
            BaseAddress = new Uri("https://playgroup.test/api/public/v1/")
        });

        PlaygroupDeck deck = await gateway.GetDeckAsync(0, TestContext.Current.CancellationToken);

        deck.Id.Should().Be(0);
        deck.Name.Should().BeEmpty();
        deck.Commander.Should().BeNull();
        deck.Partner.Should().BeNull();
        handler.Requests.Should().ContainSingle().Which.Authorization.Should().BeNull();
    }

    /// <summary>
    /// Verifies that playgroup game listing maps participation fields.
    /// </summary>
    [Fact]
    public async Task ListPlaygroupGames_MapsParticipations()
    {
        RecordingHandler handler = new();
        handler.Get(
            "playgroups/49295/games?page=2&limit=50&include_events=false",
            """
            [
              {
                "id": 9001,
                "playgroup_id": 49295,
                "total_rounds": 8,
                "started_at": "2026-05-20T01:00:00Z",
                "ended_at": "2026-05-20T02:00:00Z",
                "win_con": "combat_damage",
                "participations": [
                  {
                    "id": 1,
                    "winner": true,
                    "deck_id": 101,
                    "user_id": 10,
                    "deck_name": "Alesha",
                    "user_name": "Nick"
                  }
                ]
              }
            ]
            """
        );
        PlaygroupGateway gateway = CreateGateway(handler);

        IReadOnlyList<PlaygroupGame> games = await gateway.ListPlaygroupGamesAsync(
            49295,
            2,
            50,
            includeEvents: false,
            TestContext.Current.CancellationToken
        );

        games.Should().ContainSingle();
        games[0].Id.Should().Be(9001);
        games[0].Participations.Should().ContainSingle();
        games[0].Participations[0].DeckId.Should().Be(101);
        games[0].Participations[0].Winner.Should().BeTrue();
        games[0].Participations[0].UserName.Should().Be("Nick");
    }

    /// <summary>
    /// Verifies that deck detail and Elo history responses map Playgroup power fields.
    /// </summary>
    [Fact]
    public async Task DeckReads_MapDetailsAndScopedElo()
    {
        RecordingHandler handler = new();
        handler.Get(
            "decks/101",
            """
            {
              "id": 101,
              "name": "Alesha",
              "user_id": 10,
              "decklist_url": "https://archidekt.com/decks/101",
              "url": "https://playgroup.gg/profiles/nick/decks/101",
              "win_rate_percentage": 62.5,
              "games_won": 5,
              "games_lost": 3,
              "average_mulligans": 1.25,
              "most_popular_wincon": "Combat",
              "average_wins_by_round": 7,
              "cover_image": "https://images.test/card.jpg",
              "color_identity": ["R", "W", "B"],
              "last_game_played_at": "2026-05-20T02:00:00Z",
              "power_level": 7.4,
              "confidence_factor": 0.91,
              "competitiveness_rating": 0.8,
              "commander": { "id": 77, "name": "Alesha, Who Smiles at Death" }
            }
            """
        );
        handler.Get(
            "decks/101/elo_history?playgroup_id=49295",
            """
            {
              "deck_id": 101,
              "current_rating": 1567,
              "scope": "playgroup",
              "playgroup_id": 49295,
              "history": [
                { "rating": 1567, "delta": 12, "game_id": 9001, "played_at": "2026-05-20T02:00:00Z" }
              ]
            }
            """
        );
        PlaygroupGateway gateway = CreateGateway(handler);

        PlaygroupDeck deck = await gateway.GetDeckAsync(
            101,
            TestContext.Current.CancellationToken
        );
        PlaygroupEloHistory elo = await gateway.GetDeckEloHistoryAsync(
            101,
            49295,
            null,
            TestContext.Current.CancellationToken
        );

        deck.Name.Should().Be("Alesha");
        deck.PowerLevel.Should().Be(7.4);
        deck.ConfidenceFactor.Should().Be(0.91);
        deck.Commander?.Name.Should().Be("Alesha, Who Smiles at Death");
        deck.ColorIdentity.Should().Equal("R", "W", "B");
        elo.CurrentRating.Should().Be(1567);
        elo.History.Should().ContainSingle();
        handler.Requests.Should().OnlyContain(request => request.Authorization == "Bearer test-api-key");
    }

    /// <summary>
    /// Verifies league-scoped and global Elo requests produce distinct bounded query shapes.
    /// </summary>
    [Fact]
    public async Task DeckEloHistory_BuildsLeagueAndGlobalQueries()
    {
        RecordingHandler handler = new();
        handler.Get("decks/101/elo_history?league_id=77", "{ \"deck_id\": 101, \"history\": [] }");
        handler.Get("decks/101/elo_history", "{ \"deck_id\": 101 }");
        PlaygroupGateway gateway = CreateGateway(handler);

        PlaygroupEloHistory league = await gateway.GetDeckEloHistoryAsync(
            101,
            null,
            77,
            TestContext.Current.CancellationToken);
        PlaygroupEloHistory global = await gateway.GetDeckEloHistoryAsync(
            101,
            null,
            null,
            TestContext.Current.CancellationToken);

        league.DeckId.Should().Be(101);
        league.History.Should().BeEmpty();
        global.History.Should().BeEmpty();
        handler.Requests.Select(request => request.Path).Should().Equal(
            "decks/101/elo_history?league_id=77",
            "decks/101/elo_history");
    }

    /// <summary>
    /// Verifies that user deck listing maps accessible deck responses.
    /// </summary>
    [Fact]
    public async Task ListUserDecks_MapsAccessibleDecks()
    {
        RecordingHandler handler = new();
        handler.Get(
            "users/10/decks",
            """
            [
              {
                "id": 101,
                "name": "Alesha",
                "user_id": 10,
                "decklist_url": "https://archidekt.com/decks/101",
                "power_level": 7.4,
                "commander": { "id": 77, "name": "Alesha, Who Smiles at Death" }
              }
            ]
            """
        );
        PlaygroupGateway gateway = CreateGateway(handler);

        IReadOnlyList<PlaygroupDeck> decks = await gateway.ListUserDecksAsync(
            10,
            TestContext.Current.CancellationToken
        );

        decks.Should().ContainSingle();
        decks[0].Id.Should().Be(101);
        decks[0].DecklistUrl.Should().Be("https://archidekt.com/decks/101");
        decks[0].Commander?.Name.Should().Be("Alesha, Who Smiles at Death");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("users/10/decks");
    }

    /// <summary>
    /// Verifies that key-value credential files can provide an API key.
    /// </summary>
    [Fact]
    public async Task AuthStatus_LoadsKeyValueCredentialFile()
    {
        string credentialsFile = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            $"{Guid.NewGuid():N}.credentials"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(credentialsFile)!);
        await File.WriteAllTextAsync(
            credentialsFile,
            "apiKey=file-api-key",
            TestContext.Current.CancellationToken
        );

        try
        {
            RecordingHandler handler = new();
            PlaygroupGateway gateway = CreateGateway(
                handler,
                new PlaygroupOptions
                {
                    BaseAddress = new Uri("https://playgroup.test/api/public/v1/"),
                    CredentialsFile = credentialsFile,
                }
            );

            PlaygroupAuthStatus status = await gateway.GetAuthStatusAsync(
                TestContext.Current.CancellationToken
            );

            status.HasApiKey.Should().BeTrue();
            status.HasCredentialsFile.Should().BeTrue();
            status.HasCredentialsFileError.Should().BeFalse();
            status.Mode.Should().Be("api-key");
        }
        finally
        {
            if (File.Exists(credentialsFile))
            {
                File.Delete(credentialsFile);
            }
        }
    }

    /// <summary>
    /// Verifies access-token aliases in JSON credential files become bearer credentials.
    /// </summary>
    [Fact]
    public async Task AuthStatus_LoadsAccessTokenAliasFromJsonFile()
    {
        string credentialsFile = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(credentialsFile)!);
        await File.WriteAllTextAsync(
            credentialsFile,
            "{ \"access_token\": \"file-access-token\", \"ignored\": \"value\" }",
            TestContext.Current.CancellationToken);

        try
        {
            RecordingHandler handler = new();
            PlaygroupGateway gateway = CreateGateway(handler, new PlaygroupOptions
            {
                BaseAddress = new Uri("https://playgroup.test/api/public/v1/"),
                CredentialsFile = credentialsFile
            });

            PlaygroupAuthStatus status = await gateway.GetAuthStatusAsync(TestContext.Current.CancellationToken);

            status.HasApiKey.Should().BeTrue();
            status.Mode.Should().Be("api-key");
        }
        finally
        {
            File.Delete(credentialsFile);
        }
    }

    /// <summary>
    /// Verifies that malformed credential files report sanitized parse errors.
    /// </summary>
    [Fact]
    public async Task AuthStatus_ReportsMalformedCredentialFileWithoutLeakingSecrets()
    {
        string credentialsFile = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            $"{Guid.NewGuid():N}.json"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(credentialsFile)!);
        await File.WriteAllTextAsync(
            credentialsFile,
            "{ 'apiKey': 'super-secret' }",
            TestContext.Current.CancellationToken
        );

        try
        {
            RecordingHandler handler = new();
            PlaygroupGateway gateway = CreateGateway(
                handler,
                new PlaygroupOptions
                {
                    BaseAddress = new Uri("https://playgroup.test/api/public/v1/"),
                    CredentialsFile = credentialsFile,
                }
            );

            PlaygroupAuthStatus status = await gateway.GetAuthStatusAsync(
                TestContext.Current.CancellationToken
            );

            status.HasApiKey.Should().BeFalse();
            status.HasCredentialsFile.Should().BeTrue();
            status.HasCredentialsFileError.Should().BeTrue();
            status.CredentialsFileError.Should().Contain("JSON requires double quotes");
            status.CredentialsFileError.Should().NotContain("super-secret");
            status.Mode.Should().Be("credentials-file-error");
        }
        finally
        {
            if (File.Exists(credentialsFile))
            {
                File.Delete(credentialsFile);
            }
        }
    }

    /// <summary>
    /// Verifies that JSON credential files must contain an object.
    /// </summary>
    [Fact]
    public async Task AuthStatus_ReportsNonObjectCredentialFileWithoutLeakingSecrets()
    {
        string credentialsFile = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            $"{Guid.NewGuid():N}.json"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(credentialsFile)!);
        await File.WriteAllTextAsync(
            credentialsFile,
            """["super-secret"]""",
            TestContext.Current.CancellationToken
        );

        try
        {
            RecordingHandler handler = new();
            PlaygroupGateway gateway = CreateGateway(
                handler,
                new PlaygroupOptions
                {
                    BaseAddress = new Uri("https://playgroup.test/api/public/v1/"),
                    CredentialsFile = credentialsFile,
                }
            );

            PlaygroupAuthStatus status = await gateway.GetAuthStatusAsync(
                TestContext.Current.CancellationToken
            );

            status.HasApiKey.Should().BeFalse();
            status.HasCredentialsFile.Should().BeTrue();
            status.HasCredentialsFileError.Should().BeTrue();
            status.CredentialsFileError.Should().Contain("must contain a JSON object");
            status.CredentialsFileError.Should().NotContain("super-secret");
        }
        finally
        {
            if (File.Exists(credentialsFile))
            {
                File.Delete(credentialsFile);
            }
        }
    }

    /// <summary>
    /// Verifies that failed responses are redacted before entering exception messages.
    /// </summary>
    [Fact]
    public async Task FailedRequests_RedactSecretResponseBodies()
    {
        RecordingHandler handler = new();
        handler.Get("me", """{ "api_key": "super-secret" }""", HttpStatusCode.BadRequest);
        PlaygroupGateway gateway = CreateGateway(handler);

        Func<Task> act = () => gateway.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*400*REDACTED*");
    }

    /// <summary>
    /// Creates a gateway with default fake API-key options.
    /// </summary>
    private static PlaygroupGateway CreateGateway(RecordingHandler handler)
    {
        return CreateGateway(
            handler,
            new PlaygroupOptions
            {
                BaseAddress = new Uri("https://playgroup.test/api/public/v1/"),
                ApiKey = "test-api-key",
            }
        );
    }

    /// <summary>
    /// Creates a gateway with supplied fake options.
    /// </summary>
    private static PlaygroupGateway CreateGateway(
        RecordingHandler handler,
        PlaygroupOptions options
    )
    {
        HttpClient httpClient = new(handler) { BaseAddress = options.BaseAddress };
        return new PlaygroupGateway(httpClient, Options.Create(options));
    }

    /// <summary>
    /// Provides queued HTTP responses and records requests.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>
        /// Stores configured responses by method and path.
        /// </summary>
        private readonly Dictionary<(HttpMethod Method, string Path), Queue<RecordedResponse>> responses =
            new();

        /// <summary>
        /// Gets requests observed by the handler.
        /// </summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>
        /// Configures a GET response.
        /// </summary>
        public void Get(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK
        )
        {
            AddResponse(HttpMethod.Get, path, response, statusCode);
        }

        /// <summary>
        /// Adds one response to the matching method and path queue.
        /// </summary>
        private void AddResponse(
            HttpMethod method,
            string path,
            string response,
            HttpStatusCode statusCode
        )
        {
            if (!responses.TryGetValue((method, path), out Queue<RecordedResponse>? queue))
            {
                queue = new Queue<RecordedResponse>();
                responses[(method, path)] = queue;
            }

            queue.Enqueue(new RecordedResponse(response, statusCode));
        }

        /// <summary>
        /// Records a request and returns a configured fake response.
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri?.PathAndQuery.TrimStart('/') ?? "";
            const string apiPrefix = "api/public/v1/";
            if (path.StartsWith(apiPrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[apiPrefix.Length..];
            }
            string body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string? authorization = request.Headers.Authorization?.ToString();
            Requests.Add(new RecordedRequest(request.Method, path, body, authorization));

            if (!responses.TryGetValue((request.Method, path), out Queue<RecordedResponse>? queue))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        $"No fixture for {request.Method} {path}",
                        Encoding.UTF8,
                        "text/plain"
                    ),
                };
            }

            RecordedResponse response = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// Represents a configured fake HTTP response.
    /// </summary>
    private sealed record RecordedResponse(string Body, HttpStatusCode StatusCode);

    /// <summary>
    /// Represents a recorded fake HTTP request.
    /// </summary>
    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Body,
        string? Authorization
    );
}
