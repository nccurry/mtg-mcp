using Microsoft.Data.Sqlite;

namespace MtgMcp.Scryfall;

/// <summary>
/// Opens Scryfall SQLite connections and makes sure that its schema is valid.
/// </summary>
internal sealed class ScryfallDatabase : IDisposable
{
    /// <summary>
    /// Defines the initial clean-break Scryfall schema.
    /// </summary>
    private const int SchemaVersion = 1;

    /// <summary>
    /// Detects an existing schema whose authored migration no longer matches version one.
    /// </summary>
    private const string SchemaChecksum = "93f5f609eff2ec0b7cf25cc0155075e8b05e100c39ed1b44fa8319ede3fbed9c";

    /// <summary>
    /// Serializes first-time schema initialization inside one process.
    /// </summary>
    private readonly SemaphoreSlim initializationGate = new(1, 1);

    /// <summary>
    /// Stores the private database path.
    /// </summary>
    private readonly string databasePath;

    /// <summary>
    /// Tracks whether this process has verified the schema.
    /// </summary>
    private bool schemaReady;

    /// <summary>
    /// Creates the database object without creating a directory or database file.
    /// </summary>
    internal ScryfallDatabase(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        SQLitePCL.Batteries_V2.Init();
        databasePath = Path.Combine(dataRoot, "scryfall.db");
    }

    /// <summary>
    /// Reports whether the database already exists without creating it.
    /// </summary>
    internal bool Exists => File.Exists(databasePath);

    /// <summary>
    /// Opens the existing database read-only or reports its absence without creating anything.
    /// </summary>
    internal async Task<SqliteConnection?> OpenReadAsync(CancellationToken cancellationToken)
    {
        if (!Exists)
        {
            return null;
        }

        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ValidateExistingSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Opens the writable database and verifies its transactional schema.
    /// </summary>
    internal async Task<SqliteConnection> OpenWriteAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates and validates schema version one once per process.
    /// </summary>
    private async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (schemaReady)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (schemaReady)
            {
                return;
            }

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = SchemaSql;
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await ValidateSchemaRowAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException("The Scryfall database schema is unsupported or corrupt.", exception);
            }

            schemaReady = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    /// <summary>
    /// Validates an existing database without creating tables or changing migration history.
    /// </summary>
    private async Task ValidateExistingSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (schemaReady)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (schemaReady)
            {
                return;
            }

            try
            {
                await ValidateSchemaRowAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException("The Scryfall database schema is unsupported or corrupt.", exception);
            }

