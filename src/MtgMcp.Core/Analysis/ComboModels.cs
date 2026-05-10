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
}

/// <summary>
/// Describes a detected deck combo or near miss.
/// </summary>
public sealed class DeckCombo
{
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
    /// Gets or sets the win route.
    /// </summary>
    public string WinRoute { get; set; } = "";

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
