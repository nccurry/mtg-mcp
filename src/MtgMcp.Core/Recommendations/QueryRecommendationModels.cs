namespace MtgMcp.Core;

/// <summary>
/// Describes deck constraints applied while ranking Scryfall query results.
/// </summary>
public sealed class DeckQueryRecommendationConstraints
{
    /// <summary>
    /// Gets or sets the normalized format used for legality checks.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets whether a deck color identity was inferred.
    /// </summary>
    public bool ColorIdentityKnown { get; set; }

    /// <summary>
    /// Gets or sets the inferred deck color identity.
    /// </summary>
    public List<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum card price.
    /// </summary>
    public decimal? MaxPrice { get; set; }

    /// <summary>
    /// Gets or sets roles that candidates must match when supplied.
    /// </summary>
    public List<string> RequiredRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets tags that candidates must match when supplied.
    /// </summary>
    public List<string> RequiredTags { get; set; } = [];

    /// <summary>
    /// Gets or sets roles that reject matching candidates.
    /// </summary>
    public List<string> ExcludedRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets tags that reject matching candidates.
    /// </summary>
    public List<string> ExcludedTags { get; set; } = [];
}

/// <summary>
/// Describes an accepted card from a deck-aware Scryfall query.
/// </summary>
public sealed class DeckQueryCandidate
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the primary role assigned by the classifier.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets secondary role tags assigned by the classifier.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the overall fit score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the role-matching portion of the score.
    /// </summary>
    public double RoleScore { get; set; }

    /// <summary>
    /// Gets or sets the tag-matching portion of the score.
    /// </summary>
    public double TagScore { get; set; }

    /// <summary>
    /// Gets or sets the popularity portion of the score.
    /// </summary>
    public double RankScore { get; set; }

    /// <summary>
    /// Gets or sets the budget portion of the score.
    /// </summary>
    public double PriceScore { get; set; }

    /// <summary>
    /// Gets or sets the card price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets concise fit reasons.
    /// </summary>
    public List<string> Reasons { get; set; } = [];

    /// <summary>
    /// Gets or sets the recommendation rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Describes a query result card rejected by deterministic deck filters.
/// </summary>
public sealed class DeckQueryRejectedCandidate
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the primary role assigned by the classifier when available.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets secondary role tags assigned by the classifier when available.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the card price when available.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets exact rejection reasons.
    /// </summary>
    public List<string> Reasons { get; set; } = [];
}

/// <summary>
/// Reports deck-aware ranking for an agent-supplied Scryfall query.
/// </summary>
public sealed class DeckQueryRecommendationResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the user-facing goal that motivated the query.
    /// </summary>
    public string Goal { get; set; } = "";

    /// <summary>
    /// Gets or sets the raw Scryfall query supplied by the caller.
    /// </summary>
    public string ScryfallQuery { get; set; } = "";

    /// <summary>
    /// Gets or sets the Scryfall queries executed by the service.
    /// </summary>
    public List<string> ExecutedQueries { get; set; } = [];

    /// <summary>
    /// Gets or sets deck constraints applied after search.
    /// </summary>
    public DeckQueryRecommendationConstraints Constraints { get; set; } = new();

    /// <summary>
    /// Gets or sets accepted candidates ordered by score.
    /// </summary>
    public List<DeckQueryCandidate> Candidates { get; set; } = [];

    /// <summary>
    /// Gets or sets rejected candidates with reasons.
    /// </summary>
    public List<DeckQueryRejectedCandidate> Rejected { get; set; } = [];

    /// <summary>
    /// Gets or sets non-fatal query quality warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Reports a persisted non-mutating plan created from a deck-aware query.
/// </summary>
public sealed class DeckQueryPlanResult
{
    /// <summary>
    /// Gets or sets the persisted plan.
    /// </summary>
    public DeckEditPlan Plan { get; set; } = new();

    /// <summary>
    /// Gets or sets the ranking result that produced the plan.
    /// </summary>
    public DeckQueryRecommendationResult Ranking { get; set; } = new();
}
