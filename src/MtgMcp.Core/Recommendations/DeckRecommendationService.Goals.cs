namespace MtgMcp.Core;

/// <summary>
/// Provides goal-driven card package behavior.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Creates a recommendation plan from a natural-language deckbuilding goal.
    /// </summary>
    public async Task<GoalPackagePlanResult> FindCardsForDeckGoalAsync(
        string workspaceId,
        string goal,
        int count,
        decimal maxPrice,
        string strategy,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        DeckGoalSpec spec = DeckGoalSpecCatalog.Build(goal, workspace.Format, maxPrice, strategy);
        DeckQueryRecommendationResult ranking = await RankCardsForDeckQueriesAsync(
            workspace,
            goal,
            spec.Searches,
            count,
            maxPrice,
            spec.RequiredRoles,
            spec.RequiredTags,
            spec.ExcludedRoles,
            spec.ExcludedTags,
            cancellationToken).ConfigureAwait(false);
        DeckEditPlan plan = await SaveQueryPlanAsync(
            workspace,
            ranking,
            spec.Category,
            goal,
            DeckGoalSpecCatalog.IsLessSaltyGoal(goal),
            intent,
            count,
            spec.Rationale,
            "Goal package plan",
            "goal-package",
            cancellationToken).ConfigureAwait(false);

        return new GoalPackagePlanResult
        {
            Plan = plan,
            Goal = goal,
            Strategy = spec.Strategy,
            Suggestions = ranking.Candidates.Select(candidate => new GoalCardSuggestion
            {
                CardName = candidate.CardName,
                Role = candidate.Role,
                Tags = candidate.Tags,
                FitScore = candidate.Score,
                Price = candidate.Price,
                Rationale = candidate.Rationale
            }).ToList()
        };
    }

    /// <summary>
    /// Finds low-signal cards to cut when a goal package adds cards to a full deck.
    /// </summary>
    private static IEnumerable<CutCandidateChoice> FindGoalCutCandidates(DeckWorkspace workspace, int addedCount, DeckIntent? intent)
    {
        int includedCount = IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity));
        int desiredCuts = NormalizeFormat(workspace.Format).Equals("commander", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0, includedCount + addedCount - 100)
            : 0;
        List<DeckCard> eligibleCards = IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Where(card => !IsProtectedCard(card, intent))
            .ToList();
        Dictionary<string, int> roleCounts = BuildRoleCounts(eligibleCards);
        return eligibleCards
            .Select(card => BuildGoalCutChoice(card, roleCounts, intent))
            .OrderByDescending(choice => choice.Score)
            .ThenBy(choice => DeckRoleClassifier.Classify(choice.Card).Confidence)
            .ThenByDescending(choice => GetSnapshot(choice.Card).EdhrecRank ?? int.MaxValue)
            .ThenBy(choice => choice.Card.Name, StringComparer.OrdinalIgnoreCase)
            .Take(desiredCuts)
            .ToList();
    }

    /// <summary>
    /// Finds cards whose pressure profile conflicts with a lower-salt goal.
    /// </summary>
    private static IEnumerable<CutCandidateChoice> FindLessSaltyCutCandidates(DeckWorkspace workspace, int desiredCuts, DeckIntent? intent)
    {
        return IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Where(card => !IsProtectedCard(card, intent))
            .Select(card => new
            {
                Card = card,
                Score = SaltPressureScore(card)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Card.Name)
            .Take(desiredCuts)
            .Select(item => new CutCandidateChoice(
                item.Card,
                item.Score,
                $"Cut a high-pressure card; deterministic pressure score {item.Score}."));
    }

    /// <summary>
    /// Builds role counts for deterministic cut ranking.
    /// </summary>
    private static Dictionary<string, int> BuildRoleCounts(IEnumerable<DeckCard> cards)
    {
        Dictionary<string, int> roleCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in cards)
        {
            AddCount(roleCounts, DeckRoleClassifier.Classify(card).PrimaryRole, card.Quantity);
        }

        return roleCounts;
    }

    /// <summary>
    /// Builds one deterministic cut candidate from card and deck role statistics.
    /// </summary>
    private static CutCandidateChoice BuildGoalCutChoice(
        DeckCard card,
        IReadOnlyDictionary<string, int> roleCounts,
        DeckIntent? intent)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        CardSnapshot snapshot = GetSnapshot(card);
        List<string> reasons = [];
        double score = 0;

        if (role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
            reasons.Add("classified as utility");
        }

        if (role.Confidence < 0.55)
        {
            score += 2;
            reasons.Add($"low role confidence {role.Confidence:0.00}");
        }

        int roleCount = roleCounts.TryGetValue(role.PrimaryRole, out int count) ? count : 0;
        int targetMaximum = CutTargetMaximum(intent, role.PrimaryRole);
        if (roleCount > targetMaximum)
        {
            score += 2 + Math.Min(3, (roleCount - targetMaximum) * 0.25);
            reasons.Add($"{role.PrimaryRole} count {roleCount} is above target maximum {targetMaximum}");
        }
        else if (ProtectLeanRole(role.PrimaryRole))
        {
            score -= 2;
            reasons.Add($"{role.PrimaryRole} count {roleCount} is not above target maximum {targetMaximum}");
        }

        if (!snapshot.EdhrecRank.HasValue)
        {
            score += 0.40;
            reasons.Add("no EDHREC rank");
        }
        else if (snapshot.EdhrecRank.Value > 10_000)
        {
            score += 0.75;
            reasons.Add($"EDHREC rank {snapshot.EdhrecRank.Value}");
        }

        if (reasons.Count == 0)
        {
            reasons.Add($"lowest deterministic score in {role.PrimaryRole}");
        }

        return new CutCandidateChoice(
            card,
            score,
            $"Cut a lower-signal {role.PrimaryRole} card to make room; {string.Join("; ", reasons)}.");
    }

    /// <summary>
    /// Gets a default maximum target for cut pressure by role.
    /// </summary>
    private static int CutTargetMaximum(DeckIntent? intent, string role)
    {
        return role switch
        {
            DeckRoles.Lands => TargetMinimum(intent, DeckRoles.Lands, 35) + 5,
            DeckRoles.Ramp => TargetMinimum(intent, DeckRoles.Ramp, 8) + 4,
            DeckRoles.Draw => TargetMinimum(intent, DeckRoles.Draw, 8) + 4,
            DeckRoles.Interaction => TargetMinimum(intent, DeckRoles.Interaction, 8) + 5,
            DeckRoles.BoardWipes => 4,
            DeckRoles.Tutors => 6,
            DeckRoles.Protection => 5,
            DeckRoles.Recursion => 5,
            DeckRoles.Wincons => 5,
            _ => 10
        };
    }

    /// <summary>
    /// Gets whether a role should be protected when it is not above target.
    /// </summary>
    private static bool ProtectLeanRole(string role)
    {
        return role is DeckRoles.Lands or DeckRoles.Ramp or DeckRoles.Draw or DeckRoles.Interaction;
    }

    /// <summary>
    /// Scores cards that often make casual Commander tables feel higher-pressure.
    /// </summary>
    private static int SaltPressureScore(DeckCard card)
    {
        CardRoleAssignment role = DeckRoleClassifier.Classify(card);
        string text = $"{card.Name} {GetSnapshot(card).TypeLine} {GetSnapshot(card).OracleText}";
        int score = 0;
        if (role.PrimaryRole.Equals(DeckRoles.Tutors, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (role.Tags.Contains(DeckTags.Stax))
        {
            score += 4;
        }

        if (role.Tags.Contains(DeckTags.ComboPiece) || role.Tags.Contains(DeckTags.ComboEnabler))
        {
            score += 3;
        }

        if (ContainsAny(text, "extra turn", "destroy all lands", "can't untap", "players can't cast", "opponents can't cast"))
        {
            score += 4;
        }

        if (ContainsAny(card.Name, "Mana Crypt", "Jeweled Lotus", "Dockside Extortionist", "Demonic Tutor", "Vampiric Tutor", "Thassa's Oracle"))
        {
            score += 5;
        }

        return score;
    }

    /// <summary>
    /// Carries one deterministic cut recommendation and its scoring evidence.
    /// </summary>
    private sealed record CutCandidateChoice(
        DeckCard Card,
        double Score,
        string Rationale);

}
