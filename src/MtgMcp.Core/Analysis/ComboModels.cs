namespace MtgMcp.Core;

/// <summary>
/// Describes a combo catalog lookup.
/// </summary>
public sealed class ComboCatalogQuery
{
    /// <summary>
    /// Gets or sets the card names in the deck.
    /// </summary>
    public List<string> CardNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets whether combo providers should bypass fresh cache entries.
    /// </summary>
    public bool Refresh { get; set; }
}

/// <summary>
/// Describes a combo catalog search by one card.
/// </summary>
public sealed class ComboCardSearchQuery
{
    /// <summary>
    /// Gets or sets the normalized card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the maximum number of combos to return.
    /// </summary>
    public int Limit { get; set; } = 50;

    /// <summary>
    /// Gets or sets whether combo providers should bypass fresh cache entries.
    /// </summary>
    public bool Refresh { get; set; }
}

/// <summary>
/// Describes a combo catalog detail lookup.
/// </summary>
public sealed class ComboDetailsQuery
{
    /// <summary>
    /// Gets or sets the source combo id.
    /// </summary>
    public string ComboId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether combo providers should bypass fresh cache entries.
    /// </summary>
    public bool Refresh { get; set; }
}

/// <summary>
/// Describes one combo catalog evidence row.
/// </summary>
public sealed class ComboEvidence
{
    /// <summary>
    /// Gets or sets the source combo id.
    /// </summary>
    public string ComboId { get; set; } = "";

    /// <summary>
    /// Gets or sets cards used by the combo.
    /// </summary>
    public List<string> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets cards missing from a deck or near-miss context.
    /// </summary>
    public List<string> MissingCards { get; set; } = [];

    /// <summary>
    /// Gets or sets produced features reported by the catalog.
    /// </summary>
    public List<string> ProducedFeatures { get; set; } = [];

    /// <summary>
    /// Gets or sets required cards, templates, or game objects reported by the catalog.
    /// </summary>
    public List<string> Requires { get; set; } = [];

    /// <summary>
    /// Gets or sets required templates reported by the catalog.
    /// </summary>
    public List<string> Templates { get; set; } = [];

    /// <summary>
    /// Gets or sets prerequisites reported by the catalog.
    /// </summary>
    public List<string> Prerequisites { get; set; } = [];

    /// <summary>
    /// Gets or sets combo steps reported by the catalog.
    /// </summary>
    public List<string> Steps { get; set; } = [];

    /// <summary>
    /// Gets or sets the combo color identity when provided by the catalog.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the bracket tag reported by the catalog.
    /// </summary>
    public string? BracketTag { get; set; }

    /// <summary>
    /// Gets or sets source popularity or prevalence when provided by the catalog.
    /// </summary>
    public double? Popularity { get; set; }

    /// <summary>
    /// Gets or sets format legality flags reported by the catalog.
    /// </summary>
    public Dictionary<string, bool> Legalities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the source page or API detail URI.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Gets or sets route classifications derived from produced features.
    /// </summary>
    public List<WinRouteClassification> RouteClassifications { get; set; } = [];

    /// <summary>
    /// Gets or sets source and determinism metadata.
    /// </summary>
    public SourceEvidenceMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Reports combos found for a card search.
/// </summary>
public sealed class ComboEvidenceSearchResult
{
    /// <summary>
    /// Gets or sets the normalized searched card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the commander used for optional color identity filtering.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets whether commander color identity filtering was applied.
    /// </summary>
    public bool StrictColorIdentity { get; set; }

    /// <summary>
    /// Gets or sets combo evidence rows.
    /// </summary>
    public List<ComboEvidence> Combos { get; set; } = [];

    /// <summary>
    /// Gets or sets lookup notes and source limitations.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes a detected deck combo or near miss.
/// </summary>
public sealed class DeckCombo
{
    /// <summary>
    /// Gets or sets the catalog combo id.
    /// </summary>
    public string? ComboId { get; set; }

    /// <summary>
    /// Gets or sets the combo name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets cards present in the deck.
    /// </summary>
    public List<string> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets cards needed to complete the combo.
    /// </summary>
    public List<string> MissingCards { get; set; } = [];

    /// <summary>
    /// Gets or sets produced features reported by catalog evidence.
    /// </summary>
    public List<string> ProducedFeatures { get; set; } = [];

    /// <summary>
    /// Gets or sets required templates reported by catalog evidence.
    /// </summary>
    public List<string> RequiredTemplates { get; set; } = [];

    /// <summary>
    /// Gets or sets prerequisites reported by catalog evidence.
    /// </summary>
    public List<string> Prerequisites { get; set; } = [];

    /// <summary>
    /// Gets or sets source steps reported by catalog evidence.
    /// </summary>
    public List<string> Steps { get; set; } = [];

    /// <summary>
    /// Gets or sets combo color identity when known.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the win route.
    /// </summary>
    public string WinRoute { get; set; } = "";

    /// <summary>
    /// Gets or sets approved route labels.
    /// </summary>
    public List<string> RouteLabels { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the combo evidence directly wins.
    /// </summary>
    public bool Terminal { get; set; }

    /// <summary>
    /// Gets or sets whether the combo needs a payoff to win.
    /// </summary>
    public bool NeedsPayoff { get; set; }

    /// <summary>
    /// Gets or sets payoff kinds needed for non-terminal combo evidence.
    /// </summary>
    public List<string> PayoffKindsNeeded { get; set; } = [];

    /// <summary>
    /// Gets or sets the combo kind.
    /// </summary>
    public string Kind { get; set; } = "value";

    /// <summary>
    /// Gets or sets confidence in the detection.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets the combo source.
    /// </summary>
    public string Source { get; set; } = "heuristic";

    /// <summary>
    /// Gets or sets the detection rationale.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets a source page or API detail URI.
    /// </summary>
    public string? SourceUri { get; set; }

    /// <summary>
    /// Gets or sets source and determinism metadata.
    /// </summary>
    public SourceEvidenceMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Reports combo pressure for a deck.
/// </summary>
public sealed class ComboPressureEstimate
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the pressure score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the pressure level.
    /// </summary>
    public string Level { get; set; } = "low";

    /// <summary>
    /// Gets or sets pressure signals.
    /// </summary>
    public List<string> Signals { get; set; } = [];

    /// <summary>
    /// Gets or sets pressure notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports combos and near misses in a deck.
/// </summary>
public sealed class DeckComboReport
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets completed combos.
    /// </summary>
    public List<DeckCombo> Combos { get; set; } = [];

    /// <summary>
    /// Gets or sets one-card-away or partial combos.
    /// </summary>
    public List<DeckCombo> NearMisses { get; set; } = [];

    /// <summary>
    /// Gets or sets combo pressure.
    /// </summary>
    public ComboPressureEstimate Pressure { get; set; } = new();

    /// <summary>
    /// Gets or sets combo notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}
