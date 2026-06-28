namespace MtgMcp.Core;

/// <summary>
/// Delegates unified deck brainstorming to the focused brainstorming collaborator.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Runs the unified brewing workflow.
    /// </summary>
    public async Task<BrainstormDeckImprovementsResult> BrainstormDeckImprovementsAsync(
        string workspaceId,
        string goal,
        decimal budget,
        string targetPower,
        CancellationToken cancellationToken)
    {
        return await brainstorming.BrainstormDeckImprovementsAsync(
            workspaceId,
            goal,
            budget,
            targetPower,
            cancellationToken).ConfigureAwait(false);
    }
}
