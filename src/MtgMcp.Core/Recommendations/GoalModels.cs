namespace MtgMcp.Core;

/// <summary>
/// Describes a card proposed for a goal package.
/// </summary>
public sealed class GoalCardSuggestion
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the card role.
    /// </summary>
    public string Role { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets secondary tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the fit score.
    /// </summary>
    public double FitScore { get; set; }

    /// <summary>
    /// Gets or sets the card price.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Gets or sets the recommendation rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports a goal-driven recommendation plan.
/// </summary>
public sealed class GoalPackagePlanResult
{
    /// <summary>
    /// Gets or sets the persisted plan.
    /// </summary>
    public DeckEditPlan Plan { get; set; } = new();

    /// <summary>
    /// Gets or sets the user goal.
    /// </summary>
    public string Goal { get; set; } = "";

    /// <summary>
    /// Gets or sets the strategy used for the goal.
    /// </summary>
    public string Strategy { get; set; } = "balanced";

    /// <summary>
    /// Gets or sets card suggestions.
    /// </summary>
    public List<GoalCardSuggestion> Suggestions { get; set; } = [];
}
