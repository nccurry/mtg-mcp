using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;
using MtgMcp.Moxfield;

namespace MtgMcp.Moxfield.Tests;

/// <summary>
/// Contains tests for Moxfield deck import.
/// </summary>
public sealed class MoxfieldGatewayTests
{
    /// <summary>
    /// Verifies that public decks import as provider-neutral local workspaces.
    /// </summary>
    [Fact]
    public async Task ImportDeck_MapsBoardsTagsAndSnapshots()
    {
        RecordingHandler handler = new();
        handler.Get(
            "v3/decks/all/abc_123",
            """
            {
              "publicId": "abc_123",
              "name": "Mox Brew",
              "format": "edh",
              "description": "from Moxfield",
              "tags": {
                "tag-ramp": { "name": "Ramp" },
                "tag-draw": { "name": "Card Draw" }
              },
              "authorTags": {
                "Brainstorm": ["Cantrip", "Card Draw"],
                "Sol Ring": ["Fast Mana"]
              },
              "boards": {
                "commanders": {
                  "count": 1,
                  "cards": {
                    "commander": {
                      "quantity": 1,
                      "tags": ["tag-ramp"],
                      "card": {
                        "id": "mox-card-1",
                        "name": "Atraxa, Praetors' Voice",
                        "scryfall_id": "scryfall-atraxa",
                        "oracle_id": "oracle-atraxa",
                        "mana_cost": "{G}{W}{U}{B}",
                        "layout": "normal",
                        "type_line": "Legendary Creature - Phyrexian Angel Horror",
                        "oracle_text": "Flying, vigilance, deathtouch, lifelink",
                        "power": "4",
                        "toughness": "4",
                        "cmc": 4,
                        "color_identity": ["W", "U", "B", "G"],
                        "card_faces": [
                          {
                            "name": "Atraxa, Praetors' Voice",
                            "mana_cost": "{G}{W}{U}{B}",
                            "type_line": "Legendary Creature - Phyrexian Angel Horror",
                            "oracle_text": "Flying, vigilance, deathtouch, lifelink",
                            "power": "4",
                            "toughness": "4",
                            "colors": ["W", "U", "B", "G"]
                          }
                        ],
                        "set": "c16",
                        "cn": "28",
                        "rarity": "mythic",
                        "edhrec_rank": 99,
                        "prices": { "usd": "17.50" }
                      }
                    }
                  }
                },
                "mainboard": {
                  "count": 1,
                  "cards": {
                    "sol-ring": {
                      "quantity": 1,
                      "finish": "foil",
                      "tags": { "tag-ramp": true, "tag-draw": true },
                      "card": {
                        "id": "mox-card-2",
                        "name": "Sol Ring",
                        "scryfall_id": "scryfall-sol-ring",
                        "set": "cmm",
                        "cn": "400",
                        "type_line": "Artifact",
                        "cmc": 1,
                        "color_identity": []
                      }
                    }
                  }
                },
                "maybeboard": {
                  "count": 2,
                  "cards": {
                    "brainstorm": {
                      "quantity": 2,
                      "card": { "name": "Brainstorm", "type_line": "Instant" }
                    }
                  }
                },
                "tokens": {
                  "count": 1,
                  "cards": {
                    "token": {
                      "quantity": 1,
                      "tags": ["Display Only"],
                      "card": { "name": "Angel Token", "type_line": "Token Creature - Angel" }
                    }
                  }
                }
              }
            }
            """
        );

        MoxfieldGateway gateway = CreateGateway(handler);
        DeckWorkspace workspace = await gateway.ImportDeckAsync(
            "https://www.moxfield.com/decks/abc_123",
            TestContext.Current.CancellationToken);

        workspace.Mode.Should().Be(WorkspaceMode.Local);
        workspace.WriteBack.Should().BeFalse();
        workspace.Name.Should().Be("Mox Brew");
        workspace.Format.Should().Be("commander");
        workspace.SourceReferences.Should().ContainSingle(source =>
            source.Provider == DeckImportProviders.Moxfield
            && source.ExternalId == "abc_123"
            && source.Url == "https://www.moxfield.com/decks/abc_123");
        workspace.Categories.Should().Contain(category =>
            category.Name == DeckRoles.Commander && category.IncludedInDeck);
        workspace.Categories.Should().Contain(category =>
            category.Name == DeckDefaults.Maybeboard && !category.IncludedInDeck);
        workspace.Categories.Should().Contain(category =>
            category.Name == "Tokens" && !category.IncludedInDeck);
        workspace.Categories.Should().Contain(category =>
            category.Name == "Ramp" && !category.IncludedInDeck);
        workspace.Categories.Should().Contain(category =>
            category.Name == "Card Draw" && !category.IncludedInDeck);

        DeckCard commander = workspace.Cards.Single(card => card.Name == "Atraxa, Praetors' Voice");
        commander.PrimaryCategory.Should().Be(DeckRoles.Commander);
        commander.Categories.Should().Equal(DeckRoles.Commander, "Ramp");
        commander.ScryfallId.Should().Be("scryfall-atraxa");
        commander.ScryfallOracleId.Should().Be("oracle-atraxa");
        commander.Snapshot.ManaCost.Should().Be("{G}{W}{U}{B}");
        commander.Snapshot.Layout.Should().Be("normal");
        commander.Snapshot.Power.Should().Be("4");
        commander.Snapshot.Toughness.Should().Be("4");
        commander.Snapshot.ColorIdentity.Should().BeEquivalentTo(["W", "U", "B", "G"]);
        commander.Snapshot.Provenance.Provider.Should().Be(DeckImportProviders.Moxfield);
        commander.Snapshot.Provenance.ProviderCardId.Should().Be("mox-card-1");
        commander.Snapshot.Faces.Should().ContainSingle().Which.Name.Should().Be("Atraxa, Praetors' Voice");
        commander.Snapshot.Prices["usd"].Should().Be("17.50");

        DeckCard solRing = workspace.Cards.Single(card => card.Name == "Sol Ring");
        solRing.PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        solRing.Categories.Should().Equal(DeckDefaults.Mainboard, "Ramp", "Card Draw", "Fast Mana");
        solRing.Modifier.Should().Be("Foil");
        solRing.Metadata["moxfieldFinish"].Should().Be("foil");

        DeckCard maybe = workspace.Cards.Single(card => card.Name == "Brainstorm");
        maybe.Quantity.Should().Be(2);
        maybe.PrimaryCategory.Should().Be(DeckDefaults.Maybeboard);
        maybe.Categories.Should().Equal(DeckDefaults.Maybeboard, "Cantrip", "Card Draw");
        maybe.Metadata["moxfieldTags"].Should().Be("Cantrip, Card Draw");

        handler.Requests.Should().ContainSingle().Which.Path.Should().Be("v3/decks/all/abc_123");
    }

