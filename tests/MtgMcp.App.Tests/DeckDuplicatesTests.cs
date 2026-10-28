using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Archidekt.Tests;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>local_deck_duplicates_preview</c>: which copy is kept, ties, exemptions, match modes, and the edits it
/// plans (DEDUP-001 to DEDUP-003, cases D01 to D08).
/// </summary>
public sealed class DeckDuplicatesTests
{
    /// <summary>
    /// Holds the card objects the tests share.
    /// </summary>
    private static readonly JsonObject Swords = CardJson.Make("Swords to Plowshares", "Instant", "{W}", 1m, ["W"]);

    /// <summary>
    /// Holds a first printing of Sol Ring.
    /// </summary>
    private static readonly JsonObject SolRingOne = CardJson.Make("Sol Ring", "Artifact", "{1}", 1m);

    /// <summary>
    /// Holds a second printing of Sol Ring, with the same Oracle ID.
    /// </summary>
    private static readonly JsonObject SolRingTwo = CardJson.Make(
        "Sol Ring",
        "Artifact",
        "{1}",
        1m,
        oracleId: CardJson.OracleOf(SolRingOne));

    /// <summary>
    /// Holds a basic land.
    /// </summary>
    private static readonly JsonObject Forest = CardJson.Make("Forest", "Basic Land — Forest", null, 0m);

    /// <summary>
    /// Holds a card that a deck can have any number of.
    /// </summary>
    private static readonly JsonObject Rats = CardJson.Make(
        "Relentless Rats",
        oracleText: "A deck can have any number of cards named Relentless Rats.");

    /// <summary>
    /// Holds a card that a deck can have up to seven of.
    /// </summary>
    private static readonly JsonObject Dwarves = CardJson.Make(
        "Seven Dwarves",
        oracleText: "A deck can have up to seven cards named Seven Dwarves.");

    /// <summary>
    /// Verifies the best-ranked entry is kept and the other is removed (case D01).
    /// </summary>
    [Fact]
    public async Task Preview_CopyInRemovalAndMaybeboard_KeepsRemovalAndRemovesMaybeboard()
    {
        using World world = await World.CreateAsync();
        Guid keep = Guid.NewGuid();
        Guid drop = Guid.NewGuid();
        DeckDocument deck = await world.CreateDeckAsync(
            [Draft(Swords, keep), Draft(Swords, drop)],
            [(keep, "Removal"), (drop, "Maybeboard")]);

        DeckDuplicatesPreview preview = Success(await world.Preview(deck, ["Removal", "Maybeboard"]));

        DuplicateGroup group = Assert.Single(preview.Duplicates);
        Assert.Equal("Swords to Plowshares", group.Card.Name);
        Assert.Equal([true, false], group.Entries.Select(entry => entry.Kept));
        Assert.Equal([0, 1], group.Entries.Select(entry => entry.Rank));
        RemoveDeckEntryInput removal = Assert.IsType<RemoveDeckEntryInput>(Assert.Single(preview.Changes));
        Assert.Equal(drop, removal.EntryId);
        Assert.Equal(deck.Revision, preview.Deck.Revision);
        Assert.Contains($"expectedRevision {deck.Revision}", preview.Next, StringComparison.Ordinal);
        Assert.Empty(preview.Ambiguous);
    }

    /// <summary>
    /// Verifies two printings of one card are duplicates under oracle and name matching but not under printing
    /// (case D02).
    /// </summary>
    [Theory]
    [InlineData("oracle", 1)]
    [InlineData("name", 1)]
    [InlineData("printing", 0)]
    public async Task Preview_TwoPrintingsOfOneCard_AreDuplicatesUnlessMatchingByPrinting(string matchBy,
        int duplicates)
    {
        using World world = await World.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync([Draft(SolRingOne), Draft(SolRingTwo, zone: "sideboard")]);

        DeckDuplicatesPreview preview = Success(await world.Preview(deck, ["main"], matchBy: matchBy));

        Assert.Equal(duplicates, preview.Duplicates.Count);
        Assert.Equal(duplicates, preview.Changes.Count);
    }

