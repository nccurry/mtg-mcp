namespace MtgMcp.Core;

/// <summary>
/// Provides standard deck role names.
/// </summary>
public static class DeckRoles
{
    /// <summary>
    /// Stores the commander role.
    /// </summary>
    public const string Commander = "Commander";

    /// <summary>
    /// Stores the lands role.
    /// </summary>
    public const string Lands = "Lands";

    /// <summary>
    /// Stores the ramp role.
    /// </summary>
    public const string Ramp = "Ramp";

    /// <summary>
    /// Stores the draw role.
    /// </summary>
    public const string Draw = "Draw";

    /// <summary>
    /// Stores the tutors role.
    /// </summary>
    public const string Tutors = "Tutors";

    /// <summary>
    /// Stores the interaction role.
    /// </summary>
    public const string Interaction = "Interaction";

    /// <summary>
    /// Stores the board wipes role.
    /// </summary>
    public const string BoardWipes = "Board Wipes";

    /// <summary>
    /// Stores the protection role.
    /// </summary>
    public const string Protection = "Protection";

    /// <summary>
    /// Stores the recursion role.
    /// </summary>
    public const string Recursion = "Recursion";

    /// <summary>
    /// Stores the synergy role.
    /// </summary>
    public const string Synergy = "Synergy";

    /// <summary>
    /// Stores the payoffs role.
    /// </summary>
    public const string Payoffs = "Payoffs";

    /// <summary>
    /// Stores the wincons role.
    /// </summary>
    public const string Wincons = "Wincons";

    /// <summary>
    /// Stores the utility role.
    /// </summary>
    public const string Utility = "Utility";

    /// <summary>
    /// Stores the maybeboard role.
    /// </summary>
    public const string Maybeboard = "Maybeboard";

    /// <summary>
    /// Stores the primary role taxonomy.
    /// </summary>
    public static readonly IReadOnlyList<string> Primary =
    [
        Commander,
        Lands,
        Ramp,
        Draw,
        Tutors,
        Interaction,
        BoardWipes,
        Protection,
        Recursion,
        Synergy,
        Payoffs,
        Wincons,
        Utility,
        Maybeboard
    ];
}

/// <summary>
/// Provides standard secondary deck tags.
/// </summary>
public static class DeckTags
{
    /// <summary>
    /// Stores the discard tag.
    /// </summary>
    public const string Discard = "Discard";

    /// <summary>
    /// Stores the sacrifice outlet tag.
    /// </summary>
    public const string SacOutlet = "Sac Outlet";

    /// <summary>
    /// Stores the aristocrats tag.
    /// </summary>
    public const string Aristocrats = "Aristocrats";

    /// <summary>
    /// Stores the tokens tag.
    /// </summary>
    public const string Tokens = "Tokens";

    /// <summary>
    /// Stores the reanimation tag.
    /// </summary>
    public const string Reanimation = "Reanimation";

    /// <summary>
    /// Stores the graveyard hate tag.
    /// </summary>
    public const string GraveyardHate = "Graveyard Hate";

    /// <summary>
    /// Stores the stax tag.
    /// </summary>
    public const string Stax = "Stax";

    /// <summary>
    /// Stores the combo piece tag.
    /// </summary>
    public const string ComboPiece = "Combo Piece";

    /// <summary>
    /// Stores the mana fixing tag.
    /// </summary>
    public const string ManaFixing = "Mana Fixing";

    /// <summary>
    /// Stores the card selection tag.
    /// </summary>
    public const string CardSelection = "Card Selection";

    /// <summary>
    /// Stores the lifegain tag.
    /// </summary>
    public const string Lifegain = "Lifegain";

    /// <summary>
    /// Stores the drain tag.
    /// </summary>
    public const string Drain = "Drain";

    /// <summary>
    /// Stores the voltron tag.
    /// </summary>
    public const string Voltron = "Voltron";

    /// <summary>
    /// Stores the blink tag.
    /// </summary>
    public const string Blink = "Blink";

    /// <summary>
    /// Stores the mill tag.
    /// </summary>
    public const string Mill = "Mill";

    /// <summary>
    /// Stores the politics tag.
    /// </summary>
    public const string Politics = "Politics";

    /// <summary>
    /// Stores the secondary tag taxonomy.
    /// </summary>
    public static readonly IReadOnlyList<string> Secondary =
    [
        Discard,
        SacOutlet,
        Aristocrats,
        Tokens,
        Reanimation,
        GraveyardHate,
        Stax,
        ComboPiece,
        ManaFixing,
        CardSelection,
        Lifegain,
        Drain,
        Voltron,
        Blink,
        Mill,
        Politics
    ];
}

