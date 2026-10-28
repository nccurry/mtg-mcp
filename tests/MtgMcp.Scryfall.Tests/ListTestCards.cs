using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Builds small mapped cards for the pure card list functions, from the JSON shape Scryfall uses.
/// </summary>
internal static class ListTestCards
{
    /// <summary>
    /// Counts the cards built so far, so every card gets its own IDs.
    /// </summary>
    private static int counter;

    /// <summary>
    /// Builds one card. Only the values a test cares about need to be given.
    /// </summary>
    internal static ScryfallCard Make(
        string name,
        string typeLine = "Creature — Human",
        string? manaCost = "{1}",
        decimal manaValue = 1m,
        string[]? colors = null,
        string[]? producedMana = null,
        string? usd = null,
        string[]? keywords = null,
        string? oracleText = null,
        string? power = null,
        string[]? games = null,
        bool reserved = false,
        string? layout = "normal",
        Dictionary<string, string>? legalities = null,
        Guid? oracleId = null,
        Guid? printingId = null,
        Dictionary<string, string?>? prices = null)
    {
        int number = Interlocked.Increment(ref counter);
        JsonObject raw = new()
        {
            ["object"] = "card",
            ["id"] = (printingId ?? Guid.Parse($"00000000-0000-4000-8000-{number:D12}")).ToString(),
            ["oracle_id"] = (oracleId ?? Guid.Parse($"11111111-0000-4000-8000-{number:D12}")).ToString(),
            ["name"] = name,
            ["set"] = "tst",
            ["collector_number"] = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["lang"] = "en",
            ["type_line"] = typeLine,
            ["cmc"] = manaValue,
            ["colors"] = ToArray(colors ?? []),
            ["color_identity"] = ToArray(colors ?? []),
            ["keywords"] = ToArray(keywords ?? []),
            ["games"] = ToArray(games ?? ["paper"]),
            ["reserved"] = reserved,
            ["rarity"] = "common",
            ["layout"] = layout,
            ["legalities"] = JsonSerializer.SerializeToNode(legalities ?? new Dictionary<string, string>
            {
                ["commander"] = "legal",
            }),
            ["prices"] = JsonSerializer.SerializeToNode(prices ?? new Dictionary<string, string?> { ["usd"] = usd }),
        };
        if (manaCost is not null)
        {
            raw["mana_cost"] = manaCost;
        }

        if (producedMana is not null)
        {
            raw["produced_mana"] = ToArray(producedMana);
        }

        if (oracleText is not null)
        {
            raw["oracle_text"] = oracleText;
        }

        if (power is not null)
        {
            raw["power"] = power;
            raw["toughness"] = power;
        }

        using JsonDocument document = JsonDocument.Parse(raw.ToJsonString());
        return ScryfallMapper.Card(document.RootElement, DateTimeOffset.UnixEpoch, "test", includeRaw: false);
    }

    /// <summary>
    /// Wraps a card in a list entry with the given facts.
    /// </summary>
    internal static ScryfallListEntry Entry(
        ScryfallCard card,
        int quantity = 1,
        string[]? tags = null,
        string? cheapest = null,
        string? zone = null,
        string[]? categories = null)
    {
        return new ScryfallListEntry(card, tags ?? [], cheapest, quantity, zone, categories);
    }

    /// <summary>
    /// Converts text values to a JSON array.
    /// </summary>
    private static JsonArray ToArray(string[] values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
