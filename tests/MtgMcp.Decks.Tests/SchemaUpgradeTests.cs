using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.Decks.Tests;

/// <summary>
/// Verifies the schema 2 upgrade runs on every write path, keeps a copy first, and leaves a healthy database.
/// </summary>
public sealed class SchemaUpgradeTests
{
    /// <summary>
    /// Names the deck the version 1 fixture holds.
    /// </summary>
    private static readonly Guid DeckId = Guid.Parse("00000000-0000-4000-8000-000000000001");

    /// <summary>
    /// Verifies a batch edit on a version 1 database upgrades it first, so case-distinct categories are accepted,
    /// a copy of the old database is kept, foreign keys are intact, and the old data is still there.
    /// </summary>
    [Fact]
    public async Task ApplyChanges_OnAVersionOneDatabase_UpgradesFirst()
    {
        using TemporaryDeckDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "decks.db");
        CategoryNameTests.MakeVersionOneDatabase(path);
        using SqliteDeckStore store = new(temporary.Path, "test");
        DeckDocument before = Success(await store.GetAsync(DeckId, TestContext.Current.CancellationToken));

        DeckDocument after = Success(await store.ApplyChangesAsync(
            DeckId,
            before.Revision,
            [
                new AddDeckCategoryChange(new DeckCategoryDraft("Side")),
                new AddDeckCategoryChange(new DeckCategoryDraft("SIDE")),
            ],
            TestContext.Current.CancellationToken));

