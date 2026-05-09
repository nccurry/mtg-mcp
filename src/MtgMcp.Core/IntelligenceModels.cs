namespace MtgMcp.Core;

public static class DeckRoles
{
    public const string Commander = "Commander";
    public const string Lands = "Lands";
    public const string Ramp = "Ramp";
    public const string Draw = "Draw";
    public const string Tutors = "Tutors";
    public const string Interaction = "Interaction";
    public const string BoardWipes = "Board Wipes";
    public const string Protection = "Protection";
    public const string Recursion = "Recursion";
    public const string Synergy = "Synergy";
    public const string Payoffs = "Payoffs";
    public const string Wincons = "Wincons";
    public const string Utility = "Utility";
    public const string Maybeboard = "Maybeboard";

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

public static class DeckTags
{
    public const string Discard = "Discard";
    public const string SacOutlet = "Sac Outlet";
    public const string Aristocrats = "Aristocrats";
    public const string Tokens = "Tokens";
    public const string Reanimation = "Reanimation";
    public const string GraveyardHate = "Graveyard Hate";
    public const string Stax = "Stax";
    public const string ComboPiece = "Combo Piece";
    public const string ManaFixing = "Mana Fixing";
    public const string CardSelection = "Card Selection";
    public const string Lifegain = "Lifegain";
    public const string Drain = "Drain";
    public const string Voltron = "Voltron";
    public const string Blink = "Blink";
    public const string Mill = "Mill";
    public const string Politics = "Politics";

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

public sealed class CardRoleAssignment
{
    public string PrimaryRole { get; set; } = DeckRoles.Utility;
    public List<string> Tags { get; set; } = [];
    public double Confidence { get; set; }
}

public sealed class DeckNormalizationResult
{
    public string WorkspaceId { get; set; } = "";
    public string Scope { get; set; } = "all";
    public int RequestedCards { get; set; }
    public int UpdatedCards { get; set; }
    public List<string> MissingCards { get; set; } = [];
    public DeckWorkspace Workspace { get; set; } = new();
}

public sealed class DeckPlanSummary
{
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;
    public int IncludedCards { get; set; }
    public int MaybeboardCards { get; set; }
    public List<string> Commanders { get; set; } = [];
    public Dictionary<string, int> RoleCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> TagCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CategoryMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Strengths { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> NextSteps { get; set; } = [];
}

public sealed class DeckOddsAnalysis
{
    public string WorkspaceId { get; set; } = "";
    public int DeckSize { get; set; }
    public int OpeningHandSize { get; set; }
    public int Turn { get; set; }
    public int CardsSeen { get; set; }
    public int Simulations { get; set; }
    public List<DeckOddsRow> Rows { get; set; } = [];
}

public sealed class DeckOddsRow
{
    public string Target { get; set; } = "";
    public int SuccessesInDeck { get; set; }
    public double HypergeometricAtLeastOne { get; set; }
    public double HypergeometricAtLeastTwo { get; set; }
    public double MonteCarloAtLeastOne { get; set; }
}

public sealed class ReplacementWeights
{
    public double Role { get; set; } = 0.45;
    public double Power { get; set; } = 0.30;
    public double Price { get; set; } = 0.25;
}

public sealed class ReplacementSuggestion
{
    public string ReplaceCard { get; set; } = "";
    public string WithCard { get; set; } = "";
    public string Role { get; set; } = DeckRoles.Utility;
    public double Score { get; set; }
    public double RoleScore { get; set; }
    public double PowerScore { get; set; }
    public double PriceScore { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? CandidatePrice { get; set; }
    public decimal? EstimatedSavings { get; set; }
    public string Rationale { get; set; } = "";
}

public sealed class RecommendationPlanResult
{
    public DeckEditPlan Plan { get; set; } = new();
    public List<ReplacementSuggestion> Suggestions { get; set; } = [];
}

public sealed class CategorySuggestion
{
    public string CardName { get; set; } = "";
    public string CurrentPrimaryCategory { get; set; } = "";
    public string SuggestedPrimaryRole { get; set; } = DeckRoles.Utility;
    public List<string> Tags { get; set; } = [];
    public double Confidence { get; set; }
}

public sealed class CategoryPlanResult
{
    public DeckEditPlan Plan { get; set; } = new();
    public List<CategorySuggestion> Suggestions { get; set; } = [];
}

public sealed class DeckEditPlan
{
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = DeckEditPlanStatus.Draft;
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AppliedAt { get; set; }
    public string? CheckpointId { get; set; }
    public string Rationale { get; set; } = "";
    public double Confidence { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<DeckEditOperation> Operations { get; set; } = [];
}

public sealed class DeckEditOperation
{
    public string Operation { get; set; } = "";
    public string? CardName { get; set; }
    public string? ReplacementCardName { get; set; }
    public int? Quantity { get; set; }
    public string? Category { get; set; }
    public string? FromCategory { get; set; }
    public string? ToCategory { get; set; }
    public string? Name { get; set; }
    public string? Format { get; set; }
    public string? Description { get; set; }
    public bool? IncludedInDeck { get; set; }
    public bool? IncludedInPrice { get; set; }
    public string Rationale { get; set; } = "";
}

public sealed class DeckEditPlanApplyResult
{
    public string PlanId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public string Persistence { get; set; } = DeckPersistence.LocalOnly;
    public string? CheckpointId { get; set; }
    public int AppliedOperations { get; set; }
    public List<string> Messages { get; set; } = [];
    public DeckWorkspace Workspace { get; set; } = new();
}

public static class DeckEditPlanStatus
{
    public const string Draft = "draft";
    public const string Applied = "applied";
}

public static class DeckEditOperations
{
    public const string AddCard = "add_card";
    public const string RemoveCard = "remove_card";
    public const string SetCardQuantity = "set_card_quantity";
    public const string MoveCard = "move_card";
    public const string AddCardCategory = "add_card_category";
    public const string RemoveCardCategory = "remove_card_category";
    public const string SetPrimaryCardCategory = "set_primary_card_category";
    public const string CreateCategory = "create_category";
    public const string RenameCategory = "rename_category";
    public const string DeleteCategory = "delete_category";
    public const string UpdateDeckMetadata = "update_deck_metadata";
}
