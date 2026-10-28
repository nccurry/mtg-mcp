namespace MtgMcp.Scryfall;

/// <summary>
/// Lists the field names a concise card result can carry.
/// </summary>
public static class ScryfallConciseFields
{
    /// <summary>
    /// Gets the fields every concise card carries, even when the caller narrows the field list.
    /// </summary>
    public static IReadOnlyList<string> Always { get; } = ["scryfallId", "oracleId", "name"];

    /// <summary>
    /// Gets the fields a concise card carries when the caller names none, in output order.
    /// </summary>
    public static IReadOnlyList<string> Default { get; } =
        [.. ScryfallConciseFieldTable.Definitions.Where(field => field.InDefaultSet).Select(field => field.Name)];

    /// <summary>
    /// Gets the fields a caller can add by name; they are not in the default set.
    /// </summary>
    public static IReadOnlyList<string> Extra { get; } =
        [.. ScryfallConciseFieldTable.Definitions.Where(field => !field.InDefaultSet).Select(field => field.Name)];

    /// <summary>
    /// Gets every field name a caller can ask for: the default set, then the extra fields.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [.. ScryfallConciseFieldTable.Definitions.Select(field => field.Name)];
}

/// <summary>
/// Names the installed card data a concise field needs beyond the card object itself.
/// </summary>
internal enum ScryfallInstalledDataNeed
{
    /// <summary>
    /// The field is read from the card object alone.
    /// </summary>
    None,

    /// <summary>
    /// The field is the card's oracle tag slugs.
    /// </summary>
    Tags,

    /// <summary>
    /// The field is the lowest USD price across installed printings.
    /// </summary>
    CheapestPrice,
}

/// <summary>
/// Describes one concise field: its name, whether the default set holds it, the installed data it needs, and how
/// to read it. A price field instead names its key in the Scryfall price object and in the output price object.
/// </summary>
internal sealed record ScryfallConciseFieldDefinition(
    string Name,
    bool InDefaultSet,
    ScryfallInstalledDataNeed Need,
    Func<ScryfallCard, ScryfallInstalledCardFacts?, object?> Read,
    string? PriceSourceKey = null,
    string? PriceOutputKey = null);

/// <summary>
/// Holds the one table that drives the published field list, the projection, and which fields need installed data.
/// The table is in output order: the default set first, then the extra fields.
/// </summary>
internal static class ScryfallConciseFieldTable
{
    /// <summary>
    /// Separates the text of each face when a card has no oracle text of its own.
    /// </summary>
    private const string FaceTextSeparator = "\n//\n";

    /// <summary>
    /// Gets every concise field in output order.
    /// </summary>
    internal static IReadOnlyList<ScryfallConciseFieldDefinition> Definitions { get; } =
    [
        Plain("scryfallId", true, (card, _) => card.Id),
        Plain("oracleId", true, (card, _) => EffectiveOracleId(card)),
        Plain("name", true, (card, _) => card.Name),
        Plain("manaCost", true, (card, _) => FirstNonEmpty(card.ManaCost, Front(card)?.ManaCost) ?? card.ManaCost),
        Plain("manaValue", true, (card, _) => card.ManaValue),
        Plain("typeLine", true, (card, _) => card.TypeLine ?? JoinFaces(card, face => face.TypeLine, " // ")),
        Plain("colors", true, (card, _) => Colors(card)),
        Plain("colorIdentity", true, (card, _) => card.ColorIdentity),
        Plain("producedMana", true, (card, _) => card.ProducedMana),
        Plain("keywords", true, (card, _) => card.Keywords),
        Plain("oracleText", true, (card, _) => OracleText(card)),
        Plain("power", true, (card, _) => card.Power ?? Front(card)?.Power),
        Plain("toughness", true, (card, _) => card.Toughness ?? Front(card)?.Toughness),
        Plain("loyalty", true, (card, _) => card.Loyalty ?? Front(card)?.Loyalty),
        Plain("legalities", true, (card, _) => LegalFormats(card)),
        Price("prices.usd", true, "usd", "usd"),
        Installed("cheapestPriceUsd", true, ScryfallInstalledDataNeed.CheapestPrice, facts => facts?.CheapestPriceUsd),
        Plain("rarity", true, (card, _) => card.Rarity),
        Plain("set", true, (card, _) => card.SetCode),
        Installed("tags", true, ScryfallInstalledDataNeed.Tags, facts => facts?.Tags),
        Plain("gameChanger", true, (card, _) => card.GameChanger),
        Plain("edhrecRank", true, (card, _) => EdhrecRank(card)),
        Price("prices.usdFoil", false, "usd_foil", "usdFoil"),
        Price("prices.usdEtched", false, "usd_etched", "usdEtched"),
        Price("prices.eur", false, "eur", "eur"),
        Price("prices.eurFoil", false, "eur_foil", "eurFoil"),
        Price("prices.tix", false, "tix", "tix"),
        Plain("legalitiesAll", false, (card, _) => card.Legalities),
        Plain("games", false, (card, _) => card.Games),
        Plain("reserved", false, (card, _) => card.Reserved),
        Plain("layout", false, (card, _) => card.Layout),
    ];

