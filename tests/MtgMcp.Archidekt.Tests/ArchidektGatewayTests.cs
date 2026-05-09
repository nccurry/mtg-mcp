using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Archidekt;
using MtgMcp.Core;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Contains tests for archidekt gateway.
/// </summary>
public sealed class ArchidektGatewayTests
{
    /// <summary>
    /// Verifies that import deck maps categories and cards.
    /// </summary>
    [Fact]
    public async Task ImportDeck_MapsCategoriesAndCards()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": "commander",
              "categories": [
                { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true },
                { "id": 2, "name": "Maybeboard", "includedInDeck": false, "includedInPrice": true }
              ],
              "cards": [
                {
                  "id": 44,
                  "quantity": 1,
                  "categories": [1, { "id": 2, "name": "Maybeboard" }],
                  "card": {
                    "id": 99,
                    "uid": "scryfall-card",
                    "setCode": "lea",
                    "collectorNumber": "161",
                    "scryfallUri": "https://scryfall.test/card",
                    "oracleCard": {
                      "uid": "oracle-card",
                      "name": "Lightning Bolt",
                      "typeLine": "Instant",
                      "manaValue": 1,
                      "colorIdentity": ["R"]
                    }
                  }
                }
              ]
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = await gateway.ImportDeckAsync(
            "https://archidekt.com/decks/123/deck",
            writeBack: true,
            TestContext.Current.CancellationToken
        );

