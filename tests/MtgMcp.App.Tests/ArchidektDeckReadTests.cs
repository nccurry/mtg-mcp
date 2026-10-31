using System.Text.Json;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Checks the deck facts, the zone filter, the response format, and the entry order of archidekt_deck_get and
/// archidekt_snapshots_get against an offline deck shaped like the real public deck 22959335.
/// </summary>
public sealed class ArchidektDeckReadTests
{
    /// <summary>
    /// Holds the most characters a tool text result may have before Claude Code warns (10,000 tokens).
    /// </summary>
    private const int MaximumResultCharacters = 40_000;

    /// <summary>
    /// Lists the fields of one deck entry in 0.13.0, plus inDeck.
    /// </summary>
    private static readonly string[] FullFields =
    [
        "providerRelationId", "providerCardId", "quantity", "cardName", "oracleId", "printingId", "setCode",
        "collectorNumber", "language", "finish", "zone", "categoryNames", "primaryCategoryName", "sortOrder",
        "inDeck",
    ];

    /// <summary>
    /// Lists the fields of one concise deck entry.
    /// </summary>
    private static readonly string[] ConciseFields =
    [
        "cardName", "quantity", "zone", "categoryNames", "setCode", "collectorNumber", "oracleId", "inDeck",
    ];

    /// <summary>
    /// Verifies the commander and main zones of the deck fit one concise page, whose entries hold only the concise
    /// fields, and that the default is concise.
    /// </summary>
    [Fact]
    public async Task ConciseDefault_CommanderAndMain_FitOnePage()
    {
        using ReadWorld world = new(CreateRealisticDeck());
        CancellationToken token = TestContext.Current.CancellationToken;

        OperationResult<ArchidektDeckView> result = await world.Tools.GetDeckAsync(
            "42",
            zones: ["commander", "main"],
            cancellationToken: token);

        ArchidektDeckView page = Success(result);
        Assert.Null(page.NextOffset);
        Assert.Null(page.Truncated);
        Assert.Equal(100, page.ShownEntryCount);
        Assert.Equal(100, page.Entries.Sum(value => value.Quantity));
        Assert.DoesNotContain(page.Entries, value => value is ArchidektFullDeckEntryView);
        Assert.All(page.Entries, value => Assert.True(value.InDeck));
        Assert.True(JsonLength(result) < MaximumResultCharacters);
        Assert.Equal(ConciseFields, FieldNames(result, "entries"));
    }

