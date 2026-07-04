namespace MtgMcp.Core.Decks;

/// <summary>
/// Describes a deck without loading its detailed entries and relationships.
/// </summary>
public sealed record DeckSummary(
    Guid DeckId,
    string Name,
    string Description,
    string Format,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Carries a stable page of canonically ordered deck summaries.
/// </summary>
public sealed record DeckPage(
    IReadOnlyList<DeckSummary> Items,
    string? NextCursor)
{
    /// <summary>
    /// Gets the immutable snapshot of summaries in this page.
    /// </summary>
    public IReadOnlyList<DeckSummary> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Stores one independently addressable card row in a deck zone.
/// </summary>
public sealed record DeckEntry(
    Guid EntryId,
    int Quantity,
    string CardName,
    Guid? OracleId,
    Guid? PrintingId,
    string? SetCode,
    string? CollectorNumber,
    string Language,
    string Finish,
    string Zone,
    int SortOrder);

/// <summary>
/// Describes caller-supplied values for a new deck entry.
/// </summary>
public sealed record DeckEntryDraft(
    int Quantity,
    string CardName,
    Guid? OracleId = null,
    Guid? PrintingId = null,
    string? SetCode = null,
    string? CollectorNumber = null,
    string Language = "en",
    string Finish = "nonfoil",
    string Zone = "main",
    int SortOrder = 0,
    Guid? EntryId = null);

/// <summary>
/// Defines one functional category whose assignments never affect deck zones.
/// </summary>
public sealed record DeckCategory(
    Guid CategoryId,
    string Name,
    string? Color,
    int SortOrder);

/// <summary>
/// Describes caller-supplied values for a new deck category.
/// </summary>
public sealed record DeckCategoryDraft(
    string Name,
    string? Color = null,
    int SortOrder = 0,
    Guid? CategoryId = null);

/// <summary>
/// Links an entry to a functional category with an optional primary designation.
/// </summary>
public sealed record DeckCategoryAssignment(
    Guid EntryId,
    Guid CategoryId,
    bool IsPrimary);

/// <summary>
/// Stores provider-neutral synchronization identity without provider transport payloads.
/// </summary>
public sealed record DeckProviderBinding(
    Guid BindingId,
    string Provider,
    string RemoteId,
    string? RemoteUri,
    string? RemoteVersion,
    string? BaselineFingerprint,
    DateTimeOffset? LastPulledAtUtc,
    DateTimeOffset? LastPushedAtUtc);

/// <summary>
/// Carries the complete canonical local representation of one revisioned deck.
/// </summary>
public sealed record DeckDocument(
    Guid DeckId,
    string Name,
    string Description,
    string Format,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DeckEntry> Entries,
    IReadOnlyList<DeckCategory> Categories,
    IReadOnlyList<DeckCategoryAssignment> CategoryAssignments,
    IReadOnlyList<DeckProviderBinding> ProviderBindings)
{
    /// <summary>
    /// Gets the immutable snapshot of independently addressable entries.
    /// </summary>
    public IReadOnlyList<DeckEntry> Entries { get; init; } =
        Array.AsReadOnly(Entries.ToArray());

    /// <summary>
    /// Gets the immutable snapshot of functional categories.
    /// </summary>
    public IReadOnlyList<DeckCategory> Categories { get; init; } =
        Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets the immutable snapshot of entry-category relationships.
    /// </summary>
    public IReadOnlyList<DeckCategoryAssignment> CategoryAssignments { get; init; } =
        Array.AsReadOnly(CategoryAssignments.ToArray());

    /// <summary>
    /// Gets the immutable snapshot of provider-neutral synchronization bindings.
    /// </summary>
    public IReadOnlyList<DeckProviderBinding> ProviderBindings { get; init; } =
        Array.AsReadOnly(ProviderBindings.ToArray());
}

/// <summary>
/// Describes caller-supplied content for a new local deck.
/// </summary>
public sealed record DeckCreateRequest(
    string Name,
    string? Description = null,
    string Format = "commander",
    IReadOnlyList<DeckEntryDraft>? Entries = null,
    IReadOnlyList<DeckCategoryDraft>? Categories = null,
    IReadOnlyList<DeckCategoryAssignment>? CategoryAssignments = null,
    IReadOnlyList<DeckProviderBinding>? ProviderBindings = null,
    Guid? DeckId = null)
{
    /// <summary>
    /// Gets a stable snapshot of requested entries when supplied.
    /// </summary>
    public IReadOnlyList<DeckEntryDraft>? Entries { get; init; } =
        Entries is null ? null : Array.AsReadOnly(Entries.ToArray());

    /// <summary>
    /// Gets a stable snapshot of requested categories when supplied.
    /// </summary>
    public IReadOnlyList<DeckCategoryDraft>? Categories { get; init; } =
        Categories is null ? null : Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets a stable snapshot of requested category assignments when supplied.
    /// </summary>
    public IReadOnlyList<DeckCategoryAssignment>? CategoryAssignments { get; init; } =
        CategoryAssignments is null ? null : Array.AsReadOnly(CategoryAssignments.ToArray());

    /// <summary>
    /// Gets a stable snapshot of requested provider bindings when supplied.
    /// </summary>
    public IReadOnlyList<DeckProviderBinding>? ProviderBindings { get; init; } =
        ProviderBindings is null ? null : Array.AsReadOnly(ProviderBindings.ToArray());
}

/// <summary>
/// Reports one local structural defect without making a format-legality judgment.
/// </summary>
public sealed record DeckValidationIssue(
    string ReasonCode,
    string Message,
    Guid? EntryId = null,
    Guid? CategoryId = null);

/// <summary>
/// Reports deterministic local structure checks over one stored deck.
/// </summary>
public sealed record DeckValidationReport(
    Guid DeckId,
    long Revision,
    bool IsStructurallyValid,
    IReadOnlyList<DeckValidationIssue> Issues)
{
    /// <summary>
    /// Gets the immutable snapshot of deterministic structural findings.
    /// </summary>
    public IReadOnlyList<DeckValidationIssue> Issues { get; init; } =
        Array.AsReadOnly(Issues.ToArray());
}

/// <summary>
/// Describes one opaque backup without revealing its local filesystem location.
/// </summary>
public sealed record DeckBackup(
    Guid BackupId,
    int SchemaVersion,
    string Fingerprint,
    DateTimeOffset CreatedAtUtc,
    int DeckCount,
    bool IsRollback);

/// <summary>
/// Carries backup inventory together with the guarded current database fingerprint.
/// </summary>
public sealed record DeckBackupPage(
    string? CurrentDatabaseFingerprint,
    IReadOnlyList<DeckBackup> Items)
{
    /// <summary>
    /// Gets the immutable snapshot of opaque backup metadata.
    /// </summary>
    public IReadOnlyList<DeckBackup> Items { get; init; } =
        Array.AsReadOnly(Items.ToArray());
}

/// <summary>
/// Reports a completed restore and the retained rollback backup.
/// </summary>
public sealed record DeckRestoreResult(
    Guid RestoredBackupId,
    Guid RollbackBackupId,
    string CurrentDatabaseFingerprint);

/// <summary>
/// Confirms deletion of a revision-guarded local entity or opaque backup.
/// </summary>
public sealed record DeckDeleteResult(
    Guid DeletedId,
    long? FinalRevision = null);
