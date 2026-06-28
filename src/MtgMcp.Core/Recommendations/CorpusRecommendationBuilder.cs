namespace MtgMcp.Core;

/// <summary>
/// Builds scored corpus recommendations and compact source evidence from normalized signals.
/// </summary>
internal static class CorpusRecommendationBuilder
{
    /// <summary>
    /// Builds a scored recommendation from card data and corpus signals.
    /// </summary>
    public static CorpusRecommendation BuildRecommendation(
        CardInfo card,
        IReadOnlyList<CardCorpusSignal> signals,
        string recommendationKind,
        string? goal,
        RecommendationAnalysisBudget budget,
        string? replaceCard)
    {
        DeckCard candidate = DeckRecommendationCardFacts.CreateCandidateCard(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
        double signalScore = AverageSignalScore(signals);
        HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
        foreach (CardCorpusSignal signal in signals)
        {
            sources.Add(signal.Source);
        }

        double sourceAgreement = sources.Count / (double)Math.Max(1, budget.MaxSources);
        double roleScore = ScoreRoleFit(role, goal);
        double noveltyScore = IsLesserKnown(card) ? 0.75 : 0.35;
        decimal? price = DeckRecommendationCardFacts.ReadUsdPrice(card);
        double priceScore = price is null ? 0.45 : 0.65;
        bool lesserKnownRecommendation = recommendationKind.Equals("lesser-known", StringComparison.OrdinalIgnoreCase);
        bool offPlanComboEvidence = lesserKnownRecommendation && IsOffPlanComboEvidence(signals, goal);
        double effectiveSignalScore = offPlanComboEvidence ? Math.Min(signalScore, 0.55) : signalScore;
        double score = lesserKnownRecommendation
            ? Math.Clamp((roleScore * 0.45) + (effectiveSignalScore * 0.25) + (noveltyScore * 0.20) + (sourceAgreement * 0.05) + (priceScore * 0.05), 0, 1)
            : Math.Clamp((signalScore * 0.45) + (roleScore * 0.25) + (sourceAgreement * 0.15) + (noveltyScore * 0.10) + (priceScore * 0.05), 0, 1);
        List<CorpusEvidence> evidence = BuildEvidence(signals, budget);
        return new CorpusRecommendation
        {
            CardName = card.Name,
            ReplaceCard = replaceCard,
            RecommendationKind = recommendationKind,
            Role = role.PrimaryRole,
            Tags = role.Tags,
            Score = score,
            Confidence = Math.Clamp(0.35 + (evidence.Count * 0.10) + (sourceAgreement * 0.20), 0, 0.95),
            Price = price,
            EdhrecRank = card.EdhrecRank,
            ScryfallUri = card.ScryfallUri,
            Rationale = BuildCorpusRationale(card.Name, role.PrimaryRole, recommendationKind, evidence.Count, goal),
            Evidence = evidence
        };
    }

    /// <summary>
    /// Groups signals by case-insensitive card name.
    /// </summary>
    public static Dictionary<string, List<CardCorpusSignal>> GroupSignalsByCard(IEnumerable<CardCorpusSignal> signals)
    {
        Dictionary<string, List<CardCorpusSignal>> grouped = new(StringComparer.OrdinalIgnoreCase);
        foreach (CardCorpusSignal signal in signals)
        {
            if (string.IsNullOrWhiteSpace(signal.CardName))
            {
                continue;
            }

            if (!grouped.TryGetValue(signal.CardName, out List<CardCorpusSignal>? cardSignals))
            {
                cardSignals = [];
                grouped[signal.CardName] = cardSignals;
            }

            cardSignals.Add(signal);
        }

        return grouped;
    }

    /// <summary>
    /// Builds compact evidence rows from card signals.
    /// </summary>
    public static List<CorpusEvidence> BuildEvidence(
        IReadOnlyList<CardCorpusSignal> signals,
        RecommendationAnalysisBudget budget)
    {
        List<CardCorpusSignal> sorted = signals.ToList();
        sorted.Sort(CompareSignalsForEvidence);

        List<CorpusEvidence> evidence = [];
        int maxRows = Math.Min(budget.MaxEvidencePerRecommendation, sorted.Count);
        for (int index = 0; index < maxRows; index++)
        {
            CardCorpusSignal signal = sorted[index];
            evidence.Add(new CorpusEvidence
            {
                Source = signal.Source,
                SignalType = signal.SignalType,
                Score = signal.Score,
                Summary = string.IsNullOrWhiteSpace(signal.Rationale)
                    ? $"{signal.SignalType} signal from {signal.Source}."
                    : signal.Rationale,
                Uri = budget.IncludeSourceUrls ? signal.Uri : null
            });
        }

        return evidence;
    }

    /// <summary>
    /// Removes duplicate source/type/card signal rows.
    /// </summary>
    public static List<CardCorpusSignal> DeduplicateSignals(IEnumerable<CardCorpusSignal> signals)
    {
        Dictionary<string, CardCorpusSignal> bestByKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (CardCorpusSignal signal in signals)
        {
            string key = $"{signal.CardName}|{signal.Source}|{signal.SignalType}|{signal.Uri}|{signal.Rationale}";
            if (!bestByKey.TryGetValue(key, out CardCorpusSignal? currentBest)
                || signal.Score > currentBest.Score)
            {
                bestByKey[key] = signal;
            }
        }

        return bestByKey.Values.ToList();
    }

    /// <summary>
    /// Removes duplicate discussion rows by source URL and body.
    /// </summary>
    public static List<DiscussionEvidence> DeduplicateDiscussions(IEnumerable<DiscussionEvidence> discussions)
    {
        Dictionary<string, DiscussionEvidence> bestByKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (DiscussionEvidence discussion in discussions)
        {
            if (string.IsNullOrWhiteSpace(discussion.Uri) && string.IsNullOrWhiteSpace(discussion.Body))
            {
                continue;
            }

            string key = $"{discussion.Source}|{discussion.Uri}|{discussion.Body}";
            if (!bestByKey.TryGetValue(key, out DiscussionEvidence? currentBest)
                || (discussion.Score ?? 0) > (currentBest.Score ?? 0))
            {
                bestByKey[key] = discussion;
            }
        }

        return bestByKey.Values.ToList();
    }

    /// <summary>
    /// Computes the average source signal score.
    /// </summary>
    public static double AverageSignalScore(IReadOnlyList<CardCorpusSignal> signals)
    {
        return signals.Count == 0 ? 0.30 : signals.Average(signal => signal.Score);
    }

    /// <summary>
    /// Checks whether a card is lower-known for Commander recommendation purposes.
    /// </summary>
    public static bool IsLesserKnown(CardInfo card)
    {
        return !card.EdhrecRank.HasValue || card.EdhrecRank.Value > 5_000;
    }

    /// <summary>
    /// Orders stronger source signals first, with source name as the stable tie-breaker.
    /// </summary>
    private static int CompareSignalsForEvidence(CardCorpusSignal left, CardCorpusSignal right)
    {
        int scoreCompare = right.Score.CompareTo(left.Score);
        if (scoreCompare != 0)
        {
            return scoreCompare;
        }

        return string.Compare(left.Source, right.Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Scores role fit against a user goal or theme.
    /// </summary>
    private static double ScoreRoleFit(CardRoleAssignment role, string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
        {
            return role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase) ? 0.35 : 0.65;
        }

        if (goal.Contains(role.PrimaryRole, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Any(tag => goal.Contains(tag, StringComparison.OrdinalIgnoreCase)))
        {
            return 0.95;
        }

        return 0.55;
    }

    /// <summary>
    /// Checks whether combo-only evidence is off-plan for a non-combo lesser-known card request.
    /// </summary>
    private static bool IsOffPlanComboEvidence(IReadOnlyList<CardCorpusSignal> signals, string? goal)
    {
        return signals.Count > 0
            && signals.All(signal => signal.SignalType.Equals(CorpusSignalTypes.Combo, StringComparison.OrdinalIgnoreCase))
            && !GoalRequestsCombo(goal);
    }

    /// <summary>
    /// Checks whether the user goal explicitly asks for combo recommendations.
    /// </summary>
    private static bool GoalRequestsCombo(string? goal)
    {
        return !string.IsNullOrWhiteSpace(goal)
            && (goal.Contains("combo", StringComparison.OrdinalIgnoreCase)
                || goal.Contains("infinite", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a compact recommendation rationale.
    /// </summary>
    private static string BuildCorpusRationale(string cardName, string role, string kind, int evidenceCount, string? goal)
    {
        string goalText = string.IsNullOrWhiteSpace(goal) ? "the deck context" : goal;
        return evidenceCount == 0
            ? $"{cardName} is a {role} candidate for {goalText}."
            : $"{cardName} is a {role} candidate for {goalText} with {evidenceCount} corpus signal(s) supporting the {kind} recommendation.";
    }
}
