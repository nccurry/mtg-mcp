namespace MtgMcp.Core;

/// <summary>
/// Delegates deck-aware card query workflows to the focused query collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Gets cards from a Scryfall query after applying deck legality, color, budget, and caller-supplied role filters.
    /// </summary>
    public async Task<DeckQueryDataResult> QueryCardsForDeckAsync(
        string workspaceId,
        string goal,
        string scryfallQuery,
        int count,
        decimal? maxPrice,
        IReadOnlyList<string>? requiredRoles,
        IReadOnlyList<string>? requiredTags,
        IReadOnlyList<string>? excludedRoles,
        IReadOnlyList<string>? excludedTags,
        CancellationToken cancellationToken)
    {
        return await queries
            .QueryCardsForDeckAsync(
                workspaceId,
                goal,
                scryfallQuery,
                count,
                maxPrice,
                requiredRoles,
                requiredTags,
                excludedRoles,
                excludedTags,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
