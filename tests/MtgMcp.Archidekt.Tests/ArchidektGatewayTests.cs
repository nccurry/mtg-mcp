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
              "deckFormat": "3",
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
        deck.Format.Should().Be("commander");
        deck.Categories.Should()
            .Contain(category => category.Name == "Maybeboard" && category.IncludedInDeck == false);
        deck.Cards.Should().ContainSingle();
        deck.Cards[0].Name.Should().Be("Lightning Bolt");
        deck.Cards[0].Categories.Should().BeEquivalentTo(["Mainboard", "Maybeboard"]);
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
        card.Snapshot.TypeLine.Should().Be("Legendary Creature - God");
        card.Snapshot.Set.Should().Be("khm");
        card.Snapshot.OracleText.Should().Contain("Whenever an opponent sacrifices");
        card.Snapshot.OracleText.Should().Contain("Target player loses 3 life");
        card.Snapshot.ColorIdentity.Should().ContainSingle().Which.Should().Be("B");
        card.Snapshot.Prices["usd"].Should().Be("17.65");
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
    /// Verifies that list decks maps results and uses configured jwt.
    /// </summary>
    [Fact]
    public async Task ListDecks_MapsResultsAndUsesConfiguredJwt()
    {
        RecordingHandler handler = new();
        handler.Get(
            "api/users/278245/decks/",
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
        handler.Requests.Single().Path.Should().Be("api/users/278245/decks/");
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
                  "id": 991,
                  "quantity": 2,
                  "categories": ["Mainboard"],
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
        firstCard.TryGetProperty("deckRelationId", out _).Should().BeFalse();
        firstCard.GetProperty("modifications").TryGetProperty("modifier", out _).Should().BeFalse();
        card.ArchidektCardId.Should().Be("151147");
        card.ArchidektDeckRelationId.Should().Be(991);
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
            """{ "key": "login-jwt", "refresh_token": "refresh-token", "user": { "id": 42 } }"""
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
    /// Verifies that email credentials use Archidekt's browser login payload shape.
    /// </summary>
    [Fact]
    public async Task EmailPasswordLogin_UsesArchidektBrowserPayloadShape()
    {
        RecordingHandler handler = new();
        handler.Post(
            "api/rest-auth/login/",
            """{ "token": "login-jwt", "refresh_token": "refresh-token", "user": { "id": 42 } }"""
        );
        handler.Get("api/users/42/decks/", """{ "decks": [] }""");

        ArchidektGateway gateway = CreateGateway(
            handler,
            new ArchidektOptions
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                Email = "user@example.test",
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
              "jwt": "file-jwt",
              "refreshToken": "file-refresh",
              "email": "file@example.test",
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
            status.HasEmailPassword.Should().BeTrue();
            status.HasUsernamePassword.Should().BeTrue();
            status.HasLoginPassword.Should().BeTrue();
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
                """{ "key": "login-jwt", "refresh_token": "refresh-token", "user": { "id": 42 } }"""
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
            status.HasLoginPassword.Should().BeFalse();
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
        handler.Get("api/users/278245/decks/", """{ "token": "secret-token" }""", HttpStatusCode.BadRequest);
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
                UserId = "278245",
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
        public void Get(string path, string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            AddResponse(HttpMethod.Get, path, response, statusCode);
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
            AddResponse(HttpMethod.Post, path, response, statusCode);
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
            AddResponse(HttpMethod.Patch, path, response, statusCode);
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
            AddResponse(HttpMethod.Delete, path, response, statusCode);
        }

        /// <summary>
        /// Adds a response and preserves insertion order for repeated matching requests.
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
