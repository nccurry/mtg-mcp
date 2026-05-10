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
    /// Gets or sets whether the combo catalog should bypass fresh cache entries.
    /// </summary>
    public bool Refresh { get; set; }
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

/// <summary>
/// Describes a goldfish win route.
/// </summary>
public sealed class WinRoute
{
    /// <summary>
    /// Gets or sets the route name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the route kind.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets the earliest likely turn.
    /// </summary>
    public int? EarliestTurn { get; set; }

    /// <summary>
    /// Gets or sets the route probability.
    /// </summary>
    public double Probability { get; set; }

    /// <summary>
    /// Gets or sets cards associated with the route.
    /// </summary>
    public List<string> Cards { get; set; } = [];

    /// <summary>
    /// Gets or sets the route rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Reports likely win timing for a deck.
/// </summary>
public sealed class WinTurnEstimate
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of simulations.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the median win turn.
    /// </summary>
    public int? MedianWinTurn { get; set; }

    /// <summary>
    /// Gets or sets the twenty-fifth percentile win turn.
    /// </summary>
    public int? P25WinTurn { get; set; }

    /// <summary>
    /// Gets or sets the seventy-fifth percentile win turn.
    /// </summary>
    public int? P75WinTurn { get; set; }

    /// <summary>
    /// Gets or sets cumulative win rates by turn.
    /// </summary>
    public Dictionary<int, double> WinByTurnRates { get; set; } = [];

    /// <summary>
    /// Gets or sets likely routes to victory.
    /// </summary>
    public List<WinRoute> Routes { get; set; } = [];

    /// <summary>
    /// Gets or sets win estimate notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Reports a projected board state for a turn.
/// </summary>
public sealed class ProjectedTurnState
{
    /// <summary>
    /// Gets or sets the turn number.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets the median lands on the battlefield.
    /// </summary>
    public int MedianLands { get; set; }

    /// <summary>
    /// Gets or sets the median total mana sources.
    /// </summary>
    public int MedianManaSources { get; set; }

    /// <summary>
    /// Gets or sets the median nonland permanent count.
    /// </summary>
    public int MedianNonlandPermanents { get; set; }

    /// <summary>
    /// Gets or sets the median cards in hand.
    /// </summary>
    public int MedianCardsInHand { get; set; }

    /// <summary>
    /// Gets or sets the median battlefield power.
    /// </summary>
    public int MedianPower { get; set; }

    /// <summary>
    /// Gets or sets the median token count.
    /// </summary>
    public int MedianTokens { get; set; }

    /// <summary>
    /// Gets or sets a readable board summary.
    /// </summary>
    public string LikelyBoard { get; set; } = "";

    /// <summary>
    /// Gets or sets confidence in this projection.
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Reports a goldfish simulation.
/// </summary>
public sealed class GoldfishSimulationResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of simulations.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the target turn.
    /// </summary>
    public int TargetTurn { get; set; }

    /// <summary>
    /// Gets or sets the simple mulligan count.
    /// </summary>
    public int Mulligans { get; set; }

    /// <summary>
    /// Gets or sets turn-by-turn projections.
    /// </summary>
    public List<ProjectedTurnState> TurnSummaries { get; set; } = [];

    /// <summary>
    /// Gets or sets the win timing estimate.
    /// </summary>
    public WinTurnEstimate WinEstimate { get; set; } = new();

    /// <summary>
    /// Gets or sets representative play lines.
    /// </summary>
    public List<string> RepresentativeLines { get; set; } = [];

    /// <summary>
    /// Gets or sets simulator notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

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
