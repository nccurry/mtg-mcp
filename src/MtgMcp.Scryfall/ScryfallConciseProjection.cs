
namespace MtgMcp.Scryfall;

/// <summary>
/// Builds the concise card shape from a mapped card and the facts read from installed card data.
/// </summary>
internal static class ScryfallConciseProjection
{
    /// <summary>
    /// Separates the text of each face when a card's own oracle text is absent.
    /// </summary>
    private const string FaceTextSeparator = "\n//\n";

    /// <summary>
    /// Maps each price field name offered by concise cards to its key in the Scryfall price object.
    /// </summary>
    private static readonly (string Field, string OutputKey, string SourceKey)[] PriceFields =
    [
        ("prices.usd", "usd", "usd"),
        ("prices.usdFoil", "usdFoil", "usd_foil"),
        ("prices.usdEtched", "usdEtched", "usd_etched"),
        ("prices.eur", "eur", "eur"),
        ("prices.eurFoil", "eurFoil", "eur_foil"),
        ("prices.tix", "tix", "tix"),
    ];

    /// <summary>
    /// Projects one card to the selected concise fields in their fixed order.
    /// </summary>
    /// <param name="card">The mapped card.</param>
    /// <param name="extras">
    /// The tag slugs and cheapest price read from installed card data, or null when they are not available.
    /// </param>
    /// <param name="selected">The field names to write.</param>
    internal static ScryfallConciseCard Project(
        ScryfallCard card,
        ScryfallCardExtras? extras,
        IReadOnlySet<string> selected)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(selected);
        List<KeyValuePair<string, object?>> fields = [];
        Dictionary<string, string?>? prices = null;
        foreach (string name in ScryfallConciseFields.All)
        {
            if (!selected.Contains(name))
            {
                continue;
            }

            if (name.StartsWith("prices.", StringComparison.Ordinal))
            {
                if (prices is null)
                {
                    prices = new Dictionary<string, string?>(StringComparer.Ordinal);
                    fields.Add(new KeyValuePair<string, object?>("prices", prices));
                }

                AddPrice(card, name, prices);
                continue;
            }

            fields.Add(new KeyValuePair<string, object?>(name, ReadField(card, extras, name)));
        }

        return new ScryfallConciseCard(fields);
    }

    /// <summary>
    /// Reads the value of one non-price concise field.
    /// </summary>
    private static object? ReadField(ScryfallCard card, ScryfallCardExtras? extras, string name)
    {
        ScryfallCardFace? front = card.Faces.Count > 0 ? card.Faces[0] : null;
        return name switch
        {
            "scryfallId" => card.Id,
            "oracleId" => card.OracleId,
            "name" => card.Name,
            "manaCost" => FirstNonEmpty(card.ManaCost, front?.ManaCost) ?? card.ManaCost,
            "manaValue" => card.ManaValue,
            "typeLine" => card.TypeLine ?? JoinFaces(card.Faces, face => face.TypeLine, " // "),
            "colors" => card.Colors.Count == 0 && front is not null ? front.Colors : card.Colors,
            "colorIdentity" => card.ColorIdentity,
            "producedMana" => card.ProducedMana,
            "keywords" => card.Keywords,
            "oracleText" => card.OracleText ?? JoinFaces(card.Faces, face => face.OracleText, FaceTextSeparator),
            "power" => card.Power ?? front?.Power,
            "toughness" => card.Toughness ?? front?.Toughness,
            "loyalty" => card.Loyalty ?? front?.Loyalty,
            "legalities" => LegalFormats(card),
            "legalitiesAll" => card.Legalities,
            "cheapestPriceUsd" => extras?.CheapestPriceUsd,
            "rarity" => card.Rarity,
            "set" => card.SetCode,
            "tags" => extras?.Tags,
            "gameChanger" => card.GameChanger,
            "edhrecRank" => EdhrecRank(card),
            "games" => card.Games,
            "reserved" => card.Reserved,
            "layout" => card.Layout,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown concise field."),
        };
    }

    /// <summary>
    /// Adds one price field, keeping a known-null price as an explicit null.
    /// </summary>
    private static void AddPrice(ScryfallCard card, string fieldName, Dictionary<string, string?> prices)
    {
        foreach ((string field, string outputKey, string sourceKey) in PriceFields)
        {
            if (!string.Equals(field, fieldName, StringComparison.Ordinal))
            {
                continue;
            }

            prices[outputKey] = card.Prices.TryGetValue(sourceKey, out string? amount) ? amount : null;
            return;
        }
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
        IReadOnlyList<ScryfallCardFace> faces,
        Func<ScryfallCardFace, string?> select,
        string separator)
    {
        List<string> parts = [];
        foreach (ScryfallCardFace face in faces)
        {
            if (select(face) is { Length: > 0 } text)
            {
                parts.Add(text);
            }
        }

        return parts.Count == 0 ? null : string.Join(separator, parts);
    }
}
