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

        foreach (DeckCard cut in FindGoalCutCandidates(workspace, suggestions.Count))
        {
            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.RemoveCard,
                CardName = cut.Name,
                Quantity = 1,
                Category = cut.PrimaryCategory,
                Rationale = $"Cut a lower-signal {cut.PrimaryCategory} card to make room for the goal package."
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

        if (normalized.Contains("less salty", StringComparison.OrdinalIgnoreCase) || normalized.Contains("less power", StringComparison.OrdinalIgnoreCase))
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
    private static IEnumerable<DeckCard> FindGoalCutCandidates(DeckWorkspace workspace, int addedCount)
    {
        int includedCount = IncludedCards(workspace).Sum(card => Math.Max(0, card.Quantity));
        int desiredCuts = NormalizeFormat(workspace.Format).Equals("commander", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0, includedCount + addedCount - 100)
            : 0;
        return IncludedCards(workspace)
            .Where(card => !IsCommanderCard(card))
            .Select(card => new { Card = card, Role = DeckRoleClassifier.Classify(card) })
            .OrderBy(item => item.Role.PrimaryRole.Equals(DeckRoles.Utility, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Role.Confidence)
            .ThenByDescending(item => GetSnapshot(item.Card).EdhrecRank ?? int.MaxValue)
            .Take(desiredCuts)
            .Select(item => item.Card);
    }

}
