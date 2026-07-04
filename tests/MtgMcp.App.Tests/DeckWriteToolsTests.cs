using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies MCP deck writes enforce mode authority and share one transaction vocabulary.
/// </summary>
public sealed class DeckWriteToolsTests
{
    /// <summary>
    /// Exercises every deck wrapper as one coherent local workflow over the shared store.
    /// </summary>
    [Fact]
    public async Task LocalTools_CompleteCrudBackupAndRestoreWorkflow()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckWriteTools writes = new(store, OperationMode.Local);
        DeckReadTools reads = new(store);
        Guid initialEntryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await writes.CreateAsync(
            new DeckCreateRequest(
                "Workflow",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "First", EntryId: initialEntryId)]),
            TestContext.Current.CancellationToken));

        deck = RequireSuccess(await writes.UpdateAsync(
            deck.DeckId,
            deck.Revision,
            "Workflow Updated",
            "Local evidence",
            "custom",
            TestContext.Current.CancellationToken));
        deck = RequireSuccess(await writes.AddEntryAsync(
            deck.DeckId,
            deck.Revision,
            new DeckEntryDraft(1, "Second"),
            TestContext.Current.CancellationToken));
        DeckEntry second = Assert.Single(deck.Entries, value => value.CardName == "Second");
        deck = RequireSuccess(await writes.UpdateEntryAsync(
            deck.DeckId,
            deck.Revision,
            second with { Quantity = 2, Zone = "sideboard" },
            TestContext.Current.CancellationToken));
        deck = RequireSuccess(await writes.RemoveEntryAsync(
            deck.DeckId,
            deck.Revision,
            second.EntryId,
            TestContext.Current.CancellationToken));
        deck = RequireSuccess(await writes.CreateCategoryAsync(
            deck.DeckId,
            deck.Revision,
            new DeckCategoryDraft("Ramp"),
            TestContext.Current.CancellationToken));
        DeckCategory category = Assert.Single(deck.Categories);
        deck = RequireSuccess(await writes.UpdateCategoryAsync(
            deck.DeckId,
            deck.Revision,
            category with { Name = "Acceleration", Color = "#123456" },
            TestContext.Current.CancellationToken));
        deck = RequireSuccess(await writes.AssignCategoryAsync(
            deck.DeckId,
            deck.Revision,
            initialEntryId,
            category.CategoryId,
            isPrimary: true,
            TestContext.Current.CancellationToken));
        deck = RequireSuccess(await writes.UnassignCategoryAsync(
            deck.DeckId,
            deck.Revision,
            initialEntryId,
            category.CategoryId,
            TestContext.Current.CancellationToken));
        Guid bindingId = Guid.CreateVersion7();
        deck = RequireSuccess(await writes.ApplyChangesAsync(
            deck.DeckId,
            deck.Revision,
            [
                new DeckChangeInput(
                    "upsert-provider-binding",
                    ProviderBinding: new DeckProviderBinding(
                        bindingId,
                        "Example",
                        "remote-1",
                        null,
                        "v1",
                        "baseline-1",
                        null,
                        null),
                    CanonicalBaseline: "{\"name\":\"Workflow Updated\"}"),
                new DeckChangeInput("remove-provider-binding", BindingId: bindingId),
            ],
            TestContext.Current.CancellationToken));
        deck = RequireSuccess(await writes.DeleteCategoryAsync(
            deck.DeckId,
            deck.Revision,
            category.CategoryId,
            TestContext.Current.CancellationToken));

        Assert.Single(RequireSuccess(await reads.ListAsync(
            cancellationToken: TestContext.Current.CancellationToken)).Items);
        DeckDocument read = RequireSuccess(await reads.GetAsync(
            deck.DeckId,
            TestContext.Current.CancellationToken));
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
        DeckDocument changed = RequireSuccess(await writes.UpdateAsync(
            deck.DeckId,
            deck.Revision,
            "Changed After Backup",
            null,
            "custom",
            TestContext.Current.CancellationToken));
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
    /// Verifies every granular union mutation matches its one-operation batch form.
    /// </summary>
    [Fact]
    public async Task GranularAndBatchMutations_ProduceEquivalentState()
    {
        using TemporaryDirectory firstDirectory = new();
        using TemporaryDirectory secondDirectory = new();
        using SqliteDeckStore firstStore = new(firstDirectory.Path, "0.9.0-preview.1");
        using SqliteDeckStore secondStore = new(secondDirectory.Path, "0.9.0-preview.1");
        Guid deckId = Guid.CreateVersion7();
        Guid entryId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        DeckDocument first = RequireSuccess(await firstStore.CreateAsync(
            new DeckCreateRequest("Deck", Format: "custom", DeckId: deckId),
            TestContext.Current.CancellationToken));
        DeckDocument second = RequireSuccess(await secondStore.CreateAsync(
            new DeckCreateRequest("Deck", Format: "custom", DeckId: deckId),
            TestContext.Current.CancellationToken));
        DeckWriteTools firstTools = new(firstStore, OperationMode.Local);
        DeckWriteTools secondTools = new(secondStore, OperationMode.Local);
        first = RequireSuccess(await firstTools.UpdateAsync(
            deckId,
            first.Revision,
            "Renamed",
            "Description",
            "future-format",
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput(
                "update-metadata",
                Name: "Renamed",
                Description: "Description",
                Format: "future-format")],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        DeckEntryDraft entryDraft = new(1, "Card", EntryId: entryId);
        first = RequireSuccess(await firstTools.AddEntryAsync(
            deckId,
            first.Revision,
            entryDraft,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput("add-entry", EntryDraft: entryDraft)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        DeckEntry updatedEntry = Assert.Single(first.Entries) with
        {
            Quantity = 2,
            Finish = "foil",
            Zone = "sideboard",
        };
        first = RequireSuccess(await firstTools.UpdateEntryAsync(
            deckId,
            first.Revision,
            updatedEntry,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput("update-entry", Entry: updatedEntry)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        DeckCategoryDraft categoryDraft = new("Ramp", "#123456", 7, categoryId);
        first = RequireSuccess(await firstTools.CreateCategoryAsync(
            deckId,
            first.Revision,
            categoryDraft,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput("add-category", CategoryDraft: categoryDraft)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        DeckCategory updatedCategory = Assert.Single(first.Categories) with { Name = "Mana" };
        first = RequireSuccess(await firstTools.UpdateCategoryAsync(
            deckId,
            first.Revision,
            updatedCategory,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput("update-category", Category: updatedCategory)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        first = RequireSuccess(await firstTools.AssignCategoryAsync(
            deckId,
            first.Revision,
            entryId,
            categoryId,
            isPrimary: true,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput(
                "assign-category",
                EntryId: entryId,
                CategoryId: categoryId,
                IsPrimary: true)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        first = RequireSuccess(await firstTools.UnassignCategoryAsync(
            deckId,
            first.Revision,
            entryId,
            categoryId,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput(
                "unassign-category",
                EntryId: entryId,
                CategoryId: categoryId)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        first = RequireSuccess(await firstTools.DeleteCategoryAsync(
            deckId,
            first.Revision,
            categoryId,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput("remove-category", CategoryId: categoryId)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);

        first = RequireSuccess(await firstTools.RemoveEntryAsync(
            deckId,
            first.Revision,
            entryId,
            TestContext.Current.CancellationToken));
        second = RequireSuccess(await secondTools.ApplyChangesAsync(
            deckId,
            second.Revision,
            [new DeckChangeInput("remove-entry", EntryId: entryId)],
            TestContext.Current.CancellationToken));
        AssertEquivalent(first, second);
    }

    /// <summary>
    /// Compares the caller-visible state controlled by one deck transaction.
    /// </summary>
    private static void AssertEquivalent(DeckDocument first, DeckDocument second)
    {
        Assert.Equal(first.DeckId, second.DeckId);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Description, second.Description);
        Assert.Equal(first.Format, second.Format);
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(first.Entries, second.Entries);
        Assert.Equal(first.Categories, second.Categories);
        Assert.Equal(first.CategoryAssignments, second.CategoryAssignments);
        Assert.Equal(first.ProviderBindings, second.ProviderBindings);
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
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
