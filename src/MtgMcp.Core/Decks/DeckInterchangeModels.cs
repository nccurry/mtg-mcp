namespace MtgMcp.Core.Decks;

/// <summary>
/// Describes one deterministic manual interchange format and its verification boundary.
/// </summary>
public sealed record DeckInterchangeFormat(
    string FormatId,
    string DisplayName,
    bool SupportsImport,
    bool SupportsExport,
    bool IsLossless,
    string Status,
    string Instructions,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Gets an immutable snapshot of format-specific cautions.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.AsReadOnly(Warnings.ToArray());
}

/// <summary>
/// Captures caller-controlled defaults used while interpreting manual deck text.
/// </summary>
public sealed record DeckImportOptions(
    string? DeckName = null,
    string Description = "",
    string Format = "commander",
    string DefaultZone = "main",
    bool AllowPartial = false,
    bool AllowExperimental = false);

/// <summary>
/// Controls explicit experimental behavior during manual artifact generation.
/// </summary>
public sealed record DeckExportOptions(
    bool AllowExperimental = false,
    bool UseGlobalMoxfieldTags = false);

/// <summary>
/// Reports one bounded, one-based parser or preservation finding.
/// </summary>
public sealed record DeckInterchangeDiagnostic(
    string Severity,
    string ReasonCode,
    string Message,
    int? Line = null,
    string? Source = null);

/// <summary>
/// Preserves a provider synchronization snapshot that is intentionally absent from public deck reads.
/// </summary>
public sealed record DeckSyncBaseline(
    Guid BindingId,
    string CanonicalSnapshot);

/// <summary>
/// Carries a normalized, network-free import proposal before any local mutation.
/// </summary>
public sealed record DeckImportProposal(
    Guid? SourceDeckId,
    long? SourceRevision,
    DateTimeOffset? SourceCreatedAtUtc,
    DateTimeOffset? SourceUpdatedAtUtc,
    string Name,
    string Description,
    string Format,
    IReadOnlyList<DeckEntry> Entries,
    IReadOnlyList<DeckCategory> Categories,
    IReadOnlyList<DeckCategoryAssignment> CategoryAssignments,
    IReadOnlyList<DeckProviderBinding> ProviderBindings,
    IReadOnlyList<DeckSyncBaseline> SyncBaselines)
{
    /// <summary>
    /// Gets an immutable snapshot of proposed entries.
    /// </summary>
    public IReadOnlyList<DeckEntry> Entries { get; init; } = Array.AsReadOnly(Entries.ToArray());

    /// <summary>
    /// Gets an immutable snapshot of proposed categories.
    /// </summary>
    public IReadOnlyList<DeckCategory> Categories { get; init; } = Array.AsReadOnly(Categories.ToArray());

    /// <summary>
    /// Gets an immutable snapshot of proposed category assignments.
    /// </summary>
    public IReadOnlyList<DeckCategoryAssignment> CategoryAssignments { get; init; } =
        Array.AsReadOnly(CategoryAssignments.ToArray());

    /// <summary>
    /// Gets an immutable snapshot of provider-neutral bindings.
    /// </summary>
    public IReadOnlyList<DeckProviderBinding> ProviderBindings { get; init; } =
        Array.AsReadOnly(ProviderBindings.ToArray());

    /// <summary>
    /// Gets an immutable snapshot of binding baselines.
    /// </summary>
    public IReadOnlyList<DeckSyncBaseline> SyncBaselines { get; init; } =
        Array.AsReadOnly(SyncBaselines.ToArray());
}

/// <summary>
/// Reports a deterministic import preview, including explicit incomplete and unresolved states.
/// </summary>
public sealed record DeckImportPreview(
    string FormatId,
    string Completeness,
    string? Fingerprint,
    DeckImportProposal? Proposal,
    IReadOnlyList<DeckInterchangeDiagnostic> Diagnostics,
    int OmittedDiagnosticCount,
    IReadOnlyList<string> UnresolvedIdentities)
{
    /// <summary>
    /// Gets an immutable snapshot of bounded diagnostics.
    /// </summary>
    public IReadOnlyList<DeckInterchangeDiagnostic> Diagnostics { get; init; } =
        Array.AsReadOnly(Diagnostics.ToArray());

    /// <summary>
    /// Gets an immutable snapshot of unresolved caller-visible card identities.
    /// </summary>
    public IReadOnlyList<string> UnresolvedIdentities { get; init; } =
        Array.AsReadOnly(UnresolvedIdentities.ToArray());
}

/// <summary>
/// Reports a committed local import together with the preview diagnostics the caller accepted.
/// </summary>
public sealed record DeckImportCreateResult(
    DeckDocument Deck,
    string Completeness,
    IReadOnlyList<DeckInterchangeDiagnostic> Diagnostics,
    int OmittedDiagnosticCount)
{
    /// <summary>
    /// Gets an immutable snapshot of accepted diagnostics.
    /// </summary>
    public IReadOnlyList<DeckInterchangeDiagnostic> Diagnostics { get; init; } =
        Array.AsReadOnly(Diagnostics.ToArray());
}

/// <summary>
/// Carries one named UTF-8 artifact with a stable integrity checksum.
/// </summary>
public sealed record DeckExportArtifact(
    string FileName,
    string MediaType,
    string Content,
    string Sha256,
    string Purpose);

/// <summary>
/// States how one local field is represented by a target manual format.
/// </summary>
public sealed record DeckFieldPreservation(
    string Field,
    string Status,
    string Detail);

/// <summary>
/// Carries a deterministic export bundle and an explicit field-preservation report.
/// </summary>
public sealed record DeckExportBundle(
    int SchemaVersion,
    string FormatId,
    Guid DeckId,
    long DeckRevision,
    DateTimeOffset GeneratedAtUtc,
    string Status,
    IReadOnlyList<DeckExportArtifact> Artifacts,
    IReadOnlyList<DeckFieldPreservation> Preservation)
{
    /// <summary>
    /// Gets an immutable snapshot of ordered artifacts.
    /// </summary>
    public IReadOnlyList<DeckExportArtifact> Artifacts { get; init; } =
        Array.AsReadOnly(Artifacts.ToArray());

    /// <summary>
    /// Gets an immutable snapshot of field-preservation evidence.
    /// </summary>
    public IReadOnlyList<DeckFieldPreservation> Preservation { get; init; } =
        Array.AsReadOnly(Preservation.ToArray());
}
