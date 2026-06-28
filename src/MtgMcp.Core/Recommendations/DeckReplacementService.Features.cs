namespace MtgMcp.Core;

/// <summary>
/// Contains contextual replacement feature scoring internals.
/// </summary>
public sealed partial class DeckReplacementService
{
    /// <summary>
    /// Builds named contextual features used by replacement scoring.
    /// </summary>
    private static ReplacementFeatureVector BuildReplacementFeatureVector(
        DeckWorkspace workspace,
        DeckCard currentCard,
        CardInfo candidate,
        DeckCard candidateCard,
        CardRoleAssignment currentRole,
        CardRoleAssignment candidateRole,
        double roleScore,
        double priceScore,
        double sourcePowerScore,
        DeckIntent? intent)
    {
        return new ReplacementFeatureVector
        {
            RoleFit = RoundFeature(roleScore),
            CommanderCurve = RoundFeature(ScoreCommanderCurveFeature(workspace, currentCard, candidateCard, candidateRole)),
            Tempo = RoundFeature(ScoreTempoFeature(currentCard, candidate)),
            Fixing = RoundFeature(ScoreFixingFeature(workspace, candidateCard, candidateRole)),
            PlanSynergy = RoundFeature(ScorePlanSynergyFeature(candidate, currentRole, candidateRole, intent)),
            LateGameFloor = RoundFeature(ScoreLateGameFloorFeature(candidateCard, candidateRole)),
            InteractionModality = RoundFeature(ScoreInteractionModalityFeature(candidateCard, candidateRole)),
            Price = RoundFeature(priceScore),
            EvidenceQuality = RoundFeature(ScoreEvidenceQualityFeature(candidate, sourcePowerScore))
        };
    }

    /// <summary>
    /// Blends source popularity with contextual features for the replacement power component.
    /// </summary>
    private static double ContextualPowerScore(double sourcePowerScore, ReplacementFeatureVector vector)
    {
        double contextual =
            (vector.CommanderCurve
                + vector.Tempo
                + vector.Fixing
                + vector.PlanSynergy
                + vector.LateGameFloor
                + vector.InteractionModality
                + vector.EvidenceQuality)
            / 7.0;
        return Math.Clamp((sourcePowerScore * 0.35) + (contextual * 0.65), 0, 1);
    }

    /// <summary>
    /// Scores whether the replacement helps commander timing or the surrounding curve.
    /// </summary>
    private static double ScoreCommanderCurveFeature(
        DeckWorkspace workspace,
        DeckCard currentCard,
        DeckCard candidateCard,
        CardRoleAssignment candidateRole)
    {
        if (candidateRole.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase))
        {
            CardOperationalFacts facts = RampOperationalFactExtractor.Extract(candidateCard);
            RampContextEvaluation evaluation = RampContextScorer.Evaluate(workspace, candidateCard, facts);
            return evaluation.Score / 100.0;
        }

        double currentMana = DeckServiceHelpers.GetSnapshot(currentCard).ManaValue ?? 0;
        double candidateMana = DeckServiceHelpers.GetSnapshot(candidateCard).ManaValue ?? currentMana;
        if (candidateMana <= Math.Max(1, currentMana - 1))
        {
            return 0.9;
        }

        if (candidateMana <= currentMana)
        {
            return 0.75;
        }

