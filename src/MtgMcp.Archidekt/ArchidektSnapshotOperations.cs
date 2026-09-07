using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Owns Archidekt named snapshot reads, writes, preview, and guarded restore workflows.
/// </summary>
internal sealed class ArchidektSnapshotOperations
{
    /// <summary>
    /// Provides named snapshot provider routes for this workflow owner.
    /// </summary>
    private readonly ArchidektSnapshotTransport snapshotTransport;

    /// <summary>
    /// Provides deck provider routes needed to check snapshot source and restore state.
    /// </summary>
    private readonly ArchidektDeckTransport deckTransport;

    /// <summary>
    /// Applies a verified deck change when restoring a snapshot.
    /// </summary>
    private readonly ArchidektDeckOperations deckOperations;

    /// <summary>
    /// Stores the hard provider request ceiling for standalone operations.
    /// </summary>
    private readonly int maximumRequestsPerOperation;

    /// <summary>
    /// Creates snapshot workflows over their snapshot and deck workflow owners.
    /// </summary>
    internal ArchidektSnapshotOperations(
        ArchidektSnapshotTransport snapshotTransport,
        ArchidektDeckTransport deckTransport,
        ArchidektDeckOperations deckOperations,
        int maximumRequestsPerOperation)
    {
        this.snapshotTransport = snapshotTransport ?? throw new ArgumentNullException(nameof(snapshotTransport));
        this.deckTransport = deckTransport ?? throw new ArgumentNullException(nameof(deckTransport));
        this.deckOperations = deckOperations ?? throw new ArgumentNullException(nameof(deckOperations));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestsPerOperation);
        this.maximumRequestsPerOperation = maximumRequestsPerOperation;
    }

    /// <summary>
    /// Lists exact named snapshot metadata for one deck.
    /// </summary>
    internal Task<OperationResult<RemoteNamedSnapshotPage>> ListAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(
            maximumRequestsPerOperation,
            budget => snapshotTransport.ListAsync(deckId, budget, cancellationToken));
    }

    /// <summary>
    /// Gets one complete saved snapshot and cross-checks its owning deck.
    /// </summary>
    internal Task<OperationResult<RemoteNamedSnapshot>> GetAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(
            maximumRequestsPerOperation,
            budget => snapshotTransport.GetAsync(deckId, snapshotId, budget, cancellationToken));
    }

    /// <summary>
    /// Creates a named snapshot only when the source deck still matches its caller fingerprint.
    /// </summary>
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> CreateAsync(
        ArchidektSnapshotCreateRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RemoteDeckSnapshot deck = await deckTransport.GetAsync(
                    request.DeckId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedRemoteFingerprint,
                    deck.RemoteFingerprint,
                    "remote-deck-changed");
                string name = ArchidektContract.Required(request.Name, nameof(request.Name));
                await snapshotTransport.SendCreateAsync(
                    request.DeckId,
                    new { name, description = ArchidektContract.Optional(request.Description) },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteNamedSnapshotPage after = await snapshotTransport.ListAsync(
                    request.DeckId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteNamedSnapshotSummary[] matches = after.Items
                    .Where(value => string.Equals(value.Name, name, StringComparison.Ordinal))
                    .ToArray();
                return matches.Length == 1
                    ? matches[0]
                    : throw Conflict(
                        "snapshot-create-unverified",
                        "Archidekt did not expose one unambiguous created snapshot.");
            });
    }

    /// <summary>
    /// Updates supported snapshot metadata only when its exact source checksum still matches.
    /// </summary>
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> UpdateAsync(
        ArchidektSnapshotUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RemoteNamedSnapshot current = await snapshotTransport.GetAsync(
                    request.DeckId,
                    request.SnapshotId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedChecksum,
                    current.Summary.Checksum,
                    "snapshot-changed");
                string name = ArchidektContract.Required(request.Name, nameof(request.Name));
                await snapshotTransport.SendUpdateAsync(
                    request.SnapshotId,
                    new { name },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteNamedSnapshot updated = await snapshotTransport.GetAsync(
                    request.DeckId,
                    request.SnapshotId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(updated.Summary.Name, name, StringComparison.Ordinal))
                {
                    throw Conflict(
                        "snapshot-update-unverified",
                        "Archidekt snapshot metadata did not match the requested update.");
                }

                return updated.Summary;
            });
    }

    /// <summary>
    /// Deletes one unchanged confirmed snapshot and verifies collection absence.
    /// </summary>
    internal Task<OperationResult<ArchidektApplyResult>> DeleteAsync(
        ArchidektSnapshotDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RequireConfirmation(request.Confirmation, $"delete snapshot {request.SnapshotId}");
                RemoteNamedSnapshot current = await snapshotTransport.GetAsync(
                    request.DeckId,
                    request.SnapshotId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedChecksum,
                    current.Summary.Checksum,
                    "snapshot-changed");
                ArchidektRemoteOperation operation = new(
                    1,
                    "snapshot-delete",
                    request.SnapshotId,
                    "Delete one exact named snapshot.");
                try
                {
                    await snapshotTransport.SendDeleteAsync(
                        request.SnapshotId,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArchidektProviderException exception)
                    when (exception.Kind == ArchidektFailureKind.Unavailable)
                {
                    return PartialResult(request.DeckId, operation, exception.Message);
                }

                RemoteNamedSnapshotPage after = await snapshotTransport.ListAsync(
                    request.DeckId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (after.Items.Any(value =>
                    string.Equals(value.SnapshotId, request.SnapshotId, StringComparison.Ordinal)))
                {
                    throw Conflict(
                        "snapshot-delete-unverified",
                        "Archidekt still lists the snapshot after deletion.");
                }

                return AppliedResult(request.DeckId, operation, after.CollectionChecksum);
            });
    }

    /// <summary>
    /// Previews an exact named snapshot restore against a fresh current remote deck.
    /// </summary>
    internal Task<OperationResult<ArchidektSnapshotRestorePreview>> PreviewRestoreAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                (RemoteDeckSnapshot current, RemoteNamedSnapshot snapshot) = await GetRestoreSourcesAsync(
                    deckId,
                    snapshotId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteDeckSnapshot target = CreateRestoreTarget(current, snapshot.Deck);
                ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
                budget.EnsureRequestBound(plan.PredictedProviderRequests);
                IReadOnlyList<ArchidektDifference> differences = ContentDifference(current, target, plan);
                string previewFingerprint = RestorePreviewFingerprint(current, snapshot, plan);
                return new ArchidektSnapshotRestorePreview(
                    deckId,
                    snapshotId,
                    snapshot.Summary.Checksum,
                    snapshot.Deck.ContentFingerprint,
                    current.RemoteFingerprint,
                    previewFingerprint,
                    differences,
                    plan.PublicOperations,
                    plan.PredictedProviderRequests);
            });
    }

    /// <summary>
    /// Restores one unchanged snapshot onto one unchanged remote deck after replaying every preview guard.
    /// </summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyRestoreAsync(
        ArchidektSnapshotRestoreApplyRequest request,
        CancellationToken cancellationToken)
    {
        return ArchidektOperationResults.ExecuteAsync(maximumRequestsPerOperation,
            async budget =>
            {
                RequireConfirmation(request.Confirmation, $"restore snapshot {request.SnapshotId}");
                (RemoteDeckSnapshot current, RemoteNamedSnapshot snapshot) = await GetRestoreSourcesAsync(
                    request.DeckId,
                    request.SnapshotId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedSnapshotChecksum,
                    snapshot.Summary.Checksum,
                    "snapshot-changed");
                RequireFingerprint(
                    request.ExpectedSnapshotContentFingerprint,
                    snapshot.Deck.ContentFingerprint,
                    "snapshot-content-changed");
                RequireFingerprint(
                    request.ExpectedRemoteFingerprint,
                    current.RemoteFingerprint,
                    "remote-deck-changed");
                RemoteDeckSnapshot target = CreateRestoreTarget(current, snapshot.Deck);
                ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
                RequireFingerprint(
                    request.PreviewFingerprint,
                    RestorePreviewFingerprint(current, snapshot, plan),
                    "restore-preview-changed");
                return await deckOperations.ApplyPlanAsync(
                    current,
                    target,
                    plan,
                    budget,
                    cancellationToken).ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Fetches both immutable sources used by snapshot restore preview and apply.
    /// </summary>
    private async Task<(RemoteDeckSnapshot Current, RemoteNamedSnapshot Snapshot)> GetRestoreSourcesAsync(
        string deckId,
        string snapshotId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        RemoteDeckSnapshot current = await deckTransport.GetAsync(
            deckId,
            requireAuthentication: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        RemoteNamedSnapshot snapshot = await snapshotTransport.GetAsync(
            deckId,
            snapshotId,
            budget,
            cancellationToken).ConfigureAwait(false);
        return (current, snapshot);
    }

    /// <summary>
    /// Computes the immutable snapshot restore preview identity.
    /// </summary>
    private static string RestorePreviewFingerprint(
        RemoteDeckSnapshot current,
        RemoteNamedSnapshot snapshot,
        ArchidektRemotePlan plan)
    {
        return ArchidektContract.Fingerprint(new
        {
            current.RemoteFingerprint,
            snapshot.Summary.Checksum,
            snapshot.Deck.ContentFingerprint,
            plan.PlanFingerprint,
        });
    }

    /// <summary>
    /// Preserves current folder placement because named deck snapshots do not own that account-level relationship.
    /// </summary>
    private static RemoteDeckSnapshot CreateRestoreTarget(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot snapshot)
    {
        return snapshot with
        {
            RemoteId = current.RemoteId,
            RemoteUri = current.RemoteUri,
            Name = current.Name,
            Visibility = current.Visibility,
            ParentFolderId = current.ParentFolderId,
        };
    }

    /// <summary>
    /// Produces one exact content difference when two remote observations are not equivalent.
    /// </summary>
    private static IReadOnlyList<ArchidektDifference> ContentDifference(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        ArchidektRemotePlan plan)
    {
        if (plan.PlannedOperations.Count == 0)
        {
            return [];
        }

        return
        [
            new ArchidektDifference(
                "/remote/content",
                "changed",
                BaselineValue: null,
                current.ContentFingerprint,
                target.ContentFingerprint),
        ];
    }

    /// <summary>
    /// Requires an exact non-case-folded confirmation phrase.
    /// </summary>
    private static void RequireConfirmation(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "confirmation-required",
                "The exact confirmation phrase is required.");
        }
    }

    /// <summary>
    /// Requires one caller fingerprint to match freshly retrieved evidence.
    /// </summary>
    private static void RequireFingerprint(string expected, string actual, string reasonCode)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw Conflict(reasonCode, "Provider evidence changed after the preview.");
        }
    }

    /// <summary>
    /// Maps one safe operation descriptor into a final status row.
    /// </summary>
    private static ArchidektOperationStatus Status(
        ArchidektRemoteOperation operation,
        string status,
        string message)
    {
        return new ArchidektOperationStatus(
            operation.Sequence,
            operation.Kind,
            operation.Subject,
            status,
            message);
    }

    /// <summary>
    /// Creates a verified one-operation success result.
    /// </summary>
    private static ArchidektApplyResult AppliedResult(
        string remoteId,
        ArchidektRemoteOperation operation,
        string? finalFingerprint)
    {
        return new ArchidektApplyResult(
            "applied",
            LocalDeckId: null,
            LocalRevision: null,
            remoteId,
            finalFingerprint,
            [Status(operation, "applied", "Provider absence was verified.")]);
    }

    /// <summary>
    /// Creates a one-operation unknown-state result after an ambiguous mutation failure.
    /// </summary>
    private static ArchidektApplyResult PartialResult(
        string remoteId,
        ArchidektRemoteOperation operation,
        string message)
    {
        return new ArchidektApplyResult(
            "partial",
            LocalDeckId: null,
            LocalRevision: null,
            remoteId,
            FinalRemoteFingerprint: null,
            [Status(operation, "unknown", message)]);
    }

    /// <summary>
    /// Creates one provider-state conflict without transport details.
    /// </summary>
    private static ArchidektProviderException Conflict(string reasonCode, string message)
    {
        return new ArchidektProviderException(
            ArchidektFailureKind.Conflict,
            reasonCode,
            message);
    }
}
