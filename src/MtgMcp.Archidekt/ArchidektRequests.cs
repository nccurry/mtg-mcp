using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MtgMcp.Archidekt;

/// <summary>
/// Describes an explicit private-by-default Archidekt deck shell creation.
/// </summary>
public sealed record ArchidektDeckCreateRequest(
    [property: Description("The deck name.")] string Name,
    [property: Description("The deck format."),
     AllowedValues(
        "standard", "modern", "commander", "edh", "legacy", "vintage", "pauper", "pioneer", "brawl", "historic",
        "oathbreaker")]
    string Format,
    [property: Description("The deck description; omit for none.")] string? Description = null,
    [property: Description("Who can see the deck; default private."), AllowedValues("private", "unlisted", "public")]
    string Visibility = "private",
    [property: Description("The Archidekt folder ID to create the deck in; omit for the root.")]
    string? ParentFolderId = null);

/// <summary>
/// Carries every immutable guard required to delete one remote deck.
/// </summary>
public sealed record ArchidektDeckDeleteRequest(
    [property: Description("The Archidekt deck ID.")] string DeckId,
    [property: Description("The remoteFingerprint from a fresh archidekt_deck_get; a changed deck is refused.")]
    string ExpectedRemoteFingerprint,
    [property: Description("The exact text \"delete {deckId}\" with the deck ID filled in.")] string Confirmation);

/// <summary>
/// Carries every immutable guard required to apply a pull preview.
/// DiscardLocalChanges must equal the value the preview was made with; it takes the Archidekt state over local edits.
/// </summary>
public sealed record ArchidektPullApplyRequest(
    [property: Description("The Archidekt deck ID, as in the preview.")] string RemoteDeckId,
    [property: Description("The local deck ID, as in the preview; null when the preview creates a local deck.")]
    Guid? LocalDeckId,
    [property: Description("The local revision from the preview; null when the preview creates a local deck.")]
    long? ExpectedLocalRevision,
    [property: Description("The remoteFingerprint from the preview.")] string ExpectedRemoteFingerprint,
    [property: Description("The previewFingerprint from the preview.")] string PreviewFingerprint,
    [property: Description("Must equal the value the preview was made with; true takes Archidekt over local edits.")]
    bool DiscardLocalChanges = false);

/// <summary>
/// Carries every immutable guard required to apply a push preview.
/// ConfirmRemovals must equal the number of Archidekt entries the push removes when that is more than 10 entries, or
/// 3 or more entries that are more than 10 percent of the Archidekt entries; it is ignored otherwise.
/// </summary>
public sealed record ArchidektPushApplyRequest(
    [property: Description("The local deck ID, as in the preview.")] Guid LocalDeckId,
    [property: Description("The local revision from the preview.")] long ExpectedLocalRevision,
    [property: Description("The remoteFingerprint from the preview.")] string ExpectedRemoteFingerprint,
    [property: Description("The previewFingerprint from the preview.")] string PreviewFingerprint,
    [property: Description("The number of Archidekt entries the push removes, when the preview asks for confirmation.")]
    int? ConfirmRemovals = null);

/// <summary>
/// Describes one exact folder creation without name-based inference.
/// </summary>
public sealed record ArchidektFolderCreateRequest(
    [property: Description("The folder name.")] string Name,
    [property: Description("Who can see the folder."), FolderVisibilityValues] string Visibility,
    [property: Description("The parent folder ID; omit for the root.")] string? ParentFolderId = null);

/// <summary>
/// Describes an allowlisted folder metadata update guarded by a fresh tree.
/// </summary>
public sealed record ArchidektFolderUpdateRequest(
    [property: Description("The folder to update.")] string FolderId,
    [property: Description("The treeFingerprint from a fresh archidekt_folders_get.")] string ExpectedTreeFingerprint,
    [property: Description("The new name; omit to keep it.")] string? Name = null,
    [property: Description("The new visibility; omit to keep it."), FolderVisibilityValues] string? Visibility = null,
    [property: Description("The new parent folder ID; null moves the folder to the root. Used only when updateParent is true.")]
    string? ParentFolderId = null,
    [property: Description("Set true to move the folder to parentFolderId; default false leaves the parent as it is.")]
    bool UpdateParent = false);

