using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;
using MtgMcp.Scryfall;
using RichardSzalay.MockHttp;

namespace MtgMcp.Scryfall.Tests;

public sealed class ScryfallClientTests
{
    [Fact]
    public async Task GetCard_MapsCoreCardFields()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/named*")
            .Respond("application/json", """
            {
              "id": "card-1",
              "oracle_id": "oracle-1",
              "name": "Lightning Bolt",
              "mana_cost": "{R}",
              "cmc": 1,
              "type_line": "Instant",
              "oracle_text": "Deal 3 damage.",
              "set": "clu",
              "collector_number": "141",
              "rarity": "common",
              "scryfall_uri": "https://scryfall.com/card/clu/141",
              "colors": ["R"],
              "color_identity": ["R"],
              "legalities": { "commander": "legal" },
              "prices": { "usd": "0.25" },
              "image_uris": { "normal": "https://img.test/bolt.jpg" }
            }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        CardInfo? card = await client.GetCardAsync("Lightning Bolt", TestContext.Current.CancellationToken);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Lightning Bolt");
        card.ColorIdentity.Should().ContainSingle().Which.Should().Be("R");
        card.Legalities["commander"].Should().Be("legal");
        card.ImageUris["normal"].Should().Contain("bolt");
    }

    [Fact]
    public async Task SearchCards_RespectsLimit()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/search*")
            .Respond("application/json", """
            {
              "data": [
                { "id": "1", "name": "Card One", "type_line": "Creature", "set": "abc" },
                { "id": "2", "name": "Card Two", "type_line": "Instant", "set": "abc" }
              ]
            }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SearchCardsAsync("o:draw", 1, TestContext.Current.CancellationToken);

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Card One");
    }

    [Fact]
    public async Task GetRulings_FetchesNamedCardThenRulings()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/named*")
            .Respond("application/json", """{ "id": "card-1", "name": "Lightning Bolt" }""");
        mockHttp.When("https://api.scryfall.test/cards/card-1/rulings")
            .Respond("application/json", """
            {
              "data": [
                { "source": "wotc", "published_at": "2024-01-01", "comment": "A ruling." }
              ]
            }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<RulingInfo> rulings = await client.GetRulingsAsync("Lightning Bolt", TestContext.Current.CancellationToken);

        rulings.Should().ContainSingle();
        rulings[0].Source.Should().Be("wotc");
        rulings[0].Text.Should().Be("A ruling.");
    }

    [Fact]
    public async Task GetRulings_ReturnsEmptyWhenCardIsNotFound()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/named*")
            .Respond(HttpStatusCode.NotFound, "application/json", """
            { "object": "error", "code": "not_found" }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<RulingInfo> rulings = await client.GetRulingsAsync("Nope", TestContext.Current.CancellationToken);

        rulings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPrints_UsesOracleIdAndMapsEveryPrint()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect("https://api.scryfall.test/cards/named?fuzzy=Lightning%20Bolt")
            .Respond("application/json", """{ "id": "card-1", "oracle_id": "oracle-1", "name": "Lightning Bolt" }""");
        mockHttp.Expect("https://api.scryfall.test/cards/search?q=oracleid%3Aoracle-1&unique=prints&order=released")
            .Respond("application/json", """
            {
              "data": [
                { "id": "print-1", "name": "Lightning Bolt", "set": "lea", "collector_number": "161" },
                { "id": "print-2", "name": "Lightning Bolt", "set": "clu", "collector_number": "141" }
              ]
            }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardInfo> prints = await client.GetPrintsAsync("Lightning Bolt", TestContext.Current.CancellationToken);

        prints.Should().HaveCount(2);
        prints[0].Set.Should().Be("lea");
        prints[1].CollectorNumber.Should().Be("141");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetCard_MapsDoubleFacedCardsFromFaces()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/named*")
            .Respond("application/json", """
            {
              "id": "card-1",
              "name": "Fire // Ice",
              "cmc": 2,
              "card_faces": [
                {
                  "mana_cost": "{1}{R}",
                  "type_line": "Instant",
                  "oracle_text": "Fire deals 2 damage.",
                  "image_uris": { "normal": "https://img.test/fire.jpg" }
                },
                {
                  "mana_cost": "{1}{U}",
                  "type_line": "Instant",
                  "oracle_text": "Tap target permanent."
                }
              ],
              "color_identity": ["R", "U"]
            }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        CardInfo? card = await client.GetCardAsync("Fire Ice", TestContext.Current.CancellationToken);

        card.Should().NotBeNull();
        card!.ManaCost.Should().Be("{1}{R}");
        card.TypeLine.Should().Be("Instant");
        card.OracleText.Should().Contain("Fire deals");
        card.ColorIdentity.Should().BeEquivalentTo(["R", "U"]);
        card.ImageUris["normal"].Should().Contain("fire");
    }

    [Fact]
    public async Task SuggestCards_AppendsFormatLegality()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect("https://api.scryfall.test/cards/search?q=o%3Adraw%20legal%3Acommander&unique=cards&order=edhrec")
            .Respond("application/json", """
            { "data": [ { "id": "1", "name": "Opt", "type_line": "Instant" } ] }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SuggestCardsAsync("o:draw", "commander", 10, TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Name.Should().Be("Opt");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task GetCard_ThrowsForNonNotFoundErrors()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/named*")
            .Respond(HttpStatusCode.InternalServerError, "application/json", """
            { "object": "error", "details": "service unavailable" }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        Func<Task> act = () => client.GetCardAsync("Lightning Bolt", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*500*service unavailable*");
    }

    [Fact]
    public async Task GetCard_ReturnsNullForScryfallNotFound()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/named*")
            .Respond(HttpStatusCode.NotFound, "application/json", """
            { "object": "error", "code": "not_found", "details": "No cards found." }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        CardInfo? card = await client.GetCardAsync("Not A Real Card", TestContext.Current.CancellationToken);

        card.Should().BeNull();
    }

    [Fact]
    public async Task SearchCards_ReturnsEmptyListForScryfallNotFound()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When("https://api.scryfall.test/cards/search*")
            .Respond(HttpStatusCode.NotFound, "application/json", """
            { "object": "error", "code": "not_found", "details": "No cards found." }
            """);

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SearchCardsAsync("zzzzzz", 10, TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    private static ScryfallClient CreateClient(MockHttpMessageHandler mockHttp)
    {
        HttpClient httpClient = mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("https://api.scryfall.test/");
        return new ScryfallClient(
            httpClient,
            Options.Create(new ScryfallOptions
            {
                BaseAddress = new Uri("https://api.scryfall.test/"),
                MinimumDelay = TimeSpan.Zero,
                UserAgent = "mtg-mcp-test/1.0"
            }));
    }
}
