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
    /// Verifies a patch maps to its Core change with the printing group and the zone categories mode as given.
    /// </summary>
    [Fact]
    public void TryMap_Patch_ProducesCoreChange()
    {
        Guid entry = Guid.CreateVersion7();
        Guid printing = Guid.CreateVersion7();

        bool mapped = DeckChangeInputMapper.TryMap(
            [
                new PatchDeckEntriesInput(
                    [entry],
                    Zone: "sideboard",
                    Printing: new DeckPrintingPatch(printing),
                    ZoneCategories: " Match "),
            ],
            out IReadOnlyList<DeckChange> changes,
            out _,
            out string failure,
            out _);

        Assert.True(mapped);
        Assert.Equal(string.Empty, failure);
        PatchDeckEntriesChange patch = Assert.IsType<PatchDeckEntriesChange>(Assert.Single(changes).Value);
        Assert.Equal([entry], patch.EntryIds);
        Assert.True(DeckZoneCategories.IsMatch(patch.ZoneCategories));
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
            out _,
            out string failure,
            out _);

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

        Assert.True(DeckChangeInputMapper.TryMap(
            [new RemoveDeckEntryInput(EntryIds: ids)],
            out IReadOnlyList<DeckChange> changes,
            out IReadOnlyList<int> origins,
            out _,
            out _));
        Assert.Equal([0, 0, 0], origins);
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
            Assert.False(DeckChangeInputMapper.TryMap(
                [invalid],
                out IReadOnlyList<DeckChange> none,
                out _,
                out string failure,
                out _));
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
                Printing: new DeckPrintingPatch(
                    SetCode: (string)boltOther["set"]!,
                    CollectorNumber: (string)boltOther["collector_number"]!))));

        DeckDocument deck = await fixture.ReadAsync();
        Assert.All(deck.Entries, entry => Assert.Equal(newPrinting, entry.PrintingId));
        Assert.All(deck.Entries, entry => Assert.Equal("tst", entry.SetCode));
        Assert.All(deck.Entries, entry => Assert.Equal((string)boltOther["collector_number"]!, entry.CollectorNumber));
        Assert.Empty(result.Issues ?? []);
    }

    /// <summary>
    /// Verifies a printing group that is unresolved, belongs to another card, or names a set that matches nothing
    /// refuses the whole batch with the entries and the reason, and writes nothing.
    /// </summary>
    [Fact]
    public async Task Patch_PrintingThatDoesNotResolveForEveryEntry_RefusesTheBatch()
    {
        Guid oracle = Guid.CreateVersion7();
        JsonObject bolt = CardJson.Make("Lightning Bolt", oracleId: oracle);
        JsonObject other = CardJson.Make("Other Card");
        using CardDataFixture data = await CardDataFixture.CreateAsync([bolt, other]);
        using ToolFixture fixture = await ToolFixture.CreateAsync(data, 2, names: ["Lightning Bolt", "Other Card"]);
        DeckDocument before = await fixture.ReadAsync();
        Guid boltPrinting = CardJson.PrintingOf(bolt);

        string mismatch = await RefusedMessageAsync(
            fixture, new PatchDeckEntriesInput(fixture.EntryIds, Printing: new DeckPrintingPatch(boltPrinting)));
        string notFound = await RefusedMessageAsync(
            fixture,
            new PatchDeckEntriesInput([fixture.EntryIds[0]], Printing: new DeckPrintingPatch(Guid.CreateVersion7())));
        string noSet = await RefusedMessageAsync(
            fixture,
            new PatchDeckEntriesInput(
                [fixture.EntryIds[0]],
                Printing: new DeckPrintingPatch(SetCode: "zzz", CollectorNumber: "99")));

        Assert.StartsWith(
            "Change 1 is a patch-entries whose printing cannot be applied: ",
            mismatch,
            StringComparison.Ordinal);
        Assert.Contains(
            $"entry {fixture.EntryIds[1]} ('Other Card'): Printing {boltPrinting} is 'Lightning Bolt', not " +
            "'Other Card'.",
            mismatch,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"entry {fixture.EntryIds[0]}", mismatch, StringComparison.Ordinal);
        Assert.Contains("was not found in installed card data.", notFound, StringComparison.Ordinal);
        Assert.Contains("Set zzz number 99 was not found in installed card data.", noSet, StringComparison.Ordinal);
        Assert.All(
            [mismatch, notFound, noSet],
            message => Assert.EndsWith(" Nothing was written.", message, StringComparison.Ordinal));
        Assert.Equal(before.Entries, (await fixture.ReadAsync()).Entries);
        Assert.Equal(before.Revision, (await fixture.ReadAsync()).Revision);
    }

    /// <summary>
    /// Verifies a printing patch of an entry added earlier in the batch is refused when the printing belongs to
    /// another card, naming the caller's change number.
    /// </summary>
    [Fact]
    public async Task Patch_PrintingForAnEntryAddedEarlierInTheBatch_IsRefusedOnMismatch()
    {
        JsonObject bolt = CardJson.Make("Lightning Bolt");
        JsonObject other = CardJson.Make("Other Card");
        using CardDataFixture data = await CardDataFixture.CreateAsync([bolt, other]);
        using ToolFixture fixture = await ToolFixture.CreateAsync(data, 1, names: ["Lightning Bolt"]);
        Guid added = Guid.CreateVersion7();

        string message = await RefusedMessageAsync(
            fixture,
            new AddDeckEntryInput(new DeckEntryDraft(1, "Other Card", EntryId: added)),
            new PatchDeckEntriesInput([added], Printing: new DeckPrintingPatch(CardJson.PrintingOf(bolt))));

        Assert.StartsWith("Change 2 is a patch-entries", message, StringComparison.Ordinal);
        Assert.Contains($"entry {added} ('Other Card')", message, StringComparison.Ordinal);
        Assert.Single((await fixture.ReadAsync()).Entries);
    }

    /// <summary>
    /// Verifies a printing patch is refused when no card data is installed, because it cannot be checked.
    /// </summary>
    [Fact]
    public async Task Patch_PrintingWithoutInstalledCardData_IsRefused()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(1);

        string message = await RefusedMessageAsync(
            fixture,
            new PatchDeckEntriesInput([fixture.EntryIds[0]], Printing: new DeckPrintingPatch(Guid.CreateVersion7())));

        Assert.Contains("Scryfall card data is not installed or cannot be read", message, StringComparison.Ordinal);
        Assert.Contains($"entry {fixture.EntryIds[0]}", message, StringComparison.Ordinal);
        Assert.Null((await fixture.ReadAsync()).Entries.Single().PrintingId);
    }

    /// <summary>
    /// Verifies a refusal names the change the caller wrote when an earlier remove-entry expands to many changes.
    /// </summary>
    [Fact]
    public async Task Refusal_AfterAThirteenIdRemoval_NamesTheCallersChangeNumbers()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(14);
        Guid skippedId = Guid.CreateVersion7();

        OperationResult<DeckWriteResult> result = await fixture.ApplyAsync(
            DeckAddPresence.Skip,
            new RemoveDeckEntryInput(EntryIds: [.. fixture.EntryIds.Take(13)]),
            new AddDeckEntryInput(new DeckEntryDraft(1, "Card 13", EntryId: skippedId)),
            new PatchDeckEntriesInput([skippedId], Quantity: 2));

        string message = Assert.IsType<OperationInvalidInput>(result.Value).Message;
        Assert.StartsWith("Change 3 refers to the entry of change 2, ", message, StringComparison.Ordinal);
        Assert.Equal(14, (await fixture.ReadAsync()).Entries.Count);
    }

    /// <summary>
    /// Verifies skipping applies to the zone an entry ends in: a patch before the add that moves the card into the
    /// zone skips the add, a patch after it that moves the card out keeps the add, and a patch that names both a
    /// skipped add and an existing entry is refused.
    /// </summary>
    [Fact]
    public async Task Skip_WithPatches_UsesTheZoneTheCardEndsIn()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(1, names: ["Card 0"]);
        Guid existing = fixture.EntryIds[0];
        DeckEntryDraft inSideboard = new(1, "Card 0", Zone: "sideboard");
        DeckEntryDraft inMain = new(1, "Card 0", Zone: "main");

        DeckWriteResult skipped = Success(await fixture.ApplyAsync(
            DeckAddPresence.Skip,
            new PatchDeckEntriesInput([existing], Zone: "sideboard", ZoneCategories: "leave"),
            new AddDeckEntryInput(inSideboard)));
        Assert.Equal(1, skipped.EntryCount);
        Assert.Contains(
            skipped.Issues!,
            issue => issue.Message.Contains("is already in zone sideboard", StringComparison.Ordinal));

        DeckWriteResult kept = Success(await fixture.ApplyAsync(
            DeckAddPresence.Skip,
            new AddDeckEntryInput(inSideboard),
            new PatchDeckEntriesInput([existing], Zone: "main", ZoneCategories: "leave")));
        Assert.Equal(2, kept.EntryCount);
        Assert.DoesNotContain(
            kept.Issues ?? [],
            issue => issue.Message.EndsWith("; not added.", StringComparison.Ordinal));

        Guid skippedId = Guid.CreateVersion7();
        OperationResult<DeckWriteResult> mixed = await fixture.ApplyAsync(
            DeckAddPresence.Skip,
            new AddDeckEntryInput(inMain with { EntryId = skippedId }),
            new PatchDeckEntriesInput([existing, skippedId], Quantity: 2));
        Assert.StartsWith(
            "Change 2 refers to the entry of change 1, ",
            Assert.IsType<OperationInvalidInput>(mixed.Value).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies remove-entry with only Guid.Empty, a repeated removal across changes, and an unknown ID are refused
    /// with their reason, while exactly 100 IDs and an entry added earlier in the batch are accepted.
    /// </summary>
    [Fact]
    public async Task RemoveEntry_Boundaries_AreRefusedOrAcceptedWithReasons()
    {
        using ToolFixture fixture = await ToolFixture.CreateAsync(100);

        OperationInvalidInput empty = Assert.IsType<OperationInvalidInput>(
            (await fixture.ApplyAsync(new RemoveDeckEntryInput(EntryIds: [Guid.Empty]))).Value);
        OperationNotFound twice = Assert.IsType<OperationNotFound>((await fixture.ApplyAsync(
            new RemoveDeckEntryInput(fixture.EntryIds[0]),
            new RemoveDeckEntryInput(fixture.EntryIds[0]))).Value);
        OperationNotFound unknown = Assert.IsType<OperationNotFound>(
            (await fixture.ApplyAsync(new RemoveDeckEntryInput(Guid.CreateVersion7()))).Value);

        Assert.Equal("invalid-deck-change", empty.ReasonCode);
        Assert.Equal(
            "Deck change at index 0 with kind 'remove-entry' requires entryIds with 1 to 100 distinct, non-empty IDs.",
            empty.Message);
        Assert.Equal("deck-entry-not-found", twice.ReasonCode);
        Assert.Equal(twice.ReasonCode, unknown.ReasonCode);
        Assert.Equal("The deck entry was not found.", unknown.Message);
        Assert.Equal(100, (await fixture.ReadAsync()).Entries.Count);

        Guid added = Guid.CreateVersion7();
        DeckWriteResult addedRemoved = Success(await fixture.ApplyAsync(
            new AddDeckEntryInput(new DeckEntryDraft(1, "Temporary", EntryId: added)),
            new RemoveDeckEntryInput(added)));
        Assert.Equal(100, addedRemoved.EntryCount);

        DeckWriteResult all = Success(await fixture.ApplyAsync(new RemoveDeckEntryInput(EntryIds: fixture.EntryIds)));
        Assert.Equal(0, all.EntryCount);
        Assert.Equal(100, all.ChangeCounts.EntriesRemoved);
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
    /// Applies a batch that must be refused as invalid-deck-change and returns the message.
    /// </summary>
    private static async Task<string> RefusedMessageAsync(ToolFixture fixture, params DeckChangeInput[] changes)
    {
        OperationInvalidInput refused = Assert.IsType<OperationInvalidInput>((await fixture.ApplyAsync(changes)).Value);
        Assert.Equal("invalid-deck-change", refused.ReasonCode);
        return refused.Message;
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
