using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Archidekt;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Exposes fresh Archidekt evidence and zero-write synchronization previews in every operation mode.
/// </summary>
internal sealed class ArchidektReadTools
{
    /// <summary>
    /// Provides App-owned local/provider composition.
    /// </summary>
    private readonly ArchidektCoordinator coordinator;

    /// <summary>
    /// Creates the complete read and preview surface.
    /// </summary>
    internal ArchidektReadTools(ArchidektCoordinator coordinator)
    {
        this.coordinator = coordinator;
    }

    /// <summary>
    /// Reports credential readiness without returning identity, values, or paths.
    /// </summary>
    [McpServerTool(Name = "archidekt_auth_status", Title = "Inspect Archidekt Authentication", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reports only configured, usable, or error state for Archidekt credentials; no login or secret values are returned.")]
    internal OperationResult<ArchidektAuthStatus> AuthStatus()
    {
        return coordinator.Service.GetAuthStatus();
    }

    /// <summary>
    /// Lists one bounded authenticated page of remote decks.
    /// </summary>
    [McpServerTool(Name = "archidekt_deck_list", Title = "List Archidekt Decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists fresh provider-shaped Archidekt deck summaries using a bounded opaque continuation.")]
    internal Task<OperationResult<RemoteDeckPage>> ListDecksAsync(
        string? cursor = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.ListDecksAsync(cursor, pageSize, cancellationToken);
    }

    /// <summary>
    /// Gets one complete fresh remote deck observation.
    /// </summary>
    [McpServerTool(Name = "archidekt_deck_get", Title = "Get Archidekt Deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets fresh Archidekt metadata, exact printings, zones, categories, provider IDs, and canonical fingerprints.")]
    internal Task<OperationResult<RemoteDeckSnapshot>> GetDeckAsync(
        string deckId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.GetDeckAsync(deckId, cancellationToken);
    }

    /// <summary>
    /// Compares one local deck, its stored baseline, and fresh remote evidence.
    /// </summary>
    [McpServerTool(Name = "archidekt_sync_diff", Title = "Compare Local And Archidekt Deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns explicit local, baseline, and fresh-remote differences without selecting a conflict winner.")]
    internal Task<OperationResult<ArchidektSyncDiff>> DiffAsync(
        Guid localDeckId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.DiffAsync(localDeckId, cancellationToken);
    }

    /// <summary>
    /// Previews one remote-to-local synchronization with immutable guards.
    /// </summary>
    [McpServerTool(Name = "archidekt_pull_preview", Title = "Preview Archidekt Pull", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Previews creating or transactionally replacing a local deck from fresh Archidekt evidence; performs zero writes.")]
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewPullAsync(
        string remoteDeckId,
        Guid? localDeckId = null,
        CancellationToken cancellationToken = default)
    {
        return coordinator.PreviewPullAsync(remoteDeckId, localDeckId, cancellationToken);
    }

    /// <summary>
    /// Previews local-to-remote primitive operations and conflicts.
    /// </summary>
    [McpServerTool(Name = "archidekt_push_preview", Title = "Preview Archidekt Push", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Previews stable primitive Archidekt operations and refuses remote drift; performs zero writes.")]
    internal Task<OperationResult<ArchidektSyncPreview>> PreviewPushAsync(
        Guid localDeckId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.PreviewPushAsync(localDeckId, cancellationToken);
    }

    /// <summary>
    /// Lists the complete fresh folder tree.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_list", Title = "List Archidekt Folders", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists the fresh canonical Archidekt folder tree, direct deck assignments, extensions, and tree fingerprint.")]
    internal Task<OperationResult<RemoteFolderTree>> ListFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.ListFoldersAsync(cancellationToken);
    }

    /// <summary>
    /// Gets one folder and its direct contents.
    /// </summary>
    [McpServerTool(Name = "archidekt_folder_get", Title = "Get Archidekt Folder", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets one exact Archidekt folder with direct child folders/decks and canonical tree evidence.")]
    internal Task<OperationResult<RemoteFolderTree>> GetFolderAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.GetFolderAsync(folderId, cancellationToken);
    }

    /// <summary>
    /// Lists exact named snapshot metadata for one remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_list", Title = "List Archidekt Snapshots", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists named Archidekt snapshot metadata and a collection checksum without claiming list rows contain saved deck state.")]
    internal Task<OperationResult<RemoteNamedSnapshotPage>> ListSnapshotsAsync(
        string deckId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.ListSnapshotsAsync(deckId, cancellationToken);
    }

    /// <summary>
    /// Gets one complete named snapshot with its saved deck state.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_get", Title = "Get Archidekt Snapshot", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets one named snapshot, exact identity/checksum, extensions, and complete canonical saved deck state.")]
    internal Task<OperationResult<RemoteNamedSnapshot>> GetSnapshotAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.GetSnapshotAsync(deckId, snapshotId, cancellationToken);
    }

    /// <summary>
    /// Previews restoring one named snapshot onto the current remote deck.
    /// </summary>
    [McpServerTool(Name = "archidekt_snapshot_restore_preview", Title = "Preview Archidekt Snapshot Restore", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Diffs a fresh named snapshot against the fresh current deck and returns immutable restore guards; performs zero writes.")]
    internal Task<OperationResult<ArchidektSnapshotRestorePreview>> PreviewSnapshotRestoreAsync(
        string deckId,
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.PreviewSnapshotRestoreAsync(deckId, snapshotId, cancellationToken);
    }
}
