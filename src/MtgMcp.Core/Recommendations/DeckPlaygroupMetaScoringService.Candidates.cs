namespace MtgMcp.Core;

/// <summary>
/// Contains candidate selection and candidate-level local-meta scoring internals.
/// </summary>
public sealed partial class DeckPlaygroupMetaScoringService
{
    /// <summary>
    /// Scores one card from the deterministic scoring factors.
    /// </summary>
    private PlaygroupMetaCandidateScore ScoreMetaCandidate(
        DeckWorkspace workspace,
        CardInfo card,
        DeckPerformanceAnalysis baseline,
        IReadOnlyList<PlaygroupMetaPressureEvidence> pressures,
        ResolvedSimulationProfile profileResolution,
        DeckIntent? intent,
        decimal? maxPrice,
        IReadOnlySet<string> gameChangers,
        bool colorKnown,
        HashSet<string> colors,
        int simulations,
        int maxTurn,
        int seed,
        CancellationToken cancellationToken)
    {
        DeckCard candidate = DeckRecommendationCardFacts.CreateCandidateCard(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
        DeckPerformanceAnalysis after = DeckPerformanceAnalyzer.Analyze(
            WorkspaceWithAddedCandidate(workspace, card),
            SimulationProfileIds.Auto,
            simulations,
            maxTurn,
            seed,
            includeMulligans: true,
            cancellationToken,
            simulationProfiles);
        bool isGameChanger = gameChangers.Contains(card.Name);
        double planFit = ScorePlanFit(role, candidate, profileResolution.Profile, intent);
        double performanceDelta = ScorePerformanceDelta(baseline, after, profileResolution.Profile);
        double metaCoverage = ScoreMetaCoverage(candidate, role, pressures);
        double selfHarmPenalty = ScoreSelfHarm(candidate, role, profileResolution.Profile, intent);
        double priceBracket = ScorePriceBracket(card, maxPrice, isGameChanger, colorKnown, colors, workspace.Format);
        double confidence = ScoreEvidenceConfidence(card, pressures, simulations);
        double overall = Math.Clamp(
            (planFit * 0.25)
            + (performanceDelta * 0.20)
            + (metaCoverage * 0.30)
            + (priceBracket * 0.15)
            + (confidence * 0.10)
            - (selfHarmPenalty * 0.25),
            0,
            1);
        List<string> evidence =
        [
            $"plan fit {planFit:0.00}",
            $"performance delta score {performanceDelta:0.00}",
            $"meta coverage {metaCoverage:0.00}",
            $"self-harm penalty {selfHarmPenalty:0.00}",
            $"price/bracket score {priceBracket:0.00}",
        ];
        foreach (PlaygroupMetaPressureEvidence pressure in pressures.Take(3))
        {
            evidence.Add($"meta pressure {pressure.Pressure} at {pressure.Score:0.00}");
        }

        return new PlaygroupMetaCandidateScore
        {
            CardName = card.Name,
            Role = role.PrimaryRole,
            Tags = role.Tags,
            OverallScore = overall,
            PlanFitScore = planFit,
            PerformanceDeltaScore = performanceDelta,
            MetaCoverageScore = metaCoverage,
            SelfHarmPenalty = selfHarmPenalty,
            PriceBracketScore = priceBracket,
            EvidenceConfidence = confidence,
            Price = DeckRecommendationCardFacts.ReadUsdPrice(card),
            ScryfallUri = card.ScryfallUri,
            IsGameChanger = isGameChanger,
            Rationale = BuildMetaCandidateRationale(card.Name, role.PrimaryRole, metaCoverage, selfHarmPenalty),
            Evidence = evidence,
        };
    }

    /// <summary>
    /// Gets candidate names from explicit input or non-included workspace categories.
    /// </summary>
    private static List<string> CandidateNames(DeckWorkspace workspace, IReadOnlyList<string>? candidateCards)
    {
        if (candidateCards is { Count: > 0 })
        {
            return candidateCards
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
        }

        HashSet<string> excludedCategories = workspace.Categories
            .Where(category => !category.IncludedInDeck)
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return workspace.Cards
            .Where(card => DeckCategoryOrdering.OrderedDistinct(
                DeckCategoryOrdering.PrimaryCategory(card),
                card.Categories).Any(excludedCategories.Contains))
            .Select(card => card.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
    }

    /// <summary>
    /// Chooses the per-candidate simulation count for local-meta scoring batches.
    /// </summary>
    private static int BudgetCandidatePerformanceSimulations(int candidateCount, int requestedSimulations)
    {
        if (candidateCount <= 0)
        {
            return requestedSimulations;
        }

        int budgetedSimulations = CandidatePerformanceSimulationBudget / candidateCount;
        return Math.Clamp(
            Math.Min(requestedSimulations, budgetedSimulations),
            CandidatePerformanceMinimumSimulations,
            requestedSimulations);
    }
}
