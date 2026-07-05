using System.Text.Json;

namespace MtgMcp.Archidekt;

/// <summary>
/// Reports credential readiness without revealing an account identity or secret location.
/// </summary>
public sealed record ArchidektAuthStatus(
    string State,
    bool CredentialsConfigured,
    bool SessionAuthenticated,
    string Message);

/// <summary>
/// Identifies the observed provider contract and exact retrieval used for evidence.
/// </summary>
public sealed record ArchidektRetrievalEvidence(
    string Source,
    string Method,
    string ContractVersion,
    DateTimeOffset RetrievedAtUtc,
    string SourceChecksum);

/// <summary>
/// Describes one Archidekt deck without loading its complete content.
/// </summary>
public sealed record RemoteDeckSummary(
    string RemoteId,
    string Name,
    string? Description,
    string Format,
    string Visibility,
    string? ParentFolderId,
    string? ParentFolderName,
    string? ParentFolderPath,
    int? CardCount,
    DateTimeOffset? UpdatedAtUtc,
    string Fingerprint);

/// <summary>
/// Preserves one provider category and the flags that influence board membership.
/// </summary>
public sealed record RemoteDeckCategory(
    string ProviderCategoryId,
    string Name,
    bool? IncludedInDeck,
    bool? IncludedInPrice,
    bool IsPremier,
    int SortOrder);

/// <summary>
/// Preserves one independently addressable card relation from an Archidekt deck.
/// </summary>
public sealed record RemoteDeckEntry(
    string ProviderRelationId,
    string ProviderCardId,
    int Quantity,
    string CardName,
    Guid? OracleId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string Language,
    string Finish,
    string Zone,
    IReadOnlyList<string> CategoryNames,
    string? PrimaryCategoryName,
    int SortOrder)
{
    /// <summary>
    /// Gets the immutable category-name snapshot in provider order.
    /// </summary>
    public IReadOnlyList<string> CategoryNames { get; init; } =
        Array.AsReadOnly(CategoryNames.ToArray());
}

/// <summary>
/// Carries a complete canonical remote deck observation with preserved extension fields.
/// </summary>
public sealed record RemoteDeckSnapshot(
    string RemoteId,
    string RemoteUri,
    string Name,
    string Description,
    string Format,
    string Visibility,
    string? ParentFolderId,
    IReadOnlyList<RemoteDeckCategory> Categories,
    IReadOnlyList<RemoteDeckEntry> Entries,
    IReadOnlyDictionary<string, JsonElement> Extensions,
    ArchidektRetrievalEvidence Evidence,
    string ContentFingerprint,
    string RemoteFingerprint)
{
    /// <summary>
    /// Gets an immutable category snapshot in canonical order.
    /// </summary>
    public IReadOnlyList<RemoteDeckCategory> Categories { get; init; } =
        Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets an immutable entry snapshot in canonical order.
    /// </summary>
    public IReadOnlyList<RemoteDeckEntry> Entries { get; init; } =
        Array.AsReadOnly(Entries.ToArray());

    /// <summary>
    /// Gets copied provider fields not consumed by the normalized projection.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } =
        new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal);
}

/// <summary>
/// Carries a canonically ordered page of remote deck summaries.
/// </summary>
public sealed record RemoteDeckPage(
    IReadOnlyList<RemoteDeckSummary> Items,
    string? NextCursor,
    ArchidektRetrievalEvidence Evidence)
{
    /// <summary>
    /// Gets the immutable page items.
    /// </summary>
    public IReadOnlyList<RemoteDeckSummary> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Preserves one folder and its direct provider relationships.
/// </summary>
public sealed record RemoteFolderRecord(
    string FolderId,
    string Name,
    string Visibility,
    string? ParentFolderId,
    string Path,
    IReadOnlyList<string> ChildFolderIds,
    IReadOnlyList<RemoteDeckSummary> Decks,
    IReadOnlyDictionary<string, JsonElement> Extensions)
{
    /// <summary>
    /// Gets immutable direct child-folder identifiers.
    /// </summary>
    public IReadOnlyList<string> ChildFolderIds { get; init; } =
        Array.AsReadOnly(ChildFolderIds.ToArray());

    /// <summary>
    /// Gets immutable directly contained deck summaries.
    /// </summary>
    public IReadOnlyList<RemoteDeckSummary> Decks { get; init; } =
        Array.AsReadOnly(Decks.ToArray());

    /// <summary>
    /// Gets copied provider fields not consumed by the normalized folder projection.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } =
        new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal);
}

