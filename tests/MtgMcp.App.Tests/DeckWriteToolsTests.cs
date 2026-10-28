using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies MCP deck writes enforce mode authority, and that the batch tool covers every deck edit that the
/// removed single-step tools used to make.
/// </summary>
public sealed class DeckWriteToolsTests
{
    /// <summary>
    /// Exercises the create, edit, backup, restore, and delete tools as one coherent local workflow.
    /// </summary>
    [Fact]
    public async Task LocalTools_CompleteCrudBackupAndRestoreWorkflow()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckWriteTools writes = new(store, OperationMode.Local);
        DeckBatchWriteTools batchWrites = new(store, OperationMode.Local);
        using ScryfallService scryfall = new(
            temporary.Path,
            allowLocalWrites: false,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions);
        DeckReadTools reads = new(store, new DeckCardViewCoordinator(store, scryfall));
        Guid initialEntryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await writes.CreateAsync(
            new DeckCreateRequest(
                "Workflow",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "First", EntryId: initialEntryId)]),
            TestContext.Current.CancellationToken));

        deck = await ApplyAsync(
            batchWrites,
            deck,
            new UpdateDeckMetadataInput("Workflow Updated", "Local evidence", "custom"));
        deck = await ApplyAsync(batchWrites, deck, new AddDeckEntryInput(new DeckEntryDraft(1, "Second")));
        DeckEntry second = Assert.Single(deck.Entries, value => value.CardName == "Second");
        deck = await ApplyAsync(
            batchWrites,
            deck,
            new UpdateDeckEntryInput(second with { Quantity = 2, Zone = "sideboard" }));
        deck = await ApplyAsync(batchWrites, deck, new RemoveDeckEntryInput(second.EntryId));
        deck = await ApplyAsync(batchWrites, deck, new AddDeckCategoryInput(new DeckCategoryDraft("Ramp")));
        DeckCategory category = Assert.Single(deck.Categories);
        deck = await ApplyAsync(
            batchWrites,
            deck,
            new UpdateDeckCategoryInput(category with { Name = "Acceleration", Color = "#123456" }));
        deck = await ApplyAsync(
            batchWrites,
            deck,
            new AssignDeckCategoryInput(initialEntryId, category.CategoryId, true));
        deck = await ApplyAsync(
            batchWrites,
            deck,
            new UnassignDeckCategoryInput(initialEntryId, category.CategoryId));
        Guid bindingId = Guid.CreateVersion7();
        deck = await ApplyAsync(
            batchWrites,
            deck,
            new UpsertDeckProviderBindingInput(
                new DeckProviderBinding(bindingId, "Example", "remote-1", null, "v1", "baseline-1", null, null),
                "{\"name\":\"Workflow Updated\"}"),
            new RemoveDeckProviderBindingInput(bindingId));
        deck = await ApplyAsync(batchWrites, deck, new RemoveDeckCategoryInput(category.CategoryId));

        Assert.Single(RequireSuccess(await reads.ListAsync(
            cancellationToken: TestContext.Current.CancellationToken)).Items);
        DeckDocumentView read = RequireSuccess(await reads.GetAsync(
            deck.DeckId,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(deck.DeckId, read.DeckId);
        Assert.Equal(deck.Revision, read.Revision);
        Assert.True(RequireSuccess(await reads.ValidateAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken)).IsStructurallyValid);
        DeckBackup backup = RequireSuccess(
            await writes.CreateBackupAsync(TestContext.Current.CancellationToken));
        DeckBackupPage inventory = RequireSuccess(
            await reads.ListBackupsAsync(TestContext.Current.CancellationToken));
        Assert.Contains(inventory.Items, value => value.BackupId == backup.BackupId);
        DeckDocument changed = await ApplyAsync(
            batchWrites,
            deck,
            new UpdateDeckMetadataInput("Changed After Backup", null, "custom"));
        Assert.NotEqual(deck.Name, changed.Name);
        DeckBackupPage changedInventory = RequireSuccess(
            await reads.ListBackupsAsync(TestContext.Current.CancellationToken));
        DeckRestoreResult restored = RequireSuccess(await writes.RestoreBackupAsync(
            backup.BackupId,
            changedInventory.CurrentDatabaseFingerprint!,
            TestContext.Current.CancellationToken));
        Assert.Equal(backup.Fingerprint, restored.CurrentDatabaseFingerprint);
        Assert.Equal(backup.BackupId, RequireSuccess(await writes.DeleteBackupAsync(
            backup.BackupId,
            TestContext.Current.CancellationToken)).DeletedId);
        Assert.Equal(deck.DeckId, RequireSuccess(await writes.DeleteAsync(
            deck.DeckId,
            deck.Revision,
            TestContext.Current.CancellationToken)).DeletedId);
    }

    /// <summary>
    /// Verifies a single update-metadata change replaces the name, description, and format (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UpdateMetadata_ReplacesDeckFields()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(
            new UpdateDeckMetadataInput("Renamed", "Description", "future-format"));

        Assert.Equal(("Renamed", "Description", "future-format"), (deck.Name, deck.Description, deck.Format));
        Assert.Equal(fixture.Deck.Revision + 1, deck.Revision);
    }

    /// <summary>
    /// Verifies a single add-entry change adds an independently addressable entry (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AddEntry_AddsEntryWithoutMergingEquivalentCards()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(new AddDeckEntryInput(new DeckEntryDraft(1, "Card")));

        Assert.Equal(["Card", "Card"], deck.Entries.Select(entry => entry.CardName));
    }

    /// <summary>
    /// Verifies a single update-entry change replaces quantity, finish, and zone (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UpdateEntry_ReplacesEntryFields()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();
        DeckEntry updated = Assert.Single(fixture.Deck.Entries) with
        {
            Quantity = 2,
            Finish = "foil",
            Zone = "sideboard",
        };

        DeckDocument deck = await fixture.ApplyAsync(new UpdateDeckEntryInput(updated));

        Assert.Equal(updated, Assert.Single(deck.Entries));
    }

    /// <summary>
    /// Verifies a single remove-entry change removes the entry and its category assignments (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_RemoveEntry_RemovesEntryAndItsAssignments()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withAssignment: true);

        DeckDocument deck = await fixture.ApplyAsync(new RemoveDeckEntryInput(fixture.EntryId));

        Assert.Empty(deck.Entries);
        Assert.Empty(deck.CategoryAssignments);
        Assert.Single(deck.Categories);
    }

    /// <summary>
    /// Verifies a single add-category change adds a category without changing entries (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AddCategory_AddsCategory()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withCategory: false);
        Guid categoryId = Guid.CreateVersion7();

        DeckDocument deck = await fixture.ApplyAsync(
            new AddDeckCategoryInput(new DeckCategoryDraft("Draw", "#123456", 7, categoryId)));

        DeckCategory category = Assert.Single(deck.Categories);
        Assert.Equal((categoryId, "Draw", "#123456"), (category.CategoryId, category.Name, category.Color));
        Assert.Single(deck.Entries);
    }

    /// <summary>
    /// Verifies a single update-category change replaces the category name (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UpdateCategory_ReplacesCategoryFields()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();
        DeckCategory updated = Assert.Single(fixture.Deck.Categories) with { Name = "Mana" };

        DeckDocument deck = await fixture.ApplyAsync(new UpdateDeckCategoryInput(updated));

        Assert.Equal("Mana", Assert.Single(deck.Categories).Name);
    }

    /// <summary>
    /// Verifies a single remove-category change removes the category and its assignments but keeps entries
    /// (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_RemoveCategory_KeepsEntries()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withAssignment: true);

        DeckDocument deck = await fixture.ApplyAsync(new RemoveDeckCategoryInput(fixture.CategoryId));

        Assert.Empty(deck.Categories);
        Assert.Empty(deck.CategoryAssignments);
        Assert.Single(deck.Entries);
    }

    /// <summary>
    /// Verifies a single assign-category change adds a primary assignment (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_AssignCategory_AddsPrimaryAssignment()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(
            new AssignDeckCategoryInput(fixture.EntryId, fixture.CategoryId, true));

        Assert.Equal(
            new DeckCategoryAssignment(fixture.EntryId, fixture.CategoryId, true),
            Assert.Single(deck.CategoryAssignments));
    }

    /// <summary>
    /// Verifies a single unassign-category change removes the assignment but keeps entry and category (SURF-005).
    /// </summary>
    [Fact]
    public async Task ApplyChanges_UnassignCategory_KeepsEntryAndCategory()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync(withAssignment: true);

        DeckDocument deck = await fixture.ApplyAsync(
            new UnassignDeckCategoryInput(fixture.EntryId, fixture.CategoryId));

        Assert.Empty(deck.CategoryAssignments);
        Assert.Single(deck.Entries);
        Assert.Single(deck.Categories);
    }

    /// <summary>
    /// Verifies one batch of several changes applies in one revision.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_SeveralChanges_ApplyInOneRevision()
    {
        using BatchFixture fixture = await BatchFixture.CreateAsync();

        DeckDocument deck = await fixture.ApplyAsync(
            new AddDeckEntryInput(new DeckEntryDraft(1, "Second")),
            new UpdateDeckMetadataInput("Batch", null, "custom"));

        Assert.Equal(2, deck.Entries.Count);
        Assert.Equal("Batch", deck.Name);
        Assert.Equal(fixture.Deck.Revision + 1, deck.Revision);
    }

    /// <summary>
    /// Verifies defense-in-depth mode enforcement rejects direct write invocation without creating storage.
    /// </summary>
    [Fact]
    public async Task ReadOnlyMode_DirectWriteInvocationReturnsUnsupported()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckWriteTools tools = new(store, OperationMode.ReadOnly);

        OperationResult<DeckDocument> result = await tools.CreateAsync(
            new DeckCreateRequest("Denied"),
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationUnsupported>(result.Value);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Verifies the batch tool refuses to run in read-only mode.
    /// </summary>
    [Fact]
    public async Task ReadOnlyMode_BatchInvocationReturnsUnsupported()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckBatchWriteTools tools = new(store, OperationMode.ReadOnly);

        OperationResult<DeckDocument> result = await tools.ApplyChangesAsync(
            Guid.CreateVersion7(),
            1,
            [new UpdateDeckMetadataInput("Denied", null, "custom")],
            TestContext.Current.CancellationToken);

        Assert.Equal("operation-mode-denied", Assert.IsType<OperationUnsupported>(result.Value).ReasonCode);
    }

    /// <summary>
    /// Applies changes to one deck through the batch tool and returns the new deck state.
    /// </summary>
    private static async Task<DeckDocument> ApplyAsync(
        DeckBatchWriteTools tools,
        DeckDocument deck,
        params DeckChangeInput[] changes)
    {
        return RequireSuccess(await tools.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            changes,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Holds a temporary store with one deck, one entry, and optionally one category for single-change tests.
    /// </summary>
    private sealed class BatchFixture : IDisposable
    {
        /// <summary>
        /// Owns the folder that holds the deck database.
        /// </summary>
        private readonly TemporaryDirectory directory;

        /// <summary>
        /// Owns the deck store behind the batch tool.
        /// </summary>
        private readonly SqliteDeckStore store;

        /// <summary>
        /// Applies changes through the batch tool.
        /// </summary>
        private readonly DeckBatchWriteTools tools;

        /// <summary>
        /// Wraps the created deck and the store that holds it.
        /// </summary>
        private BatchFixture(
            TemporaryDirectory directory,
            SqliteDeckStore store,
            DeckDocument deck,
            Guid entryId,
            Guid categoryId)
        {
            this.directory = directory;
            this.store = store;
            tools = new DeckBatchWriteTools(store, OperationMode.Local);
            Deck = deck;
            EntryId = entryId;
            CategoryId = categoryId;
        }

        /// <summary>
        /// Gets the deck as created, before any change.
        /// </summary>
        internal DeckDocument Deck { get; }

        /// <summary>
        /// Gets the stable ID of the deck's one entry.
        /// </summary>
        internal Guid EntryId { get; }

        /// <summary>
        /// Gets the stable ID of the category, which exists only when the fixture was created with one.
        /// </summary>
        internal Guid CategoryId { get; }

        /// <summary>
        /// Creates a deck with one entry, optionally with one category and an assignment of the entry to it.
        /// </summary>
        internal static async Task<BatchFixture> CreateAsync(bool withCategory = true, bool withAssignment = false)
        {
            TemporaryDirectory directory = new();
            SqliteDeckStore store = new(directory.Path, "0.9.0-preview.1");
            Guid entryId = Guid.CreateVersion7();
            Guid categoryId = Guid.CreateVersion7();
            DeckCreateRequest request = new(
                "Fixture",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "Card", EntryId: entryId)],
                Categories: withCategory ? [new DeckCategoryDraft("Ramp", CategoryId: categoryId)] : null,
                CategoryAssignments: withCategory && withAssignment
                    ? [new DeckCategoryAssignment(entryId, categoryId, false)]
                    : null);
            DeckDocument deck = RequireSuccess(
                await store.CreateAsync(request, TestContext.Current.CancellationToken));
            return new BatchFixture(directory, store, deck, entryId, categoryId);
        }

        /// <summary>
        /// Applies one batch to the fixture deck at its created revision.
        /// </summary>
        internal Task<DeckDocument> ApplyAsync(params DeckChangeInput[] changes)
        {
            return DeckWriteToolsTests.ApplyAsync(tools, Deck, changes);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            store.Dispose();
            directory.Dispose();
        }
    }
}
