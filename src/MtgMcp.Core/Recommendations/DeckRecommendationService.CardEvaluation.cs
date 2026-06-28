namespace MtgMcp.Core;

/// <summary>
/// Provides read-only card evaluation reports.
/// </summary>
public sealed partial class DeckRecommendationService
{
    /// <summary>
    /// Evaluates a card's operational ramp facts and context score without creating deck edits.
    /// </summary>
    public async Task<RampContextEvaluation> EvaluateCardAsync(
        string workspaceId,
        string cardName,
        IReadOnlyList<string>? candidateCards,
        int candidateLimit,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        DeckCard card = await ResolveEvaluationCardAsync(workspace, cardName, cancellationToken)
            .ConfigureAwait(false);
        RampContextEvaluation evaluation = EvaluateCard(workspace, card);

        List<string> candidates = [];
        int safeCandidateLimit = Math.Clamp(candidateLimit, 0, 25);
        foreach (string candidate in candidateCards ?? [])
        {
            string trimmedCandidate = candidate.Trim();
            if (string.IsNullOrWhiteSpace(trimmedCandidate)
                || trimmedCandidate.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                || candidates.Contains(trimmedCandidate, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            candidates.Add(trimmedCandidate);
            if (candidates.Count >= safeCandidateLimit)
            {
                break;
            }
        }
        if (candidates.Count > 0)
        {
            IReadOnlyDictionary<string, CardInfo> candidateInfos = await CardCatalog
                .GetCardsByNamesAsync(candidates, cancellationToken)
                .ConfigureAwait(false);
            foreach (string candidateName in candidates)
            {
                DeckCard? candidateCard = FindCard(workspace, candidateName, category: null)
                    ?? CreateEvaluationCard(candidateInfos.GetValueOrDefault(candidateName), candidateName);
                if (candidateCard is null)
                {
                    evaluation.Warnings.Add($"Candidate '{candidateName}' could not be resolved from workspace or card catalog.");
                    continue;
                }

                RampContextEvaluation candidateEvaluation = EvaluateCard(workspace, candidateCard);
                candidateEvaluation.CandidateEvaluations.Clear();
                evaluation.CandidateEvaluations.Add(candidateEvaluation);
            }

            evaluation.CandidateEvaluations.Sort((left, right) =>
            {
                int comparison = right.Score.CompareTo(left.Score);
                return comparison != 0
                    ? comparison
                    : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
            });
        }

        return evaluation;
    }

    /// <summary>
    /// Evaluates one resolved deck card.
    /// </summary>
    private static RampContextEvaluation EvaluateCard(DeckWorkspace workspace, DeckCard card)
    {
        CardOperationalFacts facts = RampOperationalFactExtractor.Extract(card);
        return RampContextScorer.Evaluate(workspace, card, facts);
    }

    /// <summary>
    /// Resolves a card from the workspace first, then from the configured card catalog.
    /// </summary>
    private async Task<DeckCard> ResolveEvaluationCardAsync(
        DeckWorkspace workspace,
        string cardName,
        CancellationToken cancellationToken)
    {
        DeckCard? card = FindCard(workspace, cardName, category: null);
        if (card is not null)
        {
            return card;
        }

        CardInfo? info = await CardCatalog.GetCardAsync(cardName, cancellationToken)
            .ConfigureAwait(false);
        return CreateEvaluationCard(info, cardName)
            ?? throw new InvalidOperationException($"Card '{cardName}' was not found in the workspace or card catalog.");
    }

    /// <summary>
    /// Creates a transient deck card from catalog metadata.
    /// </summary>
    private static DeckCard? CreateEvaluationCard(CardInfo? info, string fallbackName)
    {
        if (info is null)
        {
            return null;
        }

        DeckCard card = new()
        {
            Name = string.IsNullOrWhiteSpace(info.Name) ? fallbackName : info.Name,
            Quantity = 1,
            PrimaryCategory = DeckDefaults.Mainboard,
            Categories = [DeckDefaults.Mainboard],
        };
        DeckServiceHelpers.ApplyCardSnapshot(card, info);
        return card;
    }
}
