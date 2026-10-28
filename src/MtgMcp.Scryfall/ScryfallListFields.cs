using System.Globalization;

namespace MtgMcp.Scryfall;

/// <summary>
/// Carries one card of a card list together with the facts a list query reads: the card itself, facts from
/// installed card data, and, for deck lists, the entry's quantity, zone, and categories.
/// </summary>
/// <param name="Card">The mapped card.</param>
/// <param name="Tags">Official oracle tag slugs of the card from installed card data.</param>
/// <param name="CheapestPriceUsd">The lowest USD price across installed English printings, or null.</param>
/// <param name="Quantity">The number of copies; 1 for a list that has no quantities.</param>
/// <param name="Zone">The deck zone of the entry, or null when the list has no zones.</param>
/// <param name="Categories">The category names of the entry, or null when the list has no categories.</param>
public sealed record ScryfallListEntry(
    ScryfallCard Card,
    IReadOnlyList<string> Tags,
    string? CheapestPriceUsd,
    int Quantity = 1,
    string? Zone = null,
    IReadOnlyList<string>? Categories = null);

/// <summary>
/// Names the kinds of value a list field holds, which decide the operators it accepts.
/// </summary>
internal enum ScryfallListFieldKind
{
    /// <summary>
    /// One text value.
    /// </summary>
    Text,

    /// <summary>
    /// One number.
    /// </summary>
    Number,

    /// <summary>
    /// One true or false value.
    /// </summary>
    Bool,

    /// <summary>
    /// Zero or more text values.
    /// </summary>
    TextList,
}

/// <summary>
/// Describes one field a list query can filter, group, sort, or return.
/// </summary>
/// <param name="Name">The name callers use.</param>
/// <param name="Kind">The kind of value the field holds.</param>
/// <param name="DeckOnly">Whether only lists with deck entries have the field.</param>
/// <param name="TextOperatorsOnly">Whether the field accepts only the contains operator (long text).</param>
/// <param name="Read">Reads the value; null means the data is missing. A list field returns a list.</param>
internal sealed record ScryfallListField(
    string Name,
    ScryfallListFieldKind Kind,
    bool DeckOnly,
    bool TextOperatorsOnly,
    Func<ScryfallListEntry, object?> Read);

