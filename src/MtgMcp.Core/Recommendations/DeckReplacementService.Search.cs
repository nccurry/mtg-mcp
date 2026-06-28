namespace MtgMcp.Core;

/// <summary>
/// Contains replacement search and candidate scoring internals.
/// </summary>
public sealed partial class DeckReplacementService
{
    /// <summary>
    /// Finds the best replacement for a card.
    /// </summary>
    private async Task<ReplacementSuggestion?> FindReplacementAsync(
        DeckWorkspace workspace,
        DeckCard currentCard,
        decimal? maxPrice,
        decimal minSavings,
        bool budgetMode,
        ReplacementWeights weights,
        DeckIntent? intent,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? excludedCandidateNames = null,
        Func<CardInfo, bool>? candidateFilter = null)
    {
        CardRoleAssignment currentRole = DeckRoleClassifier.Classify(currentCard);
        if (currentRole.PrimaryRole.Equals(DeckRoles.Commander, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        CardSearchRequest searchRequest = DeckRoleClassifier.SearchRequestForRole(
            currentRole.PrimaryRole,
            workspace.Format,
            maxPrice);
        IReadOnlyList<CardSearchResult> searchResults = await cardCatalog
            .SearchCardsAsync(searchRequest, limit: 12, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> candidatesByName = await cardCatalog
            .GetCardsByNamesAsync(searchResults.Select(result => result.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);

        HashSet<string> existingNames = workspace.Cards
            .Select(card => card.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        (bool colorIdentityKnown, HashSet<string> deckColorIdentity) = GetDeckColorIdentity(workspace);

        ReplacementSuggestion? bestSuggestion = null;
        foreach (CardInfo candidate in candidatesByName.Values)
        {
            if (existingNames.Contains(candidate.Name)
                || (excludedCandidateNames is not null && excludedCandidateNames.Contains(candidate.Name))
                || candidate.Name.Equals(currentCard.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsLegalInFormat(candidate, workspace.Format)
                || !IsInDeckColorIdentity(candidate, colorIdentityKnown, deckColorIdentity)
                || IsAvoidedCandidate(candidate, intent))
            {
                continue;
            }

            if (candidateFilter is not null && !candidateFilter(candidate))
            {
                continue;
            }

            ReplacementSuggestion? suggestion = ScoreReplacement(
                workspace,
                currentCard,
                candidate,
                currentRole,
                weights,
                budgetMode,
                workspace.Format,
                intent);
            if (suggestion is null)
            {
                continue;
            }

            if (budgetMode
                && suggestion.EstimatedSavings.GetValueOrDefault() < minSavings)
            {
                continue;
            }

            if (bestSuggestion is null || suggestion.Score > bestSuggestion.Score)
            {
                bestSuggestion = suggestion;
            }
        }

        return bestSuggestion;
    }

    /// <summary>
    /// Scores a replacement candidate.
    /// </summary>
    private static ReplacementSuggestion? ScoreReplacement(
        DeckWorkspace workspace,
        DeckCard currentCard,
        CardInfo candidate,
        CardRoleAssignment currentRole,
        ReplacementWeights weights,
        bool budgetMode,
        string format,
        DeckIntent? intent)
    {
        DeckCard candidateCard = CreateCandidateCard(candidate);
        CardRoleAssignment candidateRole = DeckRoleClassifier.Classify(candidateCard);
        decimal? currentPrice = ReadUsdPrice(DeckServiceHelpers.GetSnapshot(currentCard));
        decimal? candidatePrice = ReadUsdPrice(candidate);
        decimal? estimatedSavings = currentPrice.HasValue && candidatePrice.HasValue
            ? currentPrice.Value - candidatePrice.Value
            : null;

        double roleScore = RoleScore(currentRole, candidateRole);
        if (roleScore < 0.65)
        {
            return null;
        }

        double priceScore = PriceScore(currentPrice, candidatePrice, budgetMode);
        double sourcePowerScore = PowerScore(candidate, currentCard, format);
        ReplacementFeatureVector featureVector = BuildReplacementFeatureVector(
            workspace,
            currentCard,
            candidate,
            candidateCard,
            currentRole,
            candidateRole,
            roleScore,
            priceScore,
            sourcePowerScore,
            intent);
        double powerScore = ContextualPowerScore(sourcePowerScore, featureVector);
        double score = (roleScore * weights.Role) + (powerScore * weights.Power) + (priceScore * weights.Price);

        if (budgetMode && (!candidatePrice.HasValue || candidatePrice.Value <= 0))
        {
            return null;
        }

        return new ReplacementSuggestion
        {
            ReplaceCard = currentCard.Name,
            WithCard = candidate.Name,
            Role = currentRole.PrimaryRole,
            Score = score,
            RoleScore = roleScore,
            PowerScore = powerScore,
            PriceScore = priceScore,
            FeatureVector = featureVector,
            CurrentPrice = currentPrice,
            CandidatePrice = candidatePrice,
            EstimatedSavings = estimatedSavings,
            ReplaceCardScryfallUri = DeckServiceHelpers.GetSnapshot(currentCard).ScryfallUri,
            WithCardScryfallUri = candidate.ScryfallUri,
            Rationale = $"{candidate.Name} fits {currentRole.PrimaryRole} at score {score:0.00}; "
                + "feature vector explains role, curve, tempo, fixing, synergy, floor, modality, price, and evidence."
        };
    }
}
