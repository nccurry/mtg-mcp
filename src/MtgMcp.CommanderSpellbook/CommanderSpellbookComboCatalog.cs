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
            AdapterVersion = "3",
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
        if (document.RootElement.TryGetProperty("results", out JsonElement results)
            && results.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> present = query.CardNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            report.Combos.AddRange(ReadCombos(results, "included", present, nearMiss: false));
            report.NearMisses.AddRange(ReadCombos(results, "almostIncluded", present, nearMiss: true));
        }
        else
        {
            report.Notes.Add("Commander Spellbook response did not include combo results.");
        }

        report.Notes.Add("Commander Spellbook combo data comes from the public find-my-combos endpoint.");
        await cache.SetAsync(cacheKey, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Finds catalog combos that contain one card.
    /// </summary>
    public async Task<IReadOnlyList<ComboEvidence>> SearchCombosByCardAsync(
        ComboCardSearchQuery query,
        CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(query.Limit, 1, 100);
        string normalizedCardName = query.CardName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCardName))
        {
            return [];
        }

        string normalizedFormat = NormalizeFormat(query.Format);
        CorpusCacheKey cacheKey = new()
        {
            Source = "commander-spellbook",
            Endpoint = "variants-search",
            Query = $"{normalizedCardName}|{normalizedFormat}|{limit}",
            AdapterVersion = "1",
            Budget = "combo-card-search"
        };
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(
            mtgOptions.Intelligence.Cache.Ttls.CommanderSpellbook,
            TimeSpan.FromHours(24));
        if (!query.Refresh)
        {
            IReadOnlyList<ComboEvidence>? cached = await cache
                .GetAsync<IReadOnlyList<ComboEvidence>>(cacheKey, ttl, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        string search = Uri.EscapeDataString($"card:\"{normalizedCardName}\"");
        string path = $"variants?search={search}&limit={limit}&ordering=-popularity";
        using HttpResponseMessage response = await httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        List<ComboEvidence> results = [];
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("results", out JsonElement pagedResults) && pagedResults.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement combo in pagedResults.EnumerateArray().Take(limit))
            {
                ComboEvidence evidence = ReadComboEvidence(combo, present: null, nearMiss: false);
                if (IsLegalInFormat(evidence, normalizedFormat))
                {
                    results.Add(evidence);
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement combo in root.EnumerateArray().Take(limit))
            {
                ComboEvidence evidence = ReadComboEvidence(combo, present: null, nearMiss: false);
                if (IsLegalInFormat(evidence, normalizedFormat))
                {
                    results.Add(evidence);
                }
            }
        }

        await cache.SetAsync(cacheKey, results, cancellationToken).ConfigureAwait(false);
        return results;
    }

    /// <summary>
    /// Gets raw-preserving details for one Commander Spellbook combo.
    /// </summary>
    public async Task<ComboEvidence?> GetComboDetailsAsync(
        ComboDetailsQuery query,
        CancellationToken cancellationToken)
    {
        string comboId = query.ComboId.Trim();
        if (string.IsNullOrWhiteSpace(comboId))
        {
            return null;
        }

        CorpusCacheKey cacheKey = new()
        {
            Source = "commander-spellbook",
            Endpoint = "variants-detail",
            Query = comboId,
            AdapterVersion = "1",
            Budget = "combo-detail"
        };
        TimeSpan ttl = CorpusCacheFactory.ParseDuration(
            mtgOptions.Intelligence.Cache.Ttls.CommanderSpellbook,
            TimeSpan.FromHours(24));
        if (!query.Refresh)
        {
            ComboEvidence? cached = await cache.GetAsync<ComboEvidence>(cacheKey, ttl, cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        using HttpResponseMessage response = await httpClient
            .GetAsync($"variants/{Uri.EscapeDataString(comboId)}", cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        ComboEvidence evidence = ReadComboEvidence(document.RootElement, present: null, nearMiss: false);
        await cache.SetAsync(cacheKey, evidence, cancellationToken).ConfigureAwait(false);
        return evidence;
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
            yield return BuildDeckCombo(ReadComboEvidence(combo, present, nearMiss), nearMiss);
        }
    }

    /// <summary>
    /// Converts raw catalog evidence into the deck combo shape.
    /// </summary>
    private static DeckCombo BuildDeckCombo(ComboEvidence evidence, bool nearMiss)
    {
        string winRoute = string.Join(", ", evidence.ProducedFeatures);
        WinRouteClassification? route = evidence.RouteClassifications.FirstOrDefault();
        return new DeckCombo
        {
            ComboId = evidence.ComboId,
            Name = evidence.Cards.Count == 0 ? evidence.ComboId : string.Join(" + ", evidence.Cards),
            Cards = nearMiss
                ? evidence.Cards.Where(card => !evidence.MissingCards.Contains(card, StringComparer.OrdinalIgnoreCase)).ToList()
                : evidence.Cards.ToList(),
            MissingCards = evidence.MissingCards.ToList(),
            ProducedFeatures = evidence.ProducedFeatures.ToList(),
            RequiredTemplates = evidence.Templates.ToList(),
            Prerequisites = evidence.Prerequisites.ToList(),
            Steps = evidence.Steps.ToList(),
            ColorIdentity = evidence.ColorIdentity.ToList(),
            WinRoute = string.IsNullOrWhiteSpace(winRoute) ? "combo line" : winRoute,
            RouteLabels = route?.RouteTypes.ToList() ?? [],
            Terminal = route?.Terminal ?? false,
            NeedsPayoff = route?.NeedsPayoff ?? false,
            PayoffKindsNeeded = route?.PayoffKindsNeeded.ToList() ?? [],
            Kind = ClassifyKind(winRoute),
            Confidence = nearMiss ? 0.65 : 0.90,
            Source = "commander-spellbook",
            Rationale = evidence.Prerequisites.Count == 0
                ? "Matched by Commander Spellbook catalog evidence."
                : string.Join(" ", evidence.Prerequisites.Take(2)),
            SourceUri = evidence.SourceUri,
            Metadata = evidence.Metadata
        };
    }

    /// <summary>
    /// Reads raw-preserving combo evidence from a Commander Spellbook combo object.
    /// </summary>
    private static ComboEvidence ReadComboEvidence(
        JsonElement combo,
        HashSet<string>? present,
        bool nearMiss)
    {
        string comboId = ReadString(combo, "id") ?? "";
        List<string> cardNames = ReadUsedCardNames(combo);
        List<string> missingCards = nearMiss && present is not null
            ? cardNames.Where(card => !present.Contains(card)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        List<string> templates = ReadRequiredTemplates(combo).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        missingCards.AddRange(templates);
        List<string> producedFeatures = ReadProducedFeatureNames(combo);
        ComboEvidence evidence = new()
        {
            ComboId = comboId,
            Cards = cardNames,
            MissingCards = missingCards.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ProducedFeatures = producedFeatures,
            Requires = ReadRequirementNames(combo).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Templates = templates,
            Prerequisites = ReadFlexibleTextArray(combo, "prerequisites"),
            Steps = ReadFlexibleTextArray(combo, "steps"),
            ColorIdentity = ReadColorIdentity(combo),
            BracketTag = ReadBracketTag(combo),
            Popularity = ReadDouble(combo, "popularity") ?? ReadDouble(combo, "popularityScore"),
            Legalities = ReadLegalities(combo),
            SourceUri = string.IsNullOrWhiteSpace(comboId)
                ? "https://commanderspellbook.com/"
                : $"https://commanderspellbook.com/combo/{comboId}/",
            Metadata = new SourceEvidenceMetadata
            {
                Source = "commander-spellbook",
                SourceKind = "combo-catalog",
                SourceUri = string.IsNullOrWhiteSpace(comboId)
                    ? "https://commanderspellbook.com/"
                    : $"https://commanderspellbook.com/combo/{comboId}/",
                CacheStatus = "live-or-cache",
                Confidence = 0.90,
                Deterministic = true,
                Notes = ["Commander Spellbook is catalog evidence, not formal proof that a line works in every possible game state."]
            }
        };
        string description = ReadString(combo, "description") ?? "";
        if (!string.IsNullOrWhiteSpace(description))
        {
            AddDescriptionSteps(evidence, description);
        }

        AddPrerequisiteText(evidence, ReadString(combo, "easyPrerequisites"));
        AddPrerequisiteText(evidence, ReadString(combo, "notablePrerequisites"));
        if (!string.IsNullOrWhiteSpace(description) && evidence.Steps.Count == 0 && evidence.Prerequisites.Count == 0)
        {
            evidence.Prerequisites.Add(description);
        }

        evidence.RouteClassifications.Add(WinRouteClassifier.ClassifyProducedFeatures(
            string.IsNullOrWhiteSpace(comboId) ? string.Join(" + ", cardNames) : comboId,
            producedFeatures,
            evidence.Metadata));
        return evidence;
    }

    /// <summary>
    /// Adds newline-delimited description steps from live Commander Spellbook rows.
    /// </summary>
    private static void AddDescriptionSteps(ComboEvidence evidence, string description)
    {
        foreach (string step in description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!evidence.Steps.Contains(step, StringComparer.OrdinalIgnoreCase))
            {
                evidence.Steps.Add(step);
            }
        }
    }

    /// <summary>
    /// Adds prerequisite text when Commander Spellbook exposes it as a scalar field.
    /// </summary>
    private static void AddPrerequisiteText(ComboEvidence evidence, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (string prerequisite in value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!evidence.Prerequisites.Contains(prerequisite, StringComparer.OrdinalIgnoreCase))
            {
                evidence.Prerequisites.Add(prerequisite);
            }
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
    /// Reads requirement names from a combo.
    /// </summary>
    private static IEnumerable<string> ReadRequirementNames(JsonElement combo)
    {
        if (!combo.TryGetProperty("requires", out JsonElement requires) || requires.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement requirement in requires.EnumerateArray())
        {
            string? direct = ReadString(requirement, "name") ?? ReadString(requirement, "description");
            if (!string.IsNullOrWhiteSpace(direct))
            {
                yield return direct;
            }

            if (requirement.TryGetProperty("card", out JsonElement card))
            {
                string? cardName = ReadString(card, "name");
                if (!string.IsNullOrWhiteSpace(cardName))
                {
                    yield return cardName;
                }
            }

            if (requirement.TryGetProperty("template", out JsonElement template))
            {
                string? templateName = ReadString(template, "name");
                if (!string.IsNullOrWhiteSpace(templateName))
                {
                    yield return templateName;
                }
            }
        }
    }

    /// <summary>
    /// Reads produced feature names from a combo.
    /// </summary>
    private static List<string> ReadProducedFeatureNames(JsonElement combo)
    {
        if (!combo.TryGetProperty("produces", out JsonElement produces) || produces.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> names = [];
        foreach (JsonElement item in produces.EnumerateArray())
        {
            string? name = ReadString(item, "name") ?? ReadString(item, "description");
            if (item.TryGetProperty("feature", out JsonElement feature))
            {
                name ??= ReadString(feature, "name");
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reads a flexible text array whose items can be strings or objects.
    /// </summary>
    private static List<string> ReadFlexibleTextArray(JsonElement combo, string propertyName)
    {
        List<string> values = [];
        if (!combo.TryGetProperty(propertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            string? value = item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : ReadString(item, "name") ?? ReadString(item, "description") ?? ReadString(item, "instruction");
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Reads color identity from common Commander Spellbook response shapes.
    /// </summary>
    private static List<string> ReadColorIdentity(JsonElement combo)
    {
        JsonElement value;
        if (!combo.TryGetProperty("identity", out value)
            && !combo.TryGetProperty("colorIdentity", out value)
            && !combo.TryGetProperty("color_identity", out value))
        {
            return [];
        }

        List<string> colors = [];
        if (value.ValueKind == JsonValueKind.String)
        {
            foreach (char character in value.GetString() ?? "")
            {
                string color = character.ToString().ToUpperInvariant();
                if ("WUBRG".Contains(color, StringComparison.Ordinal) && !colors.Contains(color))
                {
                    colors.Add(color);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                string? color = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : ReadString(item, "name");
                if (!string.IsNullOrWhiteSpace(color))
                {
                    string normalized = color.Trim().ToUpperInvariant();
                    if ("WUBRG".Contains(normalized, StringComparison.Ordinal) && !colors.Contains(normalized))
                    {
                        colors.Add(normalized);
                    }
                }
            }
        }

        return colors;
    }

    /// <summary>
    /// Reads a Commander Spellbook bracket tag from string or object shapes.
    /// </summary>
    private static string? ReadBracketTag(JsonElement combo)
    {
        if (!combo.TryGetProperty("bracketTag", out JsonElement tag)
            && !combo.TryGetProperty("bracket_tag", out tag))
        {
            return null;
        }

        return tag.ValueKind == JsonValueKind.String
            ? tag.GetString()
            : ReadString(tag, "name") ?? ReadString(tag, "slug");
    }

    /// <summary>
    /// Reads format legalities from Commander Spellbook rows when present.
    /// </summary>
    private static Dictionary<string, bool> ReadLegalities(JsonElement combo)
    {
        Dictionary<string, bool> legalities = new(StringComparer.OrdinalIgnoreCase);
        if (!combo.TryGetProperty("legalities", out JsonElement legalitiesElement)
            || legalitiesElement.ValueKind != JsonValueKind.Object)
        {
            return legalities;
        }

        foreach (JsonProperty property in legalitiesElement.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                legalities[property.Name] = property.Value.GetBoolean();
            }
        }

        return legalities;
    }

    /// <summary>
    /// Checks catalog-reported legality when the response includes format flags.
    /// </summary>
    private static bool IsLegalInFormat(ComboEvidence evidence, string format)
    {
        return evidence.Legalities.Count == 0
            || evidence.Legalities.TryGetValue(format, out bool legal) && legal;
    }

    /// <summary>
    /// Normalizes public format names to Commander Spellbook legality keys.
    /// </summary>
    private static string NormalizeFormat(string format)
    {
        string normalized = string.IsNullOrWhiteSpace(format) ? "commander" : format.Trim();
        return normalized.Equals("paupercommander", StringComparison.OrdinalIgnoreCase)
            ? "pauperCommander"
            : normalized;
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
            JsonValueKind.String when double.TryParse(value.GetString(), out double number) => number,
            _ => null
        };
    }
}
