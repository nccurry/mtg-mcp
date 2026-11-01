using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Hosting;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the local deck create, delete, and backup tools, and that they enforce mode authority.
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
        using ScryfallService scryfall = new(
            temporary.Path,
            allowLocalWrites: false,
            "0.9.0-preview.1",
            ToolCallPolicy.SerializerOptions);
        DeckEntryResolver resolver = new(scryfall);
        DeckWriteTools writes = new(store, OperationMode.Local, resolver);
        DeckBatchWriteTools batchWrites = new(store, OperationMode.Local, resolver);
        DeckReadTools reads = new(
            store,
            new DeckCardViewCoordinator(store, scryfall),
            new DeckRuleCoordinator(store, scryfall));
        Guid initialEntryId = Guid.CreateVersion7();
        DeckDocument deck = RequireSuccess(await writes.CreateAsync(
            new DeckCreateRequest(
                "Workflow",
                Format: "custom",
                Entries: [new DeckEntryDraft(1, "First", EntryId: initialEntryId)]),
            returnDeck: true,
            TestContext.Current.CancellationToken)).Deck!;

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
            null,
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
    /// Verifies defense-in-depth mode enforcement rejects direct write invocation without creating storage.
    /// </summary>
    [Fact]
    public async Task ReadOnlyMode_DirectWriteInvocationReturnsUnsupported()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        using UninstalledCardData cardData = new();
        DeckWriteTools tools = new(store, OperationMode.ReadOnly, cardData.Resolver);

        OperationResult<DeckWriteResult> result = await tools.CreateAsync(
            new DeckCreateRequest("Denied"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<OperationUnsupported>(result.Value);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "decks.db")));
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
            returnDeck: true,
            cancellationToken: TestContext.Current.CancellationToken)).Deck!;
    }

    /// <summary>
    /// Extracts successful tool data or fails with the actual result case.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

}
