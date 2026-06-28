namespace MtgMcp.Core;

/// <summary>
/// Provides evidence-first Commander, payoff, and new-card swap workflows.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Gets source-backed aggregate cards for a commander.
    /// </summary>
    public async Task<CommanderAggregateCardsResult> GetCommanderAggregateCardsAsync(
        string commanderName,
        string? theme,
        string? source,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await commanderEvidence.GetCommanderAggregateCardsAsync(
            commanderName,
            theme,
            source,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets source-backed tags and theme sections for a commander.
    /// </summary>
    public async Task<CommanderTagsResult> GetCommanderTagsAsync(
        string commanderName,
        string? source,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await commanderEvidence.GetCommanderTagsAsync(
            commanderName,
            source,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds payoff candidates for a route using deterministic Scryfall queries.
    /// </summary>
    public async Task<WinconPayoffSearchResult> FindWinconPayoffsAsync(
        string route,
        string colorIdentity,
        string format,
        decimal? maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        return await payoffSearch.FindWinconPayoffsAsync(
            route,
            colorIdentity,
            format,
            maxPrice,
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Bundles structured win-condition evidence for one commander.
    /// </summary>
    public async Task<CommanderWinConditionEvidenceResult> GetCommanderWinConditionEvidenceAsync(
        string commanderName,
        string? theme,
        bool strictColorIdentity,
        IReadOnlyList<string>? sources,
        int limit,
        bool refresh,
        CancellationToken cancellationToken)
    {
        return await commanderEvidence.GetCommanderWinConditionEvidenceAsync(
            commanderName,
            theme,
            strictColorIdentity,
            sources,
            limit,
            refresh,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reviews newly released card candidates and deterministic cuts.
    /// </summary>
    public async Task<NewCardSwapReviewResult> ReviewNewCardSwapsAsync(
        string workspaceId,
        string? since,
        string? setCode,
        decimal? maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        return await newCardSwaps.ReviewNewCardSwapsAsync(
            workspaceId,
            since,
            setCode,
            maxPrice,
            limit,
            cancellationToken).ConfigureAwait(false);
    }
}
