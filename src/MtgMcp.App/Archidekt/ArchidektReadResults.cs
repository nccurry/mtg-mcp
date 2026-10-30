using System.Text.Json.Serialization;
using MtgMcp.Archidekt;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Returns one snapshot result whose <c>kind</c> matches the requested <c>kind</c>: list or get.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ArchidektSnapshotListResult), "list")]
[JsonDerivedType(typeof(ArchidektSnapshotGetResult), "get")]
internal abstract record ArchidektSnapshotsResult;

/// <summary>
/// Holds the snapshot metadata page for <c>kind</c> list.
/// </summary>
internal sealed record ArchidektSnapshotListResult(
    [property: JsonPropertyName("snapshots")] RemoteNamedSnapshotPage Snapshots) : ArchidektSnapshotsResult;

/// <summary>
/// Holds one snapshot with its saved deck state for <c>kind</c> get.
/// </summary>
internal sealed record ArchidektSnapshotGetResult(
    [property: JsonPropertyName("snapshot")] ArchidektSnapshotView Snapshot) : ArchidektSnapshotsResult;

/// <summary>
/// Returns one named snapshot with its metadata and one size-limited page of its saved deck, in the same shape as
/// <c>archidekt_deck_get</c>. When the deck's <c>nextOffset</c> is set, call again with that <c>offset</c>.
/// </summary>
internal sealed record ArchidektSnapshotView(
    RemoteNamedSnapshotSummary Summary,
    ArchidektRetrievalEvidence Evidence,
    ArchidektDeckView Deck);

/// <summary>
/// Returns one folder result whose <c>kind</c> matches the requested <c>kind</c>: list or get.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ArchidektFolderListResult), "list")]
[JsonDerivedType(typeof(ArchidektFolderGetResult), "get")]
internal abstract record ArchidektFoldersResult;

/// <summary>
/// Holds the whole folder tree for <c>kind</c> list.
/// </summary>
internal sealed record ArchidektFolderListResult(
    [property: JsonPropertyName("folders")] ArchidektFolderTreeView Folders) : ArchidektFoldersResult;

/// <summary>
/// Holds one folder and its direct contents for <c>kind</c> get.
/// </summary>
internal sealed record ArchidektFolderGetResult(
    [property: JsonPropertyName("folder")] ArchidektFolderTreeView Folder) : ArchidektFoldersResult;

/// <summary>
/// Returns a folder tree with its folder count and one size-limited page of its folder records. The tree fingerprint
/// covers the whole tree, whichever page was read. When <see cref="NextOffset"/> is set, call again with that
/// <c>offset</c>.
/// </summary>
internal sealed record ArchidektFolderTreeView(
    int FolderCount,
    int Offset,
    IReadOnlyList<RemoteFolderRecord> Items,
    ArchidektRetrievalEvidence Evidence,
    string TreeFingerprint)
{
    /// <summary>
    /// Gets true when folder records were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to pass to read the folder records that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>offset</c> that returns the next page of folder records, or null on the last page.
    /// </summary>
    public int? NextOffset { get; init; }
}

/// <summary>
/// Returns the three-way differences of one local deck with a leading count and one size-limited page of the rows.
/// When <see cref="NextDifferenceOffset"/> is set, call again with that <c>differenceOffset</c>.
/// </summary>
internal sealed record ArchidektSyncDiffView(
    Guid LocalDeckId,
    long LocalRevision,
    string RemoteDeckId,
    string RemoteFingerprint,
    string? BaselineRemoteFingerprint,
    bool HasConflicts,
    int DifferenceCount,
    int DifferenceOffset,
    IReadOnlyList<ArchidektDifference> Differences)
{
    /// <summary>
    /// Gets true when rows were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to pass to read the rows that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>differenceOffset</c> that returns the next page of differences, or null on the last page.
    /// </summary>
    public int? NextDifferenceOffset { get; init; }
}

