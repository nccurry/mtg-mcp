namespace MtgMcp.Core;

/// <summary>
/// Contains local-meta scoring factor internals.
/// </summary>
public sealed partial class DeckPlaygroupMetaScoringService
{
    /// <summary>
    /// Scores how well a candidate aligns with the deck plan.
    /// </summary>
    private static double ScorePlanFit(
        CardRoleAssignment role,
        DeckCard candidate,
        SimulationProfile profile,
        DeckIntent? intent)
    {
        double score = role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase) ? 0.25 : 0.45;
        IEnumerable<string> buildTargets = intent?.BuildTargets.Keys ?? Enumerable.Empty<string>();
        IEnumerable<string> legacyTargets = intent?.Targets.Keys ?? Enumerable.Empty<string>();
        IEnumerable<string> targetNames = buildTargets.Concat(legacyTargets);
        if (targetNames.Any(target => DeckRoleClassifier.MatchesTarget(candidate, target)))
        {
            score += 0.25;
        }

        if (role.Tags.Any(tag => profile.ThemeTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            || role.Tags.Any(tag => intent?.ArchetypeTags.Contains(tag, StringComparer.OrdinalIgnoreCase) == true))
        {
            score += 0.20;
        }

        score += profile.Id switch
        {
            SimulationProfileIds.Combo when role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Combo when role.Tags.Any(tag => tag is DeckTags.ComboPiece or DeckTags.ComboEnabler) => 0.25,
            SimulationProfileIds.Control when role.PrimaryRole.Equals(DeckRoles.Interaction, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Control when role.PrimaryRole.Equals(DeckRoles.BoardWipes, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Aggro when role.Tags.Any(tag => tag is DeckTags.Tokens or DeckTags.Voltron or DeckTags.Finishers) => 0.25,
            SimulationProfileIds.BigMana when role.PrimaryRole.Equals(DeckRoles.Ramp, StringComparison.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Stax when role.Tags.Contains(DeckTags.Stax, StringComparer.OrdinalIgnoreCase) => 0.25,
            SimulationProfileIds.Value when role.Tags.Contains(DeckTags.Engines, StringComparer.OrdinalIgnoreCase) => 0.20,
            _ => 0,
        };
        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Scores a candidate's performance impact from before and after deterministic simulation snapshots.
    /// </summary>
    private static double ScorePerformanceDelta(
        DeckPerformanceAnalysis before,
        DeckPerformanceAnalysis after,
        SimulationProfile profile)
    {
        double interaction = ScenarioRate(after, "hold-up-interaction-by-turn-4") - ScenarioRate(before, "hold-up-interaction-by-turn-4");
        double protection = ScenarioRate(after, "commander-with-protection-by-turn-5") - ScenarioRate(before, "commander-with-protection-by-turn-5");
        double combo = ScenarioRate(after, "combo-or-tutor-assembly-by-turn-5") - ScenarioRate(before, "combo-or-tutor-assembly-by-turn-5");
        double graveyard = ScenarioRate(after, "graveyard-hate-by-turn-3") - ScenarioRate(before, "graveyard-hate-by-turn-3");
        double strandedRiskReduction = ScenarioRate(before, "stranded-high-mana-risk-by-max-turn") - ScenarioRate(after, "stranded-high-mana-risk-by-max-turn");
        double weighted = profile.Id switch
        {
            SimulationProfileIds.Combo => (combo * 0.35) + (protection * 0.25) + (interaction * 0.25) + (graveyard * 0.10) + (strandedRiskReduction * 0.05),
            SimulationProfileIds.Control => (interaction * 0.40) + (graveyard * 0.20) + (protection * 0.15) + (combo * 0.10) + (strandedRiskReduction * 0.15),
            _ => (interaction * 0.30) + (protection * 0.20) + (combo * 0.20) + (graveyard * 0.15) + (strandedRiskReduction * 0.15),
        };
        return Math.Clamp(0.5 + (weighted * 2.0), 0, 1);
    }

    /// <summary>
    /// Scores a candidate's matchup coverage against aggregate pressures.
    /// </summary>
    private static double ScoreMetaCoverage(
        DeckCard candidate,
        CardRoleAssignment role,
        IReadOnlyList<PlaygroupMetaPressureEvidence> pressures)
    {
        double weighted = 0;
        double total = 0;
        foreach (PlaygroupMetaPressureEvidence pressure in pressures)
        {
            weighted += pressure.Score * CoverageForPressure(candidate, role, pressure.Pressure);
            total += pressure.Score;
        }

        return total <= 0 ? 0.45 : Math.Clamp(weighted / total, 0, 1);
    }

    /// <summary>
    /// Scores likely conflict between a candidate and the deck's own plan.
    /// </summary>
    private static double ScoreSelfHarm(
        DeckCard candidate,
        CardRoleAssignment role,
        SimulationProfile profile,
        DeckIntent? intent)
    {
        string text = $"{candidate.Name} {DeckServiceHelpers.GetSnapshot(candidate).OracleText}";
        double penalty = 0;
        bool blinkDeck = profile.ThemeTags.Contains("blink", StringComparer.OrdinalIgnoreCase)
            || intent?.ArchetypeTags.Contains("blink", StringComparer.OrdinalIgnoreCase) == true;
        if (blinkDeck && DeckAnalysisMetrics.ContainsAny(text, "entering the battlefield don't cause", "entering the battlefield doesn't cause"))
        {
            penalty = Math.Max(penalty, 0.90);
        }

        if (profile.Id.Equals(SimulationProfileIds.Combo, StringComparison.OrdinalIgnoreCase)
            && DeckAnalysisMetrics.ContainsAny(text, "each player can't cast more than one spell", "players can't cast more than one spell"))
        {
            penalty = Math.Max(penalty, 0.35);
        }

        if (role.Tags.Contains(DeckTags.Stax, StringComparer.OrdinalIgnoreCase)
            && intent?.Avoid.Any(avoid => text.Contains(avoid, StringComparison.OrdinalIgnoreCase)) == true)
        {
            penalty = Math.Max(penalty, 0.50);
        }

        return Math.Clamp(penalty, 0, 1);
    }

    /// <summary>
    /// Scores price, legality, color identity, and Game Changer constraints.
    /// </summary>
    private static double ScorePriceBracket(
        CardInfo card,
        decimal? maxPrice,
        bool isGameChanger,
        bool colorKnown,
        HashSet<string> colors,
        string format)
    {
        double score = 1;
        decimal? price = DeckRecommendationCardFacts.ReadUsdPrice(card);
        if (maxPrice.HasValue)
        {
            score = price.HasValue && price.Value <= maxPrice.Value
                ? 1 - Math.Clamp((double)(price.Value / maxPrice.Value) * 0.20, 0, 0.20)
                : 0.15;
        }
        else if (!price.HasValue)
        {
            score = 0.70;
        }

        if (isGameChanger)
        {
            score = Math.Min(score, 0.10);
        }

        if (!DeckRecommendationCardFacts.IsLegalInFormat(card, format)
            || !DeckRecommendationCardFacts.IsInDeckColorIdentity(card, colorKnown, colors))
        {
            score = 0;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Scores the confidence of candidate card facts and meta pressure data.
    /// </summary>
    private static double ScoreEvidenceConfidence(
        CardInfo card,
        IReadOnlyList<PlaygroupMetaPressureEvidence> pressures,
        int simulations)
    {
        double score = 0.35;
        score += !string.IsNullOrWhiteSpace(card.OracleText) ? 0.15 : 0;
        score += DeckRecommendationCardFacts.ReadUsdPrice(card).HasValue ? 0.05 : 0;
        score += card.EdhrecRank.HasValue ? 0.05 : 0;
        score += pressures.Count > 0 ? 0.15 : 0;
        score += Math.Min(0.15, simulations / 2000.0 * 0.15);
        return Math.Clamp(score, 0, 0.95);
    }

    /// <summary>
    /// Scores one card against one local-meta pressure.
    /// </summary>
    private static double CoverageForPressure(DeckCard candidate, CardRoleAssignment role, string pressure)
    {
        string text = $"{candidate.Name} {DeckServiceHelpers.GetSnapshot(candidate).TypeLine} {DeckServiceHelpers.GetSnapshot(candidate).OracleText}";
        return pressure switch
        {
            FastComboPressure => Max(
                RoleScore(role, DeckRoles.Interaction, 0.75),
                RoleScore(role, DeckRoles.BoardWipes, 0.55),
                TagScore(role, DeckTags.Stax, 0.90),
                TextScore(text, 0.85, "counter target", "exile target", "destroy target", "can't activate")),
            CreatureCombatPressure => Max(
                RoleScore(role, DeckRoles.BoardWipes, 0.95),
                RoleScore(role, DeckRoles.Interaction, 0.70),
                TagScore(role, DeckTags.Pillowfort, 0.80),
                TagScore(role, DeckTags.GoWideProtection, 0.75)),
            GoWideTokensPressure => Max(
                RoleScore(role, DeckRoles.BoardWipes, 0.95),
                TagScore(role, DeckTags.TokenHate, 0.95),
                TextScore(text, 0.85, "all creatures", "each creature", "creature tokens get")),
            GraveyardRecursionPressure => Max(
                TagScore(role, DeckTags.GraveyardHate, 0.95),
                TextScore(text, 0.95, "exile all graveyards", "exile target card from a graveyard", "cards in graveyards"),
                RoleScore(role, DeckRoles.Interaction, 0.35)),
            StackControlPressure => Max(
                RoleScore(role, DeckRoles.Protection, 0.85),
                TextScore(text, 0.90, "can't be countered", "hexproof", "phase out"),
                RoleScore(role, DeckRoles.Draw, 0.45)),
            ArtifactEnginePressure => Max(
                TagScore(role, DeckTags.ArtifactEnchantmentHate, 0.95),
                TextScore(text, 0.95, "destroy target artifact", "exile target artifact", "destroy all artifacts"),
                RoleScore(role, DeckRoles.Interaction, 0.55)),
            EnchantmentEnginePressure => Max(
                TagScore(role, DeckTags.ArtifactEnchantmentHate, 0.95),
                TextScore(text, 0.95, "destroy target enchantment", "exile target enchantment", "destroy all enchantments"),
                RoleScore(role, DeckRoles.Interaction, 0.55)),
            LifePressure => Max(
                TagScore(role, DeckTags.Lifegain, 0.70),
                RoleScore(role, DeckRoles.Protection, 0.45),
                RoleScore(role, DeckRoles.Interaction, 0.40)),
            StaxPressure => Max(
                TagScore(role, DeckTags.ArtifactEnchantmentHate, 0.80),
                RoleScore(role, DeckRoles.Interaction, 0.70),
                RoleScore(role, DeckRoles.BoardWipes, 0.65)),
            _ => 0.35,
        };
    }
}
