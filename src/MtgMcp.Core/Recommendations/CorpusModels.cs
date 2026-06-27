using System.Text.Json.Serialization;

namespace MtgMcp.Core;

/// <summary>
/// Identifies one normalized cache entry for source facts.
/// </summary>
public sealed class CorpusCacheKey
{
    /// <summary>
    /// Gets or sets the source key.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Identifies the source endpoint or logical API call.
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized query fingerprint.
    /// </summary>
    public string Query { get; set; } = "";

    /// <summary>
    /// Gets or sets the adapter version that produced the value.
    /// </summary>
    public string AdapterVersion { get; set; } = "1";

    /// <summary>
    /// Gets or sets the analysis depth or source window.
    /// </summary>
    public string Budget { get; set; } = AnalysisDepths.Balanced;
}

/// <summary>
/// Lists corpus source API contract labels.
/// </summary>
public static class CorpusSourceApiTypes
{
    /// <summary>
    /// Indicates an official or documented API.
    /// </summary>
    public const string Official = "official";

    /// <summary>
    /// Indicates an unofficial structured endpoint.
    /// </summary>
    public const string UnofficialApi = "unofficial-api";
}

/// <summary>
/// Lists corpus source status labels.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CorpusSourceStatusKind>))]
public enum CorpusSourceStatusKind
{
    /// <summary>
    /// Indicates a source can be queried.
    /// </summary>
    [JsonStringEnumMemberName("available")]
    Available,

    /// <summary>
    /// Indicates a source is disabled by configuration.
    /// </summary>
    [JsonStringEnumMemberName("disabled")]
    Disabled,

    /// <summary>
    /// Indicates a source needs a configured API key.
    /// </summary>
    [JsonStringEnumMemberName("missing-config")]
    MissingConfig,

    /// <summary>
    /// Indicates a source query failed.
    /// </summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>
    /// Indicates the source rejected access without making the recommendation run fail.
    /// </summary>
    [JsonStringEnumMemberName("access-blocked")]
    AccessBlocked,

    /// <summary>
    /// Indicates a source needs an OAuth grant before it can be queried.
    /// </summary>
    [JsonStringEnumMemberName("needs-oauth")]
    NeedsOAuth,
}

/// <summary>
/// Describes how much source data a recommendation workflow may inspect and return.
/// </summary>
public sealed class RecommendationAnalysisBudget
{
    /// <summary>
    /// Gets or sets the normalized analysis depth name.
    /// </summary>
    public string AnalysisDepth { get; set; } = AnalysisDepths.Balanced;

    /// <summary>
    /// Gets or sets the maximum number of corpus sources to query.
    /// </summary>
    public int MaxSources { get; set; } = 4;

