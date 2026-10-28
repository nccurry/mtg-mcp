using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.CardLists;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>card_lists_compare</c> over every source kind, its toolset rule, paging, cursors, and size limit
/// (SET-002 to SET-007, cases C03 to C13 and C16).
/// </summary>
public sealed class CardListCompareTests
{
    /// <summary>
    /// Verifies a saved search minus a deck gives the cards not in the deck, counts both sides, and lists the deck
    /// entries that have no Scryfall card instead of guessing (cases C03 and T18).
    /// </summary>
    [Fact]
    public async Task Compare_SearchMinusDeck_ListsCardsNotInTheDeckAndTheUnmatchedEntries()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        Guid snapshot = await world.Data.SaveSearchAsync();

        CardListsCompareResult result = Success(await Compare(
            world,
            new ScryfallSnapshotSourceInput(snapshot),
            new DeckSourceInput(deck.DeckId),
            "difference"));

        Assert.Equal(["Bravo", "Charlie"], CardListWorld.Names(result));
        Assert.Equal(new CardListCounts(3, 3, 2, 2), result.Counts);
        Assert.Equal(
            [("no-scryfall-identity", "Home-made proxy"), ("not-in-card-data", "Unknown printing")],
            result.Unmatched.Select(entry => (entry.Reason, entry.Name)).Order());
        Assert.Equal(["left", "right"], result.Sources.Select(source => source.Side));
        Assert.Equal(deck.Revision, result.Sources[1].Revision);
        Assert.Equal(snapshot.ToString("D"), result.Sources[0].SnapshotId);
        Assert.Null(result.Page.NextCursor);
        Assert.Equal("concise", result.ResponseFormat);
    }

    /// <summary>
    /// Verifies every operation over the saved search and a card list (case C01 through the tool).
    /// </summary>
    [Theory]
    [InlineData("union", "Alpha,Bravo,Charlie,Delta")]
    [InlineData("intersection", "Alpha")]
    [InlineData("difference", "Bravo,Charlie")]
    [InlineData("symmetric-difference", "Bravo,Charlie,Delta")]
    public async Task Compare_EachOperation_GivesTheExactSet(string operation, string expected)
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        Guid snapshot = await world.Data.SaveSearchAsync();
        CardsSourceInput cards = new([
            CardJson.PrintingOf(world.Alpha),
            CardJson.OracleOf(world.Delta),
        ]);

        CardListsCompareResult result = Success(await Compare(
            world,
            new ScryfallSnapshotSourceInput(snapshot),
            cards,
            operation));

        Assert.Equal(expected.Split(','), CardListWorld.Names(result));
    }

    /// <summary>
    /// Verifies the deck zone filter, a backup source, and the match modes.
    /// </summary>
    [Fact]
    public async Task Compare_DeckZonesAndBackupSources_ReadTheRightEntries()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        DeckBackup backup = Success(await world.Data.Store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        _ = Success(await world.Data.Store.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                .. deck.Entries
                    .Where(entry => entry.CardName == "Alpha")
                    .Select(entry => (DeckChange)new RemoveDeckEntryChange(entry.EntryId)),
            ],
            TestContext.Current.CancellationToken));

        CardListsCompareResult commander = Success(await Compare(
            world,
            new DeckSourceInput(deck.DeckId, Zones: ["COMMANDER"]),
            new CardsSourceInput([CardJson.PrintingOf(world.Alpha)]),
            "union"));
        CardListsCompareResult fromBackup = Success(await Compare(
            world,
            new DeckBackupSourceInput(backup.BackupId, deck.DeckId, ["main"]),
            new CardsSourceInput([CardJson.PrintingOf(world.Bravo)]),
            "union"));
        CardListsCompareResult liveNow = Success(await Compare(
            world,
            new DeckSourceInput(deck.DeckId, Zones: ["main"]),
            new CardsSourceInput([CardJson.PrintingOf(world.Bravo)]),
            "union"));

        Assert.Equal(["Alpha", "Foxtrot"], CardListWorld.Names(commander));
        Assert.Equal(["Alpha", "Bravo", "Delta"], CardListWorld.Names(fromBackup));
        Assert.Equal(["Bravo", "Delta"], CardListWorld.Names(liveNow));
        Assert.Equal("deck-backup", fromBackup.Sources[0].Kind);
        Assert.NotNull(fromBackup.Sources[0].BackupId);
    }

    /// <summary>
    /// Verifies a backup whose deck is missing, and a backup that does not exist, are reported with their reasons
    /// (cases C09 and C15).
    /// </summary>
    [Fact]
    public async Task Compare_BackupSourceProblems_ReportTheReason()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        DeckBackup backup = Success(await world.Data.Store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        CardsSourceInput one = new([CardJson.PrintingOf(world.Alpha)]);

        OperationResult<CardListsCompareResult> missingDeck = await Compare(
            world,
            new DeckBackupSourceInput(backup.BackupId, Guid.CreateVersion7()),
            one,
            "union");
        OperationResult<CardListsCompareResult> missingBackup = await Compare(
            world,
            new DeckBackupSourceInput(Guid.CreateVersion7(), deck.DeckId),
            one,
            "union");

        Assert.Equal("deck-not-in-backup", Assert.IsType<OperationNotFound>(missingDeck.Value).ReasonCode);
        Assert.Equal("deck-backup-not-found", Assert.IsType<OperationNotFound>(missingBackup.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies an Archidekt deck and an Archidekt snapshot are read through the adapter, and a snapshot needs no
    /// state marker (case C10).
    /// </summary>
    [Fact]
    public async Task Compare_ArchidektDeckAndSnapshot_ReadThroughTheAdapter()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        world.QueueRemoteDeck(world.Alpha, world.Bravo);
        world.Archidekt.Add(
            HttpMethod.Get,
            "api/decks/snapshots/77/",
            "{\"id\":77,\"name\":\"Before\",\"description\":\"\",\"createdAt\":\"2026-07-04T12:00:00Z\",\"deck\":" +
            CardListWorld.RemoteDeck(world.Charlie, world.Delta) + "}");
        CardsSourceInput nothing = new([CardJson.PrintingOf(world.Echo)]);

        CardListsCompareResult live = Success(await Compare(
            world,
            new ArchidektDeckSourceInput("42"),
            nothing,
            "difference"));
        CardListsCompareResult saved = Success(await Compare(
            world,
            new ArchidektSnapshotSourceInput("42", "77"),
            nothing,
            "difference"));

        Assert.Equal(["Alpha", "Bravo"], CardListWorld.Names(live));
        Assert.False(string.IsNullOrEmpty(live.Sources[0].StateMarker));
        Assert.Equal(["Charlie", "Delta"], CardListWorld.Names(saved));
        Assert.Null(saved.Sources[0].StateMarker);
        Assert.Equal("77", saved.Sources[0].SnapshotId);
    }

    /// <summary>
    /// Verifies a nested comparison chains operations: (search union card list) minus a deck (case T20).
    /// </summary>
    [Fact]
    public async Task Compare_NestedComparison_ChainsOperations()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        Guid snapshot = await world.Data.SaveSearchAsync();
        CompareSourceInput union = new(
            new ScryfallSnapshotSourceInput(snapshot),
            new CardsSourceInput([CardJson.PrintingOf(world.Echo)]),
            "union");

        CardListsCompareResult result = Success(await Compare(
            world,
            union,
            new DeckSourceInput(deck.DeckId),
            "difference"));

        Assert.Equal(["Bravo", "Charlie", "Echo"], CardListWorld.Names(result));
        Assert.Equal(["left.left", "left.right", "right"], result.Sources.Select(source => source.Side));
    }

    /// <summary>
    /// Verifies a nested comparison four levels deep is refused and the message states the limit (case C04).
    /// </summary>
    [Fact]
    public async Task Compare_NestedFourLevelsDeep_IsInvalidAndStatesTheLimit()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        CardsSourceInput leaf = new([CardJson.PrintingOf(world.Alpha)]);
        CardListSourceInput deep = leaf;
        for (int level = 0; level < 4; level++)
        {
            deep = new CompareSourceInput(deep, leaf, "union");
        }

        OperationInvalidInput failure = Failure(await Compare(world, deep, leaf, "union"));

        Assert.Equal("compare-too-deep", failure.ReasonCode);
        Assert.Contains("3 levels", failure.Message, StringComparison.Ordinal);
        CardListSourceInput allowed = leaf;
        for (int level = 0; level < 3; level++)
        {
            allowed = new CompareSourceInput(allowed, leaf, "union");
        }

        _ = Success(await Compare(world, allowed, leaf, "union"));
    }

    /// <summary>
    /// Verifies a request with 301 card IDs in all is refused and states the limit, and a single source over 150 is
    /// refused (case C13).
    /// </summary>
    [Fact]
    public async Task Compare_TooManyCardIds_IsInvalidAndStatesTheLimit()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        CardsSourceInput first = new([.. Enumerable.Range(0, 150).Select(_ => Guid.NewGuid())]);
        CardsSourceInput second = new([.. Enumerable.Range(0, 150).Select(_ => Guid.NewGuid())]);
        CardsSourceInput third = new([Guid.NewGuid()]);
        CardsSourceInput tooBig = new([.. Enumerable.Range(0, 151).Select(_ => Guid.NewGuid())]);

        OperationInvalidInput nested = Failure(await Compare(
            world,
            new CompareSourceInput(first, second, "union"),
            third,
            "union"));
        OperationInvalidInput single = Failure(await Compare(world, tooBig, third, "union"));
        OperationInvalidInput empty = Failure(await Compare(world, new CardsSourceInput([]), third, "union"));

        Assert.Equal("too-many-card-ids", nested.ReasonCode);
        Assert.Contains("300", nested.Message, StringComparison.Ordinal);
        Assert.Equal("invalid-source", single.ReasonCode);
        Assert.Equal("invalid-source", empty.ReasonCode);
    }

    /// <summary>
    /// Verifies a source whose toolset is off is refused with the reason: Archidekt without credentials, and a deck
    /// when the decks toolset was left out (cases C05 and C11).
    /// </summary>
    [Fact]
    public async Task Compare_SourceWhoseToolsetIsOff_IsInvalidWithTheReason()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        CardsSourceInput one = new([CardJson.PrintingOf(world.Alpha)]);
        CardListCoordinator noArchidekt = new(
            new CardListResolver(
                world.Data.Scryfall,
                new CardListAccess(world.Data.Store, archidekt: null, "credentials-missing")),
            world.Data.Scryfall);
        CardListCoordinator noDecks = world.Coordinator(decks: false, archidekt: true);

        OperationInvalidInput archidekt = Failure(await Compare(
            noArchidekt,
            new ArchidektDeckSourceInput("42"),
            one,
            "union"));
        OperationInvalidInput archidektSnapshot = Failure(await Compare(
            noArchidekt,
            one,
            new ArchidektSnapshotSourceInput("42", "1"),
            "union"));
        OperationInvalidInput deckOff = Failure(await Compare(noDecks, new DeckSourceInput(deck.DeckId), one, "union"));
        OperationInvalidInput backupOff = Failure(await Compare(
            noDecks,
            new DeckBackupSourceInput(Guid.NewGuid(), deck.DeckId),
            one,
            "union"));
        OperationInvalidInput notSelected = Failure(await Compare(
            world.Coordinator(archidekt: false),
            new ArchidektDeckSourceInput("42"),
            one,
            "union"));

        Assert.Equal("credentials-missing", archidekt.ReasonCode);
        Assert.Equal("left", archidekt.Path);
        Assert.Equal("credentials-missing", archidektSnapshot.ReasonCode);
        Assert.Equal("toolset-not-selected", deckOff.ReasonCode);
        Assert.Contains("--toolsets", deckOff.Message, StringComparison.Ordinal);
        Assert.Equal("toolset-not-selected", backupOff.ReasonCode);
        Assert.Equal("toolset-not-selected", notSelected.ReasonCode);
    }

    /// <summary>
    /// Verifies a stale expected revision returns a conflict with the current revision, and a deck read without an
    /// expected revision reports the revision it used (cases C07 and C16).
    /// </summary>
    [Fact]
    public async Task Compare_StaleExpectedRevision_ReturnsConflictWithTheCurrentRevision()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        CardsSourceInput one = new([CardJson.PrintingOf(world.Alpha)]);

        OperationConflict conflict = Assert.IsType<OperationConflict>((await Compare(
            world,
            new DeckSourceInput(deck.DeckId, ExpectedRevision: deck.Revision + 5),
            one,
            "union")).Value);
        CardListsCompareResult current = Success(await Compare(
            world,
            new DeckSourceInput(deck.DeckId, ExpectedRevision: deck.Revision),
            one,
            "union"));

        Assert.Equal("deck-revision-conflict", conflict.ReasonCode);
        Assert.Equal(deck.Revision, conflict.Current!.Revision);
        Assert.Equal(deck.Revision, current.Sources[0].Revision);
    }

    /// <summary>
    /// Verifies pages are stable, the same request gives the same pages and fingerprint, and a changed local deck
    /// between pages returns a conflict with the new revision (cases C08 and C16).
    /// </summary>
    [Fact]
    public async Task Compare_Pages_AreStableAndAChangedDeckBetweenPagesConflicts()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        Guid snapshot = await world.Data.SaveSearchAsync();
        ScryfallSnapshotSourceInput left = new(snapshot);
        DeckSourceInput right = new(deck.DeckId);

        CardListsCompareResult first = Success(await Compare(world, left, right, "union", pageSize: 2));
        CardListsCompareResult again = Success(await Compare(world, left, right, "union", pageSize: 2));
        CardListsCompareResult second = Success(await Compare(
            world,
            left,
            right,
            "union",
            cursor: first.Page.NextCursor,
            pageSize: 2));
        CardListsCompareResult third = Success(await Compare(
            world,
            left,
            right,
            "union",
            cursor: second.Page.NextCursor,
            pageSize: 2));
        _ = Success(await world.Data.Store.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new UpdateDeckMetadataChange("Renamed", string.Empty, "commander")],
            TestContext.Current.CancellationToken));
        OperationResult<CardListsCompareResult> afterChange = await Compare(
            world,
            left,
            right,
            "union",
            cursor: first.Page.NextCursor,
            pageSize: 2);

        Assert.Equal(["Alpha", "Bravo"], CardListWorld.Names(first));
        Assert.Equal(["Charlie", "Delta"], CardListWorld.Names(second));
        Assert.Equal(["Foxtrot"], CardListWorld.Names(third));
        Assert.Null(third.Page.NextCursor);
        Assert.Equal(first.Page.NextCursor, again.Page.NextCursor);
        Assert.Equal(first.Fingerprint, again.Fingerprint);
        Assert.Equal(5, first.Page.TotalCount);
        OperationConflict conflict = Assert.IsType<OperationConflict>(afterChange.Value);
        Assert.Equal(deck.Revision + 1, conflict.Current!.Revision);
    }

    /// <summary>
    /// Verifies a changed Archidekt deck between pages returns a conflict with the new state marker (case C06).
    /// </summary>
    [Fact]
    public async Task Compare_ArchidektDeckChangedBetweenPages_ReturnsConflictWithTheNewMarker()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        world.QueueRemoteDeck(world.Alpha, world.Bravo, world.Charlie);
        world.QueueRemoteDeck(world.Alpha, world.Bravo, world.Charlie, world.Delta);
        world.QueueRemoteDeck(world.Alpha, world.Bravo, world.Charlie, world.Delta);
        ArchidektDeckSourceInput left = new("42");
        CardsSourceInput right = new([CardJson.PrintingOf(world.Echo)]);

        CardListsCompareResult first = Success(await Compare(world, left, right, "union", pageSize: 2));
        OperationResult<CardListsCompareResult> second = await Compare(
            world,
            left,
            right,
            "union",
            cursor: first.Page.NextCursor,
            pageSize: 2);

        OperationConflict conflict = Assert.IsType<OperationConflict>(second.Value);
        Assert.Equal("source-changed-between-pages", conflict.ReasonCode);
        Assert.False(string.IsNullOrEmpty(conflict.Current!.RemoteFingerprint));
        Assert.NotEqual(first.Sources[0].StateMarker, conflict.Current.RemoteFingerprint);
    }

    /// <summary>
    /// Verifies a cursor from a different request is refused with no data, as are a malformed cursor and one that
    /// points past the end (case C12).
    /// </summary>
    [Fact]
    public async Task Compare_CursorFromAnotherRequest_IsInvalidInput()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        Guid snapshot = await world.Data.SaveSearchAsync();
        ScryfallSnapshotSourceInput left = new(snapshot);
        CardsSourceInput right = new([CardJson.PrintingOf(world.Echo)]);
        CardListsCompareResult first = Success(await Compare(world, left, right, "union", pageSize: 1));

        OperationInvalidInput otherOperation = Failure(await Compare(
            world,
            left,
            right,
            "intersection",
            cursor: first.Page.NextCursor,
            pageSize: 1));
        OperationInvalidInput garbage = Failure(await Compare(world, left, right, "union", cursor: "not-a-cursor"));
        OperationInvalidInput edited = Failure(await Compare(
            world,
            left,
            right,
            "union",
            cursor: first.Page.NextCursor![..^2] + "AA"));
        string past = CardListCursors.Encode(first.Fingerprint, 99, [.. first.Sources.Select(source => source.Marker)]);
        OperationInvalidInput beyond = Failure(await Compare(world, left, right, "union", cursor: past));

        Assert.All([otherOperation, garbage, edited, beyond], failure => Assert.Equal("invalid-cursor",
            failure.ReasonCode));
        Assert.All([otherOperation, garbage, edited, beyond], failure => Assert.Equal("cursor", failure.Path));
    }

    /// <summary>
    /// Verifies card data that is not installed gives not-cached and names the sync tool, while a saved search alone
    /// still compares.
    /// </summary>
    [Fact]
    public async Task Compare_WithoutInstalledCardData_NamesTheSyncToolForDecksButNotForSearches()
    {
        using CardListWorld world = await CardListWorld.CreateAsync(install: false);
        DeckDocument deck = await world.CreateDeckAsync();
        Guid snapshot = await world.Data.SaveSearchAsync();

        OperationNotCached notCached = Assert.IsType<OperationNotCached>((await Compare(
            world,
            new ScryfallSnapshotSourceInput(snapshot),
            new DeckSourceInput(deck.DeckId),
            "difference")).Value);
        CardListsCompareResult searchOnly = Success(await Compare(
            world,
            new ScryfallSnapshotSourceInput(snapshot),
            new ScryfallSnapshotSourceInput(snapshot),
            "difference"));

        Assert.Contains("scryfall_card_data_sync", notCached.Message, StringComparison.Ordinal);
        Assert.Empty(searchOnly.Page.Items);
    }

    /// <summary>
    /// Verifies bad arguments fail before any source is read: names, page size, formats, and snapshot problems.
    /// </summary>
    [Fact]
    public async Task Compare_BadArguments_FailWithTheirPath()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        CardsSourceInput one = new([CardJson.PrintingOf(world.Alpha)]);
        ScryfallSnapshotSourceInput missing = new(Guid.NewGuid());
        CompareSourceInput badNested = new(one, one, "merge");
        CompareSourceInput badMatch = new(one, one, "union", "fuzzy");
        Task<OperationResult<CardListsCompareResult>> call(
            string operation,
            string matchBy,
            string format,
            IReadOnlyList<string>? fields,
            int size)
        {
            return world.Coordinator().CompareAsync(
                one, one, operation, matchBy, format, fields, null, size, TestContext.Current.CancellationToken);
        }

        Assert.Equal("operation", Failure(await call("merge", "oracle", "concise", null, 25)).Path);
        Assert.Equal("matchBy", Failure(await call("union", "fuzzy", "concise", null, 25)).Path);
        Assert.Equal("pageSize", Failure(await call("union", "oracle", "concise", null, 0)).Path);
        Assert.Equal("pageSize", Failure(await call("union", "oracle", "full", null, 26)).Path);
        Assert.Equal("responseFormat", Failure(await call("union", "oracle", "verbose", null, 25)).Path);
        Assert.Equal("fields", Failure(await call("union", "oracle", "standard", ["name"], 25)).Path);
        Assert.Equal("left.operation", Failure(await Compare(world, badNested, one, "union")).Path);
        Assert.Equal("left.matchBy", Failure(await Compare(world, badMatch, one, "union")).Path);
        Assert.IsType<OperationNotFound>((await Compare(world, missing, one, "union")).Value);
        Assert.Equal(
            "archidektDeckId cannot be blank.",
            Failure(await Compare(world, new ArchidektDeckSourceInput(" "), one, "union")).Message);
        Assert.Equal(
            "invalid-source",
            Failure(await Compare(world, new ArchidektSnapshotSourceInput("42", ""), one, "union")).ReasonCode);
    }

    /// <summary>
    /// Verifies a saved request that holds no cards is refused with a message that points to scryfall_search.
    /// </summary>
    [Fact]
    public async Task Compare_SnapshotThatIsNotASearch_IsInvalid()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        CardsSourceInput one = new([CardJson.PrintingOf(world.Alpha)]);
        OperationResult<ScryfallSearchResponse> autocomplete = await world.Data.Scryfall.SearchAsync(
            "fixture",
            cancellationToken: TestContext.Current.CancellationToken);
        Guid searchSnapshot = Success(autocomplete).Snapshot.SnapshotId;

        CardListsCompareResult ok = Success(await Compare(world, new ScryfallSnapshotSourceInput(searchSnapshot),
            one, "union"));

        Assert.Equal(["Alpha", "Bravo", "Charlie"], CardListWorld.Names(ok));
        Assert.Equal("full", Success(await world.Coordinator().CompareAsync(
            one,
            one,
            "union",
            "oracle",
            "full",
            null,
            null,
            25,
            TestContext.Current.CancellationToken)).ResponseFormat);
    }

    /// <summary>
    /// Verifies a page of a 100-card deck in the standard format stays under the size limit, sets truncated with a
    /// narrow hint, and continues from the first card left out so no card is skipped or repeated (SIZE-001).
    /// </summary>
    [Fact]
    public async Task Compare_LargeStandardPage_StaysUnderTheSizeLimitAndContinues()
    {
        List<JsonObject> cards =
        [
            .. Enumerable.Range(0, 100).Select(index => CardJson.Make(
                $"Large card {index:D3}",
                oracleText: new string('x', 900),
                colors: ["G"],
                usd: "1.00")),
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
        CardListCoordinator coordinator = data.Coordinator();
        CardsSourceInput nothing = new([Guid.NewGuid()]);
        HashSet<string> seen = [];
        string? cursor = null;
        int pages = 0;

        do
        {
            CardListsCompareResult page = Success(await coordinator.CompareAsync(
                new DeckSourceInput(deck.DeckId),
                nothing,
                "union",
                "oracle",
                "standard",
                null,
                cursor,
                100,
                TestContext.Current.CancellationToken));

            int bytes = JsonSerializer.SerializeToUtf8Bytes(page, ToolCallPolicy.SerializerOptions).Length;
            Assert.True(bytes <= 40_000, $"Page {pages} is {bytes} bytes.");
            foreach (string name in CardListWorld.Names(page))
            {
                Assert.True(seen.Add(name), $"{name} was repeated.");
            }

            if (page.Page.NextCursor is not null)
            {
                Assert.True(page.Truncated);
                Assert.NotNull(page.Narrow);
            }

            cursor = page.Page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(100, seen.Count);
        Assert.True(pages > 1);
    }

    /// <summary>
    /// Verifies the published examples bind to the tool and run (SCHEMA-006).
    /// </summary>
    [Fact]
    public async Task Examples_BindAndRun()
    {
        using CardListWorld world = await CardListWorld.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync();
        Guid snapshot = await world.Data.SaveSearchAsync();
        using ToolCatalogFixture fixture = new();
        string description = CardListExampleSupport.DescriptionOf("card_lists_compare");
        (string Name, string Json)[] examples = CardListExampleSupport.ReadExamples(description);

        Assert.Equal(["minimal", "nested", "full"], examples.Select(example => example.Name));
        foreach ((string name, string json) in examples)
        {
            string filled = CardListExampleSupport.Fill(json, deck, snapshot, CardJson.PrintingOf(world.Echo));
            Dictionary<string, JsonElement> arguments = CardListExampleSupport.Arguments(filled);
            Assert.Null(ToolCallPolicy.DiagnoseArguments(fixture.Catalog, "card_lists_compare", arguments));
            CardListTools tools = new(world.Coordinator());

            OperationResult<CardListsCompareResult> result = await tools.CompareAsync(
                arguments["left"].Deserialize<CardListSourceInput>(ToolCallPolicy.SerializerOptions)!,
                arguments["right"].Deserialize<CardListSourceInput>(ToolCallPolicy.SerializerOptions)!,
                arguments["operation"].GetString()!,
                arguments.TryGetValue("matchBy", out JsonElement match) ? match.GetString()! : "oracle",
                arguments.TryGetValue("responseFormat", out JsonElement format) ? format.GetString()! : "standard",
                arguments.TryGetValue("fields", out JsonElement fields)
                    ? fields.Deserialize<string[]>()
                    : null,
                null,
                arguments.TryGetValue("pageSize", out JsonElement size) ? size.GetInt32() : 25,
                TestContext.Current.CancellationToken);

            Assert.True(result.Value is OperationSuccess<CardListsCompareResult>, $"{name}: " +
                $"{JsonSerializer.Serialize(result.Value)}");
        }
    }

    /// <summary>
    /// Runs one comparison with a concise page.
    /// </summary>
    private static Task<OperationResult<CardListsCompareResult>> Compare(
        CardListWorld world,
        CardListSourceInput left,
        CardListSourceInput right,
        string operation,
        string? cursor = null,
        int pageSize = 25)
    {
        return Compare(world.Coordinator(), left, right, operation, cursor, pageSize);
    }

    /// <summary>
    /// Runs one comparison with a concise page through a given coordinator.
    /// </summary>
    private static Task<OperationResult<CardListsCompareResult>> Compare(
        CardListCoordinator coordinator,
        CardListSourceInput left,
        CardListSourceInput right,
        string operation,
        string? cursor = null,
        int pageSize = 25)
    {
        return coordinator.CompareAsync(
            left,
            right,
            operation,
            "oracle",
            "concise",
            null,
            cursor,
            pageSize,
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
