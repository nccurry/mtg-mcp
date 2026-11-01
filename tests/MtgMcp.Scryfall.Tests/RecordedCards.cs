using System.Text.Json.Nodes;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Loads real Scryfall card objects recorded once from api.scryfall.com (read-only) into test fixture files, so tests
/// stay offline and measure real card shapes and sizes.
/// </summary>
internal static class RecordedCards
{
    /// <summary>
    /// Names a staple card that is legal in 15 formats.
    /// </summary>
    internal const string LightningBolt = "lightning-bolt";

    /// <summary>
    /// Names a transform card whose top-level mana cost is empty.
    /// </summary>
    internal const string Transform = "delver-of-secrets-transform";

    /// <summary>
    /// Names an adventure card that keeps its mana cost and colors at the top level.
    /// </summary>
    internal const string Adventure = "bonecrusher-giant-adventure";

    /// <summary>
    /// Names a planeswalker.
    /// </summary>
    internal const string Planeswalker = "jace-the-mind-sculptor-planeswalker";

    /// <summary>
    /// Names a reversible card, which has no Oracle ID at the top level.
    /// </summary>
    internal const string Reversible = "adrix-and-nev-reversible";

    /// <summary>
    /// Names a card whose prices are all null.
    /// </summary>
    internal const string NoPrices = "abandoned-sarcophagus-no-prices";

    /// <summary>
    /// Lists every recorded card name.
    /// </summary>
    internal static IReadOnlyList<string> All { get; } =
        [LightningBolt, Transform, Adventure, Planeswalker, Reversible, NoPrices];

    /// <summary>
    /// Reads the recorded JSON text of one card.
    /// </summary>
    internal static string Raw(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "recorded", "cards-id", name + ".json"));
    }

    /// <summary>
    /// Reads one recorded card as a JSON object that a test can change.
    /// </summary>
    internal static JsonObject Object(string name)
    {
        return JsonNode.Parse(Raw(name))!.AsObject();
    }
}
