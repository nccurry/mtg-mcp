namespace MtgMcp.Core;

/// <summary>
/// Delegates Commander metagame comparison behavior to the focused Commander meta collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Compares a deck with optional Commander metagame data.
    /// </summary>
    public async Task<CommanderMetaReport> CompareToCommanderMetaAsync(
        string workspaceId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await commanderMeta
            .CompareToCommanderMetaAsync(workspaceId, limit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a plan for popular cards missing from a deck.
    /// </summary>
    public async Task<GoalPackagePlanResult> FindMissingPopularCardsAsync(
        string workspaceId,
        int limit,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        return await commanderMeta
            .FindMissingPopularCardsAsync(workspaceId, limit, maxPrice, cancellationToken)
            .ConfigureAwait(false);
    }

}
