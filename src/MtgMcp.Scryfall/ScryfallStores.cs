using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Owns corpus generations, card/ruling/tag reads, and corpus lifecycle persistence.
/// </summary>
internal sealed class ScryfallCorpusStore
{
    /// <summary>Stores the schema and connection owner.</summary>
    private readonly ScryfallDatabase database;

    /// <summary>Creates corpus storage around the shared schema owner.</summary>
    internal ScryfallCorpusStore(ScryfallDatabase database)
    {
        this.database = database;
    }

    /// <summary>Gets the active complete generation identifier.</summary>
    internal Task<Guid?> GetActiveGenerationIdAsync(CancellationToken cancellationToken)
    {
        return database.GetActiveGenerationIdAsync(cancellationToken);
    }

    /// <summary>Gets installed corpus generation status.</summary>
    internal Task<OperationResult<ScryfallCorpusStatus>> GetStatusAsync(
        DateTimeOffset checkedAtUtc,
        TimeSpan freshnessTtl,
        CancellationToken cancellationToken)
    {
        return database.GetCorpusStatusAsync(checkedAtUtc, freshnessTtl, cancellationToken);
    }

    /// <summary>Finds one card in the active corpus.</summary>
    internal Task<StoredCorpusObject?> FindCardAsync(
        ScryfallCardLookup lookup,
        CancellationToken cancellationToken)
    {
        return database.FindCardAsync(lookup, cancellationToken);
    }

    /// <summary>Finds one card in an exact retained generation.</summary>
    internal Task<StoredCorpusObject?> FindCardInGenerationAsync(
        ScryfallCardLookup lookup,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        return database.FindCardInGenerationAsync(lookup, generationId, cancellationToken);
    }

    /// <summary>Finds one exact-language card in a retained generation.</summary>
    internal Task<StoredCorpusObject?> FindCardInGenerationAsync(
        ScryfallCardLookup lookup,
        Guid generationId,
        string? requiredLanguage,
        CancellationToken cancellationToken)
    {
        return database.FindCardInGenerationAsync(
            lookup,
            generationId,
            requiredLanguage,
            cancellationToken);
    }

    /// <summary>Reports whether one complete generation remains retained.</summary>
    internal Task<bool> ContainsCompleteGenerationAsync(
        Guid generationId,
        CancellationToken cancellationToken)
    {
        return database.ContainsCompleteGenerationAsync(generationId, cancellationToken);
    }

    /// <summary>Gets every printing for one Oracle identity.</summary>
    internal Task<StoredCorpusCollection?> GetPrintsAsync(
        Guid oracleId,
        CancellationToken cancellationToken)
    {
        return database.GetPrintsAsync(oracleId, cancellationToken);
    }

    /// <summary>Gets every ruling for one Oracle identity.</summary>
    internal Task<StoredCorpusCollection?> GetRulingsAsync(
        Guid oracleId,
        CancellationToken cancellationToken)
    {
        return database.GetRulingsAsync(oracleId, cancellationToken);
    }

    /// <summary>Gets direct Oracle and artwork tag evidence in one retained generation.</summary>
    internal Task<IReadOnlyList<ScryfallTagEvidence>> GetDirectTagsInGenerationAsync(
        Guid generationId,
        Guid? oracleId,
        IReadOnlyList<Guid> illustrationIds,
        DateTimeOffset retrievedAtUtc,
        CancellationToken cancellationToken)
    {
        return database.GetDirectTagsInGenerationAsync(
            generationId,
            oracleId,
            illustrationIds,
            retrievedAtUtc,
            cancellationToken);
    }

    /// <summary>Searches installed community-tag metadata.</summary>
    internal Task<OperationResult<ScryfallPage<ScryfallTag>>> SearchTagsAsync(
        string query,
        string? tagType,
        bool includeRaw,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return database.SearchTagsAsync(query, tagType, includeRaw, cursor, pageSize, cancellationToken);
    }

    /// <summary>Gets cards and assignments for one exact community-tag expression.</summary>
    internal Task<OperationResult<StoredCardsByTag>> GetCardsByTagAsync(
        string tagIdentity,
        string tagType,
        bool includeDescendants,
        string minimumWeight,
        CancellationToken cancellationToken)
    {
        return database.GetCardsByTagAsync(
            tagIdentity,
            tagType,
            includeDescendants,
            minimumWeight,
            cancellationToken);
    }

    /// <summary>Creates one staging generation.</summary>
    internal Task<Guid> BeginGenerationAsync(
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        return database.BeginGenerationAsync(createdAtUtc, cancellationToken);
    }

    /// <summary>Removes incomplete generations left by interrupted synchronization.</summary>
    internal Task RemoveAbandonedStagingGenerationsAsync(CancellationToken cancellationToken)
    {
        return database.RemoveAbandonedStagingGenerationsAsync(cancellationToken);
    }

    /// <summary>Streams one validated official dataset into a staging generation.</summary>
    internal Task<ScryfallCorpusDatasetStatus> ImportDatasetAsync(
        Guid generationId,
        ScryfallBulkData metadata,
        Stream content,
        CancellationToken cancellationToken)
    {
        return database.ImportDatasetAsync(
            generationId,
            metadata,
            content,
            cancellationToken);
    }

    /// <summary>Atomically activates one complete generation.</summary>
    internal Task<OperationResult<ScryfallCorpusSyncResult>> ActivateGenerationAsync(
        Guid generationId,
        DateTimeOffset activatedAtUtc,
        CancellationToken cancellationToken)
    {
        return database.ActivateGenerationAsync(
            generationId,
            activatedAtUtc,
            cancellationToken);
    }

    /// <summary>Deletes one non-active generation after failed or superseded work.</summary>
    internal Task DeleteGenerationAsync(Guid generationId, CancellationToken cancellationToken)
    {
        return database.DeleteGenerationAsync(generationId, cancellationToken);
    }

    /// <summary>Checks whether active corpus datasets match provider metadata exactly.</summary>
    internal Task<bool> ActiveMetadataMatchesAsync(
        IReadOnlyList<ScryfallBulkData> datasets,
        CancellationToken cancellationToken)
    {
        return database.ActiveMetadataMatchesAsync(datasets, cancellationToken);
    }

    /// <summary>Records one completed provider metadata check.</summary>
    internal Task RecordMetadataCheckAsync(
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        return database.RecordMetadataCheckAsync(checkedAtUtc, cancellationToken);
    }

    /// <summary>Swaps active and previous complete generations.</summary>
    internal Task<OperationResult<ScryfallCorpusMutationResult>> RollbackAsync(
        Guid expectedActiveGeneration,
        Guid expectedPreviousGeneration,
        bool acknowledgeActivationChange,
        CancellationToken cancellationToken)
    {
        return database.RollbackCorpusAsync(
            expectedActiveGeneration,
            expectedPreviousGeneration,
            acknowledgeActivationChange,
            cancellationToken);
    }

    /// <summary>Deletes all installed corpus generations under an active-generation guard.</summary>
    internal Task<OperationResult<ScryfallCorpusMutationResult>> DeleteAsync(
        Guid expectedActiveGeneration,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        return database.DeleteCorpusAsync(expectedActiveGeneration, acknowledgeDataLoss, cancellationToken);
    }
}

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
