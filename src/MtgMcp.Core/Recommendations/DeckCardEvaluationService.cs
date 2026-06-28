namespace MtgMcp.Core;

/// <summary>
/// Builds read-only card evaluation reports against a saved workspace.
/// </summary>
public sealed class DeckCardEvaluationService
{
    /// <summary>
    /// Loads local workspaces for card evaluation.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Resolves candidate cards not already present in the workspace.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Creates a card evaluation collaborator with explicit workspace and catalog dependencies.
    /// </summary>
    public DeckCardEvaluationService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog)
    {
        this.repository = repository;
        this.cardCatalog = cardCatalog;
    }

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
            IReadOnlyDictionary<string, CardInfo> candidateInfos = await cardCatalog
                .GetCardsByNamesAsync(candidates, cancellationToken)
                .ConfigureAwait(false);
            foreach (string candidateName in candidates)
            {
                DeckCard? candidateCard = FindCard(workspace, candidateName)
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
    /// Evaluates one resolved deck card with the supported operational scorers.
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
        DeckCard? card = FindCard(workspace, cardName);
        if (card is not null)
        {
            return card;
        }

        CardInfo? info = await cardCatalog.GetCardAsync(cardName, cancellationToken)
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

    /// <summary>
    /// Finds a workspace card by name using the first matching row.
    /// </summary>
    private static DeckCard? FindCard(DeckWorkspace workspace, string cardName)
    {
        foreach (DeckCard card in workspace.Cards)
        {
            if (card.Name.Equals(cardName, StringComparison.OrdinalIgnoreCase))
            {
                return card;
            }
        }

        return null;
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
