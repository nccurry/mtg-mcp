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

/// <summary>
/// Reports new-card candidates with deterministic swap evidence.
/// </summary>
public sealed class NewCardSwapReviewResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets new-card swap candidates.
    /// </summary>
    public List<NewCardSwapCandidate> Candidates { get; set; } = [];

    /// <summary>
    /// Gets or sets source and scoring notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes one new-card candidate and possible cuts.
/// </summary>
public sealed class NewCardSwapCandidate
{
    /// <summary>
    /// Gets or sets the candidate card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the candidate role.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets candidate tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets release date when known.
    /// </summary>
    public DateOnly? ReleasedAt { get; set; }

    /// <summary>
    /// Gets or sets set code when known.
    /// </summary>
    public string? Set { get; set; }

    /// <summary>
    /// Gets or sets known USD price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets deterministic candidate score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets candidate rationale.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets deterministic cut candidates.
    /// </summary>
    public List<NewCardCutEvidence> CutCandidates { get; set; } = [];

    /// <summary>
    /// Gets or sets source and determinism metadata.
    /// </summary>
    public SourceEvidenceMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Describes deterministic evidence for cutting one existing card.
/// </summary>
public sealed class NewCardCutEvidence
{
    /// <summary>
    /// Gets or sets the existing card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card's current role.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets whether the role overlaps the new card.
    /// </summary>
    public bool RoleOverlap { get; set; }

    /// <summary>
    /// Gets or sets whether mana value is in the same curve slot.
    /// </summary>
    public bool ManaCurveSlot { get; set; }

    /// <summary>
    /// Gets or sets duplicate effect density for the current role.
    /// </summary>
    public double DuplicateEffectDensity { get; set; }

    /// <summary>
    /// Gets or sets whether local tags suggest a theme mismatch.
    /// </summary>
    public bool ThemeMismatch { get; set; }

    /// <summary>
    /// Gets or sets current card price minus candidate price.
    /// </summary>
    public decimal? PriceDelta { get; set; }

    /// <summary>
    /// Gets or sets protected-card warnings.
    /// </summary>
    public List<string> ProtectedCardWarnings { get; set; } = [];

    /// <summary>
    /// Gets or sets deterministic cut score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets exact scoring reasons.
    /// </summary>
    public List<string> Reasons { get; set; } = [];
}
