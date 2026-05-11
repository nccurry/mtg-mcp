namespace MtgMcp.Core;

/// <summary>
/// Reports unified deck improvement brainstorming.
/// </summary>
public sealed class BrainstormDeckImprovementsResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets best-practice analysis.
    /// </summary>
    public DeckBestPracticeAnalysis BestPractices { get; set; } = new();

    /// <summary>
    /// Gets or sets Commander metagame comparison.
    /// </summary>
    public CommanderMetaReport Meta { get; set; } = new();

    /// <summary>
    /// Gets or sets new-card suggestions.
    /// </summary>
    public NewCardsForDeckResult NewCards { get; set; } = new();

    /// <summary>
    /// Gets or sets goal-driven package suggestions.
    /// </summary>
    public GoalPackagePlanResult GoalPackage { get; set; } = new();

    /// <summary>
    /// Gets or sets combo analysis.
    /// </summary>
    public DeckComboReport Combos { get; set; } = new();

    /// <summary>
    /// Gets or sets goldfish projection.
    /// </summary>
    public GoldfishSimulationResult Goldfish { get; set; } = new();

    /// <summary>
    /// Gets or sets ranked recommendations.
    /// </summary>
    public List<string> RankedRecommendations { get; set; } = [];

    /// <summary>
    /// Gets or sets brainstorming notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}
