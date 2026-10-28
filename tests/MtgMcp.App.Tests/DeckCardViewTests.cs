using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>deck_get</c> returns the stored deck unchanged by default and, with include, a page of entries that
/// carry concise card fields and oracle tags from installed card data.
/// </summary>
public sealed class DeckCardViewTests
{
    /// <summary>
    /// Verifies a get without include has the same JSON as the stored deck, so existing callers see no change.
    /// </summary>
    [Fact]
    public async Task Get_WithoutInclude_ReturnsTheStoredDeckJsonUnchanged()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(3);

        DeckDocumentView view = Success(await setup.Reads.GetAsync(
            deck.DeckId,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(deck, ToolCallPolicy.SerializerOptions),
            JsonSerializer.SerializeToNode(view, ToolCallPolicy.SerializerOptions)));
    }

    /// <summary>
    /// Verifies installed card data adds card fields and tags, and each lookup path or missing identity is reported.
    /// </summary>
    [Fact]
    public async Task Get_IncludeCardsAndTags_WithInstalledData_ReportsEachEntryState()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckCreateRequest request = new(
            "States",
            Format: "custom",
            Entries:
            [
                new DeckEntryDraft(1, "By printing", PrintingId: DeckCategorizationSourceFixture.CardId),
                new DeckEntryDraft(1, "By set", SetCode: "tst", CollectorNumber: "1"),
                new DeckEntryDraft(1, "By oracle", OracleId: DeckCategorizationSourceFixture.OracleId),
                new DeckEntryDraft(1, "No identity"),
                new DeckEntryDraft(1, "Not installed", PrintingId: Guid.Parse("a9999999-9999-4999-8999-999999999999")),
            ]);
        DeckDocument deck = Success(await setup.Store.CreateAsync(request, TestContext.Current.CancellationToken));

        DeckDocumentView view = Success(await setup.Reads.GetAsync(
            deck.DeckId,
            [DeckCardViewService.IncludeCards, DeckCardViewService.IncludeTags],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            ["installed", "installed", "installed", "unresolved", "unresolved"],
            view.Entries.Select(entry => entry.CardData));
        foreach (DeckEntryView entry in view.Entries.Take(3))
        {
            Assert.Equal(["fixture-ramp-child"], entry.Tags);
            Assert.True(entry.Card!.TryGetValue("name", out object? name));
            Assert.Equal("Fixture Ramp Card", name);
            Assert.False(entry.Card.TryGetValue("tags", out _));
            Assert.True(entry.Card.TryGetValue("cheapestPriceUsd", out object? price));
            Assert.Null(price);
        }

        DeckEntryView unresolved = view.Entries[3];
        Assert.Null(unresolved.Card);
        Assert.Null(unresolved.Tags);
        Assert.Equal("No identity", unresolved.CardName);
        Assert.Null(view.Limits);
        Assert.Equal(5, view.TotalEntries);
        Assert.Null(view.NextCursor);
        Assert.Null(view.Truncated);
    }

    /// <summary>
    /// Verifies a deck read without installed card data reports not-installed and where to install it.
    /// </summary>
    [Fact]
    public async Task Get_IncludeCards_WithoutInstalledData_ReportsNotInstalled()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(2);

        DeckDocumentView view = Success(await setup.Reads.GetAsync(
            deck.DeckId,
            [DeckCardViewService.IncludeCards],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.All(view.Entries, entry => Assert.Equal("not-installed", entry.CardData));
        Assert.All(view.Entries, entry => Assert.Null(entry.Card));
        Assert.Equal(["Entry 000", "Entry 001"], view.Entries.Select(entry => entry.CardName));
        Assert.Contains("scryfall_corpus_sync", Assert.Single(view.Limits!), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the tags-only include adds slugs without a card, and a field list narrows the card fields.
    /// </summary>
    [Fact]
    public async Task Get_TagsOnlyAndFieldList_ChooseWhatEntriesCarry()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(1);
        CancellationToken token = TestContext.Current.CancellationToken;

        DeckDocumentView tagsOnly = Success(await setup.Reads.GetAsync(
            deck.DeckId,
            [DeckCardViewService.IncludeTags],
            cancellationToken: token));
        DeckDocumentView narrowed = Success(await setup.Reads.GetAsync(
            deck.DeckId,
            [DeckCardViewService.IncludeCards],
            ["typeLine"],
            cancellationToken: token));

        DeckEntryView tagged = Assert.Single(tagsOnly.Entries);
        Assert.Null(tagged.Card);
        Assert.Equal(["fixture-ramp-child"], tagged.Tags);
        DeckEntryView withCard = Assert.Single(narrowed.Entries);
        Assert.Equal(
            ["scryfallId", "oracleId", "name", "typeLine"],
            withCard.Card!.Fields.Select(field => field.Key));
        Assert.Null(withCard.Tags);
    }

    /// <summary>
    /// Verifies arguments that do not fit together are rejected with invalid input.
    /// </summary>
    [Theory]
    [InlineData("include-required", null, null, null, 10)]
    [InlineData("include-required", null, new[] { "name" }, null, null)]
    [InlineData("include-required", null, null, "abc", null)]
    [InlineData("invalid-include", new[] { "cards", "prices" }, null, null, null)]
    [InlineData("fields-require-cards", new[] { "tags" }, new[] { "name" }, null, null)]
    [InlineData("invalid-fields", new[] { "cards" }, new[] { "nope" }, null, null)]
    [InlineData("invalid-page-size", new[] { "cards" }, null, null, 0)]
    [InlineData("invalid-page-size", new[] { "cards" }, null, null, 101)]
    [InlineData("invalid-cursor", new[] { "cards" }, null, "not-a-cursor", null)]
    public async Task Get_InvalidArguments_ReturnInvalidInput(
        string reasonCode,
        string[]? include,
        string[]? fields,
        string? cursor,
        int? pageSize)
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(1);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await setup.Reads.GetAsync(
            deck.DeckId,
            include,
            fields,
            cursor,
            pageSize,
            TestContext.Current.CancellationToken)).Value);

        Assert.Equal(reasonCode, invalid.ReasonCode);
    }

    /// <summary>
    /// Verifies a missing deck is reported as not found even when include is given.
    /// </summary>
    [Fact]
    public async Task Get_MissingDeck_ReturnsNotFound()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);

        OperationResult<DeckDocumentView> result = await setup.Reads.GetAsync(
            Guid.CreateVersion7(),
            [DeckCardViewService.IncludeTags],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<OperationNotFound>(result.Value);
    }

    /// <summary>
    /// Verifies a 100-entry deck pages by size, every page stays within 40,000 bytes, and the pages cover each entry once.
    /// </summary>
    [Fact]
    public async Task Get_HundredEntryDeck_PagesWithinTheSizeLimit()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(100, withCategories: true);
        string[] include = [DeckCardViewService.IncludeCards, DeckCardViewService.IncludeTags];
        List<string> names = [];
        string? cursor = null;
        bool sawTruncation = false;
        int pages = 0;

        do
        {
            OperationResult<DeckDocumentView> result = await setup.Reads.GetAsync(
                deck.DeckId,
                include,
                null,
                cursor,
                100,
                TestContext.Current.CancellationToken);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(result, ToolCallPolicy.SerializerOptions).Length <= ResultSizeLimit.MaximumBytes);
            DeckDocumentView page = Success(result);
            Assert.Equal(100, page.TotalEntries);
            Assert.Equal(page.Entries.Count, page.CategoryAssignments.Count);
            Assert.All(page.CategoryAssignments, assignment => Assert.Contains(page.Entries, entry => entry.EntryId == assignment.EntryId));
            if (page.NextCursor is not null)
            {
                Assert.True(page.Truncated);
                Assert.Contains("nextCursor", page.Narrow, StringComparison.Ordinal);
                sawTruncation = true;
            }

            names.AddRange(page.Entries.Select(entry => entry.CardName));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.True(sawTruncation);
        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 100).Select(index => $"Entry {index:D3}"), names);
    }

    /// <summary>
    /// Verifies a small page size pages without truncation and the cursor reads exactly the next entries.
    /// </summary>
    [Fact]
    public async Task Get_SmallPageSize_PagesByCount()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(5);
        string[] include = [DeckCardViewService.IncludeTags];
        CancellationToken token = TestContext.Current.CancellationToken;

        DeckDocumentView first = Success(await setup.Reads.GetAsync(deck.DeckId, include, null, null, 2, token));
        DeckDocumentView second = Success(await setup.Reads.GetAsync(deck.DeckId, include, null, first.NextCursor, 2, token));
        DeckDocumentView third = Success(await setup.Reads.GetAsync(deck.DeckId, include, null, second.NextCursor, 2, token));

        Assert.Equal(["Entry 000", "Entry 001"], first.Entries.Select(entry => entry.CardName));
        Assert.Equal(["Entry 002", "Entry 003"], second.Entries.Select(entry => entry.CardName));
        Assert.Equal(["Entry 004"], third.Entries.Select(entry => entry.CardName));
        Assert.Null(third.NextCursor);
        Assert.Null(first.Truncated);
    }

    /// <summary>
    /// Verifies a cursor from one request is refused for another request and when the deck has changed.
    /// </summary>
    [Fact]
    public async Task Get_Cursor_IsBoundToTheRequestAndDeckRevision()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(4);
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocumentView first = Success(await setup.Reads.GetAsync(
            deck.DeckId,
            [DeckCardViewService.IncludeTags],
            null,
            null,
            2,
            token));

        OperationInvalidInput otherRequest = Assert.IsType<OperationInvalidInput>((await setup.Reads.GetAsync(
            deck.DeckId,
            [DeckCardViewService.IncludeCards],
            null,
            first.NextCursor,
            2,
            token)).Value);
        _ = Success(await new DeckWriteTools(setup.Store, OperationMode.Local).UpdateAsync(
            deck.DeckId,
            deck.Revision,
            "Renamed",
            null,
            "custom",
            token));
        OperationConflict changed = Assert.IsType<OperationConflict>((await setup.Reads.GetAsync(
            deck.DeckId,
            [DeckCardViewService.IncludeTags],
            null,
            first.NextCursor,
            2,
            token)).Value);

        Assert.Equal("invalid-cursor", otherRequest.ReasonCode);
        Assert.Equal("deck-changed-between-pages", changed.ReasonCode);
        Assert.Equal(deck.Revision + 1, changed.Current!.Revision);
    }

    /// <summary>
    /// Extracts successful data or fails the test with the outcome.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Owns a deck store, a Scryfall service, and the read tools over them.
    /// </summary>
    private sealed class TestSetup : IDisposable
    {
        /// <summary>
        /// Holds the folder that backs the stores.
        /// </summary>
        private readonly TemporaryDirectory directory;

        /// <summary>
        /// Holds the Scryfall service the read tools query.
        /// </summary>
        private readonly ScryfallService scryfall;

        /// <summary>
        /// Creates a setup around already created services.
        /// </summary>
        private TestSetup(TemporaryDirectory directory, SqliteDeckStore store, ScryfallService scryfall)
        {
            this.directory = directory;
            this.scryfall = scryfall;
            Store = store;
            Reads = new DeckReadTools(store, scryfall);
        }

        /// <summary>
        /// Gets the deck store.
        /// </summary>
        internal SqliteDeckStore Store { get; }

        /// <summary>
        /// Gets the read tools under test.
        /// </summary>
        internal DeckReadTools Reads { get; }

        /// <summary>
        /// Creates a setup with or without installed Scryfall card data.
        /// </summary>
        internal static async Task<TestSetup> CreateAsync(bool installCardData)
        {
            TemporaryDirectory directory = new();
            SqliteDeckStore store = new(directory.Path, "test");
            ScryfallService scryfall = DeckCategorizationSourceFixture.CreateService(
                directory.Path,
                DeckCategorizationSourceFixture.CreateHandler());
            if (installCardData)
            {
                _ = Success(await scryfall.SyncCorpusAsync("refresh", null, TestContext.Current.CancellationToken));
            }

            return new TestSetup(directory, store, scryfall);
        }

        /// <summary>
        /// Creates a deck whose entries all point at the installed fixture card.
        /// </summary>
        internal async Task<DeckDocument> CreateDeckAsync(int entryCount, bool withCategories = false)
        {
            List<DeckEntryDraft> entries = [];
            List<DeckCategoryAssignment> assignments = [];
            Guid categoryId = Guid.CreateVersion7();
            for (int index = 0; index < entryCount; index++)
            {
                Guid entryId = Guid.CreateVersion7();
                entries.Add(new DeckEntryDraft(
                    1,
                    $"Entry {index:D3}",
                    OracleId: DeckCategorizationSourceFixture.OracleId,
                    PrintingId: DeckCategorizationSourceFixture.CardId,
                    SortOrder: index,
                    EntryId: entryId));
                assignments.Add(new DeckCategoryAssignment(entryId, categoryId, true));
            }

            DeckCreateRequest request = new(
                "Card view deck",
                Format: "custom",
                Entries: entries,
                Categories: withCategories ? [new DeckCategoryDraft("Ramp", CategoryId: categoryId)] : null,
                CategoryAssignments: withCategories ? assignments : null);
            return Success(await Store.CreateAsync(request, TestContext.Current.CancellationToken));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            scryfall.Dispose();
            Store.Dispose();
            directory.Dispose();
        }
    }
}
