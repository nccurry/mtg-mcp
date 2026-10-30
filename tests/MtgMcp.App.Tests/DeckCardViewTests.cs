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
/// Verifies <c>local_deck_get</c> returns the stored deck unchanged by default and, with include, a page of
/// entries that carry concise card fields and oracle tags from installed card data.
/// </summary>
public sealed class DeckCardViewTests
{
    /// <summary>
    /// Names the include values that ask for both cards and tags.
    /// </summary>
    private static readonly string[] CardsAndTags = ["cards", "tags"];

    /// <summary>
    /// Verifies a get without include has the same JSON as the stored deck, so existing callers see no change.
    /// </summary>
    [Fact]
    public async Task Get_WithoutInclude_ReturnsTheStoredDeckJsonUnchanged()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(3);

        DeckDocumentView view = Success(await setup.Get(deck.DeckId));

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
        Guid unknownPrinting = Guid.Parse("a9999999-9999-4999-8999-999999999999");
        DeckCreateRequest request = new(
            "States",
            Format: "custom",
            Entries:
            [
                new DeckEntryDraft(1, "By printing", PrintingId: DeckCategorizationSourceFixture.CardId),
                new DeckEntryDraft(1, "By set", SetCode: "tst", CollectorNumber: "1"),
                new DeckEntryDraft(1, "By oracle", OracleId: DeckCategorizationSourceFixture.OracleId),
                new DeckEntryDraft(1, "No identity"),
                new DeckEntryDraft(1, "Unknown printing", PrintingId: unknownPrinting),
            ]);
        DeckDocument deck = Success(await setup.Store.CreateAsync(request, TestContext.Current.CancellationToken));

        DeckDocumentView view = Success(await setup.Get(deck.DeckId, CardsAndTags));

        Assert.Equal(
            ["found", "found", "found", "unresolved", "unresolved"],
            view.Entries.Select(entry => entry.CardData));
        foreach (DeckEntryView entry in view.Entries.Take(3))
        {
            Assert.Equal(["fixture-ramp-child"], entry.Tags);
            Assert.Equal("Fixture Ramp Card", Field(entry.Card!, "name"));
            Assert.DoesNotContain(entry.Card!.Fields, field => field.Key == "tags");
            Assert.Null(Field(entry.Card, "cheapestPriceUsd"));
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

        DeckDocumentView view = Success(await setup.Get(deck.DeckId, ["cards"]));

        Assert.All(view.Entries, entry => Assert.Equal("not-installed", entry.CardData));
        Assert.All(view.Entries, entry => Assert.Null(entry.Card));
        Assert.Equal(["Entry 000", "Entry 001"], view.Entries.Select(entry => entry.CardName));
        Assert.Contains("scryfall_card_data_sync", Assert.Single(view.Limits!), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the tags-only include adds slugs without a card, and a field list narrows the card fields.
    /// </summary>
    [Fact]
    public async Task Get_TagsOnlyAndFieldList_ChooseWhatEntriesCarry()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(1);

        DeckDocumentView tagsOnly = Success(await setup.Get(deck.DeckId, ["tags"]));
        DeckDocumentView narrowed = Success(await setup.Get(deck.DeckId, ["cards"], ["typeLine"]));

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
    /// Verifies the card keeps its tags field unless the entry carries the tags too, and then drops it.
    /// </summary>
    [Fact]
    public async Task Get_CardTagsField_IsDroppedOnlyWhenIncludeAlsoHasTags()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(1);

        DeckEntryView cardsOnly = Assert.Single(Success(await setup.Get(deck.DeckId, ["cards"])).Entries);
        DeckEntryView both = Assert.Single(Success(await setup.Get(deck.DeckId, CardsAndTags)).Entries);
        DeckEntryView named = Assert.Single(Success(await setup.Get(deck.DeckId, CardsAndTags, ["tags"])).Entries);

        Assert.Equal(["fixture-ramp-child"], FieldTexts(cardsOnly.Card!, "tags"));
        Assert.Null(cardsOnly.Tags);
        Assert.DoesNotContain(both.Card!.Fields, field => field.Key == "tags");
        Assert.Equal(["fixture-ramp-child"], both.Tags);
        Assert.Equal(["fixture-ramp-child"], FieldTexts(named.Card!, "tags"));
    }

    /// <summary>
    /// Verifies arguments that do not fit together are rejected with invalid input.
    /// </summary>
    [Theory]
    [InlineData("invalid-page-size", null, null, null, 0)]
    [InlineData("include-required", null, new[] { "name" }, null, null)]
    [InlineData("invalid-cursor", null, null, "abc", null)]
    [InlineData("empty-include", new string[0], null, null, null)]
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

        OperationResult<DeckDocumentView> result = await setup.Get(deck.DeckId, include, fields, cursor, pageSize);

        Assert.Equal(reasonCode, Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a missing deck is reported as not found even when include is given.
    /// </summary>
    [Fact]
    public async Task Get_MissingDeck_ReturnsNotFound()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);

        OperationResult<DeckDocumentView> result = await setup.Get(Guid.CreateVersion7(), ["tags"]);

        Assert.IsType<OperationNotFound>(result.Value);
    }

