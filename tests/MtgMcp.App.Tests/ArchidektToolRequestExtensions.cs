using MtgMcp.App.Archidekt;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Lets workflow tests describe the arguments of an Archidekt write tool as one guard record, and calls the flat tool
/// method with the same values.
/// </summary>
internal static class ArchidektToolRequestExtensions
{
    /// <summary>
    /// Calls the remote deck creation tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<RemoteDeckSnapshot>> CreateDeckAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektDeckCreateRequest request,
        Guid? localDeckId = null,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        return tools.CreateDeckAsync(
            request.Name,
            request.Format,
            request.Description,
            request.Visibility,
            request.ParentFolderId,
            localDeckId,
            expectedRevision,
            cancellationToken);
    }

    /// <summary>
    /// Calls the remote deck deletion tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> DeleteDeckAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektDeckDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return tools.DeleteDeckAsync(
            request.RemoteDeckId,
            request.ExpectedRemoteFingerprint,
            request.Confirmation,
            cancellationToken);
    }

    /// <summary>
    /// Calls the push apply tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> ApplyPushAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektPushApplyRequest request,
        CancellationToken cancellationToken)
    {
        return tools.ApplyPushAsync(
            request.LocalDeckId,
            request.ExpectedRevision,
            request.ExpectedRemoteFingerprint,
            request.PreviewFingerprint,
            request.ConfirmRemovals,
            cancellationToken);
    }

    /// <summary>
    /// Calls the folder creation tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<RemoteFolderRecord>> CreateFolderAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektFolderCreateRequest request,
        CancellationToken cancellationToken)
    {
        return tools.CreateFolderAsync(request.Name, request.Visibility, request.ParentFolderId, cancellationToken);
    }

    /// <summary>
    /// Calls the folder update tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<RemoteFolderRecord>> UpdateFolderAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektFolderUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return tools.UpdateFolderAsync(
            request.FolderId,
            request.ExpectedTreeFingerprint,
            request.Name,
            request.Visibility,
            request.ParentFolderId,
            request.UpdateParent,
            cancellationToken);
    }

    /// <summary>
    /// Calls the folder item move tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<ArchidektFolderMoveResult>> MoveFolderItemsAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektFolderMoveRequest request,
        CancellationToken cancellationToken)
    {
        return tools.MoveFolderItemsAsync(
            request.ExpectedTreeFingerprint,
            request.Items,
            request.DestinationFolderId,
            cancellationToken);
    }

    /// <summary>
    /// Calls the folder deletion tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> DeleteFolderAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektFolderDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return tools.DeleteFolderAsync(
            request.FolderId,
            request.ExpectedName,
            request.ExpectedTreeFingerprint,
            request.Confirmation,
            cancellationToken);
    }

    /// <summary>
    /// Calls the snapshot creation tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<RemoteNamedSnapshotSummary>> CreateSnapshotAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektSnapshotCreateRequest request,
        CancellationToken cancellationToken)
    {
        return tools.CreateSnapshotAsync(
            request.RemoteDeckId,
            request.ExpectedRemoteFingerprint,
            request.Name,
            request.Description,
            cancellationToken);
    }

    /// <summary>
    /// Calls the snapshot update tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<RemoteNamedSnapshotSummary>> UpdateSnapshotAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektSnapshotUpdateRequest request,
        CancellationToken cancellationToken)
    {
        return tools.UpdateSnapshotAsync(
            request.RemoteDeckId,
            request.SnapshotId,
            request.ExpectedChecksum,
            request.Name,
            cancellationToken);
    }

    /// <summary>
    /// Calls the snapshot deletion tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> DeleteSnapshotAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektSnapshotDeleteRequest request,
        CancellationToken cancellationToken)
    {
        return tools.DeleteSnapshotAsync(
            request.RemoteDeckId,
            request.SnapshotId,
            request.ExpectedChecksum,
            request.Confirmation,
            cancellationToken);
    }

    /// <summary>
    /// Calls the snapshot restore apply tool with the fields of a request.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> ApplySnapshotRestoreAsync(
        this ArchidektRemoteWriteTools tools,
        ArchidektSnapshotRestoreApplyRequest request,
        CancellationToken cancellationToken)
    {
        return tools.ApplySnapshotRestoreAsync(
            request.RemoteDeckId,
            request.SnapshotId,
            request.ExpectedSnapshotChecksum,
            request.ExpectedSnapshotContentFingerprint,
            request.ExpectedRemoteFingerprint,
            request.PreviewFingerprint,
            request.Confirmation,
            cancellationToken);
    }

    /// <summary>
    /// Calls the pull apply tool with the fields of a request, adding the confirmation a discarding pull needs.
    /// </summary>
    internal static Task<OperationResult<ArchidektApplyResult>> ApplyPullAsync(
        this ArchidektLocalWriteTools tools,
        ArchidektPullApplyRequest request,
        CancellationToken cancellationToken)
    {
        return tools.ApplyPullAsync(
            request.RemoteDeckId,
            request.ExpectedRemoteFingerprint,
            request.PreviewFingerprint,
            request.LocalDeckId,
            request.ExpectedRevision,
            request.DiscardLocalChanges,
            request.DiscardLocalChanges && request.LocalDeckId is { } local ? $"discard local changes {local:D}" : null,
            cancellationToken);
    }
}
