using Microsoft.Data.Sqlite;

namespace MtgMcp.Spellbook;

/// <summary>
/// Stores and retrieves exact successful Commander Spellbook responses by hash-only request identity.
/// </summary>
internal sealed class SpellbookCache
{
    /// <summary>
    /// Opens the adapter-owned cache database.
    /// </summary>
    private readonly SpellbookDatabase database;

    /// <summary>
    /// Creates the cache over one adapter-owned database.
    /// </summary>
    internal SpellbookCache(SpellbookDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Reads one current response after removing expired and stale-contract rows.
    /// </summary>
    internal async Task<SpellbookCachedResponse?> TryGetAsync(
        SpellbookRequestIdentity identity,
        DateTimeOffset nowUtc,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await RemoveStaleAsync(connection, identity.ContractChecksum, nowUtc - ttl, cancellationToken)
            .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT response_json, response_checksum, source_api_version, contract_checksum, retrieved_at_utc
            FROM response_cache
            WHERE operation = $operation AND request_hash = $requestHash AND contract_checksum = $contractChecksum;
            """;
        command.Parameters.AddWithValue("$operation", identity.Operation);
        command.Parameters.AddWithValue("$requestHash", identity.RequestHash);
        command.Parameters.AddWithValue("$contractChecksum", identity.ContractChecksum);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SpellbookCachedResponse(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            SpellbookSql.ParseUtc(reader.GetString(4)));
    }

    /// <summary>
    /// Stores one validated successful source object and removes stale rows during the write.
    /// </summary>
    internal async Task StoreAsync(
        SpellbookRequestIdentity identity,
        SpellbookNetworkResponse response,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(response);
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await RemoveStaleAsync(
            connection,
            identity.ContractChecksum,
            response.RetrievedAtUtc - ttl,
            cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO response_cache (
                operation,
                request_hash,
                response_json,
                response_checksum,
                source_api_version,
                contract_checksum,
                retrieved_at_utc)
            VALUES (
                $operation,
                $requestHash,
                $json,
                $responseChecksum,
                $sourceApiVersion,
                $contractChecksum,
                $retrievedAtUtc)
            ON CONFLICT (operation, request_hash) DO UPDATE SET
                response_json = excluded.response_json,
                response_checksum = excluded.response_checksum,
                source_api_version = excluded.source_api_version,
                contract_checksum = excluded.contract_checksum,
                retrieved_at_utc = excluded.retrieved_at_utc;
            """;
        command.Parameters.AddWithValue("$operation", identity.Operation);
        command.Parameters.AddWithValue("$requestHash", identity.RequestHash);
        command.Parameters.AddWithValue("$json", response.Json);
        command.Parameters.AddWithValue("$responseChecksum", response.SourceChecksum);
        command.Parameters.AddWithValue("$sourceApiVersion", SpellbookContract.ApiVersion);
        command.Parameters.AddWithValue("$contractChecksum", identity.ContractChecksum);
        command.Parameters.AddWithValue("$retrievedAtUtc", SpellbookSql.FormatUtc(response.RetrievedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes responses that are expired or belong to an older checked-in contract.
    /// </summary>
    private static async Task RemoveStaleAsync(
        SqliteConnection connection,
        string contractChecksum,
        DateTimeOffset expiresBeforeUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM response_cache
            WHERE contract_checksum <> $contractChecksum OR retrieved_at_utc < $expiresBeforeUtc;
            """;
        command.Parameters.AddWithValue("$contractChecksum", contractChecksum);
        command.Parameters.AddWithValue("$expiresBeforeUtc", SpellbookSql.FormatUtc(expiresBeforeUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
