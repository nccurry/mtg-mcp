using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
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
    /// Stores the operation mode, so a preview can say whether its apply tool is available.
    /// </summary>
    private readonly OperationMode mode;

    /// <summary>
    /// Creates the complete read and preview surface.
    /// </summary>
    internal ArchidektReadTools(ArchidektCoordinator coordinator, OperationMode mode)
    {
        this.coordinator = coordinator;
        this.mode = mode;
    }

    /// <summary>
    /// Reports credential readiness without returning identity, values, or paths.
    /// </summary>
    [McpServerTool(Name = "archidekt_auth_status", Title = "Inspect Archidekt Authentication", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Archidekt: reports only configured, usable, or error state for Archidekt credentials; no login or secret " +
        "values are returned.")]
    internal OperationResult<ArchidektAuthStatus> AuthStatus()
    {
        return coordinator.Service.GetAuthStatus();
    }

    /// <summary>
    /// Lists one bounded authenticated page of remote decks.
    /// </summary>
    [McpServerTool(Name = "archidekt_deck_list", Title = "List Archidekt Decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: lists fresh provider-shaped Archidekt deck summaries using a bounded opaque continuation." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<RemoteDeckPage>> ListDecksAsync(
        [Description("Opaque continuation returned by a prior archidekt_deck_list call.")] string? cursor = null,
        [Description("Remote deck summaries to return, from 1 through 100.")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.ListDecksAsync(cursor, pageSize, cancellationToken);
    }

    /// <summary>
    /// Gets one fresh remote deck observation: a summary and a size-limited page of entries.
    /// </summary>
    [McpServerTool(Name = "archidekt_deck_get", Title = "Get Archidekt Deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: gets fresh deck metadata, a leading summary (entry, card, and category counts, cards by " +
        "zone), exact printings, zones, categories, provider IDs, and canonical fingerprints. Entries come in " +
        "pages that fit the size limit; categories and unknown provider fields come with the first page. When " +
        "truncated is true, call again with offset set to nextOffset." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektDeckView>> GetDeckAsync(
        [Description("Exact Archidekt deck identifier.")] string deckId,
        [Description("Number of entries to skip; use nextOffset from the previous page. Default 0.")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        OperationResult<RemoteDeckSnapshot> read = await coordinator.Service
            .GetDeckAsync(deckId, cancellationToken).ConfigureAwait(false);
        return read is OperationSuccess<RemoteDeckSnapshot> success
            ? ArchidektResultPaging.PageDeck(success.Data, offset)
            : OperationFailure.Forward<RemoteDeckSnapshot, ArchidektDeckView>(read);
    }

    /// <summary>
    /// Compares one local deck, its stored baseline, and fresh remote evidence.
    /// </summary>
    [McpServerTool(Name = "archidekt_sync_diff", Title = "Compare Local And Archidekt Deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: returns explicit local, baseline, and fresh-remote differences without selecting a conflict " +
        "winner." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<ArchidektSyncDiff>> DiffAsync(
        [Description("Stable local deck UUID with an Archidekt provider binding.")] Guid localDeckId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.DiffAsync(localDeckId, cancellationToken);
    }

    /// <summary>
    /// Previews one remote-to-local synchronization with immutable guards.
    /// </summary>
    [McpServerTool(Name = "archidekt_pull_preview", Title = "Preview Archidekt Pull", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: previews creating or transactionally replacing a local deck from fresh Archidekt evidence; " +
        "performs zero writes. The result starts with a summary (operation, difference, and conflict counts) and " +
        "says whether archidekt_pull_apply is available and which setting enables it. Rows come in pages that fit " +
        "the size limit; the guards cover the whole preview, so the apply tool needs no page." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSyncPreviewView>> PreviewPullAsync(
        [Description("Exact Archidekt deck identifier to read.")] string remoteDeckId,
        [Description("Optional stable local deck UUID to compare; omit to preview local creation.")] Guid? localDeckId = null,
        [Description(
            "Set true to take the Archidekt state over local edits made since the last synchronization (a deck " +
            "stuck as locally changed); the preview lists what would be discarded, and archidekt_pull_apply must " +
            "pass the same value. Needs localDeckId.")] bool discardLocalChanges = false,
        [Description("Number of operations to skip; use nextOperationOffset from the previous page. Default 0.")]
        int operationOffset = 0,
        [Description("Number of differences to skip; use nextDifferenceOffset from the previous page. Default 0.")]
        int differenceOffset = 0,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ArchidektSyncPreview> preview = await coordinator
            .PreviewPullAsync(remoteDeckId, localDeckId, discardLocalChanges, cancellationToken)
            .ConfigureAwait(false);
        return ToView(
            preview,
            operationOffset,
            differenceOffset,
            ArchidektResultPaging.PullApplyNotice(mode, ReadAuthStatus()));
    }

    /// <summary>
    /// Previews local-to-remote primitive operations and conflicts.
    /// </summary>
    [McpServerTool(Name = "archidekt_push_preview", Title = "Preview Archidekt Push", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: previews stable primitive Archidekt operations and refuses remote drift; performs zero writes. " +
        "The result starts with a summary (operation counts by kind, difference and conflict counts) and says " +
        "whether archidekt_push_apply is available and which setting enables it. Rows come in pages that fit the " +
        "size limit; the guards cover the whole preview, so the apply tool needs no page." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSyncPreviewView>> PreviewPushAsync(
        [Description("Stable local deck UUID with an Archidekt provider binding.")] Guid localDeckId,
        [Description("Number of operations to skip; use nextOperationOffset from the previous page. Default 0.")]
        int operationOffset = 0,
        [Description("Number of differences to skip; use nextDifferenceOffset from the previous page. Default 0.")]
        int differenceOffset = 0,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ArchidektSyncPreview> preview = await coordinator
            .PreviewPushAsync(localDeckId, cancellationToken).ConfigureAwait(false);
        return ToView(
            preview,
            operationOffset,
            differenceOffset,
            ArchidektResultPaging.PushApplyNotice(mode, ReadAuthStatus()));
    }

    /// <summary>
    /// Reads the credential state without any provider request.
    /// </summary>
    private ArchidektAuthStatus ReadAuthStatus()
    {
        return coordinator.Service.GetAuthStatus() is OperationSuccess<ArchidektAuthStatus> status
            ? status.Data
            : new ArchidektAuthStatus("unknown", CredentialsConfigured: false, SessionAuthenticated: false, string.Empty);
    }

    /// <summary>
    /// Turns a preview result into one page of it, or forwards its failure.
    /// </summary>
    private static OperationResult<ArchidektSyncPreviewView> ToView(
        OperationResult<ArchidektSyncPreview> preview,
        int operationOffset,
        int differenceOffset,
        string? applyNotice)
    {
        return preview is OperationSuccess<ArchidektSyncPreview> success
            ? ArchidektResultPaging.PagePreview(success.Data, operationOffset, differenceOffset, applyNotice)
            : OperationFailure.Forward<ArchidektSyncPreview, ArchidektSyncPreviewView>(preview);
    }

    /// <summary>
    /// Previews restoring one named snapshot onto the current remote deck.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_snapshot_restore_preview",
        Title = "Preview Archidekt Snapshot Restore",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Archidekt: diffs a fresh named snapshot against the fresh current deck and returns immutable restore " +
        "guards; performs zero writes." +
        UntrustedTextNotes.Archidekt)]
    internal Task<OperationResult<ArchidektSnapshotRestorePreview>> PreviewSnapshotRestoreAsync(
        [Description("Exact Archidekt deck identifier that would be restored.")] string deckId,
        [Description("Exact Archidekt snapshot identifier whose saved state would be applied.")] string snapshotId,
        CancellationToken cancellationToken = default)
    {
        return coordinator.Service.PreviewSnapshotRestoreAsync(deckId, snapshotId, cancellationToken);
    }

    /// <summary>
    /// Lists the complete folder tree or gets one folder with its direct contents.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_folders_get",
        Title = "Get Archidekt Folders",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Archidekt: reads folders. kind list returns the fresh canonical folder tree with direct deck assignments, " +
        "extensions, and the tree fingerprint; kind get returns one exact folder (folderId is required) with its " +
        "direct child folders and decks and canonical tree evidence. The result has the same kind: list holds the " +
        "folder tree and get holds one folder. Example: " +
        "{\"kind\":\"get\",\"folderId\":\"12345\"}." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektFoldersResult>> GetFoldersAsync(
        [Description("What to read: list (the whole folder tree) or get (one folder)."), AllowedValues("list", "get")]
        string kind,
        [Description("Only with kind get, and then required: exact Archidekt folder identifier.")]
        string? folderId = null,
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? invalid = KindFields.CheckKind(kind, "list", "get") ?? (kind == "list"
            ? KindFields.RejectGiven(kind, ("folderId", folderId is not null))
            : KindFields.RequireText(kind, "folderId", folderId));
        if (invalid is not null)
        {
            return invalid;
        }

        if (kind == "list")
        {
            OperationResult<RemoteFolderTree> list = await coordinator.Service
                .ListFoldersAsync(cancellationToken).ConfigureAwait(false);
            return list is OperationSuccess<RemoteFolderTree> listSuccess
                ? new OperationSuccess<ArchidektFoldersResult>(new ArchidektFolderListResult(listSuccess.Data))
                : OperationFailure.Forward<RemoteFolderTree, ArchidektFoldersResult>(list);
        }

        OperationResult<RemoteFolderTree> get = await coordinator.Service
            .GetFolderAsync(folderId!, cancellationToken).ConfigureAwait(false);
        return get is OperationSuccess<RemoteFolderTree> getSuccess
            ? new OperationSuccess<ArchidektFoldersResult>(new ArchidektFolderGetResult(getSuccess.Data))
            : OperationFailure.Forward<RemoteFolderTree, ArchidektFoldersResult>(get);
    }

    /// <summary>
    /// Lists named snapshot metadata for one remote deck or gets one snapshot with its saved deck state.
    /// </summary>
    [McpServerTool(
        Name = "archidekt_snapshots_get",
        Title = "Get Archidekt Snapshots",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description(
        "Archidekt: reads named snapshots of one remote deck. kind list returns snapshot metadata and a collection " +
        "checksum without claiming list rows contain saved deck state; kind get returns one snapshot (snapshotId is " +
        "required) with its exact identity and checksum, extensions, and complete canonical saved deck state. The " +
        "result has the same kind. Example: {\"kind\":\"list\",\"deckId\":\"12345\"}." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSnapshotsResult>> GetSnapshotsAsync(
        [Description("What to read: list (snapshot metadata) or get (one snapshot with saved deck state)."),
         AllowedValues("list", "get")]
        string kind,
        [Description("Exact Archidekt deck identifier that owns the snapshots.")] string deckId,
        [Description("Only with kind get, and then required: exact Archidekt snapshot identifier.")]
        string? snapshotId = null,
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? invalid = KindFields.CheckKind(kind, "list", "get") ?? (kind == "list"
            ? KindFields.RejectGiven(kind, ("snapshotId", snapshotId is not null))
            : KindFields.RequireText(kind, "snapshotId", snapshotId));
        if (invalid is not null)
        {
            return invalid;
        }

        if (kind == "list")
        {
            OperationResult<RemoteNamedSnapshotPage> list = await coordinator.Service
                .ListSnapshotsAsync(deckId, cancellationToken).ConfigureAwait(false);
            return list is OperationSuccess<RemoteNamedSnapshotPage> listSuccess
                ? new OperationSuccess<ArchidektSnapshotsResult>(new ArchidektSnapshotListResult(listSuccess.Data))
                : OperationFailure.Forward<RemoteNamedSnapshotPage, ArchidektSnapshotsResult>(list);
        }

        OperationResult<RemoteNamedSnapshot> get = await coordinator.Service
            .GetSnapshotAsync(deckId, snapshotId!, cancellationToken).ConfigureAwait(false);
        return get is OperationSuccess<RemoteNamedSnapshot> getSuccess
            ? new OperationSuccess<ArchidektSnapshotsResult>(new ArchidektSnapshotGetResult(getSuccess.Data))
            : OperationFailure.Forward<RemoteNamedSnapshot, ArchidektSnapshotsResult>(get);
    }
}
