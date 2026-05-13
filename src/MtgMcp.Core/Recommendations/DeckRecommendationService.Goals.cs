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
    private static IEnumerable<DeckCard> FindGoalCutCandidates(DeckWorkspace workspace, int addedCount, DeckIntent? intent)
    {
        int includedCount = IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity));
        int desiredCuts = NormalizeFormat(workspace.Format).Equals("commander", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0, includedCount + addedCount - 100)
            : 0;
        return IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Where(card => !IsProtectedCard(card, intent))
            .Select(card => new { Card = card, Role = DeckRoleClassifier.Classify(card) })
            .OrderBy(item => item.Role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Role.Confidence)
            .ThenByDescending(item => GetSnapshot(item.Card).EdhrecRank ?? int.MaxValue)
            .Take(desiredCuts)
            .Select(item => item.Card);
    }

    /// <summary>
    /// Finds cards whose pressure profile conflicts with a lower-salt goal.
    /// </summary>
    private static IEnumerable<DeckCard> FindLessSaltyCutCandidates(DeckWorkspace workspace, int desiredCuts, DeckIntent? intent)
    {
        return IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Where(card => !IsProtectedCard(card, intent))
            .Select(card => new { Card = card, Score = SaltPressureScore(card) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Card.Name)
            .Take(desiredCuts)
            .Select(item => item.Card);
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

}
