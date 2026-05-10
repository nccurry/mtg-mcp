namespace MtgMcp.Core;

/// <summary>
/// Describes a recent-card lookup.
/// </summary>
public sealed class CardTrendQuery
{
    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the requested theme.
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Gets or sets the earliest release date.
    /// </summary>
    public DateOnly? Since { get; set; }

    /// <summary>
    /// Gets or sets a set code filter.
    /// </summary>
    public string? SetCode { get; set; }

    /// <summary>
    /// Gets or sets the maximum single-card price.
    /// </summary>
    public decimal? MaxPrice { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of suggestions.
    /// </summary>
    public int Limit { get; set; } = 10;
}

/// <summary>
/// Describes a newly released card suggestion.
/// </summary>
public sealed class NewCardSuggestion
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the primary role fit.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets secondary tags found on the card.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets the set code.
    /// </summary>
    public string? Set { get; set; }

    /// <summary>
    /// Gets or sets the USD price when known.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets the fit score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the recommendation rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports recent cards that may fit a workspace.
/// </summary>
public sealed class NewCardsForDeckResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the card suggestions.
    /// </summary>
    public List<NewCardSuggestion> Suggestions { get; set; } = [];

    /// <summary>
    /// Gets or sets lookup notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}