        deck.Mode.Should().Be(WorkspaceMode.Archidekt);
        deck.ArchidektDeckId.Should().Be("123");
        deck.Categories.Should()
            .Contain(category => category.Name == "Maybeboard" && category.IncludedInDeck == false);
        deck.Cards.Should().ContainSingle();
        deck.Cards[0].Name.Should().Be("Lightning Bolt");
        deck.Cards[0].Categories.Should().BeEquivalentTo(["Mainboard", "Maybeboard"]);
        deck.Cards[0].ArchidektCardId.Should().Be("99");
        deck.Cards[0].ArchidektDeckRelationId.Should().Be(44);
        deck.Cards[0].Snapshot.TypeLine.Should().Be("Instant");
        deck.Cards[0].Snapshot.ManaValue.Should().Be(1);
        deck.Cards[0].Snapshot.ColorIdentity.Should().BeEquivalentTo(["R"]);
        deck.Cards[0].Snapshot.Set.Should().Be("lea");
        deck.Cards[0].Snapshot.CollectorNumber.Should().Be("161");
        deck.Cards[0].Snapshot.ScryfallUri.Should().Be("https://scryfall.test/card");
    }

    /// <summary>
    /// Verifies that list decks maps results and uses configured jwt.
    /// </summary>
    [Fact]
    public async Task ListDecks_MapsResultsAndUsesConfiguredJwt()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/",
            """
            {
              "results": [
                { "id": 123, "name": "Deck", "deckFormat": "commander", "updatedAt": "2026-05-01T00:00:00Z" }
              ]
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        IReadOnlyList<ArchidektDeckSummary> decks = await gateway.ListDecksAsync(
            TestContext.Current.CancellationToken
        );

        decks.Should().ContainSingle();
        decks[0].Id.Should().Be("123");
        decks[0].Format.Should().Be("commander");
        decks[0].UpdatedAt.Should().NotBeNull();
        handler.Requests.Single().Authorization.Should().Be("JWT test-jwt");
    }

    /// <summary>
    /// Verifies that persist cards resolves add and patches modify cards endpoint.
    /// </summary>
    [Fact]
    public async Task PersistCards_ResolvesAddAndPatchesModifyCardsEndpoint()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/cards/v2/?name=Lightning%20Bolt&pageSize=25",
            """
            { "results": [ { "id": 151147, "oracleCard": { "name": "Lightning Bolt" } } ] }
            """
        );
        handler.Patch("api/decks/123/modifyCards/v2/", "{}");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard card = new()
        {
            Name = "Lightning Bolt",
            Quantity = 2,
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        handler
            .Requests.Should()
            .Contain(request =>
                request.Method == HttpMethod.Patch
                && request.Path == "api/decks/123/modifyCards/v2/"
            );
        string body = handler.Requests.Single(request => request.Method == HttpMethod.Patch).Body;
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement firstCard = document.RootElement.GetProperty("cards")[0];
        firstCard.GetProperty("action").GetString().Should().Be("add");
        firstCard.GetProperty("cardid").GetInt32().Should().Be(151147);
        firstCard.GetProperty("categories")[0].GetString().Should().Be(DeckDefaults.Mainboard);
        firstCard.GetProperty("modifications").GetProperty("quantity").GetInt32().Should().Be(2);
        card.ArchidektCardId.Should().Be("151147");
    }

    /// <summary>
    /// Verifies that persist cards modifies and removes existing cards without lookup.
    /// </summary>
    [Fact]
    public async Task PersistCards_ModifiesAndRemovesExistingCardsWithoutLookup()
    {
        RecordingHandler handler = new();
        handler.Patch("api/decks/123/modifyCards/v2/", "{}");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard modified = new()
        {
            Name = "Sol Ring",
            Quantity = 1,
            ArchidektCardId = "99",
            ArchidektDeckRelationId = 44,
            Categories = [DeckDefaults.Mainboard],
            Modifier = "Foil",
        };
        DeckCard removed = new()
        {
            Name = "Mana Crypt",
            Quantity = 1,
            ArchidektCardId = "100",
            ArchidektDeckRelationId = 45,
            Categories = [DeckDefaults.Maybeboard],
        };

        await gateway.PersistCardsAsync(
            deck,
            [modified],
            [removed],
            TestContext.Current.CancellationToken
        );

        handler.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Patch);
        using JsonDocument document = JsonDocument.Parse(handler.Requests[0].Body);
        JsonElement cards = document.RootElement.GetProperty("cards");
        cards[0].GetProperty("action").GetString().Should().Be("modify");
        cards[0].GetProperty("deckRelationId").GetInt32().Should().Be(44);
        cards[0]
            .GetProperty("modifications")
            .GetProperty("modifier")
            .GetString()
            .Should()
            .Be("Foil");
        cards[1].GetProperty("action").GetString().Should().Be("remove");
        cards[1].GetProperty("modifications").GetProperty("quantity").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Verifies that persist category creates then updates category.
    /// </summary>
    [Fact]
    public async Task PersistCategory_CreatesThenUpdatesCategory()
    {
        RecordingHandler handler = new();
        handler.Post("api/decks/createCategory/", """{ "id": 9, "name": "Ramp" }""");
        handler.Patch("api/decks/category/9/", """{ "id": 9, "name": "Acceleration" }""");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCategory category = new()
        {
            Name = "Ramp",
            IncludedInDeck = true,
            IncludedInPrice = false,
        };

        await gateway.PersistCategoryAsync(deck, category, TestContext.Current.CancellationToken);
        category.Name = "Acceleration";
        await gateway.PersistCategoryAsync(deck, category, TestContext.Current.CancellationToken);

        category.ArchidektCategoryId.Should().Be(9);
        handler
            .Requests.Select(request => request.Path)
            .Should()
            .Equal("api/decks/createCategory/", "api/decks/category/9/");
        handler.Requests[0].Body.Should().Contain("\"includedInPrice\":false");
        handler.Requests[1].Method.Should().Be(HttpMethod.Patch);
    }

    /// <summary>
    /// Verifies that delete category skips local only and deletes remote categories.
    /// </summary>
    [Fact]
    public async Task DeleteCategory_SkipsLocalOnlyAndDeletesRemoteCategories()
    {
        RecordingHandler handler = new();
        handler.Delete("api/decks/category/9/", "{}");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };

        await gateway.DeleteCategoryAsync(
            deck,
            new DeckCategory { Name = "Local" },
            TestContext.Current.CancellationToken
        );
        await gateway.DeleteCategoryAsync(
            deck,
            new DeckCategory { Name = "Remote", ArchidektCategoryId = 9 },
            TestContext.Current.CancellationToken
        );

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("api/decks/category/9/");
    }

    /// <summary>
    /// Verifies that persist metadata patches deck update endpoint.
    /// </summary>
    [Fact]
    public async Task PersistMetadata_PatchesDeckUpdateEndpoint()
    {
        RecordingHandler handler = new();
        handler.Patch("api/decks/123/update/", "{}");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Name = "Renamed",
            Format = "modern",
            Description = "Updated",
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };

        await gateway.PersistMetadataAsync(deck, TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("api/decks/123/update/");
        handler.Requests[0].Body.Should().Contain("\"deckFormat\":\"modern\"");
        handler.Requests[0].Body.Should().Contain("\"description\":\"Updated\"");
    }

    /// <summary>
    /// Verifies that checkpoints use snapshot endpoints.
    /// </summary>
    [Fact]
    public async Task Checkpoints_UseSnapshotEndpoints()
    {
        RecordingHandler handler = new();
        handler.Post(
            "api/decks/123/snapshots/",
            """{ "id": 7, "name": "Before tuning", "description": "baseline" }"""
        );
        handler.Get(
            "api/decks/123/snapshots/",
            """{ "results": [ { "id": 7, "name": "Before tuning" } ] }"""
        );
        handler.Get(
            "api/decks/snapshots/7/",
            """{ "id": 7, "name": "Before tuning", "createdAt": "2026-05-01T00:00:00Z" }"""
        );
        handler.Patch("api/decks/snapshots/7/", """{ "id": 7, "name": "After tuning" }""");
        handler.Delete("api/decks/snapshots/7/", "{}");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };

        DeckCheckpoint created = await gateway.CreateCheckpointAsync(
            deck,
            "Before tuning",
            "baseline",
            TestContext.Current.CancellationToken
        );
        IReadOnlyList<DeckCheckpoint> listed = await gateway.ListCheckpointsAsync(
            deck,
            TestContext.Current.CancellationToken
        );
        DeckCheckpoint fetched = await gateway.GetCheckpointAsync(
            deck,
            "7",
            TestContext.Current.CancellationToken
        );
        DeckCheckpoint renamed = await gateway.RenameCheckpointAsync(
            deck,
            "7",
            "After tuning",
            null,
            TestContext.Current.CancellationToken
        );
        await gateway.DeleteCheckpointAsync(deck, "7", TestContext.Current.CancellationToken);

        created.Id.Should().Be("7");
        listed.Should().ContainSingle(checkpoint => checkpoint.Id == "7");
        fetched.CreatedAt.Should().NotBeNull();
        renamed.Name.Should().Be("After tuning");
        handler
            .Requests.Should()
            .Contain(request =>
                request.Method == HttpMethod.Delete && request.Path == "api/decks/snapshots/7/"
            );
    }

    /// <summary>
    /// Verifies that refresh token is used when jwt is missing.
    /// </summary>
    [Fact]
    public async Task RefreshToken_IsUsedWhenJwtIsMissing()
    {
        RecordingHandler handler = new();
        handler.Post("api/rest-auth/token/refresh/", """{ "access": "fresh-jwt" }""");
        handler.Get("api/decks/", """[]""");

        ArchidektGateway gateway = CreateGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                RefreshToken = "refresh-token",
            }
        );

        await gateway.ListDecksAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].Path.Should().Be("api/rest-auth/token/refresh/");
        handler.Requests[1].Authorization.Should().Be("JWT fresh-jwt");
    }

    /// <summary>
    /// Verifies that username password login is fallback credential source.
    /// </summary>
    [Fact]
    public async Task UsernamePasswordLogin_IsFallbackCredentialSource()
    {
        RecordingHandler handler = new();
        handler.Post(
            "api/rest-auth/login/",
            """{ "key": "login-jwt", "refresh": "refresh-token" }"""
        );
        handler.Get("api/decks/", """[]""");

        ArchidektGateway gateway = CreateGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                Username = "user",
                Password = "pass",
                EnableUsernamePasswordLogin = true,
            }
        );

        await gateway.ListDecksAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].Body.Should().Contain("\"username\":\"user\"");
        handler.Requests[1].Authorization.Should().Be("JWT login-jwt");
    }

    /// <summary>
    /// Verifies that auth status loads credential file without exposing secrets.
    /// </summary>
    [Fact]
    public async Task AuthStatus_LoadsCredentialFileWithoutExposingSecrets()
    {
        string credentialsFile = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            $"{Guid.NewGuid():N}.json"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(credentialsFile)!);
        await File.WriteAllTextAsync(
            credentialsFile,
            """
            {
              "jwt": "file-jwt",
              "refreshToken": "file-refresh",
              "username": "file-user",
              "password": "file-pass"
            }
            """,
            TestContext.Current.CancellationToken
        );

        try
        {
            RecordingHandler handler = new();
            ArchidektGateway gateway = CreateGateway(
                handler,
                new ArchidektOptions
                {
                    BaseAddress = new Uri("https://archidekt.test/"),
                    CredentialsFile = credentialsFile,
                }
            );

            AuthStatus status = await gateway.GetAuthStatusAsync(
                TestContext.Current.CancellationToken
            );

            status.HasJwt.Should().BeTrue();
            status.HasRefreshToken.Should().BeTrue();
            status.HasUsernamePassword.Should().BeTrue();
            status.HasCredentialsFile.Should().BeTrue();
            status.Mode.Should().Be("jwt");
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
    /// Verifies that failed requests redact secret response bodies.
    /// </summary>
    [Fact]
    public async Task FailedRequests_RedactSecretResponseBodies()
    {
        RecordingHandler handler = new();
        handler.Get("api/decks/", """{ "token": "secret-token" }""", HttpStatusCode.BadRequest);
        ArchidektGateway gateway = CreateGateway(handler);

        Func<Task> act = () => gateway.ListDecksAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*400*REDACTED*");
    }

    /// <summary>
    /// Creates a gateway with default test options.
    /// </summary>
    private static ArchidektGateway CreateGateway(RecordingHandler handler)
    {
        return CreateGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                Jwt = "test-jwt",
            }
        );
    }

    /// <summary>
    /// Creates a gateway with supplied test options.
    /// </summary>
    private static ArchidektGateway CreateGateway(
        RecordingHandler handler,
        ArchidektOptions options
    )
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://archidekt.test/") };

        return new ArchidektGateway(httpClient, Options.Create(options));
    }

    /// <summary>
    /// Provides recording handler behavior.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>
        /// Verifies that dictionary.
        /// </summary>
        private readonly Dictionary<(HttpMethod Method, string Path), RecordedResponse> responses =
            new();

        /// <summary>
        /// Gets or sets the requests.
        /// </summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>
        /// Reads a recorded response header value.
        /// </summary>
        public void Get(string path, string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            responses[(HttpMethod.Get, path)] = new RecordedResponse(response, statusCode);
        }

        /// <summary>
        /// Verifies that post.
        /// </summary>
        public void Post(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK
        )
        {
            responses[(HttpMethod.Post, path)] = new RecordedResponse(response, statusCode);
        }

        /// <summary>
        /// Verifies that patch.
        /// </summary>
        public void Patch(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK
        )
        {
            responses[(HttpMethod.Patch, path)] = new RecordedResponse(response, statusCode);
        }

        /// <summary>
        /// Verifies that delete.
        /// </summary>
        public void Delete(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK
        )
        {
            responses[(HttpMethod.Delete, path)] = new RecordedResponse(response, statusCode);
        }

        /// <summary>
        /// Verifies that send.
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri?.PathAndQuery.TrimStart('/') ?? "";
            string body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string? authorization = request.Headers.Authorization?.ToString();
            Requests.Add(new RecordedRequest(request.Method, path, body, authorization));

            if (!responses.TryGetValue((request.Method, path), out RecordedResponse? response))
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

            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// Represents recorded response.
    /// </summary>
    private sealed record RecordedResponse(string Body, HttpStatusCode StatusCode);

    /// <summary>
    /// Represents recorded request.
    /// </summary>
    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string Body,
        string? Authorization
    );
}
