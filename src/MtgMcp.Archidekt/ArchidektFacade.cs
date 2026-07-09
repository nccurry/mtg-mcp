using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt;

/// <summary>
/// Provides the stable public Archidekt API through deck, folder, and snapshot operation owners.
/// </summary>
public sealed class ArchidektService : IDisposable
{
    /// <summary>Owns shared authentication, pacing, retry, request-budget, and transport state.</summary>
    private readonly ArchidektOperationContext context;

    /// <summary>Owns remote deck reads, writes, verification, and primitive apply operations.</summary>
    private readonly ArchidektDeckOperations decks;

    /// <summary>Owns folder tree, metadata, move, and deletion operations.</summary>
    private readonly ArchidektFolderOperations folders;

    /// <summary>Owns named snapshot reads, writes, preview, and restore operations.</summary>
    private readonly ArchidektSnapshotOperations snapshots;

    /// <summary>Creates a production service over the configured Archidekt account.</summary>
    public ArchidektService(ArchidektOptions options, string packageVersion)
        : this(new ArchidektOperationContext(options, packageVersion))
    {
    }

    /// <summary>Creates a deterministic service over an injected transport.</summary>
    internal ArchidektService(ArchidektTransport transport, int maximumRequestsPerOperation)
        : this(new ArchidektOperationContext(transport, maximumRequestsPerOperation))
    {
    }

    /// <summary>Creates the public facade around one shared operation context.</summary>
    private ArchidektService(ArchidektOperationContext context)
    {
        this.context = context;
        decks = new ArchidektDeckOperations(context);
        folders = new ArchidektFolderOperations(context);
        snapshots = new ArchidektSnapshotOperations(context);
    }

    /// <summary>Reports redacted local credential readiness without provider I/O.</summary>
    public OperationResult<ArchidektAuthStatus> GetAuthStatus()
    {
        return context.GetAuthStatus();
    }

    /// <summary>Begins one hard-bounded provider-request scope for a composed invocation.</summary>
    public ArchidektOperationScope BeginOperation()
    {
        return context.BeginOperation();
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
        context.Dispose();
    }
}

/// <summary>
/// Owns Archidekt deck reads, guarded writes, and primitive remote apply workflows.
/// </summary>
internal sealed class ArchidektDeckOperations
{
    /// <summary>Stores the shared transport and request-budget context.</summary>
    private readonly ArchidektOperationContext context;

    /// <summary>Creates deck operations around one shared context.</summary>
    internal ArchidektDeckOperations(ArchidektOperationContext context)
    {
        this.context = context;
    }

    /// <summary>Lists one bounded authenticated deck page.</summary>
    internal Task<OperationResult<RemoteDeckPage>> ListAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return context.ListDecksAsync(cursor, pageSize, cancellationToken);
    }

    /// <summary>Lists one deck page under a caller-owned composed-operation budget.</summary>
    internal Task<OperationResult<RemoteDeckPage>> ListAsync(
        string? cursor,
        int pageSize,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        return context.ListDecksAsync(cursor, pageSize, operationScope, cancellationToken);
    }

    /// <summary>Gets one fresh public or authenticated deck observation.</summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> GetAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return context.GetDeckAsync(deckId, cancellationToken);
    }

    /// <summary>Gets one remote deck under a caller-owned composed-operation budget.</summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> GetAsync(
        string deckId,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        return context.GetDeckAsync(deckId, operationScope, cancellationToken);
    }

    /// <summary>Creates and verifies one private-by-default remote deck.</summary>
    internal Task<OperationResult<RemoteDeckSnapshot>> CreateAsync(
        ArchidektDeckCreateRequest request,
        CancellationToken cancellationToken)
    {
        return context.CreateDeckAsync(request, cancellationToken);
    }

    /// <summary>Deletes one unchanged exact remote deck and verifies absence.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> DeleteAsync(
        ArchidektDeckDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return context.DeleteDeckAsync(request, cancellationToken);
    }

    /// <summary>Applies one caller-previewed remote target under an operation-local budget.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        CancellationToken cancellationToken)
    {
        return context.ApplyRemoteTargetAsync(
            target,
            expectedRemoteFingerprint,
            expectedPlanFingerprint,
            cancellationToken);
    }

    /// <summary>Applies one remote target under a caller-owned composed-operation budget.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyTargetAsync(
        RemoteDeckSnapshot target,
        string expectedRemoteFingerprint,
        string expectedPlanFingerprint,
        ArchidektOperationScope operationScope,
        CancellationToken cancellationToken)
    {
        return context.ApplyRemoteTargetAsync(
            target,
            expectedRemoteFingerprint,
            expectedPlanFingerprint,
            operationScope,
            cancellationToken);
    }
}

