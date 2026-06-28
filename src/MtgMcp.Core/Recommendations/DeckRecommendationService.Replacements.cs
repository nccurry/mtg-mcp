namespace MtgMcp.Core;

/// <summary>
/// Delegates replacement and upgrade planning workflows to the focused replacement collaborator.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Finds budget replacements.
    /// </summary>
    public async Task<RecommendationPlanResult> FindBudgetReplacementsAsync(
        string workspaceId,
        decimal maxPrice,
        decimal minSavings,
        int limit,
        ReplacementWeights? weights,
        CancellationToken cancellationToken)
    {
        return await replacements
            .FindBudgetReplacementsAsync(
                workspaceId,
                maxPrice,
                minSavings,
                limit,
                weights,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds card upgrades.
    /// </summary>
    public async Task<RecommendationPlanResult> FindCardUpgradesAsync(
        string workspaceId,
        int limit,
        ReplacementWeights? weights,
        CancellationToken cancellationToken)
    {
        return await replacements
            .FindCardUpgradesAsync(
                workspaceId,
                limit,
                weights,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds card upgrades with optional focus and price constraints.
    /// </summary>
    public async Task<RecommendationPlanResult> FindCardUpgradesAsync(
        string workspaceId,
        string focus,
        decimal? maxPrice,
        int limit,
        ReplacementWeights? weights,
        CancellationToken cancellationToken)
    {
        return await replacements
            .FindCardUpgradesAsync(
                workspaceId,
                focus,
                maxPrice,
                limit,
                weights,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds bracket reduction candidates.
    /// </summary>
    public async Task<RecommendationPlanResult> FindBracketReductionCandidatesAsync(
        string workspaceId,
        int targetBracket,
        int limit,
        CancellationToken cancellationToken)
    {
        return await replacements
            .FindBracketReductionCandidatesAsync(
                workspaceId,
                targetBracket,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds power reduction candidates.
    /// </summary>
    public async Task<RecommendationPlanResult> FindPowerReductionCandidatesAsync(
        string workspaceId,
        string targetPower,
        int limit,
        CancellationToken cancellationToken)
    {
        return await replacements
            .FindPowerReductionCandidatesAsync(
                workspaceId,
                targetPower,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds mana base improvements.
    /// </summary>
    public async Task<RecommendationPlanResult> FindManaBaseImprovementsAsync(
        string workspaceId,
        decimal maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        return await replacements
            .FindManaBaseImprovementsAsync(
                workspaceId,
                maxPrice,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Finds consistency improvements.
    /// </summary>
    public async Task<RecommendationPlanResult> FindConsistencyImprovementsAsync(
        string workspaceId,
        string focus,
        decimal maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        return await replacements
            .FindConsistencyImprovementsAsync(
                workspaceId,
                focus,
                maxPrice,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a candidate deck card for remaining facade-owned recommendation workflows.
    /// </summary>
    private static DeckCard CreateCandidateCard(CardInfo candidate)
    {
        return DeckRecommendationCardFacts.CreateCandidateCard(candidate);
    }

    /// <summary>
    /// Checks whether the card is legal in a format.
    /// </summary>
    private static bool IsLegalInFormat(CardInfo card, string format)
    {
        return DeckRecommendationCardFacts.IsLegalInFormat(card, format);
    }

    /// <summary>
    /// Normalizes a format name.
    /// </summary>
    private static string NormalizeFormat(string? format)
    {
        return DeckRecommendationCardFacts.NormalizeFormat(format);
    }

    /// <summary>
    /// Gets the deck color identity.
    /// </summary>
    private static (bool IsKnown, HashSet<string> Colors) GetDeckColorIdentity(DeckWorkspace workspace)
    {
        return DeckRecommendationCardFacts.GetDeckColorIdentity(workspace);
    }

    /// <summary>
    /// Checks whether a candidate fits the deck color identity.
    /// </summary>
    private static bool IsInDeckColorIdentity(CardInfo candidate, bool colorIdentityKnown, HashSet<string> deckColorIdentity)
    {
        return DeckRecommendationCardFacts.IsInDeckColorIdentity(candidate, colorIdentityKnown, deckColorIdentity);
    }

    /// <summary>
    /// Checks whether a known price is within a requested cap for remaining facade-owned workflows.
    /// </summary>
    private static bool IsPriceWithinBudget(decimal? price, decimal? maxPrice)
    {
        return !maxPrice.HasValue || (price.HasValue && price.Value <= maxPrice.Value);
    }

    /// <summary>
    /// Builds a plan operation that adds a recommended role card.
    /// </summary>
    private static DeckEditOperation CreateAddOperation(CardInfo card, string role, string rationale)
    {
        return DeckEditOperation.AddCard(card.Name, 1, role, rationale);
    }
}
