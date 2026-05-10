namespace MtgMcp.Core;

/// <summary>
/// Provides goal-driven card package behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
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
        bool reduceSalt = IsLessSaltyGoal(goal);
        (string normalizedStrategy, string category, string[] queries, string[] targets, string rationale) =
            BuildGoalSpec(goal, workspace.Format, maxPrice, strategy);
        DeckEditPlan plan = CreatePlan(workspace, "Goal package plan", "goal-package");
        plan.Rationale = rationale;
        HashSet<string> existing = workspace.Cards.Select(card => card.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
        (bool colorKnown, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        List<GoalCardSuggestion> suggestions = [];

        foreach (string query in queries)
        {
            IReadOnlyList<CardSearchResult> searchResults = await cardCatalog
                .SearchCardsAsync(query, limit: 12, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog
                .GetCardsByNamesAsync(searchResults.Select(card => card.Name).ToList(), cancellationToken)
                .ConfigureAwait(false);

            foreach (CardInfo candidate in cards.Values)
            {
                if (suggestions.Count >= Math.Clamp(count, 1, 25))
                {
                    break;
                }

                DeckCard candidateCard = CreateCandidateCard(candidate);
                CardRoleAssignment role = DeckRoleClassifier.Classify(candidateCard);
                decimal? price = ReadUsdPrice(candidate);
                if (existing.Contains(candidate.Name)
                    || selected.Contains(candidate.Name)
                    || !IsLegalInFormat(candidate, workspace.Format)
                    || !IsInDeckColorIdentity(candidate, colorKnown, colors)
                    || !IsPriceWithinBudget(price, maxPrice)
                    || !MatchesGoalTargets(role, targets))
                {
                    continue;
                }

                double score = ScoreGoalFit(role, targets, candidate);
                selected.Add(candidate.Name);
                suggestions.Add(new GoalCardSuggestion
                {
                    CardName = candidate.Name,
                    Role = role.PrimaryRole,
                    Tags = role.Tags,
                    FitScore = score,
                    Price = price,
                    Rationale = $"{candidate.Name} matches {goal} through {string.Join(", ", role.Tags.Prepend(role.PrimaryRole).Distinct(StringComparer.OrdinalIgnoreCase))}."
                });
                plan.Operations.Add(CreateAddOperation(candidate, category, $"Add for goal '{goal}': {rationale}"));
            }
        }

        IEnumerable<DeckCard> cuts = reduceSalt
            ? FindLessSaltyCutCandidates(workspace, Math.Clamp(Math.Max(count, suggestions.Count), 1, 25), intent)
            : FindGoalCutCandidates(workspace, suggestions.Count, intent);
        foreach (DeckCard cut in cuts)
        {
            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.RemoveCard,
                CardName = cut.Name,
                Quantity = 1,
                Category = cut.PrimaryCategory,
                Rationale = reduceSalt
                    ? "Cut a high-pressure card to make the deck less salty."
                    : $"Cut a lower-signal {cut.PrimaryCategory} card to make room for the goal package."
            });
        }

        plan.Confidence = suggestions.Count == 0 ? 0 : suggestions.Average(suggestion => suggestion.FitScore);
        if (suggestions.Count == 0)
        {
            plan.Warnings.Add("No cards matched the goal, budget, color identity, and legality filters.");
        }

        await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
        return new GoalPackagePlanResult
        {
            Plan = plan,
            Goal = goal,
            Strategy = normalizedStrategy,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Builds goal-search constraints from natural language.
    /// </summary>
    private static (string Strategy, string Category, string[] Queries, string[] Targets, string Rationale) BuildGoalSpec(
        string goal,
        string format,
        decimal maxPrice,
        string strategy)
    {
        string normalized = goal.ToLowerInvariant();
        string legal = $"legal:{NormalizeFormat(format)} usd<={maxPrice:0.##}";
        if (normalized.Contains("whole table", StringComparison.OrdinalIgnoreCase) || normalized.Contains("table", StringComparison.OrdinalIgnoreCase))
        {
            return (NormalizeFocus(strategy), DeckRoles.Interaction,
                [$"(o:\"each opponent\" or o:\"each player\" or o:\"each creature\") {legal}", $"(o:\"destroy all\" or o:\"exile all\") {legal}"],
                [DeckTags.TableInteraction, DeckRoles.BoardWipes, DeckRoles.Interaction],
                "Adds effects that touch multiple opponents or the whole battlefield.");
        }

        if (normalized.Contains("token", StringComparison.OrdinalIgnoreCase) || normalized.Contains("go wide", StringComparison.OrdinalIgnoreCase))
        {
            return (NormalizeFocus(strategy), DeckRoles.Interaction,
                [$"(o:\"destroy all tokens\" or o:\"each creature gets -1/-1\" or o:\"prevent all combat damage\") {legal}", $"(o:\"creatures can't attack you\" or o:\"unless their controller pays\") {legal}"],
                [DeckTags.TokenHate, DeckTags.GoWideProtection, DeckTags.Pillowfort, DeckRoles.BoardWipes],
                "Adds defenses and sweepers against go-wide token pressure.");
        }

        if (normalized.Contains("graveyard", StringComparison.OrdinalIgnoreCase))
        {
            return (NormalizeFocus(strategy), DeckRoles.Interaction,
                [$"(o:\"exile target card from a graveyard\" or o:\"exile all graveyards\" or o:\"cards in graveyards\") {legal}"],
                [DeckTags.GraveyardHate],
                "Adds graveyard hate that can answer recursion and reanimation decks.");
        }

        if (normalized.Contains("finisher", StringComparison.OrdinalIgnoreCase) || normalized.Contains("win", StringComparison.OrdinalIgnoreCase))
        {
            return (NormalizeFocus(strategy), DeckRoles.Wincons,
                [$"(o:\"each opponent loses\" or o:\"damage to each opponent\" or o:\"win the game\" or o:\"extra combat\") {legal}"],
                [DeckRoles.Wincons, DeckTags.Finishers],
                "Adds clearer closing cards and win routes.");
        }

        if (IsLessSaltyGoal(goal))
        {
            return ("casual", DeckRoles.Utility,
                [$"(o:create or o:draw or o:gain) {legal}"],
                [DeckRoles.Draw, DeckRoles.Synergy, DeckTags.Engines],
                "Adds lower-pressure value cards rather than tutors, fast mana, stax, or combo pieces.");
        }

        return (NormalizeFocus(strategy), DeckRoles.Utility,
            [$"{legal}", $"(o:draw or o:\"destroy target\" or o:add) {legal}"],
            [DeckRoles.Draw, DeckRoles.Interaction, DeckRoles.Ramp, DeckTags.Engines],
            "Adds broadly useful cards that improve weak role coverage.");
    }

    /// <summary>
    /// Checks whether a goal asks to reduce salt or power.
    /// </summary>
    private static bool IsLessSaltyGoal(string goal)
    {
        return goal.Contains("less salty", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("less salt", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("less power", StringComparison.OrdinalIgnoreCase)
            || goal.Contains("power down", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a classified card matches goal targets.
    /// </summary>
    private static bool MatchesGoalTargets(CardRoleAssignment role, IReadOnlyList<string> targets)
    {
        return targets.Any(target => role.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Contains(target, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Scores a goal candidate.
    /// </summary>
    private static double ScoreGoalFit(CardRoleAssignment role, IReadOnlyList<string> targets, CardInfo card)
    {
        double targetScore = targets.Any(target => role.PrimaryRole.Equals(target, StringComparison.OrdinalIgnoreCase)) ? 0.75 : 0.45;
        if (role.Tags.Intersect(targets, StringComparer.OrdinalIgnoreCase).Any())
        {
            targetScore += 0.20;
        }

        double rankScore = card.EdhrecRank switch
        {
            null => 0.45,
            <= 1_000 => 0.95,
            <= 5_000 => 0.75,
            <= 10_000 => 0.55,
            _ => 0.35
        };
        return Math.Clamp((targetScore * 0.70) + (rankScore * 0.30), 0, 1);
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
