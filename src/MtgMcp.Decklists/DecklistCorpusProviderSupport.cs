using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Decklists;

/// <summary>
/// Shared helpers for structured decklist corpus adapters.
/// </summary>
internal static class DecklistCorpusProviderSupport
{
    /// <summary>
    /// Reads a configured corpus source option row.
    /// </summary>
    public static MtgMcpCorpusSourceOptions SourceOptions(MtgMcpOptions options, string sourceName, bool defaultEnabled)
    {
        return options.Intelligence.Sources.TryGetValue(sourceName, out MtgMcpCorpusSourceOptions? sourceOptions)
            ? sourceOptions
            : new MtgMcpCorpusSourceOptions { Enabled = defaultEnabled };
    }

    /// <summary>
    /// Gets whether a response payload appears to be HTML.
    /// </summary>
    public static bool LooksLikeHtml(string payload)
    {
        return payload.TrimStart().StartsWith('<');
    }

    /// <summary>
    /// Extracts card names from common deck object and text shapes.
    /// </summary>
    public static List<string> ExtractCards(JsonElement element)
    {
        List<string> cards = [];
        AddCards(element, cards);
        return cards
            .Where(card => !string.IsNullOrWhiteSpace(card))
            .Select(card => card.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adds scored card signals from exemplar decks.
    /// </summary>
    public static void AddSignalsFromExemplars(
        CorpusSignalReport report,
        string source,
        IReadOnlyList<DecklistExemplar> exemplars,
        int maxCandidates)
    {
        int deckCount = Math.Max(1, exemplars.Count);
        foreach (IGrouping<string, DecklistExemplar> group in exemplars
            .SelectMany(deck => deck.Cards.Select(card => (Card: card, Deck: deck)))
            .GroupBy(pair => pair.Card, pair => pair.Deck, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(maxCandidates))
        {
            double inclusion = group.Count() / (double)deckCount;
            double performance = group.Max(deck => deck.Weight);
            report.Signals.Add(new CardCorpusSignal
            {
                CardName = group.Key,
                Source = source,
                SignalType = CorpusSignalTypes.Inclusion,
                Score = Math.Clamp(0.35 + (inclusion * 0.45) + (performance * 0.20), 0, 1),
                InclusionRate = inclusion,
                DeckCount = group.Count(),
                PerformanceScore = performance,
                Uri = group.First().Uri,
                Rationale = $"{group.Key} appeared in {group.Count()} sampled {source} deck(s)."
            });
        }
    }

    /// <summary>
    /// Adds card names from a supported JSON value.
    /// </summary>
    private static void AddCards(JsonElement element, List<string> cards)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                AddCardsFromObject(element, cards);
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    AddCards(item, cards);
                }

                break;
            case JsonValueKind.String:
                AddCardsFromText(element.GetString() ?? "", cards);
                break;
        }
    }

    /// <summary>
    /// Adds card names from a supported JSON object.
    /// </summary>
    private static void AddCardsFromObject(JsonElement element, List<string> cards)
    {
        string? name = ReadString(element, "name") ?? ReadString(element, "cardName") ?? ReadString(element, "card");
        if (!string.IsNullOrWhiteSpace(name))
        {
            cards.Add(name);
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String
                && LooksLikeCardNameProperty(property.Name))
            {
                cards.Add(property.Name);
                continue;
            }

            AddCards(property.Value, cards);
        }
    }

    /// <summary>
    /// Adds card names from a decklist text payload.
    /// </summary>
    private static void AddCardsFromText(string text, List<string> cards)
    {
        if (!text.Contains('\n', StringComparison.Ordinal) && Uri.TryCreate(text, UriKind.Absolute, out _))
        {
            return;
        }

        ParsedDecklist parsed = DeckParser.Parse(text);
        cards.AddRange(parsed.Cards.Select(card => card.Name));
    }

    /// <summary>
    /// Gets whether an object property likely represents a card name.
    /// </summary>
    private static bool LooksLikeCardNameProperty(string name)
    {
        return name.Contains(' ', StringComparison.Ordinal)
            || name.Contains(',', StringComparison.Ordinal)
            || name.Contains(" // ", StringComparison.Ordinal);
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

/// <summary>
/// Represents one sampled source deck before normalized signal generation.
/// </summary>
internal sealed class DecklistExemplar
{
    /// <summary>
    /// Gets or sets the exemplar deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the source deck URL.
    /// </summary>
    public string? Uri { get; set; }

    /// <summary>
    /// Gets or sets the commander name when known.
    /// </summary>
    public string? Commander { get; set; }

    /// <summary>
    /// Gets or sets source-derived exemplar weight.
    /// </summary>
    public double Weight { get; set; }

    /// <summary>
    /// Gets or sets distinct card names observed in the deck.
    /// </summary>
    public List<string> Cards { get; set; } = [];
}