/// <summary>
/// Owns Archidekt folder tree reads, metadata writes, moves, and safe deletion.
/// </summary>
internal sealed class ArchidektFolderOperations
{
    /// <summary>Stores the shared transport and request-budget context.</summary>
    private readonly ArchidektOperationContext context;

    /// <summary>Creates folder operations around one shared context.</summary>
    internal ArchidektFolderOperations(ArchidektOperationContext context)
    {
        this.context = context;
    }

    /// <summary>Lists the complete authenticated folder tree.</summary>
    internal Task<OperationResult<RemoteFolderTree>> ListAsync(CancellationToken cancellationToken)
    {
        return context.ListFoldersAsync(cancellationToken);
    }

    /// <summary>Gets one authenticated folder and its direct contents.</summary>
    internal Task<OperationResult<RemoteFolderTree>> GetAsync(
        string folderId,
        CancellationToken cancellationToken)
    {
        return context.GetFolderAsync(folderId, cancellationToken);
    }

    /// <summary>Creates one folder beneath an exact optional parent.</summary>
    internal Task<OperationResult<RemoteFolderRecord>> CreateAsync(
        ArchidektFolderCreateRequest request,
        CancellationToken cancellationToken)
    {
        return context.CreateFolderAsync(request, cancellationToken);
    }

    /// <summary>Updates allowlisted metadata for one unchanged folder tree.</summary>
    internal Task<OperationResult<RemoteFolderRecord>> UpdateAsync(
        ArchidektFolderUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return context.UpdateFolderAsync(request, cancellationToken);
    }

    /// <summary>Moves exact typed items after stale-parent and cycle preflight.</summary>
    internal Task<OperationResult<ArchidektFolderMoveResult>> MoveItemsAsync(
        ArchidektFolderMoveRequest request,
        CancellationToken cancellationToken)
    {
        return context.MoveFolderItemsAsync(request, cancellationToken);
    }

    /// <summary>Deletes one exactly confirmed empty folder without recursion.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> DeleteAsync(
        ArchidektFolderDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return context.DeleteFolderAsync(request, cancellationToken);
    }
}

/// <summary>
/// Owns Archidekt named snapshot reads, writes, preview, and guarded restore workflows.
/// </summary>
internal sealed class ArchidektSnapshotOperations
{
    /// <summary>Stores the shared transport and request-budget context.</summary>
    private readonly ArchidektOperationContext context;

    /// <summary>Creates snapshot operations around one shared context.</summary>
    internal ArchidektSnapshotOperations(ArchidektOperationContext context)
    {
        this.context = context;
    }

    /// <summary>Lists exact named snapshots for one remote deck.</summary>
    internal Task<OperationResult<RemoteNamedSnapshotPage>> ListAsync(
        string deckId,
        CancellationToken cancellationToken)
    {
        return context.ListSnapshotsAsync(deckId, cancellationToken);
    }

    /// <summary>Gets one complete named snapshot and saved deck state.</summary>
    internal Task<OperationResult<RemoteNamedSnapshot>> GetAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return context.GetSnapshotAsync(deckId, snapshotId, cancellationToken);
    }

    /// <summary>Creates one named snapshot from an unchanged remote deck.</summary>
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> CreateAsync(
        ArchidektSnapshotCreateRequest request,
        CancellationToken cancellationToken)
    {
        return context.CreateSnapshotAsync(request, cancellationToken);
    }

    /// <summary>Updates supported metadata for one unchanged snapshot.</summary>
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> UpdateAsync(
        ArchidektSnapshotUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return context.UpdateSnapshotAsync(request, cancellationToken);
    }

    /// <summary>Deletes one unchanged exactly confirmed named snapshot.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> DeleteAsync(
        ArchidektSnapshotDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return context.DeleteSnapshotAsync(request, cancellationToken);
    }

    /// <summary>Previews restoring one named snapshot onto the current remote deck.</summary>
    internal Task<OperationResult<ArchidektSnapshotRestorePreview>> PreviewRestoreAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return context.PreviewSnapshotRestoreAsync(deckId, snapshotId, cancellationToken);
    }

    /// <summary>Restores one unchanged named snapshot onto one unchanged remote deck.</summary>
    internal Task<OperationResult<ArchidektApplyResult>> ApplyRestoreAsync(
        ArchidektSnapshotRestoreApplyRequest request,
        CancellationToken cancellationToken)
    {
        return context.ApplySnapshotRestoreAsync(request, cancellationToken);
    }
}
