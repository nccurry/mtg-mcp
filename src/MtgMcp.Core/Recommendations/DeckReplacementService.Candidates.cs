namespace MtgMcp.Core;

/// <summary>
/// Contains add-candidate lookup internals for consistency planning.
/// </summary>
public sealed partial class DeckReplacementService
{
    /// <summary>
    /// Finds a card to add for a role.
    /// </summary>
    private async Task<CardInfo?> FindAddCandidateAsync(
        DeckWorkspace workspace,
        string role,
        decimal maxPrice,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? excludedNames = null,
        Func<CardInfo, bool>? candidateFilter = null)
    {
        CardSearchRequest searchRequest = DeckRoleClassifier.SearchRequestForRole(role, workspace.Format, maxPrice);
        IReadOnlyList<CardSearchResult> results = await cardCatalog
            .SearchCardsAsync(searchRequest, limit: 12, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> cardsByName = await cardCatalog
            .GetCardsByNamesAsync(results.Select(result => result.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);
        HashSet<string> existingNames = workspace.Cards.Select(card => card.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        (bool colorIdentityKnown, HashSet<string> deckColorIdentity) = GetDeckColorIdentity(workspace);

        return cardsByName.Values
            .Where(card => !existingNames.Contains(card.Name))
            .Where(card => IsLegalInFormat(card, workspace.Format))
            .Where(card => IsInDeckColorIdentity(card, colorIdentityKnown, deckColorIdentity))
            .Where(card => excludedNames is null || !excludedNames.Contains(card.Name))
            .Where(card => ReadUsdPrice(card) is { } price && price <= maxPrice)
            .Where(card => CandidateMatchesRole(card, role))
            .Where(card => candidateFilter is null || candidateFilter(card))
            .OrderBy(card => card.EdhrecRank ?? int.MaxValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks whether an add candidate actually fills the requested role or tag.
    /// </summary>
    private static bool CandidateMatchesRole(CardInfo card, string role)
    {
        return DeckRoleClassifier.MatchesTarget(CreateCandidateCard(card), role);
    }
}
