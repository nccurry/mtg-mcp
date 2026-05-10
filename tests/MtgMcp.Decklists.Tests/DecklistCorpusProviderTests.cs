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
    private static MtgMcpOptions OptionsWithSource(string source, string apiKey)
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
                        BaseAddress = source == "TopDeck"
                            ? new Uri("https://topdeck.test/")
                            : new Uri("https://spicerack.test/")
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
}
