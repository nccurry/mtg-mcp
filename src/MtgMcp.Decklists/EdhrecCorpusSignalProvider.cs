using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Produces broad Commander recommendation signals from EDHREC aggregate JSON pages.
/// </summary>
public sealed class EdhrecCorpusSignalProvider : ICorpusSignalProvider
{
    /// <summary>
    /// Stores the default EDHREC static JSON root.
    /// </summary>
    private static readonly Uri DefaultBaseAddress = new("https://json.edhrec.com/pages/");

    /// <summary>
    /// Sends requests to EDHREC's structured JSON host.
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
    /// Creates an EDHREC recommendation source provider.
    /// </summary>
    public EdhrecCorpusSignalProvider(
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
    /// Gets EDHREC source capability and configuration status.
    /// </summary>
    public CorpusSourceStatus GetStatus()
    {
        MtgMcpCorpusSourceOptions sourceOptions = SourceOptions();
        bool enabled = sourceOptions.Enabled
            && DecklistCorpusProviderSupport.AllowsUnofficialApi(sourceOptions, defaultAllowed: true);
        return new CorpusSourceStatus
        {
            Key = "edhrec",
            Name = "EDHREC",
            Kind = "commander-aggregate",
            Enabled = enabled,
            StableApi = false,
            ApiType = CorpusSourceApiTypes.UnofficialApi,
            UnofficialApi = true,
            RequiresKey = false,
            PermissionSensitive = true,
            AttributionRequired = true,
            Status = sourceOptions.Enabled
                ? enabled ? CorpusSourceStatusKind.Available : CorpusSourceStatusKind.Disabled
                : CorpusSourceStatusKind.Disabled,
            Uri = "https://edhrec.com/",
            Notes =
            [
                "Uses EDHREC's structured aggregate JSON pages for broad Commander inclusion and synergy evidence.",
                "Enabled by default; set AllowUnofficialApi=false for the Edhrec source to disable this permission-sensitive endpoint."
            ]
        };
    }

    /// <summary>
    /// Gets broad Commander aggregate signals from EDHREC.
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

        if (!IsCommanderFormat(query.Format))
        {
            report.Notes.Add("EDHREC evidence only supports Commander/EDH deck contexts.");
            return report;
        }

        if (string.IsNullOrWhiteSpace(query.Commander))
        {
            report.Notes.Add("EDHREC evidence requires a commander name.");
            return report;
        }

        string commanderSlug = Slugify(query.Commander);
        if (string.IsNullOrWhiteSpace(commanderSlug))
        {
            report.Notes.Add("EDHREC evidence requires a commander name that can be converted to an EDHREC slug.");
            return report;
        }

        string? themeSlug = string.IsNullOrWhiteSpace(query.Theme) ? null : Slugify(query.Theme);
        if (string.IsNullOrWhiteSpace(themeSlug))
        {
            themeSlug = null;
        }

        CorpusCacheKey cacheKey = new()
        {
            Source = status.Key,
            Endpoint = "pages/commanders",
            Query = $"{commanderSlug}|{themeSlug}|{budget.AnalysisDepth}|{budget.MaxCandidates}",
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
                cached.Notes.Add("EDHREC signals returned from source-fact cache.");
                return cached;
            }
        }

