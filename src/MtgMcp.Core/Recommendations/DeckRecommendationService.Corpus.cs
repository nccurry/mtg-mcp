namespace MtgMcp.Core;

/// <summary>
/// Delegates corpus-backed recommendation behavior to the focused corpus collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Analyzes commander and theme trends using normalized corpus signals.
    /// </summary>
    public async Task<CorpusRecommendationResult> AnalyzeCommanderTrendsAsync(
        string workspaceId,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await corpusRecommendations
            .AnalyzeCommanderTrendsAsync(
                workspaceId,
                limit,
                analysisDepth,
                refresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds lower-known cards with useful corpus or local-fit evidence.
    /// </summary>
    public async Task<CorpusRecommendationResult> FindLesserKnownCardsAsync(
        string workspaceId,
        string goal,
        int limit,
        decimal? maxPrice,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await corpusRecommendations
            .FindLesserKnownCardsAsync(
                workspaceId,
                goal,
                limit,
                maxPrice,
                analysisDepth,
                refresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a budget replacement plan and enriches replacement suggestions with corpus evidence.
    /// </summary>
    public async Task<CorpusBudgetReplacementResult> FindCorpusBudgetReplacementsAsync(
        string workspaceId,
        decimal maxPrice,
        decimal minSavings,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await corpusRecommendations
            .FindCorpusBudgetReplacementsAsync(
                workspaceId,
                maxPrice,
                minSavings,
                limit,
                analysisDepth,
                refresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds top exemplar decks from enabled corpus providers.
    /// </summary>
    public async Task<TopExemplarDecksResult> FindTopExemplarDecksAsync(
        string workspaceId,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await corpusRecommendations
            .FindTopExemplarDecksAsync(
                workspaceId,
                limit,
                analysisDepth,
                refresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Explains corpus evidence for a single card in a deck context.
    /// </summary>
    public async Task<CorpusRecommendationResult> ExplainCardCorpusSignalAsync(
        string workspaceId,
        string cardName,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await corpusRecommendations
            .ExplainCardCorpusSignalAsync(
                workspaceId,
                cardName,
                analysisDepth,
                refresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Searches one corpus source and returns raw evidence rows without synthesizing recommendations.
    /// </summary>
    public async Task<CorpusEvidenceSearchResult> SearchCorpusEvidenceAsync(
        string workspaceId,
        string sourceKey,
        string goal,
        int limit,
        string? analysisDepth,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await corpusRecommendations
            .SearchCorpusEvidenceAsync(
                workspaceId,
                sourceKey,
                goal,
                limit,
                analysisDepth,
                refresh,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Lists configured corpus sources with real provider implementations.
    /// </summary>
    public CorpusSourceStatusResult ListCorpusSources()
    {
        return corpusRecommendations.ListCorpusSources();
    }
}
