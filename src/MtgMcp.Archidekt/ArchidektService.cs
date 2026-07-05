using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Exposes guarded Archidekt evidence and workflow operations over the isolated provider transport.
/// </summary>
public sealed class ArchidektService : IDisposable
{
    /// <summary>
    /// Owns every credential, HTTP, pacing, retry, and provider-route concern.
    /// </summary>
    private readonly ArchidektTransport transport;

    /// <summary>
    /// Stores the hard provider request ceiling applied independently to every public call.
    /// </summary>
    private readonly int maximumRequestsPerOperation;

    /// <summary>
    /// Creates a production service over the configured Archidekt account.
    /// </summary>
    public ArchidektService(ArchidektOptions options, string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        transport = new ArchidektTransport(options, packageVersion);
        maximumRequestsPerOperation = options.MaximumRequestsPerOperation;
    }

    /// <summary>
    /// Creates a deterministic service over an injected transport.
    /// </summary>
    internal ArchidektService(ArchidektTransport transport, int maximumRequestsPerOperation)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestsPerOperation);
        this.maximumRequestsPerOperation = maximumRequestsPerOperation;
    }

    /// <summary>
    /// Reports redacted credential readiness without provider I/O.
    /// </summary>
    public OperationResult<ArchidektAuthStatus> GetAuthStatus()
    {
        return new OperationSuccess<ArchidektAuthStatus>(transport.GetAuthStatus());
    }

    /// <summary>
    /// Begins one hard-bounded provider-request scope for a composed MCP tool invocation.
    /// </summary>
    public ArchidektOperationScope BeginOperation()
    {
        return new ArchidektOperationScope(maximumRequestsPerOperation);
    }

    /// <summary>
    /// Lists one bounded authenticated page of the configured user's decks.
    /// </summary>
    public Task<OperationResult<RemoteDeckPage>> ListDecksAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return ListDecksAsync(cursor, pageSize, BeginOperation(), cancellationToken);
    }

    /// <summary>
    /// Lists one page while charging a caller-owned composed-operation budget.
    /// </summary>
    public Task<OperationResult<RemoteDeckPage>> ListDecksAsync(
        string? cursor,
        int pageSize,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationScope);
        return ExecuteAsync(
            operationScope.Budget,
            budget => transport.ListDecksAsync(cursor, pageSize, budget, cancellationToken));
    }

    /// <summary>
    /// Gets one fresh public or authenticated remote deck observation.
    /// </summary>
    public Task<OperationResult<RemoteDeckSnapshot>> GetDeckAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return GetDeckAsync(deckId, BeginOperation(), cancellationToken);
    }

    /// <summary>
    /// Gets one remote deck while charging a caller-owned composed-operation budget.
    /// </summary>
    public Task<OperationResult<RemoteDeckSnapshot>> GetDeckAsync(
        string deckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationScope);
        return ExecuteAsync(
            operationScope.Budget,
            async budget =>
            {
                try
                {
                    return await transport.GetDeckAsync(
                        deckId,
                        requireAuthentication: false,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArchidektProviderException exception)
                    when (exception.ReasonCode is
                        "provider-forbidden" or
                        "provider-request-rejected" or
                        "provider-entity-not-found")
                {
                    return await transport.GetDeckAsync(
                        deckId,
                        requireAuthentication: true,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
            });
    }

    /// <summary>
    /// Creates one private-by-default empty remote deck shell and verifies it by fresh read-back.
    /// </summary>
    public Task<OperationResult<RemoteDeckSnapshot>> CreateDeckAsync(
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteDeckSnapshot created = await transport.CreateDeckAsync(
                    request,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteDeckSnapshot verified = await transport.GetDeckAsync(
                    created.RemoteId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(created.Name, verified.Name, StringComparison.Ordinal) ||
                    !string.Equals(created.Visibility, verified.Visibility, StringComparison.Ordinal))
                {
                    throw Conflict(
                        "remote-verification-mismatch",
                        "Archidekt created a deck whose verified state did not match the request.");
                }

                return verified;
            });
    }

    /// <summary>
    /// Deletes one unchanged exact remote deck and verifies absence through authenticated listing evidence.
    /// </summary>
    public Task<OperationResult<ArchidektApplyResult>> DeleteDeckAsync(
        ArchidektDeckDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RequireConfirmation(request.Confirmation, $"delete {request.DeckId}");
                RemoteDeckSnapshot current = await transport.GetDeckAsync(
                    request.DeckId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedRemoteFingerprint,
                    current.RemoteFingerprint,
                    "remote-deck-changed");
                try
                {
                    await transport.DeleteDeckAsync(
                        request.DeckId,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArchidektProviderException exception)
                    when (exception.Kind == ArchidektFailureKind.Unavailable)
                {
                    return PartialResult(
                        request.DeckId,
                        new ArchidektRemoteOperation(1, "deck-delete", request.DeckId, "Delete one exact deck."),
                        exception.Message);
                }

                bool present = await DeckAppearsInAuthenticatedListingAsync(
                    request.DeckId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (present)
                {
                    throw Conflict(
                        "remote-delete-unverified",
                        "Archidekt still lists the deck after deletion.");
                }

                return new ArchidektApplyResult(
                    "applied",
                    LocalDeckId: null,
                    LocalRevision: null,
                    request.DeckId,
                    FinalRemoteFingerprint: null,
                    [new ArchidektOperationStatus(
                        1,
                        "deck-delete",
                        request.DeckId,
                        "applied",
                        "Verified absent from the authenticated deck listing.")]);
            });
    }

    /// <summary>
    /// Lists the complete authenticated folder tree and its canonical fingerprint.
    /// </summary>
    public Task<OperationResult<RemoteFolderTree>> ListFoldersAsync(
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteFolderTree tree = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                return await EnrichFolderDecksAsync(tree, budget, cancellationToken)
                    .ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Gets one authenticated folder detail and its direct contents.
    /// </summary>
    public Task<OperationResult<RemoteFolderTree>> GetFolderAsync(
        string folderId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteFolderTree tree = await transport.GetFolderAsync(
                    folderId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                return await EnrichFolderDecksAsync(tree, budget, cancellationToken)
                    .ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Creates one folder under an exact parent and verifies it in a fresh tree.
    /// </summary>
    public Task<OperationResult<RemoteFolderRecord>> CreateFolderAsync(
        ArchidektFolderCreateRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteFolderTree before = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                string parentFolderId = ResolveProviderParent(before, request.ParentFolderId);
                string createdFolderId = await transport.CreateFolderAsync(
                    request with { ParentFolderId = parentFolderId },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderTree verified = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderRecord? match = verified.Items.FirstOrDefault(value =>
                    string.Equals(value.FolderId, createdFolderId, StringComparison.Ordinal));
                return match ?? throw Conflict(
                    "folder-create-unverified",
                    "Archidekt did not return the created folder in a fresh tree.");
            });
    }

    /// <summary>
    /// Updates allowlisted folder metadata only when the fresh tree still matches the caller guard.
    /// </summary>
    public Task<OperationResult<RemoteFolderRecord>> UpdateFolderAsync(
        ArchidektFolderUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteFolderTree before = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                before = await EnrichFolderDecksAsync(before, budget, cancellationToken)
                    .ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedTreeFingerprint,
                    before.TreeFingerprint,
                    "folder-tree-changed");
                RemoteFolderRecord current = FindFolder(before, request.FolderId);
                string? requestedParent = null;
                if (request.UpdateParent)
                {
                    requestedParent = ResolveProviderParent(before, request.ParentFolderId);
                    PreventFolderCycle(before, request.FolderId, requestedParent);
                }

                string name = request.Name is null
                    ? current.Name
                    : ArchidektContract.Required(request.Name, nameof(request.Name));
                string visibility = request.Visibility is null
                    ? current.Visibility
                    : NormalizeVisibility(request.Visibility);
                object payload = new
                {
                    name,
                    @private = visibility == "private",
                    parentFolder = ArchidektTransport.ParseProviderId(
                        request.UpdateParent ? requestedParent : current.ParentFolderId),
                };
                await transport.SendFolderUpdateAsync(
                    request.FolderId,
                    payload,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderTree after = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteFolderRecord updated = FindFolder(after, request.FolderId);
                string? expectedParent = request.UpdateParent
                    ? requestedParent
                    : current.ParentFolderId;
                if (!string.Equals(updated.Name, name, StringComparison.Ordinal) ||
                    !string.Equals(updated.Visibility, visibility, StringComparison.Ordinal) ||
                    !string.Equals(updated.ParentFolderId, expectedParent, StringComparison.Ordinal))
                {
                    throw Conflict(
                        "folder-update-unverified",
                        "Archidekt folder state did not match the requested update.");
                }

                return updated;
            });
    }

    /// <summary>
    /// Moves exact typed folder items after stale-source, missing-item, and cycle preflight checks.
    /// </summary>
    public Task<OperationResult<ArchidektFolderMoveResult>> MoveFolderItemsAsync(
        ArchidektFolderMoveRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteFolderTree before = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                before = await EnrichFolderDecksAsync(before, budget, cancellationToken)
                    .ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedTreeFingerprint,
                    before.TreeFingerprint,
                    "folder-tree-changed");
                string destinationFolderId = ResolveProviderParent(before, request.DestinationFolderId);
                ArchidektFolderMoveItem[] items = DeduplicateMoveItems(request.Items);
                int deckCount = items.Count(value => value.Kind == "deck");
                EnsureRequestBound(budget.RequestCount + (deckCount * 2) + 2);
                foreach (ArchidektFolderMoveItem item in items)
                {
                    if (item.Kind == "folder")
                    {
                        ValidateFolderMoveItem(before, item, destinationFolderId);
                        continue;
                    }

                    RemoteDeckSnapshot deck = await transport.GetDeckAsync(
                        item.Id,
                        requireAuthentication: true,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                    RequireMoveParent(item.ExpectedParentFolderId, deck.ParentFolderId);
                }

                object payload = new
                {
                    items = items.Select(value => new
                    {
                        type = value.Kind,
                        id = ArchidektTransport.ParseProviderId(value.Id),
                        patch = new
                        {
                            parentFolder = ArchidektTransport.ParseProviderId(destinationFolderId),
                        },
                    }),
                };
                await transport.SendFolderMoveAsync(payload, budget, cancellationToken)
                    .ConfigureAwait(false);
                RemoteFolderTree after = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                List<ArchidektFolderMoveStatus> statuses = [];
                foreach (ArchidektFolderMoveItem item in items)
                {
                    string? finalParent = item.Kind == "folder"
                        ? FindFolder(after, item.Id).ParentFolderId
                        : (await transport.GetDeckAsync(
                            item.Id,
                            requireAuthentication: true,
                            budget,
                            cancellationToken).ConfigureAwait(false)).ParentFolderId;
                    string status = string.Equals(
                        finalParent,
                        destinationFolderId,
                        StringComparison.Ordinal)
                        ? "applied"
                        : "unknown";
                    statuses.Add(new ArchidektFolderMoveStatus(
                        item.Kind,
                        item.Id,
                        item.ExpectedParentFolderId,
                        finalParent,
                        status));
                }

                return new ArchidektFolderMoveResult(statuses, after.TreeFingerprint);
            });
    }

    /// <summary>
    /// Deletes one confirmed empty folder and verifies its absence from a fresh tree.
    /// </summary>
    public Task<OperationResult<ArchidektApplyResult>> DeleteFolderAsync(
        ArchidektFolderDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RequireConfirmation(request.Confirmation, $"delete folder {request.FolderId}");
                RemoteFolderTree before = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                before = await EnrichFolderDecksAsync(before, budget, cancellationToken)
                    .ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedTreeFingerprint,
                    before.TreeFingerprint,
                    "folder-tree-changed");
                RemoteFolderRecord folder = FindFolder(before, request.FolderId);
                if (!string.Equals(folder.Name, request.ExpectedName, StringComparison.Ordinal))
                {
                    throw Conflict("folder-name-changed", "Archidekt folder name changed before deletion.");
                }

                if (folder.ChildFolderIds.Count > 0 || folder.Decks.Count > 0)
                {
                    throw Conflict("folder-not-empty", "Only an empty Archidekt folder can be deleted.");
                }

                ArchidektRemoteOperation operation = new(
                    1,
                    "folder-delete",
                    request.FolderId,
                    "Delete one verified empty folder.");
                try
                {
                    await transport.SendFolderDeleteAsync(
                        new
                        {
                            items = new[]
                            {
                                new
                                {
                                    type = "folder",
                                    id = ArchidektTransport.ParseProviderId(request.FolderId),
                                },
                            },
                        },
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArchidektProviderException exception)
                    when (exception.Kind == ArchidektFailureKind.Unavailable)
                {
                    return PartialResult(request.FolderId, operation, exception.Message);
                }

                RemoteFolderTree after = await transport.ListFoldersAsync(
                    budget,
                    cancellationToken).ConfigureAwait(false);
                if (after.Items.Any(value =>
                    string.Equals(value.FolderId, request.FolderId, StringComparison.Ordinal)))
                {
                    throw Conflict(
                        "folder-delete-unverified",
                        "Archidekt still lists the folder after deletion.");
                }

                return AppliedResult(request.FolderId, operation, finalFingerprint: after.TreeFingerprint);
            });
    }

    /// <summary>
    /// Lists exact named snapshot metadata for one deck.
    /// </summary>
    public Task<OperationResult<RemoteNamedSnapshotPage>> ListSnapshotsAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            budget => transport.ListSnapshotsAsync(deckId, budget, cancellationToken));
    }

    /// <summary>
    /// Gets one complete saved snapshot and cross-checks its owning deck.
    /// </summary>
    public Task<OperationResult<RemoteNamedSnapshot>> GetSnapshotAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            budget => transport.GetSnapshotAsync(deckId, snapshotId, budget, cancellationToken));
    }

    /// <summary>
    /// Creates a named snapshot only when the source deck still matches its caller fingerprint.
    /// </summary>
    public Task<OperationResult<RemoteNamedSnapshotSummary>> CreateSnapshotAsync(
        ArchidektSnapshotCreateRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteDeckSnapshot deck = await transport.GetDeckAsync(
                    request.DeckId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedRemoteFingerprint,
                    deck.RemoteFingerprint,
                    "remote-deck-changed");
                string name = ArchidektContract.Required(request.Name, nameof(request.Name));
                await transport.SendSnapshotCreateAsync(
                    request.DeckId,
                    new { name, description = ArchidektContract.Optional(request.Description) },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteNamedSnapshotPage after = await transport.ListSnapshotsAsync(
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
    public Task<OperationResult<RemoteNamedSnapshotSummary>> UpdateSnapshotAsync(
        ArchidektSnapshotUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RemoteNamedSnapshot current = await transport.GetSnapshotAsync(
                    request.DeckId,
                    request.SnapshotId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    request.ExpectedChecksum,
                    current.Summary.Checksum,
                    "snapshot-changed");
                string name = ArchidektContract.Required(request.Name, nameof(request.Name));
                await transport.SendSnapshotUpdateAsync(
                    request.SnapshotId,
                    new { name },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteNamedSnapshot updated = await transport.GetSnapshotAsync(
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
    public Task<OperationResult<ArchidektApplyResult>> DeleteSnapshotAsync(
        ArchidektSnapshotDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                RequireConfirmation(request.Confirmation, $"delete snapshot {request.SnapshotId}");
                RemoteNamedSnapshot current = await transport.GetSnapshotAsync(
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
                    await transport.SendSnapshotDeleteAsync(
                        request.SnapshotId,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ArchidektProviderException exception)
                    when (exception.Kind == ArchidektFailureKind.Unavailable)
                {
                    return PartialResult(request.DeckId, operation, exception.Message);
                }

                RemoteNamedSnapshotPage after = await transport.ListSnapshotsAsync(
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
    public Task<OperationResult<ArchidektSnapshotRestorePreview>> PreviewSnapshotRestoreAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            async budget =>
            {
                (RemoteDeckSnapshot current, RemoteNamedSnapshot snapshot) = await GetRestoreSourcesAsync(
                    deckId,
                    snapshotId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RemoteDeckSnapshot target = CreateRestoreTarget(current, snapshot.Deck);
                ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
                EnsureRequestBound(plan.PredictedProviderRequests);
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
    public Task<OperationResult<ArchidektApplyResult>> ApplySnapshotRestoreAsync(
        ArchidektSnapshotRestoreApplyRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
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
                return await ApplyPlanAsync(
                    current,
                    target,
                    plan,
                    budget,
                    cancellationToken).ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Applies one caller-previewed remote target after refetching and replaying all provider guards.
    /// </summary>
    public Task<OperationResult<ArchidektApplyResult>> ApplyRemoteTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        CancellationToken cancellationToken)
    {
        return ApplyRemoteTargetAsync(
            target,
            expectedRemoteFingerprint,
            expectedPlanFingerprint,
            BeginOperation(),
            cancellationToken);
    }

    /// <summary>
    /// Applies one remote target while charging a caller-owned composed-operation budget.
    /// </summary>
    public Task<OperationResult<ArchidektApplyResult>> ApplyRemoteTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operationScope);
        return ExecuteAsync(
            operationScope.Budget,
            async budget =>
            {
                RemoteDeckSnapshot current = await transport.GetDeckAsync(
                    target.RemoteId,
                    requireAuthentication: true,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                RequireFingerprint(
                    expectedRemoteFingerprint,
                    current.RemoteFingerprint,
                    "remote-deck-changed");
                ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
                RequireFingerprint(
                    expectedPlanFingerprint,
                    plan.PlanFingerprint,
                    "push-preview-changed");
                return await ApplyPlanAsync(
                    current,
                    target,
                    plan,
                    budget,
                    cancellationToken).ConfigureAwait(false);
            });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        transport.Dispose();
    }

    /// <summary>
    /// Executes one primitive plan in stable order and reports partial/unknown state without retries.
    /// </summary>
    private async Task<ArchidektApplyResult> ApplyPlanAsync(
        RemoteDeckSnapshot current,
        RemoteDeckSnapshot target,
        ArchidektRemotePlan plan,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        EnsureRequestBound(budget.RequestCount + plan.PredictedProviderRequests);
        List<ArchidektOperationStatus> statuses = [];
        Dictionary<string, string> resolvedCards = new(StringComparer.Ordinal);
        for (int index = 0; index < plan.PlannedOperations.Count; index++)
        {
            ArchidektPlannedOperation operation = plan.PlannedOperations[index];
            try
            {
                await ExecutePlannedOperationAsync(
                    current.RemoteId,
                    target,
                    operation,
                    resolvedCards,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                statuses.Add(Status(operation.Public, "applied", "Archidekt accepted the operation."));
            }
            catch (ArchidektProviderException exception)
                when (exception.Kind is ArchidektFailureKind.Unavailable or ArchidektFailureKind.Unsupported)
            {
                statuses.Add(Status(operation.Public, "unknown", exception.Message));
                for (int remaining = index + 1; remaining < plan.PlannedOperations.Count; remaining++)
                {
                    statuses.Add(Status(
                        plan.PlannedOperations[remaining].Public,
                        "not-attempted",
                        "A prior provider operation did not complete safely."));
                }

                return new ArchidektApplyResult(
                    "partial",
                    LocalDeckId: null,
                    LocalRevision: null,
                    current.RemoteId,
                    FinalRemoteFingerprint: null,
                    statuses);
            }
        }

        RemoteDeckSnapshot verified = await transport.GetDeckAsync(
            current.RemoteId,
            requireAuthentication: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        ArchidektRemotePlan residual = ArchidektSyncPlanner.PlanRemoteApply(verified, target);
        if (residual.PlannedOperations.Count > 0)
        {
            return new ArchidektApplyResult(
                "verification-mismatch",
                LocalDeckId: null,
                LocalRevision: null,
                current.RemoteId,
                verified.RemoteFingerprint,
                statuses);
        }

        return new ArchidektApplyResult(
            "applied",
            LocalDeckId: null,
            LocalRevision: null,
            current.RemoteId,
            verified.RemoteFingerprint,
            statuses);
    }

    /// <summary>
    /// Translates one planned primitive into its exact observed provider request.
    /// </summary>
    private async Task ExecutePlannedOperationAsync(
        string deckId,
        RemoteDeckSnapshot target,
        ArchidektPlannedOperation operation,
        IDictionary<string, string> resolvedCards,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        switch (operation.Public.Kind)
        {
            case "metadata-update":
                await transport.SendDeckMetadataAsync(
                    deckId,
                    new
                    {
                        name = target.Name,
                        description = target.Description,
                        deckFormat = ArchidektTransport.MapFormatId(target.Format),
                        @private = target.Visibility == "private",
                        unlisted = target.Visibility == "unlisted",
                        parentFolder = ArchidektTransport.ParseProviderId(target.ParentFolderId),
                    },
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "category-create":
                await transport.SendCategoryCreateAsync(
                    deckId,
                    CategoryPayload(deckId, operation.TargetCategory!),
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "category-update":
                await transport.SendCategoryUpdateAsync(
                    operation.CurrentCategory!.ProviderCategoryId,
                    CategoryPayload(deckId, operation.TargetCategory!),
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "category-delete":
                await transport.SendCategoryDeleteAsync(
                    operation.CurrentCategory!.ProviderCategoryId,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "entry-add":
            case "entry-update":
            case "entry-remove":
                await ExecuteCardOperationAsync(
                    deckId,
                    operation,
                    resolvedCards,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArchidektProviderException(
                    ArchidektFailureKind.Unsupported,
                    "provider-contract-unsupported",
                    "The remote operation kind is not supported.");
        }
    }

    /// <summary>
    /// Resolves and sends one exact single-card add, modify, or remove operation.
    /// </summary>
    private async Task ExecuteCardOperationAsync(
        string deckId,
        ArchidektPlannedOperation operation,
        IDictionary<string, string> resolvedCards,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        RemoteDeckEntry entry = operation.TargetEntry ?? operation.CurrentEntry!;
        string providerCardId = entry.ProviderCardId;
        if (operation.Public.Kind == "entry-add" && string.IsNullOrWhiteSpace(providerCardId))
        {
            string key = $"{entry.PrintingId}:{entry.SetCode}:{entry.CollectorNumber}:{entry.CardName}";
            if (!resolvedCards.TryGetValue(key, out providerCardId!))
            {
                providerCardId = await transport.ResolveCardIdAsync(
                    entry,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                resolvedCards[key] = providerCardId;
            }
        }

        string action = operation.Public.Kind switch
        {
            "entry-add" => "add",
            "entry-update" => "modify",
            "entry-remove" => "remove",
            _ => throw new InvalidOperationException("Unsupported card operation kind."),
        };
        Dictionary<string, object?> payload = new(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["cardid"] = ArchidektTransport.ParseProviderId(providerCardId),
            ["patchId"] = ArchidektContract.StableGuid(
                "patch",
                $"{deckId}:{operation.Public.Sequence}:{operation.Public.Kind}:{operation.Public.Subject}").ToString("N"),
            ["categories"] = entry.CategoryNames,
            ["modifications"] = new
            {
                quantity = action == "remove" ? 0 : entry.Quantity,
                companion = false,
                flippedDefault = false,
                modifier = ProviderModifier(entry.Finish),
            },
        };
        string? relationId = operation.CurrentEntry?.ProviderRelationId;
        if (!string.IsNullOrWhiteSpace(relationId))
        {
            payload["deckRelationId"] = ArchidektTransport.ParseProviderId(relationId);
        }

        await transport.SendCardMutationAsync(deckId, payload, budget, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates one exact category mutation payload.
    /// </summary>
    private static object CategoryPayload(string deckId, RemoteDeckCategory category)
    {
        return new
        {
            deck = ArchidektTransport.ParseProviderId(deckId),
            name = category.Name,
            includedInDeck = category.IncludedInDeck ?? true,
            includedInPrice = category.IncludedInPrice ?? true,
            isPremier = category.IsPremier,
            sortOrder = category.SortOrder,
        };
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
        RemoteDeckSnapshot current = await transport.GetDeckAsync(
            deckId,
            requireAuthentication: true,
            budget,
            cancellationToken).ConfigureAwait(false);
        RemoteNamedSnapshot snapshot = await transport.GetSnapshotAsync(
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
    /// Checks every authenticated deck-list page until the exact ID is found or the list ends.
    /// </summary>
    private async Task<bool> DeckAppearsInAuthenticatedListingAsync(
        string deckId,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        do
        {
            RemoteDeckPage page = await transport.ListDecksAsync(
                cursor,
                100,
                budget,
                cancellationToken).ConfigureAwait(false);
            if (page.Items.Any(value => string.Equals(value.RemoteId, deckId, StringComparison.Ordinal)))
            {
                return true;
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return false;
    }

    /// <summary>
    /// Joins owned-deck rows into a folder response because the observed tree omits deck children.
    /// </summary>
    private async Task<RemoteFolderTree> EnrichFolderDecksAsync(
        RemoteFolderTree tree,
        ArchidektOperationBudget budget,
        CancellationToken cancellationToken)
    {
        List<RemoteDeckSummary> decks = [];
        string? cursor = null;
        do
        {
            RemoteDeckPage page = await transport.ListDecksAsync(
                cursor,
                100,
                budget,
                cancellationToken).ConfigureAwait(false);
            decks.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        string? rootFolderId = tree.Items.Count(value => value.ParentFolderId is null) == 1
            ? tree.Items.Single(value => value.ParentFolderId is null).FolderId
            : null;
        RemoteFolderRecord[] items = tree.Items.Select(folder => folder with
        {
            Decks = Array.AsReadOnly(decks
                .Where(deck => string.Equals(
                    deck.ParentFolderId ?? rootFolderId,
                    folder.FolderId,
                    StringComparison.Ordinal))
                .OrderBy(deck => deck.RemoteId, StringComparer.Ordinal)
                .ToArray()),
        }).ToArray();
        string fingerprint = ArchidektContract.Fingerprint(items.Select(value => new
        {
            value.FolderId,
            value.Name,
            value.Visibility,
            value.ParentFolderId,
            value.Path,
            value.ChildFolderIds,
            decks = value.Decks.Select(deck => deck.RemoteId),
        }));
        return new RemoteFolderTree(items, tree.Evidence, fingerprint);
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
    /// Enforces the conservative provider request upper bound before a remote apply begins.
    /// </summary>
    private void EnsureRequestBound(int predictedRequests)
    {
        if (predictedRequests > maximumRequestsPerOperation)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "request-limit-exceeded",
                "The preview exceeds the provider request limit; no remote writes were attempted.");
        }
    }

    /// <summary>
    /// Requires an exact destination folder when one was supplied.
    /// </summary>
    private static void RequireFolderParent(RemoteFolderTree tree, string? parentFolderId)
    {
        if (parentFolderId is not null && !tree.Items.Any(value =>
            string.Equals(value.FolderId, parentFolderId, StringComparison.Ordinal)))
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "folder-not-found",
                "The requested Archidekt folder was not found.");
        }
    }

    /// <summary>
    /// Resolves an omitted logical parent to the account's one explicit provider root folder.
    /// </summary>
    private static string ResolveProviderParent(RemoteFolderTree tree, string? parentFolderId)
    {
        if (parentFolderId is not null)
        {
            RequireFolderParent(tree, parentFolderId);
            return parentFolderId;
        }

        RemoteFolderRecord[] roots = tree.Items
            .Where(value => value.ParentFolderId is null)
            .ToArray();
        if (roots.Length != 1)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.Unsupported,
                "root-folder-unavailable",
                "Archidekt did not expose one unambiguous root folder.");
        }

        return roots[0].FolderId;
    }

    /// <summary>
    /// Gets one exact folder or returns a structured not-found outcome.
    /// </summary>
    private static RemoteFolderRecord FindFolder(RemoteFolderTree tree, string folderId)
    {
        return tree.Items.FirstOrDefault(value =>
            string.Equals(value.FolderId, folderId, StringComparison.Ordinal))
            ?? throw new ArchidektProviderException(
                ArchidektFailureKind.NotFound,
                "folder-not-found",
                "The requested Archidekt folder was not found.");
    }

    /// <summary>
    /// Rejects a move that would place one folder beneath itself or a descendant.
    /// </summary>
    private static void PreventFolderCycle(
        RemoteFolderTree tree,
        string folderId,
        string? destinationFolderId)
    {
        string? cursor = destinationFolderId;
        while (cursor is not null)
        {
            if (string.Equals(cursor, folderId, StringComparison.Ordinal))
            {
                throw Conflict("folder-cycle", "The requested folder move would create a cycle.");
            }

            cursor = tree.Items.FirstOrDefault(value =>
                string.Equals(value.FolderId, cursor, StringComparison.Ordinal))?.ParentFolderId;
        }
    }

    /// <summary>
    /// Deduplicates typed move items while rejecting incompatible duplicate identities.
    /// </summary>
    private static ArchidektFolderMoveItem[] DeduplicateMoveItems(
        IReadOnlyList<ArchidektFolderMoveItem> items)
    {
        if (items.Count == 0)
        {
            throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "move-items-required",
                "At least one folder move item is required.");
        }

        Dictionary<string, ArchidektFolderMoveItem> unique = new(StringComparer.Ordinal);
        foreach (ArchidektFolderMoveItem item in items)
        {
            string kind = item.Kind.ToLowerInvariant();
            if (kind is not ("deck" or "folder"))
            {
                throw new ArchidektProviderException(
                    ArchidektFailureKind.InvalidInput,
                    "invalid-folder-item-kind",
                    "Folder move item kind must be deck or folder.");
            }

            string id = ArchidektContract.Required(item.Id, nameof(item.Id));
            string key = $"{kind}:{id}";
            ArchidektFolderMoveItem normalized = item with { Kind = kind, Id = id };
            if (unique.TryGetValue(key, out ArchidektFolderMoveItem? existing) &&
                !Equals(existing, normalized))
            {
                throw new ArchidektProviderException(
                    ArchidektFailureKind.InvalidInput,
                    "conflicting-folder-items",
                    "Duplicate folder move items disagree about their current parent.");
            }

            unique[key] = normalized;
        }

        return unique.Values.OrderBy(value => value.Kind).ThenBy(value => value.Id).ToArray();
    }

    /// <summary>
    /// Validates one exact current parent and cycle boundary before a move request is sent.
    /// </summary>
    private static void ValidateFolderMoveItem(
        RemoteFolderTree tree,
        ArchidektFolderMoveItem item,
        string? destinationFolderId)
    {
        RemoteFolderRecord folder = FindFolder(tree, item.Id);
        RequireMoveParent(item.ExpectedParentFolderId, folder.ParentFolderId);
        PreventFolderCycle(tree, item.Id, destinationFolderId);
    }

    /// <summary>
    /// Requires one typed item to retain its exact caller-observed parent before mutation.
    /// </summary>
    private static void RequireMoveParent(string? expectedParent, string? actualParent)
    {
        if (!string.Equals(expectedParent, actualParent, StringComparison.Ordinal))
        {
            throw Conflict("folder-assignment-changed", "A folder item changed parents before apply.");
        }
    }

    /// <summary>
    /// Maps the explicit provider visibility vocabulary.
    /// </summary>
    private static string NormalizeVisibility(string value)
    {
        return ArchidektContract.Required(value, nameof(value)).ToLowerInvariant() switch
        {
            "private" => "private",
            "public" => "public",
            _ => throw new ArchidektProviderException(
                ArchidektFailureKind.InvalidInput,
                "invalid-folder-visibility",
                "Folder visibility must be private or public."),
        };
    }

    /// <summary>
    /// Maps local finish vocabulary to Archidekt's observed modifier names.
    /// </summary>
    private static string ProviderModifier(string finish)
    {
        return finish switch
        {
            "foil" => "Foil",
            "etched" => "Etched",
            _ => "Normal",
        };
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

    /// <summary>
    /// Executes one bounded adapter operation and maps every known failure into the shared result union.
    /// </summary>
    private async Task<OperationResult<T>> ExecuteAsync<T>(
        Func<ArchidektOperationBudget, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArchidektOperationBudget budget = new(maximumRequestsPerOperation);
        return await ExecuteAsync(budget, operation).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes one adapter operation against an existing composed-operation budget.
    /// </summary>
    private static async Task<OperationResult<T>> ExecuteAsync<T>(
        ArchidektOperationBudget budget,
        Func<ArchidektOperationBudget, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return new OperationSuccess<T>(await operation(budget).ConfigureAwait(false));
        }
        catch (ArchidektProviderException exception)
        {
            return exception.Kind switch
            {
                ArchidektFailureKind.InvalidInput => new OperationInvalidInput(
                    exception.ReasonCode,
                    exception.Message),
                ArchidektFailureKind.NotFound => new OperationNotFound(
                    exception.ReasonCode,
                    exception.Message),
                ArchidektFailureKind.Conflict => new OperationConflict(
                    exception.ReasonCode,
                    exception.Message),
                ArchidektFailureKind.Unsupported => new OperationUnsupported(
                    exception.ReasonCode,
                    exception.Message),
                ArchidektFailureKind.Unavailable => new OperationUnavailable(
                    exception.ReasonCode,
                    exception.Message),
                _ => new OperationUnavailable(
                    "provider-unavailable",
                    "Archidekt could not complete the operation."),
            };
        }
        catch (ArgumentException)
        {
            return new OperationInvalidInput(
                "invalid-archidekt-input",
                "The Archidekt operation input is invalid.");
        }
        catch (InvalidDataException)
        {
            return new OperationUnavailable(
                "baseline-unavailable",
                "The stored Archidekt synchronization baseline is unavailable.");
        }
    }
}
