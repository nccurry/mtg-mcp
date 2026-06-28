namespace MtgMcp.Core;

/// <summary>
/// Delegates read-only card evaluation reports to the focused evaluation collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Evaluates supported operational facts and context score without creating deck edits.
    /// </summary>
    public async Task<RampContextEvaluation> EvaluateCardAsync(
        string workspaceId,
        string cardName,
        IReadOnlyList<string>? candidateCards,
        int candidateLimit,
        CancellationToken cancellationToken)
    {
        return await cardEvaluation
            .EvaluateCardAsync(
                workspaceId,
                cardName,
                candidateCards,
                candidateLimit,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
