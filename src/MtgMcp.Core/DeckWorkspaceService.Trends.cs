namespace MtgMcp.Core;

/// <summary>
/// Provides recent-card radar behavior.
/// </summary>
public sealed partial class DeckWorkspaceService
{
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
            Since = ParseDateOnly(since),
            SetCode = string.IsNullOrWhiteSpace(setCode) ? null : setCode.Trim(),
            Limit = Math.Clamp(limit, 1, 50),
            MaxPrice = maxPrice
        };
        IReadOnlyList<NewCardSuggestion> suggestions;
        List<string> notes = [];
        if (cardTrendProvider is null)
        {
            suggestions = await FindNewCardsViaCatalogAsync(workspace, query, cancellationToken).ConfigureAwait(false);
            notes.Add("No dedicated card trend provider is configured; queried Scryfall syntax through the card catalog.");
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
        string search = BuildTrendSearchQuery(query);
        IReadOnlyList<CardSearchResult> results = await cardCatalog.SearchCardsAsync(search, Math.Clamp(query.Limit * 3, 10, 75), cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, CardSearchResult> searchByName = results
            .GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog
            .GetCardsByNamesAsync(results.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);
        (bool colorKnown, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        return cards.Values
            .Select(card => (Card: card, SearchResult: FindSearchResult(card.Name, searchByName)))
            .Where(item => MatchesTrendMetadata(item.SearchResult, item.Card, query))
            .Select(item => item.Card)
            .Where(card => IsLegalInFormat(card, workspace.Format))
            .Where(card => IsInDeckColorIdentity(card, colorKnown, colors))
            .Where(card => IsPriceWithinBudget(ReadUsdPrice(card), query.MaxPrice))
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
        (bool colorKnown, HashSet<string> colors) = GetDeckColorIdentity(workspace);
        List<NewCardSuggestion> validated = [];

        foreach (KeyValuePair<string, CardInfo> item in cards)
        {
            NewCardSuggestion providerSuggestion = providerByName.TryGetValue(item.Key, out NewCardSuggestion? suggestion)
                ? suggestion
                : providerByName.Values.FirstOrDefault(candidate => candidate.CardName.Equals(item.Value.Name, StringComparison.OrdinalIgnoreCase))
                    ?? new NewCardSuggestion { CardName = item.Value.Name };
            DateOnly? releasedAt = providerSuggestion.ReleasedAt ?? item.Value.ReleasedAt;
            string? set = providerSuggestion.Set ?? item.Value.Set;
            decimal? price = ReadUsdPrice(item.Value) ?? providerSuggestion.Price;
            if (!IsLegalInFormat(item.Value, workspace.Format)
                || !IsInDeckColorIdentity(item.Value, colorKnown, colors)
                || !MatchesTrendMetadata(releasedAt, set, query)
                || !IsPriceWithinBudget(price, query.MaxPrice))
            {
                continue;
            }

            NewCardSuggestion localFit = BuildNewCardSuggestion(workspace, item.Value, query.Theme);
            localFit.ReleasedAt = releasedAt;
            localFit.Set = set;
            localFit.Price = price;
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
    /// Builds a Scryfall syntax search query for trend lookup.
    /// </summary>
    private static string BuildTrendSearchQuery(CardTrendQuery query)
    {
        List<string> parts = [$"legal:{NormalizeFormat(query.Format)}"];
        if (query.Since.HasValue)
        {
            parts.Add($"date>={query.Since.Value:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(query.SetCode))
        {
            parts.Add($"set:{query.SetCode}");
        }

        if (query.MaxPrice.HasValue)
        {
            parts.Add($"usd<={query.MaxPrice.Value:0.##}");
        }

        if (!string.IsNullOrWhiteSpace(query.Theme))
        {
            parts.Add(ThemeSearchFragment(query.Theme));
        }

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// Builds a rough Scryfall search fragment from a theme.
    /// </summary>
    private static string ThemeSearchFragment(string theme)
    {
        string normalized = theme.ToLowerInvariant();
        if (normalized.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:create o:token)";
        }

        if (normalized.Contains("discard", StringComparison.OrdinalIgnoreCase))
        {
            return "o:discard";
        }

        if (normalized.Contains("grave", StringComparison.OrdinalIgnoreCase) || normalized.Contains("reanim", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:graveyard or o:reanimate or o:\"return target creature\")";
        }

        if (normalized.Contains("aristocrat", StringComparison.OrdinalIgnoreCase) || normalized.Contains("sacrifice", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:sacrifice or o:\"whenever a creature dies\")";
        }

        return "";
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
        DeckCard candidate = CreateCandidateCard(card);
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
            Price = ReadUsdPrice(card),
            Score = Math.Clamp((themeScore * 0.40) + (roleScore * 0.35) + (rankScore * 0.25), 0, 1),
            Rationale = $"{card.Name} is a recent {role.PrimaryRole} candidate for {workspace.Name}."
        };
    }

}
