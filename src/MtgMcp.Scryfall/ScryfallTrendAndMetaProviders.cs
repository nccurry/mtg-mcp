using System.Collections.Concurrent;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Provides Scryfall-backed recent-card recommendations.
/// </summary>
public sealed class ScryfallCardTrendProvider : ICardTrendProvider
{
    /// <summary>
    /// Caches recent-card lookups by Scryfall query.
    /// </summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyList<NewCardSuggestion>> TrendCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up card data through Scryfall.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Creates a Scryfall trend provider.
    /// </summary>
    public ScryfallCardTrendProvider(ICardCatalog cardCatalog)
    {
        this.cardCatalog = cardCatalog;
    }

    /// <summary>
    /// Finds recent cards with Scryfall search syntax.
    /// </summary>
    public async Task<IReadOnlyList<NewCardSuggestion>> FindNewCardsAsync(
        CardTrendQuery query,
        CancellationToken cancellationToken)
    {
        string search = BuildTrendSearchQuery(query);
        string cacheKey = $"{search}|{Math.Clamp(query.Limit, 1, 50)}";
        if (TrendCache.TryGetValue(cacheKey, out IReadOnlyList<NewCardSuggestion>? cached))
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
            .Take(Math.Clamp(query.Limit, 1, 50))
            .ToList();
        TrendCache[cacheKey] = suggestions.Select(CloneSuggestion).ToList();
        return suggestions;
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
/// Provides Scryfall EDHREC-rank based Commander popularity context.
/// </summary>
public sealed class ScryfallCommanderMetaProvider : ICommanderMetaProvider
{
    /// <summary>
    /// Caches Commander meta lookups by search query.
    /// </summary>
    private static readonly ConcurrentDictionary<string, CommanderMetaReport> MetaCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up card data through Scryfall.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Creates a Scryfall Commander meta provider.
    /// </summary>
    public ScryfallCommanderMetaProvider(ICardCatalog cardCatalog)
    {
        this.cardCatalog = cardCatalog;
    }

    /// <summary>
    /// Gets Commander card popularity context from Scryfall EDHREC ranks.
    /// </summary>
    public async Task<CommanderMetaReport> GetCommanderMetaAsync(
        CommanderMetaQuery query,
        CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(query.Limit, 1, 100);
        string search = BuildMetaSearchQuery(query);
        string cacheKey = $"{search}|{query.Commander}|{query.Theme}|{limit}";
        if (MetaCache.TryGetValue(cacheKey, out CommanderMetaReport? cached))
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
            Source = "Scryfall EDHREC-rank search"
        };

        int rank = 0;
        foreach (CardInfo card in cards.Values.OrderBy(card => card.EdhrecRank ?? int.MaxValue).Take(limit))
        {
            rank++;
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
                InclusionRate = EstimateInclusionRate(card.EdhrecRank, rank),
                SynergyScore = EstimateSynergyScore(role, query.Theme),
                Source = "scryfall-edhrec-rank",
                Uri = card.ScryfallUri
            });
        }

        report.Notes.Add("Scryfall does not expose commander-specific deck inclusion data; this provider uses EDHREC rank ordered Scryfall search as popularity context.");
        MetaCache[cacheKey] = CloneReport(report);
        return report;
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
            Source = card.Source,
            Uri = card.Uri
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
    /// Estimates an inclusion rate from EDHREC rank.
    /// </summary>
    private static double EstimateInclusionRate(int? edhrecRank, int ordinal)
    {
        if (!edhrecRank.HasValue)
        {
            return Math.Clamp(0.25 - (ordinal * 0.01), 0.05, 0.25);
        }

        return edhrecRank.Value switch
        {
            <= 100 => 0.65,
            <= 500 => 0.50,
            <= 1_000 => 0.40,
            <= 5_000 => 0.25,
            <= 10_000 => 0.15,
            _ => 0.08
        };
    }

    /// <summary>
    /// Estimates theme synergy from classified role tags.
    /// </summary>
    private static double EstimateSynergyScore(CardRoleAssignment role, string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return 0.2;
        }

        return role.Tags.Any(tag => theme.Contains(tag, StringComparison.OrdinalIgnoreCase))
            || theme.Contains(role.PrimaryRole, StringComparison.OrdinalIgnoreCase)
            ? 0.75
            : 0.25;
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
