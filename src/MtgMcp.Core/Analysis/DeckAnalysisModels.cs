namespace MtgMcp.Core;

/// <summary>
/// Provides deck normalization result behavior.
/// </summary>
public sealed class DeckNormalizationResult
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the scope.
    /// </summary>
    public string Scope { get; set; } = "all";

    /// <summary>
    /// Gets or sets the requested cards.
    /// </summary>
    public int RequestedCards { get; set; }

    /// <summary>
    /// Gets or sets the updated cards.
    /// </summary>
    public int UpdatedCards { get; set; }

    /// <summary>
    /// Gets or sets the missing cards.
    /// </summary>
    public List<string> MissingCards { get; set; } = [];

    /// <summary>
    /// Gets or sets the workspace.
    /// </summary>
    public DeckWorkspace Workspace { get; set; } = new();
}

/// <summary>
/// Provides deck plan summary behavior.
/// </summary>
public sealed class DeckPlanSummary
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the format.
    /// </summary>
    public string Format { get; set; } = "";

    /// <summary>
    /// Gets or sets the persistence.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets the included cards.
    /// </summary>
    public int IncludedCards { get; set; }

    /// <summary>
    /// Gets or sets the maybeboard cards.
    /// </summary>
    public int MaybeboardCards { get; set; }

    /// <summary>
    /// Gets or sets the commanders.
    /// </summary>
    public List<string> Commanders { get; set; } = [];

    /// <summary>
    /// Gets or sets the role counts.
    /// </summary>
    public Dictionary<string, int> RoleCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the tag counts.
    /// </summary>
    public Dictionary<string, int> TagCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the category map.
    /// </summary>
    public Dictionary<string, string> CategoryMap { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the parsed deck intent.
    /// </summary>
    public DeckIntent? Intent { get; set; }

    /// <summary>
    /// Gets or sets notes about how intent influenced the summary.
    /// </summary>
    public List<string> IntentNotes { get; set; } = [];

    /// <summary>
    /// Gets or sets the strengths.
    /// </summary>
    public List<string> Strengths { get; set; } = [];

    /// <summary>
    /// Gets or sets the risks.
    /// </summary>
    public List<string> Risks { get; set; } = [];

    /// <summary>
    /// Gets or sets the next steps.
    /// </summary>
    public List<string> NextSteps { get; set; } = [];
}

/// <summary>
/// Provides deck odds analysis behavior.
/// </summary>
public sealed class DeckOddsAnalysis
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck size.
    /// </summary>
    public int DeckSize { get; set; }

    /// <summary>
    /// Gets or sets the opening hand size.
    /// </summary>
    public int OpeningHandSize { get; set; }

    /// <summary>
    /// Gets or sets the turn.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets the cards seen.
    /// </summary>
    public int CardsSeen { get; set; }

    /// <summary>
    /// Gets or sets the simulations.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the rows.
    /// </summary>
    public List<DeckOddsRow> Rows { get; set; } = [];
}

/// <summary>
/// Provides deck odds row behavior.
/// </summary>
public sealed class DeckOddsRow
{
    /// <summary>
    /// Gets or sets the target.
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// Gets or sets the successes in deck.
    /// </summary>
    public int SuccessesInDeck { get; set; }

    /// <summary>
    /// Gets or sets the at least one hypergeometric odds.
    /// </summary>
    public double HypergeometricAtLeastOne { get; set; }

    /// <summary>
    /// Gets or sets the at least two hypergeometric odds.
    /// </summary>
    public double HypergeometricAtLeastTwo { get; set; }

    /// <summary>
    /// Gets or sets the at least one Monte Carlo odds.
    /// </summary>
    public double MonteCarloAtLeastOne { get; set; }
}

/// <summary>
/// Reports turn-by-turn land drop odds for a deck.
/// </summary>
public sealed class LandDropOddsAnalysis
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck size used for the calculation.
    /// </summary>
    public int DeckSize { get; set; }

    /// <summary>
    /// Gets or sets the number of cards treated as lands or land slots.
    /// </summary>
    public int LandCount { get; set; }

    /// <summary>
    /// Gets or sets the number of effective early land sources.
    /// </summary>
    public int EffectiveLandSources { get; set; }

    /// <summary>
    /// Gets or sets the requested target turn.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets whether the simulation is on the play.
    /// </summary>
    public bool OnThePlay { get; set; }

    /// <summary>
    /// Gets or sets whether deterministic mulligan simulation was included.
    /// </summary>
    public bool IncludeMulligans { get; set; }

    /// <summary>
    /// Gets or sets the number of Monte Carlo simulations used.
    /// </summary>
    public int Simulations { get; set; }

    /// <summary>
    /// Gets or sets the turn-by-turn odds rows.
    /// </summary>
    public List<LandDropOddsRow> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets deterministic assumptions used by the calculation.
    /// </summary>
    public List<string> Assumptions { get; set; } = [];

    /// <summary>
    /// Gets or sets likely reasons for missed land drops.
    /// </summary>
    public List<string> FailureDrivers { get; set; } = [];
}

