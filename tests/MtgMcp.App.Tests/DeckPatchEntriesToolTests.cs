using System.Text.Json.Nodes;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the patch-entries change, list removal, and drafted categories through the batch and create tools,
/// including printing lookups, skipped adds, and refusals that write nothing.
/// </summary>
public sealed class DeckPatchEntriesToolTests
{
    /// <summary>
    /// Verifies a patch maps to its Core change with the printing group and a normalized zone categories mode.
    /// </summary>
    [Fact]
    public void TryMap_Patch_ProducesCoreChange()
    {
        Guid entry = Guid.CreateVersion7();
        Guid printing = Guid.CreateVersion7();

        bool mapped = DeckChangeInputMapper.TryMap(
            [new PatchDeckEntriesInput([entry], Zone: "sideboard", Printing: new DeckPrintingInput(printing), ZoneCategories: " Match ")],
            out IReadOnlyList<DeckChange> changes,
            out string failure);

        Assert.True(mapped);
        Assert.Equal(string.Empty, failure);
        PatchDeckEntriesChange patch = Assert.IsType<PatchDeckEntriesChange>(Assert.Single(changes).Value);
        Assert.Equal([entry], patch.EntryIds);
        Assert.Equal("match", patch.ZoneCategories);
        Assert.Equal(printing, patch.Printing!.PrintingId);
    }

    /// <summary>
    /// Verifies an invalid patch fails with the rule's message naming the change index.
    /// </summary>
    [Fact]
    public void TryMap_PatchWithNoFields_FailsWithTheRuleMessage()
    {
        bool mapped = DeckChangeInputMapper.TryMap(
            [new UpdateDeckMetadataInput("Deck", null, "custom"), new PatchDeckEntriesInput([Guid.CreateVersion7()])],
            out IReadOnlyList<DeckChange> changes,
            out string failure);

        Assert.False(mapped);
        Assert.Empty(changes);
        Assert.Equal(
            "Deck change at index 1 with kind 'patch-entries' is invalid: A patch-entries change must set at least " +
            "one field besides entryIds and zoneCategories.",
            failure);
    }

    /// <summary>
    /// Verifies remove-entry accepts entryIds and maps one remove per entry, and refuses both, neither, empty,
    /// duplicate, and oversized lists.
    /// </summary>
    [Fact]
    public void TryMap_RemoveEntry_AcceptsExactlyOneOfEntryIdAndEntryIds()
    {
        Guid[] ids = [.. Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7())];

        Assert.True(DeckChangeInputMapper.TryMap([new RemoveDeckEntryInput(EntryIds: ids)], out IReadOnlyList<DeckChange> changes, out _));
        Assert.Equal(ids, changes.Select(value => Assert.IsType<RemoveDeckEntryChange>(value.Value).EntryId));