    /// <summary>
    /// Finds the Oracle ID a card is looked up by. A card whose faces are separate cards (a reversible card) has no
    /// Oracle ID of its own, so its front face's ID stands in for it.
    /// </summary>
    internal static Guid? EffectiveOracleId(ScryfallCard card)
    {
        return card.OracleId ?? Front(card)?.OracleId;
    }

    /// <summary>
    /// Reports whether a card has no Oracle ID of its own but its front face does.
    /// </summary>
    internal static bool UsesFrontFaceOracleId(ScryfallCard card)
    {
        return card.OracleId is null && Front(card)?.OracleId is not null;
    }

    /// <summary>
    /// Defines a field read from the card object.
    /// </summary>
    private static ScryfallConciseFieldDefinition Plain(
        string name,
        bool inDefaultSet,
        Func<ScryfallCard, ScryfallInstalledCardFacts?, object?> read)
    {
        return new ScryfallConciseFieldDefinition(name, inDefaultSet, ScryfallInstalledDataNeed.None, read);
    }

    /// <summary>
    /// Defines a field read from installed card data.
    /// </summary>
    private static ScryfallConciseFieldDefinition Installed(
        string name,
        bool inDefaultSet,
        ScryfallInstalledDataNeed need,
        Func<ScryfallInstalledCardFacts?, object?> read)
    {
        return new ScryfallConciseFieldDefinition(name, inDefaultSet, need, (_, facts) => read(facts));
    }

    /// <summary>
    /// Defines a price field that is written into the shared price object.
    /// </summary>
    private static ScryfallConciseFieldDefinition Price(
        string name,
        bool inDefaultSet,
        string sourceKey,
        string outputKey)
    {
        return new ScryfallConciseFieldDefinition(
            name,
            inDefaultSet,
            ScryfallInstalledDataNeed.None,
            (_, _) => null,
            sourceKey,
            outputKey);
    }

    /// <summary>
    /// Reads the card colors, or the front face colors when a multi-face card has none of its own.
    /// </summary>
    private static IReadOnlyList<string> Colors(ScryfallCard card)
    {
        return card.Colors.Count == 0 && Front(card) is { } face ? face.Colors : card.Colors;
    }

    /// <summary>
    /// Reads the card oracle text, or each face's text joined when the card has none of its own.
    /// </summary>
    private static string? OracleText(ScryfallCard card)
    {
        return card.OracleText ?? JoinFaces(card, face => face.OracleText, FaceTextSeparator);
    }

    /// <summary>
    /// Gets the front face of a multi-face card, or null for a single-face card.
    /// </summary>
    private static ScryfallCardFace? Front(ScryfallCard card)
    {
        return card.Faces.Count > 0 ? card.Faces[0] : null;
    }

    /// <summary>
    /// Lists the formats whose status is exactly legal, in the order Scryfall gave them.
    /// </summary>
    private static List<string> LegalFormats(ScryfallCard card)
    {
        List<string> formats = [];
        foreach (KeyValuePair<string, string> legality in card.Legalities)
        {
            if (string.Equals(legality.Value, "legal", StringComparison.Ordinal))
            {
                formats.Add(legality.Key);
            }
        }

        return formats;
    }

    /// <summary>
    /// Reads the EDHREC deck-inclusion rank when Scryfall supplied one.
    /// </summary>
    private static long? EdhrecRank(ScryfallCard card)
    {
        foreach (ScryfallRankEvidence rank in card.RankEvidence)
        {
            if (string.Equals(rank.Field, "edhrec_rank", StringComparison.Ordinal))
            {
                return rank.Rank;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first value that is neither null nor empty.
    /// </summary>
    private static string? FirstNonEmpty(string? first, string? second)
    {
        return !string.IsNullOrEmpty(first) ? first : !string.IsNullOrEmpty(second) ? second : null;
    }

    /// <summary>
    /// Joins one text from each face, front face first, skipping faces that have none; null when no face has any.
    /// </summary>
    private static string? JoinFaces(
        ScryfallCard card,
        Func<ScryfallCardFace, string?> select,
        string separator)
    {
        List<string> parts = [];
        foreach (ScryfallCardFace face in card.Faces)
        {
            if (select(face) is { Length: > 0 } text)
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join(separator, parts);
    }
}
