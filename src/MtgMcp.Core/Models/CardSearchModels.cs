namespace MtgMcp.Core;

/// <summary>
/// Names card-search intents that Core can request without knowing adapter query syntax.
/// </summary>
public enum CardSearchPreset
{
    /// <summary>
    /// Uses caller-supplied provider syntax.
    /// </summary>
    RawQuery,

    /// <summary>
    /// Finds Commander Game Changer cards.
    /// </summary>
    CommanderGameChangers,

    /// <summary>
    /// Finds legal legendary commander candidates.
    /// </summary>
    CommanderCandidates,

    /// <summary>
    /// Finds cards by deck role or tag.
    /// </summary>
    Role,

    /// <summary>
    /// Finds equipment and permanents that protect a commander.
    /// </summary>
    CommanderProtectionEquipment,

    /// <summary>
    /// Finds spells that protect a commander or key creature.
    /// </summary>
    CommanderProtectionSpell,

    /// <summary>
    /// Finds cards that bridge draw and discard plans.
    /// </summary>
    DrawDiscard,

    /// <summary>
    /// Finds general card-draw effects.
    /// </summary>
    CardDraw,

    /// <summary>
    /// Finds discard enablers and payoffs.
    /// </summary>
    DiscardSynergy,

    /// <summary>
    /// Finds political choice-making effects.
    /// </summary>
    PoliticalChoices,

    /// <summary>
    /// Finds table-wide political effects.
    /// </summary>
    PoliticalTableEffects,

    /// <summary>
    /// Finds political effects that involve the whole table.
    /// </summary>
    WholeTablePolitics,

    /// <summary>
    /// Finds effects that touch all players or many permanents.
    /// </summary>
    WholeTableEffects,

    /// <summary>
    /// Finds broad table-wide interaction.
    /// </summary>
    TableWideInteraction,

    /// <summary>
    /// Finds sweepers against token or go-wide boards.
    /// </summary>
    TokenDefenseSweepers,

    /// <summary>
    /// Finds pillowfort effects against token or go-wide boards.
    /// </summary>
    TokenDefensePillowfort,

    /// <summary>
    /// Finds graveyard hate.
    /// </summary>
    GraveyardHate,

    /// <summary>
    /// Finds cards that help close games.
    /// </summary>
    Finishers,

    /// <summary>
    /// Finds lower-pressure value cards.
    /// </summary>
    LessSaltyValue,

    /// <summary>
    /// Finds broadly useful cards with minimal role guidance.
    /// </summary>
    BroadUseful,

    /// <summary>
    /// Finds fallback staples across draw, interaction, and mana.
    /// </summary>
    BroadUsefulFallback,

    /// <summary>
    /// Finds recently released cards.
    /// </summary>
    RecentCards
}

/// <summary>
/// Describes a card search in deckbuilding terms instead of provider-specific query syntax.
/// </summary>
public sealed class CardSearchRequest
{
    /// <summary>
    /// Gets or initializes the kind of search the catalog should perform.
    /// </summary>
    public CardSearchPreset Preset { get; init; }

    /// <summary>
    /// Gets or initializes a caller-supplied provider query when the request explicitly uses raw syntax.
    /// </summary>
    public string? RawQuery { get; init; }

    /// <summary>
    /// Gets or initializes the format used for legality filtering.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// Gets or initializes the role or tag to search for when Preset is Role.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Gets or initializes the maximum card price for catalog-side filtering when available.
    /// </summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>
    /// Color identity filter for commander candidate discovery, in WUBRG order when supplied.
    /// </summary>
    public string? ColorIdentity { get; init; }

    /// <summary>
    /// True when candidate color identity must exactly match the requested colors.
    /// </summary>
    public bool ExactColorIdentity { get; init; }

    /// <summary>
    /// Gets or initializes the earliest release date for recent-card searches.
    /// </summary>
    public DateOnly? Since { get; init; }

    /// <summary>
    /// Gets or initializes an optional set code for recent-card searches.
    /// </summary>
    public string? SetCode { get; init; }

    /// <summary>
    /// Gets or initializes an optional theme used to refine recent-card searches.
    /// </summary>
    public string? Theme { get; init; }

    /// <summary>
    /// Creates a request for a caller-supplied provider query.
    /// </summary>
    public static CardSearchRequest Raw(string query, string? format = null, decimal? maxPrice = null)
    {
        return new CardSearchRequest
        {
            Preset = CardSearchPreset.RawQuery,
            RawQuery = query,
            Format = format,
            MaxPrice = maxPrice
        };
    }

    /// <summary>
    /// Creates a request for cards that fill a role or tag.
    /// </summary>
    public static CardSearchRequest ForRole(string role, string? format, decimal? maxPrice = null)
    {
        return new CardSearchRequest
        {
            Preset = CardSearchPreset.Role,
            Role = role,
            Format = format,
            MaxPrice = maxPrice
        };
    }

    /// <summary>
    /// Creates a request from a named preset with common deckbuilding filters.
    /// </summary>
    public static CardSearchRequest ForPreset(
        CardSearchPreset preset,
        string? format = null,
        decimal? maxPrice = null)
    {
        return new CardSearchRequest
        {
            Preset = preset,
            Format = format,
            MaxPrice = maxPrice
        };
    }

    /// <summary>
    /// Creates a bounded commander-candidate search request.
    /// </summary>
    public static CardSearchRequest CommanderCandidates(
        string? colorIdentity,
        bool exactColorIdentity,
        string? format = "commander")
    {
        return new CardSearchRequest
        {
            Preset = CardSearchPreset.CommanderCandidates,
            Format = format,
            ColorIdentity = colorIdentity,
            ExactColorIdentity = exactColorIdentity
        };
    }

    /// <summary>
    /// Creates a recent-card request using release metadata and optional theme hints.
    /// </summary>
    public static CardSearchRequest Recent(CardTrendQuery query)
    {
        return new CardSearchRequest
        {
            Preset = CardSearchPreset.RecentCards,
            Format = query.Format,
            MaxPrice = query.MaxPrice,
            Since = query.Since,
            SetCode = query.SetCode,
            Theme = query.Theme
        };
    }
}