    /// <summary>
    /// Verifies that blocked anonymous requests return actionable sanitized errors.
    /// </summary>
    [Fact]
    public async Task ImportDeck_ReportsBlockedAnonymousAccess()
    {
        RecordingHandler handler = new();
        handler.Get(
            "v3/decks/all/blocked",
            "<html>challenge token secret</html>",
            HttpStatusCode.Forbidden
        );
        MoxfieldGateway gateway = CreateGateway(handler);

        Func<Task> act = () => gateway.ImportDeckAsync(
            "blocked",
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<HttpRequestException>()
            .WithMessage("*403*Moxfield may have blocked anonymous API access*REDACTED*")
            .Where(exception => !exception.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a gateway with default test options.
    /// </summary>
    private static MoxfieldGateway CreateGateway(RecordingHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://moxfield.test/") };
        return new MoxfieldGateway(
            httpClient,
            Options.Create(new MoxfieldOptions
            {
                BaseAddress = new Uri("https://moxfield.test/"),
                UserAgent = "mtg-mcp-test",
                EnableCurlFallback = false,
            }));
    }

    /// <summary>
    /// Provides recording handler behavior.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>
        /// Stores configured responses by request path.
        /// </summary>
        private readonly Dictionary<string, RecordedResponse> responses =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets recorded requests.
        /// </summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>
        /// Registers a GET response.
        /// </summary>
        public void Get(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK
        )
        {
            responses[path] = new RecordedResponse(response, statusCode);
        }

        /// <summary>
        /// Sends a recorded response.
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri?.PathAndQuery.TrimStart('/') ?? "";
            Requests.Add(new RecordedRequest(request.Method, path));
            if (!responses.TryGetValue(path, out RecordedResponse? response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        $"No fixture for {path}",
                        Encoding.UTF8,
                        "text/plain"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Represents a recorded response.
    /// </summary>
    private sealed record RecordedResponse(string Body, HttpStatusCode StatusCode);

    /// <summary>
    /// Represents a recorded request.
    /// </summary>
    private sealed record RecordedRequest(HttpMethod Method, string Path);
}
