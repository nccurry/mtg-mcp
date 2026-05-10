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
    /// Handles card tools.
    /// </summary>
    public CardTools(ICardCatalog cards)
    {
        this.cards = cards;
    }

    /// <summary>
    /// Searches the cards.
    /// </summary>
    [McpServerTool(
        Name = "search_cards",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Search Scryfall cards using normal Scryfall search syntax.")]
    public Task<IReadOnlyList<CardSearchResult>> SearchCardsAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default
    )
    {
        return cards.SearchCardsAsync(query, limit, cancellationToken);
    }

    /// <summary>
    /// Gets the card.
    /// </summary>
    [McpServerTool(
        Name = "get_card",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get a single card by Scryfall id or fuzzy card name.")]
    public Task<CardInfo?> GetCardAsync(
        string nameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetCardAsync(nameOrId, cancellationToken);
    }

    /// <summary>
    /// Gets the rulings.
    /// </summary>
    [McpServerTool(
        Name = "get_rulings",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get Scryfall rulings for a card.")]
    public Task<IReadOnlyList<RulingInfo>> GetRulingsAsync(
        string nameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetRulingsAsync(nameOrId, cancellationToken);
    }

    /// <summary>
    /// Gets the prints.
    /// </summary>
    [McpServerTool(
        Name = "get_prints",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Get known Scryfall prints for a card.")]
    public Task<IReadOnlyList<CardInfo>> GetPrintsAsync(
        string nameOrId,
        CancellationToken cancellationToken = default
    )
    {
        return cards.GetPrintsAsync(nameOrId, cancellationToken);
    }

    /// <summary>
    /// Suggests the cards.
    /// </summary>
    [McpServerTool(
        Name = "suggest_cards",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true
    )]
    [Description("Suggest cards using a Scryfall query plus an optional format constraint.")]
    public Task<IReadOnlyList<CardSearchResult>> SuggestCardsAsync(
        string prompt,
        string? format = null,
        int limit = 10,
        CancellationToken cancellationToken = default
    )
    {
        return cards.SuggestCardsAsync(prompt, format, limit, cancellationToken);
    }
}
