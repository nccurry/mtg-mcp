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
    [property: JsonPropertyName("snapshot")] RemoteNamedSnapshot Snapshot) : ArchidektSnapshotsResult;

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
    [property: JsonPropertyName("folders")] RemoteFolderTree Folders) : ArchidektFoldersResult;

/// <summary>
/// Holds one folder and its direct contents for <c>kind</c> get.
/// </summary>
internal sealed record ArchidektFolderGetResult(
    [property: JsonPropertyName("folder")] RemoteFolderTree Folder) : ArchidektFoldersResult;
