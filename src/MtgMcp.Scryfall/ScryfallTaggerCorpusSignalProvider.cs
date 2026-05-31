using System.Globalization;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Produces card-function signals from explicit Scryfall Tagger oracle-tag searches.
/// </summary>
public sealed class ScryfallTaggerCorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the cache version for the curated Tagger catalog.
    /// </summary>
    private const string CacheAdapterVersion = "scryfall-tagger-v2";

    /// <summary>
    /// Looks up cards through Scryfall search.
    /// </summary>
    private readonly ICardCatalog cardCatalog;

    /// <summary>
    /// Stores source facts for reuse between prompts.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores source and cache configuration.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a Scryfall Tagger corpus signal provider.
    /// </summary>
    public ScryfallTaggerCorpusSignalProvider(
        ICardCatalog cardCatalog,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options)
    {
        this.cardCatalog = cardCatalog;
        this.cache = cache;
        this.options = options.Value;
    }

    /// <summary>
    /// Gets Scryfall Tagger source capability and attribution status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        return new CorpusSourceStatus
        {
            Key = "scryfall-tagger",
            Name = "Scryfall Tagger oracle tags",
            Kind = "card-function-tags",
            Enabled = sourceOptions.Enabled,
            StableApi = true,
            ApiType = CorpusSourceApiTypes.Official,
            Status = sourceOptions.Enabled ? CorpusSourceStatuses.Available : CorpusSourceStatuses.Disabled,
            AttributionRequired = true,
            Uri = "https://tagger.scryfall.com/",
            Notes =
            [
                "Queries Scryfall search with explicit otag: terms backed by Scryfall Tagger oracle tags.",
                "Uses a curated high-signal deckbuilding catalog; broad themes without literal Tagger slugs are mapped only to concrete neighboring oracle tags.",
                "Signals mean a card matched a concrete Tagger query; mtg-mcp does not invent tags for the card."
            ]
        };
    }

    /// <summary>
    /// Gets Scryfall Tagger signals for a deck context.
    /// </summary>
    public async Task<CorpusSignalReport> GetSignalsAsync(
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget,
        CancellationToken cancellationToken)
    {
        CorpusSourceStatus status = GetStatus();
        CorpusSignalReport report = new() { Sources = [status] };
        if (!status.Enabled)
        {
            return report;
        }

        List<ScryfallTaggerRule> selectedRules = SelectRules(query, budget);
        CorpusCacheKey cacheKey = new()
        {
            Source = status.Key,
            Endpoint = "cards/search",
            Query = $"{string.Join(',', selectedRules.Select(rule => rule.Slug))}|{query.Format}|{query.MaxPrice}|{query.Goal}|{query.Theme}",
            AdapterVersion = CacheAdapterVersion,
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
                cached.Notes.Add("Scryfall Tagger signals returned from source-fact cache.");
                return cached;
            }
        }

        using IDisposable? cacheBypass = query.Refresh && cardCatalog is IScryfallCacheBypass bypass
            ? bypass.BypassCache()
            : null;

        int perRuleLimit = Math.Clamp(
            (int)Math.Ceiling(budget.MaxCandidates / (double)Math.Max(1, selectedRules.Count)),
            3,
            25);
        foreach (ScryfallTaggerRule rule in selectedRules)
        {
            string searchQuery = BuildSearchQuery(query, rule);
            IReadOnlyList<CardSearchResult> searchResults = await cardCatalog
                .SearchCardsAsync(searchQuery, perRuleLimit, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyDictionary<string, CardInfo> cards = await cardCatalog
                .GetCardsByNamesAsync(searchResults.Select(card => card.Name).ToList(), cancellationToken)
                .ConfigureAwait(false);

            foreach (string cardName in searchResults.Select(result => result.Name).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cards.TryGetValue(cardName, out CardInfo? card);
                double roleScore = ScoreRuleFit(rule, query);
                report.Signals.Add(new CardCorpusSignal
                {
                    CardName = cardName,
                    OracleId = card?.OracleId,
                    Source = status.Name,
                    SignalType = CorpusSignalTypes.Tag,
                    Score = Math.Clamp(0.55 + (roleScore * 0.35), 0, 1),
                    SynergyScore = roleScore,
                    Price = ReadUsdPrice(card),
                    ReleasedAt = card?.ReleasedAt,
                    EdhrecRank = card?.EdhrecRank,
                    Uri = card?.ScryfallUri ?? BuildScryfallSearchUri(searchQuery),
                    ScryfallUri = card?.ScryfallUri,
                    Rationale = BuildRationale(cardName, rule)
                });
            }
        }

        if (report.Signals.Count == 0)
        {
            report.Notes.Add("No cards matched the selected Scryfall Tagger oracle-tag queries.");
        }

        report.Notes.Add("Scryfall Tagger evidence is deterministic tag-search evidence, not an inferred card classification.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Gets configured Scryfall Tagger source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return options.Intelligence.Sources.TryGetValue("ScryfallTagger", out MtgMcpCorpusSourceOptions? sourceOptions)
            ? sourceOptions
            : new MtgMcpCorpusSourceOptions();
    }

    /// <summary>
    /// Selects fixed Tagger rules from the query text and analysis budget.
    /// </summary>
    private static List<ScryfallTaggerRule> SelectRules(CorpusSignalQuery query, RecommendationAnalysisBudget budget)
    {
        string text = $"{query.Goal} {query.Theme}".Trim();
        List<ScryfallTaggerRule> selected = string.IsNullOrWhiteSpace(text)
            ? []
            : ScryfallTaggerDeckbuildingCatalog.Rules
                .Where(rule => rule.Matches(text))
                .ToList();
        if (selected.Count == 0)
        {
            selected = ScryfallTaggerDeckbuildingCatalog.FallbackRules.ToList();
        }

        int maxRules = budget.AnalysisDepth.Equals(AnalysisDepths.Minimal, StringComparison.OrdinalIgnoreCase)
            ? 3
            : budget.AnalysisDepth.Equals(AnalysisDepths.Best, StringComparison.OrdinalIgnoreCase)
                ? 16
                : 10;
        return selected
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.TaggingCount ?? 0)
            .ThenBy(rule => rule.Slug, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(rule => rule.Slug, StringComparer.OrdinalIgnoreCase)
            .Take(maxRules)
            .ToList();
    }

    /// <summary>
    /// Builds one Scryfall Tagger search query.
    /// </summary>
    private static string BuildSearchQuery(CorpusSignalQuery query, ScryfallTaggerRule rule)
    {
        List<string> parts =
        [
            $"otag:{rule.Slug}",
            $"legal:{NormalizeFormat(query.Format)}",
            "-is:digital"
        ];
        if (query.MaxPrice.HasValue)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"usd<={query.MaxPrice.Value:0.##}"));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Scores how directly a selected Tagger rule matches the query text.
    /// </summary>
    private static double ScoreRuleFit(ScryfallTaggerRule rule, CorpusSignalQuery query)
    {
        string text = $"{query.Goal} {query.Theme}";
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0.60;
        }

        if (text.Contains(rule.Role, StringComparison.OrdinalIgnoreCase)
            || text.Contains(rule.SecondaryTag, StringComparison.OrdinalIgnoreCase)
            || rule.Matches(text))
        {
            return 1;
        }

        return 0.65;
    }

    /// <summary>
    /// Builds a compact rationale that keeps the source tag visible to an LLM agent.
    /// </summary>
    private static string BuildRationale(string cardName, ScryfallTaggerRule rule)
    {
        return $"{cardName} matched Scryfall Tagger oracle tag '{rule.Slug}' for {rule.Description}.";
    }

    /// <summary>
    /// Reads a USD price from card metadata.
    /// </summary>
    private static decimal? ReadUsdPrice(CardInfo? card)
    {
        if (card?.Prices.TryGetValue("usd", out string? value) == true
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price))
        {
            return price;
        }

        return null;
    }

    /// <summary>
    /// Builds a human-usable Scryfall search URI for tag evidence.
    /// </summary>
    private static string BuildScryfallSearchUri(string query)
    {
        return $"https://scryfall.com/search?q={Uri.EscapeDataString(query)}";
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
}
