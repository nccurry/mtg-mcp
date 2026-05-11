using System.Text.Json;
using System.Text.RegularExpressions;
using MtgMcp.Core;

namespace MtgMcp.Archidekt;

/// <summary>
/// Coordinates archidekt gateway HTTP operations.
/// </summary>
public sealed partial class ArchidektGateway
{
    /// <summary>
    /// Parses the categories.
    /// </summary>
    private static List<DeckCategory> ParseCategories(JsonElement root)
    {
        List<DeckCategory> categories = [];
        if (
            root.TryGetProperty("categories", out JsonElement categoryArray)
            && categoryArray.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement item in categoryArray.EnumerateArray())
            {
                string? name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                categories.Add(
                    new DeckCategory
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = name,
                        IncludedInDeck = GetBool(item, "includedInDeck", defaultValue: true),
                        IncludedInPrice = GetBool(item, "includedInPrice", defaultValue: true),
                        ArchidektCategoryId = GetInt(item, "id"),
                    }
                );
            }
        }

        return categories.Count == 0 ? DeckDefaults.CreateDefaultCategories() : categories;
    }

    /// <summary>
    /// Parses the cards.
    /// </summary>
    private static List<DeckCard> ParseCards(
        JsonElement root,
        IReadOnlyList<DeckCategory> categories
    )
    {
        if (
            !root.TryGetProperty("cards", out JsonElement cardArray)
            || cardArray.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<DeckCard> cards = [];
        foreach (JsonElement relation in cardArray.EnumerateArray())
        {
            // Archidekt responses may put card facts on the relation itself or
            // inside a nested "card" object, so mapping always checks both.
            JsonElement cardElement = relation.TryGetProperty("card", out JsonElement nestedCard)
                ? nestedCard
                : relation;
            string name =
                GetNestedString(cardElement, "oracleCard", "name")
                ?? GetString(cardElement, "name")
                ?? GetString(relation, "name")
                ?? "Unknown Card";
            List<string> parsedCategories = ParseCardCategories(relation, categories);
            List<string> categoryNames = DeckCategoryOrdering.OrderedDistinct(
                parsedCategories.FirstOrDefault() ?? DeckDefaults.Mainboard,
                parsedCategories);
            string primaryCategory = categoryNames[0];

            DeckCard card = new()
            {
                Name = name,
                Quantity = GetInt(relation, "quantity") ?? 1,
                PrimaryCategory = primaryCategory,
                Categories = categoryNames.Count == 0 ? [primaryCategory] : categoryNames,
                ScryfallId = GetString(cardElement, "uid"),
                ScryfallOracleId = GetNestedString(cardElement, "oracleCard", "uid"),
                ArchidektCardId = GetString(cardElement, "id"),
                ArchidektDeckRelationId = GetDeckRelationId(relation),
                Modifier = GetString(relation, "modifier"),
                Companion = GetBool(relation, "companion", defaultValue: false),
                FlippedDefault = GetBool(relation, "flippedDefault", defaultValue: false),
                Snapshot = CreateCardSnapshot(cardElement),
            };

            cards.Add(card);
        }

        return cards;
    }

    /// <summary>
    /// Reads Archidekt's deck-card relation id from observed response variants.
    /// </summary>
    private static long? GetDeckRelationId(JsonElement relation)
    {
        return GetLong(relation, "id")
            ?? GetLong(relation, "deckRelationId")
            ?? GetLong(relation, "deckRelationID")
            ?? GetLong(relation, "deck_relation_id")
            ?? GetNestedLong(relation, "deckRelation", "id")
            ?? GetNestedLong(relation, "deckCard", "id");
    }

    /// <summary>
    /// Creates the card snapshot.
    /// </summary>
    private static CardSnapshot CreateCardSnapshot(JsonElement cardElement)
    {
        // Archidekt has used both top-level and nested oracle fields over time;
        // keep the mapper tolerant so cached workspaces survive response drift.
        return new CardSnapshot
        {
            ManaCost = FirstNonEmpty(
                GetString(cardElement, "manaCost"),
                GetString(cardElement, "mana_cost"),
                GetNestedString(cardElement, "oracleCard", "manaCost"),
                GetNestedString(cardElement, "oracleCard", "mana_cost"),
                GetNestedFaceString(cardElement, "manaCost"),
                GetNestedFaceString(cardElement, "mana_cost")),
            TypeLine = FirstNonEmpty(
                GetNestedString(cardElement, "oracleCard", "typeLine"),
                GetNestedString(cardElement, "oracleCard", "type"),
                BuildNestedTypeLine(cardElement)),
            ManaValue =
                GetDouble(cardElement, "manaValue")
                ?? GetDouble(cardElement, "cmc")
                ?? GetNestedDouble(cardElement, "oracleCard", "manaValue")
                ?? GetNestedDouble(cardElement, "oracleCard", "cmc"),
            OracleText = FirstNonEmpty(
                GetString(cardElement, "oracleText"),
                GetString(cardElement, "oracle_text"),
                GetNestedString(cardElement, "oracleCard", "oracleText"),
                GetNestedString(cardElement, "oracleCard", "oracle_text"),
                GetNestedString(cardElement, "oracleCard", "text"),
                GetNestedFaceText(cardElement)),
            ColorIdentity = ParseColorIdentity(cardElement),
            Set = FirstNonEmpty(
                GetNestedString(cardElement, "edition", "editioncode"),
                GetNestedString(cardElement, "edition", "code"),
                GetString(cardElement, "set"),
                GetString(cardElement, "setCode"),
                GetStringValue(cardElement, "edition")),
            CollectorNumber =
                GetString(cardElement, "collectorNumber")
                ?? GetString(cardElement, "collector_number"),
            Rarity = GetString(cardElement, "rarity"),
            EdhrecRank = GetInt(cardElement, "edhrecRank")
                ?? GetInt(cardElement, "edhrec_rank")
                ?? GetNestedInt(cardElement, "oracleCard", "edhrecRank")
                ?? GetNestedInt(cardElement, "oracleCard", "edhrec_rank"),
            ScryfallUri =
                GetString(cardElement, "scryfallUri") ?? GetString(cardElement, "scryfall_uri"),
            Prices = ParsePrices(cardElement),
        };
    }

    /// <summary>
    /// Gets a text field from the first Archidekt face that defines it.
    /// </summary>
    private static string? GetNestedFaceString(JsonElement cardElement, string propertyName)
    {
        if (
            !cardElement.TryGetProperty("oracleCard", out JsonElement oracleCard)
            || oracleCard.ValueKind != JsonValueKind.Object
            || !oracleCard.TryGetProperty("faces", out JsonElement faces)
            || faces.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        foreach (JsonElement face in faces.EnumerateArray())
        {
            string? value = GetString(face, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Combines Archidekt face rules text when no flat oracle text is present.
    /// </summary>
    private static string? GetNestedFaceText(JsonElement cardElement)
    {
        if (
            !cardElement.TryGetProperty("oracleCard", out JsonElement oracleCard)
            || oracleCard.ValueKind != JsonValueKind.Object
            || !oracleCard.TryGetProperty("faces", out JsonElement faces)
            || faces.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        List<string> values = [];
        foreach (JsonElement face in faces.EnumerateArray())
        {
            string? value = FirstNonEmpty(
                GetString(face, "oracleText"),
                GetString(face, "oracle_text"),
                GetString(face, "text"));
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : string.Join("\n\n", values);
    }

    /// <summary>
    /// Builds a Scryfall-style type line from Archidekt's split type fields.
    /// </summary>
    private static string? BuildNestedTypeLine(JsonElement cardElement)
    {
        if (
            !cardElement.TryGetProperty("oracleCard", out JsonElement oracleCard)
            || oracleCard.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }

        JsonElement source = oracleCard;
        if (
            oracleCard.TryGetProperty("faces", out JsonElement faces)
            && faces.ValueKind == JsonValueKind.Array
        )
        {
            source = faces.EnumerateArray().FirstOrDefault();
            if (source.ValueKind == JsonValueKind.Undefined)
            {
                source = oracleCard;
            }
        }

        List<string> supertypes = ReadStringList(source, "superTypes");
        List<string> types = ReadStringList(source, "types");
        List<string> subtypes = ReadStringList(source, "subTypes");
        string beforeDash = string.Join(' ', supertypes.Concat(types));
        string afterDash = string.Join(' ', subtypes);

        return string.IsNullOrWhiteSpace(afterDash)
            ? FirstNonEmpty(beforeDash)
            : $"{beforeDash} - {afterDash}";
    }

    /// <summary>
    /// Reads Archidekt array-or-string fields into tokens.
    /// </summary>
    private static List<string> ReadStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            return property
                .EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property
                .GetString()
                ?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList() ?? [];
        }

        return [];
    }

    /// <summary>
    /// Reads a direct string value without converting object-valued fields to raw JSON.
    /// </summary>
    private static string? GetStringValue(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    /// <summary>
    /// Parses Archidekt's numeric price map into the shared USD snapshot key.
    /// </summary>
    private static Dictionary<string, string> ParsePrices(JsonElement cardElement)
    {
        Dictionary<string, string> prices = new(StringComparer.OrdinalIgnoreCase);
        if (
            !cardElement.TryGetProperty("prices", out JsonElement priceObject)
            || priceObject.ValueKind != JsonValueKind.Object
        )
        {
            return prices;
        }

        string? usd = GetString(priceObject, "usd")
            ?? GetString(priceObject, "tcg")
            ?? GetString(priceObject, "ck")
            ?? GetString(priceObject, "mp")
            ?? GetString(priceObject, "cardTrader");
        if (!string.IsNullOrWhiteSpace(usd))
        {
            prices["usd"] = usd;
        }

        return prices;
    }

    /// <summary>
    /// Parses the card categories.
    /// </summary>
    private static List<string> ParseCardCategories(
        JsonElement relation,
        IReadOnlyList<DeckCategory> categories
    )
    {
        if (
            !relation.TryGetProperty("categories", out JsonElement categoryArray)
            || categoryArray.ValueKind != JsonValueKind.Array
        )
        {
            return [DeckDefaults.Mainboard];
        }

        List<string> names = [];
        foreach (JsonElement item in categoryArray.EnumerateArray())
        {
            string? name = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Number => ResolveCategoryName(item.GetInt32(), categories),
                JsonValueKind.Object => GetString(item, "name")
                    ?? ResolveCategoryName(GetInt(item, "id"), categories),
                _ => null,
            };

            if (
                !string.IsNullOrWhiteSpace(name)
                && !names.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase))
            )
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? [DeckDefaults.Mainboard] : names;
    }

    /// <summary>
    /// Resolves the category name.
    /// </summary>
    private static string? ResolveCategoryName(
        int? categoryId,
        IReadOnlyList<DeckCategory> categories
    )
    {
        if (!categoryId.HasValue)
        {
            return null;
        }

        return categories
            .FirstOrDefault(category => category.ArchidektCategoryId == categoryId.Value)
            ?.Name;
    }

    /// <summary>
    /// Parses the checkpoint.
    /// </summary>
    private static DeckCheckpoint ParseCheckpoint(JsonElement element, string deckId)
    {
        return new DeckCheckpoint
        {
            Id = GetString(element, "id") ?? "",
            DeckId = deckId,
            Name = GetString(element, "name") ?? "Unnamed checkpoint",
            Description = GetString(element, "description"),
            CreatedAt = TryDate(
                GetString(element, "createdAt") ?? GetString(element, "created_at")
            ),
        };
    }

    /// <summary>
    /// Gets the deck format id.
    /// </summary>
    private static int? GetDeckFormatId(JsonElement element)
    {
        return GetInt(element, "deckFormat")
            ?? GetNestedInt(element, "deckFormat", "id")
            ?? GetNestedInt(element, "deckFormat", "pk");
    }

    /// <summary>
    /// Extracts the deck id.
    /// </summary>
    private static string ExtractDeckId(string deckIdOrUrl)
    {
        Match urlMatch = DeckUrlIdRegex().Match(deckIdOrUrl);
        if (urlMatch.Success)
        {
            return urlMatch.Groups["id"].Value;
        }

        Match match = DeckIdRegex().Match(deckIdOrUrl);
        if (!match.Success)
        {
            throw new ArgumentException(
                "Archidekt deck id or URL did not contain a deck id.",
                nameof(deckIdOrUrl)
            );
        }

        return match.Groups["id"].Value;
    }

    /// <summary>
    /// Normalizes an Archidekt deck format value to Scryfall-compatible text.
    /// </summary>
    private static string NormalizeDeckFormat(string? format)
    {
        string normalized = format?.Trim().ToLowerInvariant() ?? "";
        return normalized switch
        {
            "" => "commander",
            "3" => "commander",
            "edh" => "commander",
            _ => normalized,
        };
    }

    /// <summary>
    /// Parses the color identity.
    /// </summary>
    private static List<string> ParseColorIdentity(JsonElement cardElement)
    {
        List<string> colors = [];
        AddColors(colors, cardElement, "colorIdentity");
        AddColors(colors, cardElement, "color_identity");
        if (
            cardElement.TryGetProperty("oracleCard", out JsonElement oracleCard)
            && oracleCard.ValueKind == JsonValueKind.Object
        )
        {
            AddColors(colors, oracleCard, "colorIdentity");
            AddColors(colors, oracleCard, "color_identity");
        }

        return colors;
    }

    /// <summary>
    /// Adds the colors.
    /// </summary>
    private static void AddColors(List<string> colors, JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            string? value = property.GetString();
            foreach (
                string color in value?.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                ) ?? []
            )
            {
                AddColor(colors, color);
            }
        }

        if (property.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    AddColor(colors, item.GetString());
                }
            }
        }
    }

    /// <summary>
    /// Adds the color.
    /// </summary>
    private static void AddColor(List<string> colors, string? color)
    {
        color = NormalizeColor(color);
        if (
            !string.IsNullOrWhiteSpace(color)
            && !colors.Any(value => value.Equals(color, StringComparison.OrdinalIgnoreCase))
        )
        {
            colors.Add(color);
        }
    }

    /// <summary>
    /// Normalizes Archidekt color names to Scryfall color letters.
    /// </summary>
    private static string? NormalizeColor(string? color)
    {
        return color?.Trim().ToLowerInvariant() switch
        {
            "white" => "W",
            "blue" => "U",
            "black" => "B",
            "red" => "R",
            "green" => "G",
            "colorless" => "C",
            "" or null => null,
            _ => color.Trim(),
        };
    }

    /// <summary>
    /// Handles deck id regex.
    /// </summary>
    [GeneratedRegex(@"(?<id>\d+)(?:/)?$", RegexOptions.CultureInvariant)]
    private static partial Regex DeckIdRegex();

    /// <summary>
    /// Handles deck url id regex.
    /// </summary>
    [GeneratedRegex(
        @"(?:^|/)decks/(?<id>\d+)(?:/|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DeckUrlIdRegex();
}