        JsonDocument? document = null;
        try
        {
            bool usedTheme = false;
            if (themeSlug is not null)
            {
                (HttpStatusCode themeStatus, JsonDocument? themeDocument) = await FetchJsonPageAsync(
                    ThemePath(commanderSlug, themeSlug),
                    cancellationToken).ConfigureAwait(false);
                if (themeDocument is not null)
                {
                    document = themeDocument;
                    usedTheme = true;
                }
                else if (themeStatus is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    report.Notes.Add($"unsupported-theme: EDHREC did not expose theme slug '{themeSlug}' for this commander.");
                    return report;
                }
                else
                {
                    report.Notes.Add($"unsupported-theme: EDHREC theme slug '{themeSlug}' was unavailable for this lookup.");
                    return report;
                }
            }

            if (document is null)
            {
                (HttpStatusCode commanderStatus, JsonDocument? commanderDocument) = await FetchJsonPageAsync(
                    CommanderPath(commanderSlug),
                    cancellationToken).ConfigureAwait(false);
                document = commanderDocument;
                if (document is null && commanderStatus is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    report.Notes.Add("EDHREC commander page was unavailable for this commander.");
                    await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
                    return report;
                }
            }

            if (document is null)
            {
                report.Notes.Add("EDHREC returned no structured page for this commander context.");
                await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
                return report;
            }

            string evidenceUri = BuildEdhrecUri(commanderSlug, usedTheme ? themeSlug : null);
            AddSignals(report, document.RootElement, status, query.Commander, evidenceUri, budget);
            if (report.Signals.Count == 0)
            {
                report.Notes.Add("EDHREC returned no cardlist evidence for this commander context.");
            }

            report.Notes.Add("EDHREC evidence is broad Commander aggregate data, not tournament performance or source decklist evidence.");
            await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
            return report;
        }
        finally
        {
            document?.Dispose();
        }
    }

    /// <summary>
    /// Fetches one structured EDHREC JSON page, returning null for missing or forbidden pages.
    /// </summary>
    private async Task<(HttpStatusCode StatusCode, JsonDocument? Document)> FetchJsonPageAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return (response.StatusCode, null);
        }

        response.EnsureSuccessStatusCode();
        if (DecklistCorpusProviderSupport.LooksLikeHtml(payload))
        {
            throw new InvalidOperationException("EDHREC returned HTML; recommendation sources only accept structured API payloads.");
        }

        return (response.StatusCode, JsonDocument.Parse(payload));
    }

    /// <summary>
    /// Adds normalized card signals from EDHREC cardlist sections.
    /// </summary>
    private static void AddSignals(
        CorpusSignalReport report,
        JsonElement root,
        CorpusSourceStatus status,
        string commanderName,
        string evidenceUri,
        RecommendationAnalysisBudget budget)
    {
        if (!TryGetCardlists(root, out JsonElement cardlists))
        {
            return;
        }

        Dictionary<string, CardCorpusSignal> signalsByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement cardlist in cardlists.EnumerateArray())
        {
            string tag = ReadString(cardlist, "tag") ?? "";
            if (!cardlist.TryGetProperty("cardviews", out JsonElement cardviews)
                || cardviews.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement cardview in cardviews.EnumerateArray())
            {
                CardCorpusSignal? signal = BuildSignal(cardview, tag, status, commanderName, evidenceUri);
                if (signal is null)
                {
                    continue;
                }

                if (!signalsByName.TryGetValue(signal.CardName, out CardCorpusSignal? existing)
                    || signal.Score > existing.Score)
                {
                    signalsByName[signal.CardName] = signal;
                }
            }
        }

        List<CardCorpusSignal> signals = signalsByName.Values.ToList();
        signals.Sort((left, right) =>
        {
            int scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : string.Compare(left.CardName, right.CardName, StringComparison.OrdinalIgnoreCase);
        });

        int limit = Math.Min(signals.Count, budget.MaxCandidates);
        for (int i = 0; i < limit; i++)
        {
            report.Signals.Add(signals[i]);
        }
    }

    /// <summary>
    /// Builds one normalized signal from an EDHREC card view object.
    /// </summary>
    private static CardCorpusSignal? BuildSignal(
        JsonElement cardview,
        string tag,
        CorpusSourceStatus status,
        string commanderName,
        string evidenceUri)
    {
        string? name = ReadString(cardview, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        int? numDecks = ReadInt32(cardview, "num_decks") ?? ReadInt32(cardview, "inclusion");
        int? potentialDecks = ReadInt32(cardview, "potential_decks");
        double? inclusionRate = numDecks.HasValue && potentialDecks is > 0
            ? numDecks.Value / (double)potentialDecks.Value
            : null;
        double? synergy = ReadDouble(cardview, "synergy");
        double? trendZScore = ReadDouble(cardview, "trend_zscore");
        bool isTrend = tag.Equals("newcards", StringComparison.OrdinalIgnoreCase)
            || trendZScore is >= 2.0;
        double positiveSynergy = Math.Max(0, synergy ?? 0);
        double trendBoost = isTrend ? 0.10 : 0;
        double score = Math.Clamp(0.35 + ((inclusionRate ?? 0) * 0.40) + (positiveSynergy * 0.30) + trendBoost, 0, 1);

        return new CardCorpusSignal
        {
            CardName = name,
            Source = status.Name,
            SignalType = isTrend ? CorpusSignalTypes.Trend : CorpusSignalTypes.Inclusion,
            Section = tag,
            Score = score,
            InclusionRate = inclusionRate,
            SynergyScore = synergy.HasValue ? Math.Clamp(synergy.Value, 0, 1) : null,
            DeckCount = numDecks,
            EligibleDeckCount = potentialDecks,
            Uri = evidenceUri,
            Rationale = BuildRationale(name, commanderName, numDecks, potentialDecks, inclusionRate, synergy)
        };
    }

    /// <summary>
    /// Builds a compact EDHREC rationale for one card signal.
    /// </summary>
    private static string BuildRationale(
        string cardName,
        string commanderName,
        int? numDecks,
        int? potentialDecks,
        double? inclusionRate,
        double? synergy)
    {
        string deckCounts = numDecks.HasValue && potentialDecks.HasValue
            ? $"{numDecks.Value.ToString(CultureInfo.InvariantCulture)} of {potentialDecks.Value.ToString(CultureInfo.InvariantCulture)}"
            : "the sampled";
        string inclusion = inclusionRate.HasValue
            ? inclusionRate.Value.ToString("P1", CultureInfo.InvariantCulture)
            : "unknown";
        string synergyText = synergy.HasValue
            ? synergy.Value.ToString("P0", CultureInfo.InvariantCulture)
            : "unknown";
        return $"{cardName} appears in {deckCounts} EDHREC {commanderName} deck(s), with {inclusion} inclusion and {synergyText} synergy.";
    }

    /// <summary>
    /// Gets configured EDHREC source options.
    /// </summary>
    private MtgMcpCorpusSourceOptions SourceOptions()
    {
        return DecklistCorpusProviderSupport.SourceOptions(options, "Edhrec", defaultEnabled: true);
    }

    /// <summary>
    /// Gets whether a format label is compatible with EDHREC Commander data.
    /// </summary>
    private static bool IsCommanderFormat(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return true;
        }

        return format.Trim().ToLowerInvariant() is "commander" or "edh" or "cedh";
    }

    /// <summary>
    /// Builds a commander page path.
    /// </summary>
    private static string CommanderPath(string commanderSlug)
    {
        return $"commanders/{commanderSlug}.json";
    }

    /// <summary>
    /// Builds a commander theme page path.
    /// </summary>
    private static string ThemePath(string commanderSlug, string themeSlug)
    {
        return $"commanders/{commanderSlug}/{themeSlug}.json";
    }

    /// <summary>
    /// Builds a source URL suitable for user-visible attribution.
    /// </summary>
    private static string BuildEdhrecUri(string commanderSlug, string? themeSlug)
    {
        return themeSlug is null
            ? $"https://edhrec.com/commanders/{commanderSlug}"
            : $"https://edhrec.com/commanders/{commanderSlug}/{themeSlug}";
    }

    /// <summary>
    /// Converts EDHREC display text into the slug form used by static JSON pages.
    /// </summary>
    private static string Slugify(string value)
    {
        StringBuilder builder = new();
        bool lastWasDash = false;
        string normalized = value.Normalize(NormalizationForm.FormD);
        foreach (char character in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char lower = char.ToLowerInvariant(character);
            if (lower is '\'' or '"' or '\u2018' or '\u2019' or '\u201C' or '\u201D')
            {
                continue;
            }

            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            {
                builder.Append(lower);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    /// <summary>
    /// Finds the cardlist array inside an EDHREC static page response.
    /// </summary>
    private static bool TryGetCardlists(JsonElement root, out JsonElement cardlists)
    {
        cardlists = default;
        return root.TryGetProperty("container", out JsonElement container)
            && container.ValueKind == JsonValueKind.Object
            && container.TryGetProperty("json_dict", out JsonElement jsonDict)
            && jsonDict.ValueKind == JsonValueKind.Object
            && jsonDict.TryGetProperty("cardlists", out cardlists)
            && cardlists.ValueKind == JsonValueKind.Array;
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
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int number) => number,
            _ => null
        };
    }

    /// <summary>
    /// Reads a floating-point property when present.
    /// </summary>
    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out double number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double number) => number,
            _ => null
        };
    }
}