/// <summary>
/// Carries a complete canonical folder tree or one verified detail result.
/// </summary>
public sealed record RemoteFolderTree(
    IReadOnlyList<RemoteFolderRecord> Items,
    ArchidektRetrievalEvidence Evidence,
    string TreeFingerprint)
{
    /// <summary>
    /// Gets folders in canonical parent/path/id order.
    /// </summary>
    public IReadOnlyList<RemoteFolderRecord> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Describes one saved Archidekt deck snapshot without requiring its full deck state.
/// </summary>
public sealed record RemoteNamedSnapshotSummary(
    string SnapshotId,
    string DeckId,
    string Name,
    string? Description,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string Checksum,
    IReadOnlyDictionary<string, JsonElement> Extensions)
{
    /// <summary>
    /// Gets copied provider fields not consumed by the normalized summary.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } =
        new Dictionary<string, JsonElement>(Extensions, StringComparer.Ordinal);
}

/// <summary>
/// Carries a named snapshot collection and its evidence checksum.
/// </summary>
public sealed record RemoteNamedSnapshotPage(
    IReadOnlyList<RemoteNamedSnapshotSummary> Items,
    ArchidektRetrievalEvidence Evidence,
    string CollectionChecksum)
{
    /// <summary>
    /// Gets immutable snapshot summaries in canonical order.
    /// </summary>
    public IReadOnlyList<RemoteNamedSnapshotSummary> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Carries one named snapshot together with its complete saved deck state.
/// </summary>
public sealed record RemoteNamedSnapshot(
    RemoteNamedSnapshotSummary Summary,
    RemoteDeckSnapshot Deck,
    ArchidektRetrievalEvidence Evidence);

/// <summary>
/// Describes one exact local, baseline, and remote comparison path.
/// </summary>
public sealed record ArchidektDifference(
    string Path,
    string State,
    string? BaselineValue,
    string? LocalValue,
    string? RemoteValue);

/// <summary>
/// Carries the three-way state required for an explicit synchronization decision.
/// </summary>
public sealed record ArchidektSyncDiff(
    Guid LocalDeckId,
    long LocalRevision,
    string RemoteDeckId,
    string RemoteFingerprint,
    string? BaselineRemoteFingerprint,
    bool HasConflicts,
    IReadOnlyList<ArchidektDifference> Differences)
{
    /// <summary>
    /// Gets immutable path-addressed differences.
    /// </summary>
    public IReadOnlyList<ArchidektDifference> Differences { get; init; } =
        Array.AsReadOnly(Differences.ToArray());
}

/// <summary>
/// Carries immutable guards and exact operations for a later synchronization apply.
/// </summary>
public sealed record ArchidektSyncPreview(
    string Direction,
    Guid? LocalDeckId,
    long? LocalRevision,
    string RemoteDeckId,
    string RemoteFingerprint,
    string ContentFingerprint,
    string PreviewFingerprint,
    bool HasConflicts,
    IReadOnlyList<ArchidektDifference> Differences,
    IReadOnlyList<ArchidektRemoteOperation> Operations,
    int PredictedProviderRequests)
{
    /// <summary>
    /// Gets immutable path differences.
    /// </summary>
    public IReadOnlyList<ArchidektDifference> Differences { get; init; } =
        Array.AsReadOnly(Differences.ToArray());

    /// <summary>
    /// Gets immutable provider operations in execution order.
    /// </summary>
    public IReadOnlyList<ArchidektRemoteOperation> Operations { get; init; } =
        Array.AsReadOnly(Operations.ToArray());
}

/// <summary>
/// Describes one previewed primitive provider operation without exposing transport payloads.
/// </summary>
public sealed record ArchidektRemoteOperation(
    int Sequence,
    string Kind,
    string Subject,
    string Summary);

/// <summary>
/// Reports the final status of one attempted provider operation.
/// </summary>
public sealed record ArchidektOperationStatus(
    int Sequence,
    string Kind,
    string Subject,
    string Status,
    string Message);

/// <summary>
/// Reports a guarded apply, including partial or unknown remote state.
/// </summary>
public sealed record ArchidektApplyResult(
    string Outcome,
    Guid? LocalDeckId,
    long? LocalRevision,
    string RemoteDeckId,
    string? FinalRemoteFingerprint,
    IReadOnlyList<ArchidektOperationStatus> Operations)
{
    /// <summary>
    /// Gets immutable operation statuses in attempted order.
    /// </summary>
    public IReadOnlyList<ArchidektOperationStatus> Operations { get; init; } =
        Array.AsReadOnly(Operations.ToArray());
}

/// <summary>
/// Reports one typed folder move result after verification.
/// </summary>
public sealed record ArchidektFolderMoveStatus(
    string Kind,
    string Id,
    string? PreviousParentFolderId,
    string? FinalParentFolderId,
    string Status);

/// <summary>
/// Carries verified folder move outcomes and the resulting tree fingerprint.
/// </summary>
public sealed record ArchidektFolderMoveResult(
    IReadOnlyList<ArchidektFolderMoveStatus> Items,
    string FinalTreeFingerprint)
{
    /// <summary>
    /// Gets immutable per-item verification states.
    /// </summary>
    public IReadOnlyList<ArchidektFolderMoveStatus> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Carries a guarded snapshot restore preview.
/// </summary>
public sealed record ArchidektSnapshotRestorePreview(
    string DeckId,
    string SnapshotId,
    string SnapshotChecksum,
    string SnapshotContentFingerprint,
    string RemoteFingerprint,
    string PreviewFingerprint,
    IReadOnlyList<ArchidektDifference> Differences,
    IReadOnlyList<ArchidektRemoteOperation> Operations,
    int PredictedProviderRequests)
{
    /// <summary>
    /// Gets immutable snapshot-to-current differences.
    /// </summary>
    public IReadOnlyList<ArchidektDifference> Differences { get; init; } =
        Array.AsReadOnly(Differences.ToArray());

    /// <summary>
    /// Gets immutable restore operations.
    /// </summary>
    public IReadOnlyList<ArchidektRemoteOperation> Operations { get; init; } =
        Array.AsReadOnly(Operations.ToArray());
}
