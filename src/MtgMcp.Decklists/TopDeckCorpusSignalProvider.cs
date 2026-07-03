using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Produces tournament decklist corpus signals from the TopDeck.gg API.
/// </summary>
public sealed class TopDeckCorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the default TopDeck.gg API address.
    /// </summary>
    private static readonly Uri DefaultBaseAddress = new("https://topdeck.gg/api/");

    /// <summary>
    /// Sends requests to TopDeck.gg.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Stores source facts for reuse between prompts.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores source and cache configuration.
    /// </summary>
    private readonly MtgMcpOptions options;

    /// <summary>
    /// Creates a TopDeck.gg corpus provider.
    /// </summary>
    public TopDeckCorpusSignalProvider(
        HttpClient httpClient,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options)
    {
        this.httpClient = httpClient;
        this.cache = cache;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= SourceOptions().BaseAddress ?? DefaultBaseAddress;
        MtgMcpHttpDefaults.ApplyUserAgent(this.httpClient, SourceOptions().UserAgent);
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Gets TopDeck source capability and configuration status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        bool hasKey = !string.IsNullOrWhiteSpace(sourceOptions.ApiKey);
        return new CorpusSourceStatus
        {
            Key = "topdeck",
            Name = "TopDeck.gg",
            Kind = "tournament-api",
            Enabled = sourceOptions.Enabled && hasKey,
            StableApi = true,
            ApiType = CorpusSourceApiTypes.Official,
            RequiresKey = true,
            AttributionRequired = true,
            Status = sourceOptions.Enabled
                ? hasKey ? CorpusSourceStatusKind.Available : CorpusSourceStatusKind.MissingConfig
                : CorpusSourceStatusKind.Disabled,
            Uri = "https://topdeck.gg/docs/tournaments-v2",
            Notes = ["Uses the documented TopDeck.gg tournaments v2 API for structured tournament decklist evidence."]
        };
    }

    /// <summary>
    /// Gets tournament decklist signals from TopDeck.
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

        string requestFingerprint = $"{query.Format}|{CommanderFingerprint(query)}|{query.Theme}|{budget.AnalysisDepth}|{budget.MaxDecksPerSource}";
        CorpusCacheKey cacheKey = new()
        {
            Source = status.Key,
            Endpoint = "v2/tournaments",
            Query = requestFingerprint,
            AdapterVersion = "1",
            Budget = budget.AnalysisDepth
        };
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(options.Intelligence.Cache.Ttls.DeckSearch, TimeSpan.FromHours(6));
        if (!query.Refresh)
        {
            CorpusSignalReport? cached = await cache.GetAsync<CorpusSignalReport>(cacheKey, ttl, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                cached.Notes.Add("TopDeck.gg signals returned from source-fact cache.");
                return cached;
            }
        }

        MtgMcpHttpTextResponse response = await MtgMcpHttpRetry
            .SendForStringAsync(
                httpClient,
                () =>
                {
                    HttpRequestMessage request = new(HttpMethod.Post, "v2/tournaments");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SourceOptions().ApiKey);
                    request.Content = JsonContent.Create(new
                    {
                        game = "Magic: The Gathering",
                        format = NormalizeFormat(query.Format),
                        last = 90
                    });
                    return request;
                },
                "TopDeck.gg",
                2,
                TimeSpan.FromSeconds(1),
                cancellationToken)
            .ConfigureAwait(false);
        string payload = response.Body;
        if (DecklistCorpusProviderSupport.LooksLikeHtml(payload))
        {
            throw new InvalidOperationException("TopDeck.gg returned HTML; corpus providers only accept structured API payloads.");
        }

        using JsonDocument document = JsonDocument.Parse(payload);
        List<DecklistExemplar> exemplars = ReadExemplars(document.RootElement, query, budget);
        report.ExemplarDecks.AddRange(exemplars.Select(exemplar => new DeckExemplarSignal
        {
            Name = exemplar.Name,
            Source = status.Name,
            Uri = exemplar.Uri,
            Commander = exemplar.Commander,
            PopularityMetric = "tournament weight",
            PopularityValue = exemplar.Weight,
            DeckSize = exemplar.Cards.Count,
            Weight = exemplar.Weight,
            Notes = "Sampled from TopDeck.gg tournament standings."
        }));
        DecklistCorpusProviderSupport.AddSignalsFromExemplars(report, status.Name, exemplars, budget.MaxCandidates);
        report.Notes.Add("TopDeck.gg evidence is tournament/decklist data, not broad casual Commander inclusion data.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Reads exemplar decks from a TopDeck tournament payload.
    /// </summary>
    private List<DecklistExemplar> ReadExemplars(
        JsonElement root,
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget)
    {
        List<DecklistExemplar> exemplars = [];
        foreach (JsonElement tournament in EnumerateTournaments(root))
        {
            string tournamentName = ReadString(tournament, "name") ?? "TopDeck.gg tournament";
            if (!tournament.TryGetProperty("standings", out JsonElement standings) || standings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement standing in standings.EnumerateArray())
            {
                List<string> cards = [];
                if (standing.TryGetProperty("deckObj", out JsonElement deckObj))
                {
                    cards.AddRange(DecklistCorpusProviderSupport.ExtractCards(deckObj));
                }

                if (standing.TryGetProperty("decklist", out JsonElement decklist))
                {
                    cards.AddRange(DecklistCorpusProviderSupport.ExtractCards(decklist));
                }

                cards = cards.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (cards.Count == 0 || !MatchesQuery(cards, query))
                {
                    continue;
                }

                exemplars.Add(new DecklistExemplar
                {
                    Name = $"{tournamentName} - {ReadString(standing, "name") ?? "player"}",
                    Uri = ReadString(standing, "decklist"),
                    Commander = query.Commander,
                    Weight = ReadDouble(standing, "winRate") ?? ScoreByStanding(ReadInt32(standing, "standing")),
                    Cards = cards
                });
                if (exemplars.Count >= budget.MaxDecksPerSource)
                {
                    return exemplars;
                }
            }
        }

        return exemplars;
    }

    /// <summary>
    /// Enumerates tournament objects from supported response envelopes.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateTournaments(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in data.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Gets whether a sampled deck matches the current commander query.
    /// </summary>
    private static bool MatchesQuery(IReadOnlyCollection<string> cards, CorpusSignalQuery query)
    {
        List<string> commanderNames = QueryCommanderNames(query);
        return commanderNames.Count == 0
            || commanderNames.Any(name => cards.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a cache fingerprint for the commander query.
    /// </summary>
    private static string CommanderFingerprint(CorpusSignalQuery query)
    {
        List<string> commanderNames = QueryCommanderNames(query);
        return commanderNames.Count == 0
            ? query.Commander ?? ""
            : string.Join(" // ", commanderNames);
    }

    /// <summary>
    /// Gets exact-match commander names, falling back to the display name for singleton decks.
    /// </summary>
    private static List<string> QueryCommanderNames(CorpusSignalQuery query)
    {
        List<string> commanderNames = query.CommanderNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (commanderNames.Count == 0 && !string.IsNullOrWhiteSpace(query.Commander))
        {
            commanderNames.Add(query.Commander.Trim());
        }

        return commanderNames;
    }

    /// <summary>
    /// Gets configured TopDeck source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return DecklistCorpusProviderSupport.SourceOptions(options, "TopDeck", defaultEnabled: true);
    }

    /// <summary>
    /// Converts mtg-mcp format labels into TopDeck format labels.
    /// </summary>
    private static string NormalizeFormat(string format)
    {
        return string.IsNullOrWhiteSpace(format) ? "EDH" : format.Trim().ToLowerInvariant() switch
        {
            "commander" or "edh" => "EDH",
            "cedh" => "cEDH",
            _ => format.Trim()
        };
    }

    /// <summary>
    /// Scores a tournament row from its final standing.
    /// </summary>
    private static double ScoreByStanding(int? standing)
    {
        return standing switch
        {
            null => 0.50,
            <= 1 => 1.00,
            <= 4 => 0.85,
            <= 8 => 0.75,
            <= 16 => 0.60,
            _ => 0.45
        };
    }

    /// <summary>
    /// Reads a string property when present.
    /// </summary>
    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Reads an integer property when present.
    /// </summary>
    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    /// <summary>
    /// Reads a double property when present.
    /// </summary>
    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetDouble(out double result)
            ? result
            : null;
    }
}