/// <summary>
/// Reports land drop odds for one turn.
/// </summary>
public sealed class LandDropOddsRow
{
    /// <summary>
    /// Gets or sets the turn being evaluated.
    /// </summary>
    public int Turn { get; set; }

    /// <summary>
    /// Gets or sets the number of cards seen by this turn.
    /// </summary>
    public int CardsSeen { get; set; }

    /// <summary>
    /// Gets or sets exact no-mulligan odds of making this land drop.
    /// </summary>
    public double HypergeometricMakeLandDrop { get; set; }

    /// <summary>
    /// Gets or sets exact no-mulligan odds of missing this land drop.
    /// </summary>
    public double HypergeometricMissLandDrop { get; set; }

    /// <summary>
    /// Gets or sets deterministic Monte Carlo odds with the configured mulligan assumption.
    /// </summary>
    public double MonteCarloMakeLandDrop { get; set; }

    /// <summary>
    /// Gets or sets deterministic Monte Carlo miss odds.
    /// </summary>
    public double MonteCarloMissLandDrop { get; set; }
}

/// <summary>
/// Provides deck cost analysis behavior.
/// </summary>
public sealed class DeckCostAnalysis
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the included deck total.
    /// </summary>
    public decimal IncludedTotal { get; set; }

    /// <summary>
    /// Optional budget ceiling used to classify the known included total.
    /// </summary>
    public decimal? MaxBudget { get; set; }

    /// <summary>
    /// Indicates whether known included prices fit under the requested budget when one was supplied.
    /// </summary>
    public bool? WithinBudget { get; set; }

    /// <summary>
    /// Difference between max budget and known included total; positive values are remaining budget.
    /// </summary>
    public decimal? BudgetDelta { get; set; }

    /// <summary>
    /// Compact budget status such as unknown, under-budget, at-budget, or over-budget.
    /// </summary>
    public string BudgetStatus { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the maybeboard total.
    /// </summary>
    public decimal MaybeboardTotal { get; set; }

    /// <summary>
    /// Gets or sets the priced included cards.
    /// </summary>
    public int PricedIncludedCards { get; set; }

    /// <summary>
    /// Gets or sets the missing price cards.
    /// </summary>
    public List<string> MissingPriceCards { get; set; } = [];

    /// <summary>
    /// Basic land cards missing cached prices; these do not increase budget uncertainty.
    /// </summary>
    public List<string> BasicMissingPriceCards { get; set; } = [];

    /// <summary>
    /// Nonbasic cards missing cached prices; these keep budget status uncertain.
    /// </summary>
    public List<string> NonBasicMissingPriceCards { get; set; } = [];

    /// <summary>
    /// Budget and pricing caveats that should increase, not reduce, confidence risk.
    /// </summary>
    public List<string> PriceRiskNotes { get; set; } = [];

    /// <summary>
    /// Gets or sets the top cost drivers.
    /// </summary>
    public List<DeckCostDriver> TopCostDrivers { get; set; } = [];
}

/// <summary>
/// Provides deck cost driver behavior.
/// </summary>
public sealed class DeckCostDriver
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// Gets or sets the quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the unit price.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the total price.
    /// </summary>
    public decimal TotalPrice { get; set; }
}

/// <summary>
/// Provides a compact snapshot of deck metrics.
/// </summary>
public sealed class DeckMetricSnapshot
{
    /// <summary>
    /// Gets or sets the cost.
    /// </summary>
    public DeckCostAnalysis Cost { get; set; } = new();

    /// <summary>
    /// Gets or sets the validation result.
    /// </summary>
    public DeckValidationResult Validation { get; set; } = new();

    /// <summary>
    /// Gets or sets the deck analysis.
    /// </summary>
    public DeckAnalysis Analysis { get; set; } = new();

    /// <summary>
    /// Gets or sets the mana base analysis.
    /// </summary>
    public ManaBaseAnalysis ManaBase { get; set; } = new();

    /// <summary>
    /// Gets or sets the consistency analysis.
    /// </summary>
    public DeckConsistencyAnalysis Consistency { get; set; } = new();

    /// <summary>
    /// Gets or sets the commander bracket estimate.
    /// </summary>
    public CommanderBracketEstimate Bracket { get; set; } = new();
}

/// <summary>
/// Provides deck edit plan preview behavior.
/// </summary>
public sealed class DeckPlanPreviewResult
{
    /// <summary>
    /// Gets or sets the plan id.
    /// </summary>
    public string PlanId { get; set; } = "";

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether added cards were resolved with Scryfall data.
    /// </summary>
    public bool ResolveAddedCards { get; set; }

