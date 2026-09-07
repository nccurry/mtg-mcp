using Microsoft.Data.Sqlite;

namespace MtgMcp.Scryfall;

/// <summary>
/// Coordinates provider request leases and start times across processes.
/// </summary>
internal sealed class ScryfallRequestCoordinationStore
{
    /// <summary>Stores shared SQLite connection and schema support.</summary>
    private readonly ScryfallDatabase database;

    /// <summary>Creates request coordination around shared SQLite support.</summary>
    internal ScryfallRequestCoordinationStore(ScryfallDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        this.database = database;
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
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
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
}
