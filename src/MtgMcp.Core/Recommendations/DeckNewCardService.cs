namespace MtgMcp.Core;

/// <summary>
/// Finds recently released cards that fit a saved deck.
/// </summary>
public sealed class DeckNewCardService
{
    /// <summary>
    /// Loads local workspaces for recent-card radar.
    /// </summary>
    private readonly IDeckWorkspaceRepository repository;

    /// <summary>
    /// Searches and resolves catalog card metadata.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Supplies dedicated recent-card suggestions when configured.
    /// </summary>
    private readonly ICardTrendProvider? cardTrendProvider;

    /// <summary>
    /// Overrides today's date for deterministic release-radar tests.
    /// </summary>
    private readonly DateOnly? currentDateOverride;

    /// <summary>
    /// Creates a recent-card collaborator with explicit storage, catalog, and trend dependencies.
    /// </summary>
    public DeckNewCardService(
        IDeckWorkspaceRepository repository,
        ICardCatalog cardCatalog,
        ICardTrendProvider? cardTrendProvider = null,
        DateOnly? currentDateOverride = null)
    {
        this.repository = repository;
        this.cardCatalog = cardCatalog;
        this.cardTrendProvider = cardTrendProvider;
        this.currentDateOverride = currentDateOverride;
    }

    /// <summary>
    /// Finds recently released cards that fit a deck.
    /// </summary>
    public async Task<NewCardsForDeckResult> FindNewCardsForDeckAsync(
        string workspaceId,
        string? since,
        string? setCode,
        int limit,
        decimal? maxPrice,
        CancellationToken cancellationToken)
    {
        DeckWorkspace workspace = await LoadWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        DeckIntent? intent = DeckIntentText.Extract(workspace.Description, workspace.Id).Intent;
        CardTrendQuery query = new()
        {
            Format = workspace.Format,
            Theme = intent?.Archetype ?? DominantTheme(workspace),
            Since = ParseDateOnly(since) ?? DefaultRecentReleaseDate(),
            SetCode = string.IsNullOrWhiteSpace(setCode) ? null : setCode.Trim(),
            Limit = Math.Clamp(limit, 1, 50),
            MaxPrice = maxPrice
        };
        IReadOnlyList<NewCardSuggestion> suggestions;
        List<string> notes = [];
        if (cardTrendProvider is null)
        {
            suggestions = await FindNewCardsViaCatalogAsync(workspace, query, cancellationToken).ConfigureAwait(false);
            notes.Add("No dedicated card trend provider is configured; queried recent cards through the card catalog.");
        }
        else
        {
            try
            {
                IReadOnlyList<NewCardSuggestion> providerSuggestions = await cardTrendProvider
                    .FindNewCardsAsync(query, cancellationToken)
                    .ConfigureAwait(false);
                suggestions = await ValidateProviderNewCardsAsync(workspace, query, providerSuggestions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCancellation(exception))
            {
                suggestions = await FindNewCardsViaCatalogAsync(workspace, query, cancellationToken).ConfigureAwait(false);
                notes.Add($"Card trend provider failed; using Scryfall catalog fallback. {exception.GetType().Name}: {exception.Message}");
            }
        }

        HashSet<string> existing = workspace.Cards.Select(card => card.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        NewCardsForDeckResult result = new()
        {
            WorkspaceId = workspace.Id,
            Suggestions = suggestions
                .Where(card => !existing.Contains(card.CardName))
                .Where(card => IsPriceWithinBudget(card.Price, query.MaxPrice))
                .OrderByDescending(card => card.Score)
                .Take(query.Limit)
                .ToList()
        };
        if (string.IsNullOrWhiteSpace(since))
        {
            result.Notes.Add($"No since date supplied; using cards released on or after {query.Since.Value:yyyy-MM-dd}.");
        }

        result.Notes.AddRange(notes);

        return result;
    }

    /// <summary>
    /// Finds recent cards through the generic card catalog.
    /// </summary>
    private async Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsViaCatalogAsync(
        DeckWorkspace workspace,
        CardTrendQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CardSearchResult> results = await cardCatalog
            .SearchCardsAsync(CardSearchRequest.Recent(query), Math.Clamp(query.Limit * 3, 10, 75), cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, CardSearchResult> searchByName = results
            .GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog
            .GetCardsByNamesAsync(results.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);
        (bool colorKnown, HashSet<string> colors) = DeckRecommendationCardFacts.GetDeckColorIdentity(workspace);
        return cards.Values
            .Select(card => (Card: card, SearchResult: FindSearchResult(card.Name, searchByName)))
            .Where(item => MatchesTrendMetadata(item.SearchResult, item.Card, query))
            .Select(item => item.Card)
            .Where(card => DeckRecommendationCardFacts.IsLegalInFormat(card, workspace.Format))
            .Where(card => DeckRecommendationCardFacts.IsInDeckColorIdentity(card, colorKnown, colors))
            .Where(card => IsPriceWithinBudget(DeckRecommendationCardFacts.ReadUsdPrice(card), query.MaxPrice))
            .Select(card => BuildNewCardSuggestion(workspace, card, query.Theme, FindSearchResult(card.Name, searchByName)))
            .OrderByDescending(card => card.Score)
            .Take(query.Limit)
            .ToList();
    }

    /// <summary>
    /// Validates provider trend candidates against deck-local legality, color, date, and budget rules.
    /// </summary>
    private async Task<IReadOnlyList<NewCardSuggestion>> ValidateProviderNewCardsAsync(
        DeckWorkspace workspace,
        CardTrendQuery query,
        IReadOnlyList<NewCardSuggestion> providerSuggestions,
        CancellationToken cancellationToken)
    {
        List<string> names = providerSuggestions
            .Select(suggestion => suggestion.CardName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return [];
        }

        IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog.GetCardsByNamesAsync(names, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, NewCardSuggestion> providerByName = providerSuggestions
            .GroupBy(suggestion => suggestion.CardName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(suggestion => suggestion.Score).First(), StringComparer.OrdinalIgnoreCase);
        (bool colorKnown, HashSet<string> colors) = DeckRecommendationCardFacts.GetDeckColorIdentity(workspace);
        List<NewCardSuggestion> validated = [];

        foreach (KeyValuePair<string, CardInfo> item in cards)
        {
            NewCardSuggestion providerSuggestion = providerByName.TryGetValue(item.Key, out NewCardSuggestion? suggestion)
                ? suggestion
                : providerByName.Values.FirstOrDefault(candidate => candidate.CardName.Equals(item.Value.Name, StringComparison.OrdinalIgnoreCase))
                    ?? new NewCardSuggestion { CardName = item.Value.Name };
            DateOnly? releasedAt = providerSuggestion.ReleasedAt ?? item.Value.ReleasedAt;
            string? set = providerSuggestion.Set ?? item.Value.Set;
            CardPriceEvaluation price = DeckRecommendationCardFacts.EvaluateUsdPrice(item.Value);
            if (!DeckRecommendationCardFacts.IsLegalInFormat(item.Value, workspace.Format)
                || !DeckRecommendationCardFacts.IsInDeckColorIdentity(item.Value, colorKnown, colors)
                || !MatchesTrendMetadata(releasedAt, set, query)
                || !IsPriceWithinBudget(price.Price, query.MaxPrice))
            {
                continue;
            }

            NewCardSuggestion localFit = BuildNewCardSuggestion(workspace, item.Value, query.Theme);
            localFit.ReleasedAt = releasedAt;
            localFit.Set = set;
            localFit.Price = price.Price;
            localFit.ScryfallUri = item.Value.ScryfallUri ?? providerSuggestion.ScryfallUri;
            localFit.Score = Math.Max(localFit.Score, providerSuggestion.Score);
            localFit.Rationale = string.IsNullOrWhiteSpace(providerSuggestion.Rationale)
                ? localFit.Rationale
                : providerSuggestion.Rationale;
            validated.Add(localFit);
        }

        return validated;
    }

    /// <summary>
    /// Finds release metadata from a search result map.
    /// </summary>
    private static CardSearchResult? FindSearchResult(string cardName, IReadOnlyDictionary<string, CardSearchResult> searchByName)
    {
        return searchByName.TryGetValue(cardName, out CardSearchResult? result) ? result : null;
    }

    /// <summary>
    /// Checks release and set metadata for a catalog card.
    /// </summary>
    private static bool MatchesTrendMetadata(CardSearchResult? searchResult, CardInfo card, CardTrendQuery query)
    {
        return MatchesTrendMetadata(searchResult?.ReleasedAt ?? card.ReleasedAt, searchResult?.Set ?? card.Set, query);
    }

    /// <summary>
    /// Checks release and set metadata for a trend candidate.
    /// </summary>
    private static bool MatchesTrendMetadata(DateOnly? releasedAt, string? set, CardTrendQuery query)
    {
        if (query.Since.HasValue && (!releasedAt.HasValue || releasedAt.Value < query.Since.Value))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(query.SetCode)
            || (!string.IsNullOrWhiteSpace(set) && set.Equals(query.SetCode, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks whether a known price is within a requested cap.
    /// </summary>
    private static bool IsPriceWithinBudget(decimal? price, decimal? maxPrice)
    {
        return !maxPrice.HasValue || (price.HasValue && price.Value <= maxPrice.Value);
    }

    /// <summary>
    /// Builds a recent-card suggestion.
    /// </summary>
    private static NewCardSuggestion BuildNewCardSuggestion(
        DeckWorkspace workspace,
        CardInfo card,
        string? theme,
        CardSearchResult? searchResult = null)
    {
        DeckCard candidate = DeckRecommendationCardFacts.CreateCandidateCard(card);
        CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
        double themeScore = string.IsNullOrWhiteSpace(theme)
            ? 0.3
            : role.Tags.Any(tag => theme.Contains(tag, StringComparison.OrdinalIgnoreCase)) ? 0.9 : 0.45;
        double roleScore = role.PrimaryRole is DeckRoles.Utility ? 0.35 : 0.7;
        double rankScore = card.EdhrecRank switch
        {
            null => 0.4,
            <= 1_000 => 0.9,
            <= 5_000 => 0.7,
            <= 10_000 => 0.5,
            _ => 0.3
        };
        return new NewCardSuggestion
        {
            CardName = card.Name,
            Role = role.PrimaryRole,
            Tags = role.Tags,
            ReleasedAt = searchResult?.ReleasedAt ?? card.ReleasedAt,
            Set = searchResult?.Set ?? card.Set,
            Price = DeckRecommendationCardFacts.ReadUsdPrice(card),
            ScryfallUri = card.ScryfallUri ?? searchResult?.ScryfallUri,
            Score = Math.Clamp((themeScore * 0.40) + (roleScore * 0.35) + (rankScore * 0.25), 0, 1),
            Rationale = $"{card.Name} is a recent {role.PrimaryRole} candidate for {workspace.Name}."
        };
    }

    /// <summary>
    /// Finds a dominant theme from deck role tags.
    /// </summary>
    private static string? DominantTheme(DeckWorkspace workspace)
    {
        Dictionary<string, int> tags = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeckCard card in DeckServiceHelpers.IncludedCards(workspace))
        {
            foreach (string tag in DeckRoleClassifier.Classify(card).Tags)
            {
                DeckServiceHelpers.AddCount(tags, tag, card.Quantity);
            }
        }

        return tags.OrderByDescending(pair => pair.Value).FirstOrDefault().Key;
    }

    /// <summary>
    /// Parses an optional date value.
    /// </summary>
    private static DateOnly? ParseDateOnly(string? value)
    {
        return DateOnly.TryParse(value, out DateOnly date) ? date : null;
    }

    /// <summary>
    /// Gets the current UTC date or the test override.
    /// </summary>
    private DateOnly CurrentDate()
    {
        return currentDateOverride ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
    }

    /// <summary>
    /// Gets the default recent-release lower bound.
    /// </summary>
    private DateOnly DefaultRecentReleaseDate()
    {
        return CurrentDate().AddYears(-1);
    }

    /// <summary>
    /// Checks whether an exception represents cooperative cancellation.
    /// </summary>
    private static bool IsCancellation(Exception exception)
    {
        return exception is OperationCanceledException;
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
