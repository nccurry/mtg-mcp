using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Provides the stable public Archidekt API through deck, folder, and snapshot operation owners.
/// </summary>
public sealed class ArchidektService : IDisposable
{
    /// <summary>
    /// Owns shared HTTP, authentication, pacing, retry, and client-disposal state.
    /// </summary>
    private readonly ArchidektSession session;

    /// <summary>
    /// Stores the hard provider request ceiling for each composed operation scope.
    /// </summary>
    private readonly int maximumRequestsPerOperation;

    /// <summary>
    /// Owns remote deck reads, writes, verification, and exact update plans.
    /// </summary>
    private readonly ArchidektDeckOperations decks;

    /// <summary>
    /// Owns folder tree, metadata, move, and deletion operations.
    /// </summary>
    private readonly ArchidektFolderOperations folders;

    /// <summary>
    /// Owns named snapshot reads, writes, preview, and restore operations.
    /// </summary>
    private readonly ArchidektSnapshotOperations snapshots;

    /// <summary>
    /// Creates a production service over the configured Archidekt account.
    /// </summary>
    public ArchidektService(ArchidektOptions options, string packageVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        session = new ArchidektSession(options, packageVersion);
        maximumRequestsPerOperation = options.MaximumRequestsPerOperation;
        ArchidektDeckTransport deckTransport = new(session);
        ArchidektFolderTransport folderTransport = new(session);
        ArchidektSnapshotTransport snapshotTransport = new(session);
        decks = new ArchidektDeckOperations(deckTransport, maximumRequestsPerOperation);
        folders = new ArchidektFolderOperations(folderTransport, deckTransport, maximumRequestsPerOperation);
        snapshots = new ArchidektSnapshotOperations(
            snapshotTransport,
            deckTransport,
            decks,
            maximumRequestsPerOperation);
    }

