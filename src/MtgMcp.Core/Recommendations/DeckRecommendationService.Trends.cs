namespace MtgMcp.Core;

/// <summary>
/// Delegates recent-card radar behavior to the focused new-card collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Finds recently released cards that fit a deck.
    /// </summary>
    public async Task<NewCardsForDeckResult> FindNewCardsForDeckAsync(
        string workspaceId,
        string? since,
        string? setCode,
        int limit,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        return await newCards
            .FindNewCardsForDeckAsync(
                workspaceId,
                since,
                setCode,
                limit,
                maxPrice,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
