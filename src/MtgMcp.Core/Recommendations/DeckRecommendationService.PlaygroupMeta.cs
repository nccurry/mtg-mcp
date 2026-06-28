namespace MtgMcp.Core;

/// <summary>
/// Keeps local-meta scoring available on the recommendation facade while the focused service owns the workflow.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Scores candidate cards using deterministic deck-plan, performance, meta, budget, and confidence factors.
    /// </summary>
    public async Task<PlaygroupMetaScoringResult> ScoreCardsForPlaygroupMetaAsync(
        string workspaceId,
        string playgroupIdOrUrl,
        IReadOnlyList<string>? candidateCards,
        int maxGames,
        int metaDeckLimit,
        int simulations,
        int maxTurn,
        int seed,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        return await playgroupMeta
            .ScoreCardsForPlaygroupMetaAsync(
                workspaceId,
                playgroupIdOrUrl,
                candidateCards,
                maxGames,
                metaDeckLimit,
                simulations,
                maxTurn,
                seed,
                maxPrice,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
