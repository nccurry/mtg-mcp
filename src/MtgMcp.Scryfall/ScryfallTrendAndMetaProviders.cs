using System.Globalization;
using Microsoft.Extensions.Options;
using MtgMcp.Core;
using static MtgMcp.Scryfall.ScryfallCacheKeyParts;

namespace MtgMcp.Scryfall;

/// <summary>
/// Provides Scryfall-backed recent-card recommendations.
/// </summary>
public sealed class ScryfallCardTrendProvider : ICardTrendProvider
{
    /// <summary>
    /// Stores the maximum entries for the compatibility in-memory cache.
    /// </summary>
    private const int FallbackCacheEntries = 128;

    /// <summary>
    /// Looks up card data through Scryfall.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Stores shared source facts.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores mtg-mcp options.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a Scryfall trend provider with an in-process compatibility cache.
    /// </summary>
    public ScryfallCardTrendProvider(ICardCatalog cardCatalog)
        : this(
            cardCatalog,
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions { MaxEntries = FallbackCacheEntries }),
            Options.Create(new MtgMcpOptions()))
    {
    }

    /// <summary>
    /// Creates a Scryfall trend provider with the configured source-fact cache.
    /// </summary>
    public ScryfallCardTrendProvider(
        ICardCatalog cardCatalog,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options)
    {
        this.cardCatalog = cardCatalog;
        this.cache = cache;
        this.options = options.Value;
    }

    /// <summary>
    /// Finds recent cards with Scryfall search syntax.
    /// </summary>
    public async Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsAsync(
        CardTrendQuery query,
        CancellationToken cancellationToken)
    {
        string search = BuildTrendSearchQuery(query);
        int limit = Math.Clamp(query.Limit, 1, 50);
        CorpusCacheKey cacheKey = CreateTrendKey(search, query, limit);
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(
            options.Intelligence.Cache.Ttls.ScryfallSearch,
            TimeSpan.FromHours(6));
        List<NewCardSuggestion>? cached = await cache.GetAsync<List<NewCardSuggestion>>(cacheKey, ttl, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return cached.Select(CloneSuggestion).ToList();
        }

        IReadOnlyList<CardSearchResult> results = await cardCatalog
            .SearchCardsAsync(search, Math.Clamp(query.Limit * 3, 10, 75), cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, CardSearchResult> searchByName = results
            .GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog
            .GetCardsByNamesAsync(results.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);

        List<NewCardSuggestion> suggestions = cards.Values
            .Select(card => BuildSuggestion(card, searchByName.GetValueOrDefault(card.Name), query.Theme))
            .Where(suggestion => MatchesReleaseFilters(suggestion, query))
            .Where(suggestion => !query.MaxPrice.HasValue || (suggestion.Price.HasValue && suggestion.Price.Value <= query.MaxPrice.Value))
            .OrderByDescending(suggestion => suggestion.Score)
            .Take(limit)
            .ToList();
        await cache.SetAsync(cacheKey, suggestions.Select(CloneSuggestion).ToList(), cancellationToken)
            .ConfigureAwait(false);
        return suggestions;
    }

    /// <summary>
    /// Creates a shared source-fact cache key for recent-card searches.
    /// </summary>
    private static CorpusCacheKey CreateTrendKey(string search, CardTrendQuery query, int limit)
    {
        return new CorpusCacheKey
        {
            Source = "scryfall-card-trends",
            Endpoint = "cards/search",
            Query = string.Join(
                '|',
                search,
                NormalizeFormat(query.Format),
                CachePart(query.Theme),
                query.Since?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                CachePart(query.SetCode),
                CachePart(query.MaxPrice)),
            AdapterVersion = "2",
            Budget = $"limit:{limit}"
        };
    }

    /// <summary>
    /// Clones a recent-card suggestion for cache isolation.
    /// </summary>
    private static NewCardSuggestion CloneSuggestion(NewCardSuggestion suggestion)
    {
        return new NewCardSuggestion
        {
            CardName = suggestion.CardName,
            Role = suggestion.Role,
            Tags = suggestion.Tags.ToList(),
            ReleasedAt = suggestion.ReleasedAt,
            Set = suggestion.Set,
            Price = suggestion.Price,
            ScryfallUri = suggestion.ScryfallUri,
            Score = suggestion.Score,
            Rationale = suggestion.Rationale
        };
    }

    /// <summary>
    /// Builds one recent-card suggestion from card data.
    /// </summary>
    private static NewCardSuggestion BuildSuggestion(CardInfo card, CardSearchResult? searchResult, string? theme)
    {
        DeckCard candidate = new()
        {
            Name = card.Name,
            Snapshot = new CardSnapshot
            {
                TypeLine = card.TypeLine,
                OracleText = card.OracleText,
                ManaValue = card.ManaValue,
                EdhrecRank = card.EdhrecRank,
                Prices = new Dictionary<string, string>(card.Prices, StringComparer.OrdinalIgnoreCase)
            }
        };
        CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
        double rankScore = card.EdhrecRank switch
        {
            null => 0.4,
            <= 1_000 => 0.9,
            <= 5_000 => 0.7,
            <= 10_000 => 0.5,
            _ => 0.3
        };
        double themeScore = string.IsNullOrWhiteSpace(theme)
            ? 0.45
            : role.Tags.Any(tag => theme.Contains(tag, StringComparison.OrdinalIgnoreCase)) ? 0.9 : 0.45;
        return new NewCardSuggestion
        {
            CardName = card.Name,
            Role = role.PrimaryRole,
            Tags = role.Tags,
            ReleasedAt = searchResult?.ReleasedAt ?? card.ReleasedAt,
            Set = searchResult?.Set ?? card.Set,
            Price = ReadUsdPrice(card),
            ScryfallUri = card.ScryfallUri ?? searchResult?.ScryfallUri,
            Score = Math.Clamp((themeScore * 0.45) + (rankScore * 0.35) + 0.20, 0, 1),
            Rationale = $"{card.Name} matched the recent-release Scryfall search and was classified as {role.PrimaryRole}."
        };
    }

    /// <summary>
    /// Checks release date and set filters.
    /// </summary>
    private static bool MatchesReleaseFilters(NewCardSuggestion suggestion, CardTrendQuery query)
    {
        if (query.Since.HasValue && (!suggestion.ReleasedAt.HasValue || suggestion.ReleasedAt.Value < query.Since.Value))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(query.SetCode)
            || (!string.IsNullOrWhiteSpace(suggestion.Set) && suggestion.Set.Equals(query.SetCode, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds Scryfall search syntax for recent cards.
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
    /// Builds a Scryfall search fragment from a theme name.
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
    /// Normalizes an empty or alias format.
    /// </summary>
    private static string NormalizeFormat(string format)
    {
        return string.IsNullOrWhiteSpace(format) ? "commander" : format.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Reads a USD card price.
    /// </summary>
    private static decimal? ReadUsdPrice(CardInfo card)
    {
        return card.Prices.TryGetValue("usd", out string? value) && decimal.TryParse(value, out decimal price)
            ? price
            : null;
    }
}

/// <summary>
/// Provides global Scryfall EDHREC-rank based Commander popularity context.
/// </summary>
public sealed class ScryfallCommanderMetaProvider : ICommanderMetaProvider
{
    /// <summary>
    /// Stores the maximum entries for the compatibility in-memory cache.
    /// </summary>
    private const int FallbackCacheEntries = 128;

    /// <summary>
    /// Looks up card data through Scryfall.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Stores shared source facts.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores mtg-mcp options.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a Scryfall Commander meta provider with an in-process compatibility cache.
    /// </summary>
    public ScryfallCommanderMetaProvider(ICardCatalog cardCatalog)
        : this(
            cardCatalog,
            new MemoryCorpusCache(new MtgMcpCorpusCacheOptions { MaxEntries = FallbackCacheEntries }),
            Options.Create(new MtgMcpOptions()))
    {
    }

    /// <summary>
    /// Creates a Scryfall Commander meta provider with the configured source-fact cache.
    /// </summary>
    public ScryfallCommanderMetaProvider(
        ICardCatalog cardCatalog,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options)
    {
        this.cardCatalog = cardCatalog;
        this.cache = cache;
        this.options = options.Value;
    }

    /// <summary>
    /// Gets global Commander card popularity context from Scryfall EDHREC ranks.
    /// </summary>
    public async Task<CommanderMetaReport> GetCommanderMetaAsync(
        CommanderMetaQuery query,
        CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(query.Limit, 1, 100);
        string search = BuildMetaSearchQuery(query);
        CorpusCacheKey cacheKey = CreateCommanderMetaKey(search, query, limit);
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(
            options.Intelligence.Cache.Ttls.ScryfallSearch,
            TimeSpan.FromHours(6));
        CommanderMetaReport? cached = await cache.GetAsync<CommanderMetaReport>(cacheKey, ttl, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return CloneReport(cached);
        }

        IReadOnlyList<CardSearchResult> results = await cardCatalog
            .SearchCardsAsync(search, limit * 2, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog
            .GetCardsByNamesAsync(results.Select(card => card.Name).ToList(), cancellationToken)
            .ConfigureAwait(false);
        CommanderMetaReport report = new()
        {
            Commander = query.Commander,
            Theme = query.Theme,
            Source = "Scryfall global EDHREC-rank facts"
        };

        foreach (CardInfo card in cards.Values.OrderBy(card => card.EdhrecRank ?? int.MaxValue).Take(limit))
        {
            DeckCard candidate = new()
            {
                Name = card.Name,
                Snapshot = new CardSnapshot
                {
                    TypeLine = card.TypeLine,
                    OracleText = card.OracleText,
                    ManaValue = card.ManaValue,
                    EdhrecRank = card.EdhrecRank
                }
            };
            CardRoleAssignment role = DeckRoleClassifier.Classify(candidate);
            report.PopularCards.Add(new CommanderMetaCard
            {
                Name = card.Name,
                Category = role.PrimaryRole,
                InclusionRate = 0,
                SynergyScore = 0,
                EdhrecRank = card.EdhrecRank,
                Source = "scryfall-edhrec-rank",
                Uri = card.ScryfallUri,
                ScryfallUri = card.ScryfallUri
            });
        }

        report.Notes.Add("Scryfall does not expose commander-specific deck inclusion data; inclusionRate and synergyScore are not inferred for this source.");
        await cache.SetAsync(cacheKey, CloneReport(report), cancellationToken)
            .ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Creates a shared source-fact cache key for Commander meta searches.
    /// </summary>
    private static CorpusCacheKey CreateCommanderMetaKey(string search, CommanderMetaQuery query, int limit)
    {
        return new CorpusCacheKey
        {
            Source = "scryfall-commander-meta",
            Endpoint = "cards/search",
            Query = string.Join(
                '|',
                search,
                NormalizeFormat(query.Format),
                CachePart(query.Commander),
                CachePart(query.Theme)),
            AdapterVersion = "2",
            Budget = $"limit:{limit}"
        };
    }

    /// <summary>
    /// Clones a Commander meta report for cache isolation.
    /// </summary>
    private static CommanderMetaReport CloneReport(CommanderMetaReport report)
    {
        return new CommanderMetaReport
        {
            WorkspaceId = report.WorkspaceId,
            Commander = report.Commander,
            Theme = report.Theme,
            Source = report.Source,
            PopularCards = report.PopularCards.Select(CloneMetaCard).ToList(),
            IncludedPopularCards = report.IncludedPopularCards.Select(CloneMetaCard).ToList(),
            MissingPopularCards = report.MissingPopularCards.Select(CloneMetaCard).ToList(),
            Notes = report.Notes.ToList()
        };
    }

    /// <summary>
    /// Clones one Commander meta card for cache isolation.
    /// </summary>
    private static CommanderMetaCard CloneMetaCard(CommanderMetaCard card)
    {
        return new CommanderMetaCard
        {
            Name = card.Name,
            Category = card.Category,
            InclusionRate = card.InclusionRate,
            SynergyScore = card.SynergyScore,
            EdhrecRank = card.EdhrecRank,
            Source = card.Source,
            Uri = card.Uri,
            ScryfallUri = card.ScryfallUri
        };
    }

    /// <summary>
    /// Builds Scryfall search syntax for Commander popularity context.
    /// </summary>
    private static string BuildMetaSearchQuery(CommanderMetaQuery query)
    {
        List<string> parts = [$"legal:{NormalizeFormat(query.Format)}", "-t:basic"];
        if (!string.IsNullOrWhiteSpace(query.Theme))
        {
            parts.Add(ThemeSearchFragment(query.Theme));
        }

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>
    /// Builds a Scryfall search fragment from a Commander theme.
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
    /// Normalizes an empty or alias format.
    /// </summary>
    private static string NormalizeFormat(string format)
    {
        return string.IsNullOrWhiteSpace(format) ? "commander" : format.Trim().ToLowerInvariant();
    }
}

/// <summary>
/// Provides cache fingerprint helpers for Scryfall optional-context providers.
/// </summary>
internal static class ScryfallCacheKeyParts
{
    /// <summary>
    /// Normalizes an optional text value for a cache key.
    /// </summary>
    public static string CachePart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    /// <summary>
    /// Normalizes an optional decimal value for a cache key.
    /// </summary>
    public static string CachePart(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.#############################", CultureInfo.InvariantCulture)
            : "";
    }
}
