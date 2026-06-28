using System.Text.Json;

namespace MtgMcp.Core;

/// <summary>
/// Provides deck-aware data lookup for caller-supplied Scryfall queries.
/// </summary>
public sealed class DeckQueryService
{
    /// <summary>
    /// Loads local workspaces for query filtering.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Resolves card searches and metadata for query candidates.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Persists generated query plans when plan tools are enabled.
    /// </summary>
    private readonly IDeckPlanRepository? planRepository;

    /// <summary>
    /// Creates a query collaborator with explicit storage and catalog dependencies.
    /// </summary>
    public DeckQueryService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        IDeckPlanRepository? planRepository = null)
    {
        this.repository = repository;
        this.cardCatalog = cardCatalog;
        this.planRepository = planRepository;
    }

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
        string format = DeckRecommendationCardFacts.NormalizeFormat(workspace.Format);
        (bool colorKnown, HashSet<string> colors) = DeckRecommendationCardFacts.GetDeckColorIdentity(workspace);
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
            IReadOnlyList<CardSearchResult> searchResults;
            IReadOnlyDictionary<string, CardInfo> cardDetails;
            try
            {
                searchResults = await cardCatalog
                    .SearchCardsAsync(effectiveRequest, searchLimit, cancellationToken)
                    .ConfigureAwait(false);
                cardDetails = await cardCatalog
                    .GetCardsByNamesAsync(
                        searchResults.Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDeckQueryCatalogException(exception, cancellationToken))
            {
                result.Errors.Add(BuildQueryProviderError(executedQuery, exception));
                continue;
            }

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
                        ScryfallUri = searchResult.ScryfallUri,
                        Reasons = ["Card details were not available from the catalog."]
                    });
                    continue;
                }

                DeckCard candidateCard = DeckRecommendationCardFacts.CreateCandidateCard(card);
                CardRoleAssignment role = DeckRoleClassifier.Classify(candidateCard);
                CardPriceEvaluation priceEvaluation = DeckRecommendationCardFacts.EvaluateUsdPrice(card);
                decimal? price = priceEvaluation.PriceKnown ? priceEvaluation.Price : null;
                DeckQueryRejectedCandidate? rejection = DeckQueryRecommendationEngine.BuildRejection(
                    card,
                    candidateCard,
                    role,
                    price,
                    evaluationContext);
                if (rejection is not null)
                {
                    ApplyQueryMetadata(rejection, card, priceEvaluation, format);
                    rejected.Add(rejection);
                    continue;
                }

                DeckQueryDataCard dataCard = new()
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
                    ScryfallUri = card.ScryfallUri,
                    MatchRationale = BuildQueryMatchRationale(card, role, price, evaluationContext)
                };
                ApplyQueryMetadata(dataCard, card, priceEvaluation, format);
                cardsInSourceOrder.Add(dataCard);
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
    internal async Task<DeckQueryRecommendationResult> RankCardsForDeckQueriesAsync(
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
        string format = DeckRecommendationCardFacts.NormalizeFormat(workspace.Format);
        (bool colorKnown, HashSet<string> colors) = DeckRecommendationCardFacts.GetDeckColorIdentity(workspace);
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
            IReadOnlyList<CardSearchResult> searchResults;
            IReadOnlyDictionary<string, CardInfo> cards;
            try
            {
                searchResults = await cardCatalog
                    .SearchCardsAsync(effectiveRequest, searchLimit, cancellationToken)
                    .ConfigureAwait(false);
                cards = await cardCatalog
                    .GetCardsByNamesAsync(
                        searchResults.Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDeckQueryCatalogException(exception, cancellationToken))
            {
                result.Errors.Add(BuildQueryProviderError(executedQuery, exception));
                continue;
            }

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
                        ScryfallUri = searchResult.ScryfallUri,
                        Reasons = ["Card details were not available from the catalog."]
                    });
                    continue;
                }

                DeckCard candidateCard = DeckRecommendationCardFacts.CreateCandidateCard(card);
                CardRoleAssignment role = DeckRoleClassifier.Classify(candidateCard);
                CardPriceEvaluation priceEvaluation = DeckRecommendationCardFacts.EvaluateUsdPrice(card);
                decimal? price = priceEvaluation.PriceKnown ? priceEvaluation.Price : null;
                DeckQueryRejectedCandidate? rejection = DeckQueryRecommendationEngine.BuildRejection(
                    card,
                    candidateCard,
                    role,
                    price,
                    evaluationContext);
                if (rejection is not null)
                {
                    ApplyQueryMetadata(rejection, card, priceEvaluation, format);
                    rejected.Add(rejection);
                    continue;
                }

                DeckQueryCandidate candidate = DeckQueryRecommendationEngine.BuildCandidate(
                    card,
                    candidateCard,
                    role,
                    price,
                    roleRequirements,
                    tagRequirements,
                    maxPrice,
                    goal);
                ApplyQueryMetadata(candidate, card, priceEvaluation, format);
                candidates.Add(candidate);
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
    internal async Task<DeckEditPlan> SaveQueryPlanAsync(
        DeckWorkspace workspace,
        DeckQueryRecommendationResult ranking,
        string category,
        string rationale,
        string name,
        string kind,
        CancellationToken cancellationToken)
    {
        DeckEditPlan plan = DeckServiceHelpers.CreatePlan(workspace, name, kind);
        plan.Rationale = rationale;

        foreach (DeckQueryCandidate candidate in ranking.Candidates)
        {
            plan.Operations.Add(DeckEditOperation.AddCard(
                candidate.CardName,
                1,
                string.IsNullOrWhiteSpace(category) ? candidate.Role : category,
                candidate.Rationale));
        }

        plan.Confidence = ranking.Candidates.Count == 0 ? 0 : ranking.Candidates.Average(candidate => candidate.Score);
        plan.Warnings.AddRange(ranking.Errors);
        plan.Warnings.AddRange(ranking.Warnings);
        plan.Warnings.Add("No cuts were generated; query plans add explicit search results only.");
        if (ranking.Candidates.Count == 0)
        {
            plan.Warnings.Add("No cards matched the query, deck constraints, and requested role/tag filters.");
        }

        return await DeckServiceHelpers.RequirePlanRepository(planRepository).SaveAsync(plan, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Builds a concise explanation for accepted query rows.
    /// </summary>
    private static string BuildQueryMatchRationale(
        CardInfo card,
        CardRoleAssignment role,
        decimal? price,
        DeckQueryEvaluationContext context)
    {
        List<string> reasons = [$"Matched role {role.PrimaryRole}"];
        if (role.Tags.Count > 0)
        {
            reasons.Add($"tags {string.Join(", ", role.Tags.Take(3))}");
        }

        if (price.HasValue && context.MaxPrice.HasValue)
        {
            reasons.Add($"price {price.Value:0.##} <= {context.MaxPrice.Value:0.##}");
        }

        if (context.ColorIdentityKnown)
        {
            reasons.Add("within deck color identity");
        }

        if (DeckRecommendationCardFacts.IsLegalInFormat(card, context.Format))
        {
            reasons.Add($"legal in {context.Format}");
        }

        return string.Join("; ", reasons) + ".";
    }

    /// <summary>
    /// Checks whether a catalog exception should become a structured query result error.
    /// </summary>
    private static bool IsDeckQueryCatalogException(Exception exception, CancellationToken cancellationToken)
    {
        return exception is HttpRequestException
            || exception is InvalidOperationException
            || exception is JsonException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
    }

    /// <summary>
    /// Builds a sanitized provider error for query result output.
    /// </summary>
    private static string BuildQueryProviderError(string executedQuery, Exception exception)
    {
        return $"Scryfall query '{executedQuery}' failed: {exception.Message}";
    }

    /// <summary>
    /// Adds selected-printing and pricing metadata to a data row.
    /// </summary>
    private static void ApplyQueryMetadata(
        DeckQueryDataCard target,
        CardInfo card,
        CardPriceEvaluation price,
        string format)
    {
        target.Set = card.Set;
        target.CollectorNumber = card.CollectorNumber;
        target.ReleasedAt = card.ReleasedAt;
        target.IsReleased = IsReleased(card);
        target.Legality = ReadLegality(card, format);
        target.Price = price.PriceKnown ? price.Price : null;
        target.PriceKnown = price.PriceKnown;
        target.PriceSource = price.PriceSource;
        target.PricingMode = card.PricingMode;
        target.PrintingStatus = price.PrintingStatus;
        target.SelectedPrintingReason = price.SelectedPrintingReason;
        target.Legalities = new Dictionary<string, string>(card.Legalities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds selected-printing and pricing metadata to a scored candidate row.
    /// </summary>
    private static void ApplyQueryMetadata(
        DeckQueryCandidate target,
        CardInfo card,
        CardPriceEvaluation price,
        string format)
    {
        target.Set = card.Set;
        target.CollectorNumber = card.CollectorNumber;
        target.ReleasedAt = card.ReleasedAt;
        target.IsReleased = IsReleased(card);
        target.Legality = ReadLegality(card, format);
        target.Price = price.PriceKnown ? price.Price : null;
        target.PriceKnown = price.PriceKnown;
        target.PriceSource = price.PriceSource;
        target.PricingMode = card.PricingMode;
        target.PrintingStatus = price.PrintingStatus;
        target.SelectedPrintingReason = price.SelectedPrintingReason;
        target.Legalities = new Dictionary<string, string>(card.Legalities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds selected-printing and pricing metadata to a rejected candidate row.
    /// </summary>
    private static void ApplyQueryMetadata(
        DeckQueryRejectedCandidate target,
        CardInfo card,
        CardPriceEvaluation price,
        string format)
    {
        target.Set = card.Set;
        target.CollectorNumber = card.CollectorNumber;
        target.ReleasedAt = card.ReleasedAt;
        target.IsReleased = IsReleased(card);
        target.Legality = ReadLegality(card, format);
        target.Price = price.PriceKnown ? price.Price : null;
        target.PriceKnown = price.PriceKnown;
        target.PriceSource = price.PriceSource;
        target.PricingMode = card.PricingMode;
        target.PrintingStatus = price.PrintingStatus;
        target.SelectedPrintingReason = price.SelectedPrintingReason;
        target.Legalities = new Dictionary<string, string>(card.Legalities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads a format legality value when Scryfall provided one.
    /// </summary>
    private static string? ReadLegality(CardInfo card, string format)
    {
        return card.Legalities.TryGetValue(format, out string? legality)
            ? legality
            : null;
    }

    /// <summary>
    /// Checks whether the selected printing is already released.
    /// </summary>
    private static bool IsReleased(CardInfo card)
    {
        return !card.ReleasedAt.HasValue
            || card.ReleasedAt.Value <= DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }

    /// <summary>
    /// Loads a workspace by id or throws when it is unknown.
    /// </summary>
    private async Task<DeckWorkspace> LoadWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        DeckWorkspace? workspace = await repository
            .GetAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);
        return workspace
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }
}
