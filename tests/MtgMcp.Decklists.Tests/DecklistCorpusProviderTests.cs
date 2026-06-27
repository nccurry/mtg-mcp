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
    /// Verifies that TopDeck exact-matches individual partner names instead of the pair display string.
    /// </summary>
    [Fact]
    public async Task TopDeckProvider_MatchesPartnerCommanderNames()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Post, "https://topdeck.test/v2/tournaments")
            .Respond("application/json", TopDeckResponseJson);
        TopDeckCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://topdeck.test/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("TopDeck", "key")));
        CorpusSignalQuery query = Query();
        query.Commander = "Partner One // Tinybones, Trinket Thief";
        query.CommanderNames = ["Partner One", "Tinybones, Trinket Thief"];

        CorpusSignalReport report = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        report.ExemplarDecks.Should().ContainSingle(deck => deck.Commander == query.Commander);
        report.Signals.Should().Contain(signal => signal.CardName == "Waste Not" && signal.Source == "TopDeck.gg");
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
            && source.Status == CorpusSourceStatusKind.Disabled);
    }

    /// <summary>
    /// Verifies that EDHREC maps commander aggregate cardlists and uses cache.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_MapsSignalsAndUsesCache()
    {
        MockHttpMessageHandler mockHttp = new();
        MockedRequest request = mockHttp.When(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief.json")
            .Respond("application/json", EdhrecCommanderResponseJson);
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions()),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = null;

        CorpusSignalReport first = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);
        CorpusSignalReport second = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        first.ExemplarDecks.Should().BeEmpty();
        CardCorpusSignal wasteNot = first.Signals.Should().ContainSingle(signal =>
            signal.CardName == "Waste Not").Subject;
        wasteNot.Source.Should().Be("EDHREC");
        wasteNot.SignalType.Should().Be(CorpusSignalTypes.Inclusion);
        wasteNot.InclusionRate.Should().BeApproximately(0.80, 0.0001);
        wasteNot.SynergyScore.Should().BeApproximately(0.81, 0.0001);
        wasteNot.DeckCount.Should().Be(80);
        wasteNot.Uri.Should().Be("https://edhrec.com/commanders/tinybones-trinket-thief");
        first.Signals.Should().Contain(signal =>
            signal.CardName == "Pox Plague"
            && signal.SignalType == CorpusSignalTypes.Trend);
        first.Sources.Should().ContainSingle(source =>
            source.Key == "edhrec"
            && source.Status == CorpusSourceStatusKind.Available
            && source.UnofficialApi
            && source.PermissionSensitive
            && source.AttributionRequired);
        second.Notes.Should().Contain(note => note.Contains("cache", StringComparison.OrdinalIgnoreCase));
        mockHttp.GetMatchCount(request).Should().Be(1);
    }

    /// <summary>
    /// Verifies that EDHREC adapter slugging supports partner commander display names.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_UsesPartnerCommanderSlug()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect(
                HttpMethod.Get,
                "https://edhrec.test/pages/commanders/frodo-adventurous-hobbit-sam-loyal-attendant.json")
            .Respond("application/json", EdhrecCommanderResponseJson);
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Commander = "Frodo, Adventurous Hobbit // Sam, Loyal Attendant";
        query.Theme = null;

        CorpusSignalReport report = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().Contain(signal =>
            signal.CardName == "Waste Not"
            && signal.Uri == "https://edhrec.com/commanders/frodo-adventurous-hobbit-sam-loyal-attendant");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that refresh bypasses EDHREC cache.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_RefreshBypassesCache()
    {
        MockHttpMessageHandler mockHttp = new();
        MockedRequest request = mockHttp.When(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief.json")
            .Respond("application/json", EdhrecCommanderResponseJson);
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions()),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = null;

        await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);
        query.Refresh = true;
        await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        mockHttp.GetMatchCount(request).Should().Be(2);
    }

    /// <summary>
    /// Verifies that EDHREC is enabled by default for broad Commander aggregate evidence.
    /// </summary>
    [Fact]
    public void EdhrecProvider_IsEnabledByDefault()
    {
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(new MtgMcpOptions()));

        CorpusSourceStatus status = provider.GetStatus();

        status.Enabled.Should().BeTrue();
        status.Status.Should().Be(CorpusSourceStatusKind.Available);
        status.UnofficialApi.Should().BeTrue();
        status.PermissionSensitive.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that EDHREC respects an explicit unofficial endpoint opt-out.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_RespectsUnofficialApiOptOut()
    {
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: false)));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().BeEmpty();
        report.Sources.Should().ContainSingle(source =>
            source.Key == "edhrec"
            && !source.Enabled
            && source.UnofficialApi
            && source.Status == CorpusSourceStatusKind.Disabled);
    }

    /// <summary>
    /// Verifies that EDHREC theme lookups use commander theme pages.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_UsesThemePageWhenThemeIsAvailable()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief/discard.json")
            .Respond("application/json", EdhrecThemeResponseJson);
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = "discard";

        CorpusSignalReport report = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().Contain(signal =>
            signal.CardName == "Dark Deal"
            && signal.Uri == "https://edhrec.com/commanders/tinybones-trinket-thief/discard");
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that missing EDHREC theme pages return an unsupported-theme note.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_ReturnsUnsupportedThemeWhenThemePageIsMissing()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief/discard.json")
            .Respond(HttpStatusCode.NotFound);
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = "discard";

        CorpusSignalReport report = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().BeEmpty();
        report.Notes.Should().Contain(note => note.Contains("unsupported-theme", StringComparison.OrdinalIgnoreCase));
        mockHttp.VerifyNoOutstandingExpectation();
    }

    /// <summary>
    /// Verifies that missing EDHREC commander pages return empty evidence with a note.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_ReturnsEmptyReportWhenCommanderPageIsMissing()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief.json")
            .Respond(HttpStatusCode.NotFound);
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = null;

        CorpusSignalReport report = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().BeEmpty();
        report.Notes.Should().Contain(note => note.Contains("commander page", StringComparison.OrdinalIgnoreCase));
        mockHttp.VerifyNoOutstandingExpectation();
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
    /// Verifies that EDHREC rejects HTML payloads instead of scraping them.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_RejectsHtmlPayloads()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief.json")
            .Respond("text/html", "<html><body>nope</body></html>");
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = null;

        Func<Task> act = () => provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

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
    /// Verifies that malformed EDHREC JSON is surfaced as a contract failure.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_ThrowsForMalformedJson()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.When(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief.json")
            .Respond("application/json", "{ nope");
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = null;

        Func<Task> act = () => provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

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
        report.Sources.Should().ContainSingle(source => source.Key == "topdeck" && source.Status == CorpusSourceStatusKind.Available);
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
            && source.Status == CorpusSourceStatusKind.MissingConfig
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
        bool? allowUnofficialApi = null)
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
                            "Edhrec" => new Uri("https://edhrec.test/pages/"),
                            "EdhTop16" => new Uri("https://edhtop16.test/"),
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
    /// Provides a representative EDHREC commander aggregate response.
    /// </summary>
    private const string EdhrecCommanderResponseJson = """
    {
      "container": {
        "json_dict": {
          "cardlists": [
            {
              "header": "High Synergy Cards",
              "tag": "highsynergycards",
              "cardviews": [
                {
                  "name": "Waste Not",
                  "url": "/cards/waste-not",
                  "synergy": 0.81,
                  "num_decks": 80,
                  "potential_decks": 100,
                  "trend_zscore": 0.1
                },
                {
                  "name": "Dark Deal",
                  "url": "/cards/dark-deal",
                  "synergy": 0.54,
                  "num_decks": 40,
                  "potential_decks": 100,
                  "trend_zscore": 0.2
                }
              ]
            },
            {
              "header": "New Cards",
              "tag": "newcards",
              "cardviews": [
                {
                  "name": "Pox Plague",
                  "url": "/cards/pox-plague",
                  "synergy": 0.06,
                  "num_decks": 11,
                  "potential_decks": 100,
                  "trend_zscore": 8.1
                }
              ]
            }
          ]
        }
      }
    }
    """;

    /// <summary>
    /// Provides a representative EDHREC commander theme response.
    /// </summary>
    private const string EdhrecThemeResponseJson = """
    {
      "container": {
        "json_dict": {
          "cardlists": [
            {
              "header": "Top Cards",
              "tag": "topcards",
              "cardviews": [
                {
                  "name": "Dark Deal",
                  "url": "/cards/dark-deal",
                  "synergy": 0.62,
                  "num_decks": 31,
                  "potential_decks": 50,
                  "trend_zscore": 0.2
                }
              ]
            }
          ]
        }
      }
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

}
