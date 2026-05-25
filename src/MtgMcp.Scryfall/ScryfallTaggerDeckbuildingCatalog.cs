using System.Reflection;
using System.Text.Json;

namespace MtgMcp.Scryfall;

/// <summary>
/// Loads curated high-signal Scryfall Tagger oracle tags for deterministic deckbuilding evidence.
/// </summary>
internal static class ScryfallTaggerDeckbuildingCatalog
{
    /// <summary>
    /// Identifies the embedded structured catalog data.
    /// </summary>
    private const string ResourceName = "MtgMcp.Scryfall.ScryfallTaggerDeckbuildingCatalog.json";

    /// <summary>
    /// Configures structured catalog deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Lists fallback slugs that cover common Commander deck construction needs.
    /// </summary>
    private static readonly IReadOnlyList<string> FallbackSlugs =
    [
        "ramp",
        "pure-draw",
        "spot-removal",
        "sweeper",
        "tutor-card",
        "gives-protection",
        "hate-graveyard",
        "card-advantage"
    ];

    /// <summary>
    /// Gets deterministic Scryfall Tagger lookup rules grouped by common deckbuilding language.
    /// </summary>
    public static IReadOnlyList<ScryfallTaggerRule> Rules { get; } = LoadRules();

    /// <summary>
    /// Gets fallback rules for broad deck analysis when the user goal has no exact tag-language match.
    /// </summary>
    public static IReadOnlyList<ScryfallTaggerRule> FallbackRules { get; } = FallbackSlugs
        .Select(slug => Rules.First(rule => rule.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    /// <summary>
    /// Reads the embedded JSON catalog into immutable lookup rules.
    /// </summary>
    private static IReadOnlyList<ScryfallTaggerRule> LoadRules()
    {
        Assembly assembly = typeof(ScryfallTaggerDeckbuildingCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded Tagger catalog resource '{ResourceName}' was not found.");
        List<ScryfallTaggerRuleData> data = JsonSerializer.Deserialize<List<ScryfallTaggerRuleData>>(
                stream,
                SerializerOptions)
            ?? throw new InvalidOperationException("Embedded Tagger catalog resource could not be read.");

        List<ScryfallTaggerRule> rules = new(data.Count);
        foreach (ScryfallTaggerRuleData rule in data)
        {
            rules.Add(new ScryfallTaggerRule(
                rule.Slug,
                rule.Description,
                rule.Role,
                rule.SecondaryTag,
                rule.TaggingCount,
                rule.Priority,
                rule.Needles));
        }

        return rules;
    }

    /// <summary>
    /// Represents one rule row from the structured Tagger catalog.
    /// </summary>
    private sealed record ScryfallTaggerRuleData(
        string Slug,
        string Description,
        string Role,
        string SecondaryTag,
        int? TaggingCount,
        int Priority,
        IReadOnlyList<string> Needles);
}

/// <summary>
/// Describes one deterministic Scryfall Tagger lookup rule.
/// </summary>
internal sealed record ScryfallTaggerRule(
    string Slug,
    string Description,
    string Role,
    string SecondaryTag,
    int? TaggingCount,
    int Priority,
    IReadOnlyList<string> Needles)
{
    /// <summary>
    /// Gets whether this rule matches query text.
    /// </summary>
    public bool Matches(string text)
    {
        return Needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
