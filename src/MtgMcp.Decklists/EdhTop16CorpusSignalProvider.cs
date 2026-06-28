using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Produces cEDH performance signals from EDHTop16 commander staple and entry data.
/// </summary>
public sealed class EdhTop16CorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the default EDHTop16 site address.
    /// </summary>
    private static readonly Uri DefaultBaseAddress = new("https://edhtop16.com/");

    /// <summary>
    /// Identifies the EDHTop16 persisted query for commander staples.
    /// </summary>
    private const string StaplesQueryId = "af1acbd10d64b4727beee2c106694d9b";

    /// <summary>
    /// Identifies the EDHTop16 persisted query for commander tournament entries.
    /// </summary>
    private const string EntriesQueryId = "9f5caa1497725515a7d55c20d8cc4247";

    /// <summary>
    /// Sends requests to EDHTop16.
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
    /// Creates an EDHTop16 corpus provider.
    /// </summary>
    public EdhTop16CorpusSignalProvider(
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
    /// Gets EDHTop16 source capability and configuration status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        bool enabled = sourceOptions.Enabled
            && DecklistCorpusProviderSupport.AllowsUnofficialApi(sourceOptions, defaultAllowed: false);
        return new CorpusSourceStatus
        {
            Key = "edhtop16",
            Name = "EDHTop16",
            Kind = "cedh-performance",
            Enabled = enabled,
            StableApi = false,
            ApiType = CorpusSourceApiTypes.UnofficialApi,
            UnofficialApi = true,
            PermissionSensitive = true,
            Status = sourceOptions.Enabled
                ? enabled ? CorpusSourceStatusKind.Available : CorpusSourceStatusKind.Disabled
                : CorpusSourceStatusKind.Disabled,
            Uri = "https://edhtop16.com/about",
            Notes =
            [
                "Uses EDHTop16's structured persisted GraphQL endpoint for cEDH staple and tournament-entry evidence.",
                "Set AllowUnofficialApi=true for the EdhTop16 source before querying this endpoint."
            ]
        };
    }

    /// <summary>
    /// Gets cEDH performance signals from EDHTop16.
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

        if (string.IsNullOrWhiteSpace(query.Commander))
        {
            report.Notes.Add("EDHTop16 evidence requires a commander name.");
            return report;
        }

        CorpusCacheKey cacheKey = new()
        {
            Source = status.Key,
            Endpoint = "api/graphql",
            Query = $"{query.Commander}|{budget.AnalysisDepth}|{budget.MaxCandidates}|{budget.MaxDecksPerSource}",
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
                cached.Notes.Add("EDHTop16 signals returned from source-fact cache.");
                return cached;
            }
        }

        using JsonDocument staples = await SendPersistedQueryAsync(
            StaplesQueryId,
            new
            {
                commander = query.Commander,
                staplesSortBy = "MOST_PLAYED"
            },
            cancellationToken).ConfigureAwait(false);
        AddStapleSignals(report, staples.RootElement, status, query, budget);

        if (budget.IncludeExemplarDecks)
        {
            using JsonDocument entries = await SendPersistedQueryAsync(
                EntriesQueryId,
                new
                {
                    commander = query.Commander,
                    maxStanding = (int?)null,
                    minEventSize = 50,
                    sortBy = "TOP",
                    timePeriod = "ONE_YEAR"
                },
                cancellationToken).ConfigureAwait(false);
            AddEntryExemplars(report, entries.RootElement, status, query, budget);
        }

        report.Notes.Add("EDHTop16 evidence is cEDH tournament and staple data; it is not a casual Commander popularity source.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Sends one persisted GraphQL query to EDHTop16.
    /// </summary>
    private async Task<JsonDocument> SendPersistedQueryAsync(
        string queryId,
        object variables,
        CancellationToken cancellationToken)
    {
        MtgMcpHttpTextResponse response = await MtgMcpHttpRetry
            .SendForStringAsync(
                httpClient,
                () =>
                {
                    HttpRequestMessage request = new(HttpMethod.Post, "api/graphql");
                    request.Content = JsonContent.Create(new
                    {
                        query = (string?)null,
                        variables,
                        extensions = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["pastoria-id"] = queryId
                        }
                    });
                    return request;
                },
                "EDHTop16",
                2,
                TimeSpan.FromSeconds(1),
                cancellationToken)
            .ConfigureAwait(false);
        string payload = response.Body;
        if (DecklistCorpusProviderSupport.LooksLikeHtml(payload))
        {
            throw new InvalidOperationException("EDHTop16 returned HTML; corpus providers only accept structured API payloads.");
        }

        JsonDocument document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("errors", out JsonElement errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0)
        {
            document.Dispose();
            throw new InvalidOperationException("EDHTop16 returned GraphQL errors.");
        }

        return document;
    }

    /// <summary>
    /// Adds EDHTop16 commander staple rows as card performance signals.
    /// </summary>
    private static void AddStapleSignals(
        CorpusSignalReport report,
        JsonElement root,
        CorpusSourceStatus status,
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget)
    {
        if (!TryGetCommander(root, out JsonElement commander)
            || !commander.TryGetProperty("staples", out JsonElement staples)
            || staples.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement staple in staples.EnumerateArray().Take(budget.MaxCandidates))
        {
            string? name = ReadString(staple, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            double playRate = ReadDouble(staple, "playRateLastYear") ?? 0;
            report.Signals.Add(new CardCorpusSignal
            {
                CardName = name,
                Source = status.Name,
                SignalType = CorpusSignalTypes.Performance,
                Score = Math.Clamp(0.45 + (playRate * 0.55), 0, 1),
                InclusionRate = playRate,
                PerformanceScore = playRate,
                Uri = ReadString(staple, "scryfallUrl"),
                Rationale = $"{name} has {playRate.ToString("P1", CultureInfo.InvariantCulture)} EDHTop16 cEDH staple play rate for {query.Commander}."
            });
        }
    }

    /// <summary>
    /// Adds EDHTop16 top tournament entries as exemplar deck rows.
    /// </summary>
    private static void AddEntryExemplars(
        CorpusSignalReport report,
        JsonElement root,
        CorpusSourceStatus status,
        CorpusSignalQuery query,
        RecommendationAnalysisBudget budget)
    {
        if (!TryGetCommander(root, out JsonElement commander)
            || !commander.TryGetProperty("entries", out JsonElement entries)
            || !entries.TryGetProperty("edges", out JsonElement edges)
            || edges.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement edge in edges.EnumerateArray().Take(budget.MaxDecksPerSource))
        {
            if (!edge.TryGetProperty("node", out JsonElement node))
            {
                continue;
            }

            JsonElement tournament = node.TryGetProperty("tournament", out JsonElement tournamentElement)
                ? tournamentElement
                : default;
            string tournamentName = tournament.ValueKind == JsonValueKind.Object
                ? ReadString(tournament, "name") ?? "EDHTop16 event"
                : "EDHTop16 event";
            string player = ReadString(node, "player") ?? "player";
            int? standing = ReadInt32(node, "standing");
            int? size = tournament.ValueKind == JsonValueKind.Object ? ReadInt32(tournament, "size") : null;
            int? wins = ReadInt32(node, "wins");
            int? losses = ReadInt32(node, "losses");
            int? draws = ReadInt32(node, "draws");
            report.ExemplarDecks.Add(new DeckExemplarSignal
            {
                Name = $"{tournamentName} - {player}",
                Source = status.Name,
                Uri = ReadString(node, "decklist"),
                Commander = query.Commander,
                PopularityMetric = "cEDH tournament standing",
                PopularityValue = standing,
                Tags = ["cEDH"],
                Weight = ScoreByStanding(standing, size),
                Notes = BuildEntryNotes(standing, size, wins, losses, draws)
            });
        }
    }

    /// <summary>
    /// Gets configured EDHTop16 source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return DecklistCorpusProviderSupport.SourceOptions(options, "EdhTop16", defaultEnabled: true);
    }

    /// <summary>
    /// Finds the commander object inside an EDHTop16 response.
    /// </summary>
    private static bool TryGetCommander(JsonElement root, out JsonElement commander)
    {
        commander = default;
        if (!root.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("commander", out commander)
            || commander.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Scores an EDHTop16 entry from standing and event size.
    /// </summary>
    private static double ScoreByStanding(int? standing, int? eventSize)
    {
        double standingScore = standing switch
        {
            null => 0.55,
            <= 1 => 1.00,
            <= 4 => 0.90,
            <= 8 => 0.80,
            <= 16 => 0.65,
            _ => 0.50
        };
        double sizeBonus = eventSize switch
        {
            null => 0,
            >= 100 => 0.08,
            >= 50 => 0.04,
            _ => 0
        };
        return Math.Clamp(standingScore + sizeBonus, 0, 1);
    }

    /// <summary>
    /// Builds compact notes for one EDHTop16 entry.
    /// </summary>
    private static string BuildEntryNotes(int? standing, int? size, int? wins, int? losses, int? draws)
    {
        List<string> parts = [];
        if (standing.HasValue)
        {
            parts.Add($"standing {standing.Value}");
        }

        if (size.HasValue)
        {
            parts.Add($"event size {size.Value}");
        }

        if (wins.HasValue || losses.HasValue || draws.HasValue)
        {
            parts.Add($"{wins ?? 0}-{losses ?? 0}-{draws ?? 0}");
        }

        return parts.Count == 0 ? "Sampled from EDHTop16 cEDH tournament entries." : string.Join("; ", parts);
    }

    /// <summary>
    /// Reads a string property when present.
    /// </summary>
    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Reads an integer property when present.
    /// </summary>
    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    /// <summary>
    /// Reads a double property when present.
    /// </summary>
    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.TryGetDouble(out double result)
            ? result
            : null;
    }
}
