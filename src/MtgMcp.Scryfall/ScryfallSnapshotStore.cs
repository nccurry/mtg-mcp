using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Stores and replays immutable exact-request snapshots.
/// </summary>
internal sealed class ScryfallSnapshotStore
{
    /// <summary>Stores shared SQLite connection and schema support.</summary>
    private readonly ScryfallDatabase database;

    /// <summary>Creates snapshot storage around shared SQLite support.</summary>
    internal ScryfallSnapshotStore(ScryfallDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
    }

    /// <summary>
    /// Finds an eligible exact-request snapshot and its complete raw membership.
    /// </summary>
    internal async Task<StoredSnapshot?> FindAsync(
        string fingerprint,
        DateTimeOffset? minimumRetrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
    internal async Task<StoredSnapshot?> FindByIdAsync(
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
    internal async Task<StoredSnapshot> SaveAsync(
        string operation,
        string requestJson,
        string fingerprint,
        IReadOnlyList<string> rawPages,
        IReadOnlyList<string> members,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
    internal async Task<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>> ListAsync(
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
    internal async Task<OperationResult<ScryfallSnapshotPage>> GetAsync(
        Guid snapshotId,
        string? cursor,
        int pageSize,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection? connection = await database.OpenReadAsync(cancellationToken).ConfigureAwait(false);
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
    internal async Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteAsync(
        Guid snapshotId,
        string expectedChecksum,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        if (!acknowledgeDataLoss)
        {
            return new OperationInvalidInput("evidence-loss-not-acknowledged", "Snapshot deletion requires explicit acknowledgement.");
        }

        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