/// <summary>
/// Identifies one exact deck or folder and its expected current parent.
/// </summary>
public sealed record ArchidektFolderMoveItem(
    [property: Description("Whether the item is a deck or a folder."), AllowedValues("deck", "folder")] string Kind,
    [property: Description("The deck or folder ID.")] string Id,
    [property: Description("The item's current parent folder ID; null for the root.")]
    string? ExpectedParentFolderId);

/// <summary>
/// Describes a guarded typed folder move without recursive or inferred behavior.
/// </summary>
public sealed record ArchidektFolderMoveRequest(
    [property: Description("The treeFingerprint from a fresh archidekt_folders_get.")] string ExpectedTreeFingerprint,
    IReadOnlyList<ArchidektFolderMoveItem> Items,
    [property: Description("The folder to move them into; null moves them to the root.")]
    string? DestinationFolderId)
{
    /// <summary>
    /// Gets an immutable copy of move items.
    /// </summary>
    [Description("The decks and folders to move.")]
    public IReadOnlyList<ArchidektFolderMoveItem> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Carries exact identity and confirmation for empty-folder deletion.
/// </summary>
public sealed record ArchidektFolderDeleteRequest(
    [property: Description("The folder to delete; it must be empty.")] string FolderId,
    [property: Description("The folder's current name.")] string ExpectedName,
    [property: Description("The treeFingerprint from a fresh archidekt_folders_get.")] string ExpectedTreeFingerprint,
    [property: Description("The exact text \"delete folder {folderId}\" with the folder ID filled in.")]
    string Confirmation);

/// <summary>
/// Describes a named snapshot creation against an unchanged remote deck.
/// </summary>
public sealed record ArchidektSnapshotCreateRequest(
    [property: Description("The Archidekt deck ID.")] string DeckId,
    [property: Description("The remoteFingerprint from a fresh archidekt_deck_get; a changed deck is refused.")]
    string ExpectedRemoteFingerprint,
    [property: Description("The snapshot name.")] string Name,
    [property: Description("The snapshot description; omit for none.")] string? Description = null);

/// <summary>
/// Describes an allowlisted named-snapshot metadata update.
/// </summary>
public sealed record ArchidektSnapshotUpdateRequest(
    [property: Description("The Archidekt deck ID.")] string DeckId,
    [property: Description("The snapshot ID.")] string SnapshotId,
    [property: Description("The snapshot's checksum from archidekt_snapshots_get.")] string ExpectedChecksum,
    [property: Description("The new snapshot name.")] string Name);

/// <summary>
/// Carries exact identity and confirmation for snapshot deletion.
/// </summary>
public sealed record ArchidektSnapshotDeleteRequest(
    [property: Description("The Archidekt deck ID.")] string DeckId,
    [property: Description("The snapshot ID.")] string SnapshotId,
    [property: Description("The snapshot's checksum from archidekt_snapshots_get.")] string ExpectedChecksum,
    [property: Description("The exact text \"delete snapshot {snapshotId}\" with the snapshot ID filled in.")]
    string Confirmation);

/// <summary>
/// Carries all immutable source, target, and preview guards for snapshot restoration.
/// </summary>
public sealed record ArchidektSnapshotRestoreApplyRequest(
    [property: Description("The Archidekt deck ID.")] string DeckId,
    [property: Description("The snapshot ID.")] string SnapshotId,
    [property: Description("The snapshot checksum from the restore preview.")] string ExpectedSnapshotChecksum,
    [property: Description("The snapshot content fingerprint from the restore preview.")]
    string ExpectedSnapshotContentFingerprint,
    [property: Description("The remoteFingerprint from the restore preview.")] string ExpectedRemoteFingerprint,
    [property: Description("The previewFingerprint from the restore preview.")] string PreviewFingerprint,
    [property: Description("The exact text \"restore snapshot {snapshotId}\" with the snapshot ID filled in.")]
    string Confirmation);