    /// <summary>
    /// Verifies the full and standard formats hold every 0.13.0 entry field plus inDeck, the concise format holds the
    /// concise fields, and an unknown format is refused.
    /// </summary>
    [Theory]
    [InlineData("full", true)]
    [InlineData("standard", true)]
    [InlineData("concise", false)]
    public async Task ResponseFormat_ChoosesTheEntryFields(string format, bool every)
    {
        using ReadWorld world = new(CreateRealisticDeck());

        OperationResult<ArchidektDeckView> result = await world.Tools.GetDeckAsync(
            "42",
            zones: ["commander"],
            responseFormat: format,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            every ? FullFields.Order(StringComparer.Ordinal) : ConciseFields.Order(StringComparer.Ordinal),
            FieldNames(result, "entries").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies an unknown response format and an unknown zone are refused with the argument that is wrong.
    /// </summary>
    [Fact]
    public async Task UnknownFormatOrZone_IsRefusedWithTheArgument()
    {
        using ReadWorld world = new(CreateRealisticDeck());
        CancellationToken token = TestContext.Current.CancellationToken;

        OperationResult<ArchidektDeckView> format = await world.Tools.GetDeckAsync(
            "42",
            responseFormat: "tiny",
            cancellationToken: token);
        OperationResult<ArchidektDeckView> zone = await world.Tools.GetDeckAsync(
            "42",
            zones: ["main", "graveyard"],
            cancellationToken: token);

        Assert.Equal("responseFormat", Assert.IsType<OperationInvalidInput>(format.Value).Path);
        Assert.Equal("zones[1]", Assert.IsType<OperationInvalidInput>(zone.Value).Path);
    }

    /// <summary>
    /// Verifies the zone filter shows only the chosen zones, offsets count the filtered entries, an offset past them
    /// is refused with the filtered count, and the summary still covers the whole deck.
    /// </summary>
    [Fact]
    public async Task ZonesFilter_ShowsTheChosenZones_AndOffsetsCountTheFilteredList()
    {
        using ReadWorld world = new(CreateRealisticDeck());
        CancellationToken token = TestContext.Current.CancellationToken;

        ArchidektDeckView sideboard = Success(await world.Tools.GetDeckAsync(
            "42",
            zones: ["sideboard"],
            cancellationToken: token));
        ArchidektDeckView atEnd = Success(await world.Tools.GetDeckAsync(
            "42",
            offset: 21,
            zones: ["sideboard"],
            cancellationToken: token));
        OperationResult<ArchidektDeckView> past = await world.Tools.GetDeckAsync(
            "42",
            offset: 22,
            zones: ["sideboard"],
            cancellationToken: token);

        Assert.Equal(21, sideboard.ShownEntryCount);
        Assert.Equal(21, sideboard.Entries.Count);
        Assert.All(sideboard.Entries, value => Assert.Equal("sideboard", value.Zone));
        Assert.Equal(471, sideboard.Summary.EntryCount);
        Assert.Equal(21, atEnd.ShownEntryCount);
        Assert.Empty(atEnd.Entries);
        OperationInvalidInput refused = Assert.IsType<OperationInvalidInput>(past.Value);
        Assert.Equal("invalid-offset", refused.ReasonCode);
        Assert.Equal("offset", refused.Path);
        Assert.Contains("21", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("471", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the zones argument publishes an enum on its items, so an unknown zone is refused by the schema.
    /// </summary>
    [Fact]
    public void ZonesArgument_PublishesAnEnumOnItsItems()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        foreach (string tool in new[] { "archidekt_deck_get", "archidekt_snapshots_get" })
        {
            JsonElement properties = registered.Tools.Single(value => value.ProtocolTool.Name == tool)
                .ProtocolTool.InputSchema.GetProperty("properties");

            Assert.Equal(
                ["commander", "main", "sideboard", "maybeboard"],
                properties.GetProperty("zones").GetProperty("items").GetProperty("enum").EnumerateArray()
                    .Select(value => value.GetString()));
            Assert.Equal("concise", properties.GetProperty("responseFormat").GetProperty("default").GetString());
        }
    }

    /// <summary>
    /// Verifies the description opens with the way to the full format and the zones, and the cut page says the same.
    /// </summary>
    [Fact]
    public async Task DescriptionAndNarrow_NameResponseFormatFullAndZones()
    {
        using RegisteredToolSet registered = new(OperationMode.Remote);
        string description = registered.Tools.Single(value => value.ProtocolTool.Name == "archidekt_deck_get")
            .ProtocolTool.Description!;
        using ReadWorld world = new(CreateLargeDeck(300));

        ArchidektDeckView page = Success(await world.Tools.GetDeckAsync(
            "42",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("responseFormat \"full\"", description[..400], StringComparison.Ordinal);
        Assert.Contains("zones", description[..400], StringComparison.Ordinal);
        Assert.True(page.Truncated);
        Assert.Contains("responseFormat \"full\"", page.Narrow, StringComparison.Ordinal);
        Assert.Contains("zones", page.Narrow, StringComparison.Ordinal);
        Assert.Contains("offset", page.Narrow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies entries come in zone order, then name, then relation ID, that two reads (which the fake shuffles)
    /// give the same order, and that paging reaches every entry exactly once.
    /// </summary>
    [Fact]
    public async Task Entries_AreOrderedByZoneNameAndRelation_AndEveryEntryIsReachedOnce()
    {
        using ReadWorld world = new(CreateRealisticDeck());
        CancellationToken token = TestContext.Current.CancellationToken;

        List<ArchidektFullDeckEntryView> first = await ReadEveryEntryAsync(world, token);
        List<ArchidektFullDeckEntryView> second = await ReadEveryEntryAsync(world, token);

        Assert.Equal(471, first.Count);
        Assert.Equal(471, first.Select(value => value.ProviderRelationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            first.Select(value => value.ProviderRelationId),
            second.Select(value => value.ProviderRelationId));
        string[] zoneOrder = ["commander", "main", "sideboard", "maybeboard"];
        Assert.Equal(
            first.OrderBy(value => Array.IndexOf(zoneOrder, value.Zone))
                .ThenBy(value => value.CardName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.ProviderRelationId, StringComparer.Ordinal)
                .Select(value => value.ProviderRelationId),
            first.Select(value => value.ProviderRelationId));
        Assert.Equal(
            [2, 98, 21, 350],
            zoneOrder.Select(zone => first.Count(value => value.Zone == zone)));
    }

    /// <summary>
    /// Verifies the summary names the one real duplicate (two in-deck entries of one card), skips basic lands and
    /// the copy that is not in the deck, gives the Commander size fact, and reads the same whatever zones were asked.
    /// </summary>
    [Fact]
    public async Task Summary_HasTheDuplicatesAndTheSizeFact_ForTheWholeDeck()
    {
        using ReadWorld world = new(CreateRealisticDeck());
        CancellationToken token = TestContext.Current.CancellationToken;

        ArchidektDeckSummary all = Success(await world.Tools.GetDeckAsync("42", cancellationToken: token)).Summary;
        ArchidektDeckSummary filtered = Success(await world.Tools.GetDeckAsync(
            "42",
            zones: ["maybeboard"],
            cancellationToken: token)).Summary;

        ArchidektDuplicateGroup group = Assert.Single(all.Duplicates);
        Assert.Equal("Dupe Card", group.CardName);
        Assert.Equal(2, group.Copies);
        Assert.Equal(["main"], group.Zones);
        Assert.Null(all.DuplicatesOmitted);
        Assert.Equal(new ArchidektDeckSizeFact("commander", 100, 100), all.Size);
        Assert.Equal(group.CardName, Assert.Single(filtered.Duplicates).CardName);
        Assert.Equal(all.Size, filtered.Size);
    }

    /// <summary>
    /// Verifies a card held in the commander zone and the main zone is one group, and that the group list is cut
    /// with a count of the groups left out.
    /// </summary>
    [Fact]
    public async Task Duplicates_AcrossCommanderAndMain_AreGrouped_AndTheListIsCapped()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Commander");
        fake.AddCategory("Removal");
        FakeArchidekt.FakeCard shared = fake.AddCardToCatalog(1, "Shared Card");
        fake.AddEntry(shared, 1, "Commander");
        fake.AddEntry(shared, 1, "Removal");
        for (int index = 0; index < ArchidektDeckFacts.MaximumDuplicateGroups + 4; index++)
        {
            FakeArchidekt.FakeCard card = fake.AddCardToCatalog(100 + index, $"Twin {index:000}");
            fake.AddEntry(card, 1, "Removal");
            fake.AddEntry(card, 1, "Removal");
        }

        using ReadWorld world = new(fake);

        ArchidektDeckSummary summary = Success(await world.Tools.GetDeckAsync(
            "42",
            cancellationToken: TestContext.Current.CancellationToken)).Summary;

        Assert.Equal(ArchidektDeckFacts.MaximumDuplicateGroups, summary.Duplicates.Count);
        Assert.Equal(5, summary.DuplicatesOmitted);
        Assert.Equal(["commander", "main"], summary.Duplicates.Single(value => value.CardName == "Shared Card").Zones);
    }

    /// <summary>
    /// Verifies 17 cards that each sit four times in the sideboard are 17 repeat groups of four copies, separate from
    /// the commander and main duplicates; basic lands, a card once in each of two zones, and a sideboard card that
    /// appears once are not repeats; and the list is cut with a count of the groups left out.
    /// </summary>
    [Fact]
    public async Task Repeats_InTheSideboard_AreListedApartFromTheDuplicates_AndTheListIsCapped()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddCategory("Sideboard", includedInDeck: false);
        fake.AddCategory("Maybeboard", includedInDeck: false);
        for (int index = 0; index < 17; index++)
        {
            FakeArchidekt.FakeCard card = fake.AddCardToCatalog(100 + index, $"Repeat {index:000}");
            for (int copy = 0; copy < 4; copy++)
            {
                fake.AddEntry(card, 1, "Sideboard");
            }
        }

        FakeArchidekt.FakeCard single = fake.AddCardToCatalog(300, "Single Side");
        fake.AddEntry(single, 1, "Sideboard");
        FakeArchidekt.FakeCard split = fake.AddCardToCatalog(301, "Split Zones");
        fake.AddEntry(split, 1, "Sideboard");
        fake.AddEntry(split, 1, "Maybeboard");
        FakeArchidekt.FakeCard swamp = fake.AddCardToCatalog(302, "Swamp");
        fake.AddEntry(swamp, 1, "Sideboard");
        fake.AddEntry(swamp, 1, "Sideboard");
        using ReadWorld world = new(fake);

        ArchidektDeckSummary summary = Success(await world.Tools.GetDeckAsync(
            "42",
            cancellationToken: TestContext.Current.CancellationToken)).Summary;

        Assert.Empty(summary.Duplicates);
        Assert.Equal(17, summary.RepeatsOutsideMain.Count);
        Assert.All(summary.RepeatsOutsideMain, value => Assert.Equal(4, value.Copies));
        Assert.All(summary.RepeatsOutsideMain, value => Assert.Equal(["sideboard"], value.Zones));
        Assert.Null(summary.RepeatsOutsideMainOmitted);

        FakeArchidekt many = new();
        many.AddCategory("Sideboard", includedInDeck: false);
        for (int index = 0; index < ArchidektDeckFacts.MaximumDuplicateGroups + 3; index++)
        {
            FakeArchidekt.FakeCard card = many.AddCardToCatalog(100 + index, $"Twin {index:000}");
            many.AddEntry(card, 1, "Sideboard");
            many.AddEntry(card, 1, "Sideboard");
        }

        using ReadWorld manyWorld = new(many);
        ArchidektDeckSummary capped = Success(await manyWorld.Tools.GetDeckAsync(
            "42",
            cancellationToken: TestContext.Current.CancellationToken)).Summary;
        Assert.Equal(ArchidektDeckFacts.MaximumDuplicateGroups, capped.RepeatsOutsideMain.Count);
        Assert.Equal(3, capped.RepeatsOutsideMainOmitted);
    }

    /// <summary>
    /// Verifies cards without an Oracle ID group by name, ignoring case, and that no verdict or size fact appears
    /// for a deck that is not Commander.
    /// </summary>
    [Fact]
    public void Duplicates_WithoutOracleId_FallBackToTheName_AndOnlyCommanderGetsTheSizeFact()
    {
        string json = """
            {
              "id": 1, "name": "Named", "deckFormat": 2, "categories": [],
              "cards": [
                { "deckRelationId": 1, "quantity": 1, "order": 0, "categories": [],
                  "card": { "id": 1, "uid": "a", "oracleCard": { "name": "Same Name" } } },
                { "deckRelationId": 2, "quantity": 1, "order": 1, "categories": [],
                  "card": { "id": 2, "uid": "b", "oracleCard": { "name": "same name " } } },
                { "deckRelationId": 3, "quantity": 4, "order": 2, "categories": [],
                  "card": { "id": 3, "uid": "c", "oracleCard": { "name": "Snow-Covered Forest" } } }
              ]
            }
            """;
        using JsonDocument document = JsonDocument.Parse(json);
        RemoteDeckSnapshot deck = ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            json,
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            "GET");

        ArchidektDeckSummary summary = ArchidektDeckFacts.Summarize(deck);

        ArchidektDuplicateGroup group = Assert.Single(summary.Duplicates);
        Assert.Equal(2, group.Copies);
        Assert.Null(group.OracleId);
        Assert.Null(summary.Size);
    }

    /// <summary>
    /// Verifies the snapshot read takes the same zones and response format with the same defaults, and that its
    /// saved deck pages like a deck read.
    /// </summary>
    [Fact]
    public async Task SnapshotGet_TakesTheSameZonesAndResponseFormat()
    {
        using ReadWorld world = new(CreateRealisticDeck());
        CancellationToken token = TestContext.Current.CancellationToken;

        OperationResult<ArchidektSnapshotsResult> byDefault = await world.Tools.GetSnapshotsAsync(
            "get",
            "42",
            "77",
            zones: ["commander", "main"],
            cancellationToken: token);
        OperationResult<ArchidektSnapshotsResult> full = await world.Tools.GetSnapshotsAsync(
            "get",
            "42",
            "77",
            zones: ["sideboard"],
            responseFormat: "full",
            cancellationToken: token);
        OperationResult<ArchidektSnapshotsResult> bad = await world.Tools.GetSnapshotsAsync(
            "get",
            "42",
            "77",
            zones: ["attic"],
            cancellationToken: token);

        ArchidektDeckView concise = Assert.IsType<ArchidektSnapshotGetResult>(Success(byDefault)).Snapshot.Deck;
        ArchidektDeckView everything = Assert.IsType<ArchidektSnapshotGetResult>(Success(full)).Snapshot.Deck;
        Assert.Equal(100, concise.ShownEntryCount);
        Assert.DoesNotContain(concise.Entries, value => value is ArchidektFullDeckEntryView);
        Assert.Equal(21, everything.ShownEntryCount);
        Assert.All(everything.Entries, value => Assert.IsType<ArchidektFullDeckEntryView>(value));
        Assert.Equal("zones[0]", Assert.IsType<OperationInvalidInput>(bad.Value).Path);
    }

    /// <summary>
    /// Creates a deck shaped like the real public deck 22959335: 2 commander entries, 98 in-deck main entries (one
    /// real duplicate, Dupe Card, two Forest entries, and a Bloodthirsty Blade whose second copy sits in a category
    /// that is not in the deck), 21 sideboard entries, and 347 maybeboard entries, plus the 3 entries whose first
    /// category is not in the deck, which Archidekt counts as maybeboard.
    /// </summary>
    private static FakeArchidekt CreateRealisticDeck()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Commander");
        fake.AddCategory("Removal");
        fake.AddCategory("Burn");
        fake.AddCategory("Sideboard", includedInDeck: false);
        fake.AddCategory("Maybeboard", includedInDeck: false);
        fake.AddCategory("Multiplayer", includedInDeck: false);
        fake.AddCategory("Reanimate", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(3001, "Test Commander"), 1, "Commander");
        fake.AddEntry(fake.AddCardToCatalog(3005, "Test Partner"), 1, "Commander");
        FakeArchidekt.FakeCard blade = fake.AddCardToCatalog(3002, "Bloodthirsty Blade");
        fake.AddEntry(blade, 1, "Multiplayer");
        fake.AddEntry(blade, 1, "Removal");
        fake.AddEntry(fake.AddCardToCatalog(3003, "Revival // Revenge"), 1, "Reanimate", "Burn");
        fake.AddEntry(fake.AddCardToCatalog(3004, "Osteomancer Adept"), 1, "Reanimate");
        FakeArchidekt.FakeCard dupe = fake.AddCardToCatalog(3006, "Dupe Card");
        fake.AddEntry(dupe, 1, "Removal");
        fake.AddEntry(dupe, 1, "Burn");
        FakeArchidekt.FakeCard forest = fake.AddCardToCatalog(3007, "Forest");
        fake.AddEntry(forest, 1, "Removal");
        fake.AddEntry(forest, 1, "Removal");
        for (int index = 0; index < 98 - 1 - 2 - 2; index++)
        {
            fake.AddEntry(fake.AddCardToCatalog(3100 + index, $"Filler {index:000}"), 1, "Removal");
        }

        for (int index = 0; index < 21; index++)
        {
            fake.AddEntry(fake.AddCardToCatalog(4000 + index, $"Side {index:000}"), 1, "Sideboard");
        }

        for (int index = 0; index < 347; index++)
        {
            fake.AddEntry(fake.AddCardToCatalog(5000 + index, $"Maybe {index:000}"), 1, "Maybeboard");
        }

        return fake;
    }

    /// <summary>
    /// Creates a deck with many entries in the main zone.
    /// </summary>
    private static FakeArchidekt CreateLargeDeck(int count)
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        for (int index = 0; index < count; index++)
        {
            fake.AddEntry(fake.AddCardToCatalog(1000 + index, $"Card Number {index:000}"), 1, "Removal");
        }

        return fake;
    }

    /// <summary>
    /// Reads every page of the deck in the full format and returns its entries.
    /// </summary>
    private static async Task<List<ArchidektFullDeckEntryView>> ReadEveryEntryAsync(
        ReadWorld world,
        CancellationToken token)
    {
        List<ArchidektFullDeckEntryView> entries = [];
        int offset = 0;
        while (true)
        {
            ArchidektDeckView page = Success(await world.Tools.GetDeckAsync(
                "42",
                offset,
                responseFormat: "full",
                cancellationToken: token));
            entries.AddRange(page.Entries.Cast<ArchidektFullDeckEntryView>());
            if (page.NextOffset is not int next)
            {
                return entries;
            }

            offset = next;
        }
    }

    /// <summary>
    /// Lists the property names of the first element of the named array of the serialized deck, as a client sees it.
    /// </summary>
    private static List<string> FieldNames(OperationResult<ArchidektDeckView> result, string array)
    {
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(result, ToolCallPolicy.SerializerOptions));
        JsonElement root = document.RootElement.TryGetProperty("data", out JsonElement data)
            ? data
            : document.RootElement;
        return [.. root.GetProperty(array)[0].EnumerateObject().Select(value => value.Name)];
    }

    /// <summary>
    /// Measures a result the way the server writes it as a tool text result.
    /// </summary>
    private static int JsonLength<T>(OperationResult<T> result)
    {
        return JsonSerializer.Serialize(result, ToolCallPolicy.SerializerOptions).Length;
    }

    /// <summary>
    /// Extracts one successful union case.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Holds the service, store, and read tools around one fake Archidekt.
    /// </summary>
    private sealed class ReadWorld : IDisposable
    {
        /// <summary>
        /// Holds the provider service.
        /// </summary>
        private readonly ArchidektService service;

        /// <summary>
        /// Holds the temporary data folder.
        /// </summary>
        private readonly TemporaryDirectory directory = new();

        /// <summary>
        /// Holds the local deck store.
        /// </summary>
        private readonly SqliteDeckStore store;

        /// <summary>
        /// Creates the world around one fake Archidekt.
        /// </summary>
        internal ReadWorld(FakeArchidekt fake)
        {
            ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                MinimumRequestInterval = TimeSpan.Zero,
                MaximumRequestsPerWindow = 100_000,
                MaximumRequestsPerOperation = 2_000,
            };
            service = new ArchidektService(
                new ArchidektSession(
                    new HttpClient(fake) { BaseAddress = options.BaseAddress },
                    ownsHttpClient: true,
                    options),
                options.MaximumRequestsPerOperation);
            store = new SqliteDeckStore(directory.Path, "test");
            Tools = new ArchidektReadTools(new ArchidektCoordinator(service, store), OperationMode.Remote);
        }

        /// <summary>
        /// Gets the read tools.
        /// </summary>
        internal ArchidektReadTools Tools { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
            store.Dispose();
            directory.Dispose();
        }
    }
}
