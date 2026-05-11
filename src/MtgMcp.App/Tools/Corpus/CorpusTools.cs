using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes corpus-backed recommendation and source-inspection MCP tools.
/// </summary>
[McpServerToolType]
public sealed class CorpusTools
{
    /// <summary>
    /// Creates corpus-backed recommendation reports.
    /// </summary>
    private readonly DeckRecommendationService recommendations;

    /// <summary>
    /// Guards corpus tools that persist planning state.
    /// </summary>
    private readonly OperationModeGuard operationMode;

    /// <summary>
    /// Supplies default corpus analysis settings.
    /// </summary>
    private readonly IOptions<MtgMcpOptions> options;

    /// <summary>
    /// Creates corpus MCP tools from recommendation services and host options.
    /// </summary>
    public CorpusTools(
        DeckRecommendationService recommendations,
        OperationModeGuard operationMode,
        IOptions<MtgMcpOptions> options)
    {
        this.recommendations = recommendations;
        this.operationMode = operationMode;
        this.options = options;
    }

    /// <summary>
    /// Analyzes commander and deck-context trends from enabled corpus providers.
    /// </summary>
    [McpServerTool(Name = "analyze_commander_trends", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Analyze commander or deck-context card trends using enabled API-backed corpus providers. AnalysisDepth can be minimal, balanced, or best; refresh bypasses source-fact cache.")]
    public Task<CorpusRecommendationResult> AnalyzeCommanderTrendsAsync(
        string workspaceId,
        int limit = 10,
        string? analysisDepth = null,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.AnalyzeCommanderTrendsAsync(
            workspaceId,
            limit,
            EffectiveAnalysisDepth(analysisDepth),
            refresh,
            cancellationToken);
    }

    /// <summary>
    /// Finds lower-known cards with useful corpus evidence for a deck goal.
    /// </summary>
    [McpServerTool(Name = "find_lesser_known_cards", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find lower-known cards that fit a deck goal using API-backed corpus evidence. AnalysisDepth can be minimal, balanced, or best; refresh bypasses source-fact cache.")]
    public Task<CorpusRecommendationResult> FindLesserKnownCardsAsync(
        string workspaceId,
        string goal = "",
        int limit = 10,
        decimal? maxPrice = null,
        string? analysisDepth = null,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.FindLesserKnownCardsAsync(
            workspaceId,
            goal,
            limit,
            maxPrice,
            EffectiveAnalysisDepth(analysisDepth),
            refresh,
            cancellationToken);
    }

    /// <summary>
    /// Creates budget replacement plans enriched with corpus evidence.
    /// </summary>
    [McpServerTool(Name = "find_corpus_budget_replacements", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Create a persisted budget replacement plan and attach API-backed corpus evidence to each replacement. AnalysisDepth can be minimal, balanced, or best; refresh bypasses source-fact cache.")]
    public Task<CorpusBudgetReplacementResult> FindCorpusBudgetReplacementsAsync(
        string workspaceId,
        decimal maxPrice = 5,
        decimal minSavings = 1,
        int limit = 10,
        string? analysisDepth = null,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        operationMode.EnsureCanWritePlanningState("find_corpus_budget_replacements");
        return recommendations.FindCorpusBudgetReplacementsAsync(
            workspaceId,
            maxPrice,
            minSavings,
            limit,
            EffectiveAnalysisDepth(analysisDepth),
            refresh,
            cancellationToken);
    }

    /// <summary>
    /// Finds high-signal exemplar decks from enabled corpus providers.
    /// </summary>
    [McpServerTool(Name = "find_top_exemplar_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Find top exemplar decks for a deck context from enabled API-backed corpus providers. AnalysisDepth can be minimal, balanced, or best; refresh bypasses source-fact cache.")]
    public Task<TopExemplarDecksResult> FindTopExemplarDecksAsync(
        string workspaceId,
        int limit = 10,
        string? analysisDepth = null,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.FindTopExemplarDecksAsync(
            workspaceId,
            limit,
            EffectiveAnalysisDepth(analysisDepth),
            refresh,
            cancellationToken);
    }

    /// <summary>
    /// Explains corpus evidence for one card in a deck context.
    /// </summary>
    [McpServerTool(Name = "explain_card_corpus_signal", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Explain why enabled API-backed corpus sources do or do not support one card in this deck context. AnalysisDepth can be minimal, balanced, or best; refresh bypasses source-fact cache.")]
    public Task<CorpusRecommendationResult> ExplainCardCorpusSignalAsync(
        string workspaceId,
        string cardName,
        string? analysisDepth = null,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        return recommendations.ExplainCardCorpusSignalAsync(
            workspaceId,
            cardName,
            EffectiveAnalysisDepth(analysisDepth),
            refresh,
            cancellationToken);
    }

    /// <summary>
    /// Lists enabled and planned corpus sources.
    /// </summary>
    [McpServerTool(Name = "list_corpus_sources", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List enabled, disabled, planned, and unsupported deck corpus sources with API stability and permission notes.")]
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