/// <summary>
/// Returns a snapshot restore preview with leading counts and size-limited pages of its operations and differences.
/// Every guard describes the whole preview, whichever page was read, so the apply tool needs no page.
/// </summary>
internal sealed record ArchidektSnapshotRestorePreviewView(
    string DeckId,
    string SnapshotId,
    string SnapshotChecksum,
    string SnapshotContentFingerprint,
    string RemoteFingerprint,
    string PreviewFingerprint,
    int PredictedProviderRequests,
    int OperationCount,
    int DifferenceCount,
    int OperationOffset,
    int DifferenceOffset,
    IReadOnlyList<ArchidektRemoteOperation> Operations,
    IReadOnlyList<ArchidektDifference> Differences)
{
    /// <summary>
    /// Gets true when rows were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to pass to read the rows that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>operationOffset</c> that returns the next page of operations, or null on the last page. When the operations ended but the differences did not, it is the number of operations.
    /// </summary>
    public int? NextOperationOffset { get; init; }

    /// <summary>
    /// Gets the <c>differenceOffset</c> that returns the next page of differences, or null on the last page. When the differences ended but the operations did not, it is the number of differences.
    /// </summary>
    public int? NextDifferenceOffset { get; init; }
}

/// <summary>
/// Counts what one Archidekt deck holds, so an agent can size it before reading its entries.
/// </summary>
internal sealed record ArchidektDeckSummary(
    int EntryCount,
    int CardCount,
    int CategoryCount,
    IReadOnlyDictionary<string, int> CardsByZone);

/// <summary>
/// Returns one deck with a leading summary and one size-limited page of its entries. The categories and the unknown
/// provider fields come with the first page (offset 0) only. When <see cref="NextOffset"/> is set, call again with
/// that offset for the following entries.
/// </summary>
internal sealed record ArchidektDeckView(
    ArchidektDeckSummary Summary,
    string RemoteId,
    string RemoteUri,
    string Name,
    string Description,
    string Format,
    string Visibility,
    string? ParentFolderId,
    string ContentFingerprint,
    string RemoteFingerprint,
    ArchidektRetrievalEvidence Evidence,
    int Offset,
    IReadOnlyList<RemoteDeckCategory> Categories,
    IReadOnlyList<RemoteDeckEntry> Entries,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Extensions)
{
    /// <summary>
    /// Gets true when entries were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to pass to read the entries that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>offset</c> that returns the next page of entries, or null on the last page.
    /// </summary>
    public int? NextOffset { get; init; }

    /// <summary>
    /// Gets true when the deck's unknown provider fields were too large to include.
    /// </summary>
    public bool? ExtensionsOmitted { get; init; }
}

/// <summary>
/// Counts the contents of one synchronization preview so an agent can judge it before reading any row.
/// </summary>
internal sealed record ArchidektPreviewSummary(
    int OperationCount,
    IReadOnlyDictionary<string, int> OperationsByKind,
    int DifferenceCount,
    IReadOnlyDictionary<string, int> DifferencesByState,
    int ConflictCount);

/// <summary>
/// Returns one synchronization preview with a leading summary and size-limited pages of its operations and
/// differences. Every guard and the preview fingerprint describe the whole preview, whichever page was read, so the
/// apply tool needs no page. When <see cref="NextOperationOffset"/> or <see cref="NextDifferenceOffset"/> is set,
/// call again with those offsets for the rest.
/// </summary>
internal sealed record ArchidektSyncPreviewView(
    string Direction,
    ArchidektPreviewSummary Summary,
    bool ApplyAvailable,
    string? ApplyNotice,
    Guid? LocalDeckId,
    long? LocalRevision,
    string RemoteDeckId,
    string RemoteFingerprint,
    string ContentFingerprint,
    string PreviewFingerprint,
    bool HasConflicts,
    int PredictedProviderRequests,
    int OperationOffset,
    int DifferenceOffset,
    IReadOnlyList<ArchidektRemoteOperation> Operations,
    IReadOnlyList<ArchidektDifference> Differences)
{
    /// <summary>
    /// Gets true when rows were left out to keep the result under the size limit.
    /// </summary>
    public bool? Truncated { get; init; }

    /// <summary>
    /// Gets what to pass to read the rows that were left out.
    /// </summary>
    public string? Narrow { get; init; }

    /// <summary>
    /// Gets the <c>operationOffset</c> that returns the next page of operations, or null on the last page. When the operations ended but the differences did not, it is the number of operations.
    /// </summary>
    public int? NextOperationOffset { get; init; }

    /// <summary>
    /// Gets the <c>differenceOffset</c> that returns the next page of differences, or null on the last page. When the differences ended but the operations did not, it is the number of differences.
    /// </summary>
    public int? NextDifferenceOffset { get; init; }
}
