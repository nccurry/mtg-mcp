namespace MtgMcp.Core;

/// <summary>
/// Provides replacement scoring weights.
/// </summary>
public sealed class ReplacementWeights
{
    /// <summary>
    /// Gets or sets the role weight.
    /// </summary>
    public double Role { get; set; } = 0.45;

    /// <summary>
    /// Gets or sets the power weight.
    /// </summary>
    public double Power { get; set; } = 0.30;

    /// <summary>
    /// Gets or sets the price weight.
    /// </summary>
    public double Price { get; set; } = 0.25;
}

/// <summary>
/// Provides replacement suggestion behavior.
/// </summary>
public sealed class ReplacementSuggestion
{
    /// <summary>
    /// Gets or sets the card to replace.
    /// </summary>
    public string ReplaceCard { get; set; } = "";

    /// <summary>
    /// Gets or sets the replacement card.
    /// </summary>
    public string WithCard { get; set; } = "";

    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets the score.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the role score.
    /// </summary>
    public double RoleScore { get; set; }

    /// <summary>
    /// Gets or sets the power score.
    /// </summary>
    public double PowerScore { get; set; }

    /// <summary>
    /// Gets or sets the price score.
    /// </summary>
    public double PriceScore { get; set; }

    /// <summary>
    /// Breaks the replacement score into deterministic contextual features.
    /// </summary>
    public ReplacementFeatureVector FeatureVector { get; set; } = new();

    /// <summary>
    /// Gets or sets the current price.
    /// </summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>
    /// Gets or sets the candidate price.
    /// </summary>
    public decimal? CandidatePrice { get; set; }

    /// <summary>
    /// Gets or sets the estimated savings.
    /// </summary>
    public decimal? EstimatedSavings { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page for the card being replaced.
    /// </summary>
    public string? ReplaceCardScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets the Scryfall card page for the replacement card.
    /// </summary>
    public string? WithCardScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Breaks replacement scoring into named factors that can be inspected independently.
/// </summary>
public sealed class ReplacementFeatureVector
{
    /// <summary>
    /// Measures primary role and tag overlap with the replaced card.
    /// </summary>
    public double RoleFit { get; set; }

    /// <summary>
    /// Measures whether the replacement helps the commander or deck curve.
    /// </summary>
    public double CommanderCurve { get; set; }

    /// <summary>
    /// Measures mana-value and tempo pressure compared with the replaced card.
    /// </summary>
    public double Tempo { get; set; }

    /// <summary>
    /// Measures color production or color-fixing relevance when applicable.
    /// </summary>
    public double Fixing { get; set; }

    /// <summary>
    /// Measures explicit deck-plan, intent, and tag synergy.
    /// </summary>
    public double PlanSynergy { get; set; }

    /// <summary>
    /// Measures whether the card remains useful after early development.
    /// </summary>
    public double LateGameFloor { get; set; }

    /// <summary>
    /// Measures interaction speed, modality, or resilience when relevant.
    /// </summary>
    public double InteractionModality { get; set; }

    /// <summary>
    /// Measures price fit using the active replacement mode.
    /// </summary>
    public double Price { get; set; }

    /// <summary>
    /// Measures the quality of source-backed metadata available for scoring.
    /// </summary>
    public double EvidenceQuality { get; set; }
}

/// <summary>
/// Provides recommendation plan result behavior.
/// </summary>
public sealed class RecommendationPlanResult
{
    /// <summary>
    /// Gets or sets the plan.
    /// </summary>
    public DeckEditPlan Plan { get; set; } = new();

    /// <summary>
    /// Gets or sets the suggestions.
    /// </summary>
    public List<ReplacementSuggestion> Suggestions { get; set; } = [];
}

/// <summary>
/// Provides category suggestion behavior.
/// </summary>
public sealed class CategorySuggestion
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the current primary category.
    /// </summary>
    public string CurrentPrimaryCategory { get; set; } = "";

    /// <summary>
    /// Gets or sets the suggested primary role.
    /// </summary>
    public string SuggestedPrimaryRole { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the Scryfall card page for linking the card.
    /// </summary>
    public string? ScryfallUri { get; set; }

    /// <summary>
    /// Gets or sets the confidence.
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Provides category plan result behavior.
/// </summary>
public sealed class CategoryPlanResult
{
    /// <summary>
    /// Gets or sets the plan.
    /// </summary>
    public DeckEditPlan Plan { get; set; } = new();

    /// <summary>
    /// Gets or sets the suggestions.
    /// </summary>
    public List<CategorySuggestion> Suggestions { get; set; } = [];
}
