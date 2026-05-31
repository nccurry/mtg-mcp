using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes MCP tools for card.
/// </summary>
[McpServerToolType]
public sealed class CardTools
{
    /// <summary>
    /// Stores the cards.
    /// </summary>
    private readonly ICardCatalog cards;

    /// <summary>
    /// Creates the MCP card lookup tool group.
    /// </summary>
    public CardTools(ICardCatalog cards)
    {
        this.cards = cards;
    }

    /// <summary>
    /// Searches the cards.
    /// </summary>
    [McpServerTool(
        Name = "card_search",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Search Scryfall cards using normal Scryfall search syntax, with an optional format legality constraint.")]
    public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
        string query,
        [Description("Optional Scryfall legality format such as commander, modern, standard, or pauper.")]
        string? format = null,
        int limit = 10,
        CancellationToken cancellationToken = default
    )
    {
        return string.IsNullOrWhiteSpace(format)
            ? cards.SearchCardsAsync(query, limit, cancellationToken)
            : cards.SuggestCardsAsync(query, format, limit, cancellationToken);
    }

    /// <summary>
    /// Returns a card by Scryfall id or fuzzy name.
    /// </summary>
    [McpServerTool(
        Name = "card_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get a single card by Scryfall id or fuzzy card name.")]
    public Task<CardInfo?> GetCardAsync(
        string cardNameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetCardAsync(cardNameOrId, cancellationToken);
    }

    /// <summary>
    /// Returns official Scryfall rulings for a card.
    /// </summary>
    [McpServerTool(
        Name = "card_get_rulings",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get Scryfall rulings for a card.")]
    public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
        string cardNameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetRulingsAsync(cardNameOrId, cancellationToken);
    }

    /// <summary>
    /// Returns known Scryfall prints for a card.
    /// </summary>
    [McpServerTool(
        Name = "card_get_prints",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get known Scryfall prints for a card.")]
    public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
        string cardNameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetPrintsAsync(cardNameOrId, cancellationToken);
    }
}
