using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.CardLists;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>card_list_query</c> over every source kind, its paging, its size limit, and its errors (QUERY-001 to
/// QUERY-007, cases Q09 and Q10, and tasks T22 to T25).
/// </summary>
public sealed class CardListQueryTests
{
    /// <summary>
    /// Verifies a deck grouped by mana value gives the curve with copies counted, and the deck entries that have no
    /// card are listed as unmatched (task T22).
    /// </summary>
    [Fact]
    public async Task Query_DeckGroupedByManaValue_GivesTheCurveAndListsUnmatchedEntries()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();

        CardListQueryResult result = Success(await Query(
            world,
            new DeckSourceInput(deck.DeckId),
            groupBy: ["manaValue"],
            metrics: ["quantity", "cards"]));

        Assert.Equal(
            [(1L, 1L), (2L, 3L), (4L, 1L)],
            result.Groups.Select(group => (
                (long)((ScryfallOrderedObject)group.Get("key")!).Get("manaValue")!,
                (long)group.Get("quantity")!)));
        Assert.Equal(new ScryfallListMatched(3, 5), result.Matched);
        Assert.Equal(2, result.Unmatched.Count);
        Assert.Equal(deck.Revision, result.Sources[0].Revision);
        Assert.False(string.IsNullOrEmpty(result.Fingerprint));
        Assert.Null(result.Rows);
    }

    /// <summary>
    /// Verifies counts by oracle tag count a card with two tags in both groups and say so (task T23).
    /// </summary>
    [Fact]
    public async Task Query_GroupByTags_CountsMultiTaggedCardsInEveryTag()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();

        CardListQueryResult result = Success(await Query(
            world,
            new DeckSourceInput(deck.DeckId),
            groupBy: ["tags"],
            rows: new CardListRowsInput(["name", "tags"])));

        Assert.True(result.MultiValued);
        Assert.Equal(
            [("draw", 3L), ("ramp", 5L)],
            result.Groups.Select(group => (
                (string)((ScryfallOrderedObject)group.Get("key")!).Get("tags")!,
                (long)group.Get("quantity")!)));
        Assert.Equal(["Alpha", "Delta", "Foxtrot"], result.Rows!.Items.Select(row => (string)row.Get("name")!));
    }

    /// <summary>
    /// Verifies pips, sources, and a price sum over a deck (tasks T24 and T25).
    /// </summary>
    [Fact]
    public async Task Query_PipsSourcesAndPriceSum_AreCountedOverTheDeck()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();

        CardListQueryResult result = Success(await Query(
            world,
            new DeckSourceInput(deck.DeckId),
            metrics: ["pips", "sources", "sum:priceUsd", "sum:cheapestPriceUsd"],
            rows: new CardListRowsInput(
                ["name", "priceUsd"],
                [new CardListSortInput("priceUsd", "desc")],
                2)));

        ScryfallOrderedObject total = Assert.Single(result.Groups);
        ScryfallOrderedObject pips = (ScryfallOrderedObject)total.Get("pips")!;
        Assert.Equal(1L, pips.Get("G"));
        Assert.Equal(3L, pips.Get("R"));
        Assert.Equal(1L, ((ScryfallOrderedObject)total.Get("sources")!).Get("G"));
        Assert.Equal("5.50", total.Get("sum:priceUsd"));
        Assert.Equal(["Foxtrot", "Alpha"], result.Rows!.Items.Select(row => (string)row.Get("name")!));
        Assert.NotNull(result.Rows.NextCursor);
    }

    /// <summary>
    /// Verifies the rows cursor pages the rows in a stable order and ends with a null cursor.
    /// </summary>
    [Fact]
    public async Task Query_RowsCursor_PagesStablyAndEnds()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        DeckSourceInput source = new(deck.DeckId);
        CardListRowsInput rows = new(["name", "zone", "category", "quantity"], PageSize: 2);

        CardListQueryResult first = Success(await Query(world, source, rows: rows));
        CardListQueryResult second = Success(await Query(world, source, rows: rows, cursor: first.Rows!.NextCursor));
        CardListQueryResult again = Success(await Query(world, source, rows: rows));

        Assert.Equal(["Alpha", "Delta"], first.Rows.Items.Select(row => (string)row.Get("name")!));
        Assert.Equal(["Foxtrot"], second.Rows!.Items.Select(row => (string)row.Get("name")!));
        Assert.Equal(3, first.Rows.TotalRows);
        Assert.Null(second.Rows.NextCursor);
        Assert.Equal(first.Rows.NextCursor, again.Rows!.NextCursor);
        Assert.Equal("commander", second.Rows.Items[0].Get("zone"));
    }

    /// <summary>
    /// Verifies every other source kind works: a saved search, a card list, a backup, Archidekt, and a nested
    /// comparison. Deck fields exist only for deck-like sources.
    /// </summary>
    [Fact]
    public async Task Query_EverySourceKind_ReadsItsCards()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        DeckBackup backup = Success(await world.Data.Store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        Guid snapshot = await world.Data.SaveSearchAsync();
        world.QueueRemoteDeck(world.Echo, world.Foxtrot);
        world.QueueRemoteDeck(world.Echo, world.Foxtrot);
        world.Archidekt.Add(
            HttpMethod.Get,
            "api/decks/snapshots/77/",
            "{\"id\":77,\"name\":\"S\",\"description\":\"\",\"createdAt\":\"2026-07-04T12:00:00Z\",\"deck\":" +
            CardListWorld.RemoteDeck(world.Bravo) + "}");
        CardListSourceInput[] sources =
        [
            new ScryfallSnapshotSourceInput(snapshot),
            new CardsSourceInput([CardJson.PrintingOf(world.Alpha), CardJson.OracleOf(world.Bravo)]),
            new DeckBackupSourceInput(backup.BackupId, deck.DeckId),
            new ArchidektDeckSourceInput("42"),
            new ArchidektSnapshotSourceInput("42", "77"),
            new CompareSourceInput(
                new ScryfallSnapshotSourceInput(snapshot),
                new CardsSourceInput([CardJson.PrintingOf(world.Alpha)]),
                "difference"),
        ];
        int[] expectedCards = [3, 2, 3, 2, 1, 2];

        for (int index = 0; index < sources.Length; index++)
        {
            CardListQueryResult result = Success(await Query(world, sources[index]));
            Assert.Equal(expectedCards[index], result.Matched.Cards);
        }

        OperationInvalidInput deckFieldOnSearch = Failure(await Query(
            world,
            sources[0],
            groupBy: ["zone"]));
        Assert.Equal("groupBy[0]", deckFieldOnSearch.Path);
        CardListQueryResult remote = Success(await Query(
            world,
            new ArchidektDeckSourceInput("42"),
            groupBy: ["category"]));
        Assert.Equal("Mainboard", ((ScryfallOrderedObject)Assert.Single(remote.Groups).Get("key")!).Get("category"));
    }

    /// <summary>
    /// Verifies card data that is not installed gives not-cached and names the sync tool (case Q09), and an unknown
    /// field lists the allowed fields (case Q10).
    /// </summary>
    [Fact]
    public async Task Query_WithoutCardDataOrWithAnUnknownField_FailsWithAClearReason()
    {
        using CardListWorld missing = await CardListWorld.CreateAsync(install: false);
        DeckDocument deck = await missing.CreateDeckAsync();
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument realDeck = await world.CreateDeckAsync();

        OperationNotCached notCached = Assert.IsType<OperationNotCached>(
            (await Query(missing, new DeckSourceInput(deck.DeckId))).Value);
        OperationInvalidInput unknown = Failure(await Query(
            world,
            new DeckSourceInput(realDeck.DeckId),
            groupBy: ["colour"]));

        Assert.Contains("scryfall_card_data_sync", notCached.Message, StringComparison.Ordinal);
        Assert.Equal("invalid-field", unknown.ReasonCode);
        Assert.Contains("manaValue", unknown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies filters, a stale revision, and bad paging arguments are reported at their paths.
    /// </summary>
    [Fact]
    public async Task Query_BadArgumentsAndStaleSources_AreReported()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        DeckSourceInput source = new(deck.DeckId);

        OperationInvalidInput size = Failure(await Query(world, source, rows: new CardListRowsInput(PageSize: 0)));
        OperationInvalidInput noRows = Failure(await Query(world, source, cursor: "x"));
        OperationInvalidInput badCursor = Failure(await Query(
            world,
            source,
            rows: new CardListRowsInput(["name"], PageSize: 1),
            cursor: "x"));
        OperationInvalidInput badSource = Failure(await Query(world, new CardsSourceInput([])));
        OperationConflict stale = Assert.IsType<OperationConflict>((await Query(
            world,
            new DeckSourceInput(deck.DeckId, ExpectedRevision: deck.Revision + 1))).Value);
        CardListQueryResult filtered = Success(await Query(
            world,
            source,
            where: [new CardListFilterInput("quantity", "gte", JsonElement.Parse("2"))],
            rows: new CardListRowsInput(["name"])));

        Assert.Equal("rows.pageSize", size.Path);
        Assert.Equal("cursor-requires-rows", noRows.ReasonCode);
        Assert.Equal("invalid-cursor", badCursor.ReasonCode);
        Assert.Equal("invalid-source", badSource.ReasonCode);
        Assert.Equal(deck.Revision, stale.Current!.Revision);
        Assert.Equal(["Delta"], filtered.Rows!.Items.Select(row => (string)row.Get("name")!));
    }

    /// <summary>
    /// Verifies a changed deck between row pages returns a conflict with the new revision.
    /// </summary>
    [Fact]
    public async Task Query_DeckChangedBetweenRowPages_ReturnsConflict()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        DeckSourceInput source = new(deck.DeckId);
        CardListRowsInput rows = new(["name"], PageSize: 1);
        CardListQueryResult first = Success(await Query(world, source, rows: rows));
        _ = Success(await world.Data.Store.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new UpdateDeckMetadataChange("Renamed", string.Empty, "commander")],
            TestContext.Current.CancellationToken));

        OperationConflict conflict = Assert.IsType<OperationConflict>(
            (await Query(world, source, rows: rows, cursor: first.Rows!.NextCursor)).Value);

        Assert.Equal(deck.Revision + 1, conflict.Current!.Revision);
    }

    /// <summary>
    /// Verifies a query that would return too many groups keeps the leading groups that fit the size limit, says
    /// so, and names what to narrow (SIZE-001).
    /// </summary>
    [Fact]
    public async Task Query_ManyGroups_AreCutToTheSizeLimitWithANarrowHint()
    {
        List<JsonObject> cards =
        [
            .. Enumerable.Range(0, 400).Select(index => CardJson.Make(
                $"Card number {index:D4} with a long name to fill the group list",
                manaCost: "{W/U}{G/P}",
                producedMana: ["G"],
                usd: "1.25")),
        ];
        using CardDataFixture data = await CardDataFixture.CreateAsync(cards);
        DeckCreateRequest request = new(
            "Big",
            Format: "commander",
            Entries: [.. cards.Select(card => new DeckEntryDraft(
                1,
                card["name"]!.GetValue<string>(),
                PrintingId: CardJson.PrintingOf(card)))]);
        DeckDocument deck = Success(await data.Store.CreateAsync(request, TestContext.Current.CancellationToken));

        CardListQueryResult result = Success(await data.Coordinator().QueryAsync(
            new DeckSourceInput(deck.DeckId),
            null,
            ["name"],
            ["quantity", "cards", "pips", "sources", "sum:priceUsd"],
            new CardListRowsInput(["name", "typeLine", "oracleText"], PageSize: 100),
            null,
            TestContext.Current.CancellationToken));

        int bytes = JsonSerializer.SerializeToUtf8Bytes(result, ToolCallPolicy.SerializerOptions).Length;
        Assert.True(bytes <= 40_000, $"The result is {bytes} bytes.");
        Assert.True(result.Truncated);
        Assert.Contains("Group by a field", result.Narrow, StringComparison.Ordinal);
        Assert.True(result.Groups.Count < 400);
        Assert.NotNull(result.Rows);
    }

    /// <summary>
    /// Verifies the published examples bind to the tool and run (SCHEMA-006).
    /// </summary>
    [Fact]
    public async Task Examples_BindAndRun()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        using ToolCatalogFixture fixture = new();
        (string Name, string Json)[] examples = CardListExampleSupport.ReadExamples(
            CardListExampleSupport.DescriptionOf("card_list_query"));

        Assert.Equal(["curve", "pips", "full"], examples.Select(example => example.Name));
        foreach ((string name, string json) in examples)
        {
            string filled = CardListExampleSupport.Fill(json, deck, Guid.NewGuid(), Guid.NewGuid());
            Dictionary<string, JsonElement> arguments = CardListExampleSupport.Arguments(filled);
            Assert.Null(ToolCallPolicy.DiagnoseArguments(fixture.Catalog, "card_list_query", arguments));
            CardListTools tools = new(world.Coordinator());

            OperationResult<CardListQueryResult> result = await tools.QueryAsync(
                arguments["source"].Deserialize<CardListSourceInput>(ToolCallPolicy.SerializerOptions)!,
                arguments.TryGetValue("where", out JsonElement where)
                    ? where.Deserialize<IReadOnlyList<CardListFilterInput>>(ToolCallPolicy.SerializerOptions)
                    : null,
                arguments.TryGetValue("groupBy", out JsonElement groupBy) ? groupBy.Deserialize<string[]>() : null,
                arguments.TryGetValue("metrics", out JsonElement metrics) ? metrics.Deserialize<string[]>() : null,
                arguments.TryGetValue("rows", out JsonElement rows)
                    ? rows.Deserialize<CardListRowsInput>(ToolCallPolicy.SerializerOptions)
                    : null,
                null,
                TestContext.Current.CancellationToken);

            Assert.True(
                result.Value is OperationSuccess<CardListQueryResult>,
                $"{name}: {JsonSerializer.Serialize(result.Value)}");
        }
    }

    /// <summary>
    /// Verifies a cursor past the end of the rows is refused exactly as card_lists_compare refuses it, not answered
    /// with an empty page.
    /// </summary>
    [Fact]
    public async Task Query_CursorPastTheEnd_IsInvalid()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        DeckSourceInput source = new(deck.DeckId);
        CardListRowsInput rows = new(["name"], PageSize: 2);
        CardListQueryResult first = Success(await Query(world, source, rows: rows));
        string past = CardListCursors.Encode(first.Fingerprint, 99, [.. first.Sources.Select(item => item.Marker)]);

        OperationInvalidInput beyond = Failure(await Query(world, source, rows: rows, cursor: past));

        Assert.Equal(("invalid-cursor", "cursor"), (beyond.ReasonCode, beyond.Path));
    }

    /// <summary>
    /// Verifies the tag and price lookups run only when the query names tags or the cheapest price, in any part of
    /// the request.
    /// </summary>
    [Fact]
    public void NeedsFacts_OnlyForTagsAndCheapestPrice()
    {
        CardListFilterInput byTags = new("tags", "contains", JsonSerializer.SerializeToElement("ramp"));
        CardListFilterInput byName = new("name", "contains", JsonSerializer.SerializeToElement("a"));

        Assert.False(CardListCoordinator.NeedsFacts(null, null, null, null));
        Assert.False(CardListCoordinator.NeedsFacts([byName], ["manaValue"], ["sum:priceUsd", "pips"],
            new CardListRowsInput(["name"], [new CardListSortInput("name", "asc")])));
        Assert.True(CardListCoordinator.NeedsFacts([byTags], null, null, null));
        Assert.True(CardListCoordinator.NeedsFacts(null, ["tags"], null, null));
        Assert.True(CardListCoordinator.NeedsFacts(null, null, ["avg:cheapestPriceUsd"], null));
        Assert.True(CardListCoordinator.NeedsFacts(null, null, null, new CardListRowsInput(["cheapestPriceUsd"])));
        Assert.True(CardListCoordinator.NeedsFacts(
            null,
            null,
            null,
            new CardListRowsInput(["name"], [new CardListSortInput("cheapestPriceUsd", "desc")])));
    }

    /// <summary>
    /// Verifies a request is refused when its sources together hold more cards than the request budget, naming the
    /// source that went over, and that a snapshot larger than the remaining budget is refused before it is read.
    /// </summary>
    [Fact]
    public async Task Resolve_SourcesTogetherOverTheBudget_AreRefusedWithTheSourceNamed()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        CardListResolver resolver = new(world.Data.Scryfall, world.Data.Access());
        CardListReadOptions options = new(IncludeRaw: false, IncludeFacts: false, new CardListBudget(5));
        CardsSourceInput three = new([.. world.Cards.Take(3).Select(CardJson.PrintingOf)]);

        OperationResult<ResolvedCardList> left = await resolver.ResolveAsync(
            three, "left", options, TestContext.Current.CancellationToken);
        OperationResult<ResolvedCardList> right = await resolver.ResolveAsync(
            three, "right", options, TestContext.Current.CancellationToken);

        Assert.IsType<OperationSuccess<ResolvedCardList>>(left.Value);
        OperationInvalidInput over = Assert.IsType<OperationInvalidInput>(right.Value);
        Assert.Equal(("list-too-large", "right"), (over.ReasonCode, over.Path));
        Assert.Contains("5", over.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a query over a saved search that uses no tags or prices runs without installed card data, and one
    /// that groups by tags answers not-cached naming the sync tool.
    /// </summary>
    [Fact]
    public async Task Query_SnapshotWithoutCardData_NeedsItOnlyForTagsAndPrices()
    {
        using CardListWorld world = await CardListWorld.CreateAsync(install: false);
        OperationResult<ScryfallSearchResponse> search = await world.Data.Scryfall.SearchAsync(
            "fixture",
            cancellationToken: TestContext.Current.CancellationToken);
        ScryfallSnapshotSourceInput source = new(Success(search).Snapshot.SnapshotId);

        CardListQueryResult plain = Success(await Query(world, source, groupBy: ["manaValue"]));
        OperationNotCached tagged = Assert.IsType<OperationNotCached>((await Query(
            world,
            source,
            groupBy: ["tags"])).Value);

        Assert.Equal(3, plain.Matched.Quantity);
        Assert.Contains("scryfall_card_data_sync", tagged.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs one query with a group, metric, and row choice.
    /// </summary>
    private static Task<OperationResult<CardListQueryResult>> Query(
        CardListWorld world,
        CardListSourceInput source,
        IReadOnlyList<CardListFilterInput>? where = null,
        IReadOnlyList<string>? groupBy = null,
        IReadOnlyList<string>? metrics = null,
        CardListRowsInput? rows = null,
        string? cursor = null)
    {
        return world.Coordinator().QueryAsync(
            source,
            where,
            groupBy,
            metrics,
            rows,
            cursor,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return result.Value is OperationSuccess<T> success
            ? success.Data
            : throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(result.Value));
    }

    /// <summary>
    /// Unwraps an invalid-input result.
    /// </summary>
    private static OperationInvalidInput Failure<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationInvalidInput>(result.Value);
    }
}
