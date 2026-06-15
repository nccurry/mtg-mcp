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
        Maybeboard,
        Commander,
        Lands,
        Ramp,
        Draw,
        Tutors,
        BoardWipes,
        Interaction,
        Protection,
        Recursion,
        Wincons,
        Payoffs,
        Synergy,
        Utility
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
    /// Stores the Food token tag.
    /// </summary>
    public const string Food = "Food";

    /// <summary>
    /// Stores the artifact token tag.
    /// </summary>
    public const string ArtifactTokens = "Artifact Tokens";

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
    /// Stores the table interaction tag.
    /// </summary>
    public const string TableInteraction = "Table Interaction";

    /// <summary>
    /// Stores the go-wide protection tag.
    /// </summary>
    public const string GoWideProtection = "Go-Wide Protection";

    /// <summary>
    /// Stores the pillowfort tag.
    /// </summary>
    public const string Pillowfort = "Pillowfort";

    /// <summary>
    /// Stores the token hate tag.
    /// </summary>
    public const string TokenHate = "Token Hate";

    /// <summary>
    /// Stores the artifact and enchantment hate tag.
    /// </summary>
    public const string ArtifactEnchantmentHate = "Artifact/Enchantment Hate";

    /// <summary>
    /// Stores the combat protection tag.
    /// </summary>
    public const string CombatProtection = "Combat Protection";

    /// <summary>
    /// Stores the combat payoff tag.
    /// </summary>
    public const string CombatPayoff = "Combat Payoff";

    /// <summary>
    /// Stores the evasion tag.
    /// </summary>
    public const string Evasion = "Evasion";

    /// <summary>
    /// Stores the finisher tag.
    /// </summary>
    public const string Finishers = "Finishers";

    /// <summary>
    /// Stores the sacrifice fodder tag.
    /// </summary>
    public const string SacrificeFodder = "Sacrifice Fodder";

    /// <summary>
    /// Stores the engine tag.
    /// </summary>
    public const string Engines = "Engines";

    /// <summary>
    /// Stores the combo enabler tag.
    /// </summary>
    public const string ComboEnabler = "Combo Enabler";

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
        Food,
        ArtifactTokens,
        Drain,
        Voltron,
        Blink,
        Mill,
        Politics,
        TableInteraction,
        GoWideProtection,
        Pillowfort,
        TokenHate,
        ArtifactEnchantmentHate,
        CombatProtection,
        CombatPayoff,
        Evasion,
        Finishers,
        SacrificeFodder,
        Engines,
        ComboEnabler
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
    /// Gets or sets additive functional roles used when one card serves multiple deck jobs.
    /// </summary>
    public List<string> FunctionalRoles { get; set; } = [];

    /// <summary>
    /// Gets or sets the confidence.
    /// </summary>
    public double Confidence { get; set; }
}
