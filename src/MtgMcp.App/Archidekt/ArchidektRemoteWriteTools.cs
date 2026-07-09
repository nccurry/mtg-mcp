using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Exposes only explicit guarded Archidekt mutations in remote operation mode.
/// </summary>
internal sealed class ArchidektRemoteWriteTools
{
    /// <summary>
    /// Provides guarded provider/local workflows.
    /// </summary>
    private readonly ArchidektCoordinator coordinator;

    /// <summary>
    /// Stores effective authority for invocation-time defense in depth.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates the complete remote-write surface.
    /// </summary>
    internal ArchidektRemoteWriteTools(ArchidektCoordinator coordinator, OperationMode mode)
    {
        this.coordinator = coordinator;
        this.mode = mode;
    }

    /// <summary>
    /// Creates one private-by-default empty remote shell and optionally binds an unchanged local deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_deck_create", Title = "Create Archidekt Deck", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Creates and verifies one empty Archidekt deck shell; contents require a separate push preview/apply.")]
    internal Task<OperationResult<RemoteDeckSnapshot>> CreateDeckAsync(
        [Description("Explicit Archidekt deck shell or content request.")] ArchidektDeckCreateRequest request,
        [Description("Optional stable local deck UUID to bind after verified creation.")] Guid? localDeckId = null,
        [Description("Required current local revision when localDeckId is provided.")] long? expectedLocalRevision = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.CreateRemoteDeckAsync(
            localDeckId,
            expectedLocalRevision,
            request,
            cancellationToken));
    }

    /// <summary>
    /// Deletes one unchanged exactly confirmed remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_deck_delete", Title = "Delete Archidekt Deck", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Deletes one exact unchanged Archidekt deck and verifies listing absence; local decks and bindings remain untouched.")]
    internal Task<OperationResult<ArchidektApplyResult>> DeleteDeckAsync(
        [Description("Exact remote fingerprint and acknowledgement required to delete one Archidekt deck.")]
        ArchidektDeckDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.DeleteDeckAsync(request, cancellationToken));
    }

    /// <summary>
    /// Applies one unchanged local-to-remote preview and updates the baseline only after verification.
    /// </summary>
    [McpServerTool(Name = "archidekt_push_apply", Title = "Apply Archidekt Push", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Refetches local and remote state, verifies preview guards, executes stable primitive operations, and verifies final content.")]
    internal Task<OperationResult<ArchidektApplyResult>> ApplyPushAsync(
        [Description("Preview-, revision-, and remote-fingerprint-guarded push application request.")]
        ArchidektPushApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.ApplyPushAsync(request, cancellationToken));
    }

    /// <summary>
    /// Creates one folder under an exact optional parent.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_create", Title = "Create Archidekt Folder", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Creates and verifies one explicitly named Archidekt folder without same-name inference or recursive creation.")]
    internal Task<OperationResult<RemoteFolderRecord>> CreateFolderAsync(
        [Description("Explicit folder name and optional exact parent identifier.")] ArchidektFolderCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.CreateFolderAsync(request, cancellationToken));
    }

    /// <summary>
    /// Updates allowlisted metadata for one unchanged folder tree.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_update", Title = "Update Archidekt Folder", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Updates one exact folder only when its fresh tree fingerprint and optional destination remain valid.")]
    internal Task<OperationResult<RemoteFolderRecord>> UpdateFolderAsync(
        [Description("Exact folder metadata update guarded by its current tree fingerprint.")]
        ArchidektFolderUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.UpdateFolderAsync(request, cancellationToken));
    }

    /// <summary>
    /// Moves exact typed items after stale-parent and cycle preflight.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_move_items", Title = "Move Archidekt Folder Items", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Moves deduplicated exact deck/folder IDs after fresh source-parent and cycle checks, then verifies each assignment.")]
    internal Task<OperationResult<ArchidektFolderMoveResult>> MoveFolderItemsAsync(
        [Description("Exact typed folder/deck moves with source and destination fingerprint guards.")]
        ArchidektFolderMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.MoveFolderItemsAsync(request, cancellationToken));
    }

    /// <summary>
    /// Deletes one exactly confirmed empty folder without recursive or deck deletion.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_delete", Title = "Delete Archidekt Folder", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Deletes one unchanged empty folder only; never submits deck items or recursive deletion.")]
    internal Task<OperationResult<ArchidektApplyResult>> DeleteFolderAsync(
        [Description("Exact empty-folder deletion request with fingerprint and acknowledgement guards.")]
        ArchidektFolderDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.DeleteFolderAsync(request, cancellationToken));
    }

    /// <summary>
    /// Creates one named snapshot from an unchanged remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_create", Title = "Create Archidekt Snapshot", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Creates and verifies one explicitly named snapshot only when the source deck fingerprint still matches.")]
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> CreateSnapshotAsync(
        [Description("Named snapshot request guarded by the current remote deck fingerprint.")]
        ArchidektSnapshotCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.CreateSnapshotAsync(request, cancellationToken));
    }

    /// <summary>
    /// Updates one unchanged snapshot's supported metadata.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_update", Title = "Update Archidekt Snapshot", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Updates supported name/description metadata only when the exact snapshot checksum still matches.")]
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> UpdateSnapshotAsync(
        [Description("Allowlisted snapshot metadata update guarded by current remote state.")]
        ArchidektSnapshotUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.UpdateSnapshotAsync(request, cancellationToken));
    }

    /// <summary>
    /// Deletes one unchanged exactly confirmed named snapshot.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_delete", Title = "Delete Archidekt Snapshot", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Deletes one exact unchanged snapshot and verifies its absence from the fresh collection.")]
    internal Task<OperationResult<ArchidektApplyResult>> DeleteSnapshotAsync(
        [Description("Exact snapshot deletion request with fingerprint and acknowledgement guards.")]
        ArchidektSnapshotDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.DeleteSnapshotAsync(request, cancellationToken));
    }

    /// <summary>
    /// Restores one unchanged named snapshot onto one unchanged remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_restore_apply", Title = "Apply Archidekt Snapshot Restore", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Replays every source/target/preview guard, applies primitive deck writes, and verifies restored content without changing local state.")]
    internal Task<OperationResult<ArchidektApplyResult>> ApplySnapshotRestoreAsync(
        [Description("Preview- and remote-state-guarded request to restore one named snapshot.")]
        ArchidektSnapshotRestoreApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.ApplySnapshotRestoreAsync(request, cancellationToken));
    }

    /// <summary>
    /// Enforces remote-write authority at invocation time in addition to static registration filtering.
    /// </summary>
    private Task<OperationResult<T>> ExecuteAsync<T>(Func<Task<OperationResult<T>>> operation)
    {
        if (!OperationModeGuard.Allows(mode, OperationRequirement.RemoteWrite))
        {
            return Task.FromResult<OperationResult<T>>(
                new OperationUnsupported(
                    "operation-mode-denied",
                    "The effective operation mode does not permit remote writes."));
        }

        return operation();
    }
}
