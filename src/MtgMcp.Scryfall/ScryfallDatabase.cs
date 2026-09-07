using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Results;

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
    /// Finds an eligible exact-request snapshot and its complete raw membership.
    /// </summary>
    internal async Task<StoredSnapshot?> FindSnapshotAsync(
        string fingerprint,
        DateTimeOffset? minimumRetrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE fingerprint = $fingerprint " +
            "AND ($minimum IS NULL OR retrieved_at_utc >= $minimum) " +
            "ORDER BY retrieved_at_utc DESC, snapshot_id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        command.Parameters.AddWithValue(
            "$minimum",
            minimumRetrievedAtUtc is DateTimeOffset minimum ? ScryfallSql.FormatUtc(minimum) : DBNull.Value);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StoredSnapshotHeader header = ReadSnapshotHeader(reader);
        await reader.DisposeAsync().ConfigureAwait(false);
        IReadOnlyList<string> members = await ReadSnapshotMembersAsync(
            connection,
            header.SnapshotId,
            cancellationToken).ConfigureAwait(false);
        return new StoredSnapshot(header, members);
    }

    /// <summary>
    /// Finds one immutable snapshot by exact identity for cursor-bound replay.
    /// </summary>
    internal async Task<StoredSnapshot?> FindSnapshotByIdAsync(
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return null;
        }

        StoredSnapshotHeader? header = await ReadSnapshotHeaderAsync(connection, snapshotId, cancellationToken)
            .ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        IReadOnlyList<string> members = await ReadSnapshotMembersAsync(
            connection,
            snapshotId,
            cancellationToken).ConfigureAwait(false);
        return new StoredSnapshot(header, members);
    }

    /// <summary>
    /// Stores one completed provider request atomically with raw pages and ordered members.
    /// </summary>
    internal async Task<StoredSnapshot> SaveSnapshotAsync(
        string operation,
        string requestJson,
        string fingerprint,
        IReadOnlyList<string> rawPages,
        IReadOnlyList<string> members,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        Guid snapshotId = Guid.NewGuid();
        StoredSnapshot? predecessor = await FindSnapshotOnConnectionAsync(
            connection,
            fingerprint,
            cancellationToken).ConfigureAwait(false);
        string checksum = ScryfallHash.Compute(string.Join('\n', rawPages.Select(ScryfallHash.Compute)));
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO request_snapshots " +
                "(snapshot_id, operation, request_json, fingerprint, retrieved_at_utc, checksum, total_count, predecessor_id) " +
                "VALUES ($id, $operation, $request, $fingerprint, $retrieved, $checksum, $count, $predecessor);";
            command.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(snapshotId));
            command.Parameters.AddWithValue("$operation", operation);
            command.Parameters.AddWithValue("$request", requestJson);
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.Parameters.AddWithValue("$retrieved", ScryfallSql.FormatUtc(retrievedAtUtc));
            command.Parameters.AddWithValue("$checksum", checksum);
            command.Parameters.AddWithValue("$count", members.Count);
            command.Parameters.AddWithValue(
                "$predecessor",
                predecessor is null ? DBNull.Value : ScryfallSql.FormatGuid(predecessor.Header.SnapshotId));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (int index = 0; index < rawPages.Count; index++)
        {
            string payloadChecksum = ScryfallHash.Compute(rawPages[index]);
            await InsertSnapshotPayloadAsync(
                connection,
                transaction,
                payloadChecksum,
                rawPages[index],
                cancellationToken).ConfigureAwait(false);
            await using SqliteCommand page = connection.CreateCommand();
            page.Transaction = transaction;
            page.CommandText =
                "INSERT INTO snapshot_pages (snapshot_id, ordinal, checksum) " +
                "VALUES ($id, $ordinal, $checksum);";
            page.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(snapshotId));
            page.Parameters.AddWithValue("$ordinal", index);
            page.Parameters.AddWithValue("$checksum", payloadChecksum);
            await page.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        for (int index = 0; index < members.Count; index++)
        {
            string payloadChecksum = ScryfallHash.Compute(members[index]);
            await InsertSnapshotPayloadAsync(
                connection,
                transaction,
                payloadChecksum,
                members[index],
                cancellationToken).ConfigureAwait(false);
            await using SqliteCommand member = connection.CreateCommand();
            member.Transaction = transaction;
            member.CommandText =
                "INSERT INTO snapshot_members (snapshot_id, ordinal, checksum) " +
                "VALUES ($id, $ordinal, $checksum);";
            member.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(snapshotId));
            member.Parameters.AddWithValue("$ordinal", index);
            member.Parameters.AddWithValue("$checksum", payloadChecksum);
            await member.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new StoredSnapshot(
            new StoredSnapshotHeader(
                snapshotId,
                operation,
                requestJson,
                retrievedAtUtc.ToUniversalTime(),
                checksum,
                members.Count,
                predecessor?.Header.SnapshotId),
            members);
    }

    /// <summary>
    /// Lists immutable request snapshots in stable reverse-retrieval order.
    /// </summary>
    internal async Task<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>> ListSnapshotsAsync(
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return new OperationSuccess<ScryfallPage<ScryfallSnapshotSummary>>(
                new ScryfallPage<ScryfallSnapshotSummary>([], 0, null));
        }

        string scope =
            $"snapshots:{operation ?? "all"}:{FormatOptionalUtc(retrievedAfterUtc)}:{FormatOptionalUtc(retrievedBeforeUtc)}";
        string checksum = await SnapshotCollectionChecksumAsync(
            connection,
            operation,
            retrievedAfterUtc,
            retrievedBeforeUtc,
            cancellationToken)
            .ConfigureAwait(false);
        if (!ScryfallCursor.TryDecode(cursor, scope, checksum, out int offset))
        {
            return new OperationInvalidInput("invalid-cursor", "The snapshot cursor is invalid for this request.");
        }

        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText =
            "SELECT COUNT(*) FROM request_snapshots WHERE ($operation IS NULL OR operation = $operation) " +
            "AND ($after IS NULL OR retrieved_at_utc >= $after) " +
            "AND ($before IS NULL OR retrieved_at_utc <= $before);";
        AddSnapshotFilters(count, operation, retrievedAfterUtc, retrievedBeforeUtc);
        int total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE ($operation IS NULL OR operation = $operation) " +
            "AND ($after IS NULL OR retrieved_at_utc >= $after) " +
            "AND ($before IS NULL OR retrieved_at_utc <= $before) " +
            "ORDER BY retrieved_at_utc DESC, snapshot_id DESC LIMIT $limit OFFSET $offset;";
        AddSnapshotFilters(command, operation, retrievedAfterUtc, retrievedBeforeUtc);
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", offset);
        List<ScryfallSnapshotSummary> summaries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            StoredSnapshotHeader header = ReadSnapshotHeader(reader);
            summaries.Add(ToSummary(header));
        }

        string? next = offset + summaries.Count < total
            ? ScryfallCursor.Encode(scope, checksum, offset + summaries.Count)
            : null;
        return new OperationSuccess<ScryfallPage<ScryfallSnapshotSummary>>(
            new ScryfallPage<ScryfallSnapshotSummary>(summaries, total, next));
    }

    /// <summary>
    /// Replays one immutable snapshot page using a checksum-bound cursor.
    /// </summary>
    internal async Task<OperationResult<ScryfallSnapshotPage>> GetSnapshotAsync(
        Guid snapshotId,
        string? cursor,
        int pageSize,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await OpenReadAsync(cancellationToken).ConfigureAwait(false);
        if (connection is null)
        {
            return new OperationNotFound("scryfall-snapshot-not-found", "The requested snapshot does not exist.");
        }

        StoredSnapshotHeader? header = await ReadSnapshotHeaderAsync(connection, snapshotId, cancellationToken)
            .ConfigureAwait(false);
        if (header is null)
        {
            return new OperationNotFound("scryfall-snapshot-not-found", "The requested snapshot does not exist.");
        }

        string scope = $"snapshot:{snapshotId:D}";
        if (!ScryfallCursor.TryDecode(cursor, scope, header.Checksum, out int offset) || offset > header.TotalCount)
        {
            return new OperationInvalidInput("invalid-cursor", "The snapshot cursor is invalid for this request.");
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT m.ordinal, m.checksum, p.raw_json FROM snapshot_members m " +
            "JOIN snapshot_payloads p ON p.checksum = m.checksum " +
            "WHERE m.snapshot_id = $id ORDER BY m.ordinal LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(snapshotId));
        command.Parameters.AddWithValue("$limit", pageSize);
        command.Parameters.AddWithValue("$offset", offset);
        List<ScryfallSnapshotMember> items = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                JsonElement? raw = null;
                if (includeRaw)
                {
                    using JsonDocument document = JsonDocument.Parse(reader.GetString(2));
                    raw = document.RootElement.Clone();
                }

                items.Add(new ScryfallSnapshotMember(reader.GetInt32(0), reader.GetString(1), raw));
            }
        }

        string? next = offset + items.Count < header.TotalCount
            ? ScryfallCursor.Encode(scope, header.Checksum, offset + items.Count)
            : null;
        using JsonDocument requestDocument = JsonDocument.Parse(header.RequestJson);
        return new OperationSuccess<ScryfallSnapshotPage>(
            new ScryfallSnapshotPage(ToSummary(header), requestDocument.RootElement.Clone(), items, next));
    }

    /// <summary>
    /// Deletes one snapshot only when its checksum and explicit acknowledgement match.
    /// </summary>
    internal async Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteSnapshotAsync(
        Guid snapshotId,
        string expectedChecksum,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeDataLoss)
        {
            return new OperationInvalidInput("evidence-loss-not-acknowledged", "Snapshot deletion requires explicit acknowledgement.");
        }

        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        StoredSnapshotHeader? header = await ReadSnapshotHeaderAsync(connection, snapshotId, cancellationToken)
            .ConfigureAwait(false);
        if (header is null)
        {
            return new OperationNotFound("scryfall-snapshot-not-found", "The requested snapshot does not exist.");
        }

        if (!string.Equals(header.Checksum, expectedChecksum, StringComparison.Ordinal))
        {
            return new OperationConflict("stale-scryfall-snapshot", "The snapshot checksum changed before deletion.");
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM request_snapshots WHERE snapshot_id = $id; " +
            "DELETE FROM snapshot_payloads WHERE NOT EXISTS " +
            "(SELECT 1 FROM snapshot_pages p WHERE p.checksum = snapshot_payloads.checksum) " +
            "AND NOT EXISTS (SELECT 1 FROM snapshot_members m WHERE m.checksum = snapshot_payloads.checksum);";
        command.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(snapshotId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new OperationSuccess<ScryfallSnapshotDeleteResult>(
            new ScryfallSnapshotDeleteResult(snapshotId, header.Checksum));
    }

    /// <summary>
    /// Acquires one expiring acquisition lease without blocking unrelated reads.
    /// </summary>
    internal async Task<bool> TryAcquireLeaseAsync(
        string key,
        string owner,
        DateTimeOffset nowUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using (SqliteCommand cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = transaction;
            cleanup.CommandText = "DELETE FROM acquisition_leases WHERE expires_at_utc <= $now;";
            cleanup.Parameters.AddWithValue("$now", ScryfallSql.FormatUtc(nowUtc));
            await cleanup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT OR IGNORE INTO acquisition_leases (lease_key, owner_id, expires_at_utc) " +
            "VALUES ($key, $owner, $expires);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$owner", owner);
        command.Parameters.AddWithValue("$expires", ScryfallSql.FormatUtc(nowUtc + duration));
        bool acquired = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return acquired;
    }

    /// <summary>
    /// Releases a lease only for its exact owner.
    /// </summary>
    internal async Task ReleaseLeaseAsync(string key, string owner, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM acquisition_leases WHERE lease_key = $key AND owner_id = $owner;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$owner", owner);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reserves the next globally paced provider start and returns its required delay.
    /// </summary>
    internal async Task<TimeSpan> ReserveProviderStartAsync(
        DateTimeOffset nowUtc,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT next_start_utc FROM provider_pacing WHERE singleton = 1;";
        object? current = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset reserved = current is string text && ScryfallSql.ParseUtc(text) > nowUtc
            ? ScryfallSql.ParseUtc(text)
            : nowUtc;
        await using SqliteCommand write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = "UPDATE provider_pacing SET next_start_utc = $next WHERE singleton = 1;";
        write.Parameters.AddWithValue("$next", ScryfallSql.FormatUtc(reserved + interval));
        await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return reserved > nowUtc ? reserved - nowUtc : TimeSpan.Zero;
    }

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
    /// Stores one immutable JSON payload once, keyed by its SHA-256 checksum.
    /// </summary>
    private static async Task InsertSnapshotPayloadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string checksum,
        string rawJson,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT OR IGNORE INTO snapshot_payloads (checksum, raw_json) VALUES ($checksum, $raw);";
        command.Parameters.AddWithValue("$checksum", checksum);
        command.Parameters.AddWithValue("$raw", rawJson);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the latest snapshot using an existing writable connection.
    /// </summary>
    private static async Task<StoredSnapshot?> FindSnapshotOnConnectionAsync(
        SqliteConnection connection,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE fingerprint = $fingerprint " +
            "ORDER BY retrieved_at_utc DESC, snapshot_id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        StoredSnapshotHeader header = ReadSnapshotHeader(reader);
        await reader.DisposeAsync().ConfigureAwait(false);
        IReadOnlyList<string> members = await ReadSnapshotMembersAsync(connection, header.SnapshotId, cancellationToken)
            .ConfigureAwait(false);
        return new StoredSnapshot(header, members);
    }

    /// <summary>
    /// Reads one snapshot header by exact ID.
    /// </summary>
    private static async Task<StoredSnapshotHeader?> ReadSnapshotHeaderAsync(
        SqliteConnection connection,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT snapshot_id, operation, request_json, retrieved_at_utc, checksum, total_count, predecessor_id " +
            "FROM request_snapshots WHERE snapshot_id = $id;";
        command.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(snapshotId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadSnapshotHeader(reader)
            : null;
    }

    /// <summary>
    /// Reads one snapshot header from the current data-reader row.
    /// </summary>
    private static StoredSnapshotHeader ReadSnapshotHeader(SqliteDataReader reader)
    {
        return new StoredSnapshotHeader(
            ScryfallSql.ParseGuid(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            ScryfallSql.ParseUtc(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt32(5),
            ReadNullableGuid(reader, 6));
    }

    /// <summary>
    /// Reads all ordered members for one snapshot.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadSnapshotMembersAsync(
        SqliteConnection connection,
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT p.raw_json FROM snapshot_members m " +
            "JOIN snapshot_payloads p ON p.checksum = m.checksum " +
            "WHERE m.snapshot_id = $id ORDER BY m.ordinal;";
        command.Parameters.AddWithValue("$id", ScryfallSql.FormatGuid(snapshotId));
        return await ReadStringsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Hashes the current snapshot summary collection for cursor invalidation.
    /// </summary>
    private static async Task<string> SnapshotCollectionChecksumAsync(
        SqliteConnection connection,
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(GROUP_CONCAT(snapshot_id || ':' || checksum, '|'), '') " +
            "FROM (SELECT snapshot_id, checksum FROM request_snapshots " +
            "WHERE ($operation IS NULL OR operation = $operation) " +
            "AND ($after IS NULL OR retrieved_at_utc >= $after) " +
            "AND ($before IS NULL OR retrieved_at_utc <= $before) " +
            "ORDER BY retrieved_at_utc, snapshot_id);";
        AddSnapshotFilters(command, operation, retrievedAfterUtc, retrievedBeforeUtc);
        string value = (string)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty);
        return ScryfallHash.Compute(value);
    }

    /// <summary>
    /// Adds the shared optional operation and UTC retrieval bounds to a snapshot query.
    /// </summary>
    private static void AddSnapshotFilters(
        SqliteCommand command,
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc)
    {
        command.Parameters.AddWithValue("$operation", operation is null ? DBNull.Value : operation);
        command.Parameters.AddWithValue(
            "$after",
            retrievedAfterUtc is DateTimeOffset after ? ScryfallSql.FormatUtc(after) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$before",
            retrievedBeforeUtc is DateTimeOffset before ? ScryfallSql.FormatUtc(before) : DBNull.Value);
    }

    /// <summary>
    /// Formats an optional UTC bound into a stable cursor-scope component.
    /// </summary>
    private static string FormatOptionalUtc(DateTimeOffset? value)
    {
        return value is DateTimeOffset present ? ScryfallSql.FormatUtc(present) : "all";
    }

    /// <summary>
    /// Projects an internal header into its safe public summary.
    /// </summary>
    private static ScryfallSnapshotSummary ToSummary(StoredSnapshotHeader header)
    {
        return new ScryfallSnapshotSummary(
            header.SnapshotId,
            header.Operation,
            header.RetrievedAtUtc,
            header.Checksum,
            header.TotalCount,
            header.PredecessorId);
    }

    /// <summary>
    /// Reads a command's first string column into an ordered list.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        List<string> results = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    /// <summary>
    /// Reads one nullable UUID column.
    /// </summary>
    private static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ScryfallSql.ParseGuid(reader.GetString(ordinal));
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

/// <summary>
/// Carries one immutable request snapshot header.
/// </summary>
internal sealed record StoredSnapshotHeader(
    Guid SnapshotId,
    string Operation,
    string RequestJson,
    DateTimeOffset RetrievedAtUtc,
    string Checksum,
    int TotalCount,
    Guid? PredecessorId);

/// <summary>
/// Carries one complete stored request snapshot.
/// </summary>
internal sealed record StoredSnapshot(StoredSnapshotHeader Header, IReadOnlyList<string> Members);
