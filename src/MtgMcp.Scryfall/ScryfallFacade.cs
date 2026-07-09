using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall;

/// <summary>
/// Provides the stable public Scryfall API while routing work to concrete capability owners.
/// </summary>
public sealed class ScryfallService : IDisposable
{
    /// <summary>
    /// Owns card, query, metadata, and tag evidence operations.
    /// </summary>
    private readonly ScryfallCardEvidenceOperations cards;

    /// <summary>
    /// Owns explicit corpus synchronization and generation lifecycle operations.
    /// </summary>
    private readonly ScryfallCorpusLifecycleOperations corpus;

    /// <summary>
    /// Owns immutable request-snapshot inventory, replay, and deletion operations.
    /// </summary>
    private readonly ScryfallSnapshotOperations snapshots;

    /// <summary>
    /// Creates the shared Scryfall capability with official production defaults.
    /// </summary>
    public ScryfallService(
        string dataRoot,
        bool allowLocalWrites,
        string packageVersion,
        Uri? apiBaseUri = null,
        TimeSpan? freshnessTtl = null,
        TimeProvider? timeProvider = null,
        HttpMessageHandler? handler = null)
    {
        cards = new ScryfallCardEvidenceOperations(
            dataRoot,
            allowLocalWrites,
            packageVersion,
            apiBaseUri,
            freshnessTtl,
            timeProvider,
            handler);
        corpus = new ScryfallCorpusLifecycleOperations(cards);
        snapshots = new ScryfallSnapshotOperations(cards);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.SearchAsync"/>
    public Task<OperationResult<ScryfallSearchResult>> SearchAsync(
        string query,
        string unique = "cards",
        string order = "name",
        string direction = "auto",
        bool includeExtras = false,
        bool includeMultilingual = false,
        bool includeVariations = false,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.SearchAsync(
            query,
            unique,
            order,
            direction,
            includeExtras,
            includeMultilingual,
            includeVariations,
            freshnessPolicy,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetCardAsync"/>
    public Task<OperationResult<ScryfallCardResult>> GetCardAsync(
        ScryfallCardLookup lookup,
        string freshnessPolicy = "default",
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.GetCardAsync(lookup, freshnessPolicy, includeRaw, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetCollectionAsync"/>
    public Task<OperationResult<ScryfallCollectionResult>> GetCollectionAsync(
        IReadOnlyList<ScryfallCardLookup>? lookups,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.GetCollectionAsync(
            lookups,
            freshnessPolicy,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.ResolveExactCollectionAsync"/>
    public Task<OperationResult<ScryfallExactCollectionEvidence>> ResolveExactCollectionAsync(
        IReadOnlyList<ScryfallEvidenceLookup>? lookups,
        string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        return cards.ResolveExactCollectionAsync(lookups, freshnessPolicy, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.ReplayExactCollectionAsync"/>
    public Task<OperationResult<ScryfallExactCollectionEvidence>> ReplayExactCollectionAsync(
        IReadOnlyList<ScryfallEvidenceLookup>? lookups,
        ScryfallCollectionEvidenceBinding? binding,
        CancellationToken cancellationToken = default)
    {
        return cards.ReplayExactCollectionAsync(lookups, binding, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetPrintsAsync"/>
    public Task<OperationResult<ScryfallPrintsResult>> GetPrintsAsync(
        Guid oracleId,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.GetPrintsAsync(
            oracleId,
            freshnessPolicy,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetRulingsAsync"/>
    public Task<OperationResult<ScryfallRulingsResult>> GetRulingsAsync(
        Guid oracleId,
        Guid? scryfallCardId = null,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.GetRulingsAsync(
            oracleId,
            scryfallCardId,
            freshnessPolicy,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetSetsAsync"/>
    public Task<OperationResult<ScryfallSetsResult>> GetSetsAsync(
        string? codeOrId = null,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.GetSetsAsync(
            codeOrId,
            freshnessPolicy,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetCatalogAsync"/>
    public Task<OperationResult<ScryfallCatalogResult>> GetCatalogAsync(
        string catalog,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return cards.GetCatalogAsync(catalog, freshnessPolicy, cursor, pageSize, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.AutocompleteAsync"/>
    public Task<OperationResult<ScryfallAutocompleteResult>> AutocompleteAsync(
        string query,
        bool includeExtras = false,
        string freshnessPolicy = "default",
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return cards.AutocompleteAsync(
            query,
            includeExtras,
            freshnessPolicy,
            cursor,
            pageSize,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetBulkMetadataAsync"/>
    public Task<OperationResult<ScryfallBulkMetadataResult>> GetBulkMetadataAsync(
        string freshnessPolicy = "default",
        CancellationToken cancellationToken = default)
    {
        return cards.GetBulkMetadataAsync(freshnessPolicy, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.SearchTagsAsync"/>
    public Task<OperationResult<ScryfallPage<ScryfallTag>>> SearchTagsAsync(
        string query,
        string? tagType = null,
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.SearchTagsAsync(query, tagType, cursor, pageSize, includeRaw, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCardEvidenceOperations.GetCardsByTagAsync"/>
    public Task<OperationResult<ScryfallCardsByTagResult>> GetCardsByTagAsync(
        string tagIdentity,
        string tagType,
        bool includeDescendants = false,
        string minimumWeight = "weak",
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return cards.GetCardsByTagAsync(
            tagIdentity,
            tagType,
            includeDescendants,
            minimumWeight,
            cursor,
            pageSize,
            includeRaw,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCorpusLifecycleOperations.GetStatusAsync"/>
    public Task<OperationResult<ScryfallCorpusStatus>> GetCorpusStatusAsync(
        CancellationToken cancellationToken = default)
    {
        return corpus.GetStatusAsync(cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCorpusLifecycleOperations.SyncAsync"/>
    public Task<OperationResult<ScryfallCorpusSyncResult>> SyncCorpusAsync(
        string metadataPolicy = "default",
        Guid? expectedActiveGeneration = null,
        CancellationToken cancellationToken = default)
    {
        return corpus.SyncAsync(metadataPolicy, expectedActiveGeneration, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCorpusLifecycleOperations.RollbackAsync"/>
    public Task<OperationResult<ScryfallCorpusMutationResult>> RollbackCorpusAsync(
        Guid expectedActiveGeneration,
        Guid expectedPreviousGeneration,
        bool acknowledgeActivationChange,
        CancellationToken cancellationToken = default)
    {
        return corpus.RollbackAsync(
            expectedActiveGeneration,
            expectedPreviousGeneration,
            acknowledgeActivationChange,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallCorpusLifecycleOperations.DeleteAsync"/>
    public Task<OperationResult<ScryfallCorpusMutationResult>> DeleteCorpusAsync(
        Guid expectedActiveGeneration,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken = default)
    {
        return corpus.DeleteAsync(expectedActiveGeneration, acknowledgeDataLoss, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallSnapshotOperations.ListAsync"/>
    public Task<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>> ListSnapshotsAsync(
        string? operation = null,
        DateTimeOffset? retrievedAfterUtc = null,
        DateTimeOffset? retrievedBeforeUtc = null,
        string? cursor = null,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return snapshots.ListAsync(
            operation,
            retrievedAfterUtc,
            retrievedBeforeUtc,
            cursor,
            pageSize,
            cancellationToken);
    }

    /// <inheritdoc cref="ScryfallSnapshotOperations.GetAsync"/>
    public Task<OperationResult<ScryfallSnapshotPage>> GetSnapshotAsync(
        Guid snapshotId,
        string? cursor = null,
        int pageSize = 25,
        bool includeRaw = false,
        CancellationToken cancellationToken = default)
    {
        return snapshots.GetAsync(snapshotId, cursor, pageSize, includeRaw, cancellationToken);
    }

    /// <inheritdoc cref="ScryfallSnapshotOperations.DeleteAsync"/>
    public Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteSnapshotAsync(
        Guid snapshotId,
        string expectedChecksum,
        bool acknowledgeDataLoss,
        CancellationToken cancellationToken = default)
    {
        return snapshots.DeleteAsync(snapshotId, expectedChecksum, acknowledgeDataLoss, cancellationToken);
    }

    /// <summary>
    /// Releases the shared HTTP and SQLite resources exactly once.
    /// </summary>
    public void Dispose()
    {
        cards.Dispose();
    }
}

/// <summary>
/// Owns explicit corpus synchronization, rollback, status, and deletion operations.
/// </summary>
internal sealed class ScryfallCorpusLifecycleOperations
{
    /// <summary>Stores card metadata acquisition and the shared provider runtime.</summary>
    private readonly ScryfallCardEvidenceOperations cards;

    /// <summary>Creates corpus lifecycle operations around the shared provider runtime.</summary>
    internal ScryfallCorpusLifecycleOperations(ScryfallCardEvidenceOperations cards)
    {
        this.cards = cards;
    }

    /// <summary>Reports installed corpus state without network access.</summary>
    internal Task<OperationResult<ScryfallCorpusStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return cards.CorpusStore.GetStatusAsync(
            cards.TimeProvider.GetUtcNow(),
            cards.FreshnessTtl,
            cancellationToken);
    }

    /// <summary>Synchronizes the complete fixed corpus atomically.</summary>
    internal async Task<OperationResult<ScryfallCorpusSyncResult>> SyncAsync(
        string metadataPolicy, Guid? expectedActiveGeneration, CancellationToken cancellationToken)
    {
        if (!cards.AllowLocalWrites)
        {
            return LocalWriteRequired<ScryfallCorpusSyncResult>();
        }

        if (metadataPolicy is not ("default" or "refresh"))
        {
            return new OperationInvalidInput(
                "invalid-freshness-policy",
                "Corpus sync policy must be default or refresh.");
        }

        const string leaseKey = "corpus-sync";
        string leaseOwner = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        bool acquired = await cards.CoordinationStore.TryAcquireLeaseAsync(
            leaseKey,
            leaseOwner,
            cards.TimeProvider.GetUtcNow(),
            TimeSpan.FromHours(2),
            cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            return new OperationUnavailable(
                "scryfall-corpus-sync-in-progress",
                "Another process is synchronizing the Scryfall corpus.");
        }

        try
        {
            await cards.CorpusStore.RemoveAbandonedStagingGenerationsAsync(cancellationToken)
                .ConfigureAwait(false);
            return await SyncUnderLeaseAsync(
                metadataPolicy,
                expectedActiveGeneration,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await cards.CoordinationStore.ReleaseLeaseAsync(
                leaseKey,
                leaseOwner,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Swaps active and previous complete generations under exact guards.</summary>
    internal Task<OperationResult<ScryfallCorpusMutationResult>> RollbackAsync(
        Guid expectedActiveGeneration, Guid expectedPreviousGeneration, bool acknowledgeActivationChange,
        CancellationToken cancellationToken)
    {
        return cards.AllowLocalWrites
            ? cards.CorpusStore.RollbackAsync(
                expectedActiveGeneration,
                expectedPreviousGeneration,
                acknowledgeActivationChange,
                cancellationToken)
            : Task.FromResult(LocalWriteRequired<ScryfallCorpusMutationResult>());
    }

    /// <summary>Deletes installed corpus generations under an exact active-generation guard.</summary>
    internal Task<OperationResult<ScryfallCorpusMutationResult>> DeleteAsync(
        Guid expectedActiveGeneration, bool acknowledgeDataLoss, CancellationToken cancellationToken)
    {
        return cards.AllowLocalWrites
            ? cards.CorpusStore.DeleteAsync(
                expectedActiveGeneration,
                acknowledgeDataLoss,
                cancellationToken)
            : Task.FromResult(LocalWriteRequired<ScryfallCorpusMutationResult>());
    }

    /// <summary>Performs one complete synchronization while holding the cross-process lease.</summary>
    private async Task<OperationResult<ScryfallCorpusSyncResult>> SyncUnderLeaseAsync(
        string metadataPolicy,
        Guid? expectedActiveGeneration,
        CancellationToken cancellationToken)
    {
        OperationResult<ScryfallCorpusStatus> statusResult = await GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        if (statusResult is not OperationSuccess<ScryfallCorpusStatus> statusSuccess)
        {
            return ForwardFailure<ScryfallCorpusStatus, ScryfallCorpusSyncResult>(statusResult);
        }

        if (expectedActiveGeneration is Guid expected &&
            statusSuccess.Data.Active?.GenerationId != expected)
        {
            return new OperationConflict(
                "stale-scryfall-generation",
                "The active corpus generation changed before synchronization.");
        }

        OperationResult<ScryfallBulkMetadataResult> metadataResult = await cards.GetBulkMetadataAsync(
            metadataPolicy == "refresh" ? "refresh" : "default",
            cancellationToken).ConfigureAwait(false);
        if (metadataResult is not OperationSuccess<ScryfallBulkMetadataResult> metadata)
        {
            return ForwardFailure<ScryfallBulkMetadataResult, ScryfallCorpusSyncResult>(metadataResult);
        }

        DateTimeOffset now = cards.TimeProvider.GetUtcNow();
        if (await cards.CorpusStore.ActiveMetadataMatchesAsync(metadata.Data.Datasets, cancellationToken)
                .ConfigureAwait(false))
        {
            await cards.CorpusStore.RecordMetadataCheckAsync(now, cancellationToken).ConfigureAwait(false);
            OperationResult<ScryfallCorpusStatus> currentResult = await GetStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            if (currentResult is not OperationSuccess<ScryfallCorpusStatus> current)
            {
                return ForwardFailure<ScryfallCorpusStatus, ScryfallCorpusSyncResult>(currentResult);
            }

            return new OperationSuccess<ScryfallCorpusSyncResult>(new ScryfallCorpusSyncResult(
                "unchanged",
                current.Data.Active!.GenerationId,
                current.Data.Previous?.GenerationId,
                current.Data.Active.Datasets));
        }

        OperationUnavailable? spaceFailure = CheckDiskSpace(metadata.Data.Datasets);
        if (spaceFailure is not null)
        {
            return spaceFailure;
        }

        Guid generationId = await cards.CorpusStore.BeginGenerationAsync(now, cancellationToken)
            .ConfigureAwait(false);
        string stage = "initialization";
        string? datasetType = null;
        try
        {
            foreach (ScryfallBulkData dataset in metadata.Data.Datasets)
            {
                datasetType = dataset.Type;
                stage = "download";
                await using ProviderDownload download = await cards.Provider.OpenDownloadAsync(
                    dataset.JsonlDownloadUri,
                    cancellationToken).ConfigureAwait(false);
                stage = "import";
                await using GZipStream decompressed = new(
                    download.Stream,
                    CompressionMode.Decompress,
                    leaveOpen: true);
                await cards.CorpusStore.ImportDatasetAsync(
                    generationId,
                    dataset,
                    decompressed,
                    cancellationToken).ConfigureAwait(false);
            }

            datasetType = null;
            stage = "activation";
            return await cards.CorpusStore.ActivateGenerationAsync(generationId, now, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await cards.CorpusStore.DeleteGenerationAsync(generationId, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException or IOException or
                ScryfallProviderException or SqliteException)
        {
            await cards.CorpusStore.DeleteGenerationAsync(generationId, CancellationToken.None)
                .ConfigureAwait(false);
            string subject = datasetType is null ? "corpus" : $"{datasetType} dataset";
            return exception switch
            {
                ScryfallProviderException providerFailure =>
                    new OperationUnavailable(providerFailure.ReasonCode, providerFailure.Message),
                InvalidDataException or JsonException =>
                    new OperationUnavailable(
                        "invalid-scryfall-corpus",
                        $"The Scryfall {subject} did not match the expected contract during {stage}."),
                IOException =>
                    new OperationUnavailable(
                        "scryfall-corpus-io-failed",
                        $"The Scryfall {subject} could not be read completely during {stage}."),
                SqliteException =>
                    new OperationUnavailable(
                        "scryfall-corpus-storage-failed",
                        $"The Scryfall {subject} could not be stored during {stage}."),
                _ => new OperationUnavailable(
                    "scryfall-corpus-sync-failed",
                    "Scryfall corpus synchronization failed without changing the active generation."),
            };
        }
    }

    /// <summary>Performs a conservative free-space preflight before corpus synchronization.</summary>
    private OperationUnavailable? CheckDiskSpace(IReadOnlyList<ScryfallBulkData> datasets)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(cards.DataRoot))!;
            DriveInfo drive = new(root);
            long sourceBytes = datasets.Sum(value => value.Size);
            long required = checked(sourceBytes * 3);
            return drive.AvailableFreeSpace >= required
                ? null
                : new OperationUnavailable(
                    "insufficient-scryfall-disk-space",
                    "Scryfall corpus synchronization requires more free disk space.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return new OperationUnavailable(
                "scryfall-disk-check-unavailable",
                "Available disk space could not be verified safely.");
        }
    }

    /// <summary>Returns the uniform read-only failure for corpus mutations.</summary>
    private static OperationResult<T> LocalWriteRequired<T>()
    {
        return new OperationUnavailable(
            "local-write-required",
            "This Scryfall operation requires local mode to record coordinated evidence.");
    }

    /// <summary>Preserves every non-success case while changing the generic success type.</summary>
    private static OperationResult<TTarget> ForwardFailure<TSource, TTarget>(
        OperationResult<TSource> result)
    {
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            OperationSuccess<TSource> => new OperationUnavailable(
                "unexpected-success-shape",
                "The operation returned an unexpected success shape."),
        };
    }
}

/// <summary>
/// Owns immutable request-snapshot inventory, replay, and deletion operations.
/// </summary>
internal sealed class ScryfallSnapshotOperations
{
    /// <summary>Stores the shared persistence and mode state.</summary>
    private readonly ScryfallCardEvidenceOperations cards;

    /// <summary>Creates snapshot operations around one shared evidence runtime.</summary>
    internal ScryfallSnapshotOperations(ScryfallCardEvidenceOperations cards)
    {
        this.cards = cards;
    }

    /// <summary>Lists immutable request snapshots using stable filters and pagination.</summary>
    internal Task<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>> ListAsync(
        string? operation, DateTimeOffset? retrievedAfterUtc, DateTimeOffset? retrievedBeforeUtc,
        string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        if (retrievedAfterUtc is DateTimeOffset after &&
            retrievedBeforeUtc is DateTimeOffset before &&
            after > before)
        {
            return Task.FromResult<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>>(
                new OperationInvalidInput(
                    "invalid-snapshot-time-range",
                    "Snapshot retrieval bounds must be in chronological order."));
        }

        OperationInvalidInput? failure = ValidatePageSize(pageSize, includeRaw: false);
        return failure is null
            ? cards.SnapshotStore.ListAsync(
                operation,
                retrievedAfterUtc,
                retrievedBeforeUtc,
                cursor,
                pageSize,
                cancellationToken)
            : Task.FromResult<OperationResult<ScryfallPage<ScryfallSnapshotSummary>>>(failure);
    }

    /// <summary>Replays one immutable snapshot page.</summary>
    internal Task<OperationResult<ScryfallSnapshotPage>> GetAsync(
        Guid snapshotId, string? cursor, int pageSize, bool includeRaw, CancellationToken cancellationToken)
    {
        OperationInvalidInput? failure = ValidatePageSize(pageSize, includeRaw);
        return failure is null
            ? cards.SnapshotStore.GetAsync(snapshotId, cursor, pageSize, includeRaw, cancellationToken)
            : Task.FromResult<OperationResult<ScryfallSnapshotPage>>(failure);
    }

    /// <summary>Deletes one immutable snapshot under checksum and acknowledgement guards.</summary>
    internal Task<OperationResult<ScryfallSnapshotDeleteResult>> DeleteAsync(
        Guid snapshotId, string expectedChecksum, bool acknowledgeDataLoss,
        CancellationToken cancellationToken)
    {
        if (!cards.AllowLocalWrites)
        {
            return Task.FromResult<OperationResult<ScryfallSnapshotDeleteResult>>(
                new OperationUnavailable(
                    "local-write-required",
                    "This Scryfall operation requires local mode to record coordinated evidence."));
        }

        if (string.IsNullOrWhiteSpace(expectedChecksum))
        {
            return Task.FromResult<OperationResult<ScryfallSnapshotDeleteResult>>(
                new OperationInvalidInput(
                    "invalid-snapshot-checksum",
                    "Expected snapshot checksum cannot be blank."));
        }

        return cards.SnapshotStore.DeleteAsync(
            snapshotId,
            expectedChecksum,
            acknowledgeDataLoss,
            cancellationToken);
    }

    /// <summary>Validates compact and raw snapshot page bounds.</summary>
    private static OperationInvalidInput? ValidatePageSize(int pageSize, bool includeRaw)
    {
        int maximum = includeRaw ? 25 : 100;
        return pageSize is >= 1 && pageSize <= maximum
            ? null
            : new OperationInvalidInput(
                "invalid-page-size",
                $"Page size must be from 1 through {maximum}.");
    }
}
