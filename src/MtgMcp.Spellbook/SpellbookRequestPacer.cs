using Microsoft.Data.Sqlite;

namespace MtgMcp.Spellbook;

/// <summary>
/// Reserves Commander Spellbook request starts across processes that share one cache database.
/// </summary>
internal sealed class SpellbookRequestPacer
{
    /// <summary>
    /// Keeps starts below the source's published safe request ceiling.
    /// </summary>
    internal static TimeSpan MinimumRequestInterval { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Caps a recorded Retry-After cooldown without creating an unbounded local wait.
    /// </summary>
    internal static TimeSpan MaximumCooldown { get; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Opens the shared adapter-owned pacing state.
    /// </summary>
    private readonly SpellbookDatabase database;

    /// <summary>
    /// Creates the pacer over the same database used by the response cache.
    /// </summary>
    internal SpellbookRequestPacer(SpellbookDatabase database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Reserves the next allowed source start or reports an active cooldown without waiting.
    /// </summary>
    internal async Task<TimeSpan> ReserveStartAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        SpellbookPacingState state = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (state.CooldownUntilUtc > now)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            throw RateLimited();
        }

        DateTimeOffset reserved = state.NextStartUtc > now ? state.NextStartUtc : now;
        await WriteNextStartAsync(
            connection,
            transaction,
            reserved + MinimumRequestInterval,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return reserved > now ? reserved - now : TimeSpan.Zero;
    }

    /// <summary>
    /// Stops a previously reserved request if another process recorded a cooldown while it waited.
    /// </summary>
    internal async Task ThrowIfCoolingDownAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        SpellbookPacingState state = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (state.CooldownUntilUtc > now)
        {
            throw RateLimited();
        }
    }

    /// <summary>
    /// Records a bounded source cooldown without retrying the failed request.
    /// </summary>
    internal async Task RecordCooldownAsync(
        DateTimeOffset nowUtc,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = nowUtc.ToUniversalTime();
        TimeSpan duration = retryAfter is { } sourceDuration && sourceDuration > TimeSpan.Zero
            ? sourceDuration <= MaximumCooldown ? sourceDuration : MaximumCooldown
            : MaximumCooldown;
        DateTimeOffset requested = now + duration;
        await using SqliteConnection connection = await database.OpenWriteAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        SpellbookPacingState state = await ReadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (requested > state.CooldownUntilUtc)
        {
            await WriteCooldownAsync(connection, transaction, requested, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the singleton pacing row under the caller's immediate transaction.
    /// </summary>
    private static async Task<SpellbookPacingState> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT next_start_utc, cooldown_until_utc FROM request_pacing WHERE singleton = 1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The Commander Spellbook pacing state is missing.");
        }

        return new SpellbookPacingState(
            SpellbookSql.ParseUtc(reader.GetString(0)),
            SpellbookSql.ParseUtc(reader.GetString(1)));
    }

    /// <summary>
    /// Writes one future request-start boundary under the caller's transaction.
    /// </summary>
    private static async Task WriteNextStartAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset nextStartUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE request_pacing SET next_start_utc = $nextStartUtc WHERE singleton = 1;";
        command.Parameters.AddWithValue("$nextStartUtc", SpellbookSql.FormatUtc(nextStartUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one cooldown boundary under the caller's transaction.
    /// </summary>
    private static async Task WriteCooldownAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset cooldownUntilUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE request_pacing SET cooldown_until_utc = $cooldownUntilUtc WHERE singleton = 1;";
        command.Parameters.AddWithValue("$cooldownUntilUtc", SpellbookSql.FormatUtc(cooldownUntilUtc));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the stable result used for local or source rate limiting.
    /// </summary>
    private static SpellbookProviderException RateLimited()
    {
        return new SpellbookProviderException(
            SpellbookFailureKind.RateLimited,
            "source-rate-limited",
            "Commander Spellbook is temporarily rate-limiting requests.");
    }
}
