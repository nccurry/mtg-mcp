using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;
using MtgMcp.Scryfall;
using RichardSzalay.MockHttp;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Contains tests for scryfall client.
/// </summary>
public sealed class ScryfallClientTests
{
    /// <summary>
    /// Verifies that get card maps core card fields.
    /// </summary>
    [Fact]
    public async Task GetCard_MapsCoreCardFields()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/named*")
            .Respond(
                "application/json",
                """
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
                  "edhrec_rank": 42,
                  "colors": ["R"],
                  "color_identity": ["R"],
                  "keywords": ["Flash"],
                  "produced_mana": ["R"],
                  "legalities": { "commander": "legal" },
                  "prices": { "usd": "0.25" },
                  "image_uris": { "normal": "https://img.test/bolt.jpg" }
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        CardInfo? card = await client.GetCardAsync(
            "Lightning Bolt",
            TestContext.Current.CancellationToken
        );

        card.Should().NotBeNull();
        card!.Name.Should().Be("Lightning Bolt");
        card.EdhrecRank.Should().Be(42);
        card.Keywords.Should().Contain("Flash");
        card.ProducedMana.Should().Contain("R");
        card.ColorIdentity.Should().ContainSingle().Which.Should().Be("R");
        card.Legalities["commander"].Should().Be("legal");
        card.ImageUris["normal"].Should().Contain("bolt");
    }

    /// <summary>
    /// Verifies that get cards by names posts collection and maps returned cards.
    /// </summary>
    [Fact]
    public async Task GetCardsByNames_PostsCollectionAndMapsReturnedCards()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .Expect(HttpMethod.Post, "https://api.scryfall.test/cards/collection")
            .WithContent("""{"identifiers":[{"name":"Sol Ring"},{"name":"Arcane Signet"}]}""")
            .Respond(
                "application/json",
                """
                {
                  "data": [
                    {
                      "id": "sol-ring",
                      "name": "Sol Ring",
                      "type_line": "Artifact",
                      "oracle_text": "{T}: Add {C}{C}.",
                      "edhrec_rank": 1,
                      "produced_mana": ["C"],
                      "prices": { "usd": "1.25" }
                    },
                    {
                      "id": "arcane-signet",
                      "name": "Arcane Signet",
                      "type_line": "Artifact",
                      "oracle_text": "{T}: Add one mana of any color.",
                      "edhrec_rank": 5,
                      "produced_mana": ["W", "U", "B", "R", "G"],
                      "prices": { "usd": "1.00" }
                    }
                  ]
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyDictionary<string, CardInfo> cards = await client.GetCardsByNamesAsync(
            ["Sol Ring", "Arcane Signet"],
            TestContext.Current.CancellationToken
        );

        cards.Should().ContainKeys("Sol Ring", "Arcane Signet");
        cards["Sol Ring"].EdhrecRank.Should().Be(1);
        cards["Arcane Signet"].ProducedMana.Should().BeEquivalentTo(["W", "U", "B", "R", "G"]);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that get cards by names resolves multi-face cards by face aliases.
    /// </summary>
    [Fact]
    public async Task GetCardsByNames_ResolvesMultiFaceCardsByAliases()
    {
        const string expectedRequest =
            """
            {"identifiers":[{"name":"Murderous Rider // Swift End"},{"name":"Murderous Rider"},{"name":"Swift End"}]}
            """;
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .Expect(HttpMethod.Post, "https://api.scryfall.test/cards/collection")
            .WithContent(expectedRequest)
            .Respond(
                "application/json",
                """
                {
                  "not_found": [
                    { "name": "Murderous Rider // Swift End" }
                  ],
                  "data": [
                    {
                      "id": "murderous-rider",
                      "name": "Murderous Rider // Swift End",
                      "type_line": "Creature — Zombie Knight // Instant — Adventure",
                      "card_faces": [
                        {
                          "name": "Murderous Rider",
                          "oracle_text": "Lifelink"
                        },
                        {
                          "name": "Swift End",
                          "oracle_text": "Destroy target creature or planeswalker. You lose 2 life."
                        }
                      ],
                      "prices": { "usd": "0.41" }
                    }
                  ]
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyDictionary<string, CardInfo> cards = await client.GetCardsByNamesAsync(
            ["Murderous Rider // Swift End"],
            TestContext.Current.CancellationToken
        );

        cards.Should().ContainKey("Murderous Rider // Swift End");
        CardInfo card = cards["Murderous Rider // Swift End"];
        card.Name.Should().Be("Murderous Rider // Swift End");
        card.OracleText.Should().Contain("Lifelink");
        card.OracleText.Should().Contain("Destroy target creature");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that search cards respects limit.
    /// </summary>
    [Fact]
    public async Task SearchCards_RespectsLimit()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/search*")
            .Respond(
                "application/json",
                """
                {
                  "data": [
                    { "id": "1", "name": "Card One", "type_line": "Creature", "set": "abc" },
                    { "id": "2", "name": "Card Two", "type_line": "Instant", "set": "abc" }
                  ]
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SearchCardsAsync(
            "o:draw",
            1,
            TestContext.Current.CancellationToken
        );

        results.Should().ContainSingle();
        results[0].Name.Should().Be("Card One");
    }

    /// <summary>
    /// Verifies that search cards follows pagination until the limit is reached.
    /// </summary>
    [Fact]
    public async Task SearchCards_FollowsPagination()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .Expect("https://api.scryfall.test/cards/search?q=o%3Adraw&unique=cards&order=edhrec")
            .Respond(
                "application/json",
                """
                {
                  "has_more": true,
                  "next_page": "https://api.scryfall.test/cards/search?page=2",
                  "data": [
                    { "id": "1", "name": "Card One", "type_line": "Instant" }
                  ]
                }
                """
            );
        mockHttp
            .Expect("https://api.scryfall.test/cards/search?page=2")
            .Respond(
                "application/json",
                """
                {
                  "has_more": false,
                  "data": [
                    { "id": "2", "name": "Card Two", "type_line": "Instant" }
                  ]
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SearchCardsAsync(
            "o:draw",
            2,
            TestContext.Current.CancellationToken);

        results.Select(result => result.Name).Should().Equal("Card One", "Card Two");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that Game Changer search uses Scryfall search syntax.
    /// </summary>
    [Fact]
    public async Task SearchCards_UsesGameChangerQueryShape()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .Expect("https://api.scryfall.test/cards/search?q=is%3Agame-changer&unique=cards&order=edhrec")
            .Respond(
                "application/json",
                """
                {
                  "has_more": false,
                  "data": [
                    { "id": "1", "name": "Mana Crypt", "type_line": "Artifact" }
                  ]
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SearchCardsAsync(
            "is:game-changer",
            250,
            TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Name.Should().Be("Mana Crypt");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that get rulings fetches named card then rulings.
    /// </summary>
    [Fact]
    public async Task GetRulings_FetchesNamedCardThenRulings()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/named*")
            .Respond("application/json", """{ "id": "card-1", "name": "Lightning Bolt" }""");
        mockHttp
            .When("https://api.scryfall.test/cards/card-1/rulings")
            .Respond(
                "application/json",
                """
                {
                  "data": [
                    { "source": "wotc", "published_at": "2024-01-01", "comment": "A ruling." }
                  ]
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<RulingInfo> rulings = await client.GetRulingsAsync(
            "Lightning Bolt",
            TestContext.Current.CancellationToken
        );

        rulings.Should().ContainSingle();
        rulings[0].Source.Should().Be("wotc");
        rulings[0].Text.Should().Be("A ruling.");
    }

    /// <summary>
    /// Verifies that get rulings returns empty when card is not found.
    /// </summary>
    [Fact]
    public async Task GetRulings_ReturnsEmptyWhenCardIsNotFound()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/named*")
            .Respond(
                HttpStatusCode.NotFound,
                "application/json",
                """
                { "object": "error", "code": "not_found" }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<RulingInfo> rulings = await client.GetRulingsAsync(
            "Nope",
            TestContext.Current.CancellationToken
        );

        rulings.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that get prints uses oracle id and maps every print.
    /// </summary>
    [Fact]
    public async Task GetPrints_UsesOracleIdAndMapsEveryPrint()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .Expect("https://api.scryfall.test/cards/named?fuzzy=Lightning%20Bolt")
            .Respond(
                "application/json",
                """{ "id": "card-1", "oracle_id": "oracle-1", "name": "Lightning Bolt" }"""
            );
        mockHttp
            .Expect(
                "https://api.scryfall.test/cards/search?q=oracleid%3Aoracle-1&unique=prints&order=released"
            )
            .Respond(
                "application/json",
                """
                {
                  "data": [
                    { "id": "print-1", "name": "Lightning Bolt", "set": "lea", "collector_number": "161" },
                    { "id": "print-2", "name": "Lightning Bolt", "set": "clu", "collector_number": "141" }
                  ]
                }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardInfo> prints = await client.GetPrintsAsync(
            "Lightning Bolt",
            TestContext.Current.CancellationToken
        );

        prints.Should().HaveCount(2);
        prints[0].Set.Should().Be("lea");
        prints[1].CollectorNumber.Should().Be("141");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that get card maps double faced cards from faces.
    /// </summary>
    [Fact]
    public async Task GetCard_MapsDoubleFacedCardsFromFaces()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/named*")
            .Respond(
                "application/json",
                """
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
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        CardInfo? card = await client.GetCardAsync(
            "Fire Ice",
            TestContext.Current.CancellationToken
        );

        card.Should().NotBeNull();
        card!.ManaCost.Should().Be("{1}{R}");
        card.TypeLine.Should().Be("Instant");
        card.OracleText.Should().Contain("Fire deals");
        card.ColorIdentity.Should().BeEquivalentTo(["R", "U"]);
        card.ImageUris["normal"].Should().Contain("fire");
    }

    /// <summary>
    /// Verifies that suggest cards appends format legality.
    /// </summary>
    [Fact]
    public async Task SuggestCards_AppendsFormatLegality()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .Expect(
                "https://api.scryfall.test/cards/search?q=o%3Adraw%20legal%3Acommander&unique=cards&order=edhrec"
            )
            .Respond(
                "application/json",
                """
                { "data": [ { "id": "1", "name": "Opt", "type_line": "Instant" } ] }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SuggestCardsAsync(
            "o:draw",
            "commander",
            10,
            TestContext.Current.CancellationToken
        );

        results.Should().ContainSingle().Which.Name.Should().Be("Opt");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that get card throws for non not found errors.
    /// </summary>
    [Fact]
    public async Task GetCard_ThrowsForNonNotFoundErrors()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/named*")
            .Respond(
                HttpStatusCode.InternalServerError,
                "application/json",
                """
                { "object": "error", "details": "service unavailable" }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        Func<Task> act = () =>
            client.GetCardAsync("Lightning Bolt", TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<HttpRequestException>()
            .WithMessage("*500*service unavailable*");
    }

    /// <summary>
    /// Verifies that get card returns null for scryfall not found.
    /// </summary>
    [Fact]
    public async Task GetCard_ReturnsNullForScryfallNotFound()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/named*")
            .Respond(
                HttpStatusCode.NotFound,
                "application/json",
                """
                { "object": "error", "code": "not_found", "details": "No cards found." }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        CardInfo? card = await client.GetCardAsync(
            "Not A Real Card",
            TestContext.Current.CancellationToken
        );

        card.Should().BeNull();
    }

    /// <summary>
    /// Verifies that search cards returns empty list for scryfall not found.
    /// </summary>
    [Fact]
    public async Task SearchCards_ReturnsEmptyListForScryfallNotFound()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .When("https://api.scryfall.test/cards/search*")
            .Respond(
                HttpStatusCode.NotFound,
                "application/json",
                """
                { "object": "error", "code": "not_found", "details": "No cards found." }
                """
            );

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SearchCardsAsync(
            "zzzzzz",
            10,
            TestContext.Current.CancellationToken
        );

        results.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that search cards retries once after Scryfall rate limiting.
    /// </summary>
    [Fact]
    public async Task SearchCards_RetriesRateLimitOnce()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp
            .Expect("https://api.scryfall.test/cards/search*")
            .Respond(
                _ =>
                {
                    HttpResponseMessage response = new(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent(
                            """{ "object": "error", "code": "rate_limited" }""",
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                    return response;
                });
        mockHttp
            .Expect("https://api.scryfall.test/cards/search*")
            .Respond(
                "application/json",
                """
                {
                  "data": [
                    { "id": "opt", "name": "Opt" }
                  ],
                  "has_more": false
                }
                """);

        ScryfallClient client = CreateClient(mockHttp);
        IReadOnlyList<CardSearchResult> results = await client.SearchCardsAsync(
            "o:scry",
            10,
            TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Name.Should().Be("Opt");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that create client.
    /// </summary>
    private static ScryfallClient CreateClient(MockHttpMessageHandler mockHttp)
    {
        HttpClient httpClient = mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("https://api.scryfall.test/");
        return new ScryfallClient(
            httpClient,
            Options.Create(
                new ScryfallOptions
                {
                    BaseAddress = new Uri("https://api.scryfall.test/"),
                    MinimumDelay = TimeSpan.Zero,
                    UserAgent = "mtg-mcp-test/1.0",
                }
            )
        );
    }
}
