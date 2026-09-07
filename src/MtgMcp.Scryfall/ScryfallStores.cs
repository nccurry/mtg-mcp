using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;


/// <summary>
/// Owns immutable exact-request snapshot persistence and replay.
/// </summary>
internal sealed class ScryfallSnapshotStore
{
    /// <summary>Stores the schema and connection owner.</summary>
    private readonly ScryfallDatabase database;

    /// <summary>Creates snapshot storage around the shared schema owner.</summary>
    internal ScryfallSnapshotStore(ScryfallDatabase database)
    {
        this.database = database;
    }

    /// <summary>Finds the newest snapshot matching one exact request fingerprint.</summary>
    internal Task<StoredSnapshot?> FindAsync(
        string fingerprint,
        DateTimeOffset? minimumRetrievedAtUtc,
        CancellationToken cancellationToken)
    {
        return database.FindSnapshotAsync(fingerprint, minimumRetrievedAtUtc, cancellationToken);
    }

    /// <summary>Finds one immutable snapshot by identifier.</summary>
    internal Task<StoredSnapshot?> FindByIdAsync(Guid snapshotId, CancellationToken cancellationToken)
    {
        return database.FindSnapshotByIdAsync(snapshotId, cancellationToken);
    }

    /// <summary>Saves one fully acquired immutable request snapshot.</summary>
    internal Task<StoredSnapshot> SaveAsync(
        string operation,
        string requestJson,
        string fingerprint,
        IReadOnlyList<string> pages,
        IReadOnlyList<string> members,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        return database.SaveSnapshotAsync(
            operation,
            requestJson,
            fingerprint,
            pages,
            members,
            retrievedAtUtc,
            cancellationToken);
    }

    /// <summary>Lists immutable snapshots using stable filters and pagination.</summary>
    internal Task<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>> ListAsync(
        string? operation,
        DateTimeOffset? retrievedAfterUtc,
        DateTimeOffset? retrievedBeforeUtc,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return database.ListSnapshotsAsync(
            operation,
            retrievedAfterUtc,
            retrievedBeforeUtc,
            cursor,
            pageSize,
            cancellationToken);
    }

    /// <summary>Replays one immutable snapshot page.</summary>
    internal Task<OperationResult<ScryfallSnapshotPage>> GetAsync(
        Guid snapshotId,
        string? cursor,
        int pageSize,
        bool includeRaw,
        CancellationToken cancellationToken)
    {
        return database.GetSnapshotAsync(snapshotId, cursor, pageSize, includeRaw, cancellationToken);
    }

    /// <summary>Deletes one snapshot under checksum and acknowledgement guards.</summary>
    internal Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteAsync(
        Guid snapshotId,
        string expectedChecksum,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        return database.DeleteSnapshotAsync(
            snapshotId,
            expectedChecksum,
            acknowledgeDataLoss,
            cancellationToken);
    }
}

/// <summary>
/// Owns cross-process request leases and the single provider-start pacing timeline.
/// </summary>
internal sealed class ScryfallRequestCoordinationStore
{
    /// <summary>Stores the schema and connection owner.</summary>
    private readonly ScryfallDatabase database;

    /// <summary>Creates request coordination around the shared schema owner.</summary>
    internal ScryfallRequestCoordinationStore(ScryfallDatabase database)
    {
        this.database = database;
    }

    /// <summary>Attempts to acquire one expiring exact-request lease.</summary>
    internal Task<bool> TryAcquireLeaseAsync(
        string key,
        string owner,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        return database.TryAcquireLeaseAsync(key, owner, nowUtc, leaseDuration, cancellationToken);
    }

    /// <summary>Releases one exact-request lease owned by this operation.</summary>
    internal Task ReleaseLeaseAsync(string key, string owner, CancellationToken cancellationToken)
    {
        return database.ReleaseLeaseAsync(key, owner, cancellationToken);
    }

    /// <summary>Atomically reserves the next cross-process provider request start.</summary>
    internal Task<TimeSpan> ReserveProviderStartAsync(
        DateTimeOffset nowUtc,
        TimeSpan minimumInterval,
        CancellationToken cancellationToken)
    {
        return database.ReserveProviderStartAsync(nowUtc, minimumInterval, cancellationToken);
    }
}
