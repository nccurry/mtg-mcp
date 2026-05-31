namespace MtgMcp.Core;

/// <summary>
/// Reports source-backed aggregate cards for one commander.
/// </summary>
public sealed class CommanderAggregateCardsResult
{
    /// <summary>
    /// Gets or sets the normalized commander name.
    /// </summary>
    public string CommanderName { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized theme slug or text when requested.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets aggregate card rows grouped by source in result order.
    /// </summary>
    public List<CommanderAggregateCardRow> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets source status rows.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets source limitations and non-fatal notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one source-backed commander aggregate card row.
/// </summary>
public sealed class CommanderAggregateCardRow
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the source key or display name.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the source section or theme bucket.
    /// </summary>
    public string Section { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of decks behind the row when provided.
    /// </summary>
    public int? DeckCount { get; set; }

    /// <summary>
    /// Gets or sets the eligible deck count when provided by the source.
    /// </summary>
    public int? EligibleDeckCount { get; set; }

    /// <summary>
    /// Gets or sets the inclusion rate when provided or calculable.
    /// </summary>
    public double? InclusionRate { get; set; }

    /// <summary>
    /// Gets or sets the source synergy score when provided.
    /// </summary>
    public double? SynergyScore { get; set; }

    /// <summary>
    /// Gets or sets source score used for deterministic ordering.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets source and determinism metadata.
    /// </summary>
    public SourceEvidenceMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Reports source-backed commander tags and themes.
/// </summary>
public sealed class CommanderTagsResult
{
    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string CommanderName { get; set; } = "";

    /// <summary>
    /// Gets or sets tag rows.
    /// </summary>
    public List<CommanderTagRow> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets source status rows.
    /// </summary>
    public List<CorpusSourceStatus> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets lookup notes and source limitations.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one commander tag or source section.
/// </summary>
public sealed class CommanderTagRow
{
    /// <summary>
    /// Gets or sets the tag display name.
    /// </summary>
    public string TagName { get; set; } = "";

    /// <summary>
    /// Gets or sets the normalized source slug.
    /// </summary>
    public string ThemeSlug { get; set; } = "";

    /// <summary>
    /// Gets or sets the source name.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets the summed deck/sample count when available.
    /// </summary>
    public int? DeckCount { get; set; }

    /// <summary>
    /// Gets or sets source and determinism metadata.
    /// </summary>
    public SourceEvidenceMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Bundles structured win-condition evidence for one commander.
/// </summary>
public sealed class CommanderWinConditionEvidenceResult
{
    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string CommanderName { get; set; } = "";

    /// <summary>
    /// Gets or sets the requested theme.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets source-backed aggregate card evidence.
    /// </summary>
    public CommanderAggregateCardsResult AggregateCards { get; set; } = new();

    /// <summary>
    /// Gets or sets source-backed tags or sections.
    /// </summary>
    public CommanderTagsResult Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets combo catalog evidence containing the commander.
    /// </summary>
    public ComboEvidenceSearchResult Combos { get; set; } = new();

    /// <summary>
    /// Gets or sets route classifications from combo evidence.
    /// </summary>
    public List<WinRouteClassification> RouteClassifications { get; set; } = [];

    /// <summary>
    /// Gets or sets payoff searches for non-terminal routes.
    /// </summary>
    public List<WinconPayoffSearchResult> PayoffSearches { get; set; } = [];

    /// <summary>
    /// Gets or sets bundle notes and source limitations.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