/// <summary>
/// Provides card role assignment behavior.
/// </summary>
public sealed class CardRoleAssignment
{
    /// <summary>
    /// Gets or sets the primary role.
    /// </summary>
    public string PrimaryRole { get; set; } = DeckRoles.Utility;

    /// <summary>
    /// Gets or sets the tags.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the confidence.
    /// </summary>
    public double Confidence { get; set; }
}

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
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
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

/// <summary>
/// Provides deck edit plan behavior.
/// </summary>
public sealed class DeckEditPlan
{
    /// <summary>
    /// Gets or sets the plan id.
    /// </summary>
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the workspace id.
    /// </summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public string Status { get; set; } = DeckEditPlanStatus.Draft;

    /// <summary>
    /// Gets or sets the persistence.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets the created at.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the applied at.
    /// </summary>
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>
    /// Gets or sets the checkpoint id.
    /// </summary>
    public string? CheckpointId { get; set; }

    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; set; } = "";

    /// <summary>
    /// Gets or sets the confidence.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets the warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets the operations.
    /// </summary>
    public List<DeckEditOperation> Operations { get; set; } = [];
}

/// <summary>
/// Provides deck edit operation behavior.
/// </summary>
public sealed class DeckEditOperation
{
    /// <summary>
    /// Gets or sets the edit operation name.
    /// </summary>
    public string Operation { get; set; } = "";

    /// <summary>
    /// Gets or sets the card name.
    /// </summary>
    public string? CardName { get; set; }

    /// <summary>
    /// Gets or sets the replacement card name.
    /// </summary>
    public string? ReplacementCardName { get; set; }

    /// <summary>
    /// Gets or sets the quantity.
    /// </summary>
    public int? Quantity { get; set; }

    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the from category.
    /// </summary>
    public string? FromCategory { get; set; }

    /// <summary>
    /// Gets or sets the to category.
    /// </summary>
    public string? ToCategory { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the format.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the included in deck.
    /// </summary>
    public bool? IncludedInDeck { get; set; }

    /// <summary>
    /// Gets or sets the included in price.
    /// </summary>
    public bool? IncludedInPrice { get; set; }

    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; set; } = "";
}

/// <summary>
/// Provides deck edit plan apply result behavior.
/// </summary>
public sealed class DeckEditPlanApplyResult
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
    /// Gets or sets the persistence.
    /// </summary>
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;

    /// <summary>
    /// Gets or sets the checkpoint id.
    /// </summary>
    public string? CheckpointId { get; set; }

    /// <summary>
    /// Gets or sets the applied operations.
    /// </summary>
    public int AppliedOperations { get; set; }

    /// <summary>
    /// Gets or sets the messages.
    /// </summary>
    public List<string> Messages { get; set; } = [];

    /// <summary>
    /// Gets or sets the workspace.
    /// </summary>
    public DeckWorkspace Workspace { get; set; } = new();
}

/// <summary>
/// Provides deck edit plan statuses.
/// </summary>
public static class DeckEditPlanStatus
{
    /// <summary>
    /// Stores the draft status.
    /// </summary>
    public const string Draft = "draft";

    /// <summary>
    /// Stores the applied status.
    /// </summary>
    public const string Applied = "applied";
}

/// <summary>
/// Provides deck edit operation names.
/// </summary>
public static class DeckEditOperations
{
    /// <summary>
    /// Stores the add card edit name.
    /// </summary>
    public const string AddCard = "add_card";

    /// <summary>
    /// Stores the remove card edit name.
    /// </summary>
    public const string RemoveCard = "remove_card";

    /// <summary>
    /// Stores the set card quantity edit name.
    /// </summary>
    public const string SetCardQuantity = "set_card_quantity";

    /// <summary>
    /// Stores the move card edit name.
    /// </summary>
    public const string MoveCard = "move_card";

    /// <summary>
    /// Stores the add card category edit name.
    /// </summary>
    public const string AddCardCategory = "add_card_category";

    /// <summary>
    /// Stores the remove card category edit name.
    /// </summary>
    public const string RemoveCardCategory = "remove_card_category";

    /// <summary>
    /// Stores the set primary card category edit name.
    /// </summary>
    public const string SetPrimaryCardCategory = "set_primary_card_category";

    /// <summary>
    /// Stores the create category edit name.
    /// </summary>
    public const string CreateCategory = "create_category";

    /// <summary>
    /// Stores the rename category edit name.
    /// </summary>
    public const string RenameCategory = "rename_category";

    /// <summary>
    /// Stores the delete category edit name.
    /// </summary>
    public const string DeleteCategory = "delete_category";

    /// <summary>
    /// Stores the update deck metadata edit name.
    /// </summary>
    public const string UpdateDeckMetadata = "update_deck_metadata";
}
