using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Produces decklist corpus signals from the Spicerack public decklist API.
/// </summary>
public sealed class SpicerackCorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the default Spicerack API address.
    /// </summary>
    private static readonly Uri DefaultBaseAddress = new("https://api.spicerack.gg/");

    /// <summary>
    /// Sends requests to Spicerack.
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
    /// Creates a Spicerack corpus provider.
    /// </summary>
    public SpicerackCorpusSignalProvider(
        HttpClient httpClient,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> options)
    {
        this.httpClient = httpClient;
        this.cache = cache;
        this.options = options.Value;
        this.httpClient.BaseAddress ??= SourceOptions().BaseAddress ?? DefaultBaseAddress;
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Gets Spicerack source capability and configuration status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        bool hasKey = !string.IsNullOrWhiteSpace(sourceOptions.ApiKey);
        return new CorpusSourceStatus
        {
            Key = "spicerack",
            Name = "Spicerack public decklists",
            Kind = "decklist-api",
            Enabled = sourceOptions.Enabled && hasKey,
            StableApi = true,
            ApiType = CorpusSourceApiTypes.Official,
            RequiresKey = true,
            AttributionRequired = true,
            Status = sourceOptions.Enabled
                ? hasKey ? CorpusSourceStatuses.Available : CorpusSourceStatuses.MissingConfig
                : CorpusSourceStatuses.Disabled,
            Uri = "https://docs.spicerack.gg/api-reference/public-decklist-database",
            Notes = ["Uses Spicerack's documented public decklist database API."]
        };
    }

    /// <summary>
    /// Gets recent public decklist signals from Spicerack.
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

        string path = $"api/export-decklists/?num_days=30&event_format={Uri.EscapeDataString(NormalizeFormat(query.Format))}&decklist_as_text=true";
        CorpusCacheKey cacheKey = new()
        {
            Source = status.Key,
            Endpoint = "api/export-decklists",
            Query = $"{path}|{CommanderFingerprint(query)}|{query.Theme}|{budget.AnalysisDepth}|{budget.MaxDecksPerSource}",
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
                cached.Notes.Add("Spicerack signals returned from source-fact cache.");
                return cached;
            }
        }

        using HttpRequestMessage request = new(HttpMethod.Get, path);
        string apiKey = SourceOptions().ApiKey ?? "";
        request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (DecklistCorpusProviderSupport.LooksLikeHtml(payload))
        {
            throw new InvalidOperationException("Spicerack returned HTML; corpus providers only accept structured API payloads.");
        }

        using JsonDocument document = JsonDocument.Parse(payload);
        List<DecklistExemplar> exemplars = ReadExemplars(document.RootElement, query, budget);
        report.ExemplarDecks.AddRange(exemplars.Select(exemplar => new DeckExemplarSignal
        {
            Name = exemplar.Name,
            Source = status.Name,
            Uri = exemplar.Uri,
            Commander = exemplar.Commander,
            PopularityMetric = "recent decklist weight",
            PopularityValue = exemplar.Weight,
            DeckSize = exemplar.Cards.Count,
            Weight = exemplar.Weight,
            Notes = "Sampled from Spicerack recent public decklists."
        }));
        DecklistCorpusProviderSupport.AddSignalsFromExemplars(report, status.Name, exemplars, budget.MaxCandidates);
        report.Notes.Add("Spicerack evidence comes from recent public event decklists.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Reads exemplar decks from a Spicerack response payload.
    /// </summary>
    private List<DecklistExemplar> ReadExemplars(
        JsonElement root,
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget)
    {
        List<DecklistExemplar> exemplars = [];
        foreach (JsonElement deck in EnumerateDeckObjects(root))
        {
            List<string> cards = [];
            foreach (string property in new[] { "decklist_text", "decklistText", "decklist", "mainboard", "cards" })
            {
                if (deck.TryGetProperty(property, out JsonElement decklist))
                {
                    cards.AddRange(DecklistCorpusProviderSupport.ExtractCards(decklist));
                }
            }

            cards = cards.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cards.Count == 0 || !MatchesQuery(cards, query))
            {
                continue;
            }

            exemplars.Add(new DecklistExemplar
            {
                Name = ReadString(deck, "deck_name")
                    ?? ReadString(deck, "deckName")
                    ?? ReadString(deck, "player")
                    ?? "Spicerack decklist",
                Uri = ReadString(deck, "decklist_url") ?? ReadString(deck, "url"),
                Commander = query.Commander,
                Weight = 0.65,
                Cards = cards
            });
            if (exemplars.Count >= budget.MaxDecksPerSource)
            {
                break;
            }
        }

        return exemplars;
    }

    /// <summary>
    /// Enumerates decklist objects from supported response envelopes.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateDeckObjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (string propertyName in new[] { "data", "decklists", "results" })
        {
            if (root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    yield return item;
                }
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
    /// Gets configured Spicerack source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return DecklistCorpusProviderSupport.SourceOptions(options, "Spicerack", defaultEnabled: true);
    }

    /// <summary>
    /// Converts mtg-mcp format labels into Spicerack format labels.
    /// </summary>
    private static string NormalizeFormat(string format)
    {
        return string.IsNullOrWhiteSpace(format) ? "commander" : format.Trim().ToLowerInvariant();
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
}
