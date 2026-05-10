using System.Globalization;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Produces normalized corpus signals from Scryfall card search and EDHREC rank metadata.
/// </summary>
public sealed class ScryfallCorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the card catalog used for Scryfall lookups.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Stores the shared source-fact cache.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores mtg-mcp options.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a Scryfall corpus signal provider.
    /// </summary>
    public ScryfallCorpusSignalProvider(
        ICardCatalog cardCatalog,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options)
    {
        this.cardCatalog = cardCatalog;
        this.cache = cache;
        this.options = options.Value;
    }

    /// <summary>
    /// Gets source capability and attribution status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        return new CorpusSourceStatus
        {
            Key = "scryfall-edhrec-rank",
            Name = "Scryfall EDHREC-rank search",
            Kind = "card-metadata",
            Enabled = sourceOptions.Enabled,
            StableApi = true,
            ApiType = CorpusSourceApiTypes.Official,
            Status = sourceOptions.Enabled ? CorpusSourceStatuses.Available : CorpusSourceStatuses.Disabled,
            AttributionRequired = true,
            Uri = "https://scryfall.com/docs/api",
            Notes =
            [
                "Provides card metadata, legality, prices, release dates, and global EDHREC rank.",
                "Does not provide commander-specific raw deck inclusion data."
            ]
        };
    }

    /// <summary>
    /// Gets normalized corpus signals for a deck context.
    /// </summary>
    public async Task<CorpusSignalReport> GetSignalsAsync(
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget,
        CancellationToken cancellationToken)
    {
        CorpusSourceStatus status = GetStatus();
        if (!status.Enabled)
        {
            return new CorpusSignalReport { Sources = [status] };
        }

        CorpusCacheKey cacheKey = new()
        {
            Source = status.Key,
            Endpoint = "cards/search",
            Query = $"{BuildSearchQuery(query)}|{query.MaxPrice}|{query.Goal}|{query.Theme}|{query.Format}",
            AdapterVersion = "2",
            Budget = budget.AnalysisDepth
        };
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(
            options.Intelligence.Cache.Ttls.ScryfallSearch,
            TimeSpan.FromHours(24));
        if (!query.Refresh)
        {
            CorpusSignalReport? cached = await cache.GetAsync<CorpusSignalReport>(cacheKey, ttl, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                cached.Notes.Add("Scryfall corpus signals returned from source-fact cache.");
                return cached;
            }
        }

        using IDisposable? cacheBypass = query.Refresh && cardCatalog is IScryfallCacheBypass bypass
            ? bypass.BypassCache()
            : null;

        string search = BuildSearchQuery(query);
        int limit = Math.Clamp(budget.MaxCandidates, 5, 100);
        IReadOnlyList<CardSearchResult> results = await cardCatalog
            .SearchCardsAsync(search, limit, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog
            .GetCardsByNamesAsync(results.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);

        CorpusSignalReport report = new();
        report.Sources.Add(GetStatus());
        foreach (CardInfo card in cards.Values.OrderBy(card => card.EdhrecRank ?? int.MaxValue).Take(limit))
        {
            DeckCard candidate = new()
            {
                Name = card.Name,
                Snapshot = new CardSnapshot
                {
                    ManaValue = card.ManaValue,
                    OracleText = card.OracleText,
                    TypeLine = card.TypeLine,
                    EdhrecRank = card.EdhrecRank
                }
            };
            CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
            double rankScore = ScoreRank(card.EdhrecRank);
            double themeScore = ScoreTheme(role, query);
            decimal? price = ReadUsdPrice(card);
            report.Signals.Add(new CardCorpusSignal
            {
                CardName = card.Name,
                OracleId = card.OracleId,
                Source = "Scryfall EDHREC-rank search",
                SignalType = CorpusSignalTypes.Inclusion,
                Score = Math.Clamp((rankScore * 0.70) + (themeScore * 0.30), 0, 1),
                InclusionRate = EstimateInclusionRate(card.EdhrecRank),
                SynergyScore = themeScore,
                Price = price,
                ReleasedAt = card.ReleasedAt,
                EdhrecRank = card.EdhrecRank,
                Uri = card.ScryfallUri,
                Rationale = $"{card.Name} matched Scryfall search for {DescribeQuery(query)} and is classified as {role.PrimaryRole}."
            });

            if (card.ReleasedAt.HasValue && card.ReleasedAt.Value >= DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)))
            {
                report.Signals.Add(new CardCorpusSignal
                {
                    CardName = card.Name,
                    OracleId = card.OracleId,
                    Source = "Scryfall recent release",
                    SignalType = CorpusSignalTypes.Trend,
                    Score = Math.Clamp(0.45 + (themeScore * 0.35) + (rankScore * 0.20), 0, 1),
                    SynergyScore = themeScore,
                    Price = price,
                    ReleasedAt = card.ReleasedAt,
                    EdhrecRank = card.EdhrecRank,
                    Uri = card.ScryfallUri,
                    Rationale = $"{card.Name} was released recently and fits the current deck context."
                });
            }
        }

        report.Notes.Add("Scryfall corpus signals are metadata-derived; use deck-corpus providers for source deck counts and exemplar deck evidence.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Gets configured Scryfall corpus source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return options.Intelligence.Sources.TryGetValue("Scryfall", out MtgMcpCorpusSourceOptions? sourceOptions)
            ? sourceOptions
            : new MtgMcpCorpusSourceOptions();
    }

    /// <summary>
    /// Builds a Scryfall search query for the corpus context.
    /// </summary>
    private static string BuildSearchQuery(CorpusSignalQuery query)
    {
        List<string> parts = [$"legal:{NormalizeFormat(query.Format)}"];
        string? theme = !string.IsNullOrWhiteSpace(query.Goal) ? query.Goal : query.Theme;
        if (!string.IsNullOrWhiteSpace(theme))
        {
            string fragment = ThemeSearchFragment(theme);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                parts.Add(fragment);
            }
        }

        if (query.MaxPrice.HasValue)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"usd<={query.MaxPrice.Value:0.##}"));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Builds a rough Scryfall text fragment from user theme text.
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

        if (normalized.Contains("draw", StringComparison.OrdinalIgnoreCase))
        {
            return "o:draw";
        }

        if (normalized.Contains("ramp", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:add t:artifact or o:\"search your library\" o:land)";
        }

        if (normalized.Contains("interaction", StringComparison.OrdinalIgnoreCase) || normalized.Contains("removal", StringComparison.OrdinalIgnoreCase))
        {
            return "(o:\"destroy target\" or o:\"exile target\" or o:\"counter target\")";
        }

        return "";
    }

    /// <summary>
    /// Scores EDHREC rank as a broad popularity proxy.
    /// </summary>
    private static double ScoreRank(int? edhrecRank)
    {
        return edhrecRank switch
        {
            null => 0.35,
            <= 1_000 => 0.95,
            <= 5_000 => 0.75,
            <= 10_000 => 0.55,
            <= 20_000 => 0.40,
            _ => 0.25
        };
    }

    /// <summary>
    /// Estimates broad inclusion from EDHREC rank.
    /// </summary>
    private static double EstimateInclusionRate(int? edhrecRank)
    {
        return edhrecRank switch
        {
            null => 0.05,
            <= 1_000 => 0.35,
            <= 5_000 => 0.22,
            <= 10_000 => 0.14,
            <= 20_000 => 0.08,
            _ => 0.04
        };
    }

    /// <summary>
    /// Scores how directly a classified card matches the query context.
    /// </summary>
    private static double ScoreTheme(CardRoleAssignment role, CorpusSignalQuery query)
    {
        string text = $"{query.Theme} {query.Goal}";
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0.45;
        }

        if (text.Contains(role.PrimaryRole, StringComparison.OrdinalIgnoreCase)
            || role.Tags.Any(tag => text.Contains(tag, StringComparison.OrdinalIgnoreCase)))
        {
            return 0.90;
        }

        return 0.50;
    }

    /// <summary>
    /// Reads a USD price from card metadata.
    /// </summary>
    private static decimal? ReadUsdPrice(CardInfo card)
    {
        if (card.Prices.TryGetValue("usd", out string? value)
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price))
        {
            return price;
        }

        return null;
    }

    /// <summary>
    /// Normalizes format aliases for Scryfall syntax.
    /// </summary>
    private static string NormalizeFormat(string format)
    {
        return string.IsNullOrWhiteSpace(format)
            ? "commander"
            : format.Trim().Equals("edh", StringComparison.OrdinalIgnoreCase)
                ? "commander"
                : format.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Describes the query context in a compact phrase.
    /// </summary>
    private static string DescribeQuery(CorpusSignalQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Commander))
        {
            return query.Commander;
        }

        return string.IsNullOrWhiteSpace(query.Theme) ? query.Format : query.Theme;
    }
}
