using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes deterministic deck data lookup MCP tools.
/// </summary>
[McpServerToolType]
public sealed class RecommendationTools
{
    /// <summary>
    /// Creates recommendation reports from source data.
    /// </summary>
    private readonly DeckRecommendationService recommendations;

    /// <summary>
    /// Creates recommendation tools for the MCP surface.
    /// </summary>
    public RecommendationTools(DeckRecommendationService recommendations)
    {
        this.recommendations = recommendations;
    }

    /// <summary>
    /// Compares a deck to Commander metagame context.
    /// </summary>
    [McpServerTool(Name = "compare_to_commander_meta", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Compare a deck to configured Commander source data. Scryfall-backed results are global EDHREC-rank facts, not commander-specific inclusion estimates.")]
    public Task<CommanderMetaReport> CompareToCommanderMetaAsync(
        string workspaceId,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        return recommendations.CompareToCommanderMetaAsync(workspaceId, limit, cancellationToken);
    }

    /// <summary>
    /// Finds newly released cards that fit a deck.
    /// </summary>
    [McpServerTool(Name = "find_new_cards_for_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find newly released cards matching deck format, color identity, release filters, and source facts. Since accepts YYYY-MM-DD and defaults to the last year.")]
    public Task<NewCardsForDeckResult> FindNewCardsForDeckAsync(
        string workspaceId,
        string? since = null,
        string? setCode = null,
        int limit = 10,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        return recommendations.FindNewCardsForDeckAsync(workspaceId, since, setCode, limit, maxPrice, cancellationToken);
    }

    /// <summary>
    /// Gets cards from an agent-supplied Scryfall query using deterministic deck-aware filters.
    /// </summary>
    [McpServerTool(Name = "query_cards_for_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Return source-order cards from a caller-supplied Scryfall query after deck legality, color identity, budget, required role/tag, excluded role/tag, and duplicate filters. No fit scores or plan is created.")]
    public Task<DeckQueryDataResult> QueryCardsForDeckAsync(
        string workspaceId,
        string goal,
        string scryfallQuery,
        int count = 10,
        decimal? maxPrice = null,
        string[]? requiredRoles = null,
        string[]? requiredTags = null,
        string[]? excludedRoles = null,
        string[]? excludedTags = null,
        CancellationToken cancellationToken = default)
    {
        return recommendations.QueryCardsForDeckAsync(
            workspaceId,
            goal,
            scryfallQuery,
            count,
            maxPrice,
            requiredRoles,
            requiredTags,
            excludedRoles,
            excludedTags,
            cancellationToken);
    }

}