    /// <summary>
    /// Creates a deterministic service over an injected provider session.
    /// </summary>
    internal ArchidektService(ArchidektSession session, int maximumRequestsPerOperation)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRequestsPerOperation);
        this.maximumRequestsPerOperation = maximumRequestsPerOperation;
        ArchidektDeckTransport deckTransport = new(session);
        ArchidektFolderTransport folderTransport = new(session);
        ArchidektSnapshotTransport snapshotTransport = new(session);
        decks = new ArchidektDeckOperations(deckTransport, maximumRequestsPerOperation);
        folders = new ArchidektFolderOperations(folderTransport, deckTransport, maximumRequestsPerOperation);
        snapshots = new ArchidektSnapshotOperations(
            snapshotTransport,
            deckTransport,
            decks,
            maximumRequestsPerOperation);
    }

    /// <summary>
    /// Reports redacted local credential readiness without provider I/O.
    /// </summary>
    public OperationResult<ArchidektAuthStatus> GetAuthStatus()
    {
        return new OperationSuccess<ArchidektAuthStatus>(session.GetAuthStatus());
    }

    /// <summary>
    /// Begins one hard-bounded provider-request scope for a composed invocation.
    /// </summary>
    public ArchidektOperationScope BeginOperation()
    {
        return new ArchidektOperationScope(maximumRequestsPerOperation);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.ListAsync(string?, int, CancellationToken)"/>
    public Task<OperationResult<RemoteDeckPage>> ListDecksAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return decks.ListAsync(cursor, pageSize, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.ListAsync(string?, int, ArchidektOperationScope, CancellationToken)"/>
    public Task<OperationResult<RemoteDeckPage>> ListDecksAsync(
        string? cursor,
        int pageSize,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        return decks.ListAsync(cursor, pageSize, operationScope, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.GetAsync(string, CancellationToken)"/>
    public Task<OperationResult<RemoteDeckSnapshot>> GetDeckAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return decks.GetAsync(deckId, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.GetAsync(string, ArchidektOperationScope, CancellationToken)"/>
    public Task<OperationResult<RemoteDeckSnapshot>> GetDeckAsync(
        string deckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        return decks.GetAsync(deckId, operationScope, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.CreateAsync"/>
    public Task<OperationResult<RemoteDeckSnapshot>> CreateDeckAsync(
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        return decks.CreateAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.DeleteAsync"/>
    public Task<OperationResult<ArchidektApplyResult>> DeleteDeckAsync(
        ArchidektDeckDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return decks.DeleteAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.ApplyTargetAsync(RemoteDeckSnapshot, string, string, CancellationToken)"/>
    public Task<OperationResult<ArchidektApplyResult>> ApplyRemoteTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        CancellationToken cancellationToken)
    {
        return decks.ApplyTargetAsync(
            target,
            expectedRemoteFingerprint,
            expectedPlanFingerprint,
            cancellationToken);
    }

    /// <inheritdoc cref="ArchidektDeckOperations.ApplyTargetAsync(RemoteDeckSnapshot, string, string, ArchidektOperationScope, CancellationToken)"/>
    public Task<OperationResult<ArchidektApplyResult>> ApplyRemoteTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        return decks.ApplyTargetAsync(
            target,
            expectedRemoteFingerprint,
            expectedPlanFingerprint,
            operationScope,
            cancellationToken);
    }

    /// <inheritdoc cref="ArchidektFolderOperations.ListAsync"/>
    public Task<OperationResult<RemoteFolderTree>> ListFoldersAsync(CancellationToken cancellationToken)
    {
        return folders.ListAsync(cancellationToken);
    }

    /// <inheritdoc cref="ArchidektFolderOperations.GetAsync"/>
    public Task<OperationResult<RemoteFolderTree>> GetFolderAsync(
        string folderId,
        CancellationToken cancellationToken)
    {
        return folders.GetAsync(folderId, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektFolderOperations.CreateAsync"/>
    public Task<OperationResult<RemoteFolderRecord>> CreateFolderAsync(
        ArchidektFolderCreateRequest request,
        CancellationToken cancellationToken)
    {
        return folders.CreateAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektFolderOperations.UpdateAsync"/>
    public Task<OperationResult<RemoteFolderRecord>> UpdateFolderAsync(
        ArchidektFolderUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return folders.UpdateAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektFolderOperations.MoveItemsAsync"/>
    public Task<OperationResult<ArchidektFolderMoveResult>> MoveFolderItemsAsync(
        ArchidektFolderMoveRequest request,
        CancellationToken cancellationToken)
    {
        return folders.MoveItemsAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektFolderOperations.DeleteAsync"/>
    public Task<OperationResult<ArchidektApplyResult>> DeleteFolderAsync(
        ArchidektFolderDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return folders.DeleteAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektSnapshotOperations.ListAsync"/>
    public Task<OperationResult<RemoteNamedSnapshotPage>> ListSnapshotsAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return snapshots.ListAsync(deckId, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektSnapshotOperations.GetAsync"/>
    public Task<OperationResult<RemoteNamedSnapshot>> GetSnapshotAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return snapshots.GetAsync(deckId, snapshotId, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektSnapshotOperations.CreateAsync"/>
    public Task<OperationResult<RemoteNamedSnapshotSummary>> CreateSnapshotAsync(
        ArchidektSnapshotCreateRequest request,
        CancellationToken cancellationToken)
    {
        return snapshots.CreateAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektSnapshotOperations.UpdateAsync"/>
    public Task<OperationResult<RemoteNamedSnapshotSummary>> UpdateSnapshotAsync(
        ArchidektSnapshotUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return snapshots.UpdateAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektSnapshotOperations.DeleteAsync"/>
    public Task<OperationResult<ArchidektApplyResult>> DeleteSnapshotAsync(
        ArchidektSnapshotDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return snapshots.DeleteAsync(request, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektSnapshotOperations.PreviewRestoreAsync"/>
    public Task<OperationResult<ArchidektSnapshotRestorePreview>> PreviewSnapshotRestoreAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return snapshots.PreviewRestoreAsync(deckId, snapshotId, cancellationToken);
    }

    /// <inheritdoc cref="ArchidektSnapshotOperations.ApplyRestoreAsync"/>
    public Task<OperationResult<ArchidektApplyResult>> ApplySnapshotRestoreAsync(
        ArchidektSnapshotRestoreApplyRequest request,
        CancellationToken cancellationToken)
    {
        return snapshots.ApplyRestoreAsync(request, cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        session.Dispose();
    }
}
