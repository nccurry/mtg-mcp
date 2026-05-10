using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MtgMcp.Core;

namespace MtgMcp.CommanderSpellbook;

/// <summary>
/// Reads combo and near-miss data from Commander Spellbook.
/// </summary>
public sealed class CommanderSpellbookComboCatalog : IComboCatalog
{
    /// <summary>
    /// Sends requests to Commander Spellbook.
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// Stores the shared source-fact cache.
    /// </summary>
    private readonly ICorpusCache cache;

    /// <summary>
    /// Stores mtg-mcp options.
    /// </summary>
    private readonly MtgMcpOptions mtgOptions;

    /// <summary>
    /// Creates a Commander Spellbook combo catalog.
    /// </summary>
    public CommanderSpellbookComboCatalog(
        HttpClient httpClient,
        IOptions<CommanderSpellbookOptions> options,
        ICorpusCache cache,
        IOptions<MtgMcpOptions> mtgOptions)
    {
        this.httpClient = httpClient;
        this.cache = cache;
        this.mtgOptions = mtgOptions.Value;
        this.httpClient.BaseAddress ??= options.Value.BaseAddress;
        this.httpClient.DefaultRequestHeaders.UserAgent.Clear();
        this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("mtg-mcp/1.0 (+https://github.com/nccurry/mtg-mcp)");
        this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Finds combos and near misses for the supplied deck card names.
    /// </summary>
    public async Task<DeckComboReport> FindCombosAsync(
        ComboCatalogQuery query,
        CancellationToken cancellationToken)
    {
        string cardList = string.Join(
            '\n',
            query.CardNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase));
        CorpusCacheKey cacheKey = new()
        {
            Source = "commander-spellbook",
            Endpoint = "find-my-combos",
            Query = cardList,
            AdapterVersion = "2",
            Budget = "combo"
        };
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(
            mtgOptions.Intelligence.Cache.Ttls.CommanderSpellbook,
            TimeSpan.FromHours(24));
        if (!query.Refresh)
        {
            DeckComboReport? cached = await cache.GetAsync<DeckComboReport>(cacheKey, ttl, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        using StringContent content = new(cardList, Encoding.UTF8, "text/plain");
        using HttpResponseMessage response = await httpClient
            .PostAsync("find-my-combos", content, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        DeckComboReport report = new();
        JsonElement results = document.RootElement.GetProperty("results");
        HashSet<string> present = query.CardNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        report.Combos.AddRange(ReadCombos(results, "included", present, nearMiss: false));
        report.NearMisses.AddRange(ReadCombos(results, "almostIncluded", present, nearMiss: true));
        report.Notes.Add("Commander Spellbook combo data comes from the public find-my-combos endpoint.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Reads a combo array from the response.
    /// </summary>
    private static IEnumerable<DeckCombo> ReadCombos(
        JsonElement results,
        string propertyName,
        HashSet<string> present,
        bool nearMiss)
    {
        if (!results.TryGetProperty(propertyName, out JsonElement combos) || combos.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement combo in combos.EnumerateArray().Take(50))
        {
            List<string> cardNames = ReadUsedCardNames(combo);
            List<string> presentCards = cardNames
                .Where(card => present.Contains(card))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> missingCards = nearMiss
                ? cardNames.Where(card => !present.Contains(card)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : [];
            missingCards.AddRange(ReadRequiredTemplates(combo));
            string winRoute = ReadProducedFeatures(combo);
            yield return new DeckCombo
            {
                Name = cardNames.Count == 0 ? ReadString(combo, "id") ?? "Commander Spellbook combo" : string.Join(" + ", cardNames),
                Cards = presentCards,
                MissingCards = missingCards.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                WinRoute = string.IsNullOrWhiteSpace(winRoute) ? "combo line" : winRoute,
                Kind = ClassifyKind(winRoute),
                Confidence = nearMiss ? 0.65 : 0.90,
                Source = "commander-spellbook",
                Rationale = ReadString(combo, "description") ?? "Matched by Commander Spellbook find-my-combos."
            };
        }
    }

    /// <summary>
    /// Reads card names used by a combo.
    /// </summary>
    private static List<string> ReadUsedCardNames(JsonElement combo)
    {
        List<string> names = [];
        if (!combo.TryGetProperty("uses", out JsonElement uses) || uses.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (JsonElement use in uses.EnumerateArray())
        {
            if (use.TryGetProperty("card", out JsonElement card))
            {
                string? name = ReadString(card, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reads template requirements from a near-miss combo.
    /// </summary>
    private static IEnumerable<string> ReadRequiredTemplates(JsonElement combo)
    {
        if (!combo.TryGetProperty("requires", out JsonElement requires) || requires.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement requirement in requires.EnumerateArray())
        {
            if (requirement.TryGetProperty("template", out JsonElement template))
            {
                string? name = ReadString(template, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name;
                }
            }
        }
    }

    /// <summary>
    /// Reads produced feature names from a combo.
    /// </summary>
    private static string ReadProducedFeatures(JsonElement combo)
    {
        if (!combo.TryGetProperty("produces", out JsonElement produces) || produces.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(
            ", ",
            produces.EnumerateArray()
                .Select(item => item.TryGetProperty("feature", out JsonElement feature) ? ReadString(feature, "name") : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Classifies a combo route into a broad kind.
    /// </summary>
    private static string ClassifyKind(string winRoute)
    {
        if (winRoute.Contains("win", StringComparison.OrdinalIgnoreCase)
            || winRoute.Contains("damage", StringComparison.OrdinalIgnoreCase)
            || winRoute.Contains("mana", StringComparison.OrdinalIgnoreCase))
        {
            return "combo";
        }

        if (winRoute.Contains("lock", StringComparison.OrdinalIgnoreCase)
            || winRoute.Contains("skip", StringComparison.OrdinalIgnoreCase))
        {
            return "lock";
        }

        return "value";
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