    /// <summary>
    /// Verifies a tie for the best rank lists the card as ambiguous and plans no change (case D03).
    /// </summary>
    [Fact]
    public async Task Preview_TwoCopiesInTheSameGroup_AreAmbiguousWithNoChange()
    {
        using World world = await World.CreateAsync();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        DeckDocument deck = await world.CreateDeckAsync(
            [Draft(Swords, first), Draft(Swords, second)],
            [(first, "Ramp"), (second, "Ramp")]);

        DeckDuplicatesPreview preview = Success(await world.Preview(deck, ["Ramp"]));

        Assert.Empty(preview.Duplicates);
        Assert.Empty(preview.Changes);
        DuplicateGroup ambiguous = Assert.Single(preview.Ambiguous);
        Assert.Equal(2, ambiguous.Entries.Count);
        Assert.Contains("keepPriority", preview.Next, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a copy in a listed group is kept over a copy in an unlisted group, and group names ignore letter case
    /// (case D04).
    /// </summary>
    [Fact]
    public async Task Preview_CopyInAListedGroup_IsKeptOverAnUnlistedOne()
    {
        using World world = await World.CreateAsync();
        Guid listed = Guid.NewGuid();
        Guid unlisted = Guid.NewGuid();
        DeckDocument deck = await world.CreateDeckAsync(
            [Draft(Swords, unlisted, zone: "sideboard"), Draft(Swords, listed, zone: "MAIN")]);

        DeckDuplicatesPreview preview = Success(await world.Preview(deck, ["main"]));

        Assert.Equal(unlisted, Assert.IsType<RemoveDeckEntryInput>(Assert.Single(preview.Changes)).EntryId);
        Assert.Equal(listed, Assert.Single(preview.Duplicates).Entries.Single(entry => entry.Kept).EntryId);
    }

    /// <summary>
    /// Verifies one entry with a quantity of 3 and no other copies is cut to 1 with an update (case D05), and an
    /// update applied with the returned revision leaves one copy.
    /// </summary>
    [Fact]
    public async Task Preview_OneEntryWithQuantityThree_UpdatesItToOne()
    {
        using World world = await World.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync([Draft(Swords, quantity: 3)]);

        DeckDuplicatesPreview preview = Success(await world.Preview(deck, ["main"]));

        UpdateDeckEntryInput update = Assert.IsType<UpdateDeckEntryInput>(Assert.Single(preview.Changes));
        Assert.Equal(1, update.Entry.Quantity);
        Assert.Equal(deck.Entries[0].EntryId, update.Entry.EntryId);
        DeckBatchWriteTools writes = new(world.Data.Store, OperationMode.Local);
        DeckDocument applied = Success(await writes.ApplyChangesAsync(
            deck.DeckId,
            preview.Deck.Revision,
            preview.Changes,
            TestContext.Current.CancellationToken));
        Assert.Equal(1, Assert.Single(applied.Entries).Quantity);
    }

    /// <summary>
    /// Verifies basic lands and any-number cards are skipped with the exemption and reported without it (case D06).
    /// </summary>
    [Fact]
    public async Task Preview_BasicLandsAndAnyNumberCards_AreSkippedOnlyWithTheExemption()
    {
        using World world = await World.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync([Draft(Forest, quantity: 30), Draft(Rats), Draft(Rats)]);

        DeckDuplicatesPreview exempted = Success(await world.Preview(deck, ["main"],
            exempt: "commander-singleton-exceptions"));
        DeckDuplicatesPreview reported = Success(await world.Preview(deck, ["main"], exempt: "none"));

        Assert.Empty(exempted.Duplicates);
        Assert.Equal(["Forest"], reported.Duplicates.Select(group => group.Card.Name).Where(name => name == "Forest"));
        Assert.Equal("Relentless Rats", Assert.Single(reported.Ambiguous).Card.Name);
    }

    /// <summary>
    /// Verifies Seven Dwarves in eight entries keeps seven copies and removes one with the exemption (case D08), and
    /// matching by oracle without installed card data is not-cached naming the sync tool.
    /// </summary>
    [Fact]
    public async Task Preview_SevenDwarvesInEightEntries_KeepsSevenAndNeedsCardDataForOracle()
    {
        using World world = await World.CreateAsync();
        using World noData = await World.CreateAsync(install: false);
        List<DeckEntryDraft> drafts = [.. Enumerable.Range(0, 7).Select(_ => Draft(Dwarves)), Draft(Dwarves,
            zone: "maybe")];
        DeckDocument deck = await world.CreateDeckAsync(drafts);
        DeckDocument bare = await noData.CreateDeckAsync(drafts);

        DeckDuplicatesPreview preview = Success(await world.Preview(deck, ["main"],
            exempt: "commander-singleton-exceptions"));
        OperationNotCached notCached = Assert.IsType<OperationNotCached>((await noData.Preview(bare, ["main"])).Value);
        DeckDuplicatesPreview byName = Success(await noData.Preview(
            bare,
            ["main"],
            matchBy: "name",
            exempt: "commander-singleton-exceptions"));

        Assert.Equal(7, Assert.Single(preview.Duplicates).Limit);
        Assert.Equal(deck.Entries[7].EntryId,
            Assert.IsType<RemoveDeckEntryInput>(Assert.Single(preview.Changes)).EntryId);
        Assert.Contains("scryfall_card_data_sync", notCached.Message, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Single(byName.Ambiguous).Limit);
        Assert.Contains("only basic land names", Assert.Single(byName.Limits!), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies entries with no identity for the match mode are listed as unmatched and left alone.
    /// </summary>
    [Fact]
    public async Task Preview_EntriesWithoutTheIdentityTheModeNeeds_AreUnmatched()
    {
        using World world = await World.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync(
        [
            Draft(Swords),
            new DeckEntryDraft(1, "Proxy", Zone: "main"),
            new DeckEntryDraft(1, "Missing", PrintingId: Guid.NewGuid(), Zone: "main"),
        ]);

        DeckDuplicatesPreview oracle = Success(await world.Preview(deck, ["main"]));
        DeckDuplicatesPreview printing = Success(await world.Preview(deck, ["main"], matchBy: "printing"));

        Assert.Equal(
            [("no-scryfall-identity", "Proxy"), ("not-in-card-data", "Missing")],
            oracle.Unmatched.Select(entry => (entry.Reason, entry.Name)).Order());
        Assert.Equal("no-printing-identity", printing.Unmatched.First(entry => entry.Name == "Proxy").Reason);
        Assert.Empty(oracle.Duplicates);
    }

    /// <summary>
    /// Verifies bad arguments and a stale revision are refused with their paths.
    /// </summary>
    [Fact]
    public async Task Preview_BadArgumentsAndStaleRevision_AreRefused()
    {
        using World world = await World.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync([Draft(Swords)]);

        OperationInvalidInput matchBy = Failure(await world.Preview(deck, ["main"], matchBy: "fuzzy"));
        OperationInvalidInput exempt = Failure(await world.Preview(deck, ["main"], exempt: "some"));
        OperationInvalidInput empty = Failure(await world.Coordinator.PreviewAsync(
            deck.DeckId, null, "oracle", "none", [], TestContext.Current.CancellationToken));
        OperationInvalidInput both = Failure(await world.Coordinator.PreviewAsync(
            deck.DeckId,
            null,
            "oracle",
            "none",
            [new DuplicateKeepGroupInput("main", "Ramp")],
            TestContext.Current.CancellationToken));
        OperationConflict stale = Assert.IsType<OperationConflict>((await world.Coordinator.PreviewAsync(
            deck.DeckId,
            deck.Revision + 3,
            "oracle",
            "none",
            [new DuplicateKeepGroupInput("main")],
            TestContext.Current.CancellationToken)).Value);
        OperationResult<DeckDuplicatesPreview> missing = await world.Coordinator.PreviewAsync(
            Guid.NewGuid(), null, "oracle", "none", [new DuplicateKeepGroupInput("main")],
                TestContext.Current.CancellationToken);

        Assert.Equal("matchBy", matchBy.Path);
        Assert.Equal("exempt", exempt.Path);
        Assert.Equal("keepPriority", empty.Path);
        Assert.Equal("keepPriority[0]", both.Path);
        Assert.Equal(deck.Revision, stale.Current!.Revision);
        Assert.IsType<OperationNotFound>(missing.Value);
    }

    /// <summary>
    /// Verifies a deck with many duplicates gives a result under the size limit that lists the first duplicates and
    /// says to apply them and ask again (SIZE-001).
    /// </summary>
    [Fact]
    public async Task Preview_ManyDuplicates_AreCutToTheSizeLimitWithAHint()
    {
        List<JsonObject> cards =
        [
            .. Enumerable.Range(0, 300).Select(index => CardJson.Make($"Duplicated card number {index:D3}")),
        ];
        using World world = await World.CreateAsync(cards: cards);
        List<DeckEntryDraft> drafts =
        [
            .. cards.SelectMany(card => new[] { Draft(card), Draft(card, zone: "sideboard") }),
        ];
        DeckDocument deck = await world.CreateDeckAsync(drafts);

        DeckDuplicatesPreview preview = Success(await world.Preview(deck, ["main"]));

        int bytes = JsonSerializer.SerializeToUtf8Bytes(preview, ToolCallPolicy.SerializerOptions).Length;
        Assert.True(bytes <= 40_000, $"The result is {bytes} bytes.");
        Assert.True(preview.Truncated);
        Assert.Contains("run the preview again", preview.Narrow, StringComparison.Ordinal);
        Assert.Equal(preview.Duplicates.Count, preview.Changes.Count);
        Assert.True(preview.Duplicates.Count < 300);
    }

    /// <summary>
    /// Verifies the published example binds to the tool and runs (SCHEMA-006 style check for the preview).
    /// </summary>
    [Fact]
    public async Task Example_BindsAndRuns()
    {
        using World world = await World.CreateAsync();
        DeckDocument deck = await world.CreateDeckAsync([Draft(Swords), Draft(Swords, zone: "sideboard")]);
        using ToolCatalogFixture fixture = new();
        string description = CardListExampleSupport.DescriptionOf("local_deck_duplicates_preview");
        (_, string json) = Assert.Single(CardListExampleSupport.ReadExamples(description));
        Dictionary<string, JsonElement> arguments = CardListExampleSupport.Arguments(
            json.Replace(DeckToolExamples.PlaceholderDeckId, deck.DeckId.ToString("D"), StringComparison.Ordinal));

        Assert.Null(ToolCallPolicy.DiagnoseArguments(fixture.Catalog, "local_deck_duplicates_preview", arguments));
        DeckDuplicatesReadTools tools = new(world.Coordinator);
        DeckDuplicatesPreview preview = Success(await tools.PreviewAsync(
            deck.DeckId,
            arguments["exempt"].GetString()!,
            arguments["keepPriority"]
                .Deserialize<IReadOnlyList<DuplicateKeepGroupInput>>(ToolCallPolicy.SerializerOptions)!,
            null,
            arguments["matchBy"].GetString()!,
            TestContext.Current.CancellationToken));

        Assert.Single(preview.Duplicates);
    }

    /// <summary>
    /// Verifies the plan applied with local_deck_apply_changes leaves a push preview that holds only the planned
    /// removal, with no category order updates (case D07 and ARCH-001).
    /// </summary>
    [Fact]
    public async Task Preview_ThenApplyThenPushPreview_PlansOnlyTheRemoval()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-dedupe-{Guid.NewGuid():N}");
        try
        {
            using ArchidektTestHttpHandler handler = new();
            handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"t\"}");
            for (int index = 0; index < 6; index++)
            {
                handler.Add(HttpMethod.Get, "api/decks/42/", RemoteDeckWithDuplicate());
            }

            ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                MinimumRequestInterval = TimeSpan.Zero,
                MaximumRequestsPerWindow = 1_000,
            };
            using ArchidektService service = new(
                new ArchidektSession(new HttpClient(handler) { BaseAddress = options.BaseAddress }, true, options),
                options.MaximumRequestsPerOperation);
            using MtgMcp.Decks.SqliteDeckStore store = new(root, "test");
            using MtgMcp.Scryfall.ScryfallService scryfall = new(root, true, "test", ToolCallPolicy.SerializerOptions);
            MtgMcp.App.Archidekt.ArchidektCoordinator archidekt = new(service, store);
            ArchidektSyncPreview pull = Success(await archidekt.PreviewPullAsync("42", null,
                TestContext.Current.CancellationToken));
            Guid localId = Success(await archidekt.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, pull.RemoteFingerprint, pull.PreviewFingerprint),
                TestContext.Current.CancellationToken)).LocalDeckId!.Value;
            DeckDocument local = Success(await store.GetAsync(localId, TestContext.Current.CancellationToken));
            DeckDuplicatesCoordinator coordinator = new(store, scryfall);

            DeckDuplicatesPreview preview = Success(await coordinator.PreviewAsync(
                localId,
                local.Revision,
                "name",
                "none",
                [new DuplicateKeepGroupInput(Category: "Removal"), new DuplicateKeepGroupInput(Category: "Maybeboard")],
                TestContext.Current.CancellationToken));
            _ = Success(await new DeckBatchWriteTools(store, OperationMode.Local).ApplyChangesAsync(
                localId,
                preview.Deck.Revision,
                preview.Changes,
                TestContext.Current.CancellationToken));
            ArchidektSyncPreview push = Success(await archidekt.PreviewPushAsync(localId,
                TestContext.Current.CancellationToken));

            Assert.Single(preview.Changes);
            Assert.All(push.Operations, operation => Assert.Contains("remove", operation.Kind,
                StringComparison.Ordinal));
            Assert.Single(push.Operations);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Builds an Archidekt deck that holds one card twice: once in Removal with a second category, once in Maybeboard.
    /// </summary>
    private static string RemoteDeckWithDuplicate()
    {
        return """
            {
              "id": 42, "name": "Dupes", "description": "", "deckFormat": 3, "private": true,
              "categories": [
                { "id": 10, "name": "Removal", "includedInDeck": true, "includedInPrice": true, "sortOrder": 0 },
                { "id": 11, "name": "Sideboard", "includedInDeck": false, "includedInPrice": true, "sortOrder": 1 },
                { "id": 12, "name": "Maybeboard", "includedInDeck": false, "includedInPrice": false, "sortOrder": 2 }
              ],
              "cards": [
                { "deckRelationId": 101, "quantity": 1, "categories": ["Sideboard", "Removal"],
                  "card": { "id": 501, "uid": "33333333-3333-3333-3333-333333333333",
                            "edition": { "editioncode": "dmu" }, "collectorNumber": "278",
                            "oracleCard": { "uid": "44444444-4444-4444-4444-444444444444",
                                            "name": "Swords to Plowshares" } } },
                { "deckRelationId": 102, "quantity": 1, "categories": ["Maybeboard"],
                  "card": { "id": 502, "uid": "55555555-5555-5555-5555-555555555555",
                            "edition": { "editioncode": "dmu" }, "collectorNumber": "279",
                            "oracleCard": { "uid": "44444444-4444-4444-4444-444444444444",
                                            "name": "Swords to Plowshares" } } }
              ]
            }
            """;
    }

    /// <summary>
    /// Builds a draft entry for a card object, in the main zone unless another is given.
    /// </summary>
    private static DeckEntryDraft Draft(JsonObject card, Guid? entryId = null, int quantity = 1, string zone = "main")
    {
        return new DeckEntryDraft(
            quantity,
            card["name"]!.GetValue<string>(),
            PrintingId: CardJson.PrintingOf(card),
            Zone: zone,
            EntryId: entryId);
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

    /// <summary>
    /// Builds a card data fixture and a coordinator for the duplicate tests.
    /// </summary>
    private sealed class World : IDisposable
    {
        /// <summary>
        /// Creates a world around a fixture.
        /// </summary>
        private World(CardDataFixture data)
        {
            Data = data;
            Coordinator = new DeckDuplicatesCoordinator(data.Store, data.Scryfall);
        }

        /// <summary>
        /// Gets the card data fixture.
        /// </summary>
        internal CardDataFixture Data { get; }

        /// <summary>
        /// Gets the coordinator under test.
        /// </summary>
        internal DeckDuplicatesCoordinator Coordinator { get; }

        /// <summary>
        /// Creates a world whose installed card data holds the shared cards, or the cards given.
        /// </summary>
        internal static async Task<World> CreateAsync(bool install = true, IReadOnlyList<JsonObject>? cards = null)
        {
            IReadOnlyList<JsonObject> all = cards ?? [Swords, SolRingOne, SolRingTwo, Forest, Rats, Dwarves];
            return new World(await CardDataFixture.CreateAsync(all, install: install));
        }

        /// <summary>
        /// Creates a deck from drafts, with categories and assignments when given.
        /// </summary>
        internal async Task<DeckDocument> CreateDeckAsync(
            IReadOnlyList<DeckEntryDraft> entries,
            IReadOnlyList<(Guid EntryId, string Category)>? assignments = null)
        {
            List<DeckCategoryDraft> categories = [];
            List<DeckCategoryAssignment> links = [];
            foreach ((Guid entryId, string category) in assignments ?? [])
            {
                DeckCategoryDraft? existing = categories.FirstOrDefault(draft => draft.Name == category);
                if (existing is null)
                {
                    existing = new DeckCategoryDraft(category, CategoryId: Guid.NewGuid());
                    categories.Add(existing);
                }

                links.Add(new DeckCategoryAssignment(entryId, existing.CategoryId!.Value, true));
            }

            DeckCreateRequest request = new(
                "Dupes",
                Format: "commander",
                Entries: entries,
                Categories: categories,
                CategoryAssignments: links);
            return Success(await Data.Store.CreateAsync(request, TestContext.Current.CancellationToken));
        }

        /// <summary>
        /// Runs a preview whose keep order lists the given zone or category names; a name that is a category of the
        /// deck is read as a category and any other as a zone.
        /// </summary>
        internal Task<OperationResult<DeckDuplicatesPreview>> Preview(
            DeckDocument deck,
            string[] keepNames,
            string matchBy = "oracle",
            string exempt = "none")
        {
            HashSet<string> categories = [.. deck.Categories.Select(category => category.Name)];
            List<DuplicateKeepGroupInput> groups =
            [
                .. keepNames.Select(name => categories.Contains(name)
                    ? new DuplicateKeepGroupInput(Category: name)
                    : new DuplicateKeepGroupInput(Zone: name)),
            ];
            return Coordinator.PreviewAsync(
                deck.DeckId,
                null,
                matchBy,
                exempt,
                groups,
                TestContext.Current.CancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Data.Dispose();
        }
    }
}
