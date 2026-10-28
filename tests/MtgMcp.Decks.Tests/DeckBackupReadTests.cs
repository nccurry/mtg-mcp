using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies one deck can be read from a backup without changing the backup or the live database (SET-008, cases
/// C09, C14, and C15).
/// </summary>
public sealed class DeckBackupReadTests
{
    /// <summary>
    /// Verifies the backup content is returned even after the deck changed, and the live database file is not
    /// touched (case C09).
    /// </summary>
    [Fact]
    public async Task ReadDeck_AfterTheDeckChanged_ReturnsTheBackedUpContentAndLeavesTheDatabaseAlone()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        DeckDocument original = Success(await store.CreateAsync(
            new DeckCreateRequest("Before", Format: "custom", Entries: [new DeckEntryDraft(1, "Sol Ring")]),
            TestContext.Current.CancellationToken));
        DeckBackup backup = Success(await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        _ = Success(await store.ApplyChangesAsync(
            original.DeckId,
            original.Revision,
            [new UpdateDeckMetadataChange("After", string.Empty, "custom")],
            TestContext.Current.CancellationToken));
        string liveBefore = HashOf(Path.Combine(temporary.Path, "decks.db"));
        string backupBefore = HashOf(BackupPath(temporary.Path, backup.BackupId));

        DeckDocument read = Success(await store.Backups.ReadDeckAsync(
            backup.BackupId,
            original.DeckId,
            TestContext.Current.CancellationToken));

        Assert.Equal("Before", read.Name);
        Assert.Equal(original.Revision, read.Revision);
        Assert.Equal("Sol Ring", Assert.Single(read.Entries).CardName);
        Assert.Equal(liveBefore, HashOf(Path.Combine(temporary.Path, "decks.db")));
        Assert.Equal(backupBefore, HashOf(BackupPath(temporary.Path, backup.BackupId)));
    }

    /// <summary>
    /// Verifies a backup with another schema version is unsupported, names both versions, and stays unchanged
    /// (case C14).
    /// </summary>
    [Fact]
    public async Task ReadDeck_WithAnOlderSchemaVersion_IsUnsupportedAndNamesBothVersions()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        DeckDocument deck = Success(await store.CreateAsync(
            new DeckCreateRequest("Old", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckBackup backup = Success(await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        string path = BackupPath(temporary.Path, backup.BackupId);
        await RewriteSchemaVersionAsync(path, 0);
        string before = HashOf(path);

        OperationResult<DeckDocument> result = await store.Backups.ReadDeckAsync(
            backup.BackupId,
            deck.DeckId,
            TestContext.Current.CancellationToken);

        OperationUnsupported unsupported = Assert.IsType<OperationUnsupported>(result.Value);
        Assert.Equal("backup-schema-mismatch", unsupported.ReasonCode);
        Assert.Contains("version 0", unsupported.Message, StringComparison.Ordinal);
        Assert.Contains($"version {DeckDatabase.SchemaVersion}", unsupported.Message, StringComparison.Ordinal);
        Assert.Equal(before, HashOf(path));
    }

    /// <summary>
    /// Verifies a deck that is not in the backup is not found with its own reason (case C15), and unknown or empty
    /// identifiers fail cleanly.
    /// </summary>
    [Fact]
    public async Task ReadDeck_ForADeckOrBackupThatIsMissing_ReportsTheReason()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        _ = Success(await store.CreateAsync(new DeckCreateRequest("One", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckBackup backup = Success(await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        DeckDocument later = Success(await store.CreateAsync(
            new DeckCreateRequest("Later", Format: "custom"),
            TestContext.Current.CancellationToken));

        OperationResult<DeckDocument> missingDeck = await store.Backups.ReadDeckAsync(
            backup.BackupId,
            later.DeckId,
            TestContext.Current.CancellationToken);
        OperationResult<DeckDocument> missingBackup = await store.Backups.ReadDeckAsync(
            Guid.CreateVersion7(),
            later.DeckId,
            TestContext.Current.CancellationToken);
        OperationResult<DeckDocument> empty = await store.Backups.ReadDeckAsync(
            Guid.Empty,
            later.DeckId,
            TestContext.Current.CancellationToken);

        Assert.Equal("deck-not-in-backup", Assert.IsType<OperationNotFound>(missingDeck.Value).ReasonCode);
        Assert.Equal("deck-backup-not-found", Assert.IsType<OperationNotFound>(missingBackup.Value).ReasonCode);
        Assert.IsType<OperationInvalidInput>(empty.Value);
    }

    /// <summary>
    /// Verifies a backup file that is not a database is reported as unavailable without leaking its path.
    /// </summary>
    [Fact]
    public async Task ReadDeck_FromACorruptBackupFile_IsUnavailableWithoutAPath()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        DeckDocument deck = Success(await store.CreateAsync(
            new DeckCreateRequest("One", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckBackup backup = Success(await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(BackupPath(temporary.Path, backup.BackupId), "not a database",
            TestContext.Current.CancellationToken);

        OperationResult<DeckDocument> result = await store.Backups.ReadDeckAsync(
            backup.BackupId,
            deck.DeckId,
            TestContext.Current.CancellationToken);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.DoesNotContain(temporary.Path, unavailable.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gives the file path of a backup inside a data root.
    /// </summary>
    private static string BackupPath(string root, Guid backupId)
    {
        return Path.Combine(root, "backups", "decks", backupId.ToString("N") + ".db");
    }

    /// <summary>
    /// Hashes a file's bytes, so a test can prove the file did not change.
    /// </summary>
    private static string HashOf(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    /// <summary>
    /// Rewrites the recorded schema version of a backup file, to stand for a backup made by another version.
    /// </summary>
    private static async Task RewriteSchemaVersionAsync(string path, int version)
    {
        SqliteConnectionStringBuilder builder = new() { DataSource = path, Pooling = false };
        await using (SqliteConnection connection = new(builder.ToString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM schema_migrations WHERE version > 1; UPDATE schema_migrations SET version = $version;";
            command.Parameters.AddWithValue("$version", version);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
