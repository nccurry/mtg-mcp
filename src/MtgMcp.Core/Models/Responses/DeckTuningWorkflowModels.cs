using System.Text.Json.Serialization;

namespace MtgMcp.Core;

/// <summary>
/// Controls how a workspace decklist is rendered for callers.
/// </summary>
public sealed class DeckExportOptions
{
    /// <summary>
    /// Gets or sets the output format: text, markdown, or markdown-links.
    /// </summary>
    public string Format { get; set; } = "text";

    /// <summary>
    /// Gets or sets whether excluded categories such as Sideboard and Maybeboard are omitted.
    /// </summary>
    public bool IncludedOnly { get; set; }

    /// <summary>
    /// Gets or sets whether category headings are included in the rendered decklist.
    /// </summary>
    public bool IncludeCategories { get; set; } = true;
}

/// <summary>
/// Explains which cards counted toward a role, tag, or category total.
/// </summary>
public sealed class DeckRoleCountExplanation
{
    /// <summary>
    /// Gets or sets the analyzed workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the role, tag, or category requested by the caller.
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// Gets or sets active card quantity whose primary category exactly matches the requested role.
    /// </summary>
    public int CategoryCount { get; set; }

    /// <summary>
    /// Gets or sets active card quantity with any primary or secondary category matching the requested role.
    /// </summary>
    public int AllCategoryCount { get; set; }

    /// <summary>
    /// Gets or sets active card quantity whose classifier primary role exactly matches the requested role.
    /// </summary>
    public int HeuristicCount { get; set; }

    /// <summary>
    /// Gets or sets active card quantity whose additive functional roles match the requested role.
    /// </summary>
    public int FunctionalCount { get; set; }

    /// <summary>
    /// Gets or sets active card quantity that the draw-odds target matcher would treat as a success.
    /// </summary>
    public int OddsTargetCount { get; set; }

    /// <summary>
    /// Gets or sets cards that matched at least one counting path, including excluded cards for correction.
    /// </summary>
    public List<DeckRoleCountCardEvidence> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets notes about divergent counts or data quality.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one card's contribution to a role-count explanation.
/// </summary>
public sealed class DeckRoleCountCardEvidence
{
    /// <summary>
    /// Gets or sets the workspace card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the card's primary category.
    /// </summary>
    public string PrimaryCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets all category labels on the card.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the primary category contributes to the active deck.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets the classifier's primary role for this card.
    /// </summary>
    public string ClassifierPrimaryRole { get; set; } = "";

    /// <summary>
    /// Gets or sets classifier secondary tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets additive classifier roles for multi-function cards.
    /// </summary>
    public List<string> FunctionalRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets classifier confidence.
    /// </summary>
    public double ClassifierConfidence { get; set; }

    /// <summary>
    /// Gets or sets whether this card was counted by exact primary category.
    /// </summary>
    public bool CountedByCategory { get; set; }

    /// <summary>
    /// Gets or sets whether this card was counted by any matching primary or secondary category.
    /// </summary>
    public bool CountedByAnyCategory { get; set; }

    /// <summary>
    /// Gets or sets whether this card was counted by classifier primary role.
    /// </summary>
    public bool CountedByHeuristic { get; set; }

    /// <summary>
    /// Gets or sets whether this card was counted by additive functional role.
    /// </summary>
    public bool CountedByFunctionalRole { get; set; }

    /// <summary>
    /// Gets or sets whether this card was counted by the draw-odds role/tag/category matcher.
    /// </summary>
    public bool CountedByOddsTarget { get; set; }

    /// <summary>
    /// Gets or sets exact evidence strings inspected for the match.
    /// </summary>
    public List<string> MatchingEvidence { get; set; } = [];