            schemaReady = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    /// <summary>
    /// Verifies the one clean-break migration identity and checksum.
    /// </summary>
    private static async Task ValidateSchemaRowAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT version, checksum FROM schema_migrations ORDER BY version;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        bool valid = await reader.ReadAsync(cancellationToken).ConfigureAwait(false) &&
            reader.GetInt32(0) == SchemaVersion &&
            string.Equals(reader.GetString(1), SchemaChecksum, StringComparison.Ordinal) &&
            !await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!valid)
        {
            throw new InvalidDataException("The Scryfall database schema is unsupported or corrupt.");
        }
    }


    /// <summary>
    /// Releases the process-local schema initialization gate.
    /// </summary>
    public void Dispose()
    {
        initializationGate.Dispose();
    }

    /// <summary>
    /// Declares schema version one and all generation-owned cascade boundaries.
    /// </summary>
    private const string SchemaSql = """
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;
        BEGIN IMMEDIATE;
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at_utc TEXT NOT NULL,
            checksum TEXT NOT NULL
        );
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc, checksum)
        VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
            '93f5f609eff2ec0b7cf25cc0155075e8b05e100c39ed1b44fa8319ede3fbed9c');
        CREATE TABLE IF NOT EXISTS corpus_generations (
            generation_id TEXT PRIMARY KEY,
            created_at_utc TEXT NOT NULL,
            activated_at_utc TEXT NULL,
            status TEXT NOT NULL CHECK (status IN ('staging', 'complete'))
        );
        CREATE TABLE IF NOT EXISTS corpus_state (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            active_generation_id TEXT NULL REFERENCES corpus_generations(generation_id),
            previous_generation_id TEXT NULL REFERENCES corpus_generations(generation_id),
            last_metadata_check_utc TEXT NULL
        );
        INSERT OR IGNORE INTO corpus_state (singleton) VALUES (1);
        CREATE TABLE IF NOT EXISTS corpus_datasets (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            dataset_type TEXT NOT NULL,
            provider_id TEXT NOT NULL,
            provider_updated_at_utc TEXT NOT NULL,
            source_bytes INTEGER NOT NULL,
            row_count INTEGER NOT NULL,
            checksum TEXT NOT NULL,
            PRIMARY KEY (generation_id, dataset_type)
        );
        CREATE TABLE IF NOT EXISTS card_objects (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            card_id TEXT NOT NULL,
            oracle_id TEXT NULL,
            illustration_id TEXT NULL,
            name TEXT NOT NULL,
            name_key TEXT NOT NULL,
            set_code TEXT NOT NULL,
            collector_number TEXT NOT NULL,
            lang TEXT NOT NULL,
            released_at TEXT NOT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, card_id)
        );
        CREATE INDEX IF NOT EXISTS ix_cards_oracle ON card_objects(generation_id, oracle_id);
        CREATE INDEX IF NOT EXISTS ix_cards_name ON card_objects(generation_id, name_key);
        CREATE INDEX IF NOT EXISTS ix_cards_printing ON card_objects(generation_id, set_code, collector_number);
        CREATE INDEX IF NOT EXISTS ix_cards_illustration ON card_objects(generation_id, illustration_id);
        CREATE TABLE IF NOT EXISTS card_faces (
            generation_id TEXT NOT NULL,
            card_id TEXT NOT NULL,
            ordinal INTEGER NOT NULL,
            name_key TEXT NOT NULL,
            illustration_id TEXT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, card_id, ordinal),
            FOREIGN KEY (generation_id, card_id) REFERENCES card_objects(generation_id, card_id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_faces_name ON card_faces(generation_id, name_key);
        CREATE INDEX IF NOT EXISTS ix_faces_illustration ON card_faces(generation_id, illustration_id);
        CREATE TABLE IF NOT EXISTS rulings (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            oracle_id TEXT NOT NULL,
            source TEXT NOT NULL,
            published_at TEXT NOT NULL,
            comment TEXT NOT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, ordinal)
        );
        CREATE INDEX IF NOT EXISTS ix_rulings_oracle ON rulings(generation_id, oracle_id, published_at);
        CREATE TABLE IF NOT EXISTS tags (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            tag_id TEXT NOT NULL,
            label TEXT NOT NULL,
            slug TEXT NOT NULL,
            tag_type TEXT NOT NULL CHECK (tag_type IN ('oracle', 'art')),
            description TEXT NULL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY (generation_id, tag_id),
            UNIQUE (generation_id, tag_type, slug)
        );
        CREATE TABLE IF NOT EXISTS tag_relations (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            parent_tag_id TEXT NOT NULL,
            child_tag_id TEXT NOT NULL,
            PRIMARY KEY (generation_id, parent_tag_id, child_tag_id)
        );
        CREATE TABLE IF NOT EXISTS tag_aliases (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            tag_id TEXT NOT NULL,
            alias TEXT NOT NULL,
            PRIMARY KEY (generation_id, tag_id, alias)
        );
        CREATE TABLE IF NOT EXISTS tag_assignments (
            generation_id TEXT NOT NULL REFERENCES corpus_generations(generation_id) ON DELETE CASCADE,
            tag_id TEXT NOT NULL,
            target_type TEXT NOT NULL CHECK (target_type IN ('oracle', 'art')),
            target_id TEXT NOT NULL,
            weight TEXT NOT NULL,
            annotation TEXT NULL,
            PRIMARY KEY (generation_id, tag_id, target_type, target_id)
        );
        CREATE INDEX IF NOT EXISTS ix_tag_assignment_target ON tag_assignments(generation_id, target_type, target_id);
        CREATE TABLE IF NOT EXISTS request_snapshots (
            snapshot_id TEXT PRIMARY KEY,
            operation TEXT NOT NULL,
            request_json TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            retrieved_at_utc TEXT NOT NULL,
            checksum TEXT NOT NULL,
            total_count INTEGER NOT NULL,
            predecessor_id TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_snapshot_fingerprint ON request_snapshots(fingerprint, retrieved_at_utc DESC);
        CREATE TABLE IF NOT EXISTS snapshot_payloads (
            checksum TEXT PRIMARY KEY,
            raw_json TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS snapshot_pages (
            snapshot_id TEXT NOT NULL REFERENCES request_snapshots(snapshot_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            checksum TEXT NOT NULL REFERENCES snapshot_payloads(checksum),
            PRIMARY KEY (snapshot_id, ordinal)
        );
        CREATE TABLE IF NOT EXISTS snapshot_members (
            snapshot_id TEXT NOT NULL REFERENCES request_snapshots(snapshot_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            checksum TEXT NOT NULL REFERENCES snapshot_payloads(checksum),
            PRIMARY KEY (snapshot_id, ordinal)
        );
        CREATE TABLE IF NOT EXISTS acquisition_leases (
            lease_key TEXT PRIMARY KEY,
            owner_id TEXT NOT NULL,
            expires_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS provider_pacing (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            next_start_utc TEXT NULL
        );
        INSERT OR IGNORE INTO provider_pacing (singleton) VALUES (1);
        COMMIT;
        """;
}
