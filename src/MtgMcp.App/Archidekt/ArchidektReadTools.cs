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
    /// Describes the zones argument of the tools that read a deck's entries.
    /// </summary>
    private const string ZonesDescription =
        "Optional zones to read: commander, main, sideboard, maybeboard. Omit to read every zone. Offsets count " +
        "the chosen entries.";

    /// <summary>
    /// Describes the responseFormat argument of the tools that read a deck's entries.
    /// </summary>
    private const string ResponseFormatDescription =
        "Entry fields: concise (default: card name, quantity, zone, categories, printing, Oracle ID, inDeck), or " +
        "full or standard (every entry field: provider IDs, finish, language, sort order, and the rest).";

    /// <summary>
    /// Gets the allowed item values of the list parameters, published in the tool schema.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ItemChoices { get; } =
        new Dictionary<string, IReadOnlyList<string>> { ["zones"] = ArchidektDeckReadOptions.ZoneOrder };

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
        "Archidekt: gets fresh deck metadata and entries. Entries hold card name, quantity, zone, categories, " +
        "printing, Oracle ID, and inDeck; pass responseFormat \"full\" for every field (provider IDs, finish, " +
        "language, sort order, and the rest). Pass zones (commander, main, sideboard, maybeboard) to read only " +
        "some; offsets count the chosen entries. The leading summary covers the whole deck whatever the zones: " +
        "entry, card, and category counts, cards by zone, cards held more than once across commander and main " +
        "(duplicates, matched by Oracle ID, basic lands and entries not in the deck skipped), cards held more than " +
        "once inside the sideboard or maybeboard (repeatsOutsideMain), and for Commander " +
        "decks the main and commander card count next to 100. Entries come in pages that fit the size limit, " +
        "ordered by zone, then card name; categories and unknown provider fields come with the first page. Each " +
        "entry has inDeck: false when its first category is not included in the deck (it is then in the " +
        "maybeboard zone). Unknown provider fields over 8,000 bytes are left out and extensionsOmitted is true. " +
        "When truncated is true, call again with offset set to nextOffset." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektDeckView>> GetDeckAsync(
        [Description("Exact Archidekt deck identifier.")] string deckId,
        [Description(
            "Number of entries to skip, counting only the chosen zones; use nextOffset from the previous page. " +
            "Default 0.")]
        int offset = 0,
        [Description(ZonesDescription)] IReadOnlyList<string>? zones = null,
        [Description(ResponseFormatDescription), ResponseFormatValues] string responseFormat = "concise",
        CancellationToken cancellationToken = default)
    {
        OperationResult<ArchidektDeckReadOptions> options = ArchidektDeckReadOptions.Parse(zones, responseFormat);
        if (options is not OperationSuccess<ArchidektDeckReadOptions> chosen)
        {
            return OperationFailure.Forward<ArchidektDeckReadOptions, ArchidektDeckView>(options);
        }

        OperationResult<RemoteDeckSnapshot> read = await coordinator.Service
            .GetDeckAsync(deckId, cancellationToken).ConfigureAwait(false);
        return read is OperationSuccess<RemoteDeckSnapshot> success
            ? ArchidektResultPaging.PageDeck(success.Data, offset, chosen.Data)
            : OperationFailure.Forward<RemoteDeckSnapshot, ArchidektDeckView>(read);
    }

    /// <summary>
    /// Compares one local deck, its stored baseline, and fresh remote evidence.
    /// </summary>
    [McpServerTool(Name = "archidekt_sync_diff", Title = "Compare Local And Archidekt Deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: returns explicit local, baseline, and fresh-remote differences without selecting a conflict " +
        "winner. The result starts with the difference count; rows come in pages that fit the size limit, so when " +
        "truncated is true, call again with differenceOffset set to nextDifferenceOffset." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSyncDiffView>> DiffAsync(
        [Description("Stable local deck UUID with an Archidekt provider binding.")] Guid localDeckId,
        [Description("Number of differences to skip; use nextDifferenceOffset from the previous page. Default 0.")]
        int differenceOffset = 0,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ArchidektSyncDiff> diff = await coordinator
            .DiffAsync(localDeckId, cancellationToken).ConfigureAwait(false);
        return diff is OperationSuccess<ArchidektSyncDiff> success
            ? ArchidektResultPaging.PageDiff(success.Data, differenceOffset)
            : OperationFailure.Forward<ArchidektSyncDiff, ArchidektSyncDiffView>(diff);
    }

    /// <summary>
    /// Previews one remote-to-local synchronization with immutable guards.
    /// </summary>
    [McpServerTool(Name = "archidekt_pull_preview", Title = "Preview Archidekt Pull", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: previews creating or transactionally replacing a local deck from fresh Archidekt evidence; " +
        "performs zero writes. The result starts with a summary (operation, difference, and conflict counts) and " +
        "says whether archidekt_pull_apply is available and, if not, what to ask the user to change. A public deck " +
        "needs no credentials; a private deck needs Archidekt credentials (see archidekt_auth_status). Rows come in " +
        "pages that fit the size limit; the guards cover the whole preview, so the apply tool needs no page. On " +
        "later pages pass expectedPreviewFingerprint, the previewFingerprint of the first page." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSyncPreviewView>> PreviewPullAsync(
        [Description("Exact Archidekt deck identifier to read.")] string remoteDeckId,
        [Description("Optional stable local deck UUID to compare; omit to preview local creation.")]
        Guid? localDeckId = null,
        [Description(
            "Set true to take the Archidekt state over local edits made since the last synchronization (a deck " +
            "stuck as locally changed); the preview lists what would be discarded, and archidekt_pull_apply must " +
            "pass the same value. Needs localDeckId.")] bool discardLocalChanges = false,
        [Description("Number of operations to skip; use nextOperationOffset from the previous page. Default 0.")]
        int operationOffset = 0,
        [Description("Number of differences to skip; use nextDifferenceOffset from the previous page. Default 0.")]
        int differenceOffset = 0,
        [Description(ResultPage.FingerprintHelp)] string? expectedPreviewFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ArchidektSyncPreview> preview = await coordinator
            .PreviewPullAsync(remoteDeckId, localDeckId, discardLocalChanges, cancellationToken)
            .ConfigureAwait(false);
        return ToView(
            preview,
            operationOffset,
            differenceOffset,
            ArchidektApplyAvailability.PullNotice(mode),
            expectedPreviewFingerprint);
    }

    /// <summary>
    /// Previews local-to-remote primitive operations and conflicts.
    /// </summary>
    [McpServerTool(Name = "archidekt_push_preview", Title = "Preview Archidekt Push", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description(
        "Archidekt: previews stable primitive Archidekt operations and refuses remote drift; performs zero writes. " +
        "The result starts with a summary (operation counts by kind, difference and conflict counts) and says " +
        "whether archidekt_push_apply is available and, if not, what to ask the user to change. Rows come in pages " +
        "that fit the size limit; the guards cover the whole preview, so the apply tool needs no page. A new card " +
        "with no printing set gets Archidekt's default printing; each add names the printing it will use, and " +
        "applyAvailable is false, with the card names, when Archidekt has no match. On later " +
        "pages pass expectedPreviewFingerprint, the previewFingerprint of the first page. A push that removes more " +
        "than 10 Archidekt entries, or more than 10 percent of them, is not available until confirmRemovals " +
        "equals the removal count." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSyncPreviewView>> PreviewPushAsync(
        [Description("Stable local deck UUID with an Archidekt provider binding.")] Guid localDeckId,
        [Description("Number of operations to skip; use nextOperationOffset from the previous page. Default 0.")]
        int operationOffset = 0,
        [Description("Number of differences to skip; use nextDifferenceOffset from the previous page. Default 0.")]
        int differenceOffset = 0,
        [Description(ResultPage.FingerprintHelp)] string? expectedPreviewFingerprint = null,
        [Description(
            "Number of Archidekt entries the push removes, to allow a large removal (more than 10 entries or 10 " +
            "percent). Use the count the preview names; pass the same value to archidekt_push_apply.")]
        int? confirmRemovals = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ArchidektSyncPreview> preview = await coordinator
            .PreviewPushAsync(localDeckId, cancellationToken, confirmRemovals).ConfigureAwait(false);
        return ToView(
            preview,
            operationOffset,
            differenceOffset,
            ArchidektApplyAvailability.PushNotice(mode, ReadAuthStatus()),
            expectedPreviewFingerprint);
    }

    /// <summary>
    /// Reads the credential state without any provider request.
    /// </summary>
    private ArchidektAuthStatus ReadAuthStatus()
    {
        return coordinator.Service.GetAuthStatus() is OperationSuccess<ArchidektAuthStatus> status
            ? status.Data
            : new ArchidektAuthStatus(
                "unknown",
                CredentialsConfigured: false,
                SessionAuthenticated: false,
                string.Empty);
    }

    /// <summary>
    /// Turns a preview result into one page of it, or forwards its failure.
    /// </summary>
    private static OperationResult<ArchidektSyncPreviewView> ToView(
        OperationResult<ArchidektSyncPreview> preview,
        int operationOffset,
        int differenceOffset,
        string? applyNotice,
        string? expectedPreviewFingerprint)
    {
        return preview is OperationSuccess<ArchidektSyncPreview> success
            ? ArchidektResultPaging.PagePreview(
                success.Data,
                operationOffset,
                differenceOffset,
                applyNotice,
                expectedPreviewFingerprint)
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
        "guards; performs zero writes. The result starts with operation and difference counts; rows come in pages " +
        "that fit the size limit and the guards cover the whole preview, so when truncated is true, call again " +
        "with operationOffset and differenceOffset set to the next values and expectedPreviewFingerprint set " +
        "to the previewFingerprint of the first page." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSnapshotRestorePreviewView>> PreviewSnapshotRestoreAsync(
        [Description("Exact Archidekt deck identifier that would be restored.")] string deckId,
        [Description("Exact Archidekt snapshot identifier whose saved state would be applied.")] string snapshotId,
        [Description("Number of operations to skip; use nextOperationOffset from the previous page. Default 0.")]
        int operationOffset = 0,
        [Description("Number of differences to skip; use nextDifferenceOffset from the previous page. Default 0.")]
        int differenceOffset = 0,
        [Description(ResultPage.FingerprintHelp)] string? expectedPreviewFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        OperationResult<ArchidektSnapshotRestorePreview> preview = await coordinator.Service
            .PreviewSnapshotRestoreAsync(deckId, snapshotId, cancellationToken).ConfigureAwait(false);
        return preview is OperationSuccess<ArchidektSnapshotRestorePreview> success
            ? ArchidektResultPaging.PageRestorePreview(
                success.Data,
                operationOffset,
                differenceOffset,
                expectedPreviewFingerprint)
            : OperationFailure.Forward<ArchidektSnapshotRestorePreview, ArchidektSnapshotRestorePreviewView>(preview);
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
        "{\"kind\":\"get\",\"folderId\":\"12345\"}. Folder records come in pages that fit the size limit, with the " +
        "folder count first; when truncated is true, call again with offset set to nextOffset." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektFoldersResult>> GetFoldersAsync(
        [Description("What to read: list (the whole folder tree) or get (one folder)."), AllowedValues("list", "get")]
        string kind,
        [Description("Only with kind get, and then required: exact Archidekt folder identifier.")]
        string? folderId = null,
        [Description("Number of folder records to skip; use nextOffset from the previous page. Default 0.")]
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? invalid = KindFields.CheckKind(kind, "list", "get") ?? (kind == "list"
            ? KindFields.RejectGiven(kind, ("folderId", folderId is not null))
            : KindFields.RequireText(kind, "folderId", folderId));
        if (invalid is not null)
        {
            return invalid;
        }

        OperationResult<RemoteFolderTree> read = kind == "list"
            ? await coordinator.Service.ListFoldersAsync(cancellationToken).ConfigureAwait(false)
            : await coordinator.Service.GetFolderAsync(folderId!, cancellationToken).ConfigureAwait(false);
        if (read is not OperationSuccess<RemoteFolderTree> success)
        {
            return OperationFailure.Forward<RemoteFolderTree, ArchidektFoldersResult>(read);
        }

        OperationResult<ArchidektFolderTreeView> page = ArchidektResultPaging.PageFolders(success.Data, offset);
        if (page is not OperationSuccess<ArchidektFolderTreeView> view)
        {
            return OperationFailure.Forward<ArchidektFolderTreeView, ArchidektFoldersResult>(page);
        }

        return new OperationSuccess<ArchidektFoldersResult>(kind == "list"
            ? new ArchidektFolderListResult(view.Data)
            : new ArchidektFolderGetResult(view.Data));
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
        "result has the same kind. The saved deck of kind get comes in pages that fit the size limit, like " +
        "archidekt_deck_get: entries hold the concise fields; pass responseFormat \"full\" for every field and " +
        "zones (commander, main, sideboard, maybeboard) to read only some; when truncated is true, call again " +
        "with offset set to nextOffset. " +
        "Example: {\"kind\":\"list\",\"deckId\":\"12345\"}." +
        UntrustedTextNotes.Archidekt)]
    internal async Task<OperationResult<ArchidektSnapshotsResult>> GetSnapshotsAsync(
        [Description("What to read: list (snapshot metadata) or get (one snapshot with saved deck state)."),
         AllowedValues("list", "get")]
        string kind,
        [Description("Exact Archidekt deck identifier that owns the snapshots.")] string deckId,
        [Description("Only with kind get, and then required: exact Archidekt snapshot identifier.")]
        string? snapshotId = null,
        [Description(
            "Only with kind get: number of saved deck entries to skip; use nextOffset from the previous page. " +
            "Default 0.")]
        int offset = 0,
        [Description("Only with kind get: " + ZonesDescription)] IReadOnlyList<string>? zones = null,
        [Description("Only with kind get: " + ResponseFormatDescription), ResponseFormatValues]
        string responseFormat = "concise",
        CancellationToken cancellationToken = default)
    {
        OperationInvalidInput? invalid = KindFields.CheckKind(kind, "list", "get") ?? (kind == "list"
            ? KindFields.RejectGiven(kind, ("snapshotId", snapshotId is not null))
            : KindFields.RequireText(kind, "snapshotId", snapshotId));
        if (invalid is not null)
        {
            return invalid;
        }

        OperationResult<ArchidektDeckReadOptions> options = ArchidektDeckReadOptions.Parse(zones, responseFormat);
        if (options is not OperationSuccess<ArchidektDeckReadOptions> chosen)
        {
            return OperationFailure.Forward<ArchidektDeckReadOptions, ArchidektSnapshotsResult>(options);
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
        if (get is not OperationSuccess<RemoteNamedSnapshot> getSuccess)
        {
            return OperationFailure.Forward<RemoteNamedSnapshot, ArchidektSnapshotsResult>(get);
        }

        OperationResult<ArchidektSnapshotView> page = ArchidektResultPaging.PageSnapshot(
            getSuccess.Data,
            offset,
            chosen.Data);
        return page is OperationSuccess<ArchidektSnapshotView> view
            ? new OperationSuccess<ArchidektSnapshotsResult>(new ArchidektSnapshotGetResult(view.Data))
            : OperationFailure.Forward<ArchidektSnapshotView, ArchidektSnapshotsResult>(page);
    }
}