    /// <summary>
    /// Gets or sets the card type line from the cached snapshot when known.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets a short oracle excerpt from the cached snapshot when known.
    /// </summary>
    public string? OracleSnippet { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page when known.
    /// </summary>
    public string? ScryfallUri { get; set; }
}

/// <summary>
/// Provides compact reusable context for a workspace.
/// </summary>
public sealed class DeckWorkspaceState
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the full workspace resource URI.
    /// </summary>
    public string WorkspaceResourceUri { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "";

    /// <summary>
    /// Gets or sets the persistence label for future mutations.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets active included card quantity.
    /// </summary>
    public int IncludedCount { get; set; }

    /// <summary>
    /// Gets or sets detected commander names.
    /// </summary>
    public List<string> Commanders { get; set; } = [];

    /// <summary>
    /// Gets or sets primary-category counts across all cards.
    /// </summary>
    public Dictionary<string, int> CategoryCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets classifier role counts for active included cards.
    /// </summary>
    public Dictionary<string, int> RoleCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets active noncreature spell quantity.
    /// </summary>
    public int ActiveNoncreatureSpells { get; set; }

    /// <summary>
    /// Gets or sets high mana-value included cards.
    /// </summary>
    public List<DeckWorkspaceStateCard> HighManaValueCards { get; set; } = [];

    /// <summary>
    /// Gets or sets cards whose primary category is Sideboard.
    /// </summary>
    public List<DeckWorkspaceStateCard> SideboardCards { get; set; } = [];

    /// <summary>
    /// Gets or sets cards whose primary category is Maybeboard.
    /// </summary>
    public List<DeckWorkspaceStateCard> MaybeboardCards { get; set; } = [];

    /// <summary>
    /// Gets or sets lightweight deck-rule validation.
    /// </summary>
    public DeckValidationResult Validation { get; set; } = new();

    /// <summary>
    /// Gets or sets the highest-signal warnings to show before deeper analysis.
    /// </summary>
    public List<string> TopWarnings { get; set; } = [];
}

/// <summary>
/// Provides compact card data for reusable workspace state.
/// </summary>
public sealed class DeckWorkspaceStateCard
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets card quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets primary category.
    /// </summary>
    public string PrimaryCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets cached mana value.
    /// </summary>
    public double? ManaValue { get; set; }

    /// <summary>
    /// Gets or sets cached type line.
    /// </summary>
    public string? TypeLine { get; set; }

    /// <summary>
    /// Gets or sets Scryfall card page when known.
    /// </summary>
    public string? ScryfallUri { get; set; }
}

/// <summary>
/// Summarizes deck context and parsed intent for assistant prompt workflows.
/// </summary>
public sealed class DeckAssistantContext
{
    /// <summary>
    /// Gets or sets compact workspace state.
    /// </summary>
    public DeckWorkspaceState State { get; set; } = new();

    /// <summary>
    /// Gets or sets parsed deck intent stored in the workspace description.
    /// </summary>
    public DeckIntentResult Intent { get; set; } = new();
}

/// <summary>
/// Reports a compact before/after mutation diff instead of a full workspace snapshot.
/// </summary>
public sealed class CompactMutationResult
{
    /// <summary>
    /// Gets or sets whether the mutation completed successfully.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the plan id when this compact result comes from plan application.
    /// </summary>
    public string? PlanId { get; set; }

    /// <summary>
    /// Gets or sets the full workspace resource URI.
    /// </summary>
    public string WorkspaceResourceUri { get; set; } = "";

    /// <summary>
    /// Gets or sets persistence used by the mutation.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets the mutation message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets the saved plan status when this compact result comes from plan application.
    /// </summary>
    public DeckEditPlanStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets a checkpoint id created before applying a plan, when one exists.
    /// </summary>
    public string? CheckpointId { get; set; }

    /// <summary>
    /// Gets or sets aggregate card-copy quantity increases represented by the mutation.
    /// </summary>
    public int Added { get; set; }

    /// <summary>
    /// Gets or sets aggregate card-copy quantity decreases represented by the mutation.
    /// </summary>
    public int Removed { get; set; }

    /// <summary>
    /// Gets or sets card identities whose primary category changed while still present.
    /// </summary>
    public int Moved { get; set; }

