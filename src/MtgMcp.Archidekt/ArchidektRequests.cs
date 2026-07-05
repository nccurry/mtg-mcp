namespace MtgMcp.Archidekt;

/// <summary>
/// Describes an explicit private-by-default Archidekt deck shell creation.
/// </summary>
public sealed record ArchidektDeckCreateRequest(
    string Name,
    string Format,
    string? Description = null,
    string Visibility = "private",
    string? ParentFolderId = null);

/// <summary>
/// Carries every immutable guard required to delete one remote deck.
/// </summary>
public sealed record ArchidektDeckDeleteRequest(
    string DeckId,
    string ExpectedRemoteFingerprint,
    string Confirmation);

/// <summary>
/// Carries every immutable guard required to apply a pull preview.
/// </summary>
public sealed record ArchidektPullApplyRequest(
    string RemoteDeckId,
    Guid? LocalDeckId,
    long? ExpectedLocalRevision,
    string ExpectedRemoteFingerprint,
    string PreviewFingerprint);

/// <summary>
/// Carries every immutable guard required to apply a push preview.
/// </summary>
public sealed record ArchidektPushApplyRequest(
    Guid LocalDeckId,
    long ExpectedLocalRevision,
    string ExpectedRemoteFingerprint,
    string PreviewFingerprint);

/// <summary>
/// Describes one exact folder creation without name-based inference.
/// </summary>
public sealed record ArchidektFolderCreateRequest(
    string Name,
    string Visibility,
    string? ParentFolderId = null);

/// <summary>
/// Describes an allowlisted folder metadata update guarded by a fresh tree.
/// </summary>
public sealed record ArchidektFolderUpdateRequest(
    string FolderId,
    string ExpectedTreeFingerprint,
    string? Name = null,
    string? Visibility = null,
    string? ParentFolderId = null,
    bool UpdateParent = false);

/// <summary>
/// Identifies one exact deck or folder and its expected current parent.
/// </summary>
public sealed record ArchidektFolderMoveItem(
    string Kind,
    string Id,
    string? ExpectedParentFolderId);

/// <summary>
/// Describes a guarded typed folder move without recursive or inferred behavior.
/// </summary>
public sealed record ArchidektFolderMoveRequest(
    string ExpectedTreeFingerprint,
    IReadOnlyList<ArchidektFolderMoveItem> Items,
    string? DestinationFolderId)
{
    /// <summary>
    /// Gets an immutable copy of move items.
    /// </summary>
    public IReadOnlyList<ArchidektFolderMoveItem> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Carries exact identity and confirmation for empty-folder deletion.
/// </summary>
public sealed record ArchidektFolderDeleteRequest(
    string FolderId,
    string ExpectedName,
    string ExpectedTreeFingerprint,
    string Confirmation);

/// <summary>
/// Describes a named snapshot creation against an unchanged remote deck.
/// </summary>
public sealed record ArchidektSnapshotCreateRequest(
    string DeckId,
    string ExpectedRemoteFingerprint,
    string Name,
    string? Description = null);

/// <summary>
/// Describes an allowlisted named-snapshot metadata update.
/// </summary>
public sealed record ArchidektSnapshotUpdateRequest(
    string DeckId,
    string SnapshotId,
    string ExpectedChecksum,
    string Name);

/// <summary>
/// Carries exact identity and confirmation for snapshot deletion.
/// </summary>
public sealed record ArchidektSnapshotDeleteRequest(
    string DeckId,
    string SnapshotId,
    string ExpectedChecksum,
    string Confirmation);

/// <summary>
/// Carries all immutable source, target, and preview guards for snapshot restoration.
/// </summary>
public sealed record ArchidektSnapshotRestoreApplyRequest(
    string DeckId,
    string SnapshotId,
    string ExpectedSnapshotChecksum,
    string ExpectedSnapshotContentFingerprint,
    string ExpectedRemoteFingerprint,
    string PreviewFingerprint,
    string Confirmation);
