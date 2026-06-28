namespace MtgMcp.Core;

/// <summary>
/// Finds deterministic Scryfall payoff candidates for classified win-condition routes.
/// </summary>
public sealed class DeckWinconPayoffSearchService
{
    /// <summary>
    /// Searches and resolves catalog card metadata.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Creates a payoff-search collaborator with an explicit catalog dependency.
    /// </summary>
    public DeckWinconPayoffSearchService(ICardCatalog cardCatalog)
    {
        this.cardCatalog = cardCatalog;
    }

    /// <summary>
    /// Finds payoff candidates for a route using deterministic Scryfall queries.
    /// </summary>
    public async Task<WinconPayoffSearchResult> FindWinconPayoffsAsync(
        string route,
        string colorIdentity,
        string format,
        decimal? maxPrice,
        int limit,
        CancellationToken cancellationToken)
    {
        string normalizedRoute = NormalizeRoute(route);
        if (!WinRouteLabels.All.Contains(normalizedRoute, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("route must be one of the approved win-route labels.", nameof(route));
        }

        string normalizedFormat = DeckRecommendationCardFacts.NormalizeFormat(format);
        HashSet<string> colors = NormalizeColorIdentity(colorIdentity);
        string query = BuildPayoffQuery(normalizedRoute, colors, normalizedFormat, maxPrice);
        int boundedLimit = Math.Clamp(limit, 1, 50);
        IReadOnlyList<CardSearchResult> searchResults = await cardCatalog.SearchCardsAsync(
            query,
            Math.Clamp(boundedLimit * 3, 10, 100),
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> details = await cardCatalog.GetCardsByNamesAsync(
            DistinctCardNames(searchResults),
            cancellationToken).ConfigureAwait(false);

        List<string> sortedColors = colors.ToList();
        sortedColors.Sort(StringComparer.OrdinalIgnoreCase);
        WinconPayoffSearchResult result = new()
        {
            Route = normalizedRoute,
            ColorIdentity = sortedColors,
            Format = normalizedFormat,
            ScryfallQuery = query
        };
        foreach (CardSearchResult searchResult in searchResults)
        {
            if (!details.TryGetValue(searchResult.Name, out CardInfo? card))
            {
                continue;
            }

            bool legal = DeckRecommendationCardFacts.IsLegalInFormat(card, normalizedFormat);
            bool colorOk = DeckRecommendationCardFacts.IsInDeckColorIdentity(card, colorIdentityKnown: true, colors);
            if (!legal
                || !colorOk
                || (maxPrice.HasValue && DeckRecommendationCardFacts.ReadUsdPrice(card).GetValueOrDefault(decimal.MaxValue) > maxPrice.Value))
            {
                continue;
            }

            result.Candidates.Add(new WinconPayoffCandidate
            {
                CardName = card.Name,
                WhyItMatches = $"{card.Name} matched the fixed {normalizedRoute} Scryfall payoff query.",
                LegalInFormat = legal,
                ColorIdentityOk = colorOk,
                Price = DeckRecommendationCardFacts.ReadUsdPrice(card),
                EdhrecRank = card.EdhrecRank,
                ScryfallUri = card.ScryfallUri,
                Metadata = BuildMetadata("scryfall", "payoff-candidate-search", card.ScryfallUri, confidence: 0.70)
            });
            if (result.Candidates.Count >= boundedLimit)
            {
                break;
            }
        }

        result.Notes.Add("Payoff rows are Scryfall-query-derived candidates, not popularity evidence unless joined with aggregate source rows.");
        return result;
    }

    /// <summary>
    /// Extracts unique card names from search results while preserving first-seen order.
    /// </summary>
    private static List<string> DistinctCardNames(IReadOnlyList<CardSearchResult> searchResults)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> names = [];
        foreach (CardSearchResult searchResult in searchResults)
        {
            if (seen.Add(searchResult.Name))
            {
                names.Add(searchResult.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Normalizes a route label.
    /// </summary>
    private static string NormalizeRoute(string route)
    {
        return route.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes color identity text such as WUBRG, U,B, or colorless.
    /// </summary>
    private static HashSet<string> NormalizeColorIdentity(string colorIdentity)
    {
        HashSet<string> colors = new(StringComparer.OrdinalIgnoreCase);
        foreach (char character in colorIdentity.ToUpperInvariant())
        {
            string color = character.ToString();
            if ("WUBRG".Contains(color, StringComparison.Ordinal))
            {
                colors.Add(color);
            }
        }

        return colors;
    }

    /// <summary>
    /// Builds a Scryfall query for payoff candidates.
    /// </summary>
    private static string BuildPayoffQuery(string route, HashSet<string> colors, string format, decimal? maxPrice)
    {
        string expression = route switch
        {
            WinRouteLabels.InfiniteMana => "(o:\"{X}\" or o:\"x damage\" or o:\"draw x\" or o:\"each opponent loses x\")",
            WinRouteLabels.Storm => "(o:storm or o:\"copy target instant\" or o:\"copy target sorcery\" or o:\"whenever you cast\")",
            WinRouteLabels.DrawDeck => "(o:\"win the game\" or o:\"if you would draw\" or o:\"no cards in your library\")",
            WinRouteLabels.SelfMill => "(o:\"win the game\" o:graveyard or o:\"no cards in your library\")",
            WinRouteLabels.Etb => "(o:\"whenever\" o:\"enters the battlefield\" o:\"each opponent\")",
            WinRouteLabels.Tokens => "(o:\"tokens you control\" or o:\"creatures you control get\" or o:\"whenever you create\")",
            WinRouteLabels.Aristocrats => "(o:\"whenever\" o:dies o:\"each opponent loses\" or o:sacrifice o:\"each opponent loses\")",
            WinRouteLabels.Combat or WinRouteLabels.ValueCombat => "(o:\"creatures you control get\" or o:\"extra combat\" or o:trample)",
            WinRouteLabels.OpponentMill => "(o:\"each opponent mills\" or o:\"target opponent mills\")",
            WinRouteLabels.ExtraTurns => "(o:\"extra turn\" or o:\"additional turn\")",
            WinRouteLabels.AlternateWin => "o:\"win the game\"",
            _ => ""
        };
        List<string> parts = [expression, $"legal:{format}"];
        if (colors.Count > 0)
        {
            List<string> sortedColors = colors.ToList();
            sortedColors.Sort(StringComparer.OrdinalIgnoreCase);
            parts.Add($"id<={string.Concat(sortedColors).ToLowerInvariant()}");
        }
        else
        {
            parts.Add("id<=c");
        }

        if (maxPrice.HasValue)
        {
            parts.Add($"usd<={maxPrice.Value:0.##}");
        }

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// Builds source metadata for deterministic evidence rows.
    /// </summary>
    private static SourceEvidenceMetadata BuildMetadata(
        string source,
        string sourceKind,
        string? sourceUri,
        double confidence)
    {
        return new SourceEvidenceMetadata
        {
            Source = source,
            SourceKind = sourceKind,
            SourceUri = sourceUri,
            CacheStatus = "live-or-cache",
            Confidence = Math.Clamp(confidence, 0, 1),
            Deterministic = true
        };
    }
}