    /// <summary>
    /// Gets or sets changed card names.
    /// </summary>
    public List<string> ChangedCards { get; set; } = [];

    /// <summary>
    /// Gets or sets active included count before the mutation.
    /// </summary>
    public int IncludedCountBefore { get; set; }

    /// <summary>
    /// Gets or sets active included count after the mutation.
    /// </summary>
    public int IncludedCountAfter { get; set; }

    /// <summary>
    /// Gets or sets primary-category counts before the mutation.
    /// </summary>
    public Dictionary<string, int> CategoryCountsBefore { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets primary-category counts after the mutation.
    /// </summary>
    public Dictionary<string, int> CategoryCountsAfter { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets validation after the mutation.
    /// </summary>
    public DeckValidationResult Validation { get; set; } = new();

    /// <summary>
    /// Gets or sets compact notes, including apply failure details.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports the smallest safe response for repetitive workspace mutations.
/// </summary>
public sealed class CompactMutationSummaryResult
{
    /// <summary>
    /// Gets or sets whether the mutation completed successfully.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets changed card names.
    /// </summary>
    public List<string> ChangedCards { get; set; } = [];

    /// <summary>
    /// Gets or sets the mutation message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets bounded validation counts after the mutation.
    /// </summary>
    public CompactValidationSummary ValidationSummary { get; set; } = new();
}

/// <summary>
/// Summarizes validation without listing every warning or error.
/// </summary>
public sealed class CompactValidationSummary
{
    /// <summary>
    /// Gets or sets whether validation passed.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the number of validation errors.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Gets or sets the number of validation warnings.
    /// </summary>
    public int WarningCount { get; set; }
}

/// <summary>
/// Describes one exact move requested while creating a deck edit plan.
/// </summary>
public sealed class ExplicitDeckPlanMoveCardChange
{
    /// <summary>
    /// Gets or sets the card name to move.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the expected current primary or secondary category, when the caller wants one.
    /// </summary>
    public string? FromCategory { get; set; }

    /// <summary>
    /// Gets or sets the destination primary category.
    /// </summary>
    public string ToCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets the caller's reason for moving the card.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports a transient card-package preview without saving a deck edit plan.
/// </summary>
public sealed class DeckCardPackagePreviewResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the unsaved, non-applyable plan body used for the preview.
    /// </summary>
    public PreviewDeckEditPlan PreviewPlan { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this result is only an in-memory preview.
    /// </summary>
    public bool PreviewOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets whether this response includes a persisted plan id that can be applied.
    /// </summary>
    public bool CanApply { get; set; }

    /// <summary>
    /// Gets or sets the applyable plan id when one exists.
    /// </summary>
    public string? ApplyPlanId { get; set; }

    /// <summary>
    /// Gets or sets the recommended next action for callers that want to mutate a deck.
    /// </summary>
    public string NextAction { get; set; } = "";

    /// <summary>
    /// Gets or sets the same metric preview shape used by persisted plans.
    /// </summary>
    public DeckPlanPreviewResult Preview { get; set; } = new();

    /// <summary>
    /// Gets or sets role-count deltas after applying the package in memory.
    /// </summary>
    public List<DeckRoleCountDelta> RoleDeltas { get; set; } = [];

    /// <summary>
    /// Gets or sets validation changes after applying the package in memory.
    /// </summary>
    public DeckValidationDelta ValidationChanges { get; set; } = new();

    /// <summary>
    /// Gets or sets included-deck price delta.
    /// </summary>
    public DeckPriceDelta PriceDelta { get; set; } = new();

    /// <summary>
    /// Gets or sets Commander bracket impact.
    /// </summary>
    public DeckBracketImpact BracketImpact { get; set; } = new();

    /// <summary>
    /// Gets or sets the preview analysis mode that controlled expensive analysis work.
    /// </summary>
    public string AnalysisMode { get; set; } = PreviewAnalysisModes.Summary;

    /// <summary>
    /// Gets or sets whether the package was previewed against a below-size Commander deck.
    /// </summary>
    public bool PartialDeck { get; set; }

    /// <summary>
    /// Gets or sets the expected included card count when a Commander deck is partial.
    /// </summary>
    public int? ExpectedIncludedCards { get; set; }

    /// <summary>
    /// Gets or sets whether goldfish performance analysis was intentionally skipped.
    /// </summary>
    public bool PerformanceSkipped { get; set; }

    /// <summary>
    /// Gets or sets why goldfish performance analysis was skipped.
    /// </summary>
    public string? PerformanceSkipReason { get; set; }

    /// <summary>
    /// Gets or sets deterministic source-support status rows for package cards.
    /// </summary>
    public List<DeckPackageSourceSupport> SourceSupport { get; set; } = [];

    /// <summary>
    /// Gets or sets the source-support detail level used for package card rows.
    /// </summary>
    public string SourceSupportDepth { get; set; } = PreviewSourceSupportDepths.Minimal;

    /// <summary>
    /// Gets or sets deterministic performance comparison for the transient package.
    /// </summary>
    public DeckPerformanceComparison Performance { get; set; } = new();

    /// <summary>
    /// Gets or sets preview warnings and model notes.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Describes a transient deck edit plan without exposing a persisted apply id.
/// </summary>
public sealed class PreviewDeckEditPlan
{
    /// <summary>
    /// Gets or sets the workspace id used for the preview.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the preview plan name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the preview plan kind.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets the preview rationale.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets the preview confidence.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets preview warnings copied from the transient plan body.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets the exact operations evaluated in memory.
    /// </summary>
    public List<DeckEditOperation> Operations { get; set; } = [];
}

/// <summary>
/// Reports one role-count delta.
/// </summary>
public sealed class DeckRoleCountDelta
{
    /// <summary>
    /// Gets or sets the role name.
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// Gets or sets the before count.
    /// </summary>
    public int Before { get; set; }

    /// <summary>
    /// Gets or sets the after count.
    /// </summary>
    public int After { get; set; }

    /// <summary>
    /// Gets or sets after minus before.
    /// </summary>
    public int Delta { get; set; }
}

/// <summary>
/// Reports validation deltas for a transient package preview.
/// </summary>
public sealed class DeckValidationDelta
{
    /// <summary>
    /// Gets or sets validation errors added by the package.
    /// </summary>
    public List<string> AddedErrors { get; set; } = [];

    /// <summary>
    /// Gets or sets validation errors removed by the package.
    /// </summary>
    public List<string> RemovedErrors { get; set; } = [];

    /// <summary>
    /// Gets or sets validation warnings added by the package.
    /// </summary>
    public List<string> AddedWarnings { get; set; } = [];

    /// <summary>
    /// Gets or sets validation warnings removed by the package.
    /// </summary>
    public List<string> RemovedWarnings { get; set; } = [];
}

/// <summary>
/// Reports an included-price delta for a transient package preview.
/// </summary>
public sealed class DeckPriceDelta
{
    /// <summary>
    /// Gets or sets the before included total.
    /// </summary>
    public decimal BeforeIncludedTotal { get; set; }

    /// <summary>
    /// Gets or sets the after included total.
    /// </summary>
    public decimal AfterIncludedTotal { get; set; }

    /// <summary>
    /// Gets or sets after minus before.
    /// </summary>
    public decimal IncludedTotalDelta { get; set; }
}

/// <summary>
/// Reports Commander bracket impact for a transient package preview.
/// </summary>
public sealed class DeckBracketImpact
{
    /// <summary>
    /// Gets or sets whether live bracket impact analysis was intentionally skipped.
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Gets or sets why live bracket impact analysis was skipped.
    /// </summary>
    public string? SkipReason { get; set; }

    /// <summary>
    /// Gets or sets the before estimated bracket.
    /// </summary>
    public int BeforeEstimatedBracket { get; set; }

    /// <summary>
    /// Gets or sets the after estimated bracket.
    /// </summary>
    public int AfterEstimatedBracket { get; set; }

    /// <summary>
    /// Gets or sets after minus before.
    /// </summary>
    public int EstimatedBracketDelta { get; set; }

    /// <summary>
    /// Gets or sets the before Game Changer count.
    /// </summary>
    public int BeforeGameChangerCount { get; set; }

    /// <summary>
    /// Gets or sets the after Game Changer count.
    /// </summary>
    public int AfterGameChangerCount { get; set; }
}

/// <summary>
/// Reports source-support availability for one package card.
/// </summary>
public sealed class DeckPackageSourceSupport
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the package operation involving this card.
    /// </summary>
    public string Operation { get; set; } = "";

    /// <summary>
    /// Gets or sets source-support status.
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// Gets or sets the Scryfall card page when source-backed metadata resolved.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets the EDHREC rank when available from source-backed card metadata.
    /// </summary>
    public int? EdhrecRank { get; set; }

    /// <summary>
    /// Gets or sets the classifier role used in balanced source-support output.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets classifier tags used in balanced source-support output.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the USD price when available from source-backed card metadata.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets the price field used for the source-support price.
    /// </summary>
    public string? PriceSource { get; set; }

    /// <summary>
    /// Gets or sets source-support notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Lists package preview source-support depths accepted by MCP tools.
/// </summary>
public static class PreviewSourceSupportDepths
{
    /// <summary>
    /// Omits per-card source-support rows.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// Includes compact source-backed metadata status for package cards.
    /// </summary>
    public const string Minimal = "minimal";

    /// <summary>
    /// Includes source-backed metadata plus role, tags, and price when available.
    /// </summary>
    public const string Balanced = "balanced";
}

/// <summary>
/// Lists analysis modes accepted by transient package previews.
/// </summary>
public static class PreviewAnalysisModes
{
    /// <summary>
    /// Skips expensive open-world and simulation work, keeping local composition, price, and validation deltas.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// Uses bounded defaults and skips noisy performance analysis for large packages or partial decks.
    /// </summary>
    public const string Summary = "summary";

    /// <summary>
    /// Runs the full preview analysis path requested by the caller.
    /// </summary>
    public const string Full = "full";
}

/// <summary>
/// Reports deterministic differences between two explicitly selected saved workspaces.
/// </summary>
public sealed class WorkspaceDiffResult
{
    /// <summary>
    /// Gets or sets the current workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the explicit baseline workspace id used for comparison.
    /// </summary>
    public string PreviousWorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets baseline workspace identity and source metadata.
    /// </summary>
    public WorkspaceDiffBaseline Baseline { get; set; } = new();

    /// <summary>
    /// Gets or sets current workspace identity and source metadata.
    /// </summary>
    public WorkspaceDiffBaseline Current { get; set; } = new();

    /// <summary>
    /// Gets or sets cards present in the current workspace but absent from the baseline.
    /// </summary>
    public List<WorkspaceDiffCardChange> AddedCards { get; set; } = [];

    /// <summary>
    /// Gets or sets cards present in the baseline but absent from the current workspace.
    /// </summary>
    public List<WorkspaceDiffCardChange> RemovedCards { get; set; } = [];

    /// <summary>
    /// Gets or sets cards whose primary category changed between workspaces.
    /// </summary>
    public List<WorkspaceDiffCardChange> PrimaryMoves { get; set; } = [];

    /// <summary>
    /// Gets or sets cards whose non-primary categories changed between workspaces.
    /// </summary>
    public List<WorkspaceDiffCardChange> SecondaryTagChanges { get; set; } = [];

    /// <summary>
    /// Gets or sets cards whose aggregate quantity changed without being purely added or removed.
    /// </summary>
    public List<WorkspaceDiffCardChange> QuantityChanges { get; set; } = [];

    /// <summary>
    /// Gets or sets included count in the baseline workspace.
    /// </summary>
    public int IncludedCountBefore { get; set; }

    /// <summary>
    /// Gets or sets included count in the current workspace.
    /// </summary>
    public int IncludedCountAfter { get; set; }

    /// <summary>
    /// Gets or sets current included count minus baseline included count.
    /// </summary>
    public int IncludedCountDelta { get; set; }

    /// <summary>
    /// Gets or sets validation changes between the baseline and current workspace.
    /// </summary>
    public DeckValidationDelta ValidationDelta { get; set; } = new();

    /// <summary>
    /// Gets or sets explicit comparison notes and caveats.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Lists statuses returned by the last-import diff workflow.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceDiffLastImportStatus>))]
public enum WorkspaceDiffLastImportStatus
{
    /// <summary>
    /// A matching import-history baseline was found.
    /// </summary>
    [JsonStringEnumMemberName("baselineFound")]
    BaselineFound,

    /// <summary>
    /// The workspace has a supported source but no prior baseline snapshot.
    /// </summary>
    [JsonStringEnumMemberName("noPriorBaseline")]
    NoPriorBaseline,

    /// <summary>
    /// The workspace source provider is not supported by import history.
    /// </summary>
    [JsonStringEnumMemberName("sourceUnsupported")]
    SourceUnsupported,

    /// <summary>
    /// The workspace does not identify an imported provider deck source.
    /// </summary>
    [JsonStringEnumMemberName("workspaceHasNoSource")]
    WorkspaceHasNoSource,

    /// <summary>
    /// History metadata exists but the prior snapshot is unavailable.
    /// </summary>
    [JsonStringEnumMemberName("historyUnavailable")]
    HistoryUnavailable,
}

/// <summary>
/// Reports a workspace diff against the previous import into the same source-scoped workspace.
/// </summary>
public sealed class WorkspaceDiffLastImportResult
{
    /// <summary>
    /// Gets or sets the status describing whether a baseline was available.
    /// </summary>
    public WorkspaceDiffLastImportStatus Status { get; set; } = WorkspaceDiffLastImportStatus.WorkspaceHasNoSource;

    /// <summary>
    /// Gets or sets the current workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the source provider key when known.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the source provider deck id when known.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Gets or sets the local workspace id used for import-history scoping.
    /// </summary>
    public string? LocalWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the import timestamp for the baseline comparison.
    /// </summary>
    public DateTimeOffset? ImportedAt { get; set; }

    /// <summary>
    /// Gets or sets the diff when a prior baseline was found.
    /// </summary>
    public WorkspaceDiffResult? Diff { get; set; }

    /// <summary>
    /// Gets or sets explanatory notes for missing or unsupported history.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Lists statuses returned by in-place provider refresh.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceRefreshFromSourceStatus>))]
public enum WorkspaceRefreshFromSourceStatus
{
    /// <summary>
    /// The workspace was refreshed from its provider source.
    /// </summary>
    [JsonStringEnumMemberName("refreshed")]
    Refreshed,

    /// <summary>
    /// The workspace does not identify an imported provider deck source.
    /// </summary>
    [JsonStringEnumMemberName("workspaceHasNoSource")]
    WorkspaceHasNoSource,

    /// <summary>
    /// The workspace source provider is not refreshable.
    /// </summary>
    [JsonStringEnumMemberName("sourceUnsupported")]
    SourceUnsupported,

    /// <summary>
    /// The source provider could not return the deck for this refresh.
    /// </summary>
    [JsonStringEnumMemberName("sourceUnavailable")]
    SourceUnavailable,
}

/// <summary>
/// Reports the result of refreshing an existing workspace from its provider source.
/// </summary>
public sealed class WorkspaceRefreshFromSourceResult
{
    /// <summary>
    /// Gets or sets the refresh status.
    /// </summary>
    public WorkspaceRefreshFromSourceStatus Status { get; set; } = WorkspaceRefreshFromSourceStatus.WorkspaceHasNoSource;

    /// <summary>
    /// Gets or sets the refreshed workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the source provider key when known.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the source provider deck id when known.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Gets or sets the local workspace id used for refresh and import-history scoping.
    /// </summary>
    public string? LocalWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the refreshed workspace when refresh succeeds.
    /// </summary>
    public DeckWorkspace? Workspace { get; set; }

    /// <summary>
    /// Gets or sets the diff against the captured pre-refresh baseline.
    /// </summary>
    public WorkspaceDiffLastImportResult? DiffLastImport { get; set; }

    /// <summary>
    /// Gets or sets explanatory refresh notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Returns the saved baseline workspace for last-import analysis comparisons.
/// </summary>
public sealed class WorkspaceImportBaselineResolution
{
    /// <summary>
    /// Gets or sets the baseline resolution status.
    /// </summary>
    public WorkspaceDiffLastImportStatus Status { get; set; } = WorkspaceDiffLastImportStatus.WorkspaceHasNoSource;

    /// <summary>
    /// Gets or sets the current workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the source provider key when known.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the source provider deck id when known.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Gets or sets the local workspace id used for import-history scoping.
    /// </summary>
    public string? LocalWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets when the baseline was captured.
    /// </summary>
    public DateTimeOffset? ImportedAt { get; set; }

    /// <summary>
    /// Gets or sets the baseline workspace when one is available.
    /// </summary>
    public DeckWorkspace? BaselineWorkspace { get; set; }

    /// <summary>
    /// Gets or sets explanatory notes for unavailable baselines.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Identifies one side of a workspace diff comparison.
/// </summary>
public sealed class WorkspaceDiffBaseline
{
    /// <summary>
    /// Gets or sets workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets workspace name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets workspace mode.
    /// </summary>
    public string Mode { get; set; } = "";

    /// <summary>
    /// Gets or sets persistence label.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets a concise source label derived from mode and source references.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace update timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the full workspace resource URI.
    /// </summary>
    public string WorkspaceResourceUri { get; set; } = "";
}

/// <summary>
/// Describes one card-level difference between two workspace snapshots.
/// </summary>
public sealed class WorkspaceDiffCardChange
{
    /// <summary>
    /// Gets or sets stable card identity used for comparison.
    /// </summary>
    public string Identity { get; set; } = "";

    /// <summary>
    /// Gets or sets display card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets baseline quantity.
    /// </summary>
    public int QuantityBefore { get; set; }

    /// <summary>
    /// Gets or sets current quantity.
    /// </summary>
    public int QuantityAfter { get; set; }

    /// <summary>
    /// Gets or sets baseline primary category.
    /// </summary>
    public string? PrimaryCategoryBefore { get; set; }

    /// <summary>
    /// Gets or sets current primary category.
    /// </summary>
    public string? PrimaryCategoryAfter { get; set; }

    /// <summary>
    /// Gets or sets baseline ordered category labels.
    /// </summary>
    public List<string> CategoriesBefore { get; set; } = [];

    /// <summary>
    /// Gets or sets current ordered category labels.
    /// </summary>
    public List<string> CategoriesAfter { get; set; } = [];

    /// <summary>
    /// Gets or sets baseline secondary category labels.
    /// </summary>
    public List<string> SecondaryCategoriesBefore { get; set; } = [];

    /// <summary>
    /// Gets or sets current secondary category labels.
    /// </summary>
    public List<string> SecondaryCategoriesAfter { get; set; } = [];

    /// <summary>
    /// Gets or sets Scryfall page from either side when known.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets evidence notes for the change row.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports deterministic weak-slot evidence without choosing final cuts for the caller.
/// </summary>
public sealed class DeckWeakSpotReview
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets compact deck state used by the review.
    /// </summary>
    public DeckWorkspaceState State { get; set; } = new();

    /// <summary>
    /// Gets or sets role and tag balance rows from the selected heuristic profile.
    /// </summary>
    public List<DeckWeakSpotBalanceRow> RoleBalance { get; set; } = [];

    /// <summary>
    /// Gets or sets primary category balance rows.
    /// </summary>
    public List<DeckWeakSpotCategoryRow> CategoryBalance { get; set; } = [];

    /// <summary>
    /// Gets or sets evidence rows for active cards that may deserve review.
    /// </summary>
    public List<DeckWeakSlotEvidenceRow> WeakSlots { get; set; } = [];

    /// <summary>
    /// Gets or sets existing excluded cards that may address low role or tag counts.
    /// </summary>
    public List<DeckWeakSpotCandidateRow> CandidateRows { get; set; } = [];

    /// <summary>
    /// Gets or sets source availability rows.
    /// </summary>
    public List<DeckWeakSpotSourceStatus> SourceStatuses { get; set; } = [];

    /// <summary>
    /// Gets or sets evidence-only review notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes a role or tag balance finding.
/// </summary>
public sealed class DeckWeakSpotBalanceRow
{
    /// <summary>
    /// Gets or sets role or tag target.
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the target is a role or tag.
    /// </summary>
    public string TargetKind { get; set; } = "";

    /// <summary>
    /// Gets or sets current count.
    /// </summary>
    public int CurrentCount { get; set; }

    /// <summary>
    /// Gets or sets minimum target count.
    /// </summary>
    public int Minimum { get; set; }

    /// <summary>
    /// Gets or sets maximum target count when defined.
    /// </summary>
    public int? Maximum { get; set; }

    /// <summary>
    /// Gets or sets low, high, or ok.
    /// </summary>
    public string Status { get; set; } = "ok";

    /// <summary>
    /// Gets or sets evidence rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Describes one primary category balance row.
/// </summary>
public sealed class DeckWeakSpotCategoryRow
{
    /// <summary>
    /// Gets or sets category name.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Gets or sets total quantity in the category.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets whether this category contributes to active deck count.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets balance evidence signals.
    /// </summary>
    public List<string> Signals { get; set; } = [];
}

/// <summary>
/// Describes one active card that may deserve review.
/// </summary>
public sealed class DeckWeakSlotEvidenceRow
{
    /// <summary>
    /// Gets or sets card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets card quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets primary category.
    /// </summary>
    public string PrimaryCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets classifier primary role.
    /// </summary>
    public string Role { get; set; } = "";

    /// <summary>
    /// Gets or sets classifier tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets cached mana value.
    /// </summary>
    public double? ManaValue { get; set; }

    /// <summary>
    /// Gets or sets cached USD price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets classifier confidence.
    /// </summary>
    public double ClassifierConfidence { get; set; }

    /// <summary>
    /// Gets or sets Scryfall page when known.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets warnings when deck intent marks the card as protected from casual cuts.
    /// </summary>
    public List<string> ProtectedCardWarnings { get; set; } = [];

    /// <summary>
    /// Gets or sets weak-slot evidence signals.
    /// </summary>
    public List<string> Signals { get; set; } = [];
}

/// <summary>
/// Describes an existing excluded card that may cover a role or tag gap.
/// </summary>
public sealed class DeckWeakSpotCandidateRow
{
    /// <summary>
    /// Gets or sets card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets source category such as Sideboard or Maybeboard.
    /// </summary>
    public string SourceCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets role or tag this candidate may help.
    /// </summary>
    public string MatchedTarget { get; set; } = "";

    /// <summary>
    /// Gets or sets matched target kind.
    /// </summary>
    public string TargetKind { get; set; } = "";

    /// <summary>
    /// Gets or sets cached USD price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets Scryfall page when known.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets deterministic candidate rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports whether a review evidence source was evaluated.
/// </summary>
public sealed class DeckWeakSpotSourceStatus
{
    /// <summary>
    /// Gets or sets source key.
    /// </summary>
    public string SourceKey { get; set; } = "";

    /// <summary>
    /// Gets or sets status such as evaluated or not-queried.
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// Gets or sets source status notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}
