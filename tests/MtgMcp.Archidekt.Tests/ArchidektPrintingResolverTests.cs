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
    private const string TrophyRoute = "api/cards/v2/?name=Assassin%27s%20Trophy&exact=true&pageSize=25";

    /// <summary>
    /// Verifies an entry with no printing takes the first listed exact-name result and says how many printings of the
    /// card the search listed (the recorded page is trimmed to four printings).
    /// </summary>
    [Fact]
    public async Task NameOnlyEntry_TakesTheFirstListedPrintingAndNamesIt()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));

        ArchidektPrintingChoices choices = await ResolveAsync(handler, Entry("Assassin's Trophy"));

        Assert.Empty(choices.UnresolvedCardNames);
        ArchidektPrintingChoice choice = Assert.IsType<ArchidektPrintingChoice>(
            choices.Find(Entry("Assassin's Trophy")));
        Assert.Equal("151025", choice.ProviderCardId);
        Assert.Equal(
            "Archidekt printing soc 294 (first of 4 Archidekt printings of this name; no printing was set).",
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
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Assassin%27s%20Trophy&exact=true&edition=grn&pageSize=25",
            TrophyPage(count: 18));
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
            "api/cards/v2/?name=Island&exact=true&edition=dmu&pageSize=25",
            """
            {"count":1,"next":null,"results":[{"id":501,"uid":"33333333-3333-3333-3333-333333333333",
            "setCode":"dmu","collectorNumber":"278",
            "oracleCard":{"name":"Island","uid":"44444444-4444-4444-4444-444444444444"}}]}
            """);
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
            "api/cards/v2/?name=Name&exact=true&pageSize=25",
            Page(
                2,
                null,
                Result(1, Uid(1), "aaa", "1", "Name", OracleOf(1)),
                Result(2, Uid(2), "bbb", "2", "Name", OracleOf(2))));
        RemoteDeckEntry entry = Entry("Name") with { OracleId = Guid.Parse(OracleOf(2)) };

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
            "api/cards/v2/?name=Sol%20Ring&exact=true&edition=c21&pageSize=25",
            Page(
                2,
                "https://archidekt.com/api/cards/v2/?name=Sol%20Ring&exact=true&edition=c21&page=2&pageSize=25",
                Result(1, Uid(1), "cmd", "1", "Sol Ring", TrophyOracle)));
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Sol%20Ring&exact=true&edition=c21&page=2&pageSize=25",
            Page(
                2,
                null,
                Result(2, Uid(2), "c21", "263", "Sol Ring", TrophyOracle)));
        RemoteDeckEntry entry = Entry("Sol Ring") with { SetCode = "c21", CollectorNumber = "263" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("2", choices.Find(entry)!.ProviderCardId);
        Assert.Equal(2, choices.SearchRequests);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies a printing ID that no search result has stays unresolved: the entry asked for one printing, so the
    /// first listed printing is never taken in its place.
    /// </summary>
    [Fact]
    public async Task PrintingIdNotListed_IsUnresolvedAndNotTheDefault()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 4));
        RemoteDeckEntry entry = Entry("Assassin's Trophy") with { PrintingId = Guid.NewGuid() };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Null(choices.Find(entry));
        Assert.Equal(["Assassin's Trophy"], choices.UnresolvedCardNames);
    }

    /// <summary>
    /// Verifies a printing ID on page two wins over a set and collector number match on page one: the set rule waits
    /// until every page was read.
    /// </summary>
    [Fact]
    public async Task PrintingIdOnPageTwo_WinsOverASetMatchOnPageOne()
    {
        ArchidektTestHttpHandler handler = new();
        const string route = "api/cards/v2/?name=Sol%20Ring&exact=true&edition=c21&pageSize=25";
        handler.Add(
            HttpMethod.Get,
            route,
            Page(
                2,
                "https://archidekt.com/api/cards/v2/?name=Sol%20Ring&exact=true&edition=c21&page=2&pageSize=25",
                Result(1, Uid(1), "c21", "263", "Sol Ring", TrophyOracle)));
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Sol%20Ring&exact=true&edition=c21&page=2&pageSize=25",
            Page(2, null, Result(2, Uid(2), "c21", "264", "Sol Ring", TrophyOracle)));
        RemoteDeckEntry entry = Entry("Sol Ring") with
        {
            PrintingId = Guid.Parse(Uid(2)),
            SetCode = "c21",
            CollectorNumber = "263",
        };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("2", choices.Find(entry)!.ProviderCardId);
        Assert.Equal(2, choices.SearchRequests);
    }

    /// <summary>
    /// Verifies a search whose printing is never found reads at most twelve pages and then leaves the card
    /// unresolved, even though Archidekt keeps sending a next link.
    /// </summary>
    [Fact]
    public async Task PrintingNeverFound_StopsAtTwelvePages()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Forest&exact=true&pageSize=25", EndlessPage(1));
        for (int page = 2; page <= 14; page++)
        {
            handler.Add(
                HttpMethod.Get,
                $"api/cards/v2/?name=Forest&exact=true&page={page}&pageSize=25",
                EndlessPage(page));
        }

        RemoteDeckEntry entry = Entry("Forest") with { PrintingId = Guid.NewGuid() };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal(["Forest"], choices.UnresolvedCardNames);
        Assert.Equal(12, choices.SearchRequests);
        Assert.Equal(12, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies the request limit reached on page two stops the search and leaves the card unresolved.
    /// </summary>
    [Fact]
    public async Task RequestLimitOnPageTwo_StopsTheSearchAndLeavesTheCardUnresolved()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Forest&exact=true&pageSize=25", EndlessPage(1));
        using ArchidektSession session = CreateSession(handler);
        ArchidektPrintingResolver resolver = new(new ArchidektDeckTransport(session));
        ArchidektPrintingChoices choices = new();
        RemoteDeckEntry entry = Entry("Forest") with { PrintingId = Guid.NewGuid() };

        await resolver.ResolveAsync(
            [entry],
            choices,
            new ArchidektOperationBudget(1),
            TestContext.Current.CancellationToken);

        Assert.True(choices.StoppedAtRequestLimit);
        Assert.Equal(["Forest"], choices.UnresolvedCardNames);
        Assert.Equal(1, choices.SearchRequests);
    }

    /// <summary>
    /// Verifies a result for another card whose name only contains the name asked for is never chosen, and is not
    /// counted among the printings of the card: Archidekt's own count includes it.
    /// </summary>
    [Fact]
    public async Task ResultOfAnotherCardWithALongerName_IsSkippedAndNotCounted()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Shock&exact=true&pageSize=25",
            Page(
                3,
                null,
                Result(1, Uid(1), "m10", "1", "Shock Troops", OracleOf(1)),
                Result(2, Uid(2), "m10", "2", "Shock", OracleOf(2)),
                Result(3, Uid(3), "m11", "3", "Shock", OracleOf(2))));
        RemoteDeckEntry entry = Entry("Shock");

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("2", choices.Find(entry)!.ProviderCardId);
        Assert.Equal(
            "Archidekt printing m10 2 (first of 2 Archidekt printings of this name; no printing was set).",
            choices.Find(entry)!.Description);
    }

    /// <summary>
    /// Verifies a set printing of a card with hundreds of printings is found through the set filter: the search asks
    /// for the set, so the printing is on the first page although the name alone would list it far past the pages
    /// the resolver reads.
    /// </summary>
    [Fact]
    public async Task SetPrintingOfACardWithManyPrintings_IsFoundThroughTheSetFilter()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Forest&exact=true&edition=dmu&pageSize=25",
            Page(
                2,
                null,
                Result(274, Uid(274), "dmu", "274", "Forest", OracleOf(6)),
                Result(275, Uid(275), "dmu", "275", "Forest", OracleOf(6))));
        RemoteDeckEntry entry = Entry("Forest") with { SetCode = "DMU", CollectorNumber = "275" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("275", choices.Find(entry)!.ProviderCardId);
        Assert.Equal(1, choices.SearchRequests);
    }

    /// <summary>
    /// Verifies a double-faced card that Archidekt does not return for its exact front-face name is asked for again
    /// without the exact-name flag, and the name rule then keeps only its own card.
    /// </summary>
    [Fact]
    public async Task ExactSearchWithNoResult_IsAskedAgainWithoutTheExactFlag()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Delver%20of%20Secrets&exact=true&pageSize=25", Page(0, null));
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Delver%20of%20Secrets&pageSize=25",
            Page(
                2,
                null,
                Result(1, Uid(1), "isd", "51", "Delver of Secrets // Insectile Aberration", OracleOf(4)),
                Result(2, Uid(2), "isd", "52", "Delver of Secrets Fan Card", OracleOf(5))));
        RemoteDeckEntry entry = Entry("Delver of Secrets");

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("1", choices.Find(entry)!.ProviderCardId);
        Assert.Equal(2, choices.SearchRequests);
    }

    /// <summary>
    /// Verifies a result that holds both an edition code and a top-level set code is read by the edition code, the way
    /// a deck relation is read, so the search and the deck read agree.
    /// </summary>
    [Fact]
    public async Task ResultWithBothSetCodes_IsReadByTheEditionCode()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Island&exact=true&edition=dmu&pageSize=25",
            """
            {"count":1,"next":null,"results":[{"id":501,"uid":"33333333-3333-3333-3333-333333333333",
            "setCode":"xxx","collectorNumber":"278","edition":{"editioncode":"dmu"},
            "oracleCard":{"name":"Island","uid":"44444444-4444-4444-4444-444444444444"}}]}
            """);
        RemoteDeckEntry entry = Entry("Island") with { SetCode = "dmu", CollectorNumber = "278" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal("501", choices.Find(entry)!.ProviderCardId);
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
            "api/cards/v2/?name=Sol%20Ring&exact=true&edition=c21&pageSize=25",
            Page(
                2,
                "https://example.invalid/steal?x=1",
                Result(1, Uid(1), "cmd", "1", "Sol Ring", TrophyOracle)));
        RemoteDeckEntry entry = Entry("Sol Ring") with { SetCode = "c21", CollectorNumber = "263" };

        ArchidektPrintingChoices choices = await ResolveAsync(handler, entry);

        Assert.Equal(["Sol Ring"], choices.UnresolvedCardNames);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies one search serves every entry of a name and a set, that an entry with a set code is searched in that
    /// set, and that a name with no result is reported after a second try without the exact-name flag.
    /// </summary>
    [Fact]
    public async Task OneSearchPerNameAndSet_AndNamesWithoutAResultAreReported()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Assassin%27s%20Trophy&exact=true&edition=tmc&pageSize=25",
            TrophyPage(count: 1));
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Nonexistent%20Card&exact=true&pageSize=25", Page(0, null));
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Nonexistent%20Card&pageSize=25", Page(0, null));
        RemoteDeckEntry first = Entry("Assassin's Trophy");
        RemoteDeckEntry again = Entry("Assassin's Trophy") with { PrintingId = Guid.NewGuid() };
        RemoteDeckEntry second = first with { SetCode = "tmc", CollectorNumber = "55" };
        RemoteDeckEntry missing = Entry("Nonexistent Card");

        ArchidektPrintingChoices choices = await ResolveAsync(handler, first, again, second, missing);

        Assert.Equal("151025", choices.Find(first)!.ProviderCardId);
        Assert.Equal("150200", choices.Find(second)!.ProviderCardId);
        Assert.Equal(
            ["Assassin's Trophy", "Nonexistent Card"],
            choices.UnresolvedCardNames.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                TrophyRoute,
                "api/cards/v2/?name=Assassin%27s%20Trophy&exact=true&edition=tmc&pageSize=25",
                "api/cards/v2/?name=Nonexistent%20Card&exact=true&pageSize=25",
                "api/cards/v2/?name=Nonexistent%20Card&pageSize=25",
            ],
            handler.Requests.Select(value => value.Path));
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
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Nonexistent%20Card&exact=true&pageSize=25", Page(0, null));
        handler.Add(HttpMethod.Get, "api/cards/v2/?name=Nonexistent%20Card&pageSize=25", Page(0, null));
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(Entry("Nonexistent Card"));
        target = target with { Name = "Renamed" };

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("not-applied", result.Outcome);
        Assert.Equal(["metadata-update", "entry-add"], result.Operations.Select(value => value.Kind));
        Assert.Equal("not-attempted", result.Operations[0].Status);
        Assert.Equal("Not sent: an earlier operation stopped the push.", result.Operations[0].Message);
        Assert.Equal("not-sent", result.Operations[1].Status);
        Assert.StartsWith(
            "Nothing was sent to Archidekt for this operation.",
            result.Operations[1].Message,
            StringComparison.Ordinal);
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
    /// Verifies a failure after a write was attempted stays unknown for every card in that request, and the rest are
    /// not sent.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_FailureAfterAWriteStarted_IsUnknown()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, TrophyRoute, TrophyPage(count: 18));
        handler.Add(
            HttpMethod.Get,
            "api/cards/v2/?name=Assassin%27s%20Trophy&exact=true&edition=tmc&pageSize=25",
            TrophyPage(count: 18));
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}", HttpStatusCode.InternalServerError);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(
            Entry("Assassin's Trophy"),
            Entry("Assassin's Trophy") with { SetCode = "tmc", CollectorNumber = "55" });

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.All(result.Operations, value => Assert.Equal("unknown", value.Status));
        Assert.Single(handler.Requests, value => value.Method == HttpMethod.Patch);
    }

    /// <summary>
    /// Verifies 40 new cards are sent in two requests of 25 and 15 cards, each card with its own patch ID, and that
    /// the plan predicted at least those requests.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_FortyNewCards_SendsTwoMutationRequests()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.AddAcceptedCards(HttpMethod.Patch, "api/decks/42/modifyCards/v2/");
        handler.AddAcceptedCards(HttpMethod.Patch, "api/decks/42/modifyCards/v2/");
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(40));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        CapturedArchidektRequest[] sent = [.. handler.Requests.Where(value => value.Method == HttpMethod.Patch)];
        Assert.Equal(2, sent.Length);
        Assert.Equal([25, 15], sent.Select(value => CardsIn(value).Count));
        List<string?> patchIds =
            [.. sent.SelectMany(CardsIn).Select(value => value.GetProperty("patchId").GetString())];
        Assert.Equal(40, patchIds.Distinct().Count());
        Assert.All(result.Operations, value => Assert.NotEqual("unknown", value.Status));
        Assert.Equal(40, result.Operations.Count(value => value.Status == "applied"));
        Assert.True(sent.Length <= ArchidektSyncPlanner.PlanRemoteApply(current, target).OperationRequestCount);
    }

    /// <summary>
    /// Verifies a card missing from the answer of an accepted batch is unknown, the others are applied, and the push
    /// does not go on to verify.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_CardMissingFromTheAcceptedAnswer_IsUnknown()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.AddAcceptedCards(HttpMethod.Patch, Route, omitIndex: 1);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(3));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.Equal(["applied", "unknown", "applied"], result.Operations.Select(value => value.Status));
        Assert.Equal("Card 02", result.Operations[1].Subject);
        Assert.Contains("did not list this card", result.Operations[1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requests, value => value.Method == HttpMethod.Get);
    }

    /// <summary>
    /// Verifies an answer that holds no patch IDs, or no JSON, leaves every added card unknown instead of applied.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"add":[{"deckRelationId":9001,"patchId":"00000000000000000000000000000000"}]}""")]
    [InlineData("not json")]
    public async Task ApplyPlan_AnswerWithoutTheSentPatchIds_LeavesEveryAddUnknown(string answer)
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, Route, answer, HttpStatusCode.Created);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(2));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.All(result.Operations, value => Assert.Equal("unknown", value.Status));
    }

    /// <summary>
    /// Verifies a batch Archidekt refuses without naming a card is sent again one card at a time, the cards before and
    /// after the refused one are applied, the refused card is named as not applied, the push goes on, and the requests
    /// sent stay within the prediction.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_BatchWithOneBadCard_AppliesTheGoodCardsAndNamesTheBadCard()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, Route, RefusedBody, HttpStatusCode.BadRequest);
        handler.AddAcceptedCards(HttpMethod.Patch, Route);
        handler.AddAcceptedCards(HttpMethod.Patch, Route);
        handler.Add(HttpMethod.Patch, Route, RefusedBody, HttpStatusCode.BadRequest);
        handler.AddAcceptedCards(HttpMethod.Patch, Route);
        handler.AddAcceptedCards(HttpMethod.Patch, Route);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(5));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        CapturedArchidektRequest[] sent = [.. handler.Requests.Where(value => value.Method == HttpMethod.Patch)];
        Assert.Equal([5, 1, 1, 1, 1, 1], sent.Select(value => CardsIn(value).Count));
        Assert.Equal("partial", result.Outcome);
        Assert.Equal(
            ["applied", "applied", "not-applied", "applied", "applied"],
            result.Operations.Select(value => value.Status));
        Assert.Equal("Card 03", result.Operations[2].Subject);
        Assert.Null(result.FinalRemoteFingerprint);
        Assert.True(handler.Requests.Count <= ArchidektSyncPlanner.PlanRemoteApply(current, target)
            .PredictedProviderRequests);
    }

    /// <summary>
    /// Verifies a second rejected batch in one push is not sent card by card, since the prediction reserves one
    /// fallback: its cards are not sent, the earlier cards stay applied, and the requests sent stay within the
    /// prediction.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_TwoRejectedBatches_SendsTheSecondOneNoMoreAndStaysWithinThePrediction()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, Route, RefusedBody, HttpStatusCode.BadRequest);
        for (int card = 0; card < 25; card++)
        {
            handler.AddAcceptedCards(HttpMethod.Patch, Route);
        }

        handler.Add(HttpMethod.Patch, Route, RefusedBody, HttpStatusCode.BadRequest);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(30));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal(27, handler.Requests.Count(value => value.Method == HttpMethod.Patch));
        Assert.Equal("partial", result.Outcome);
        Assert.Equal(25, result.Operations.Count(value => value.Status == "applied"));
        Assert.Equal(5, result.Operations.Count(value => value.Status == "not-sent"));
        Assert.Contains("Push again", result.Operations[^1].Message, StringComparison.Ordinal);
        Assert.True(handler.Requests.Count <= ArchidektSyncPlanner.PlanRemoteApply(current, target)
            .PredictedProviderRequests);
    }

    /// <summary>
    /// Verifies a rate-limit answer to a batch of 25 leaves its cards not sent and the rest not attempted, with no
    /// fallback and nothing applied.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_RateLimitOnABatch_IsNotSentAndNotRetried()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, Route, "{}", HttpStatusCode.TooManyRequests, "1");
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(30));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("not-applied", result.Outcome);
        Assert.Equal(25, result.Operations.Count(value => value.Status == "not-sent"));
        Assert.Equal(5, result.Operations.Count(value => value.Status == "not-attempted"));
        Assert.Single(handler.Requests, value => value.Method == HttpMethod.Patch);
    }

    /// <summary>
    /// Verifies a rate-limit answer to one card sent during the fallback stops the push there: the cards before it stay
    /// applied, it is not sent, and the cards after it are not attempted.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_RateLimitDuringTheFallback_StopsThePush()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, Route, RefusedBody, HttpStatusCode.BadRequest);
        handler.AddAcceptedCards(HttpMethod.Patch, Route);
        handler.Add(HttpMethod.Patch, Route, "{}", HttpStatusCode.TooManyRequests, "1");
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(5));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.Equal(
            ["applied", "not-sent", "not-attempted", "not-attempted", "not-attempted"],
            result.Operations.Select(value => value.Status));
        Assert.Equal(3, handler.Requests.Count(value => value.Method == HttpMethod.Patch));
    }

    /// <summary>
    /// Verifies a refusal that is not a 400 on a batch is not sent card by card: every card of the batch is unknown and
    /// the push stops.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ApplyPlan_RefusalThatIsNotA400_HasNoFallback(HttpStatusCode status)
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, Route, "{}", status);
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(3));

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.All(result.Operations, value => Assert.Equal("unknown", value.Status));
        Assert.Single(handler.Requests, value => value.Method == HttpMethod.Patch);
    }

    /// <summary>
    /// Verifies a 404 on an update after a batch of adds was applied stops the push with the update unknown and keeps
    /// the applied statuses of the batch, instead of losing them in a failure.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_NotFoundOnAnUpdateAfterAnAppliedBatch_KeepsTheEarlierStatuses()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.AddAcceptedCards(HttpMethod.Patch, Route);
        handler.Add(HttpMethod.Patch, Route, "{}", HttpStatusCode.NotFound);
        (RemoteDeckSnapshot current, _) = DeckWithNewCard();
        RemoteDeckSnapshot target = current with
        {
            Entries = [current.Entries[0], .. NewCards(2), current.Entries[1] with { Quantity = 4 }],
        };

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.Equal(
            [("entry-add", "applied"), ("entry-add", "applied"), ("entry-update", "unknown")],
            result.Operations.Select(value => (value.Kind, value.Status)));
    }

    /// <summary>
    /// Verifies a 400 on a write that is not an add stays unknown, because only a refused add is known to leave the
    /// deck unchanged.
    /// </summary>
    [Theory]
    [InlineData("entry-update")]
    [InlineData("entry-remove")]
    [InlineData("category-update")]
    [InlineData("category-create")]
    public async Task ApplyPlan_RejectedWriteThatIsNotAnAdd_IsUnknown(string kind)
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        (RemoteDeckSnapshot current, _) = DeckWithNewCard();
        (HttpMethod method, string route, RemoteDeckSnapshot target) = kind switch
        {
            "entry-update" => (HttpMethod.Patch, Route, current with
            {
                Entries = [current.Entries[0], current.Entries[1] with { Quantity = 4 }],
            }),
            "entry-remove" => (HttpMethod.Patch, Route, current with { Entries = [current.Entries[0]] }),
            "category-update" => (HttpMethod.Patch, "api/decks/category/10/", current with
            {
                Categories = [current.Categories[0] with { IncludedInPrice = false }, current.Categories[1]],
            }),
            _ => (HttpMethod.Post, "api/decks/createCategory/", current with
            {
                Categories = [.. current.Categories, new RemoteDeckCategory(string.Empty, "Ramp", true, true, false, 2)],
            }),
        };
        handler.Add(method, route, RefusedBody, HttpStatusCode.BadRequest);

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        ArchidektOperationStatus status = Assert.Single(result.Operations);
        Assert.Equal((kind, "unknown"), (status.Kind, status.Status));
    }

    /// <summary>
    /// Verifies a 400 on a single add that is not part of a batch fallback marks that card not applied and the push
    /// goes on to the next operation.
    /// </summary>
    [Fact]
    public async Task ApplyPlan_RejectedSingleAdd_IsNotAppliedAndThePushGoesOn()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        handler.Add(HttpMethod.Patch, Route, RefusedBody, HttpStatusCode.BadRequest);
        handler.AddAcceptedCards(HttpMethod.Patch, Route);
        (RemoteDeckSnapshot current, _) = DeckWithNewCard();
        RemoteDeckSnapshot target = current with
        {
            Entries = [current.Entries[0], .. NewCards(1), current.Entries[1] with { Quantity = 4 }],
        };

        ArchidektApplyResult result = await ApplyAsync(handler, current, target);

        Assert.Equal("partial", result.Outcome);
        Assert.Equal(
            [("entry-add", "not-applied"), ("entry-update", "applied")],
            result.Operations.Select(value => (value.Kind, value.Status)));
        Assert.Equal(2, handler.Requests.Count(value => value.Method == HttpMethod.Patch));
    }

    /// <summary>
    /// Verifies the prediction counts one request per run of up to 25 adds, one request for any other operation, and
    /// the cards of the largest batch again for the single sends after a refused batch.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1 + 2)]
    [InlineData(25, 1 + 25)]
    [InlineData(26, 2 + 25)]
    [InlineData(40, 2 + 25)]
    public void OperationRequests_CountsBatchesAndTheFallbackReserve(int adds, int expected)
    {
        (RemoteDeckSnapshot current, RemoteDeckSnapshot target) = DeckWithNewCard(NewCards(adds));

        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);

        Assert.Equal(expected, plan.OperationRequestCount);
        Assert.Equal(expected + 1, plan.PredictedProviderRequests);
    }

    /// <summary>
    /// Builds new entries that already carry an Archidekt card ID, so no search is needed to send them.
    /// </summary>
    private static RemoteDeckEntry[] NewCards(int count)
    {
        return
        [
            .. Enumerable.Range(1, count).Select(number => Entry($"Card {number:00}") with
            {
                ProviderCardId = (5000 + number).ToString(System.Globalization.CultureInfo.InvariantCulture),
                SortOrder = number,
            }),
        ];
    }

    /// <summary>
    /// Reads the card payloads of a recorded modifyCards request.
    /// </summary>
    private static List<JsonElement> CardsIn(CapturedArchidektRequest request)
    {
        using JsonDocument document = JsonDocument.Parse(request.Body);
        return [.. document.RootElement.GetProperty("cards").EnumerateArray().Select(value => value.Clone())];
    }

    /// <summary>
    /// Holds the card-mutation route of the test deck.
    /// </summary>
    private const string Route = "api/decks/42/modifyCards/v2/";

    /// <summary>
    /// Holds the answer Archidekt gave to a batch with one card it refused: 400, and the deck left unchanged.
    /// </summary>
    private const string RefusedBody = """{"error":"Unable to add one or more cards"}""";

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
    /// Builds the Scryfall printing ID the tests give to result number <paramref name="number"/>.
    /// </summary>
    private static string Uid(int number)
    {
        return $"10000000-0000-0000-0000-{number:D12}";
    }

    /// <summary>
    /// Builds the Oracle ID the tests give to card number <paramref name="number"/>.
    /// </summary>
    private static string OracleOf(int number)
    {
        return $"00000000-0000-0000-0000-{number:D12}";
    }

    /// <summary>
    /// Builds a page of Forest results for other printings than the test asks for, with a next link to the page after.
    /// </summary>
    private static string EndlessPage(int page)
    {
        return Page(
            955,
            $"https://archidekt.com/api/cards/v2/?name=Forest&exact=true&page={page + 1}&pageSize=25",
            Result(
                page,
                $"10000000-0000-0000-0000-{page:D12}",
                "dmu",
                page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Forest",
                OracleOf(6)));
    }

    /// <summary>
    /// Builds one search page in the real service's shape.
    /// </summary>
    private static string Page(int count, string? next, params string[] results)
    {
        string nextJson = next is null ? "null" : JsonSerializer.Serialize(next);
        string items = string.Join(',', results);
        return $$"""{"count":{{count}},"next":{{nextJson}},"previous":null,"results":[{{items}}]}""";
    }

    /// <summary>
    /// Builds one search result in the real service's shape: the set is in the edition, and there is no top-level set
    /// code.
    /// </summary>
    private static string Result(int id, string uid, string set, string number, string name, string oracle)
    {
        string quoted = JsonSerializer.Serialize(name);
        return $$$"""
            {"id":{{{id}}},"uid":"{{{uid}}}","collectorNumber":"{{{number}}}","edition":{"editioncode":"{{{set}}}"},
            "oracleCard":{"uid":"{{{oracle}}}","name":{{{quoted}}}}}
            """;
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
