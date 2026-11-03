using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
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
    [Description(
        "Archidekt: creates and verifies one empty Archidekt deck shell; contents require a separate push " +
        "preview/apply. Pass localDeckId with its current expectedRevision to bind the new deck to an unchanged " +
        "local deck; a changed local deck is refused with stale-deck-revision." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<ArchidektCreatedDeckView>> CreateDeckAsync(
        [Description("The deck name.")] string name,
        [Description("The deck format."), DeckFormatValues] string format,
        [Description("The deck description; omit for none.")] string? description = null,
        [Description("Who can see the deck; default private."), DeckVisibilityValues]
        string visibility = "private",
        [Description("The Archidekt folder ID to create the deck in; omit for the root.")]
        string? parentFolderId = null,
        [Description("Optional stable local deck UUID to bind after verified creation.")] Guid? localDeckId = null,
        [Description("Required current local revision when localDeckId is provided.")] long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () => ArchidektCreatedDeckView.Show(await coordinator.CreateRemoteDeckAsync(
            localDeckId,
            expectedRevision,
            new ArchidektDeckCreateRequest(name, format, description, visibility, parentFolderId),
            cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Deletes one unchanged exactly confirmed remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_deck_delete", Title = "Delete Archidekt Deck", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.RequiresUserInteractionKey, JsonValue = "true")]
    [Description(
        "Archidekt: deletes one exact unchanged Archidekt deck and verifies listing absence; local decks and " +
        "bindings remain untouched. confirmation must be exactly \"delete deck <remoteDeckId>\".")]
    internal Task<OperationResult<ArchidektApplyResult>> DeleteDeckAsync(
        [Description("The Archidekt deck ID.")] string remoteDeckId,
        [Description("The remoteFingerprint from a fresh archidekt_deck_get; a changed deck is refused.")]
        string expectedRemoteFingerprint,
        [Description("The exact text \"delete deck <remoteDeckId>\" with the deck ID filled in.")]
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.DeleteDeckAsync(
            new ArchidektDeckDeleteRequest(remoteDeckId, expectedRemoteFingerprint, confirmation),
            cancellationToken));
    }

    /// <summary>
    /// Applies one unchanged local-to-remote preview and updates the baseline only after verification.
    /// </summary>
    [McpServerTool(Name = "archidekt_push_apply", Title = "Apply Archidekt Push", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.RequiresUserInteractionKey, JsonValue = "true")]
    [Description(
        "Archidekt: refetches local and remote state, verifies preview guards, executes stable primitive operations, " +
        "then reads Archidekt back and verifies each card, so no separate check is needed. The guards are checked in " +
        "order: a changed local deck is refused with stale-deck-revision, a changed Archidekt deck with " +
        "remote-deck-changed, and any other change to the preview with preview-changed. A push that removes " +
        "more than 10 Archidekt entries, or 3 or more entries that are more than 10 percent of them, is refused " +
        "unless confirmRemovals equals the removal count." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<ArchidektApplyResult>> ApplyPushAsync(
        [Description("The local deck ID, as in the preview.")] Guid localDeckId,
        [Description("The revision from the preview.")] long expectedRevision,
        [Description("The remoteFingerprint from the preview.")] string expectedRemoteFingerprint,
        [Description("The previewFingerprint from the preview.")] string previewFingerprint,
        [Description("The number of Archidekt entries the push removes, when the preview asks for confirmation.")]
        int? confirmRemovals = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                expectedRevision,
                expectedRemoteFingerprint,
                previewFingerprint,
                confirmRemovals),
            cancellationToken));
    }

    /// <summary>
    /// Creates one folder under an exact optional parent.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_create", Title = "Create Archidekt Folder", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: creates and verifies one explicitly named Archidekt folder without same-name inference or " +
        "recursive creation." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<ArchidektFolderView>> CreateFolderAsync(
        [Description("The folder name.")] string name,
        [Description("Who can see the folder."), FolderVisibilityValues] string visibility,
        [Description("The parent folder ID; omit for the root.")] string? parentFolderId = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () => ArchidektFolderView.Show(await coordinator.Service.CreateFolderAsync(
            new ArchidektFolderCreateRequest(name, visibility, parentFolderId),
            cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Updates allowlisted metadata for one unchanged folder tree.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_update", Title = "Update Archidekt Folder", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: updates one exact folder only when its fresh tree fingerprint and optional destination remain " +
        "valid." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<ArchidektFolderView>> UpdateFolderAsync(
        [Description("The folder to update.")] string folderId,
        [Description("The treeFingerprint from a fresh archidekt_folders_get.")] string expectedTreeFingerprint,
        [Description("The new name; omit to keep it.")] string? name = null,
        [Description("The new visibility; omit to keep it."), FolderVisibilityValues] string? visibility = null,
        [Description(
            "The new parent folder ID; null moves the folder to the root. Used only when updateParent is true.")]
        string? parentFolderId = null,
        [Description("Set true to move the folder to parentFolderId; default false leaves the parent as it is.")]
        bool updateParent = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () => ArchidektFolderView.Show(await coordinator.Service.UpdateFolderAsync(
            new ArchidektFolderUpdateRequest(
                folderId,
                expectedTreeFingerprint,
                name,
                visibility,
                parentFolderId,
                updateParent),
            cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Moves exact typed items after stale-parent and cycle preflight.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_move_items", Title = "Move Archidekt Folder Items", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.RequiresUserInteractionKey, JsonValue = "true")]
    [Description(
        "Archidekt: moves deduplicated exact deck/folder IDs after fresh source-parent and cycle checks, then " +
        "verifies each assignment. Each item names its kind, its id, and its expectedParentFolderId.")]
    internal Task<OperationResult<ArchidektFolderMoveResult>> MoveFolderItemsAsync(
        [Description("The treeFingerprint from a fresh archidekt_folders_get.")] string expectedTreeFingerprint,
        [Description("The decks and folders to move.")] IReadOnlyList<ArchidektFolderMoveItem> items,
        [Description("The folder to move them into; null moves them to the root.")]
        string? destinationFolderId = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.MoveFolderItemsAsync(
            new ArchidektFolderMoveRequest(expectedTreeFingerprint, items, destinationFolderId),
            cancellationToken));
    }

    /// <summary>
    /// Deletes one exactly confirmed empty folder without recursive or deck deletion.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_delete", Title = "Delete Archidekt Folder", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.RequiresUserInteractionKey, JsonValue = "true")]
    [Description(
        "Archidekt: deletes one unchanged empty folder only; never submits deck items or recursive deletion. " +
        "confirmation must be exactly \"delete folder <folderId>\".")]
    internal Task<OperationResult<ArchidektApplyResult>> DeleteFolderAsync(
        [Description("The folder to delete; it must be empty.")] string folderId,
        [Description("The folder's current name.")] string expectedName,
        [Description("The treeFingerprint from a fresh archidekt_folders_get.")] string expectedTreeFingerprint,
        [Description("The exact text \"delete folder <folderId>\" with the folder ID filled in.")]
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.DeleteFolderAsync(
            new ArchidektFolderDeleteRequest(folderId, expectedName, expectedTreeFingerprint, confirmation),
            cancellationToken));
    }

    /// <summary>
    /// Creates one named snapshot from an unchanged remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_create", Title = "Create Archidekt Snapshot", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: creates and verifies one explicitly named snapshot only when the source deck fingerprint still " +
        "matches." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> CreateSnapshotAsync(
        [Description("The Archidekt deck ID.")] string remoteDeckId,
        [Description("The remoteFingerprint from a fresh archidekt_deck_get; a changed deck is refused.")]
        string expectedRemoteFingerprint,
        [Description("The snapshot name.")] string name,
        [Description("The snapshot description; omit for none.")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.CreateSnapshotAsync(
            new ArchidektSnapshotCreateRequest(remoteDeckId, expectedRemoteFingerprint, name, description),
            cancellationToken));
    }

    /// <summary>
    /// Updates one unchanged snapshot's supported metadata.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_update", Title = "Update Archidekt Snapshot", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: updates supported name/description metadata only when the exact snapshot checksum still matches." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<RemoteNamedSnapshotSummary>> UpdateSnapshotAsync(
        [Description("The Archidekt deck ID.")] string remoteDeckId,
        [Description("The snapshot ID.")] string snapshotId,
        [Description("The snapshot's checksum from archidekt_snapshots_get.")] string expectedChecksum,
        [Description("The new snapshot name.")] string name,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.UpdateSnapshotAsync(
            new ArchidektSnapshotUpdateRequest(remoteDeckId, snapshotId, expectedChecksum, name),
            cancellationToken));
    }

    /// <summary>
    /// Deletes one unchanged exactly confirmed named snapshot.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_delete", Title = "Delete Archidekt Snapshot", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.RequiresUserInteractionKey, JsonValue = "true")]
    [Description(
        "Archidekt: deletes one exact unchanged snapshot and verifies its absence from the fresh collection. " +
        "confirmation must be exactly \"delete snapshot <snapshotId>\".")]
    internal Task<OperationResult<ArchidektApplyResult>> DeleteSnapshotAsync(
        [Description("The Archidekt deck ID.")] string remoteDeckId,
        [Description("The snapshot ID.")] string snapshotId,
        [Description("The snapshot's checksum from archidekt_snapshots_get.")] string expectedChecksum,
        [Description("The exact text \"delete snapshot <snapshotId>\" with the snapshot ID filled in.")]
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.DeleteSnapshotAsync(
            new ArchidektSnapshotDeleteRequest(remoteDeckId, snapshotId, expectedChecksum, confirmation),
            cancellationToken));
    }

    /// <summary>
    /// Restores one unchanged named snapshot onto one unchanged remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_restore_apply", Title = "Apply Archidekt Snapshot Restore", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [McpMeta(ToolMetadata.RequiresUserInteractionKey, JsonValue = "true")]
    [Description(
        "Archidekt: replays every source/target/preview guard, applies primitive deck writes, and verifies restored " +
        "content without changing local state. confirmation must be exactly \"restore snapshot <snapshotId>\"." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<ArchidektApplyResult>> ApplySnapshotRestoreAsync(
        [Description("The Archidekt deck ID.")] string remoteDeckId,
        [Description("The snapshot ID.")] string snapshotId,
        [Description("The snapshotChecksum from the restore preview.")] string expectedSnapshotChecksum,
        [Description("The snapshotContentFingerprint from the restore preview.")]
        string expectedSnapshotContentFingerprint,
        [Description("The remoteFingerprint from the restore preview.")] string expectedRemoteFingerprint,
        [Description("The previewFingerprint from the restore preview.")] string previewFingerprint,
        [Description("The exact text \"restore snapshot <snapshotId>\" with the snapshot ID filled in.")]
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => coordinator.Service.ApplySnapshotRestoreAsync(
            new ArchidektSnapshotRestoreApplyRequest(
                remoteDeckId,
                snapshotId,
                expectedSnapshotChecksum,
                expectedSnapshotContentFingerprint,
                expectedRemoteFingerprint,
                previewFingerprint,
                confirmation),
            cancellationToken));
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
