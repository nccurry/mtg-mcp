using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies opaque backup creation, corruption refusal, guarded restore, and deletion.
/// </summary>
public sealed class DeckBackupStoreTests
{
    /// <summary>
    /// Verifies absent storage and malformed opaque identifiers remain distinct outcomes.
    /// </summary>
    [Fact]
    public async Task BackupOperations_WithoutStorage_ReturnEmptyInvalidOrNotFound()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");

        DeckBackupPage page = RequireSuccess(
            await store.Backups.ListAsync(TestContext.Current.CancellationToken));
        OperationResult<DeckRestoreResult> invalidRestore = await store.Backups.RestoreAsync(
            Guid.Empty,
            string.Empty,
            TestContext.Current.CancellationToken);
        OperationResult<DeckRestoreResult> missingDatabase = await store.Backups.RestoreAsync(
            Guid.CreateVersion7(),
            "v1:current",
            TestContext.Current.CancellationToken);
        OperationResult<DeckDeleteResult> invalidDelete = await store.Backups.DeleteAsync(
            Guid.Empty,
            TestContext.Current.CancellationToken);
        OperationResult<DeckDeleteResult> missingDelete = await store.Backups.DeleteAsync(
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        Assert.Null(page.CurrentDatabaseFingerprint);
        Assert.Empty(page.Items);
        Assert.IsType<OperationInvalidInput>(invalidRestore.Value);
        Assert.IsType<OperationNotFound>(missingDatabase.Value);
        Assert.IsType<OperationInvalidInput>(invalidDelete.Value);
        Assert.IsType<OperationNotFound>(missingDelete.Value);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "decks.db")));
    }

    /// <summary>
    /// Verifies restore preserves newer bytes as a rollback backup and restores prior content.
    /// </summary>
    [Fact]
    public async Task BackupRestore_WithCurrentFingerprint_RestoresAndRetainsRollback()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument original = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Original", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckBackup backup = RequireSuccess(
            await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        _ = RequireSuccess(await store.ApplyChangesAsync(
            original.DeckId,
            original.Revision,
            [new UpdateDeckMetadataChange("Changed", null, "custom")],
            TestContext.Current.CancellationToken));
        DeckBackupPage beforeRestore = RequireSuccess(
            await store.Backups.ListAsync(TestContext.Current.CancellationToken));

        DeckRestoreResult restored = RequireSuccess(await store.Backups.RestoreAsync(
            backup.BackupId,
            beforeRestore.CurrentDatabaseFingerprint!,
            TestContext.Current.CancellationToken));
        DeckDocument loaded = RequireSuccess(
            await store.GetAsync(original.DeckId, TestContext.Current.CancellationToken));
        DeckBackupPage afterRestore = RequireSuccess(
            await store.Backups.ListAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Original", loaded.Name);
        Assert.Equal(backup.Fingerprint, restored.CurrentDatabaseFingerprint);
        Assert.Contains(afterRestore.Items, value =>
            value.BackupId == restored.RollbackBackupId && value.IsRollback);
    }

    /// <summary>
    /// Verifies a stale fingerprint refuses restore without changing the current deck.
    /// </summary>
    [Fact]
    public async Task BackupRestore_WithStaleFingerprint_RefusesWithoutMutation()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument original = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Original", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckBackup backup = RequireSuccess(
            await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        _ = RequireSuccess(await store.ApplyChangesAsync(
            original.DeckId,
            original.Revision,
            [new UpdateDeckMetadataChange("Current", null, "custom")],
            TestContext.Current.CancellationToken));

        OperationResult<DeckRestoreResult> result = await store.Backups.RestoreAsync(
            backup.BackupId,
            "v1:stale",
            TestContext.Current.CancellationToken);
        DeckDocument current = RequireSuccess(
            await store.GetAsync(original.DeckId, TestContext.Current.CancellationToken));

        Assert.IsType<OperationConflict>(result.Value);
        Assert.Equal("Current", current.Name);
    }

    /// <summary>
    /// Verifies manifest or database tampering is rejected before the current database is swapped.
    /// </summary>
    [Fact]
    public async Task BackupRestore_WithTamperedBytes_ReturnsCorruptAndPreservesCurrentDatabase()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument original = RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Current", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckBackup backup = RequireSuccess(
            await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        DeckBackupPage inventory = RequireSuccess(
            await store.Backups.ListAsync(TestContext.Current.CancellationToken));
        string backupPath = System.IO.Path.Combine(
            temporary.Path,
            "backups",
            "decks",
            backup.BackupId.ToString("N") + ".db");
        await File.AppendAllTextAsync(
            backupPath,
            "tampered",
            TestContext.Current.CancellationToken);

        OperationResult<DeckRestoreResult> result = await store.Backups.RestoreAsync(
            backup.BackupId,
            inventory.CurrentDatabaseFingerprint!,
            TestContext.Current.CancellationToken);
        DeckDocument current = RequireSuccess(
            await store.GetAsync(original.DeckId, TestContext.Current.CancellationToken));

        Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal("Current", current.Name);
    }

    /// <summary>
    /// Verifies backup deletion uses only the opaque ID and removes it from inventory.
    /// </summary>
    [Fact]
    public async Task BackupDelete_RemovesOpaqueBackupPair()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckBackup backup = RequireSuccess(
            await store.Backups.CreateAsync(TestContext.Current.CancellationToken));

        DeckDeleteResult deleted = RequireSuccess(
            await store.Backups.DeleteAsync(backup.BackupId, TestContext.Current.CancellationToken));
        DeckBackupPage inventory = RequireSuccess(
            await store.Backups.ListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(backup.BackupId, deleted.DeletedId);
        Assert.Empty(inventory.Items);
    }

    /// <summary>
    /// Extracts a successful result or fails the test with its actual union case.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
