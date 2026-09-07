using Microsoft.Data.Sqlite;

namespace MtgMcp.Spellbook;

/// <summary>
/// Opens the Commander Spellbook cache database and validates its one owned schema.
/// </summary>
internal sealed class SpellbookDatabase : IDisposable
{
    /// <summary>
    /// Identifies the first clean-break Commander Spellbook cache schema.
    /// </summary>
    private const int SchemaVersion = 1;

    /// <summary>
    /// Describes the schema fields that must change with an authored migration.
    /// </summary>
    private const string SchemaDefinition = """
        schema-version:1
        response_cache:operation,request_hash,response_json,response_checksum,source_api_version,contract_checksum,retrieved_at_utc
        request_pacing:singleton,next_start_utc,cooldown_until_utc
        """;

    /// <summary>
    /// Detects a persisted schema that does not match the current migration definition.
    /// </summary>
    internal static string SchemaChecksum { get; } = SpellbookHash.Compute(SchemaDefinition);

    /// <summary>
    /// Creates the complete schema using the current migration checksum.
    /// </summary>
    private static string SchemaSql { get; } = $"""
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;
        BEGIN IMMEDIATE;
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at_utc TEXT NOT NULL,
            checksum TEXT NOT NULL
        );
        INSERT OR IGNORE INTO schema_migrations (version, applied_at_utc, checksum)
        VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), '{SchemaChecksum}');
        CREATE TABLE IF NOT EXISTS response_cache (
            operation TEXT NOT NULL,
            request_hash TEXT NOT NULL,
            response_json TEXT NOT NULL,
            response_checksum TEXT NOT NULL,
            source_api_version TEXT NOT NULL,
            contract_checksum TEXT NOT NULL,
            retrieved_at_utc TEXT NOT NULL,
            PRIMARY KEY (operation, request_hash)
        );
        CREATE INDEX IF NOT EXISTS ix_response_cache_contract_retrieved
            ON response_cache (contract_checksum, retrieved_at_utc);
        CREATE TABLE IF NOT EXISTS request_pacing (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            next_start_utc TEXT NOT NULL,
            cooldown_until_utc TEXT NOT NULL
        );
        INSERT OR IGNORE INTO request_pacing (singleton, next_start_utc, cooldown_until_utc)
        VALUES (1, '0001-01-01T00:00:00.0000000+00:00', '0001-01-01T00:00:00.0000000+00:00');
        COMMIT;
        """;

    /// <summary>
    /// Serializes first-time schema validation inside this process.
    /// </summary>
    private readonly SemaphoreSlim initializationGate = new(1, 1);

    /// <summary>
    /// Stores the private cache database file path.
    /// </summary>
    private readonly string databasePath;

    /// <summary>
    /// Tracks whether this process has validated the schema.
    /// </summary>
    private bool schemaReady;

    /// <summary>
    /// Creates the database owner without creating the data root or cache file.
    /// </summary>
    internal SpellbookDatabase(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        SQLitePCL.Batteries_V2.Init();
        databasePath = Path.Combine(dataRoot, "spellbook.db");
    }

    /// <summary>
    /// Opens a writable cache connection after creating and validating the schema.
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

    /// <inheritdoc />
    public void Dispose()
    {
        initializationGate.Dispose();
    }

    /// <summary>
    /// Creates and validates the current schema once for this database owner.
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
                await ValidateSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException("The Commander Spellbook cache schema is unsupported or corrupt.", exception);
            }

            schemaReady = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    /// <summary>
    /// Verifies that the cache contains exactly the current schema migration.
    /// </summary>
    private static async Task ValidateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
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
            throw new InvalidDataException("The Commander Spellbook cache schema is unsupported or corrupt.");
        }
    }
}
