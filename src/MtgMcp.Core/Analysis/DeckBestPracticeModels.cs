namespace MtgMcp.Core;

/// <summary>
/// Describes a deck role or tag need.
/// </summary>
public sealed class DeckNeed
{
    /// <summary>
    /// Gets or sets the role or tag name.
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// Gets or sets the current card count.
    /// </summary>
    public int CurrentCount { get; set; }

    /// <summary>
    /// Gets or sets the minimum desired count.
    /// </summary>
    public int Minimum { get; set; }

    /// <summary>
    /// Gets or sets the maximum desired count.
    /// </summary>
    public int? Maximum { get; set; }

    /// <summary>
    /// Gets or sets the gap status.
    /// </summary>
    public string Status { get; set; } = "ok";

    /// <summary>
    /// Gets or sets the reason for the need assessment.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports role and tag needs for a workspace.
/// </summary>
public sealed class DeckNeedProfile
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck format.
    /// </summary>
    public string Format { get; set; } = "commander";

    /// <summary>
    /// Gets or sets the role need rows.
    /// </summary>
    public List<DeckNeed> RoleNeeds { get; set; } = [];

    /// <summary>
    /// Gets or sets the tag need rows.
    /// </summary>
    public List<DeckNeed> TagNeeds { get; set; } = [];

    /// <summary>
    /// Gets or sets profile notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports how closely a deck matches a named Commander heuristic profile.
/// </summary>
public sealed class DeckHeuristicProfileComparison
{
    /// <summary>
    /// Gets or sets the profile id.
    /// </summary>
    public string ProfileId { get; set; } = "";

    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the fit score from 0 to 100.
    /// </summary>
    public double FitScore { get; set; }

    /// <summary>
    /// Gets or sets the fit status.
    /// </summary>
    public string Status { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets low-count findings.
    /// </summary>
    public List<string> Gaps { get; set; } = [];

    /// <summary>
    /// Gets or sets high-count findings.
    /// </summary>
    public List<string> Overages { get; set; } = [];

    /// <summary>
    /// Gets or sets explanatory notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Describes a source used by best-practice analysis.
/// </summary>
public sealed class DeckCitation
{
    /// <summary>
    /// Gets or sets a stable citation key.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the source title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the source URI.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets a short source note.
    /// </summary>
    public string Notes { get; set; } = "";
}

/// <summary>
/// Reports best-practice analysis for a deck.
/// </summary>
public sealed class DeckBestPracticeAnalysis
{
    /// <summary>
    /// Gets or sets the deterministic analysis model label.
    /// </summary>
    public string ModelLabel { get; set; } = "deterministic-best-practice-thresholds";

    /// <summary>
    /// Gets or sets the selected profile catalog version.
    /// </summary>
    public string ConfigVersion { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the need profile.
    /// </summary>
    public DeckNeedProfile NeedProfile { get; set; } = new();

    /// <summary>
    /// Gets or sets the profile used for the primary need profile.
    /// </summary>
    public string RecommendedProfile { get; set; } = "commander-baseline";

    /// <summary>
    /// Gets or sets the explicit input that selected the recommended profile.
    /// </summary>
    public string ProfileSource { get; set; } = "baseline-default";

    /// <summary>
    /// Gets or sets heuristic profile comparisons.
    /// </summary>
    public List<DeckHeuristicProfileComparison> HeuristicComparisons { get; set; } = [];

    /// <summary>
    /// Gets or sets strengths found in the deck.
    /// </summary>
    public List<string> Strengths { get; set; } = [];

    /// <summary>
    /// Gets or sets risks found in the deck.
    /// </summary>
    public List<string> Risks { get; set; } = [];

    /// <summary>
    /// Gets or sets recommended next actions.
    /// </summary>
    public List<string> Recommendations { get; set; } = [];

    /// <summary>
    /// Gets or sets cited heuristics and sources.
    /// </summary>
    public List<DeckCitation> Citations { get; set; } = [];
}