/// <summary>
/// Holds the one table of list fields and the readers for mana costs and produced mana.
/// </summary>
internal static class ScryfallListFields
{
    /// <summary>
    /// Lists the card supertypes, which come before the card types on a type line.
    /// </summary>
    private static readonly HashSet<string> Supertypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Basic", "Elite", "Host", "Legendary", "Ongoing", "Snow", "World",
    };

    /// <summary>
    /// Maps each concise field the list query reuses to its reader, so both tools read cards the same way.
    /// </summary>
    private static readonly Dictionary<string, ScryfallPlainField> Concise = BuildConcise();

    /// <summary>
    /// Gets every list field in the order they are published.
    /// </summary>
    internal static IReadOnlyList<ScryfallListField> All { get; } =
    [
        TextField("name", entry => entry.Card.Name),
        NumberField("manaValue", entry => ConciseNumber(entry, "manaValue")),
        TextField("typeLine", entry => ConciseText(entry, "typeLine")),
        ListField("cardTypes", entry => TypeWords(entry.Card, wantTypes: true)),
        ListField("subtypes", entry => Subtypes(entry.Card)),
        ListField("supertypes", entry => TypeWords(entry.Card, wantTypes: false)),
        ListField("colors", entry => ConciseList(entry, "colors")),
        ListField("colorIdentity", entry => ConciseList(entry, "colorIdentity")),
        ListField("keywords", entry => ConciseList(entry, "keywords")),
        ListField("tags", entry => entry.Tags),
        TextField("rarity", entry => entry.Card.Rarity),
        TextField("set", entry => entry.Card.SetCode),
        NumberField("power", entry => NumberOrNull(ConciseText(entry, "power"))),
        NumberField("toughness", entry => NumberOrNull(ConciseText(entry, "toughness"))),
        NumberField("loyalty", entry => NumberOrNull(ConciseText(entry, "loyalty"))),
        NumberField("priceUsd", entry => Price(entry, "usd")),
        NumberField("cheapestPriceUsd", entry => NumberOrNull(entry.CheapestPriceUsd)),
        NumberField("edhrecRank", entry => ConciseNumber(entry, "edhrecRank")),
        BoolField("gameChanger", entry => entry.Card.GameChanger),
        ListField("producedMana", entry => ProducedColors(entry.Card)),
        TextField("zone", entry => entry.Zone, deckOnly: true),
        ListField("category", entry => entry.Categories, deckOnly: true),
        NumberField("quantity", entry => (decimal)entry.Quantity, deckOnly: true),
        new ScryfallListField(
            "oracleText",
            ScryfallListFieldKind.Text,
            DeckOnly: false,
            TextOperatorsOnly: true,
            entry => ConciseText(entry, "oracleText")),
        ListField("legalIn", entry => FormatsWithStatus(entry.Card, "legal")),
        ListField("bannedIn", entry => FormatsWithStatus(entry.Card, "banned")),
        ListField("games", entry => entry.Card.Games.Count == 0 ? null : entry.Card.Games),
        BoolField("reserved", entry => entry.Card.Reserved),
        TextField("layout", entry => entry.Card.Layout),
        NumberField("priceUsdFoil", entry => Price(entry, "usd_foil")),
        NumberField("priceUsdEtched", entry => Price(entry, "usd_etched")),
        NumberField("priceEur", entry => Price(entry, "eur")),
        NumberField("priceEurFoil", entry => Price(entry, "eur_foil")),
        NumberField("priceTix", entry => Price(entry, "tix")),
    ];

    /// <summary>
    /// Finds a field by name, or null when no field has that name.
    /// </summary>
    internal static ScryfallListField? Find(string name)
    {
        foreach (ScryfallListField field in All)
        {
            if (string.Equals(field.Name, name, StringComparison.Ordinal))
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// Lists the names of every field, in published order.
    /// </summary>
    internal static IReadOnlyList<string> Names { get; } = [.. All.Select(field => field.Name)];

    /// <summary>
    /// Counts the colored mana symbols of a mana cost by color, with a hybrid symbol counted once for each of its
    /// colors and also as hybrid, and a Phyrexian symbol counted for its color and also as Phyrexian. Generic,
    /// variable, snow, and empty symbols are not pips.
    /// </summary>
    internal static ManaPips CountPips(string? manaCost)
    {
        ManaPips pips = new();
        if (string.IsNullOrEmpty(manaCost))
        {
            return pips;
        }

        foreach (string symbol in ScryfallCardFaces.ManaSymbols(manaCost))
        {
            string[] parts = symbol.Split('/');
            bool phyrexian = parts.Contains("P", StringComparer.Ordinal);
            string[] colors =
            [
                .. parts.Where(part => ScryfallCardFaces.ManaColors.Contains(part, StringComparer.Ordinal)).Distinct(),
            ];
            if (colors.Length == 0 && !phyrexian)
            {
                continue;
            }

            foreach (string color in colors)
            {
                pips.Add(color, 1);
            }

            if (parts.Count(part => !string.Equals(part, "P", StringComparison.Ordinal)) >= 2)
            {
                pips.Hybrid++;
            }

            if (phyrexian)
            {
                pips.Phyrexian++;
            }
        }

        return pips;
    }

    /// <summary>
    /// Reads the mana cost of a card as the concise field does: its own, or the front face's.
    /// </summary>
    internal static string? ManaCostOf(ScryfallCard card)
    {
        return Concise["manaCost"].Read(card) as string;
    }

    /// <summary>
    /// Lists the colors Scryfall says a card produces, in the order it gave them; empty when it gave none.
    /// </summary>
    internal static IReadOnlyList<string> ProducedColors(ScryfallCard card)
    {
        return card.ProducedMana.Value is ScryfallProducedManaValues values ? values.Colors : [];
    }

    /// <summary>
    /// Builds the table of reused concise field readers.
    /// </summary>
    private static Dictionary<string, ScryfallPlainField> BuildConcise()
    {
        Dictionary<string, ScryfallPlainField> table = new(StringComparer.Ordinal);
        foreach (ScryfallConciseField field in ScryfallConciseFieldTable.Definitions)
        {
            if (field.Value is ScryfallPlainField plain)
            {
                table[plain.Name] = plain;
            }
        }

        return table;
    }

    /// <summary>
    /// Defines a text field.
    /// </summary>
    private static ScryfallListField TextField(string name, Func<ScryfallListEntry, string?> read,
        bool deckOnly = false)
    {
        return new ScryfallListField(name, ScryfallListFieldKind.Text, deckOnly, false, entry => read(entry));
    }

    /// <summary>
    /// Defines a number field.
    /// </summary>
    private static ScryfallListField NumberField(
        string name,
        Func<ScryfallListEntry, decimal?> read,
        bool deckOnly = false)
    {
        return new ScryfallListField(name, ScryfallListFieldKind.Number, deckOnly, false, entry => read(entry));
    }

    /// <summary>
    /// Defines a true or false field.
    /// </summary>
    private static ScryfallListField BoolField(string name, Func<ScryfallListEntry, bool?> read)
    {
        return new ScryfallListField(name, ScryfallListFieldKind.Bool, false, false, entry => read(entry));
    }

    /// <summary>
    /// Defines a field that holds a list of text values.
    /// </summary>
    private static ScryfallListField ListField(
        string name,
        Func<ScryfallListEntry, IReadOnlyList<string>?> read,
        bool deckOnly = false)
    {
        return new ScryfallListField(name, ScryfallListFieldKind.TextList, deckOnly, false, entry => read(entry));
    }

    /// <summary>
    /// Reads a concise text field of the card.
    /// </summary>
    private static string? ConciseText(ScryfallListEntry entry, string name)
    {
        return Concise[name].Read(entry.Card) as string;
    }

    /// <summary>
    /// Reads a concise numeric field of the card.
    /// </summary>
    private static decimal? ConciseNumber(ScryfallListEntry entry, string name)
    {
        return Concise[name].Read(entry.Card) switch
        {
            decimal value => value,
            long value => value,
            int value => value,
            _ => null,
        };
    }

    /// <summary>
    /// Reads a concise list field of the card.
    /// </summary>
    private static IReadOnlyList<string> ConciseList(ScryfallListEntry entry, string name)
    {
        return Concise[name].Read(entry.Card) as IReadOnlyList<string> ?? [];
    }

    /// <summary>
    /// Reads one price column of the printing, or null when the card has none.
    /// </summary>
    private static decimal? Price(ScryfallListEntry entry, string column)
    {
        return entry.Card.Prices.TryGetValue(column, out string? text) ? NumberOrNull(text) : null;
    }

    /// <summary>
    /// Reads a number written as text, or null when the text is missing or is not a plain number, such as an
    /// asterisk power.
    /// </summary>
    private static decimal? NumberOrNull(string? text)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }

    /// <summary>
    /// Lists the formats whose legality status is exactly the given one, sorted by format key. Null when the card
    /// carries no legality data at all.
    /// </summary>
    private static IReadOnlyList<string>? FormatsWithStatus(ScryfallCard card, string status)
    {
        if (card.Legalities.Count == 0)
        {
            return null;
        }

        List<string> formats =
        [
            .. card.Legalities
                .Where(pair => string.Equals(pair.Value, status, StringComparison.Ordinal))
                .Select(pair => pair.Key),
        ];
        formats.Sort(StringComparer.Ordinal);
        return formats;
    }

    /// <summary>
    /// Splits the type lines of every face into the words before the dash (supertypes and card types) and the words
    /// after it (subtypes), and returns the requested part without repeats.
    /// </summary>
    private static List<string> TypeWords(ScryfallCard card, bool wantTypes)
    {
        List<string> words = [];
        foreach (string typeLine in ScryfallCardFaces.TypeLines(card))
        {
            string head = typeLine.Split('—')[0];
            foreach (string word in head.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Supertypes.Contains(word) != wantTypes && !words.Contains(word, StringComparer.Ordinal))
                {
                    words.Add(word);
                }
            }
        }

        return words;
    }

    /// <summary>
    /// Reads the words after the dash on every face's type line, without repeats.
    /// </summary>
    private static List<string> Subtypes(ScryfallCard card)
    {
        List<string> words = [];
        foreach (string typeLine in ScryfallCardFaces.TypeLines(card))
        {
            string[] halves = typeLine.Split('—');
            if (halves.Length < 2)
            {
                continue;
            }

            foreach (string word in halves[1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!words.Contains(word, StringComparer.Ordinal))
                {
                    words.Add(word);
                }
            }
        }

        return words;
    }

}

/// <summary>
/// Collects colored mana symbol counts for one card or one group of cards.
/// </summary>
internal sealed class ManaPips
{
    /// <summary>
    /// Holds the count of each mana color.
    /// </summary>
    private readonly Dictionary<string, long> colors = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets how many hybrid symbols were counted.
    /// </summary>
    internal long Hybrid { get; set; }

    /// <summary>
    /// Gets or sets how many Phyrexian symbols were counted.
    /// </summary>
    internal long Phyrexian { get; set; }

    /// <summary>
    /// Reads the count of one color.
    /// </summary>
    internal long Get(string color)
    {
        return colors.TryGetValue(color, out long count) ? count : 0;
    }

    /// <summary>
    /// Adds to the count of one color.
    /// </summary>
    internal void Add(string color, long count)
    {
        colors[color] = Get(color) + count;
    }

    /// <summary>
    /// Adds every count of another collection, multiplied by a number of copies.
    /// </summary>
    internal void AddScaled(ManaPips other, int copies)
    {
        foreach (string color in ScryfallCardFaces.ManaColors)
        {
            Add(color, other.Get(color) * copies);
        }

        Hybrid += other.Hybrid * copies;
        Phyrexian += other.Phyrexian * copies;
    }
}
