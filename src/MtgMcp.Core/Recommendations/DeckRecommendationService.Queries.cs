namespace MtgMcp.Core;

/// <summary>
/// Provides deck-aware data lookup for caller-supplied Scryfall queries.
/// </summary>
public sealed partial class DeckRecommendationService : DeckServiceBase
{
    /// <summary>
    /// Gets cards from a Scryfall query after applying deck legality, color, budget, and caller-supplied role filters.
    /// </summary>
    public async Task<DeckQueryDataResult> QueryCardsForDeckAsync(
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
        return await QueryCardsForDeckQueriesAsync(
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
    /// Gets cards from one or more Scryfall queries without scoring or saving a plan.
    /// </summary>
    private async Task<DeckQueryDataResult> QueryCardsForDeckQueriesAsync(
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
        List<DeckQueryDataCard> cardsInSourceOrder = [];
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

        DeckQueryDataResult result = new()
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
            IReadOnlyDictionary<string, CardInfo> cardDetails = await CardCatalog
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

                if (!cardDetails.TryGetValue(searchResult.Name, out CardInfo? card))
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

                cardsInSourceOrder.Add(new DeckQueryDataCard
                {
                    CardName = card.Name,
                    Role = role.PrimaryRole,
                    Tags = role.Tags,
                    ManaValue = card.ManaValue,
                    TypeLine = card.TypeLine,
                    OracleText = card.OracleText,
                    ColorIdentity = card.ColorIdentity.ToList(),
                    EdhrecRank = card.EdhrecRank,
                    Price = price,
                    ScryfallUri = card.ScryfallUri
                });
            }
        }

        result.Cards = cardsInSourceOrder.Take(candidateLimit).ToList();
        result.Rejected = rejected;
        AddQueryDataWarnings(result, result.ExecutedQueries, cardsInSourceOrder.Count, candidateLimit, searchLimit);
        return result;
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

        plan.Confidence = ranking.Candidates.Count == 0 ? 0 : ranking.Candidates.Average(candidate => candidate.Score);
        plan.Warnings.AddRange(ranking.Warnings);
        plan.Warnings.Add("No cuts were generated; query plans add explicit search results only.");
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
    /// Adds query quality warnings to a data-only query result.
    /// </summary>
    private static void AddQueryDataWarnings(
        DeckQueryDataResult result,
        IReadOnlyList<string> queries,
        int acceptedCount,
        int requestedCount,
        int searchLimit)
    {
        if (queries.All(string.IsNullOrWhiteSpace))
        {
            result.Warnings.Add("The Scryfall query was empty; only automatic deck constraints were available.");
        }

        if (acceptedCount == 0)
        {
            result.Warnings.Add("No searched cards survived deck constraints and role/tag filters.");
        }
        else if (acceptedCount < requestedCount)
        {
            result.Warnings.Add($"Only {acceptedCount} card(s) survived the filters for {requestedCount} requested card(s).");
        }

        if (result.Rejected.Count >= searchLimit)
        {
            result.Warnings.Add("Many search hits were rejected; the query may be too broad for the requested filters.");
        }
    }
}