    /// <summary>
    /// Gets or sets the before snapshot.
    /// </summary>
    public DeckMetricSnapshot Before { get; set; } = new();

    /// <summary>
    /// Gets or sets the after snapshot.
    /// </summary>
    public DeckMetricSnapshot After { get; set; } = new();

    /// <summary>
    /// Gets or sets the preview warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Provides a Commander bracket estimate.
/// </summary>
public sealed class CommanderBracketEstimate
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the estimated bracket.
    /// </summary>
    public int EstimatedBracket { get; set; } = 1;

    /// <summary>
    /// Gets or sets the bracket floor from hard signals.
    /// </summary>
    public int BracketFloor { get; set; } = 1;

    /// <summary>
    /// Gets or sets the estimate confidence.
    /// </summary>
    public double Confidence { get; set; } = 0.35;

    /// <summary>
    /// Gets or sets the number of Game Changers found.
    /// </summary>
    public int GameChangerCount { get; set; }

    /// <summary>
    /// Gets or sets the Game Changer card names found in the deck.
    /// </summary>
    public List<string> GameChangers { get; set; } = [];

    /// <summary>
    /// Gets or sets the bracket signals.
    /// </summary>
    public List<BracketSignal> Signals { get; set; } = [];

    /// <summary>
    /// Gets or sets the data source.
    /// </summary>
    public string Source { get; set; } = "Scryfall is:game-changer";

    /// <summary>
    /// Gets or sets estimate notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Provides a bracket signal.
/// </summary>
public sealed class BracketSignal
{
    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string CardName { get; set; } = "";

    /// <summary>
    /// Gets or sets the signal.
    /// </summary>
    public string Signal { get; set; } = "";

    /// <summary>
    /// Gets or sets the severity.
    /// </summary>
    public int Severity { get; set; }

    /// <summary>
    /// Gets or sets the suggested bracket.
    /// </summary>
    public int SuggestedBracket { get; set; }

    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Provides mana base analysis behavior.
/// </summary>
public sealed class ManaBaseAnalysis
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the land count.
    /// </summary>
    public int LandCount { get; set; }

    /// <summary>
    /// Gets or sets cards whose primary category is a land slot.
    /// </summary>
    public int LandSlotCount { get; set; }

    /// <summary>
    /// Gets or sets land slots that have a nonland front face and land back face.
    /// </summary>
    public int ModalDoubleFacedLandCount { get; set; }

    /// <summary>
    /// Gets or sets land-role cards with inferred produced mana.
    /// </summary>
    public int ManaProducingLandCount { get; set; }

    /// <summary>
    /// Gets or sets the color source counts.
    /// </summary>
    public Dictionary<string, int> ColorSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the produced mana source counts.
    /// </summary>
    public Dictionary<string, int> ProducedManaSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the tapped land count.
    /// </summary>
    public int TappedLandCount { get; set; }

    /// <summary>
    /// Lands that appear to always enter tapped.
    /// </summary>
    public int AlwaysTappedLandCount { get; set; }

    /// <summary>
    /// Lands that may enter untapped when a condition is met.
    /// </summary>
    public int ConditionalTappedLandCount { get; set; }

    /// <summary>
    /// Gets or sets the untapped land count.
    /// </summary>
    public int UntappedLandCount { get; set; }

    /// <summary>
    /// Gets or sets the fixing count.
    /// </summary>
    public int FixingCount { get; set; }

    /// <summary>
    /// Gets or sets the ramp fixing count.
    /// </summary>
    public int RampFixingCount { get; set; }

    /// <summary>
    /// Gets or sets mana base risks.
    /// </summary>
    public List<string> Risks { get; set; } = [];

    /// <summary>
    /// Gets or sets mana base notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>
/// Provides deck consistency analysis behavior.
/// </summary>
public sealed class DeckConsistencyAnalysis
{
    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the deck size.
    /// </summary>
    public int DeckSize { get; set; }

    /// <summary>
    /// Gets or sets the ramp count.
    /// </summary>
    public int RampCount { get; set; }

    /// <summary>
    /// Gets or sets the draw count.
    /// </summary>
    public int DrawCount { get; set; }

    /// <summary>
    /// Gets or sets the tutor count.
    /// </summary>
    public int TutorCount { get; set; }

    /// <summary>
    /// Gets or sets the card selection count.
    /// </summary>
    public int CardSelectionCount { get; set; }

    /// <summary>
    /// Gets or sets the low curve nonland count.
    /// </summary>
    public int LowCurveNonlandCount { get; set; }

    /// <summary>
    /// Gets or sets key draw odds.
    /// </summary>
    public DeckOddsAnalysis KeyOdds { get; set; } = new();

    /// <summary>
    /// Gets or sets consistency risks.
    /// </summary>
    public List<string> Risks { get; set; } = [];

    /// <summary>
    /// Gets or sets consistency notes.
    /// </summary>
    public List<string> Notes { get; set; } = [];
}
