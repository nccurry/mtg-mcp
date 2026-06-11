using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
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
              "deckFormat": "3",
              "categories": [
                { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true },
                { "id": 2, "name": "Maybeboard", "includedInDeck": false, "includedInPrice": true },
                { "id": 3, "name": "Commander", "isPremier": true, "includedInDeck": true, "includedInPrice": true }
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
                    "rarity": "common",
                    "scryfallUri": "https://scryfall.test/card",
                    "oracleCard": {
                      "uid": "oracle-card",
                      "name": "Lightning Bolt",
                      "typeLine": "Instant",
                      "oracleText": "Lightning Bolt deals 3 damage to any target.",
                      "manaValue": 1,
                      "edhrecRank": 42,
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
        deck.ArchidektDeckFormatId.Should().Be(3);
        deck.SourceReferences.Should().ContainSingle(source =>
            source.Provider == DeckImportProviders.Archidekt
            && source.ExternalId == "123");
        deck.Format.Should().Be("commander");
        deck.Categories.Should()
            .Contain(category => category.Name == "Maybeboard" && category.IncludedInDeck == false);
        deck.Categories.Should()
            .Contain(category => category.Name == DeckRoles.Commander && category.IsPremier);
        deck.Cards.Should().ContainSingle();
        deck.Cards[0].Name.Should().Be("Lightning Bolt");
        deck.Cards[0].PrimaryCategory.Should().Be(DeckDefaults.Mainboard);
        deck.Cards[0].Categories.Should().Equal(DeckDefaults.Mainboard, DeckDefaults.Maybeboard);
        deck.Cards[0].ArchidektCardId.Should().Be("99");
        deck.Cards[0].ArchidektDeckRelationId.Should().Be(44);
        deck.Cards[0].Snapshot.TypeLine.Should().Be("Instant");
        deck.Cards[0].Snapshot.OracleText.Should().Contain("3 damage");
        deck.Cards[0].Snapshot.ManaValue.Should().Be(1);
        deck.Cards[0].Snapshot.EdhrecRank.Should().Be(42);
        deck.Cards[0].Snapshot.ColorIdentity.Should().BeEquivalentTo(["R"]);
        deck.Cards[0].Snapshot.Set.Should().Be("lea");
        deck.Cards[0].Snapshot.CollectorNumber.Should().Be("161");
        deck.Cards[0].Snapshot.Rarity.Should().Be("common");
        deck.Cards[0].Snapshot.ScryfallUri.Should().Be("https://scryfall.test/card");
    }

    /// <summary>
    /// Verifies that import deck tolerates Archidekt's live bracket-era card shape.
    /// </summary>
    [Fact]
    public async Task ImportDeck_MapsBracketEraCardShape()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/5850815/",
            """
            {
              "id": 5850815,
              "name": "Tinybones, Trinket Thief",
              "deckFormat": 3,
              "edhBracket": null,
              "categories": [
                { "id": 52958298, "name": "Maybeboard", "includedInDeck": false, "includedInPrice": false },
                { "id": 52994821, "name": "Creature", "includedInDeck": true, "includedInPrice": true }
              ],
              "cards": [
                {
                  "id": 1521012334,
                  "categories": ["Maybeboard", "Creature"],
                  "modifier": "Normal",
                  "quantity": 1,
                  "card": {
                    "id": 91694,
                    "uid": "14dc88ee-bba9-4625-af0d-89f3762a0ead",
                    "edition": {
                      "editioncode": "khm",
                      "editionname": "Kaldheim",
                      "editiondate": "2021-02-05",
                      "editiontype": "expansion",
                      "mtgoCode": "khm"
                    },
                    "setCode": "khm",
                    "collectorNumber": "112",
                    "rarity": "rare",
                    "prices": {
                      "tcg": 17.65,
                      "ck": 19.99
                    },
                    "oracleCard": {
                      "uid": "8485cfaa-1dbf-432b-b5d0-92a6aa6a329b",
                      "name": "Tergrid, God of Fright // Tergrid's Lantern",
                      "cmc": 5,
                      "manaCost": "",
                      "text": "",
                      "colorIdentity": ["Black"],
                      "faces": [
                        {
                          "manaCost": "{3}{B}{B}",
                          "text": "Menace\nWhenever an opponent sacrifices a nontoken permanent or discards a permanent card, you may put that card onto the battlefield.",
                          "superTypes": "Legendary",
                          "types": "Creature",
                          "subTypes": "God"
                        },
                        {
                          "manaCost": "{3}{B}",
                          "text": "{T}: Target player loses 3 life unless they sacrifice a nonland permanent or discard a card.",
                          "superTypes": "Legendary",
                          "types": "Artifact",
                          "subTypes": ""
                        }
                      ],
                      "gameChanger": true,
                      "extraTurns": false,
                      "tutor": false,
                      "massLandDenial": false,
                      "power": "",
                      "salt": 2.8
                    }
                  }
                }
              ]
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = await gateway.ImportDeckAsync(
            "https://archidekt.com/decks/5850815/tinybones_trinket_thief",
            writeBack: true,
            TestContext.Current.CancellationToken
        );

        deck.Format.Should().Be("commander");
        deck.Categories.Should()
            .Contain(category => category.Name == "Maybeboard" && category.IncludedInDeck == false);
        DeckCard card = deck.Cards.Should().ContainSingle().Which;
        card.Name.Should().Be("Tergrid, God of Fright // Tergrid's Lantern");
        card.PrimaryCategory.Should().Be("Maybeboard");
        card.Categories.Should().Equal("Maybeboard", "Creature");
        card.Modifier.Should().Be("Normal");
        card.Snapshot.ManaCost.Should().Be("{3}{B}{B}");
        card.Snapshot.TypeLine.Should().Be("Legendary Creature - God // Legendary Artifact");
        card.Snapshot.Set.Should().Be("khm");
        card.Snapshot.OracleText.Should().Contain("Whenever an opponent sacrifices");
        card.Snapshot.OracleText.Should().Contain("Target player loses 3 life");
        card.Snapshot.ColorIdentity.Should().ContainSingle().Which.Should().Be("B");
        card.Snapshot.Prices["usd"].Should().Be("17.65");
    }

    /// <summary>
    /// Verifies that import deck preserves the land face type on modal double-faced lands.
    /// </summary>
    [Fact]
    public async Task ImportDeck_MapsModalDoubleFacedLandTypeLine()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/5850815/",
            """
            {
              "id": 5850815,
              "name": "Tinybones, Trinket Thief",
              "deckFormat": 3,
              "categories": [
                { "id": 52857748, "name": "Land", "includedInDeck": true, "includedInPrice": true }
              ],
              "cards": [
                {
                  "id": 3087658000,
                  "categories": ["Land"],
                  "quantity": 1,
                  "card": {
                    "id": 91694,
                    "uid": "malakir-card",
                    "oracleCard": {
                      "uid": "malakir-oracle",
                      "name": "Malakir Rebirth // Malakir Mire",
                      "cmc": 1,
                      "manaCost": "",
                      "colorIdentity": ["Black"],
                      "faces": [
                        {
                          "manaCost": "{B}",
                          "text": "Choose target creature. You lose 2 life.",
                          "types": "Instant",
                          "subTypes": ""
                        },
                        {
                          "text": "Malakir Mire enters the battlefield tapped.",
                          "types": "Land",
                          "subTypes": ""
                        }
                      ]
                    }
                  }
                }
              ]
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = await gateway.ImportDeckAsync(
            "https://archidekt.com/decks/5850815/tinybones_trinket_thief",
            writeBack: true,
            TestContext.Current.CancellationToken
        );

        DeckCard card = deck.Cards.Should().ContainSingle().Which;
        card.PrimaryCategory.Should().Be("Land");
        card.Snapshot.TypeLine.Should().Be("Instant // Land");
        card.Snapshot.OracleText.Should().Contain("Malakir Mire enters");
        card.Snapshot.ColorIdentity.Should().ContainSingle().Which.Should().Be("B");
    }

    /// <summary>
    /// Verifies that import deck maps alternate deck relation id fields.
    /// </summary>
    [Fact]
    public async Task ImportDeck_MapsAlternateDeckRelationIdField()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": 3,
              "categories": [
                { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true }
              ],
              "cards": [
                {
                  "deckRelationId": 3085344231,
                  "quantity": 1,
                  "categories": ["Mainboard"],
                  "card": {
                    "id": 99,
                    "oracleCard": { "name": "Mind Rot" }
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

        deck.Cards.Should().ContainSingle().Which.ArchidektDeckRelationId.Should().Be(3085344231L);
    }

    /// <summary>
    /// Verifies that list decks maps results and uses existing HTTP authorization.
    /// </summary>
    [Fact]
    public async Task ListDecks_MapsResultsAndUsesExistingAuthorization()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/",
            """
            {
              "decks": [
                { "id": 123, "name": "Deck", "deckFormat": "3", "updatedAt": "2026-05-01T00:00:00Z" }
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
        handler.Requests.Single().Path.Should().Be("api/decks/");
    }

    /// <summary>
    /// Verifies that folder operations use Archidekt folder contracts.
    /// </summary>
    [Fact]
    public async Task FolderOperations_MapArchidektFolderContracts()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/folderTree/",
            """
            {
              "results": [
                {
                  "id": 10,
                  "name": "Root",
                  "children": [
                    { "id": 11, "name": "Child", "parent": 10 }
                  ]
                }
              ]
            }
            """);
        handler.Post(
            "api/decks/folders/",
            """{ "id": 12, "name": "New Folder", "parent": 10 }""");
        handler.Patch("api/massUpdate/", "{}");

        ArchidektGateway gateway = CreateGateway(handler);

        IReadOnlyList<ArchidektFolder> folders = await gateway.ListFoldersAsync(
            TestContext.Current.CancellationToken);
        ArchidektFolder created = await gateway.CreateFolderAsync(
            "New Folder",
            "10",
            TestContext.Current.CancellationToken);
        ArchidektMoveDecksResult moved = await gateway.MoveDecksAsync(
            ["123", "456"],
            "12",
            TestContext.Current.CancellationToken);

        folders.Should().HaveCount(2);
        folders.Should().Contain(folder => folder.Id == "10" && folder.Name == "Root");
        folders.Should().Contain(folder => folder.Id == "11" && folder.ParentFolderId == "10");
        created.Id.Should().Be("12");
        created.ParentFolderId.Should().Be("10");
        moved.Moved.Should().Be(2);

        RecordedRequest createRequest = handler.Requests.Single(request => request.Method == HttpMethod.Post);
        using JsonDocument createDocument = JsonDocument.Parse(createRequest.Body);
        createDocument.RootElement.GetProperty("name").GetString().Should().Be("New Folder");
        createDocument.RootElement.GetProperty("parent").GetInt32().Should().Be(10);

        RecordedRequest moveRequest = handler.Requests.Single(request => request.Path == "api/massUpdate/");
        using JsonDocument moveDocument = JsonDocument.Parse(moveRequest.Body);
        moveDocument.RootElement.GetProperty("deckIds").EnumerateArray()
            .Select(element => element.GetInt32())
            .Should()
            .Equal(123, 456);
        moveDocument.RootElement.GetProperty("parentFolder").GetInt32().Should().Be(12);
    }

    /// <summary>
    /// Verifies that create deck posts a private deck payload and maps the created workspace.
    /// </summary>
    [Fact]
    public async Task CreateDeck_PostsPrivateDeckAndMapsWorkspace()
    {
        RecordingHandler handler = new();
        handler.Post(
            "api/decks/v2/",
            """
            {
              "id": 456,
              "name": "Migrated",
              "deckFormat": 3,
              "description": "Copied deck",
              "categories": [
                { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true }
              ],
              "cards": []
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace workspace = await gateway.CreateDeckAsync(
            new ArchidektDeckCreateRequest
            {
                Name = "Migrated",
                Format = "commander",
                Description = "Copied deck",
                Visibility = "private",
            },
            TestContext.Current.CancellationToken);

        workspace.Mode.Should().Be(WorkspaceMode.Archidekt);
        workspace.WriteBack.Should().BeTrue();
        workspace.ArchidektDeckId.Should().Be("456");
        workspace.ArchidektDeckFormatId.Should().Be(3);
        workspace.Categories.Should().ContainSingle(category =>
            category.Name == DeckDefaults.Mainboard
            && category.ArchidektCategoryId == 1);

        RecordedRequest request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be("api/decks/v2/");
        request.Authorization.Should().Be("JWT test-jwt");
        using JsonDocument document = JsonDocument.Parse(request.Body);
        document.RootElement.GetProperty("name").GetString().Should().Be("Migrated");
        document.RootElement.GetProperty("deckFormat").GetInt32().Should().Be(3);
        document.RootElement.GetProperty("private").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("unlisted").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("theorycrafted").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("extras").GetProperty("decksToInclude").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Verifies that create deck can resolve a folder name before posting the deck payload.
    /// </summary>
    [Fact]
    public async Task CreateDeck_ResolvesFolderNameBeforePostingDeck()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/folderTree/",
            """{ "results": [ { "id": 42, "name": "Commander" } ] }""");
        handler.Post(
            "api/decks/v2/",
            """
            {
              "id": 456,
              "name": "Migrated",
              "deckFormat": 3,
              "categories": [],
              "cards": []
            }
            """);

        ArchidektGateway gateway = CreateGateway(handler);
        await gateway.CreateDeckAsync(
            new ArchidektDeckCreateRequest
            {
                Name = "Migrated",
                Format = "commander",
                Visibility = "private",
                FolderName = "Commander"
            },
            TestContext.Current.CancellationToken);

        RecordedRequest request = handler.Requests.Single(recorded => recorded.Method == HttpMethod.Post);
        using JsonDocument document = JsonDocument.Parse(request.Body);
        document.RootElement.GetProperty("parentFolder").GetInt32().Should().Be(42);
    }

    /// <summary>
    /// Verifies that create deck rejects folder names that do not identify exactly one folder.
    /// </summary>
    [Theory]
    [InlineData("""{ "results": [] }""", "*was not found*parentFolderId*")]
    [InlineData("""{ "results": [ { "id": 42, "name": "Commander" }, { "id": 43, "name": "Commander" } ] }""", "*matched 2 folders*parentFolderId*")]
    public async Task CreateDeck_RejectsUnknownOrAmbiguousFolderName(string folderTreeJson, string expectedMessage)
    {
        RecordingHandler handler = new();
        handler.Get("api/decks/folderTree/", folderTreeJson);

        ArchidektGateway gateway = CreateGateway(handler);
        Func<Task> act = () => gateway.CreateDeckAsync(
            new ArchidektDeckCreateRequest
            {
                Name = "Migrated",
                Format = "commander",
                Visibility = "private",
                FolderName = "Commander"
            },
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(expectedMessage);
        handler.Requests.Should().NotContain(request => request.Method == HttpMethod.Post);
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
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """
            { "cards": [ { "id": 77, "quantity": 1, "categories": ["Mainboard"], "card": { "id": 2, "oracleCard": { "name": "Sol Ring" } } } ] }
            """);
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": 3,
              "categories": [
                { "id": 2, "name": "Testing", "includedInDeck": true, "includedInPrice": true },
                { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true }
              ],
              "cards": [
                {
                  "id": 991,
                  "quantity": 2,
                  "categories": ["Testing", "Mainboard"],
                  "card": {
                    "id": 151147,
                    "oracleCard": { "name": "Lightning Bolt" }
                  }
                }
              ]
            }
            """
        );

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
            Categories = ["Testing", DeckDefaults.Mainboard, "testing"],
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
        firstCard.GetProperty("categories").EnumerateArray()
            .Select(category => category.GetString())
            .Should()
            .Equal("Testing", DeckDefaults.Mainboard);
        firstCard.GetProperty("modifications").GetProperty("quantity").GetInt32().Should().Be(2);
        firstCard.TryGetProperty("deckRelationId", out _).Should().BeFalse();
        firstCard.GetProperty("modifications").TryGetProperty("modifier", out _).Should().BeFalse();
        card.ArchidektCardId.Should().Be("151147");
        card.PrimaryCategory.Should().Be("Testing");
        card.ArchidektDeckRelationId.Should().Be(991);
    }

    /// <summary>
    /// Verifies that card resolution prefers imported Scryfall print ids.
    /// </summary>
    [Fact]
    public async Task PersistCards_PrefersScryfallPrintMatch()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/cards/v2/?name=Sol%20Ring&pageSize=25",
            """
            {
              "results": [
                { "id": 1, "uid": "wrong-print", "oracleCard": { "name": "Sol Ring" } },
                { "id": 2, "uid": "scryfall-sol-ring", "oracleCard": { "name": "Sol Ring" } }
              ]
            }
            """
        );
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """
            {
              "cards": [
                {
                  "id": 7,
                  "quantity": 1,
                  "categories": ["Mainboard"],
                  "card": { "id": 2, "uid": "scryfall-sol-ring", "oracleCard": { "name": "Sol Ring" } }
                }
              ]
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard card = new()
        {
            Name = "Sol Ring",
            Quantity = 1,
            ScryfallId = "scryfall-sol-ring",
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        string body = handler.Requests.Single(request => request.Method == HttpMethod.Patch).Body;
        using JsonDocument document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("cards")[0].GetProperty("cardid").GetInt32().Should().Be(2);
        card.ArchidektCardId.Should().Be("2");
    }

    /// <summary>
    /// Verifies that card-id resolution is persisted under Scryfall, print, and name keys.
    /// </summary>
    [Fact]
    public async Task PersistCards_StoresResolvedCardIdCacheKeys()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests", $"{Guid.NewGuid():N}.json");
        RecordingHandler handler = new();
        handler.Get(
            "api/cards/v2/?name=Sol%20Ring&pageSize=25",
            """
            { "results": [ { "id": 2, "uid": "scryfall-sol-ring", "setCode": "cmm", "collectorNumber": "400", "oracleCard": { "name": "Sol Ring" } } ] }
            """);
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """
            { "cards": [ { "id": 77, "quantity": 1, "categories": ["Mainboard"], "card": { "id": 2, "oracleCard": { "name": "Sol Ring" } } } ] }
            """);
        ArchidektGateway gateway = CreateAuthorizedGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                CardIdCacheFile = cacheFile,
            });
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard card = new()
        {
            Name = "Sol Ring",
            Quantity = 1,
            ScryfallId = "scryfall-sol-ring",
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
            Snapshot = new CardSnapshot { Set = "cmm", CollectorNumber = "400" },
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        string cacheText = File.ReadAllText(cacheFile);
        cacheText.Should().Contain("scryfall:scryfall-sol-ring");
        cacheText.Should().Contain("print:cmm:400");
        cacheText.Should().Contain("name:Sol Ring");
        using JsonDocument cacheDocument = JsonDocument.Parse(cacheText);
        JsonElement entry = cacheDocument.RootElement.GetProperty("scryfall:scryfall-sol-ring");
        entry.GetProperty("archidektId").GetString().Should().Be("2");
        entry.GetProperty("source").GetString().Should().Be("archidekt-card-search");
        entry.GetProperty("validationStatus").GetString().Should().Be("scryfall-print-match");
        card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution].Should().Be("resolved");
    }

    /// <summary>
    /// Verifies that duplicate unresolved cards share one Archidekt card search.
    /// </summary>
    [Fact]
    public async Task PersistCards_ReusesResolvedCardIdForDuplicateCards()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests", $"{Guid.NewGuid():N}.json");
        RecordingHandler handler = new();
        handler.Get(
            "api/cards/v2/?name=Sol%20Ring&pageSize=25",
            """
            { "results": [ { "id": 2, "uid": "scryfall-sol-ring", "setCode": "cmm", "collectorNumber": "400", "oracleCard": { "name": "Sol Ring" } } ] }
            """);
        handler.Patch("api/decks/123/modifyCards/v2/", "{}");
        ArchidektGateway gateway = CreateAuthorizedGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                CardIdCacheFile = cacheFile,
            });
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard mainboardCard = new()
        {
            Name = "Sol Ring",
            Quantity = 1,
            ScryfallId = "scryfall-sol-ring",
            ArchidektDeckRelationId = 11,
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
        };
        DeckCard maybeboardCard = new()
        {
            Name = "Sol Ring",
            Quantity = 1,
            ScryfallId = "scryfall-sol-ring",
            ArchidektDeckRelationId = 12,
            Categories = [DeckDefaults.Maybeboard],
            PrimaryCategory = DeckDefaults.Maybeboard,
        };

        await gateway.PersistCardsAsync(
            deck,
            [mainboardCard, maybeboardCard],
            [],
            TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle(request => request.Path.StartsWith("api/cards/v2/", StringComparison.Ordinal));
        string body = handler.Requests.Single(request => request.Method == HttpMethod.Patch).Body;
        using JsonDocument document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("cards").EnumerateArray()
            .Should()
            .OnlyContain(card => card.GetProperty("cardid").GetInt32() == 2);
        mainboardCard.ArchidektCardId.Should().Be("2");
        maybeboardCard.ArchidektCardId.Should().Be("2");
        mainboardCard.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution].Should().Be("resolved");
        maybeboardCard.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution].Should().Be("resolved");
    }

    /// <summary>
    /// Verifies that cached card ids skip Archidekt card search requests.
    /// </summary>
    [Fact]
    public async Task PersistCards_UsesCachedCardIdWithoutSearch()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        File.WriteAllText(cacheFile, """{ "scryfall:scryfall-sol-ring": "2" }""");
        RecordingHandler handler = new();
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """
            { "cards": [ { "id": 77, "quantity": 1, "categories": ["Mainboard"], "card": { "id": 2, "oracleCard": { "name": "Sol Ring" } } } ] }
            """);
        ArchidektGateway gateway = CreateAuthorizedGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                CardIdCacheFile = cacheFile,
            });
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard card = new()
        {
            Name = "Sol Ring",
            Quantity = 1,
            ScryfallId = "scryfall-sol-ring",
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        handler.Requests.Should().NotContain(request => request.Path.StartsWith("api/cards/v2/", StringComparison.Ordinal));
        handler.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Patch);
        card.ArchidektCardId.Should().Be("2");
        card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution].Should().Be("cache");

        using JsonDocument cacheDocument = JsonDocument.Parse(File.ReadAllText(cacheFile));
        JsonElement entry = cacheDocument.RootElement.GetProperty("scryfall:scryfall-sol-ring");
        entry.GetProperty("archidektId").GetString().Should().Be("2");
        entry.GetProperty("validationStatus").GetString().Should().Be("legacy-unvalidated");
    }

    /// <summary>
    /// Verifies that a rejected mutation evicts a stale cached card id and retries with a fresh id.
    /// </summary>
    [Fact]
    public async Task PersistCards_RefreshesStaleCachedCardIdAfterMutationRejection()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), "mtg-mcp-tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        File.WriteAllText(cacheFile, """{ "scryfall:scryfall-sol-ring": "999" }""");
        RecordingHandler handler = new();
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """{ "detail": "bad card id" }""",
            HttpStatusCode.BadRequest);
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": 3,
              "categories": [ { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true } ],
              "cards": []
            }
            """);
        handler.Get(
            "api/cards/v2/?name=Sol%20Ring&pageSize=25",
            """
            { "results": [ { "id": 2, "uid": "scryfall-sol-ring", "oracleCard": { "name": "Sol Ring" } } ] }
            """);
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """
            { "cards": [ { "id": 77, "quantity": 1, "categories": ["Mainboard"], "card": { "id": 2, "oracleCard": { "name": "Sol Ring" } } } ] }
            """);

        ArchidektGateway gateway = CreateAuthorizedGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                CardIdCacheFile = cacheFile,
            });
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard card = new()
        {
            Name = "Sol Ring",
            Quantity = 1,
            ScryfallId = "scryfall-sol-ring",
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        List<RecordedRequest> patches = handler.Requests
            .Where(request => request.Method == HttpMethod.Patch)
            .ToList();
        patches.Should().HaveCount(2);
        GetFirstMutationCardId(patches[0]).Should().Be(999);
        GetFirstMutationCardId(patches[1]).Should().Be(2);
        card.ArchidektCardId.Should().Be("2");
        card.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution].Should().Be("refreshed");

        using JsonDocument cacheDocument = JsonDocument.Parse(File.ReadAllText(cacheFile));
        cacheDocument.RootElement.GetProperty("scryfall:scryfall-sol-ring")
            .GetProperty("archidektId")
            .GetString()
            .Should()
            .Be("2");
    }

    /// <summary>
    /// Verifies that Archidekt-assigned relation ids are retained after adding a card.
    /// </summary>
    [Fact]
    public async Task PersistCards_AddCopiesDeckRelationIdFromResponse()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/cards/v2/?name=Mind%20Rot&pageSize=25",
            """
            { "results": [ { "id": 82308, "oracleCard": { "name": "Mind Rot" } } ] }
            """
        );
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """
            {
              "cards": [
                {
                  "id": 1521019999,
                  "quantity": 1,
                  "categories": ["Codex Manual Test"],
                  "card": {
                    "id": 82308,
                    "oracleCard": { "name": "Mind Rot" }
                  }
                }
              ]
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard card = new()
        {
            Name = "Mind Rot",
            Quantity = 1,
            Categories = ["Codex Manual Test"],
            PrimaryCategory = "Codex Manual Test",
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        card.ArchidektCardId.Should().Be("82308");
        card.ArchidektDeckRelationId.Should().Be(1521019999);
    }

    /// <summary>
    /// Verifies that relation id hydration tolerates Archidekt's eventual deck read consistency.
    /// </summary>
    [Fact]
    public async Task PersistCards_AddRetriesDeckReadUntilRelationAppears()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/cards/v2/?name=Mind%20Rot&pageSize=25",
            """
            { "results": [ { "id": 82308, "oracleCard": { "name": "Mind Rot" } } ] }
            """
        );
        handler.Patch("api/decks/123/modifyCards/v2/", "{}");
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": 3,
              "categories": [
                { "id": 1, "name": "Codex Fix Verification", "includedInDeck": false, "includedInPrice": false }
              ],
              "cards": []
            }
            """
        );
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": 3,
              "categories": [
                { "id": 1, "name": "Codex Fix Verification", "includedInDeck": false, "includedInPrice": false }
              ],
              "cards": [
                {
                  "id": 3085344231,
                  "quantity": 1,
                  "categories": ["Codex Fix Verification"],
                  "card": {
                    "id": 82308,
                    "oracleCard": { "name": "Mind Rot" }
                  }
                }
              ]
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard card = new()
        {
            Name = "Mind Rot",
            Quantity = 1,
            Categories = ["Codex Fix Verification"],
            PrimaryCategory = "Codex Fix Verification",
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        card.ArchidektDeckRelationId.Should().Be(3085344231L);
        handler
            .Requests.Where(request => request.Method == HttpMethod.Get && request.Path == "api/decks/123/")
            .Should()
            .HaveCount(2);
    }

    /// <summary>
    /// Verifies that transient Archidekt write-log failures are retried.
    /// </summary>
    [Fact]
    public async Task PersistCards_RetriesTransientWriteLogFailure()
    {
        RecordingHandler handler = new();
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """{ "error": "Uh oh, failed to create a log. Not saving anything" }""",
            HttpStatusCode.BadRequest
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
            Name = "Mind Rot",
            Quantity = 1,
            ArchidektCardId = "82308",
            ArchidektDeckRelationId = 1521020000,
            Categories = ["Codex Fix Verification"],
            PrimaryCategory = "Codex Fix Verification",
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        handler
            .Requests.Where(request => request.Method == HttpMethod.Patch)
            .Should()
            .HaveCount(2);
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
        cards[1].GetProperty("modifications").TryGetProperty("modifier", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that large card writes are split into Archidekt-sized batches.
    /// </summary>
    [Fact]
    public async Task PersistCards_SendsLargeMutationsInBatches()
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
        List<DeckCard> cards = Enumerable.Range(1, 51)
            .Select(index => new DeckCard
            {
                Name = $"Card {index}",
                Quantity = 1,
                ArchidektCardId = (1000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ArchidektDeckRelationId = 2000 + index,
                Categories = [DeckDefaults.Mainboard],
                PrimaryCategory = DeckDefaults.Mainboard,
            })
            .ToList();

        await gateway.PersistCardsAsync(deck, cards, [], TestContext.Current.CancellationToken);

        List<RecordedRequest> patchRequests = handler.Requests
            .Where(request => request.Method == HttpMethod.Patch)
            .ToList();
        patchRequests.Should().HaveCount(2);
        GetMutationCount(patchRequests[0]).Should().Be(50);
        GetMutationCount(patchRequests[1]).Should().Be(1);
    }

    /// <summary>
    /// Verifies that opaque batch failures are bisected down to the rejected card row.
    /// </summary>
    [Fact]
    public async Task PersistCards_BisectsOpaqueBatchFailure()
    {
        RecordingHandler handler = new();
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """{ "detail": "bad batch" }""",
            HttpStatusCode.BadRequest);
        handler.Get(
            "api/cards/v2/?name=Good%20Card&pageSize=25",
            """{ "results": [ { "id": 10, "oracleCard": { "name": "Good Card" } } ] }""");
        handler.Get(
            "api/cards/v2/?name=Bad%20Card&pageSize=25",
            """{ "results": [ { "id": 20, "oracleCard": { "name": "Bad Card" } } ] }""");
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """{ "detail": "bad batch" }""",
            HttpStatusCode.BadRequest);
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """{ "detail": "bad card" }""",
            HttpStatusCode.BadRequest);
        handler.Patch("api/decks/123/modifyCards/v2/", "{}");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCard good = new()
        {
            Name = "Good Card",
            Quantity = 1,
            ArchidektCardId = "old-10",
            ArchidektDeckRelationId = 101,
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
        };
        DeckCard bad = new()
        {
            Name = "Bad Card",
            Quantity = 1,
            ArchidektCardId = "old-20",
            ArchidektDeckRelationId = 102,
            Categories = [DeckDefaults.Mainboard],
            PrimaryCategory = DeckDefaults.Mainboard,
        };

        Func<Task> act = () => gateway.PersistCardsAsync(
            deck,
            [bad, good],
            [],
            TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bad Card*archidektCardId=20*");

        List<RecordedRequest> patchRequests = handler.Requests
            .Where(request => request.Method == HttpMethod.Patch)
            .ToList();
        patchRequests.Should().HaveCount(4);
        patchRequests.Select(GetMutationCount).Should().Equal(2, 2, 1, 1);
        GetFirstMutationCardId(patchRequests[1]).Should().Be(20);
        GetFirstMutationCardId(patchRequests[2]).Should().Be(20);
        GetFirstMutationCardId(patchRequests[3]).Should().Be(10);
        good.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution].Should().Be("refreshed");
        bad.Metadata[DeckCardMetadataKeys.ArchidektCardIdResolution].Should().Be("refreshed");
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
    /// Verifies that persist category marks the standard Commander category as premier.
    /// </summary>
    [Fact]
    public async Task PersistCategory_SetsPremierForCommanderCategory()
    {
        RecordingHandler handler = new();
        handler.Post("api/decks/createCategory/", """{ "id": 9, "name": "Commander" }""");

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = new()
        {
            Mode = WorkspaceMode.Archidekt,
            WriteBack = true,
            ArchidektDeckId = "123",
        };
        DeckCategory category = new()
        {
            Name = DeckRoles.Commander,
            IncludedInDeck = true,
            IncludedInPrice = true,
        };

        await gateway.PersistCategoryAsync(deck, category, TestContext.Current.CancellationToken);

        category.IsPremier.Should().BeTrue();
        handler.Requests[0].Body.Should().Contain("\"isPremier\":true");
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
            ArchidektDeckFormatId = 3,
        };

        await gateway.PersistMetadataAsync(deck, TestContext.Current.CancellationToken);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Path.Should().Be("api/decks/123/update/");
        handler.Requests[0].Body.Should().Contain("\"deckFormat\":3");
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
    /// Verifies that username password login is the configured credential source.
    /// </summary>
    [Fact]
    public async Task UsernamePasswordLogin_IsCredentialSource()
    {
        RecordingHandler handler = new();
        handler.Post(
            "api/rest-auth/login/",
            """{ "key": "login-jwt", "user": { "id": 42 } }"""
        );
        handler.Get("api/users/42/decks/", """{ "decks": [] }""");

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
        handler.Requests[1].Path.Should().Be("api/users/42/decks/");
        handler.Requests[1].Authorization.Should().Be("JWT login-jwt");
    }

    /// <summary>
    /// Verifies that read-only imports attempt configured credentials for private Archidekt decks.
    /// </summary>
    [Fact]
    public async Task ImportDeck_AuthenticatesReadOnlyImportWhenCredentialsAreConfigured()
    {
        RecordingHandler handler = new();
        handler.Post(
            "api/rest-auth/login/",
            """{ "access_token": "login-jwt", "user": { "id": 42 } }""");
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Private Deck",
              "deckFormat": 3,
              "categories": [],
              "cards": []
            }
            """);

        ArchidektGateway gateway = CreateGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                Username = "user",
                Password = "pass",
                EnableUsernamePasswordLogin = true,
            });

        DeckWorkspace deck = await gateway.ImportDeckAsync(
            "123",
            writeBack: false,
            TestContext.Current.CancellationToken);

        deck.Name.Should().Be("Private Deck");
        handler.Requests.Select(request => request.Path).Should().Equal(
            "api/rest-auth/login/",
            "api/decks/123/");
        handler.Requests[1].Authorization.Should().Be("JWT login-jwt");
    }

    /// <summary>
    /// Verifies that public read-only imports still work anonymously when no credentials are configured.
    /// </summary>
    [Fact]
    public async Task ImportDeck_AllowsAnonymousReadOnlyImportWithoutCredentials()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Public Deck",
              "deckFormat": 3,
              "categories": [],
              "cards": []
            }
            """);

        ArchidektGateway gateway = CreateGateway(
            handler,
            new ArchidektOptions { BaseAddress = new Uri("https://archidekt.test/") });

        DeckWorkspace deck = await gateway.ImportDeckAsync(
            "123",
            writeBack: false,
            TestContext.Current.CancellationToken);

        deck.Name.Should().Be("Public Deck");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Authorization.Should().BeNull();
    }

    /// <summary>
    /// Verifies that email-shaped usernames use Archidekt's browser login payload shape.
    /// </summary>
    [Fact]
    public async Task UsernamePasswordLogin_WithEmailValueUsesArchidektBrowserPayloadShape()
    {
        RecordingHandler handler = new();
        handler.Post(
            "api/rest-auth/login/",
            """{ "token": "login-jwt", "user": { "id": 42 } }"""
        );
        handler.Get("api/users/42/decks/", """{ "decks": [] }""");

        ArchidektGateway gateway = CreateGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                Username = "user@example.test",
                Password = "pass",
                EnableUsernamePasswordLogin = true,
            }
        );

        await gateway.ListDecksAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].Body.Should().Contain("\"email\":\"user@example.test\"");
        handler.Requests[0].Body.Should().NotContain("\"username\"");
        handler.Requests[1].Path.Should().Be("api/users/42/decks/");
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

            status.HasUsernamePassword.Should().BeTrue();
            status.HasCredentialsFile.Should().BeTrue();
            status.Mode.Should().Be("username-password");
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
    /// Verifies that key-value credential files preserve passwords with JSON-reserved characters.
    /// </summary>
    [Fact]
    public async Task UsernamePasswordCredentialFile_AllowsKeyValuePasswordsWithoutJsonEscaping()
    {
        string credentialsFile = Path.Combine(
            Path.GetTempPath(),
            "mtg-mcp-tests",
            $"{Guid.NewGuid():N}.credentials"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(credentialsFile)!);
        const string password = "pa\\ss\"word=with#chars!";
        await File.WriteAllTextAsync(
            credentialsFile,
            $"username=file-user{Environment.NewLine}password={password}{Environment.NewLine}",
            TestContext.Current.CancellationToken
        );

        try
        {
            RecordingHandler handler = new();
            handler.Post(
                "api/rest-auth/login/",
                """{ "key": "login-jwt", "user": { "id": 42 } }"""
            );
            handler.Get("api/users/42/decks/", """{ "decks": [] }""");

            ArchidektGateway gateway = CreateGateway(
                handler,
                new ArchidektOptions
                {
                    BaseAddress = new Uri("https://archidekt.test/"),
                    CredentialsFile = credentialsFile,
                    EnableUsernamePasswordLogin = true,
                }
            );

            await gateway.ListDecksAsync(TestContext.Current.CancellationToken);

            using JsonDocument loginBody = JsonDocument.Parse(handler.Requests[0].Body);
            loginBody.RootElement.GetProperty("username").GetString().Should().Be("file-user");
            loginBody.RootElement.GetProperty("password").GetString().Should().Be(password);
            handler.Requests[1].Path.Should().Be("api/users/42/decks/");
            handler.Requests[1].Authorization.Should().Be("JWT login-jwt");
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
    /// Verifies that malformed credential files report a sanitized parse error.
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
            "{ 'username': 'file-user', 'password': 'super-secret' }",
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

            status.HasCredentialsFile.Should().BeTrue();
            status.HasUsernamePassword.Should().BeFalse();
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
    /// Verifies that failed Archidekt responses do not expose secret response bodies.
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
    /// Verifies that deck reads retry Archidekt throttle responses.
    /// </summary>
    [Fact]
    public async Task ImportDeck_RetriesRateLimitResponse()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/123/",
            """{ "detail": "Request was throttled. Expected available in 55 seconds." }""",
            HttpStatusCode.TooManyRequests,
            TimeSpan.Zero
        );
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": 3,
              "categories": [],
              "cards": []
            }
            """
        );

        ArchidektGateway gateway = CreateGateway(handler);
        DeckWorkspace deck = await gateway.ImportDeckAsync(
            "123",
            writeBack: false,
            TestContext.Current.CancellationToken
        );

        deck.Name.Should().Be("Deck");
        handler
            .Requests.Where(request => request.Method == HttpMethod.Get && request.Path == "api/decks/123/")
            .Should()
            .HaveCount(2);
    }

    /// <summary>
    /// Verifies that configured proactive Archidekt pacing delays requests before sending them.
    /// </summary>
    [Fact]
    public async Task ImportDeck_AppliesConfiguredRequestPacing()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/decks/123/",
            """
            {
              "id": 123,
              "name": "Deck",
              "deckFormat": 3,
              "categories": [],
              "cards": []
            }
            """
        );
        handler.Get(
            "api/decks/456/",
            """
            {
              "id": 456,
              "name": "Other Deck",
              "deckFormat": 3,
              "categories": [],
              "cards": []
            }
            """
        );
        ArchidektGateway gateway = CreateGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                RateLimit = new ArchidektRateLimitOptions
                {
                    MaxRequests = 1,
                    WindowSeconds = 1
                }
            });

        Stopwatch timer = Stopwatch.StartNew();
        await gateway.ImportDeckAsync("123", writeBack: false, TestContext.Current.CancellationToken);
        await gateway.ImportDeckAsync("456", writeBack: false, TestContext.Current.CancellationToken);
        timer.Stop();

        timer.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        handler.Requests.Where(request => request.Method == HttpMethod.Get).Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that card writes retry Archidekt throttle responses.
    /// </summary>
    [Fact]
    public async Task PersistCards_RetriesRateLimitResponse()
    {
        RecordingHandler handler = new();
        handler.Patch(
            "api/decks/123/modifyCards/v2/",
            """{ "detail": "Request was throttled. Expected available in 55 seconds." }""",
            HttpStatusCode.TooManyRequests,
            TimeSpan.Zero
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
            Name = "Mind Rot",
            Quantity = 1,
            ArchidektCardId = "82308",
            ArchidektDeckRelationId = 3085344231,
            Categories = ["Discard"],
            PrimaryCategory = "Discard",
        };

        await gateway.PersistCardsAsync(deck, [card], [], TestContext.Current.CancellationToken);

        handler
            .Requests.Where(request => request.Method == HttpMethod.Patch)
            .Should()
            .HaveCount(2);
    }

    /// <summary>
    /// Verifies that bodyless mutating requests retry Archidekt throttle responses.
    /// </summary>
    [Fact]
    public async Task DeleteCategory_RetriesRateLimitResponse()
    {
        RecordingHandler handler = new();
        handler.Delete(
            "api/decks/category/9/",
            """{ "detail": "Request was throttled. Expected available in 55 seconds." }""",
            HttpStatusCode.TooManyRequests,
            TimeSpan.Zero
        );
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
            new DeckCategory { Name = "Test", ArchidektCategoryId = 9 },
            TestContext.Current.CancellationToken
        );

        handler
            .Requests.Where(request => request.Method == HttpMethod.Delete)
            .Should()
            .HaveCount(2);
    }

    /// <summary>
    /// Counts card mutation payload entries in a recorded request body.
    /// </summary>
    private static int GetMutationCount(RecordedRequest request)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        return document.RootElement.GetProperty("cards").GetArrayLength();
    }

    /// <summary>
    /// Reads the first card id from a recorded mutation request.
    /// </summary>
    private static int GetFirstMutationCardId(RecordedRequest request)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        return document.RootElement.GetProperty("cards")[0].GetProperty("cardid").GetInt32();
    }

    /// <summary>
    /// Creates a gateway with default test options.
    /// </summary>
    private static ArchidektGateway CreateGateway(RecordingHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://archidekt.test/") };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("JWT", "test-jwt");

        return new ArchidektGateway(
            httpClient,
            Options.Create(new ArchidektOptions { BaseAddress = new Uri("https://archidekt.test/") })
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
    /// Creates a gateway with supplied options and an existing auth header.
    /// </summary>
    private static ArchidektGateway CreateAuthorizedGateway(
        RecordingHandler handler,
        ArchidektOptions options)
    {
        HttpClient httpClient = new(handler) { BaseAddress = options.BaseAddress };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("JWT", "test-jwt");
        return new ArchidektGateway(httpClient, Options.Create(options));
    }

    /// <summary>
    /// Provides recording handler behavior.
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        /// <summary>
        /// Stores configured responses by method and request path.
        /// </summary>
        private readonly Dictionary<(HttpMethod Method, string Path), Queue<RecordedResponse>> responses =
            new();

        /// <summary>
        /// Gets or sets the requests.
        /// </summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>
        /// Reads a recorded response header value.
        /// </summary>
        public void Get(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            TimeSpan? retryAfter = null
        )
        {
            AddResponse(HttpMethod.Get, path, response, statusCode, retryAfter);
        }

        /// <summary>
        /// Verifies that post.
        /// </summary>
        public void Post(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            TimeSpan? retryAfter = null
        )
        {
            AddResponse(HttpMethod.Post, path, response, statusCode, retryAfter);
        }

        /// <summary>
        /// Verifies that patch.
        /// </summary>
        public void Patch(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            TimeSpan? retryAfter = null
        )
        {
            AddResponse(HttpMethod.Patch, path, response, statusCode, retryAfter);
        }

        /// <summary>
        /// Verifies that delete.
        /// </summary>
        public void Delete(
            string path,
            string response,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            TimeSpan? retryAfter = null
        )
        {
            AddResponse(HttpMethod.Delete, path, response, statusCode, retryAfter);
        }

        /// <summary>
        /// Adds a response and preserves insertion order for repeated matching requests.
        /// </summary>
        private void AddResponse(
            HttpMethod method,
            string path,
            string response,
            HttpStatusCode statusCode,
            TimeSpan? retryAfter
        )
        {
            if (!responses.TryGetValue((method, path), out Queue<RecordedResponse>? queue))
            {
                queue = new Queue<RecordedResponse>();
                responses[(method, path)] = queue;
            }

            queue.Enqueue(new RecordedResponse(response, statusCode, retryAfter));
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
            HttpResponseMessage message = new(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            };
            if (response.RetryAfter.HasValue)
            {
                message.Headers.RetryAfter = new RetryConditionHeaderValue(
                    response.RetryAfter.Value
                );
            }

            return message;
        }
    }

    /// <summary>
    /// Represents recorded response.
    /// </summary>
    private sealed record RecordedResponse(
        string Body,
        HttpStatusCode StatusCode,
        TimeSpan? RetryAfter
    );

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
