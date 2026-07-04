using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MtgMcp.Decks;

/// <summary>
/// Owns SQLite connection policy, schema creation, and ordered migration safety.
/// </summary>
internal sealed class DeckDatabase
{
    /// <summary>
    /// Identifies the schema understood by this assembly.
    /// </summary>
    internal const int SchemaVersion = 1;

    /// <summary>
    /// Bounds lock waits so callers receive an explicit unavailable result.
    /// </summary>
    internal const int BusyTimeoutMilliseconds = 5_000;

    /// <summary>
    /// Creates every v1 table and relational constraint in one transaction.
    /// </summary>
    private const string SchemaV1Sql = """
        CREATE TABLE schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at_utc TEXT NOT NULL,
            application_version TEXT NOT NULL,
            checksum TEXT NOT NULL
        );
        CREATE TABLE decks (
            deck_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT NOT NULL,
            format TEXT NOT NULL,
            revision INTEGER NOT NULL CHECK (revision > 0),
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        );
        CREATE TABLE deck_entries (
            entry_id TEXT PRIMARY KEY,
            deck_id TEXT NOT NULL REFERENCES decks(deck_id) ON DELETE CASCADE,
            quantity INTEGER NOT NULL CHECK (quantity > 0),
            card_name TEXT NOT NULL,
            oracle_id TEXT NULL,
            printing_id TEXT NULL,
            set_code TEXT NULL,
            collector_number TEXT NULL,
            language TEXT NOT NULL,
            finish TEXT NOT NULL,
            zone TEXT NOT NULL,
            sort_order INTEGER NOT NULL
        );
        CREATE TABLE deck_categories (
            category_id TEXT PRIMARY KEY,
            deck_id TEXT NOT NULL REFERENCES decks(deck_id) ON DELETE CASCADE,
            name TEXT NOT NULL COLLATE NOCASE,
            color TEXT NULL,
            sort_order INTEGER NOT NULL,
            UNIQUE (deck_id, name)
        );
        CREATE TABLE deck_entry_categories (
            entry_id TEXT NOT NULL REFERENCES deck_entries(entry_id) ON DELETE CASCADE,
            category_id TEXT NOT NULL REFERENCES deck_categories(category_id) ON DELETE CASCADE,
            is_primary INTEGER NOT NULL CHECK (is_primary IN (0, 1)),
            PRIMARY KEY (entry_id, category_id)
        );
        CREATE UNIQUE INDEX deck_entry_one_primary_category
            ON deck_entry_categories(entry_id)
            WHERE is_primary = 1;
        CREATE TABLE provider_bindings (
            binding_id TEXT PRIMARY KEY,
            deck_id TEXT NOT NULL REFERENCES decks(deck_id) ON DELETE CASCADE,
            provider TEXT NOT NULL,
            remote_id TEXT NOT NULL,
            remote_uri TEXT NULL,
            remote_version TEXT NULL,
            baseline_fingerprint TEXT NULL,
            last_pulled_at_utc TEXT NULL,
            last_pushed_at_utc TEXT NULL,
            UNIQUE (deck_id, provider, remote_id)
        );
        CREATE TABLE sync_baselines (
            binding_id TEXT PRIMARY KEY REFERENCES provider_bindings(binding_id) ON DELETE CASCADE,
            canonical_snapshot TEXT NOT NULL
        );
        """;

    /// <summary>
    /// Defines the exact first migration and its computed content fingerprint.
    /// </summary>
    private static readonly SqliteMigration SchemaV1Migration = new(
        1,
        IsDestructive: false,
        SchemaV1Sql);

    /// <summary>
    /// Gets the private database location for same-assembly backup operations.
    /// </summary>
    internal string DatabasePath { get; }

    /// <summary>
    /// Creates a database boundary around one fully resolved private path.
    /// </summary>
    internal DeckDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    /// <summary>
    /// Reports whether a database currently exists without creating it.
    /// </summary>
    internal bool Exists => File.Exists(DatabasePath);

