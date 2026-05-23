using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MtgMcp.Core;
using MtgMcp.Decklists;
using RichardSzalay.MockHttp;

namespace MtgMcp.Decklists.Tests;

/// <summary>
/// Contains tests for structured decklist corpus adapters.
/// </summary>
public sealed class DecklistCorpusProviderTests
{
    /// <summary>
    /// Verifies that TopDeck maps structured tournament decklists and uses cache.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_MapsSignalsAndUsesCache()
    {
        MockHttpMessageHandler mockHttp = new();
        MockedRequest request = mockHttp.When(HttpMethod.Post, "https://topdeck.test/v2/tournaments")
            .Respond("application/json", TopDeckResponseJson);
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://topdeck.test/"),
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions()),
            Options.Create(OptionsWithSource("TopDeck", "key")));

        CorpusSignalReport first = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);
        CorpusSignalReport second = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        first.ExemplarDecks.Should().ContainSingle(deck => deck.Name.Contains("Tinybones", StringComparison.OrdinalIgnoreCase));
        first.Signals.Should().Contain(signal => signal.CardName == "Waste Not" && signal.Source == "TopDeck.gg");
        second.Notes.Should().Contain(note => note.Contains("cache", StringComparison.OrdinalIgnoreCase));
        mockHttp.GetMatchCount(request).Should().Be(1);
    }

    /// <summary>
    /// Verifies that refresh bypasses TopDeck cache.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_RefreshBypassesCache()
    {
        MockHttpMessageHandler mockHttp = new();
        MockedRequest request = mockHttp.When(HttpMethod.Post, "https://topdeck.test/v2/tournaments")
            .Respond("application/json", TopDeckResponseJson);
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://topdeck.test/"),
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions()),
            Options.Create(OptionsWithSource("TopDeck", "key")));
        CorpusSignalQuery query = Query();

        await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);
        query.Refresh = true;
        await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        mockHttp.GetMatchCount(request).Should().Be(2);
    }

    /// <summary>
    /// Verifies that Spicerack maps structured decklists.
    /// </summary>
    [Fact]
    public async Task SpicerackProvider_MapsSignals()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://spicerack.test/api/export-decklists/*")
            .Respond("application/json", SpicerackResponseJson);
        SpicerackCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://spicerack.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Spicerack", "key")));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.ExemplarDecks.Should().ContainSingle(deck => deck.Name == "Tinybones League");
        report.Signals.Should().Contain(signal => signal.CardName == "Dark Deal" && signal.Source == "Spicerack public decklists");
    }

    /// <summary>
    /// Verifies that EDHTop16 maps cEDH staple and tournament-entry data.
    /// </summary>
    [Fact]
    public async Task EdhTop16Provider_MapsStaplesAndEntries()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect(HttpMethod.Post, "https://edhtop16.test/api/graphql")
            .Respond("application/json", EdhTop16StaplesResponseJson);
        mockHttp.Expect(HttpMethod.Post, "https://edhtop16.test/api/graphql")
            .Respond("application/json", EdhTop16EntriesResponseJson);
        EdhTop16CorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhtop16.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("EdhTop16", "", allowUnofficialApi: true)));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().Contain(signal =>
            signal.CardName == "Waste Not"
            && signal.Source == "EDHTop16"
            && signal.SignalType == CorpusSignalTypes.Performance
            && signal.InclusionRate == 0.42);
        report.ExemplarDecks.Should().ContainSingle(deck =>
            deck.Name == "Tinybones Open - Pilot A"
            && deck.Source == "EDHTop16"
            && deck.Weight > 0.90);
    }

    /// <summary>
    /// Verifies that EDHTop16 requires explicit unofficial endpoint opt-in.
    /// </summary>
    [Fact]
    public async Task EdhTop16Provider_RequiresUnofficialApiOptIn()
    {
        EdhTop16CorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://edhtop16.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("EdhTop16", "")));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().BeEmpty();
        report.Sources.Should().ContainSingle(source =>
            source.Key == "edhtop16"
            && !source.Enabled
            && source.UnofficialApi
            && source.Status == CorpusSourceStatuses.Disabled);
    }

    /// <summary>
    /// Verifies that Reddit maps bounded raw discussions and explicit card references.
    /// </summary>
    [Fact]
    public async Task RedditProvider_MapsDiscussionsAndExplicitCardSignals()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect(HttpMethod.Get, "https://reddit.test/r/EDH/search.json*")
            .Respond("application/json", RedditSearchResponseJson);
        mockHttp.Expect(HttpMethod.Get, "https://reddit.test/comments/abc123.json*")
            .Respond("application/json", RedditCommentsResponseJson);
        RedditDiscussionCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://reddit.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Reddit", "", allowUnofficialApi: true)));
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth("minimal");
        budget.MaxDecksPerSource = 1;
        budget.MaxEvidencePerRecommendation = 2;

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), budget, TestContext.Current.CancellationToken);

        report.Discussions.Should().HaveCount(2);
        report.Discussions.Should().Contain(discussion =>
            discussion.Source == "Reddit discussion search"
            && discussion.Body.Contains("[[Waste Not]]", StringComparison.Ordinal)
            && discussion.MentionedCards.Contains("Waste Not"));
        report.Signals.Should().Contain(signal =>
            signal.CardName == "Dark Deal"
            && signal.SignalType == CorpusSignalTypes.Discussion);
    }

    /// <summary>
    /// Verifies that Reddit requires explicit unofficial endpoint opt-in.
    /// </summary>
    [Fact]
    public async Task RedditProvider_RequiresUnofficialApiOptIn()
    {
        RedditDiscussionCorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://reddit.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Reddit", "")));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.Discussions.Should().BeEmpty();
        report.Sources.Should().ContainSingle(source =>
            source.Key == "reddit-discussions"
            && !source.Enabled
            && source.UnofficialApi
            && source.Status == CorpusSourceStatuses.Disabled);
    }

    /// <summary>
    /// Verifies that providers reject HTML payloads instead of scraping them.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_RejectsHtmlPayloads()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Post, "https://topdeck.test/v2/tournaments")
            .Respond("text/html", "<html><body>nope</body></html>");
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://topdeck.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("TopDeck", "key")));

        Func<Task> act = () => provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HTML*");
    }

    /// <summary>
    /// Verifies that Spicerack rejects HTML payloads instead of scraping them.
    /// </summary>
    [Fact]
    public async Task SpicerackProvider_RejectsHtmlPayloads()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://spicerack.test/api/export-decklists/*")
            .Respond("text/html", "<html><body>nope</body></html>");
        SpicerackCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://spicerack.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Spicerack", "key")));

        Func<Task> act = () => provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HTML*");
    }

    /// <summary>
    /// Verifies that malformed TopDeck JSON is surfaced as a contract failure.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_ThrowsForMalformedJson()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Post, "https://topdeck.test/v2/tournaments")
            .Respond("application/json", "{ nope");
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://topdeck.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("TopDeck", "key")));

        Func<Task> act = () => provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<JsonException>();
    }

    /// <summary>
    /// Verifies that malformed Spicerack JSON is surfaced as a contract failure.
    /// </summary>
    [Fact]
    public async Task SpicerackProvider_ThrowsForMalformedJson()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://spicerack.test/api/export-decklists/*")
            .Respond("application/json", "{ nope");
        SpicerackCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://spicerack.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Spicerack", "key")));

        Func<Task> act = () => provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<JsonException>();
    }

    /// <summary>
    /// Verifies that TopDeck rate limits fail clearly instead of returning partial evidence.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_ThrowsForRateLimitResponse()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Post, "https://topdeck.test/v2/tournaments")
            .Respond(HttpStatusCode.TooManyRequests, "application/json", """{ "error": "rate limited" }""");
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://topdeck.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("TopDeck", "key")));

        Func<Task> act = () => provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(exception => exception.StatusCode == HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Verifies that Spicerack auth failures fail clearly instead of returning partial evidence.
    /// </summary>
    [Fact]
    public async Task SpicerackProvider_ThrowsForAuthFailure()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://spicerack.test/api/export-decklists/*")
            .Respond(HttpStatusCode.Unauthorized, "application/json", """{ "error": "bad key" }""");
        SpicerackCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://spicerack.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Spicerack", "key")));

        Func<Task> act = () => provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(exception => exception.StatusCode == HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that unsupported TopDeck envelopes produce no evidence without fabricating cards.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_ReturnsEmptyReportForUnsupportedEnvelope()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Post, "https://topdeck.test/v2/tournaments")
            .Respond("application/json", """{ "data": { "changed": true } }""");
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://topdeck.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("TopDeck", "key")));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.ExemplarDecks.Should().BeEmpty();
        report.Signals.Should().BeEmpty();
        report.Sources.Should().ContainSingle(source => source.Key == "topdeck" && source.Status == CorpusSourceStatuses.Available);
    }

    /// <summary>
    /// Verifies that unsupported Spicerack envelopes produce no evidence without fabricating cards.
    /// </summary>
    [Fact]
    public async Task SpicerackProvider_ReturnsEmptyReportForUnsupportedEnvelope()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://spicerack.test/api/export-decklists/*")
            .Respond("application/json", """{ "records": [{ "decklist": "1 Waste Not" }] }""");
        SpicerackCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://spicerack.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Spicerack", "key")));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.ExemplarDecks.Should().BeEmpty();
        report.Signals.Should().BeEmpty();
        report.Sources.Should().ContainSingle(source => source.Key == "spicerack" && source.Status == CorpusSourceStatuses.Available);
    }

    /// <summary>
    /// Verifies that missing API keys are surfaced as source status instead of network calls.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_ReportsMissingApiKey()
    {
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://topdeck.test/"),
            new NullCorpusCache(),
            Options.Create(new MtgMcpOptions()));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.Sources.Should().ContainSingle(source =>
            source.Key == "topdeck"
            && source.Status == CorpusSourceStatuses.MissingConfig
            && source.RequiresKey);
    }

    /// <summary>
    /// Verifies that Spicerack reports missing API keys without a network call.
    /// </summary>
    [Fact]
    public async Task SpicerackProvider_ReportsMissingApiKey()
    {
        SpicerackCorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://spicerack.test/"),
            new NullCorpusCache(),
            Options.Create(new MtgMcpOptions()));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.Sources.Should().ContainSingle(source =>
            source.Key == "spicerack"
            && source.Status == CorpusSourceStatuses.MissingConfig
            && source.RequiresKey);
    }

    /// <summary>
    /// Creates a test HTTP client with a fake base address.
    /// </summary>
    private static HttpClient CreateClient(MockHttpMessageHandler mockHttp, string baseAddress)
    {
        HttpClient client = mockHttp.ToHttpClient();
        client.BaseAddress = new Uri(baseAddress);
        return client;
    }

    /// <summary>
    /// Creates mtg-mcp options with one enabled API source.
    /// </summary>
    private static MtgMcpOptions OptionsWithSource(
        string source,
        string apiKey,
        bool allowUnofficialApi = false)
    {
        return new MtgMcpOptions
        {
            Intelligence =
            {
                Sources =
                {
                    [source] = new MtgMcpCorpusSourceOptions
                    {
                        Enabled = true,
                        ApiKey = apiKey,
                        AllowUnofficialApi = allowUnofficialApi,
                        BaseAddress = source switch
                        {
                            "TopDeck" => new Uri("https://topdeck.test/"),
                            "Spicerack" => new Uri("https://spicerack.test/"),
                            "EdhTop16" => new Uri("https://edhtop16.test/"),
                            "Reddit" => new Uri("https://reddit.test/"),
                            _ => new Uri("https://decklist-source.test/")
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Creates a Tinybones corpus query fixture.
    /// </summary>
    private static CorpusSignalQuery Query()
    {
        return new CorpusSignalQuery
        {
            Format = "commander",
            Commander = "Tinybones, Trinket Thief",
            Theme = "discard"
        };
    }

    /// <summary>
    /// Creates a small balanced corpus budget fixture.
    /// </summary>
    private static RecommendationAnalysisBudget Budget()
    {
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth("balanced");
        budget.MaxCandidates = 10;
        budget.MaxDecksPerSource = 5;
        return budget;
    }

    /// <summary>
    /// Provides a representative TopDeck tournament response.
    /// </summary>
    private const string TopDeckResponseJson = """
    [
      {
        "name": "Tinybones Invitational",
        "standings": [
          {
            "name": "Pilot A",
            "standing": 1,
            "decklist": "https://topdeck.test/deck/1",
            "deckObj": {
              "Commander": { "Tinybones, Trinket Thief": 1 },
              "Mainboard": {
                "Waste Not": 1,
                "Dark Deal": 1,
                "Rankle's Prank": 1
              }
            }
          }
        ]
      }
    ]
    """;

    /// <summary>
    /// Provides a representative Spicerack decklist response.
    /// </summary>
    private const string SpicerackResponseJson = """
    {
      "data": [
        {
          "deck_name": "Tinybones League",
          "decklist_url": "https://spicerack.test/deck/1",
          "decklist_text": "1 Tinybones, Trinket Thief\n1 Waste Not\n1 Dark Deal\n1 Geier Reach Sanitarium"
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a representative EDHTop16 staple response.
    /// </summary>
    private const string EdhTop16StaplesResponseJson = """
    {
      "data": {
        "commander": {
          "name": "Tinybones, Trinket Thief",
          "staples": [
            {
              "id": "Card:1",
              "name": "Waste Not",
              "type": "Enchantment",
              "manaCost": "{1}{B}",
              "scryfallUrl": "https://scryfall.test/card/waste-not",
              "playRateLastYear": 0.42
            }
          ]
        }
      }
    }
    """;

    /// <summary>
    /// Provides a representative EDHTop16 entry response.
    /// </summary>
    private const string EdhTop16EntriesResponseJson = """
    {
      "data": {
        "commander": {
          "entries": {
            "edges": [
              {
                "node": {
                  "standing": 1,
                  "wins": 5,
                  "losses": 1,
                  "draws": 0,
                  "decklist": "https://deck.test/tinybones",
                  "player": "Pilot A",
                  "tournament": {
                    "name": "Tinybones Open",
                    "size": 64,
                    "tournamentDate": "2026-01-01",
                    "TID": "event-1"
                  }
                }
              }
            ]
          }
        }
      }
    }
    """;

    /// <summary>
    /// Provides a representative Reddit search response.
    /// </summary>
    private const string RedditSearchResponseJson = """
    {
      "data": {
        "children": [
          {
            "kind": "t3",
            "data": {
              "id": "abc123",
              "subreddit": "EDH",
              "title": "Tinybones discard package",
              "selftext": "I would start with [[Waste Not]] and a few wheel effects.",
              "permalink": "/r/EDH/comments/abc123/tinybones_discard_package/",
              "score": 42,
              "created_utc": 1767225600
            }
          }
        ]
      }
    }
    """;

    /// <summary>
    /// Provides a representative Reddit comments response.
    /// </summary>
    private const string RedditCommentsResponseJson = """
    [
      {
        "data": {
          "children": []
        }
      },
      {
        "data": {
          "children": [
            {
              "kind": "t1",
              "data": {
                "subreddit": "EDH",
                "body": "[[Dark Deal]] is clunky but strong with Tinybones.",
                "permalink": "/r/EDH/comments/abc123/comment/def456/",
                "score": 12,
                "created_utc": 1767229200
              }
            }
          ]
        }
      }
    ]
    """;
}