        return candidateMana <= currentMana + 1 ? 0.55 : 0.35;
    }

    /// <summary>
    /// Scores mana-value pressure compared with the card being replaced.
    /// </summary>
    private static double ScoreTempoFeature(DeckCard currentCard, CardInfo candidate)
    {
        double currentMana = DeckServiceHelpers.GetSnapshot(currentCard).ManaValue ?? 0;
        double candidateMana = candidate.ManaValue ?? currentMana;
        double delta = candidateMana - currentMana;
        if (delta <= -2)
        {
            return 1;
        }

        if (delta <= 0)
        {
            return 0.85;
        }

        return delta <= 1 ? 0.6 : Math.Max(0.2, 0.6 - ((delta - 1) * 0.15));
    }

    /// <summary>
    /// Scores color fixing only when the replacement is mana or land shaped.
    /// </summary>
    private static double ScoreFixingFeature(
        DeckWorkspace workspace,
        DeckCard candidateCard,
        CardRoleAssignment candidateRole)
    {
        bool manaRole = candidateRole.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase)
            || candidateRole.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase);
        if (!manaRole)
        {
            return 0.5;
        }

        IReadOnlyList<string> producedMana = DeckAnalysisMetrics.ReadProducedMana(candidateCard);
        if (producedMana.Count >= 5)
        {
            return 1;
        }

        (bool known, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        if (!known || colors.Count == 0)
        {
            return producedMana.Count > 0 ? 0.7 : 0.45;
        }

        int covered = 0;
        foreach (string color in colors)
        {
            if (producedMana.Contains(color, StringComparer.OrdinalIgnoreCase))
            {
                covered++;
            }
        }

        return Math.Clamp(covered / (double)colors.Count, 0.25, 1);
    }

    /// <summary>
    /// Scores explicit overlap with role, tags, or saved deck intent.
    /// </summary>
    private static double ScorePlanSynergyFeature(
        CardInfo candidate,
        CardRoleAssignment currentRole,
        CardRoleAssignment candidateRole,
        DeckIntent? intent)
    {
        string candidateText = $"{candidate.Name} {candidate.TypeLine} {candidate.OracleText}";
        if (intent?.Prefer.Any(value =>
                !string.IsNullOrWhiteSpace(value)
                && candidateText.Contains(value, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return 1;
        }

        bool sharedTags = candidateRole.Tags.Intersect(currentRole.Tags, StringComparer.OrdinalIgnoreCase).Any();
        if (sharedTags)
        {
            return 0.8;
        }

        return candidateRole.PrimaryRole.Equals(currentRole.PrimaryRole, StringComparison.OrdinalIgnoreCase)
            ? 0.65
            : 0.4;
    }

    /// <summary>
    /// Scores whether the candidate still matters after early turns.
    /// </summary>
    private static double ScoreLateGameFloorFeature(DeckCard candidateCard, CardRoleAssignment candidateRole)
    {
        string text = DeckServiceHelpers.GetSnapshot(candidateCard).OracleText ?? "";
        if (candidateRole.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase)
            || DeckAnalysisMetrics.ContainsAny(text, "whenever", "at the beginning", "draw a card", "draw cards", "return", "recursion"))
        {
            return 0.85;
        }

        if (DeckAnalysisMetrics.ContainsAny(text, "cycling", "flashback", "escape", "kicker", "activated ability"))
        {
            return 0.7;
        }

        return IsReplacementPermanent(candidateCard) ? 0.55 : 0.4;
    }

    /// <summary>
    /// Scores interaction speed and answer modality when the candidate is an answer.
    /// </summary>
    private static double ScoreInteractionModalityFeature(DeckCard candidateCard, CardRoleAssignment candidateRole)
    {
        bool answerRole = candidateRole.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase)
            || candidateRole.PrimaryRole.Equals(DeckRoles.Protection, StringComparison.OrdinalIgnoreCase)
            || candidateRole.PrimaryRole.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase);
        if (!answerRole)
        {
            return 0.5;
        }

        string typeLine = DeckServiceHelpers.GetSnapshot(candidateCard).TypeLine ?? "";
        string text = DeckServiceHelpers.GetSnapshot(candidateCard).OracleText ?? "";
        double score = typeLine.Contains("Instant", StringComparison.OrdinalIgnoreCase) ? 0.85 : 0.55;
        if (DeckAnalysisMetrics.ContainsAny(text, "exile", "counter target", "phase out", "indestructible", "hexproof", "can't be countered"))
        {
            score += 0.15;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Checks whether the replacement candidate leaves a permanent behind.
    /// </summary>
    private static bool IsReplacementPermanent(DeckCard candidateCard)
    {
        string typeLine = DeckServiceHelpers.GetSnapshot(candidateCard).TypeLine ?? "";
        return DeckAnalysisMetrics.ContainsAny(typeLine, "Creature", "Artifact", "Enchantment", "Planeswalker", "Battle", "Land");
    }

    /// <summary>
    /// Scores how much source-backed card metadata was available.
    /// </summary>
    private static double ScoreEvidenceQualityFeature(CardInfo candidate, double sourcePowerScore)
    {
        double score = 0;
        if (!string.IsNullOrWhiteSpace(candidate.ScryfallUri))
        {
            score += 0.25;
        }

        if (!string.IsNullOrWhiteSpace(candidate.OracleText))
        {
            score += 0.25;
        }

        if (candidate.EdhrecRank.HasValue)
        {
            score += 0.20;
        }

        if (candidate.Prices.Count > 0)
        {
            score += 0.15;
        }

        return Math.Clamp(score + (sourcePowerScore * 0.15), 0, 1);
    }

    /// <summary>
    /// Rounds feature scores to stable two-decimal output.
    /// </summary>
    private static double RoundFeature(double value)
    {
        return Math.Round(Math.Clamp(value, 0, 1), 2);
    }

    /// <summary>
    /// Checks whether a land replacement improves tapped-land pressure or fixing.
    /// </summary>
    private static bool IsManaBaseImprovement(DeckCard currentCard, CardInfo candidate)
    {
        DeckCard candidateCard = CreateCandidateCard(candidate);
        CardRoleAssignment candidateRole = DeckRoleClassifier.Classify(candidateCard);
        if (!candidateRole.PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CardSnapshot currentSnapshot = DeckServiceHelpers.GetSnapshot(currentCard);
        CardSnapshot candidateSnapshot = DeckServiceHelpers.GetSnapshot(candidateCard);
        if (DeckAnalysisMetrics.LooksTapped(candidateSnapshot))
        {
            return false;
        }

        int currentColors = DeckAnalysisMetrics.ReadProducedMana(currentCard).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int candidateColors = DeckAnalysisMetrics.ReadProducedMana(candidateCard).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        bool preservesSources = candidateColors >= currentColors || currentColors == 0;
        return DeckAnalysisMetrics.LooksTapped(currentSnapshot)
            && (preservesSources || candidateRole.Tags.Contains(DeckTags.ManaFixing));
    }

    /// <summary>
    /// Checks whether a land add candidate avoids tapped-land pressure.
    /// </summary>
    private static bool IsUntappedLandCandidate(CardInfo candidate)
    {
        DeckCard candidateCard = CreateCandidateCard(candidate);
        return DeckRoleClassifier.Classify(candidateCard).PrimaryRole.Equals(DeckRoles.Lands, StringComparison.OrdinalIgnoreCase)
            && !DeckAnalysisMetrics.LooksTapped(DeckServiceHelpers.GetSnapshot(candidateCard));
    }
}