        foreach (RemoveDeckEntryInput invalid in new RemoveDeckEntryInput[]
        {
            new(ids[0], ids),
            new(),
            new(EntryIds: []),
            new(EntryIds: [ids[0], ids[0]]),
            new(EntryIds: [.. Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7())]),
            new(EntryIds: [Guid.Empty]),
        })
        {
            Assert.False(DeckChangeInputMapper.TryMap([invalid], out IReadOnlyList<DeckChange> none, out string failure));
            Assert.Empty(none);
            Assert.StartsWith("Deck change at index 0 with kind 'remove-entry' requires ", failure, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies one remove-entry change removes 13 entries, and an unknown ID refuses the batch with no write.
    /// </summary>
    [Fact]
    public async Task RemoveEntry_WithEntryIds_RemovesAllOrNothing()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(14);
        Guid[] thirteen = [.. fixture.EntryIds.Take(13)];

        OperationResult<DeckWriteResult> refused = await fixture.ApplyAsync(
            new RemoveDeckEntryInput(EntryIds: [.. thirteen, Guid.CreateVersion7()]));
        Assert.IsType<OperationNotFound>(refused.Value);
        Assert.Equal(14, (await fixture.ReadAsync()).Entries.Count);

        DeckWriteResult removed = Success(await fixture.ApplyAsync(new RemoveDeckEntryInput(EntryIds: thirteen)));

        Assert.Equal(1, removed.EntryCount);
        Assert.Equal(13, removed.ChangeCounts.EntriesRemoved);
        Assert.Equal(fixture.EntryIds[13], Assert.Single((await fixture.ReadAsync()).Entries).EntryId);
    }

    /// <summary>
    /// Verifies the tool refuses a remove-entry with both or neither field as invalid-deck-change.
    /// </summary>
    [Fact]
    public async Task RemoveEntry_BothOrNeither_IsRefusedWithoutWriting()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(2);

        foreach (RemoveDeckEntryInput invalid in new RemoveDeckEntryInput[] { new(fixture.EntryIds[0], fixture.EntryIds), new() })
        {
            OperationResult<DeckWriteResult> result = await fixture.ApplyAsync(invalid);
            Assert.Equal("invalid-deck-change", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
        }

        Assert.Equal(2, (await fixture.ReadAsync()).Entries.Count);
    }

    /// <summary>
    /// Verifies a patch refused by the store is reported as invalid-deck-change through the tool, with no write.
    /// </summary>
    [Fact]
    public async Task Patch_UnknownEntryOrCategory_IsRefusedAsInvalidDeckChange()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(2);

        OperationResult<DeckWriteResult> unknownEntry = await fixture.ApplyAsync(
            new PatchDeckEntriesInput([fixture.EntryIds[0], Guid.CreateVersion7()], Quantity: 2));
        OperationResult<DeckWriteResult> unknownCategory = await fixture.ApplyAsync(
            new PatchDeckEntriesInput([fixture.EntryIds[0]], AddCategoryIds: [Guid.CreateVersion7()]));

        Assert.Equal("invalid-deck-change", Assert.IsType<OperationInvalidInput>(unknownEntry.Value).ReasonCode);
        Assert.Equal("invalid-deck-change", Assert.IsType<OperationInvalidInput>(unknownCategory.Value).ReasonCode);
        Assert.All((await fixture.ReadAsync()).Entries, entry => Assert.Equal(1, entry.Quantity));
    }

    /// <summary>
    /// Verifies a patch moves 13 maybeboard entries to the sideboard with match in one change, and the sideboard
    /// category is created in the same batch.
    /// </summary>
    [Fact]
    public async Task Patch_MovesThirteenEntriesWithMatch_InOneChange()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(13, zone: "maybeboard");

        DeckWriteResult result = Success(await fixture.ApplyAsync(
            new PatchDeckEntriesInput(fixture.EntryIds, Zone: "sideboard", ZoneCategories: "match")));

        DeckDocument deck = await fixture.ReadAsync();
        Assert.Equal(result.Revision, deck.Revision);
        Assert.All(deck.Entries, entry => Assert.Equal("sideboard", entry.Zone));
        Guid sideboard = Assert.Single(deck.Categories, value => value.Name == "Sideboard").CategoryId;
        Assert.Equal(13, deck.CategoryAssignments.Count(value => value.CategoryId == sideboard && value.IsPrimary));
        Assert.Equal(13, deck.CategoryAssignments.Count);
    }

    /// <summary>
    /// Verifies an add-entry with match and drafted categories is one change, and a skipped add assigns nothing and
    /// creates no zone category.
    /// </summary>
    [Fact]
    public async Task AddEntry_WithCategories_IsOneChangeAndSkippedAddAssignsNothing()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(1, zone: "sideboard", names: ["Card 0"]);
        Guid category = Guid.CreateVersion7();
        DeckEntryDraft duplicate = new(
            1,
            "Card 0",
            Zone: "sideboard",
            CategoryIds: [category],
            ZoneCategories: "match");

        DeckWriteResult skipped = Success(await fixture.ApplyAsync(
            DeckAddPresence.Skip,
            new AddDeckCategoryInput(new DeckCategoryDraft("Mine", CategoryId: category)),
            new AddDeckEntryInput(duplicate)));

        DeckDocument deck = await fixture.ReadAsync();
        Assert.Equal(1, skipped.EntryCount);
        Assert.Single(deck.Entries);
        Assert.Empty(deck.CategoryAssignments);
        Assert.DoesNotContain(deck.Categories, value => value.Name == "Sideboard");

        Success(await fixture.ApplyAsync(DeckAddPresence.Add, new AddDeckEntryInput(duplicate)));

        deck = await fixture.ReadAsync();
        Guid sideboard = Assert.Single(deck.Categories, value => value.Name == "Sideboard").CategoryId;
        Guid added = deck.Entries.Single(entry => entry.EntryId != fixture.EntryIds[0]).EntryId;
        Assert.Equal(
            [(category, false), (sideboard, true)],
            deck.CategoryAssignments.Where(value => value.EntryId == added)
                .OrderBy(value => value.IsPrimary).Select(value => (value.CategoryId, value.IsPrimary)));
    }

    /// <summary>
    /// Verifies a patch that names an entry whose add was skipped is refused with the existing skip explanation.
    /// </summary>
    [Fact]
    public async Task Patch_NamingASkippedAdd_IsRefused()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(1, zone: "main", names: ["Card 0"]);
        Guid skippedId = Guid.CreateVersion7();

        OperationResult<DeckWriteResult> result = await fixture.ApplyAsync(
            DeckAddPresence.Skip,
            new AddDeckEntryInput(new DeckEntryDraft(1, "Card 0", EntryId: skippedId)),
            new PatchDeckEntriesInput([skippedId], Quantity: 2));

        string message = Assert.IsType<OperationInvalidInput>(result.Value).Message;
        Assert.Contains("whenCardPresent \"skip\" left out", message, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Single((await fixture.ReadAsync()).Entries).Quantity);
    }

    /// <summary>
    /// Verifies a printing patch is looked up against each entry's own card, for a stored entry and for an entry
    /// added earlier in the same batch, and an identity mismatch is reported like add-entry reports it.
    /// </summary>
    [Fact]
    public async Task Patch_Printing_IsResolvedAgainstEachEntrysCard()
    {
        Guid oracle = Guid.CreateVersion7();
        Guid newPrinting = Guid.CreateVersion7();
        JsonObject bolt = CardJson.Make("Lightning Bolt", oracleId: oracle);
        JsonObject boltOther = CardJson.Make("Lightning Bolt", oracleId: oracle, printingId: newPrinting);
        JsonObject other = CardJson.Make("Other Card");
        using CardDataFixture data = await CardDataFixture.CreateAsync([bolt, boltOther, other]);
        using ToolFixture fixture = await ToolFixture.CreateAsync(data, 1, names: ["Lightning Bolt"]);
        Guid added = Guid.CreateVersion7();

        DeckWriteResult result = Success(await fixture.ApplyAsync(
            new AddDeckEntryInput(new DeckEntryDraft(1, "Lightning Bolt", EntryId: added)),
            new PatchDeckEntriesInput(
                [fixture.EntryIds[0], added],
                Printing: new DeckPrintingInput(SetCode: (string)boltOther["set"]!, CollectorNumber: (string)boltOther["collector_number"]!))));

        DeckDocument deck = await fixture.ReadAsync();
        Assert.All(deck.Entries, entry => Assert.Equal(newPrinting, entry.PrintingId));
        Assert.All(deck.Entries, entry => Assert.Equal("tst", entry.SetCode));
        Assert.All(deck.Entries, entry => Assert.Equal((string)boltOther["collector_number"]!, entry.CollectorNumber));
        Assert.Empty(result.Issues ?? []);

        DeckWriteResult mismatch = Success(await fixture.ApplyAsync(
            new PatchDeckEntriesInput([fixture.EntryIds[0]], Printing: new DeckPrintingInput(PrintingId: Guid.Parse((string)other["id"]!)))));

        DeckWriteIssue issue = Assert.Single(mismatch.Issues!);
        Assert.Equal(DeckWriteIssue.Unresolved, issue.Kind);
        Assert.Contains("is 'Other Card', not 'Lightning Bolt'", issue.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies local_deck_create honors drafted categories and refuses unknown ones instead of dropping them.
    /// </summary>
    [Fact]
    public async Task Create_WithDraftedCategories_HonorsOrRefuses()
    {
        using TemporaryDirectory directory = new();
        using SqliteDeckStore store = new(directory.Path, "test");
        using UninstalledCardData cardData = new();
        DeckWriteTools tools = new(store, OperationMode.Local, cardData.Resolver);
        Guid ramp = Guid.CreateVersion7();
        Guid entry = Guid.CreateVersion7();

        DeckWriteResult created = Success(await tools.CreateAsync(
            new DeckCreateRequest(
                "Drafted",
                Entries: [new DeckEntryDraft(1, "A", EntryId: entry, CategoryIds: [ramp], PrimaryCategoryId: ramp)],
                Categories: [new DeckCategoryDraft("Ramp", CategoryId: ramp)]),
            returnDeck: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(
            new DeckCategoryAssignment(entry, ramp, true),
            Assert.Single(created.Deck!.CategoryAssignments));

        OperationResult<DeckWriteResult> refused = await tools.CreateAsync(
            new DeckCreateRequest("Unknown", Entries: [new DeckEntryDraft(1, "A", CategoryIds: [Guid.CreateVersion7()])]),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<OperationInvalidInput>(refused.Value);
    }

    /// <summary>
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Owns a temporary deck of plain entries and the batch tool over it.
    /// </summary>
    private sealed class ToolFixture : IDisposable
    {
        /// <summary>
        /// Holds the folder behind the store, or null when the card data fixture owns it.
        /// </summary>
        private readonly TemporaryDirectory? directory;

        /// <summary>
        /// Holds the store the tool writes to.
        /// </summary>
        private readonly SqliteDeckStore store;

        /// <summary>
        /// Holds the card data that is not installed, when the fixture made it.
        /// </summary>
        private readonly UninstalledCardData? uninstalled;

        /// <summary>
        /// Holds the batch tool under test.
        /// </summary>
        private readonly DeckBatchWriteTools tools;

        /// <summary>
        /// Holds the deck the tests edit.
        /// </summary>
        private readonly Guid deckId;

        /// <summary>
        /// Creates a fixture around a store, a tool, and a created deck.
        /// </summary>
        private ToolFixture(
            TemporaryDirectory? directory,
            SqliteDeckStore store,
            UninstalledCardData? uninstalled,
            DeckBatchWriteTools tools,
            DeckDocument deck)
        {
            this.directory = directory;
            this.store = store;
            this.uninstalled = uninstalled;
            this.tools = tools;
            deckId = deck.DeckId;
            EntryIds = [.. deck.Entries.OrderBy(value => value.SortOrder).Select(value => value.EntryId)];
        }

        /// <summary>
        /// Gets the entry IDs in creation order.
        /// </summary>
        internal IReadOnlyList<Guid> EntryIds { get; }

        /// <summary>
        /// Creates a deck without installed card data.
        /// </summary>
        internal static async Task<ToolFixture> CreateAsync(int entries, string zone = "main", string[]? names = null)
        {
            TemporaryDirectory directory = new();
            SqliteDeckStore store = new(directory.Path, "test");
            UninstalledCardData cardData = new();
            DeckBatchWriteTools tools = new(store, OperationMode.Local, cardData.Resolver);
            return new ToolFixture(directory, store, cardData, tools, await CreateDeckAsync(store, entries, zone, names));
        }

        /// <summary>
        /// Creates a deck in the store of installed card data.
        /// </summary>
        internal static async Task<ToolFixture> CreateAsync(CardDataFixture data, int entries, string[]? names = null)
        {
            DeckBatchWriteTools tools = new(data.Store, OperationMode.Local, new DeckEntryResolver(data.Scryfall));
            return new ToolFixture(null, data.Store, null, tools, await CreateDeckAsync(data.Store, entries, "main", names));
        }

        /// <summary>
        /// Applies a batch and returns the tool outcome.
        /// </summary>
        internal Task<OperationResult<DeckWriteResult>> ApplyAsync(params DeckChangeInput[] changes)
        {
            return ApplyAsync(DeckAddPresence.Add, changes);
        }

        /// <summary>
        /// Applies a batch with the given add-presence choice at the current revision.
        /// </summary>
        internal async Task<OperationResult<DeckWriteResult>> ApplyAsync(string whenCardPresent, params DeckChangeInput[] changes)
        {
            DeckDocument current = await ReadAsync();
            return await tools.ApplyChangesAsync(
                deckId,
                current.Revision,
                changes,
                whenCardPresent: whenCardPresent,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        /// Reads the deck as stored.
        /// </summary>
        internal async Task<DeckDocument> ReadAsync()
        {
            return Success(await store.GetAsync(deckId, TestContext.Current.CancellationToken));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            uninstalled?.Dispose();
            if (directory is not null)
            {
                store.Dispose();
                directory.Dispose();
            }
        }

        /// <summary>
        /// Creates a deck of entries with caller-chosen IDs.
        /// </summary>
        private static async Task<DeckDocument> CreateDeckAsync(SqliteDeckStore store, int entries, string zone, string[]? names)
        {
            List<DeckEntryDraft> drafts = [.. Enumerable.Range(0, entries).Select(index => new DeckEntryDraft(
                1,
                names?[index] ?? $"Card {index}",
                OracleId: Guid.CreateVersion7(),
                Zone: zone,
                SortOrder: index,
                EntryId: Guid.CreateVersion7()))];
            return Success(await store.CreateAsync(
                new DeckCreateRequest("Patch tool", Format: "custom", Entries: drafts),
                TestContext.Current.CancellationToken));
        }
    }
}
