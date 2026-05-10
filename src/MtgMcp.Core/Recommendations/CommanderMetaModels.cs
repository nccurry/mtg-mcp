namespace MtgMcp.Core;

/// <summary>
/// Describes a Commander metagame lookup.
/// </summary>
public sealed class CommanderMetaQuery
{
    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets the requested theme or archetype.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the maximum number of cards to return.
    /// </summary>
    public int Limit { get; set; } = 25;
}

/// <summary>
/// Describes a card from Commander metagame data.
/// </summary>
public sealed class CommanderMetaCard
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the metagame category.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Gets or sets the observed inclusion rate.
    /// </summary>
    public double InclusionRate { get; set; }

    /// <summary>
    /// Gets or sets the source-specific synergy score.
    /// </summary>
    public double SynergyScore { get; set; }

    /// <summary>
    /// Gets or sets the data source.
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// Gets or sets a source page for the card when available.
    /// </summary>
    public string? Uri { get; set; }
}

/// <summary>
/// Reports Commander metagame comparison data.
/// </summary>
public sealed class CommanderMetaReport
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
    /// Gets or sets the data source.
    /// </summary>
    public string Source { get; set; } = "unconfigured";

    /// <summary>
    /// Gets or sets popular cards for the commander or theme.
    /// </summary>
    public List<CommanderMetaCard> PopularCards { get; set; } = [];

    /// <summary>
    /// Gets or sets popular cards already present in the deck.
    /// </summary>
    public List<CommanderMetaCard> IncludedPopularCards { get; set; } = [];

    /// <summary>
    /// Gets or sets popular cards missing from the deck.
    /// </summary>
    public List<CommanderMetaCard> MissingPopularCards { get; set; } = [];

    /// <summary>
    /// Gets or sets comparison notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}
