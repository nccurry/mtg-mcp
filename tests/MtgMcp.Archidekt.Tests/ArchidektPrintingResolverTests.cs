using System.Net;
using System.Text.Json;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Exercises how a push picks the Archidekt printing of each new card, and how an apply reports a card it could not
/// place. The search results copy the shape recorded from the real service: the set is in
/// <c>edition.editioncode</c>, there is no top-level set code, and a page holds <c>count</c> and <c>next</c>.
/// </summary>
public sealed class ArchidektPrintingResolverTests
{
    /// <summary>
    /// Holds the Oracle ID the recorded Assassin's Trophy results share.
    /// </summary>
    private const string TrophyOracle = "ac10d218-f9a6-4058-9cda-a15ca1b0b7b5";

    /// <summary>
    /// Holds the search route for Assassin's Trophy.
    /// </summary>
    private const string TrophyRoute = "api/cards/v2/?name=Assassin%27s%20Trophy&pageSize=25";

    /// <summary>
    /// Verifies an entry with no printing takes the first listed exact-name result and says how many there were.
    /// </summary>
    [Fact]
    public async Task NameOnlyEntry_TakesTheFirstListedPrintingAndNamesIt()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));

        ArchidektPrintingChoices choices = await ResolveAsync(handler, Entry("Assassin's Trophy"));

        Assert.Empty(choices.UnresolvedCardNames);
        ArchidektPrintingChoice choice = Assert.IsType<ArchidektPrintingChoice>(choices.Find(Entry("Assassin's Trophy")));
        Assert.Equal("151025", choice.ProviderCardId);
        Assert.Equal(
            "Archidekt printing soc 294 (default of 18; no printing was set).",
            choice.Description);
        Assert.Equal(1, choices.SearchRequests);
    }

    /// <summary>
    /// Verifies the printing ID is matched to the result's <c>uid</c>, even when it is not listed first.
    /// </summary>
    [Fact]
    public async Task PrintingId_MatchesTheResultUid()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));
        RemoteDeckEntry entry = Entry("Assassin's Trophy") with
        {
            PrintingId = Guid.Parse("ed6c7d29-71b4-4134-b591-5598f479d592"),
        };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("128098", choices.Find(entry)!.ProviderCardId);
        Assert.Contains("mkm 187", choices.Find(entry)!.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a set code and collector number are matched to <c>edition.editioncode</c>, since a real result has no
    /// top-level set code.
    /// </summary>
    [Fact]
    public async Task SetAndCollectorNumber_MatchTheEditionCode()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));
        RemoteDeckEntry entry = Entry("Assassin's Trophy") with { SetCode = "GRN", CollectorNumber = "152" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("76319", choices.Find(entry)!.ProviderCardId);
    }

    /// <summary>
    /// Verifies a result that has only a top-level set code still matches.
    /// </summary>
    [Fact]
    public async Task SetAndCollectorNumber_FallBackToATopLevelSetCode()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Island&pageSize=25",
            """{"count":1,"next":null,"results":[{"id":501,"uid":"33333333-3333-3333-3333-333333333333","setCode":"dmu","collectorNumber":"278","oracleCard":{"name":"Island","uid":"44444444-4444-4444-4444-444444444444"}}]}""");
        RemoteDeckEntry entry = Entry("Island") with { SetCode = "DMU", CollectorNumber = "278" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("501", choices.Find(entry)!.ProviderCardId);
    }

    /// <summary>
    /// Verifies an Oracle ID keeps a result of a different card with the same name from being chosen.
    /// </summary>
    [Fact]
    public async Task OracleId_FiltersTheNameMatches()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Name&pageSize=25",
            Page(
                2,
                null,
                Result(1, "10000000-0000-0000-0000-000000000001", "aaa", "1", "Name", "00000000-0000-0000-0000-00000000000a"),
                Result(2, "10000000-0000-0000-0000-000000000002", "bbb", "2", "Name", "00000000-0000-0000-0000-00000000000b")));
        RemoteDeckEntry entry = Entry("Name") with { OracleId = Guid.Parse("00000000-0000-0000-0000-00000000000b") };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("2", choices.Find(entry)!.ProviderCardId);
    }

    /// <summary>
    /// Verifies a printing that is not on the first page is found by following the <c>next</c> link, and that the
    /// link is followed only as a card search query.
    /// </summary>
    [Fact]
    public async Task PrintingOnPageTwo_IsFoundThroughTheNextLink()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Sol%20Ring&pageSize=25",
            Page(
                2,
                "https://archidekt.com/api/cards/v2/?name=Sol%20Ring&page=2&pageSize=25",
                Result(1, "10000000-0000-0000-0000-000000000001", "cmd", "1", "Sol Ring", TrophyOracle)));
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Sol%20Ring&page=2&pageSize=25",
            Page(
                2,
                null,
                Result(2, "10000000-0000-0000-0000-000000000002", "c21", "263", "Sol Ring", TrophyOracle)));
        RemoteDeckEntry entry = Entry("Sol Ring") with { SetCode = "c21", CollectorNumber = "263" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("2", choices.Find(entry)!.ProviderCardId);
        Assert.Equal(2, choices.SearchRequests);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies a link to another route is never followed.
    /// </summary>
    [Fact]
    public async Task NextLinkToAnotherRoute_IsNotFollowed()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Sol%20Ring&pageSize=25",
            Page(
                2,
                "https://example.invalid/steal?x=1",
                Result(1, "10000000-0000-0000-0000-000000000001", "cmd", "1", "Sol Ring", TrophyOracle)));
        RemoteDeckEntry entry = Entry("Sol Ring") with { SetCode = "c21", CollectorNumber = "263" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal(["Sol Ring"], choices.UnresolvedCardNames);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies one search serves every entry of a name, and that a name with no result is reported.
    /// </summary>
    [Fact]
    public async Task OneSearchPerName_AndNamesWithoutAResultAreReported()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Nonexistent%20Card&pageSize=25", Page(0, null));
        RemoteDeckEntry first = Entry("Assassin's Trophy");
        RemoteDeckEntry second = first with { SetCode = "tmc", CollectorNumber = "55" };
        RemoteDeckEntry missing = Entry("Nonexistent Card");

        ArchidektPrintingChoices choices = await ResolveAsync(handler, first, second, missing);

        Assert.Equal("151025", choices.Find(first)!.ProviderCardId);
        Assert.Equal("150200", choices.Find(second)!.ProviderCardId);
        Assert.Equal(["Nonexistent Card"], choices.UnresolvedCardNames);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies a search that reaches the request limit stops and leaves the rest unresolved.
    /// </summary>
    [Fact]
    public async Task RequestLimit_StopsTheSearchAndLeavesTheRestUnresolved()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));
        using ArchidektSession session = CreateSession(handler);
        ArchidektPrintingResolver resolver = new(new ArchidektDeckTransport(session));
        ArchidektPrintingChoices choices = new();

        await resolver.ResolveAsync(
            [Entry("Assassin's Trophy"), Entry("Sol Ring"), Entry("Island")],
            choices,
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);

        Assert.True(choices.StoppedAtRequestLimit);
        Assert.Equal(["Sol Ring", "Island"], choices.UnresolvedCardNames);
    }

    /// <summary>
    /// Verifies a card that cannot be placed stops the apply before any write, and every operation says so.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_CardWithNoPrinting_SendsNothingAndIsNotApplied()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Nonexistent%20Card&pageSize=25", Page(0, null));
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(Entry("Nonexistent Card"));
        target = target with { Name = "Renamed" };

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("not-applied", result.Outcome);
        Assert.Equal(["metadata-update", "entry-add"], result.Operations.Select(value => value.Kind));
        Assert.Equal("not-attempted", result.Operations[0].Status);
        Assert.Equal("Not sent: an earlier operation stopped the push.", result.Operations[0].Message);
        Assert.Equal("not-sent", result.Operations[1].Status);
        Assert.StartsWith("Nothing was sent to Archidekt for this operation.", result.Operations[1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, value => value.Method != HttpMethod.Get);
    }

    /// <summary>
    /// Verifies a search that fails before any write is reported as not sent, never as unknown.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_SearchFailureBeforeAnyWrite_IsNotSent()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, "{}", HttpStatusCode.Forbidden);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(Entry("Assassin's Trophy"));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("not-applied", result.Outcome);
        Assert.Equal("not-sent", Assert.Single(result.Operations).Status);
        Assert.DoesNotContain(handler.Requests, value => value.Method != HttpMethod.Get);
    }

    /// <summary>
    /// Verifies a failure after a write was attempted stays unknown, and the rest are not sent.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_FailureAfterAWriteStarted_IsUnknown()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}", HttpStatusCode.InternalServerError);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(
            Entry("Assassin's Trophy"),
            Entry("Assassin's Trophy") with { SetCode = "tmc", CollectorNumber = "55" });

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.Equal("unknown", result.Operations[0].Status);
        Assert.Equal("not-attempted", result.Operations[1].Status);
        Assert.Equal("Not sent: an earlier operation stopped the push.", result.Operations[1].Message);
        Assert.Single(handler.Requests, value => value.Method == HttpMethod.Patch);
    }

    /// <summary>
    /// Runs the resolver over the given entries with an unlimited-enough budget.
    /// </summary>
    private static async Task<ArchidektPrintingChoices> ResolveAsync(
        ArchidektTestHttpHandler handler,
        params RemoteDeckEntry[] entries)
    {
        using ArchidektSession session = CreateSession(handler);
        ArchidektPrintingResolver resolver = new(new ArchidektDeckTransport(session));
        ArchidektPrintingChoices choices = new();
        await resolver.ResolveAsync(
            entries,
            choices,
            new ArchidektOperationBudget(50),
            TestContext.Current.CancellationToken);
        return choices;
    }

    /// <summary>
    /// Applies a plan from the current deck to the target the way the apply tool does.
    /// </summary>
    private static async Task<ArchidektApplyResult> ApplyAsync(
        ArchidektTestHttpHandler handler,
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target)
    {
        using ArchidektSession session = CreateSession(handler);
        ArchidektDeckOperations operations = new(new ArchidektDeckTransport(session), 150);
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
        return await operations.ApplyPlanAsync(
            current,
            target,
            plan,
            new ArchidektOperationBudget(150),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Builds the canonical test deck and a target that adds the given new entries to it.
    /// </summary>
    private static (RemoteDeckSnapshot Current, RemoteDeckSnapshot Target) DeckWithNewCard(
        params RemoteDeckEntry[] added)
    {
        using JsonDocument document = JsonDocument.Parse(ArchidektTestPayloads.Deck);
        RemoteDeckSnapshot current = ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            ArchidektTestPayloads.Deck,
            DateTimeOffset.UtcNow,
            "GET");
        return (current, current with { Entries = [.. current.Entries, .. added] });
    }

    /// <summary>
    /// Builds a new name-only entry for the main zone.
    /// </summary>
    private static RemoteDeckEntry Entry(string cardName)
    {
        return new RemoteDeckEntry(
            string.Empty,
            string.Empty,
            1,
            cardName,
            OracleId: null,
            PrintingId: null,
            SetCode: null,
            CollectorNumber: null,
            "en",
            "nonfoil",
            "main",
            ["Mainboard"],
            "Mainboard",
            SortOrder: 0);
    }

    /// <summary>
    /// Builds the recorded Assassin's Trophy search page, trimmed to the printings the tests use.
    /// </summary>
    private static string TrophyPage(int count)
    {
        return Page(
            count,
            null,
            Result(151025, "aaf258fc-3ba4-4b83-bdbf-10a07e0b6c03", "soc", "294", "Assassin's Trophy", TrophyOracle),
            Result(150200, "932efc19-9bfd-4501-a623-ff6765a356d8", "tmc", "55", "Assassin's Trophy", TrophyOracle),
            Result(128098, "ed6c7d29-71b4-4134-b591-5598f479d592", "mkm", "187", "Assassin's Trophy", TrophyOracle),
            Result(76319, "906b6e99-128f-4c11-8daf-16099d35b0d4", "grn", "152", "Assassin's Trophy", TrophyOracle));
    }

    /// <summary>
    /// Builds one search page in the real service's shape.
    /// </summary>
    private static string Page(int count, string? next, params string[] results)
    {
        string nextJson = next is null ? "null" : JsonSerializer.Serialize(next);
        return $$"""{"count":{{count}},"next":{{nextJson}},"previous":null,"results":[{{string.Join(',', results)}}]}""";
    }

    /// <summary>
    /// Builds one search result in the real service's shape: the set is in the edition, and there is no top-level set
    /// code.
    /// </summary>
    private static string Result(int id, string uid, string set, string number, string name, string oracle)
    {
        string quoted = JsonSerializer.Serialize(name);
        return $$$"""{"id":{{{id}}},"uid":"{{{uid}}}","collectorNumber":"{{{number}}}","edition":{"editioncode":"{{{set}}}"},"oracleCard":{"uid":"{{{oracle}}}","name":{{{quoted}}}}}""";
    }

    /// <summary>
    /// Creates an injected session with zero-delay safety bounds.
    /// </summary>
    private static ArchidektSession CreateSession(ArchidektTestHttpHandler handler)
    {
        ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
        };
        return new ArchidektSession(
            new HttpClient(handler) { BaseAddress = options.BaseAddress },
            ownsHttpClient: true,
            options);
    }
}
