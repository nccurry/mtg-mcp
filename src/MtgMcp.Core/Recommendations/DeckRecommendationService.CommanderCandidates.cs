namespace MtgMcp.Core;

/// <summary>
/// Delegates bounded Commander candidate discovery to the focused candidate search collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Finds Commander candidates with EDHREC eligible deck counts inside requested bounds.
    /// </summary>
    public async Task<CommanderCandidateSearchResult> SearchCommanderCandidatesAsync(
        string? colorIdentity,
        bool exactColorIdentity,
        int minEligibleDecks,
        int? maxEligibleDecks,
        int limit,
        int scryfallCandidateCap,
        int edhrecFetchCap,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await commanderCandidates.SearchCommanderCandidatesAsync(
            colorIdentity,
            exactColorIdentity,
            minEligibleDecks,
            maxEligibleDecks,
            limit,
            scryfallCandidateCap,
            edhrecFetchCap,
            refresh,
            cancellationToken).ConfigureAwait(false);
    }
}