        Assert.Equal(["Ramp", "SIDE", "Side"], after.Categories.Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal(before.CategoryAssignments, after.CategoryAssignments);
        Assert.Equal(2L, await ScalarAsync(path, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.Equal(0L, await ScalarAsync(path, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        string copies = Path.Combine(temporary.Path, "backups", "decks");
        Assert.Single(Directory.GetFiles(copies, "pre-migration-*.db"));
    }

    /// <summary>
    /// Verifies a writable connection to a version 1 database is upgraded when it opens and has foreign keys on.
    /// </summary>
    [Fact]
    public async Task OpenForWriting_OnAVersionOneDatabase_UpgradesAndKeepsForeignKeysOn()
    {
        using TemporaryDeckDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "decks.db");
        CategoryNameTests.MakeVersionOneDatabase(path);
        DeckDatabase database = new(path, TimeProvider.System, "test");

        await using SqliteConnection connection = await database.OpenConnectionAsync(
            writable: true,
            TestContext.Current.CancellationToken);

        Assert.Equal(DeckDatabase.SchemaVersion, await DeckDatabase.GetSchemaVersionAsync(
            connection,
            TestContext.Current.CancellationToken));
        Assert.Equal(1L, await ScalarOnAsync(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(0L, await ScalarOnAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    /// <summary>
    /// Verifies a read does not upgrade, and a failed migration rolls back and leaves foreign keys on.
    /// </summary>
    [Fact]
    public async Task FailedMigration_RollsBackAndLeavesForeignKeysOn()
    {
        using TemporaryDeckDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "decks.db");
        CategoryNameTests.MakeVersionOneDatabase(path);
        using SqliteDeckStore store = new(temporary.Path, "test");
        _ = Success(await store.GetAsync(DeckId, TestContext.Current.CancellationToken));
        Assert.Equal(1L, await ScalarAsync(path, "SELECT MAX(version) FROM schema_migrations;"));
        DeckDatabase database = new(path, TimeProvider.System, "test");
        await using SqliteConnection connection = new($"Data Source={path};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SqliteException>(async () => await database.ApplyMigrationAsync(
            connection,
            new SqliteMigration(2, true, "DROP TABLE deck_categories; SELECT * FROM missing_table;"),
            DateTimeOffset.UtcNow,
            "test",
            TestContext.Current.CancellationToken));

        Assert.Equal(1L, await ScalarOnAsync(connection, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.Equal(1L, await ScalarOnAsync(connection, "SELECT COUNT(*) FROM deck_categories;"));
        Assert.Equal(1L, await ScalarOnAsync(connection, "PRAGMA foreign_keys;"));
    }

    /// <summary>
    /// Verifies restoring a backup made with schema 1 leaves an upgraded database.
    /// </summary>
    [Fact]
    public async Task Restore_OfAVersionOneBackup_UpgradesTheRestoredDatabase()
    {
        using TemporaryDeckDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "test");
        _ = Success(await store.CreateAsync(
            new DeckCreateRequest("Live", Format: "custom"),
            TestContext.Current.CancellationToken));
        DeckBackup backup = Success(await store.Backups.CreateAsync(TestContext.Current.CancellationToken));
        string backupDirectory = Path.Combine(temporary.Path, "backups", "decks");
        string stem = backup.BackupId.ToString("N");
        string backupFile = Path.Combine(backupDirectory, stem + ".db");
        File.Delete(backupFile);
        CategoryNameTests.MakeVersionOneDatabase(backupFile);
        SqliteConnection.ClearAllPools();
        byte[] bytes = await File.ReadAllBytesAsync(backupFile, TestContext.Current.CancellationToken);
        string fingerprint = "v1:" + Convert.ToHexString(
            SHA256.HashData([.. Encoding.UTF8.GetBytes("decks-schema-1\n"), .. bytes])).ToLowerInvariant();
        string manifestFile = Path.Combine(backupDirectory, stem + ".json");
        string manifest = await File.ReadAllTextAsync(manifestFile, TestContext.Current.CancellationToken);
        manifest = System.Text.RegularExpressions.Regex.Replace(manifest, "\"schemaVersion\":\\d+", "\"schemaVersion\":1");
        manifest = System.Text.RegularExpressions.Regex.Replace(
            manifest,
            "\"fingerprint\":\"[^\"]*\"",
            "\"fingerprint\":\"" + fingerprint + "\"");
        await File.WriteAllTextAsync(manifestFile, manifest, TestContext.Current.CancellationToken);
        DeckBackupPage page = Success(await store.Backups.ListAsync(TestContext.Current.CancellationToken));

        _ = Success(await store.Backups.RestoreAsync(
            backup.BackupId,
            page.CurrentDatabaseFingerprint!,
            TestContext.Current.CancellationToken));

        string live = Path.Combine(temporary.Path, "decks.db");
        Assert.Equal(2L, await ScalarAsync(live, "SELECT MAX(version) FROM schema_migrations;"));
        DeckDocument restored = Success(await store.GetAsync(DeckId, TestContext.Current.CancellationToken));
        DeckDocument edited = Success(await store.ApplyChangesAsync(
            DeckId,
            restored.Revision,
            [new AddDeckCategoryChange(new DeckCategoryDraft("X")), new AddDeckCategoryChange(new DeckCategoryDraft("x"))],
            TestContext.Current.CancellationToken));
        Assert.Equal(3, edited.Categories.Count);
    }

    /// <summary>
    /// Verifies a connection that read version 1 before another process upgraded the file does not migrate again and
    /// does not make another pre-migration copy.
    /// </summary>
    [Fact]
    public async Task Migration_AfterAnotherProcessAlreadyUpgraded_IsSkipped()
    {
        using TemporaryDeckDirectory temporary = new();
        string path = Path.Combine(temporary.Path, "decks.db");
        CategoryNameTests.MakeVersionOneDatabase(path);
        DeckDatabase database = new(path, TimeProvider.System, "test");
        await using SqliteConnection stale = new($"Data Source={path};Pooling=False");
        await stale.OpenAsync(TestContext.Current.CancellationToken);
        await using (await database.OpenConnectionAsync(writable: true, TestContext.Current.CancellationToken))
        {
        }

        string copies = Path.Combine(temporary.Path, "backups", "decks");
        int before = Directory.GetFiles(copies, "pre-migration-*.db").Length;

        await database.ApplyMigrationAsync(
            stale,
            new SqliteMigration(2, true, "SELECT * FROM missing_table;"),
            DateTimeOffset.UtcNow,
            "test",
            TestContext.Current.CancellationToken);

        Assert.Equal(1, before);
        Assert.Equal(before, Directory.GetFiles(copies, "pre-migration-*.db").Length);
        Assert.Equal(2L, await ScalarOnAsync(stale, "SELECT MAX(version) FROM schema_migrations;"));
    }

    /// <summary>
    /// Reads one number from a database file.
    /// </summary>
    private static async Task<long> ScalarAsync(string path, string sql)
    {
        await using SqliteConnection connection = new($"Data Source={path};Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await ScalarOnAsync(connection, sql);
    }

    /// <summary>
    /// Reads one number on an open connection.
    /// </summary>
    private static async Task<long> ScalarOnAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
