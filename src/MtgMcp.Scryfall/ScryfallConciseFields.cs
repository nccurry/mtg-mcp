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
        [.. ScryfallConciseFieldTable.Definitions.Where(ScryfallConciseFieldTable.InDefaultSet)
            .Select(ScryfallConciseFieldTable.NameOf)];

    /// <summary>
    /// Gets the fields a caller can add by name; they are not in the default set.
    /// </summary>
    public static IReadOnlyList<string> Extra { get; } =
        [.. ScryfallConciseFieldTable.Definitions.Where(field => !ScryfallConciseFieldTable.InDefaultSet(field))
            .Select(ScryfallConciseFieldTable.NameOf)];

    /// <summary>
    /// Gets every field name a caller can ask for: the default set, then the extra fields.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [.. ScryfallConciseFieldTable.Definitions.Select(ScryfallConciseFieldTable.NameOf)];
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
/// Describes a concise field that is read from the card object alone.
/// </summary>
internal sealed record ScryfallPlainField(string Name, bool InDefaultSet, Func<ScryfallCard, object?> Read);

/// <summary>
/// Describes a concise field that is read from installed card data.
/// </summary>
internal sealed record ScryfallInstalledDataField(
    string Name,
    bool InDefaultSet,
    ScryfallInstalledDataNeed Need,
    Func<ScryfallInstalledCardFacts?, object?> Read);

/// <summary>
/// Describes a concise price field, which is written into the shared price object under its own key.
/// </summary>
internal sealed record ScryfallPriceField(string Name, bool InDefaultSet, string SourceKey, string OutputKey);

/// <summary>
/// Holds one concise field of one of the three kinds.
/// </summary>
internal readonly union ScryfallConciseField(ScryfallPlainField, ScryfallInstalledDataField, ScryfallPriceField);

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
    internal static IReadOnlyList<ScryfallConciseField> Definitions { get; } =
    [
        Plain("scryfallId", true, card => card.Id),
        Plain("oracleId", true, card => EffectiveOracleId(card)),
        Plain("name", true, card => card.Name),
        Plain("manaCost", true, card => ManaCost(card)),
        Plain("manaValue", true, card => card.ManaValue),
        Plain("typeLine", true, card => card.TypeLine ?? JoinFaces(card, face => face.TypeLine, " // ")),
        Plain("colors", true, card => Colors(card)),
        Plain("colorIdentity", true, card => card.ColorIdentity),
        Plain("producedMana", true, card => card.ProducedMana),
        Plain("keywords", true, card => card.Keywords),
        Plain("oracleText", true, card => OracleText(card)),
        Plain("power", true, card => card.Power ?? Front(card)?.Power),
        Plain("toughness", true, card => card.Toughness ?? Front(card)?.Toughness),
        Plain("loyalty", true, card => card.Loyalty ?? Front(card)?.Loyalty),
        Plain("legalities", true, card => LegalFormats(card)),
        Price("prices.usd", true, "usd", "usd"),
        Installed("cheapestPriceUsd", true, ScryfallInstalledDataNeed.CheapestPrice, facts => facts?.CheapestPriceUsd),
        Plain("rarity", true, card => card.Rarity),
        Plain("set", true, card => card.SetCode),
        Installed("tags", true, ScryfallInstalledDataNeed.Tags, facts => facts?.Tags),
        Plain("gameChanger", true, card => card.GameChanger),
        Plain("edhrecRank", true, card => EdhrecRank(card)),
        Price("prices.usdFoil", false, "usd_foil", "usdFoil"),
        Price("prices.usdEtched", false, "usd_etched", "usdEtched"),
        Price("prices.eur", false, "eur", "eur"),
        Price("prices.eurFoil", false, "eur_foil", "eurFoil"),
        Price("prices.tix", false, "tix", "tix"),
        Plain("legalitiesAll", false, card => card.Legalities),
        Plain("games", false, card => card.Games),
        Plain("reserved", false, card => card.Reserved),
        Plain("layout", false, card => card.Layout),
    ];

    /// <summary>
    /// Gets the name of a concise field.
    /// </summary>
    internal static string NameOf(ScryfallConciseField field)
    {
        return field switch
        {
            ScryfallPlainField plain => plain.Name,
            ScryfallInstalledDataField installed => installed.Name,
            ScryfallPriceField price => price.Name,
        };
    }

    /// <summary>
    /// Reports whether the default concise set holds a field.
    /// </summary>
    internal static bool InDefaultSet(ScryfallConciseField field)
    {
        return field switch
        {
            ScryfallPlainField plain => plain.InDefaultSet,
            ScryfallInstalledDataField installed => installed.InDefaultSet,
            ScryfallPriceField price => price.InDefaultSet,
        };
    }

    /// <summary>
    /// Gets the installed card data a concise field needs.
    /// </summary>
    internal static ScryfallInstalledDataNeed NeedOf(ScryfallConciseField field)
    {
        return field switch
        {
            ScryfallPlainField => ScryfallInstalledDataNeed.None,
            ScryfallInstalledDataField installed => installed.Need,
            ScryfallPriceField => ScryfallInstalledDataNeed.None,
        };
    }

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
    private static ScryfallConciseField Plain(string name, bool inDefaultSet, Func<ScryfallCard, object?> read)
    {
        return new ScryfallPlainField(name, inDefaultSet, read);
    }

    /// <summary>
    /// Defines a field read from installed card data.
    /// </summary>
    private static ScryfallConciseField Installed(
        string name,
        bool inDefaultSet,
        ScryfallInstalledDataNeed need,
        Func<ScryfallInstalledCardFacts?, object?> read)
    {
        return new ScryfallInstalledDataField(name, inDefaultSet, need, read);
    }

    /// <summary>
    /// Defines a price field that is written into the shared price object.
    /// </summary>
    private static ScryfallConciseField Price(string name, bool inDefaultSet, string sourceKey, string outputKey)
    {
        return new ScryfallPriceField(name, inDefaultSet, sourceKey, outputKey);
    }

    /// <summary>
    /// Reads the mana cost, or the front face's when a multi-face card has none of its own.
    /// </summary>
    private static string? ManaCost(ScryfallCard card)
    {
        if (!string.IsNullOrEmpty(card.ManaCost))
        {
            return card.ManaCost;
        }

        return !string.IsNullOrEmpty(Front(card)?.ManaCost) ? Front(card)!.ManaCost : card.ManaCost;
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
