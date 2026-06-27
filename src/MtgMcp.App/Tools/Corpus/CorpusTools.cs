using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes source-backed recommendation and source-inspection MCP tools.
/// </summary>
[McpServerToolType]
public sealed class CorpusTools
{
    /// <summary>
    /// Creates source-backed recommendation reports.
    /// </summary>
    private readonly DeckRecommendationService recommendations;

    /// <summary>
    /// Supplies default source-backed recommendation settings.
    /// </summary>
    private readonly IOptions<MtgMcpOptions> options;

    /// <summary>
    /// Creates recommendation source MCP tools from recommendation services and host options.
    /// </summary>
    public CorpusTools(
        DeckRecommendationService recommendations,
        IOptions<MtgMcpOptions> options)
    {
        this.recommendations = recommendations;
        this.options = options;
    }

    /// <summary>
    /// Analyzes commander and deck-context trends from enabled recommendation sources.
    /// </summary>
    [McpServerTool(Name = "deck_analyze_commander_trends", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Analyze commander or deck-context card trends using enabled API-backed recommendation sources. AnalysisDepth can be minimal, balanced, or best; bypassCache bypasses source-fact cache. Use source_search_evidence when raw deterministic source rows are needed instead of ranked recommendations.")]
    public Task<CorpusRecommendationResult> AnalyzeCommanderTrendsAsync(
        string workspaceId,
        int limit = 10,
        [Description("Recommendation source analysis depth: minimal, balanced, or best.")]
        string? analysisDepth = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.AnalyzeCommanderTrendsAsync(
            workspaceId,
            limit,
            EffectiveAnalysisDepth(analysisDepth),
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Finds lower-known cards with useful source evidence for a deck goal.
    /// </summary>
    [McpServerTool(Name = "deck_find_lesser_known_cards", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find lower-known cards that fit a deck goal using API-backed source evidence. AnalysisDepth can be minimal, balanced, or best; bypassCache bypasses source-fact cache. Use source-specific evidence tools before mutating a deck.")]
    public Task<CorpusRecommendationResult> FindLesserKnownCardsAsync(
        string workspaceId,
        string goal = "",
        int limit = 10,
        decimal? maxPrice = null,
        [Description("Recommendation source analysis depth: minimal, balanced, or best.")]
        string? analysisDepth = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.FindLesserKnownCardsAsync(
            workspaceId,
            goal,
            limit,
            maxPrice,
            EffectiveAnalysisDepth(analysisDepth),
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Finds high-signal exemplar decks from enabled recommendation sources.
    /// </summary>
    [McpServerTool(Name = "deck_find_exemplar_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find top exemplar decks for a deck context from enabled API-backed recommendation sources. AnalysisDepth can be minimal, balanced, or best; bypassCache bypasses source-fact cache.")]
    public Task<TopExemplarDecksResult> FindTopExemplarDecksAsync(
        string workspaceId,
        int limit = 10,
        [Description("Recommendation source analysis depth: minimal, balanced, or best.")]
        string? analysisDepth = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.FindTopExemplarDecksAsync(
            workspaceId,
            limit,
            EffectiveAnalysisDepth(analysisDepth),
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Explains source evidence for one card in a deck context.
    /// </summary>
    [McpServerTool(Name = "source_explain_card_signal", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Explain why enabled API-backed recommendation sources do or do not support one card in this deck context. AnalysisDepth can be minimal, balanced, or best; bypassCache bypasses source-fact cache.")]
    public Task<CorpusRecommendationResult> ExplainCardCorpusSignalAsync(
        string workspaceId,
        string cardName,
        [Description("Recommendation source analysis depth: minimal, balanced, or best.")]
        string? analysisDepth = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.ExplainCardCorpusSignalAsync(
            workspaceId,
            cardName,
            EffectiveAnalysisDepth(analysisDepth),
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Searches one recommendation source for raw evidence rows.
    /// </summary>
    [McpServerTool(Name = "source_search_evidence", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Search one enabled recommendation source by sourceKey and return deterministic card evidence rows plus raw discussions or exemplar decks. This tool does not infer card quality or choose cuts.")]
    public Task<CorpusEvidenceSearchResult> SearchCorpusEvidenceAsync(
        string workspaceId,
        [Description("Recommendation source key such as edhrec, edhtop16, or topdeck.")]
        string sourceKey,
        string goal = "",
        int limit = 20,
        [Description("Recommendation source analysis depth: minimal, balanced, or best.")]
        string? analysisDepth = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.SearchCorpusEvidenceAsync(
            workspaceId,
            sourceKey,
            goal,
            limit,
            EffectiveAnalysisDepth(analysisDepth),
            bypassCache,
            cancellationToken);
    }

    /// <summary>
    /// Lists configured recommendation source providers.
    /// </summary>
    [McpServerTool(Name = "source_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List configured deck recommendation source providers with enablement, API stability, configuration, and permission notes.")]
    public CorpusSourceStatusResult ListCorpusSources()
    {
        return recommendations.ListCorpusSources();
    }

    /// <summary>
    /// Uses the per-call analysis depth when provided, otherwise the configured host default.
    /// </summary>
    private string EffectiveAnalysisDepth(string? analysisDepth)
    {
        return string.IsNullOrWhiteSpace(analysisDepth)
            ? options.Value.Intelligence.AnalysisDepth
            : analysisDepth;
    }
}
