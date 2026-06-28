namespace MtgMcp.Core;

/// <summary>
/// Reviews recently released card candidates and produces deterministic cut evidence.
/// </summary>
public sealed class DeckNewCardSwapReviewService
{
    /// <summary>
    /// Loads local workspaces for swap review.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Resolves catalog metadata for suggested new cards.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Supplies recent-card candidates before cut scoring.
    /// </summary>
    private readonly DeckNewCardService newCards;

    /// <summary>
    /// Creates a swap-review collaborator with explicit storage, catalog, and new-card dependencies.
    /// </summary>
    public DeckNewCardSwapReviewService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        DeckNewCardService newCards)
    {
        this.repository = repository;
        this.cardCatalog = cardCatalog;
        this.newCards = newCards;
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
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        NewCardsForDeckResult newCards = await this.newCards.FindNewCardsForDeckAsync(
            workspaceId,
            since,
            setCode,
            limit,
            maxPrice,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> candidateCards = await cardCatalog.GetCardsByNamesAsync(
            newCards.Suggestions.Select(suggestion => suggestion.CardName).ToList(),
            cancellationToken).ConfigureAwait(false);
        NewCardSwapReviewResult result = new()
        {
            WorkspaceId = workspace.Id
        };
        foreach (NewCardSuggestion suggestion in newCards.Suggestions)
        {
            candidateCards.TryGetValue(suggestion.CardName, out CardInfo? candidateInfo);
            DeckCard candidateCard = candidateInfo is null
                ? new DeckCard { Name = suggestion.CardName, PrimaryCategory = suggestion.Role }
                : DeckRecommendationCardFacts.CreateCandidateCard(candidateInfo);
            CardRoleAssignment candidateRole = DeckRoleClassifier.Classify(candidateCard);
            List<NewCardCutEvidence> cutCandidates = BuildCutEvidence(workspace, intent, candidateRole, candidateInfo, suggestion.Price);
            if (cutCandidates.Count > 5)
            {
                cutCandidates.RemoveRange(5, cutCandidates.Count - 5);
            }

            result.Candidates.Add(new NewCardSwapCandidate
            {
                CardName = suggestion.CardName,
                Role = suggestion.Role,
                Tags = suggestion.Tags,
                ReleasedAt = suggestion.ReleasedAt,
                Set = suggestion.Set,
                Price = suggestion.Price,
                ScryfallUri = candidateInfo?.ScryfallUri ?? suggestion.ScryfallUri,
                Score = suggestion.Score,
                Rationale = suggestion.Rationale,
                CutCandidates = cutCandidates,
                Metadata = BuildMetadata("scryfall", "recent-card-swap-review", candidateInfo?.ScryfallUri ?? suggestion.ScryfallUri, confidence: 0.70)
            });
        }

        result.Notes.AddRange(newCards.Notes);
        result.Notes.Add("Cut evidence is deterministic: role overlap, mana curve slot, duplicate effect density, theme mismatch, price delta, and protected-card warnings.");
        return result;
    }

    /// <summary>
    /// Builds deterministic cut evidence for one candidate.
    /// </summary>
    private static List<NewCardCutEvidence> BuildCutEvidence(
        DeckWorkspace workspace,
        DeckIntent? intent,
        CardRoleAssignment candidateRole,
        CardInfo? candidateInfo,
        decimal? candidatePrice)
    {
        List<DeckCard> included = [];
        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace))
        {
            if (!DeckRecommendationCardFacts.IsCommanderCard(card))
            {
                included.Add(card);
            }
        }

        Dictionary<string, int> roleCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in included)
        {
            string role = DeckRoleClassifier.Classify(card).PrimaryRole;
            roleCounts[role] = roleCounts.TryGetValue(role, out int count) ? count + 1 : 1;
        }

        List<NewCardCutEvidence> cuts = [];
        foreach (DeckCard card in included)
        {
            CardRoleAssignment currentRole = DeckRoleClassifier.Classify(card);
            bool roleOverlap = currentRole.PrimaryRole.Equals(candidateRole.PrimaryRole, StringComparison.OrdinalIgnoreCase)
                || currentRole.Tags.Intersect(candidateRole.Tags, StringComparer.OrdinalIgnoreCase).Any();
            CardSnapshot snapshot = DeckServiceHelpers.GetSnapshot(card);
            bool curveSlot = IsSameCurveSlot(snapshot.ManaValue, candidateInfo?.ManaValue);
            double duplicateDensity = roleCounts.TryGetValue(currentRole.PrimaryRole, out int count)
                ? Math.Clamp(count / 10.0, 0, 1)
                : 0;
            bool themeMismatch = candidateRole.Tags.Count > 0
                && !currentRole.Tags.Intersect(candidateRole.Tags, StringComparer.OrdinalIgnoreCase).Any()
                && !currentRole.PrimaryRole.Equals(candidateRole.PrimaryRole, StringComparison.OrdinalIgnoreCase);
            decimal? currentPrice = DeckRecommendationCardFacts.ReadUsdPrice(snapshot);
            decimal? priceDelta = currentPrice.HasValue && candidatePrice.HasValue
                ? currentPrice.Value - candidatePrice.Value
                : null;
            List<string> protectedWarnings = [];
            if (DeckIntentProtection.IsProtectedCard(card, intent))
            {
                protectedWarnings.Add("Card is protected by deck intent.");
            }

            double score = 0;
            score += roleOverlap ? 0.45 : 0;
            score += curveSlot ? 0.20 : 0;
            score += duplicateDensity * 0.20;
            score += themeMismatch ? 0.10 : 0;
            score += priceDelta is > 0 ? 0.05 : 0;
            if (protectedWarnings.Count > 0)
            {
                score *= 0.25;
            }

            NewCardCutEvidence evidence = new()
            {
                CardName = card.Name,
                Role = currentRole.PrimaryRole,
                RoleOverlap = roleOverlap,
                ManaCurveSlot = curveSlot,
                DuplicateEffectDensity = duplicateDensity,
                ThemeMismatch = themeMismatch,
                PriceDelta = priceDelta,
                ScryfallUri = snapshot.ScryfallUri,
                ProtectedCardWarnings = protectedWarnings,
                Score = Math.Clamp(score, 0, 1)
            };
            AddCutReasons(evidence);
            cuts.Add(evidence);
        }

        cuts.Sort(CompareCutEvidence);
        return cuts;
    }

    /// <summary>
    /// Adds exact scoring reasons to a cut row.
    /// </summary>
    private static void AddCutReasons(NewCardCutEvidence evidence)
    {
        if (evidence.RoleOverlap)
        {
            evidence.Reasons.Add("Role or tag overlaps the new card.");
        }

        if (evidence.ManaCurveSlot)
        {
            evidence.Reasons.Add("Mana value is in the same curve slot.");
        }

        if (evidence.DuplicateEffectDensity > 0)
        {
            evidence.Reasons.Add($"Duplicate effect density for role is {evidence.DuplicateEffectDensity:0.00}.");
        }

        if (evidence.ThemeMismatch)
        {
            evidence.Reasons.Add("Existing card has weaker tag overlap with the new card's route/theme.");
        }

        if (evidence.PriceDelta is > 0)
        {
            evidence.Reasons.Add("Candidate is cheaper than the existing card.");
        }
    }

    /// <summary>
    /// Checks whether two mana values share a curve slot.
    /// </summary>
    private static bool IsSameCurveSlot(double? current, double? candidate)
    {
        return current.HasValue && candidate.HasValue && Math.Abs(current.Value - candidate.Value) <= 1;
    }

    /// <summary>
    /// Sorts stronger cut evidence first, with card name as the deterministic tie-breaker.
    /// </summary>
    private static int CompareCutEvidence(NewCardCutEvidence left, NewCardCutEvidence right)
    {
        int score = right.Score.CompareTo(left.Score);
        return score != 0
            ? score
            : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds source metadata for deterministic evidence rows.
    /// </summary>
    private static SourceEvidenceMetadata BuildMetadata(
        string source,
        string sourceKind,
        string? sourceUri,
        double confidence)
    {
        return new SourceEvidenceMetadata
        {
            Source = source,
            SourceKind = sourceKind,
            SourceUri = sourceUri,
            CacheStatus = "live-or-cache",
            Confidence = Math.Clamp(confidence, 0, 1),
            Deterministic = true
        };
    }

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
