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
    /// and with every include combination, and that following nextCursor reaches every entry once, in order.
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
    /// Verifies every page of a 172-entry deck with 99 sideboard rows carries the deck-wide totals, a cut page says it
    /// is one page of the deck, and following nextCursor reaches every entry once.
    /// </summary>
    [Fact]
    public async Task Get_LargeSideboardDeck_EveryPageCarriesDeckWideTotals()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        List<DeckEntryDraft> drafts = [new DeckEntryDraft(1, "Commander Card", Zone: "commander")];
        drafts.AddRange(Enumerable.Range(0, 72).Select(index =>
            new DeckEntryDraft(1, $"Main card {index:D3} {new string('x', 120)}", Zone: "main", SortOrder: index)));
        drafts.AddRange(Enumerable.Range(0, 99).Select(index =>
            new DeckEntryDraft(
                index < 12 ? 2 : 1,
                $"Sideboard card {index:D3} {new string('y', 120)}",
                Zone: "sideboard",
                SortOrder: index)));
        DeckDocument deck = Success(await setup.Store.CreateAsync(
            new DeckCreateRequest("Big deck", Format: "commander", Entries: drafts),
            TestContext.Current.CancellationToken));
        List<Guid> reached = [];
        List<DeckDocumentView> pages = [];
        string? cursor = null;

        do
        {
            DeckDocumentView page = Success(await setup.Get(deck.DeckId, null, null, cursor));
            pages.Add(page);
            reached.AddRange(page.Entries.Select(entry => entry.EntryId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.Equal(172, page.EntryCount));
        Assert.All(pages, page => Assert.Equal(184, page.CardCount));
        Assert.All(pages, page => Assert.Equal(
            new Dictionary<string, int> { ["commander"] = 1, ["main"] = 72, ["sideboard"] = 111 },
            page.CardsByZone));
        Assert.True(pages[0].Truncated);
        Assert.Equal(
            $"This page shows {pages[0].Entries.Count} of 172 entries; the deck is not missing the rest. " +
            "Follow nextCursor. Ask for a smaller pageSize to get smaller pages.",
            pages[0].Narrow);
        Assert.Equal(172, reached.Count);
        Assert.Equal(172, reached.Distinct().Count());
        Assert.Equal(deck.Entries.Select(entry => entry.EntryId).Order(), reached.Order());
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
    /// Verifies cardNames returns only matching entries, ignores case, finds a double-faced card by one face, lists
    /// the names with no match, and keeps the deck-wide totals.
    /// </summary>
    [Fact]
    public async Task Get_CardNames_ReturnsMatchesAndTheNamesNotFound()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = Success(await setup.Store.CreateAsync(
            new DeckCreateRequest(
                "Names",
                Format: "custom",
                Entries:
                [
                    new DeckEntryDraft(1, "Sol Ring"),
                    new DeckEntryDraft(2, "Island", Zone: "sideboard"),
                    new DeckEntryDraft(1, "Delver of Secrets // Insectile Aberration"),
                    new DeckEntryDraft(1, "Arcane Signet"),
                ]),
            TestContext.Current.CancellationToken));

        DeckDocumentView view = Success(await setup.Get(
            deck.DeckId,
            cardNames: ["sol ring", "DELVER OF SECRETS", "Mana Crypt", "Island"]));

        Assert.Equal(
            ["Delver of Secrets // Insectile Aberration", "Sol Ring", "Island"],
            view.Entries.Select(entry => entry.CardName));
        Assert.Equal(["Mana Crypt"], view.CardNamesNotFound);
        Assert.Equal(4, view.EntryCount);
        Assert.Equal(5, view.CardCount);
        Assert.Equal(
            new Dictionary<string, int> { ["main"] = 3, ["sideboard"] = 2 },
            view.CardsByZone);
        Assert.Equal(3, view.TotalEntries);
        Assert.Null(view.NextCursor);
    }

    /// <summary>
    /// Verifies a name list with no match gives no entries, and an empty list, too many names, a blank name, and an
    /// unknown response format are refused.
    /// </summary>
    [Fact]
    public async Task Get_CardNames_AllMissOrInvalid_AreHandled()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(2);

        DeckDocumentView miss = Success(await setup.Get(deck.DeckId, cardNames: ["Nothing"]));
        OperationResult<DeckDocumentView> empty = await setup.Get(deck.DeckId, cardNames: []);
        OperationResult<DeckDocumentView> tooMany = await setup.Get(
            deck.DeckId,
            cardNames: [.. Enumerable.Range(0, 101).Select(index => $"Card {index}")]);
        OperationResult<DeckDocumentView> blank = await setup.Get(deck.DeckId, cardNames: ["Entry 000", " "]);
        OperationResult<DeckDocumentView> format = await setup.Get(deck.DeckId, responseFormat: "tiny");

        Assert.Empty(miss.Entries);
        Assert.Equal(["Nothing"], miss.CardNamesNotFound);
        Assert.Equal("invalid-card-names", Assert.IsType<OperationInvalidInput>(empty.Value).ReasonCode);
        Assert.Equal("invalid-card-names", Assert.IsType<OperationInvalidInput>(tooMany.Value).ReasonCode);
        OperationInvalidInput blankFailure = Assert.IsType<OperationInvalidInput>(blank.Value);
        Assert.Equal("cardNames[1]", blankFailure.Path);
        Assert.Equal("invalid-response-format", Assert.IsType<OperationInvalidInput>(format.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies cardNames works with include, and a cursor refuses a changed name list or response format.
    /// </summary>
    [Fact]
    public async Task Get_Cursor_RefusesAChangedFilterOrFormat()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(6);
        string[] names = ["Entry 001", "Entry 003", "Entry 004", "Entry 005"];
        DeckDocumentView first = Success(await setup.Get(deck.DeckId, ["tags"], null, null, 2, names));

        DeckDocumentView second = Success(await setup.Get(deck.DeckId, ["tags"], null, first.NextCursor, 2, names));
        OperationResult<DeckDocumentView> otherNames = await setup.Get(
            deck.DeckId, ["tags"], null, first.NextCursor, 2, ["Entry 001"]);
        OperationResult<DeckDocumentView> noNames = await setup.Get(deck.DeckId, ["tags"], null, first.NextCursor, 2);
        OperationResult<DeckDocumentView> otherFormat = await setup.Get(
            deck.DeckId, ["tags"], null, first.NextCursor, 2, names, "concise");

        Assert.Equal(4, first.TotalEntries);
        Assert.Equal(["Entry 001", "Entry 003"], first.Entries.Select(entry => entry.CardName));
        Assert.Equal(["Entry 004", "Entry 005"], second.Entries.Select(entry => entry.CardName));
        Assert.Null(second.NextCursor);
        Assert.Equal(["fixture-ramp-child"], first.Entries[0].Tags);
        Assert.All(
            (OperationResult<DeckDocumentView>[])[otherNames, noNames, otherFormat],
            result => Assert.Equal("invalid-cursor", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode));
    }

    /// <summary>
    /// Verifies concise entries hold only the entry ID, card name, quantity, zone, and category names, with no
    /// assignment rows, include still adds card data to them, and standard gives the same result as full.
    /// </summary>
    [Fact]
    public async Task Get_Concise_ReturnsOnlyTheShortEntryFields()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: true);
        DeckDocument deck = await setup.CreateDeckAsync(2, withCategories: true);

        DeckDocumentView view = Success(await setup.Get(deck.DeckId, responseFormat: "concise"));
        DeckDocumentView withTags = Success(await setup.Get(deck.DeckId, ["tags"], responseFormat: "concise"));
        DeckDocumentView standard = Success(await setup.Get(deck.DeckId, responseFormat: "standard"));
        DeckDocumentView full = Success(await setup.Get(deck.DeckId));

        JsonNode entry = JsonSerializer.SerializeToNode(view, ToolCallPolicy.SerializerOptions)!["entries"]![0]!;
        Assert.Equal(
            ["entryId", "quantity", "cardName", "zone", "categoryNames"],
            [.. entry.AsObject().Select(property => property.Key)]);
        Assert.Equal(["Ramp"], view.Entries[0].CategoryNames);
        Assert.Empty(view.CategoryAssignments);
        Assert.Equal(["fixture-ramp-child"], withTags.Entries[0].Tags);
        Assert.Equal(["Ramp"], withTags.Entries[0].CategoryNames);
        Assert.Equal(2, standard.CategoryAssignments.Count);
        Assert.Null(standard.Entries[0].CategoryNames);
        Assert.True(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(full, ToolCallPolicy.SerializerOptions),
            JsonSerializer.SerializeToNode(standard, ToolCallPolicy.SerializerOptions)));
    }

    /// <summary>
    /// Verifies a 172-entry deck with 99 sideboard rows and category assignments comes back in one concise result
    /// under the size limit, while the full read of the same deck is cut into pages.
    /// </summary>
    [Fact]
    public async Task Get_Concise_LargeSideboardDeckFitsInOneResult()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        Guid categoryId = Guid.CreateVersion7();
        List<DeckEntryDraft> drafts = [];
        List<DeckCategoryAssignment> assignments = [];
        for (int index = 0; index < 172; index++)
        {
            Guid entryId = Guid.CreateVersion7();
            drafts.Add(new DeckEntryDraft(
                1,
                $"Card number {index:D3} of the large deck",
                OracleId: Guid.CreateVersion7(),
                PrintingId: Guid.CreateVersion7(),
                SetCode: "abc",
                CollectorNumber: index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Zone: index < 73 ? "main" : "sideboard",
                SortOrder: index,
                EntryId: entryId));
            assignments.Add(new DeckCategoryAssignment(entryId, categoryId, true));
        }

        DeckDocument deck = Success(await setup.Store.CreateAsync(
            new DeckCreateRequest(
                "Big deck",
                Format: "commander",
                Entries: drafts,
                Categories: [new DeckCategoryDraft("Ramp", CategoryId: categoryId)],
                CategoryAssignments: assignments),
            TestContext.Current.CancellationToken));

        OperationResult<DeckDocumentView> concise = await setup.Get(deck.DeckId, responseFormat: "concise");
        DeckDocumentView full = Success(await setup.Get(deck.DeckId));

        DeckDocumentView page = Success(concise);
        Assert.Equal(172, page.Entries.Count);
        Assert.Null(page.NextCursor);
        Assert.Null(page.Truncated);
        Assert.True(Bytes(concise) < 40_000, $"concise result has {Bytes(concise)} bytes");
        Assert.True(full.Truncated);
    }

    /// <summary>
    /// Verifies a stored read with cardNames and concise entries, and no include, pages by pageSize, accepts the names
    /// reordered or in another letter case on the next page, says the page holds matching entries, and refuses a
    /// changed name list or format.
    /// </summary>
    [Fact]
    public async Task Get_StoredCardNamesAndConcise_PageAndKeepTheCursorFingerprint()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(6);
        string[] names = ["Entry 001", "Entry 003", "Entry 005"];

        DeckDocumentView first = Success(await setup.Get(
            deck.DeckId,
            pageSize: 2,
            cardNames: names,
            responseFormat: "concise"));
        DeckDocumentView second = Success(await setup.Get(
            deck.DeckId,
            cursor: first.NextCursor,
            pageSize: 2,
            cardNames: ["ENTRY 005", "entry 001", " Entry 003 "],
            responseFormat: "concise"));
        OperationResult<DeckDocumentView> otherNames = await setup.Get(
            deck.DeckId,
            cursor: first.NextCursor,
            cardNames: ["Entry 001"],
            responseFormat: "concise");
        OperationResult<DeckDocumentView> otherFormat = await setup.Get(
            deck.DeckId,
            cursor: first.NextCursor,
            cardNames: names);

        Assert.Equal(3, first.TotalEntries);
        Assert.Equal(["Entry 001", "Entry 003"], first.Entries.Select(entry => entry.CardName));
        Assert.Equal(["Entry 005"], second.Entries.Select(entry => entry.CardName));
        Assert.Null(second.NextCursor);
        Assert.All(
            (OperationResult<DeckDocumentView>[])[otherNames, otherFormat],
            result => Assert.Equal("invalid-cursor", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode));
    }

    /// <summary>
    /// Verifies a page cut by size for a read of named cards says it holds matching entries, while the same cut for a
    /// whole-deck read keeps the whole-deck wording.
    /// </summary>
    [Fact]
    public async Task Get_CardNamesCutBySize_NarrowSaysMatchingEntries()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        DeckDocument deck = await setup.CreateDeckAsync(100, withCategories: true);

        DeckDocumentView named = Success(await setup.Get(
            deck.DeckId,
            cardNames: [.. Enumerable.Range(0, 100).Select(index => $"Entry {index:D3}")]));
        DeckDocumentView whole = Success(await setup.Get(deck.DeckId));

        Assert.True(named.Truncated);
        Assert.Contains(
            "of 100 matching entries; follow nextCursor for the rest.",
            named.Narrow,
            StringComparison.Ordinal);
        Assert.Contains("the deck is not missing the rest", whole.Narrow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies concise category names list the primary category first and the others in name order.
    /// </summary>
    [Fact]
    public async Task Get_Concise_ListsThePrimaryCategoryFirst()
    {
        using TestSetup setup = await TestSetup.CreateAsync(installCardData: false);
        Guid entryId = Guid.CreateVersion7();
        Guid alpha = Guid.CreateVersion7();
        Guid beta = Guid.CreateVersion7();
        Guid zebra = Guid.CreateVersion7();
        DeckDocument deck = Success(await setup.Store.CreateAsync(
            new DeckCreateRequest(
                "Categories",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "Sol Ring", EntryId: entryId)],
                Categories:
                [
                    new DeckCategoryDraft("Alpha", CategoryId: alpha),
                    new DeckCategoryDraft("Beta", CategoryId: beta),
                    new DeckCategoryDraft("Zebra", CategoryId: zebra),
                ],
                CategoryAssignments:
                [
                    new DeckCategoryAssignment(entryId, alpha, false),
                    new DeckCategoryAssignment(entryId, zebra, true),
                    new DeckCategoryAssignment(entryId, beta, false),
                ]),
            TestContext.Current.CancellationToken));

        DeckDocumentView view = Success(await setup.Get(deck.DeckId, responseFormat: "concise"));

        Assert.Equal(["Zebra", "Alpha", "Beta"], Assert.Single(view.Entries).CategoryNames);
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
            int? pageSize = null,
            string[]? cardNames = null,
            string responseFormat = "full")
        {
            return reads.GetAsync(
                deckId,
                include,
                fields,
                cursor,
                pageSize,
                cardNames,
                responseFormat,
                TestContext.Current.CancellationToken);
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