    /// <summary>
    /// Verifies a 100-entry deck pages by size, every page stays within 40,000 bytes, and the pages cover each entry
    /// once, in order.
    /// </summary>
    [Fact]
    public async Task Get_HundredEntryDeck_PagesWithinTheSizeLimit()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(100, withCategories: true);
        List<string> names = [];
        int pages = 0;
        string? cursor = null;

        do
        {
            OperationResult<DeckDocumentView> result = await setup.Get(deck.DeckId, CardsAndTags, null, cursor, 100);

            DeckDocumentView page = Success(result);
            Assert.True(Bytes(result) <= ResultSizeLimit.MaximumBytes);
            Assert.Equal(100, page.TotalEntries);
            Assert.Equal(page.Entries.Count, page.CategoryAssignments.Count);
            Assert.All(
                page.CategoryAssignments,
                assignment => Assert.Contains(page.Entries, entry => entry.EntryId == assignment.EntryId));
            Assert.Equal(page.NextCursor is not null, page.Truncated == true);
            names.AddRange(page.Entries.Select(entry => entry.CardName));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 100).Select(index => $"Entry {index:D3}"), names);
    }

    /// <summary>
    /// Verifies a deck with a 60,000-character description and a long name pages under the size limit: the name is cut
    /// on every page, and the description is cut on the first page and empty on the pages after it.
    /// </summary>
    [Fact]
    public async Task Get_LongDescriptionAndName_AreCutSoEveryPageStaysUnderTheLimit()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = Success(await setup.Store.CreateAsync(
            new DeckCreateRequest(
                new string('n', 5_000),
                Description: new string('d', 60_000),
                Format: "custom",
                Entries: [.. Enumerable.Range(0, 300).Select(index => new DeckEntryDraft(1, $"Entry {index:D3}"))]),
            TestContext.Current.CancellationToken));
        List<DeckDocumentView> pages = [];
        string? cursor = null;

        do
        {
            OperationResult<DeckDocumentView> result = await setup.Get(deck.DeckId, null, null, cursor, null);
            Assert.True(Bytes(result) <= 40_000, $"page {pages.Count} has {Bytes(result)} bytes");
            pages.Add(Success(result));
            cursor = pages[^1].NextCursor;
        }
        while (cursor is not null);

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.Equal(ResultText.MaximumNameBytes, page.Name.Length));
        Assert.All(pages, page => Assert.True(page.NameTruncated));
        Assert.Equal(ResultText.MaximumDescriptionBytes, pages[0].Description.Length);
        Assert.Equal(60_000, pages[0].DescriptionLength);
        Assert.All(pages.Skip(1), page => Assert.Equal(string.Empty, page.Description));
        Assert.All(pages, page => Assert.True(page.DescriptionTruncated));
    }

    /// <summary>
    /// Verifies a 300-entry deck stays within 40,000 characters (the hard ceiling is 50,000) for a read with no include
    /// and with every include
    /// combination, and that following nextCursor reaches every entry once, in order.
    /// </summary>
    /// <param name="includeText">Comma-separated include values, or empty for none.</param>
    [Theory]
    [InlineData("")]
    [InlineData("cards")]
    [InlineData("tags")]
    [InlineData("cards,tags")]
    public async Task Get_ThreeHundredEntryDeck_EveryPageIsWithinFortyThousandCharacters(string includeText)
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(300, withCategories: true);
        string[]? include = includeText.Length == 0 ? null : includeText.Split(',');
        List<string> names = [];
        int pages = 0;
        string? cursor = null;

        do
        {
            OperationResult<DeckDocumentView> result = await setup.Get(deck.DeckId, include, null, cursor);

            DeckDocumentView page = Success(result);
            Assert.True(Bytes(result) <= 40_000, $"page {pages} has {Bytes(result)} bytes");
            Assert.Equal(300, page.TotalEntries);
            Assert.True(include is not null || page.NextCursor is null || page.Truncated == true);
            if (page.Truncated == true)
            {
                Assert.False(string.IsNullOrWhiteSpace(page.Narrow));
                Assert.Contains("nextCursor", page.Narrow, StringComparison.Ordinal);
            }

            names.AddRange(page.Entries.Select(entry => entry.CardName));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 300).Select(index => $"Entry {index:D3}"), names);
    }

    /// <summary>
    /// Verifies a deck that fits one result is returned whole with no paging fields, and a caller can still ask for
    /// smaller pages of the stored deck with pageSize and a cursor.
    /// </summary>
    [Fact]
    public async Task Get_WithoutInclude_PagesOnlyWhenAskedOrTooLarge()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument small = await setup.CreateDeckAsync(10);

        DeckDocumentView whole = Success(await setup.Get(small.DeckId));
        DeckDocumentView first = Success(await setup.Get(small.DeckId, pageSize: 4));
        DeckDocumentView second = Success(await setup.Get(small.DeckId, pageSize: 4, cursor: first.NextCursor));

        Assert.Equal(10, whole.Entries.Count);
        Assert.Null(whole.NextCursor);
        Assert.Null(whole.TotalEntries);
        Assert.Equal(4, first.Entries.Count);
        Assert.Equal(10, first.TotalEntries);
        Assert.Null(first.Truncated);
        Assert.Equal(
            small.Entries.Skip(4).Take(4).Select(entry => entry.EntryId),
            second.Entries.Select(entry => entry.EntryId));
    }

    /// <summary>
    /// Verifies a small page size pages without truncation and the cursor reads exactly the next entries.
    /// </summary>
    [Fact]
    public async Task Get_SmallPageSize_PagesByCount()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(5);

        DeckDocumentView first = Success(await setup.Get(deck.DeckId, ["tags"], null, null, 2));
        DeckDocumentView second = Success(await setup.Get(deck.DeckId, ["tags"], null, first.NextCursor, 2));
        DeckDocumentView third = Success(await setup.Get(deck.DeckId, ["tags"], null, second.NextCursor, 2));

        Assert.Equal(["Entry 000", "Entry 001"], first.Entries.Select(entry => entry.CardName));
        Assert.Equal(["Entry 002", "Entry 003"], second.Entries.Select(entry => entry.CardName));
        Assert.Equal(["Entry 004"], third.Entries.Select(entry => entry.CardName));
        Assert.Null(third.NextCursor);
        Assert.Null(first.Truncated);
    }

    /// <summary>
    /// Verifies the order of include and fields does not change which cursor belongs to the request.
    /// </summary>
    [Fact]
    public async Task Get_Cursor_IgnoresTheOrderOfIncludeAndFields()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(4);
        string[] fields = ["layout", "typeLine"];
        DeckDocumentView first = Success(await setup.Get(deck.DeckId, ["cards", "tags"], fields, null, 2));

        DeckDocumentView second = Success(await setup.Get(
            deck.DeckId,
            ["tags", "cards"],
            ["typeLine", "layout"],
            first.NextCursor,
            2));

        Assert.Equal(["Entry 002", "Entry 003"], second.Entries.Select(entry => entry.CardName));
    }

    /// <summary>
    /// Verifies a cursor from another request is refused, and a changed deck gives a conflict with its revision.
    /// </summary>
    [Fact]
    public async Task Get_Cursor_IsBoundToTheRequestAndDeckRevision()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(4);
        DeckDocumentView first = Success(await setup.Get(deck.DeckId, ["tags"], null, null, 2));

        OperationResult<DeckDocumentView> otherRequest = await setup.Get(
            deck.DeckId,
            ["cards"],
            null,
            first.NextCursor,
            2);
        DeckWriteResult renamed = Success(await setup.Writes.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [new UpdateDeckMetadataInput("Renamed", null, "custom")],
            cancellationToken: TestContext.Current.CancellationToken));
        OperationResult<DeckDocumentView> changed = await setup.Get(deck.DeckId, ["tags"], null, first.NextCursor, 2);

        Assert.Equal("invalid-cursor", Assert.IsType<OperationInvalidInput>(otherRequest.Value).ReasonCode);
        OperationConflict conflict = Assert.IsType<OperationConflict>(changed.Value);
        Assert.Equal("deck-changed-between-pages", conflict.ReasonCode);
        Assert.Equal(renamed.Revision, conflict.Current!.Revision);
    }

    /// <summary>
    /// Verifies entries removed between pages give a conflict with the current revision, not an offset error.
    /// </summary>
    [Fact]
    public async Task Get_EntriesRemovedBetweenPages_ReturnsConflictWithTheCurrentRevision()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(6);
        DeckDocumentView first = Success(await setup.Get(deck.DeckId, ["tags"], null, null, 5));
        DeckWriteResult? current = null;
        long revision = deck.Revision;
        foreach (DeckEntry entry in deck.Entries.Take(5))
        {
            current = Success(await setup.Writes.ApplyChangesAsync(
                deck.DeckId,
                revision,
                [new RemoveDeckEntryInput(entry.EntryId)],
                cancellationToken: TestContext.Current.CancellationToken));
            revision = current.Revision;
        }

        OperationResult<DeckDocumentView> result = await setup.Get(deck.DeckId, ["tags"], null, first.NextCursor, 5);

        OperationConflict conflict = Assert.IsType<OperationConflict>(result.Value);
        Assert.Equal(current!.Revision, conflict.Current!.Revision);
    }

    /// <summary>
    /// Verifies a cursor with a missing or null fingerprint, a JSON null body, a negative position, or a wrong
    /// fingerprint is refused as invalid, never as an exception.
    /// </summary>
    [Theory]
    [InlineData("missing-fingerprint")]
    [InlineData("null-fingerprint")]
    [InlineData("json-null")]
    [InlineData("negative-offset")]
    [InlineData("wrong-fingerprint")]
    [InlineData("offset-past-the-end")]
    public async Task Get_MalformedCursor_ReturnsInvalidCursor(string kind)
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(4);
        DeckDocumentView first = Success(await setup.Get(deck.DeckId, ["tags"], null, null, 2));
        JsonObject real = DecodeCursor(first.NextCursor!);
        string cursor = kind switch
        {
            "missing-fingerprint" => EncodeCursor($"{{\"Revision\":{deck.Revision},\"Offset\":2}}"),
            "null-fingerprint" => EncodeCursor($"{{\"Fingerprint\":null,\"Revision\":{deck.Revision},\"Offset\":2}}"),
            "json-null" => EncodeCursor("null"),
            "negative-offset" => EncodeCursor(WithOffset(real, -1)),
            "wrong-fingerprint" => EncodeCursor($"{{\"Fingerprint\":\"x\",\"Revision\":{deck.Revision},\"Offset\":2}}"),
            _ => EncodeCursor(WithOffset(real, 99)),
        };

        OperationResult<DeckDocumentView> result = await setup.Get(deck.DeckId, ["tags"], null, cursor, 2);

        Assert.Equal("invalid-cursor", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Reads one list-of-text field value of a concise card.
    /// </summary>
    private static string[] FieldTexts(ScryfallConciseCard card, string name)
    {
        return [.. Assert.IsAssignableFrom<IEnumerable<string>>(Field(card, name))];
    }

    /// <summary>
    /// Reads one field value of a concise card.
    /// </summary>
    private static object? Field(ScryfallConciseCard card, string name)
    {
        KeyValuePair<string, object?> field = Assert.Single(card.Fields, value => value.Key == name);
        return field.Value;
    }

    /// <summary>
    /// Extracts successful data or fails the test with the outcome.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Measures a result as the server writes it.
    /// </summary>
    private static int Bytes<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, ToolCallPolicy.SerializerOptions).Length;
    }

    /// <summary>
    /// Reads the JSON body of an opaque cursor.
    /// </summary>
    private static JsonObject DecodeCursor(string cursor)
    {
        string padded = cursor.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
        return JsonNode.Parse(Convert.FromBase64String(padded))!.AsObject();
    }

    /// <summary>
    /// Builds an opaque cursor from JSON text.
    /// </summary>
    private static string EncodeCursor(string json)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Writes a real cursor body with another entry position.
    /// </summary>
    private static string WithOffset(JsonObject body, int offset)
    {
        JsonObject copy = JsonNode.Parse(body.ToJsonString())!.AsObject();
        copy["Offset"] = offset;
        return copy.ToJsonString();
    }

    /// <summary>
    /// Owns a deck store, a Scryfall service, and the read and write tools over them.
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
        /// Holds the read tools under test.
        /// </summary>
        private readonly DeckReadTools reads;

        /// <summary>
        /// Creates a setup around already created services.
        /// </summary>
        private TestSetup(TemporaryDirectory directory, SqliteDeckStore store, ScryfallService scryfall)
        {
            this.directory = directory;
            this.scryfall = scryfall;
            Store = store;
            reads = new DeckReadTools(store, new DeckCardViewCoordinator(store, scryfall),
                new DeckRuleCoordinator(store, scryfall));
            Writes = new DeckBatchWriteTools(store, OperationMode.Local, new DeckEntryResolver(scryfall));
        }

        /// <summary>
        /// Gets the deck store.
        /// </summary>
        internal SqliteDeckStore Store { get; }

        /// <summary>
        /// Gets the batch write tool used to change decks between pages.
        /// </summary>
        internal DeckBatchWriteTools Writes { get; }

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
        /// Calls <c>local_deck_get</c> with the given arguments.
        /// </summary>
        internal Task<OperationResult<DeckDocumentView>> Get(
            Guid deckId,
            string[]? include = null,
            string[]? fields = null,
            string? cursor = null,
            int? pageSize = null)
        {
            return reads.GetAsync(deckId, include, fields, cursor, pageSize, TestContext.Current.CancellationToken);
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
