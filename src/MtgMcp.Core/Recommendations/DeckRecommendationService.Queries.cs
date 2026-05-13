namespace MtgMcp.Core;

/// <summary>
/// Provides deck-aware ranking and planning for caller-supplied Scryfall queries.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Ranks cards from a Scryfall query after applying deck legality, color, budget, and role filters.
    /// </summary>
    public async Task<DeckQueryRecommendationResult> RankCardsForDeckQueryAsync(
        string workspaceId,
        string goal,
        string scryfallQuery,
        int count,
        decimal? maxPrice,
        IReadOnlyList<string>? requiredRoles,
        IReadOnlyList<string>? requiredTags,
        IReadOnlyList<string>? excludedRoles,
        IReadOnlyList<string>? excludedTags,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return await RankCardsForDeckQueriesAsync(
            workspace,
            goal,
            [CardSearchRequest.Raw(scryfallQuery)],
            count,
            maxPrice,
            requiredRoles,
            requiredTags,
            excludedRoles,
            excludedTags,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a persisted non-mutating plan from a Scryfall query after deterministic deck-aware ranking.
    /// </summary>
    public async Task<DeckQueryPlanResult> CreateDeckPlanFromQueryAsync(
        string workspaceId,
        string goal,
        string scryfallQuery,
        string category,
        string cutsStrategy,
        int count,
        decimal? maxPrice,
        IReadOnlyList<string>? requiredRoles,
        IReadOnlyList<string>? requiredTags,
        IReadOnlyList<string>? excludedRoles,
        IReadOnlyList<string>? excludedTags,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckQueryRecommendationResult ranking = await RankCardsForDeckQueriesAsync(
            workspace,
            goal,
            [CardSearchRequest.Raw(scryfallQuery)],
            count,
            maxPrice,
            requiredRoles,
            requiredTags,
            excludedRoles,
            excludedTags,
            cancellationToken).ConfigureAwait(false);

        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        DeckEditPlan plan = await SaveQueryPlanAsync(
            workspace,
            ranking,
            string.IsNullOrWhiteSpace(category) ? DeckRoles.Utility : category,
            goal,
            UseHighPressureCuts(cutsStrategy),
            intent,
            count,
            string.IsNullOrWhiteSpace(goal)
                ? "Adds cards selected from a deck-aware Scryfall query."
                : $"Adds cards for goal '{goal}' from a deck-aware Scryfall query.",
            "Query card package plan",
            "query-card-package",
            cancellationToken).ConfigureAwait(false);

        return new DeckQueryPlanResult { Plan = plan, Ranking = ranking };
    }

    /// <summary>
    /// Ranks cards from one or more Scryfall queries without saving a plan.
    /// </summary>
    private async Task<DeckQueryRecommendationResult> RankCardsForDeckQueriesAsync(
        DeckWorkspace workspace,
        string goal,
        IReadOnlyList<CardSearchRequest> searchRequests,
        int count,
        decimal? maxPrice,
        IReadOnlyList<string>? requiredRoles,
        IReadOnlyList<string>? requiredTags,
        IReadOnlyList<string>? excludedRoles,
        IReadOnlyList<string>? excludedTags,
        CancellationToken cancellationToken)
    {
        int candidateLimit = Math.Clamp(count, 1, 25);
        int searchLimit = Math.Clamp(candidateLimit * 6, 12, 50);
        string format = NormalizeFormat(workspace.Format);
        (bool colorKnown, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        HashSet<string> existing = workspace.Cards
            .Select(card => card.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        List<string> roleRequirements = NormalizeTargets(requiredRoles);
        List<string> tagRequirements = NormalizeTargets(requiredTags);
        List<string> roleExclusions = NormalizeTargets(excludedRoles);
        List<string> tagExclusions = NormalizeTargets(excludedTags);
        List<DeckQueryCandidate> candidates = [];
        List<DeckQueryRejectedCandidate> rejected = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        DeckQueryEvaluationContext evaluationContext = new()
        {
            ExistingCards = existing,
            Format = format,
            ColorIdentityKnown = colorKnown,
            ColorIdentity = colors,
            MaxPrice = maxPrice,
            RequiredRoles = roleRequirements,
            RequiredTags = tagRequirements,
            ExcludedRoles = roleExclusions,
            ExcludedTags = tagExclusions,
            Intent = intent
        };

        DeckQueryRecommendationResult result = new()
        {
            WorkspaceId = workspace.Id,
            Goal = goal,
            ScryfallQuery = string.Join(" | ", searchRequests.Select(DescribeSearchRequest).Where(query => !string.IsNullOrWhiteSpace(query))),
            Constraints = new DeckQueryRecommendationConstraints
            {
                Format = format,
                ColorIdentityKnown = colorKnown,
                ColorIdentity = colors.Order(StringComparer.OrdinalIgnoreCase).ToList(),
                MaxPrice = maxPrice,
                RequiredRoles = roleRequirements,
                RequiredTags = tagRequirements,
                ExcludedRoles = roleExclusions,
                ExcludedTags = tagExclusions
            }
        };

        foreach (CardSearchRequest request in searchRequests)
        {
            CardSearchRequest effectiveRequest = NormalizeSearchRequest(request, format, maxPrice);
            string executedQuery = DescribeSearchRequest(effectiveRequest);
            if (string.IsNullOrWhiteSpace(executedQuery))
            {
                continue;
            }

            result.ExecutedQueries.Add(executedQuery);
            IReadOnlyList<CardSearchResult> searchResults = await CardCatalog
                .SearchCardsAsync(effectiveRequest, searchLimit, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyDictionary<string, CardInfo> cards = await CardCatalog
                .GetCardsByNamesAsync(
                    searchResults.Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (CardSearchResult searchResult in searchResults)
            {
                if (!seen.Add(searchResult.Name))
                {
                    continue;
                }

                if (!cards.TryGetValue(searchResult.Name, out CardInfo? card))
                {
                    rejected.Add(new DeckQueryRejectedCandidate
                    {
                        CardName = searchResult.Name,
                        Reasons = ["Card details were not available from the catalog."]
                    });
                    continue;
                }

                DeckCard candidateCard = CreateCandidateCard(card);
                CardRoleAssignment role = DeckRoleClassifier.Classify(candidateCard);
                decimal? price = ReadUsdPrice(card);
                DeckQueryRejectedCandidate? rejection = DeckQueryRecommendationEngine.BuildRejection(
                    card,
                    role,
                    price,
                    evaluationContext);
                if (rejection is not null)
                {
                    rejected.Add(rejection);
                    continue;
                }

                candidates.Add(DeckQueryRecommendationEngine.BuildCandidate(
                    card,
                    role,
                    price,
                    roleRequirements,
                    tagRequirements,
                    maxPrice,
                    goal));
            }
        }

        result.Candidates = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.CardName)
            .Take(candidateLimit)
            .ToList();
        result.Rejected = rejected
            .OrderBy(candidate => candidate.CardName)
            .ToList();

        DeckQueryRecommendationEngine.AddWarnings(result, result.ExecutedQueries, candidates.Count, candidateLimit, searchLimit);
        return result;
    }

    /// <summary>
    /// Applies workspace-level filters to a card search request.
    /// </summary>
    private static CardSearchRequest NormalizeSearchRequest(
        CardSearchRequest request,
        string format,
        decimal? maxPrice)
    {
        return request.Preset switch
        {
            CardSearchPreset.RawQuery => CardSearchRequest.Raw(request.RawQuery ?? "", format, maxPrice),
            CardSearchPreset.Role => CardSearchRequest.ForRole(request.Role ?? "", request.Format ?? format, request.MaxPrice ?? maxPrice),
            CardSearchPreset.RecentCards => new CardSearchRequest
            {
                Preset = CardSearchPreset.RecentCards,
                Format = request.Format ?? format,
                MaxPrice = request.MaxPrice ?? maxPrice,
                Since = request.Since,
                SetCode = request.SetCode,
                Theme = request.Theme
            },
            _ => CardSearchRequest.ForPreset(
                request.Preset,
                request.Format ?? format,
                request.MaxPrice ?? maxPrice)
        };
    }

    /// <summary>
    /// Describes a search request without exposing adapter-owned query syntax.
    /// </summary>
    private static string DescribeSearchRequest(CardSearchRequest request)
    {
        if (request.Preset == CardSearchPreset.RawQuery)
        {
            return request.RawQuery?.Trim() ?? "";
        }

        if (request.Preset == CardSearchPreset.Role)
        {
            return string.IsNullOrWhiteSpace(request.Role)
                ? CardSearchPreset.Role.ToString()
                : $"Role:{request.Role}";
        }

        return request.Preset.ToString();
    }

    /// <summary>
    /// Saves a plan from ranked query candidates.
    /// </summary>
    private async Task<DeckEditPlan> SaveQueryPlanAsync(
        DeckWorkspace workspace,
        DeckQueryRecommendationResult ranking,
        string category,
        string goal,
        bool highPressureCuts,
        DeckIntent? intent,
        int requestedCount,
        string rationale,
        string name,
        string kind,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = CreatePlan(workspace, name, kind);
        plan.Rationale = rationale;

        foreach (DeckQueryCandidate candidate in ranking.Candidates)
        {
            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.AddCard,
                CardName = candidate.CardName,
                Quantity = 1,
                Category = string.IsNullOrWhiteSpace(category) ? candidate.Role : category,
                Rationale = candidate.Rationale
            });
        }

        int desiredHighPressureCuts = Math.Clamp(Math.Max(requestedCount, ranking.Candidates.Count), 1, 25);
        IEnumerable<DeckCard> cuts = highPressureCuts
            ? FindLessSaltyCutCandidates(workspace, desiredHighPressureCuts, intent)
            : FindGoalCutCandidates(workspace, ranking.Candidates.Count, intent);
        foreach (DeckCard cut in cuts)
        {
            plan.Operations.Add(new DeckEditOperation
            {
                Operation = DeckEditOperations.RemoveCard,
                CardName = cut.Name,
                Quantity = 1,
                Category = cut.PrimaryCategory,
                Rationale = highPressureCuts
                    ? "Cut a high-pressure card to make the deck less salty."
                    : $"Cut a lower-signal {cut.PrimaryCategory} card to make room for the query package."
            });
        }

        plan.Confidence = ranking.Candidates.Count == 0 ? 0 : ranking.Candidates.Average(candidate => candidate.Score);
        plan.Warnings.AddRange(ranking.Warnings);
        if (ranking.Candidates.Count == 0)
        {
            plan.Warnings.Add("No cards matched the query, deck constraints, and requested role/tag filters.");
        }

        return await RequirePlanRepository().SaveAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Normalizes role or tag filters.
    /// </summary>
    private static List<string> NormalizeTargets(IReadOnlyList<string>? targets)
    {
        return targets?
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Select(target => target.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];
    }

    /// <summary>
    /// Checks whether plan cuts should target high-pressure cards.
    /// </summary>
    private static bool UseHighPressureCuts(string cutsStrategy)
    {
        return cutsStrategy.Equals("high-pressure", StringComparison.OrdinalIgnoreCase)
            || cutsStrategy.Equals("salt", StringComparison.OrdinalIgnoreCase)
            || cutsStrategy.Equals("less-salty", StringComparison.OrdinalIgnoreCase);
    }
}
