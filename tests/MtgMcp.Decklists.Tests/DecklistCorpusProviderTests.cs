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
            && source.Status == CorpusSourceStatuses.Available
            && source.UnofficialApi
            && source.PermissionSensitive
            && source.AttributionRequired);
        second.Notes.Should().Contain(note => note.Contains("cache", StringComparison.OrdinalIgnoreCase));
        mockHttp.GetMatchCount(request).Should().Be(1);
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
    /// Verifies that EDHREC requires explicit unofficial endpoint opt-in.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_RequiresUnofficialApiOptIn()
    {
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "")));

        CorpusSignalReport report = await provider.GetSignalsAsync(Query(), Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().BeEmpty();
        report.Sources.Should().ContainSingle(source =>
            source.Key == "edhrec"
            && !source.Enabled
            && source.UnofficialApi
            && source.Status == CorpusSourceStatuses.Disabled);
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
    /// Verifies that missing EDHREC theme pages fall back to commander aggregates.
    /// </summary>
    [Fact]
    public async Task EdhrecProvider_FallsBackWhenThemePageIsMissing()
    {
        MockHttpMessageHandler mockHttp = new();
        mockHttp.Expect(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief/discard.json")
            .Respond(HttpStatusCode.NotFound);
        mockHttp.Expect(HttpMethod.Get, "https://edhrec.test/pages/commanders/tinybones-trinket-thief.json")
            .Respond("application/json", EdhrecCommanderResponseJson);
        EdhrecCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://edhrec.test/pages/"),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Edhrec", "", allowUnofficialApi: true)));
        CorpusSignalQuery query = Query();
        query.Theme = "discard";

        CorpusSignalReport report = await provider.GetSignalsAsync(query, Budget(), TestContext.Current.CancellationToken);

        report.Signals.Should().Contain(signal =>
            signal.CardName == "Waste Not"
            && signal.Uri == "https://edhrec.com/commanders/tinybones-trinket-thief");
        report.Notes.Should().Contain(note => note.Contains("falling back", StringComparison.OrdinalIgnoreCase));
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
            new FakeCardCatalog("Dark Deal", "Waste Not"),
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
        report.Discussions.Should().Contain(discussion =>
            discussion.LinkedDeckUris.Contains("https://archidekt.com/decks/12345/tinybones"));
        report.Signals.Should().Contain(signal =>
            signal.CardName == "Dark Deal"
            && signal.SignalType == CorpusSignalTypes.Discussion);
    }

    /// <summary>
    /// Verifies that Reddit samples popular Commander subreddits and validates plain card names.
    /// </summary>
    [Fact]
    public async Task RedditProvider_SearchesCommanderSubredditsAndValidatesPlainCardNames()
    {
        long recent = DateTimeOffset.UtcNow.AddMonths(-2).ToUnixTimeSeconds();
        long stale = DateTimeOffset.UtcNow.AddYears(-8).ToUnixTimeSeconds();
        MockHttpMessageHandler mockHttp = new();
        MockedRequest commanderSearch = mockHttp.When(HttpMethod.Get, "https://reddit.test/r/Commander/search.json*")
            .Respond("application/json", RedditSearchResponseWithPlainTextJson(recent, stale));
        mockHttp.When(HttpMethod.Get, "https://reddit.test/r/EDH/search.json*")
            .Respond("application/json", RedditSearchResponseWithPlainTextJson(recent, stale));
        mockHttp.When(HttpMethod.Get, "https://reddit.test/r/Magicdeckbuilding/search.json*")
            .Respond("application/json", RedditSearchResponseWithPlainTextJson(recent, stale));
        mockHttp.When(HttpMethod.Get, "https://reddit.test/comments/plain123.json*")
            .Respond("application/json", RedditPlainTextCommentsResponseJson);
        RedditDiscussionCorpusSignalProvider provider = new(
            CreateClient(mockHttp, "https://reddit.test/"),
            new FakeCardCatalog("Beast Whisperer", "Raise the Palisade", "Craterhoof Behemoth", "V.A.T.S."),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Reddit", "", allowUnofficialApi: true)));
        RecommendationAnalysisBudget budget = RecommendationAnalysisBudget.FromDepth("balanced");
        budget.MaxDecksPerSource = 1;
        budget.MaxEvidencePerRecommendation = 1;

        CorpusSignalReport report = await provider.GetSignalsAsync(
            new CorpusSignalQuery
            {
                Format = "commander",
                Commander = "Galadriel, Elven-Queen",
                Theme = "voting elves"
            },
            budget,
            TestContext.Current.CancellationToken);

        mockHttp.GetMatchCount(commanderSearch).Should().BeGreaterThan(0);
        report.Discussions.Should().Contain(discussion =>
            discussion.Title == "Galadriel voting upgrades"
            && discussion.MentionedCards.Contains("Beast Whisperer")
            && discussion.MentionedCards.Contains("Raise the Palisade")
            && discussion.MentionedCards.Contains("V.A.T.S."));
        report.Discussions.Should().NotContain(discussion =>
            discussion.MentionedCards.Contains("Craterhoof Behemoth"));
        report.Signals.Should().Contain(signal =>
            signal.CardName == "Beast Whisperer"
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
            new FakeCardCatalog(),
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
    /// Verifies that a Reddit OAuth bearer token enables the official API path without unofficial opt-in.
    /// </summary>
    [Fact]
    public void RedditProvider_BearerTokenEnablesOfficialApiStatus()
    {
        RedditDiscussionCorpusSignalProvider provider = new(
            CreateClient(new MockHttpMessageHandler(), "https://reddit.test/"),
            new FakeCardCatalog(),
            new NullCorpusCache(),
            Options.Create(OptionsWithSource("Reddit", "token")));

        CorpusSourceStatus status = provider.GetStatus();

        status.Enabled.Should().BeTrue();
        status.ApiType.Should().Be(CorpusSourceApiTypes.Official);
        status.UnofficialApi.Should().BeFalse();
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
                            "Edhrec" => new Uri("https://edhrec.test/pages/"),
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
    /// Provides a Reddit response with one recent plain-text post and one stale post.
    /// </summary>
    private static string RedditSearchResponseWithPlainTextJson(long recentCreatedAt, long staleCreatedAt)
    {
        return $$"""
        {
          "data": {
            "children": [
              {
                "kind": "t3",
                "data": {
                  "id": "plain123",
                  "subreddit": "Commander",
                  "title": "Galadriel voting upgrades",
                  "selftext": "[Beast Whisperer] keeps the cards flowing, [V.A.T.S.] buys time, and Raise the Palisade is a clean finisher.",
                  "permalink": "/r/Commander/comments/plain123/galadriel_voting_upgrades/",
                  "score": 150,
                  "created_utc": {{recentCreatedAt}}
                }
              },
              {
                "kind": "t3",
                "data": {
                  "id": "stale123",
                  "subreddit": "Commander",
                  "title": "Old Galadriel finisher thread",
                  "selftext": "Craterhoof Behemoth was the old plan.",
                  "permalink": "/r/Commander/comments/stale123/old_galadriel_finisher_thread/",
                  "score": 999,
                  "created_utc": {{staleCreatedAt}}
                }
              }
            ]
          }
        }
        """;
    }

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
                "body": "[[Dark Deal]] is clunky but strong with Tinybones. My list is https://archidekt.com/decks/12345/tinybones.",
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

    /// <summary>
    /// Provides a representative Reddit comments response with plain-text card names.
    /// </summary>
    private const string RedditPlainTextCommentsResponseJson = """
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
                "subreddit": "Commander",
                "body": "[Beast Whisperer] overperformed for me, and Raise the Palisade ended stalled boards.",
                "permalink": "/r/Commander/comments/plain123/comment/plain456/",
                "score": 31,
                "created_utc": 1767229200
              }
            }
          ]
        }
      }
    ]
    """;

    /// <summary>
    /// Resolves a fixed set of exact card names for decklist provider tests.
    /// </summary>
    private sealed class FakeCardCatalog : ICardCatalog
    {
        /// <summary>
        /// Stores card names that exact-name validation should resolve.
        /// </summary>
        private readonly HashSet<string> names;

        /// <summary>
        /// Creates a fake catalog that resolves the provided card names.
        /// </summary>
        public FakeCardCatalog(params string[] names)
        {
            this.names = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns no search results.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns no semantic search results.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
            CardSearchRequest request,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }

        /// <summary>
        /// Returns one fake card when the name is configured.
        /// </summary>
        public Task<CardInfo?> GetCardAsync(string nameOrId, CancellationToken cancellationToken)
        {
            return Task.FromResult(names.Contains(nameOrId) ? new CardInfo { Name = nameOrId } : null);
        }

        /// <summary>
        /// Resolves configured exact names.
        /// </summary>
        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardsByNamesAsync(
            IReadOnlyList<string> names,
            CancellationToken cancellationToken)
        {
            Dictionary<string, CardInfo> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                if (this.names.Contains(name))
                {
                    result[name] = new CardInfo { Name = name };
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, CardInfo>>(result);
        }

        /// <summary>
        /// Returns no rulings.
        /// </summary>
        public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RulingInfo>>([]);
        }

        /// <summary>
        /// Returns no print rows.
        /// </summary>
        public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
            string nameOrId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardInfo>>([]);
        }

        /// <summary>
        /// Returns no suggestions.
        /// </summary>
        public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
            string prompt,
            string? format,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CardSearchResult>>([]);
        }
    }
}