    /// <summary>
    /// Creates schema v1 when needed and rejects databases from a future schema.
    /// </summary>
    internal async Task EnsureInitializedAsync(
        DateTimeOffset appliedAtUtc,
        string applicationVersion,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using SqliteConnection connection = await OpenConnectionAsync(
            writable: true,
            cancellationToken).ConfigureAwait(false);
        int currentVersion = await GetSchemaVersionAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        if (currentVersion > SchemaVersion)
        {
            throw new InvalidDataException("The local deck database uses an unsupported schema.");
        }

        if (currentVersion == 0)
        {
            await ApplyMigrationAsync(
                connection,
                SchemaV1Migration,
                appliedAtUtc,
                applicationVersion,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens one configured connection and applies per-connection safety pragmas.
    /// </summary>
    internal async Task<SqliteConnection> OpenConnectionAsync(
        bool writable,
        CancellationToken cancellationToken)
    {
        bool existed = Exists;
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = DatabasePath,
            Mode = writable ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1_000,
        };
        SqliteConnection connection = new(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = writable
                ? $"PRAGMA foreign_keys=ON; PRAGMA busy_timeout={BusyTimeoutMilliseconds}; PRAGMA journal_mode=WAL;"
                : $"PRAGMA foreign_keys=ON; PRAGMA busy_timeout={BusyTimeoutMilliseconds};";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (existed)
            {
                await ValidateExistingSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            }

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Reads the highest applied migration or zero for an empty database.
    /// </summary>
    internal static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
        long tableCount = (long)(await tableCommand.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0L);
        if (tableCount == 0)
        {
            return 0;
        }

        await using SqliteCommand versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(
            await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Rejects incomplete, future, or fingerprint-mismatched existing schemas.
    /// </summary>
    internal static async Task ValidateExistingSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        int version = await GetSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (version != SchemaVersion)
        {
            throw new InvalidDataException("The local deck database uses an unsupported schema.");
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT checksum FROM schema_migrations WHERE version=$version;";
        command.Parameters.AddWithValue("$version", SchemaVersion);
        string checksum = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        if (!checksum.Equals(SchemaV1Migration.Checksum, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The local deck database uses an unsupported schema.");
        }
    }

    /// <summary>
    /// Applies one migration atomically and preserves the database before destructive work.
    /// </summary>
    internal async Task ApplyMigrationAsync(
        SqliteConnection connection,
        SqliteMigration migration,
        DateTimeOffset appliedAtUtc,
        string applicationVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(migration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        if (migration.IsDestructive && Exists)
        {
            await CreatePreMigrationBackupAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        try
        {
            await using SqliteCommand schemaCommand = connection.CreateCommand();
            schemaCommand.Transaction = transaction;
            schemaCommand.CommandText = migration.Sql;
            await schemaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await using SqliteCommand recordCommand = connection.CreateCommand();
            recordCommand.Transaction = transaction;
            recordCommand.CommandText = """
                INSERT INTO schema_migrations (
                    version, applied_at_utc, application_version, checksum)
                VALUES ($version, $appliedAtUtc, $applicationVersion, $checksum);
                """;
            recordCommand.Parameters.AddWithValue("$version", migration.Version);
            recordCommand.Parameters.AddWithValue("$appliedAtUtc", FormatUtc(appliedAtUtc));
            recordCommand.Parameters.AddWithValue("$applicationVersion", applicationVersion.Trim());
            recordCommand.Parameters.AddWithValue("$checksum", migration.Checksum);
            await recordCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates a recoverable byte copy before a migration declared destructive.
    /// </summary>
    private async Task CreatePreMigrationBackupAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        string backupDirectory = Path.Combine(
            Path.GetDirectoryName(DatabasePath)!,
            "backups",
            "decks");
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(
            backupDirectory,
            $"pre-migration-{Guid.CreateVersion7():N}.db");
        File.Copy(DatabasePath, backupPath, overwrite: false);
    }

    /// <summary>
    /// Formats a timestamp using the canonical UTC round-trip representation.
    /// </summary>
    internal static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Describes one ordered migration and whether it requires a safety copy first.
/// </summary>
internal sealed record SqliteMigration(
    int Version,
    bool IsDestructive,
    string Sql)
{
    /// <summary>
    /// Gets the lowercase SHA-256 fingerprint of the exact migration SQL.
    /// </summary>
    internal string Checksum { get; } =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql))).ToLowerInvariant();
}