    /// <summary>
    /// Gets or sets the maximum number of exemplar decks to sample per source.
    /// </summary>
    public int MaxDecksPerSource { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum number of candidate cards retained before scoring.
    /// </summary>
    public int MaxCandidates { get; set; } = 40;

    /// <summary>
    /// Gets or sets the maximum number of recommendations returned to the client.
    /// </summary>
    public int MaxRecommendations { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of evidence rows returned per recommendation.
    /// </summary>
    public int MaxEvidencePerRecommendation { get; set; } = 3;

    /// <summary>
    /// Bounds each corpus provider call so slow sources yield partial results instead of blocking the whole lookup.
    /// </summary>
    public int SourceTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Gets or sets whether source URLs should be returned in compact evidence.
    /// </summary>
    public bool IncludeSourceUrls { get; set; } = true;

    /// <summary>
    /// Gets or sets whether exemplar deck rows should be requested and returned.
    /// </summary>
    public bool IncludeExemplarDecks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether combo details should be folded into card evidence.
    /// </summary>
    public bool IncludeComboDetails { get; set; } = true;

    /// <summary>
    /// Creates a budget from a configured or per-tool depth value.
    /// </summary>
    public static RecommendationAnalysisBudget FromDepth(string? analysisDepth)
    {
        string depth = NormalizeAnalysisDepth(analysisDepth);
        return depth switch
        {
            AnalysisDepths.Minimal => new RecommendationAnalysisBudget
            {
                AnalysisDepth = depth,
                MaxSources = 2,
                MaxDecksPerSource = 10,
                MaxCandidates = 15,
                MaxRecommendations = 5,
                MaxEvidencePerRecommendation = 2,
                SourceTimeoutSeconds = 12,
                IncludeSourceUrls = false,
                IncludeExemplarDecks = false,
                IncludeComboDetails = false
            },
            AnalysisDepths.Best => new RecommendationAnalysisBudget
            {
                AnalysisDepth = depth,
                MaxSources = 10,
                MaxDecksPerSource = 30,
                MaxCandidates = 100,
                MaxRecommendations = 20,
                MaxEvidencePerRecommendation = 6,
                SourceTimeoutSeconds = 25,
                IncludeSourceUrls = true,
                IncludeExemplarDecks = true,
                IncludeComboDetails = true
            },
            _ => new RecommendationAnalysisBudget
            {
                AnalysisDepth = AnalysisDepths.Balanced
            }
        };
    }

    /// <summary>
    /// Normalizes user-facing analysis-depth aliases.
    /// </summary>
    public static string NormalizeAnalysisDepth(string? analysisDepth)
    {
        string value = string.IsNullOrWhiteSpace(analysisDepth)
            ? AnalysisDepths.Balanced
            : analysisDepth.Trim().ToLowerInvariant();
        return value switch
        {
            "min" or "minimum" or "minimize" or "low" or "cheap" => AnalysisDepths.Minimal,
            "deep" or "full" or "max" or "maximum" or "best-analysis" => AnalysisDepths.Best,
            AnalysisDepths.Minimal => AnalysisDepths.Minimal,
            AnalysisDepths.Best => AnalysisDepths.Best,
            _ => AnalysisDepths.Balanced
        };
    }
}

/// <summary>
/// Lists normalized corpus signal types.
/// </summary>
public static class CorpusSignalTypes
{
    /// <summary>
    /// Indicates that a card appears in commander or theme aggregates.
    /// </summary>
    public const string Inclusion = "inclusion";

    /// <summary>
    /// Indicates that a card appears in successful event decks.
    /// </summary>
    public const string Performance = "performance";

    /// <summary>
    /// Indicates that a card appears in high-signal exemplar decks.
    /// </summary>
    public const string Exemplar = "exemplar";

    /// <summary>
    /// Indicates recent adoption or new-card relevance.
    /// </summary>
    public const string Trend = "trend";

    /// <summary>
    /// Indicates combo completion or near-miss relevance.
    /// </summary>
    public const string Combo = "combo";

    /// <summary>
    /// Indicates price or budget relevance.
    /// </summary>
    public const string Budget = "budget";

    /// <summary>
    /// Indicates market movement or price heat.
    /// </summary>
    public const string Market = "market";

    /// <summary>
    /// Indicates a lower-known card with meaningful fit.
    /// </summary>
    public const string Novelty = "novelty";

    /// <summary>
    /// Indicates direct fit with the local deck and intent.
    /// </summary>
    public const string LocalFit = "local-fit";

    /// <summary>
    /// Indicates that a card matched an explicit source tag.
    /// </summary>
    public const string Tag = "tag";

    /// <summary>
    /// Indicates that a card was explicitly mentioned in source discussion text.
    /// </summary>
    public const string Discussion = "discussion";
}

/// <summary>
/// Describes a corpus source capability and current status.
/// </summary>
public sealed class CorpusSourceStatus
{
    /// <summary>
    /// Gets or sets the source key.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the source display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the source kind.
    /// </summary>
    public string Kind { get; set; } = "provider";

    /// <summary>
    /// Gets or sets whether the source is enabled for analysis.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether the source is backed by a stable documented API.
    /// </summary>
    public bool StableApi { get; set; }

    /// <summary>
    /// Gets or sets the API contract type.
    /// </summary>
    public string ApiType { get; set; } = CorpusSourceApiTypes.Official;

    /// <summary>
    /// Gets or sets whether the source uses an unofficial structured endpoint.
    /// </summary>
    public bool UnofficialApi { get; set; }

    /// <summary>
    /// Gets or sets whether an API key is required.
    /// </summary>
    public bool RequiresKey { get; set; }

    /// <summary>
    /// Gets or sets whether permission or terms concerns require extra care.
    /// </summary>
    public bool PermissionSensitive { get; set; }

    /// <summary>
    /// Gets or sets whether visible attribution is required when using the source.
    /// </summary>
    public bool AttributionRequired { get; set; }

    /// <summary>
    /// Gets or sets the source status label.
    /// </summary>
    public CorpusSourceStatusKind Status { get; set; } = CorpusSourceStatusKind.Available;

    /// <summary>
    /// Gets or sets the source URL.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets when this source status was last observed from a live source call.
    /// </summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>
    /// Gets or sets source notes, limitations, and permission guidance.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes a normalized corpus lookup request.
/// </summary>
public sealed class CorpusSignalQuery
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets individual command-zone commander names for providers that exact-match deck cards.
    /// </summary>
    public List<string> CommanderNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the requested theme.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets the user goal or prompt.
    /// </summary>
    public string? Goal { get; set; }

    /// <summary>
    /// Gets or sets card names already present in the deck.
    /// </summary>
    public List<string> ExistingCards { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum single-card price.
    /// </summary>
    public decimal? MaxPrice { get; set; }

    /// <summary>
    /// Gets or sets whether providers should bypass fresh cache entries for this lookup.
    /// </summary>
    public bool Refresh { get; set; }
}

/// <summary>
/// Describes normalized evidence for one card from one source.
/// </summary>
public sealed class CardCorpusSignal
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the Scryfall Oracle id when known.
    /// </summary>
    public string? OracleId { get; set; }

    /// <summary>
    /// Gets or sets the source key or display name.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized signal type.
    /// </summary>
    public string SignalType { get; set; } = CorpusSignalTypes.Inclusion;

    /// <summary>
    /// Gets or sets the source section, tag, or theme bucket when provided.
    /// </summary>
    public string? Section { get; set; }

    /// <summary>
    /// Gets or sets source-specific confidence from 0 to 1.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the observed inclusion rate when a source provides one.
    /// </summary>
    public double? InclusionRate { get; set; }

    /// <summary>
    /// Gets or sets source-specific synergy from 0 to 1.
    /// </summary>
    public double? SynergyScore { get; set; }

    /// <summary>
    /// Gets or sets the number of decks behind the signal.
    /// </summary>
    public int? DeckCount { get; set; }

    /// <summary>
    /// Gets or sets the eligible deck count when provided by the source.
    /// </summary>
    public int? EligibleDeckCount { get; set; }

    /// <summary>
    /// Gets or sets performance or win-rate relevance from 0 to 1.
    /// </summary>
    public double? PerformanceScore { get; set; }

    /// <summary>
    /// Gets or sets the known USD price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets the card release date.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets the global EDHREC rank when known.
    /// </summary>
    public int? EdhrecRank { get; set; }

    /// <summary>
    /// Gets or sets a source URL for the evidence.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page for the signaled card when the provider supplied it.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets a compact human-readable source rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Describes one high-signal deck used as exemplar evidence.
/// </summary>
public sealed class DeckExemplarSignal
{
    /// <summary>
    /// Gets or sets the exemplar deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the source name.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the exemplar deck URL.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets the commander name when known.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets the popularity metric name.
    /// </summary>
    public string PopularityMetric { get; set; } = "";

    /// <summary>
    /// Gets or sets the popularity metric value.
    /// </summary>
    public double? PopularityValue { get; set; }

    /// <summary>
    /// Gets or sets the deck size.
    /// </summary>
    public int? DeckSize { get; set; }

    /// <summary>
    /// Gets or sets source tags or themes.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the source-derived exemplar weight from 0 to 1.
    /// </summary>
    public double Weight { get; set; }

    /// <summary>
    /// Gets or sets notes about this exemplar.
    /// </summary>
    public string Notes { get; set; } = "";
}

/// <summary>
/// Describes bounded raw discussion evidence from a public discussion source.
/// </summary>
public sealed class DiscussionEvidence
{
    /// <summary>
    /// Gets or sets the source name.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the source query that found this discussion row.
    /// </summary>
    public string Query { get; set; } = "";

    /// <summary>
    /// Gets or sets the discussion community name when known.
    /// </summary>
    public string? Community { get; set; }

    /// <summary>
    /// Gets or sets the post or thread title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the bounded source body text.
    /// </summary>
    public string Body { get; set; } = "";

    /// <summary>
    /// Gets or sets the source URL for the discussion row.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets the source score when provided.
    /// </summary>
    public int? Score { get; set; }

    /// <summary>
    /// Gets or sets when the source row was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets card names explicitly referenced by the discussion row.
    /// </summary>
    public List<string> MentionedCards { get; set; } = [];

    /// <summary>
    /// Lists decklist URLs found in the discussion body or title.
    /// </summary>
    public List<string> LinkedDeckUris { get; set; } = [];
}

/// <summary>
/// Reports normalized signals from one or more corpus sources.
/// </summary>
public sealed class CorpusSignalReport
{
    /// <summary>
    /// Gets or sets source status rows represented in this report.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets card evidence rows.
    /// </summary>
    public List<CardCorpusSignal> Signals { get; set; } = [];

    /// <summary>
    /// Gets or sets exemplar deck rows.
    /// </summary>
    public List<DeckExemplarSignal> ExemplarDecks { get; set; } = [];

    /// <summary>
    /// Gets or sets bounded raw discussion rows.
    /// </summary>
    public List<DiscussionEvidence> Discussions { get; set; } = [];

    /// <summary>
    /// Gets or sets lookup notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one deterministic card evidence row grouped from corpus signals.
/// </summary>
public sealed class CardEvidenceTableRow
{
    /// <summary>
    /// Names the card represented by this grouped evidence row.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Names the source that produced the evidence.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Identifies the normalized corpus signal type.
    /// </summary>
    public string SignalType { get; set; } = "";

    /// <summary>
    /// Captures the strongest source score represented by the row.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Counts evidence rows or deck rows represented by this group.
    /// </summary>
    public int EvidenceCount { get; set; }

    /// <summary>
    /// Carries source deck count when provided separately from evidence rows.
    /// </summary>
    public int? DeckCount { get; set; }

    /// <summary>
    /// Carries observed inclusion rate when the source provides one.
    /// </summary>
    public double? InclusionRate { get; set; }

    /// <summary>
    /// Indicates whether this card is already present in the active deck.
    /// </summary>
    public bool AlreadyInDeck { get; set; }

    /// <summary>
    /// Indicates whether this card appears anywhere in the workspace.
    /// </summary>
    public bool AlreadyInWorkspace { get; set; }

    /// <summary>
    /// Lists all workspace categories attached to matching card rows.
    /// </summary>
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Lists non-primary workspace categories attached to matching card rows.
    /// </summary>
    public List<string> SecondaryCategories { get; set; } = [];

    /// <summary>
    /// Lists where this card appears in the workspace.
    /// </summary>
    public List<CardWorkspaceLocation> Locations { get; set; } = [];

    /// <summary>
    /// Points to a representative source URL.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page for linking this evidence row.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Summarizes the source rationale without adding LLM interpretation.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Describes one workspace location for a card evidence row.
/// </summary>
public sealed class CardWorkspaceLocation
{
    /// <summary>
    /// Gets or sets the workspace category name.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this category is the card row's primary category.
    /// </summary>
    public bool Primary { get; set; }

    /// <summary>
    /// Gets or sets whether the category contributes to the active deck.
    /// </summary>
    public bool IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets card quantity in this category location.
    /// </summary>
    public int Quantity { get; set; }
}

/// <summary>
/// Reports raw source evidence and grouped card rows without making deckbuilding choices.
/// </summary>
public sealed class CorpusEvidenceSearchResult
{
    /// <summary>
    /// Identifies the workspace used for the evidence query.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Names the commander used to build the source query.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Describes the requested or inferred deck theme.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Echoes the requested source key or name.
    /// </summary>
    public string SourceKey { get; set; } = "";

    /// <summary>
    /// Records the normalized analysis depth.
    /// </summary>
    public string AnalysisDepth { get; set; } = AnalysisDepths.Balanced;

    /// <summary>
    /// Records the effective source budget.
    /// </summary>
    public RecommendationAnalysisBudget Budget { get; set; } = new();

    /// <summary>
    /// Contains deterministic card evidence rows grouped from source signals.
    /// </summary>
    public List<CardEvidenceTableRow> CardEvidence { get; set; } = [];

    /// <summary>
    /// Contains raw bounded discussion rows returned by discussion providers.
    /// </summary>
    public List<DiscussionEvidence> Discussions { get; set; } = [];

    /// <summary>
    /// Contains exemplar deck rows returned by decklist providers.
    /// </summary>
    public List<DeckExemplarSignal> ExemplarDecks { get; set; } = [];

    /// <summary>
    /// Contains source status rows.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Contains lookup notes and source limitations.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes compact evidence attached to a recommendation.
/// </summary>
public sealed class CorpusEvidence
{
    /// <summary>
    /// Gets or sets the source name.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the signal type.
    /// </summary>
    public string SignalType { get; set; } = "";

    /// <summary>
    /// Gets or sets the signal score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets a compact summary.
    /// </summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// Gets or sets the source URL.
    /// </summary>
    public string? Uri { get; set; }
}

/// <summary>
/// Describes one corpus-backed card recommendation.
/// </summary>
public sealed class CorpusRecommendation
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card being replaced, when this is a replacement recommendation.
    /// </summary>
    public string? ReplaceCard { get; set; }

    /// <summary>
    /// Gets or sets the recommendation kind.
    /// </summary>
    public string RecommendationKind { get; set; } = "candidate";

    /// <summary>
    /// Gets or sets the primary role.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets secondary tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the final score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets recommendation confidence.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets known USD price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets EDHREC rank when known.
    /// </summary>
    public int? EdhrecRank { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page for linking the recommended card.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page for the replaced card when applicable.
    /// </summary>
    public string? ReplaceCardScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets the concise rationale.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets compact source evidence.
    /// </summary>
    public List<CorpusEvidence> Evidence { get; set; } = [];
}

/// <summary>
/// Reports corpus-backed recommendations for a deck.
/// </summary>
public sealed class CorpusRecommendationResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets the requested theme.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets the normalized analysis depth.
    /// </summary>
    public string AnalysisDepth { get; set; } = AnalysisDepths.Balanced;

    /// <summary>
    /// Gets or sets the effective analysis budget.
    /// </summary>
    public RecommendationAnalysisBudget Budget { get; set; } = new();

    /// <summary>
    /// Gets or sets recommendations.
    /// </summary>
    public List<CorpusRecommendation> Recommendations { get; set; } = [];

    /// <summary>
    /// Gets or sets compact source status rows.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets exemplar deck rows.
    /// </summary>
    public List<DeckExemplarSignal> ExemplarDecks { get; set; } = [];

    /// <summary>
    /// Gets or sets bounded raw discussion rows that informed the lookup.
    /// </summary>
    public List<DiscussionEvidence> Discussions { get; set; } = [];

    /// <summary>
    /// Gets or sets lookup notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports a corpus-backed budget replacement plan.
/// </summary>
public sealed class CorpusBudgetReplacementResult
{
    /// <summary>
    /// Gets or sets the persisted edit plan.
    /// </summary>
    public DeckEditPlan Plan { get; set; } = new();

    /// <summary>
    /// Gets or sets corpus-enriched replacement recommendations.
    /// </summary>
    public List<CorpusRecommendation> Recommendations { get; set; } = [];

    /// <summary>
    /// Gets or sets source status rows.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets the normalized analysis depth.
    /// </summary>
    public string AnalysisDepth { get; set; } = AnalysisDepths.Balanced;

    /// <summary>
    /// Gets or sets lookup notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports high-signal exemplar decks for a commander or deck context.
/// </summary>
public sealed class TopExemplarDecksResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized analysis depth.
    /// </summary>
    public string AnalysisDepth { get; set; } = AnalysisDepths.Balanced;

    /// <summary>
    /// Gets or sets exemplar deck rows.
    /// </summary>
    public List<DeckExemplarSignal> ExemplarDecks { get; set; } = [];

    /// <summary>
    /// Gets or sets source status rows.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets lookup notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports configured corpus sources.
/// </summary>
public sealed class CorpusSourceStatusResult
{
    /// <summary>
    /// Gets or sets source status rows.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];
}
