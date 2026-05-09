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
            List<string> categoryNames = ParseCardCategories(relation, categories);
            string primaryCategory = categoryNames.FirstOrDefault() ?? DeckDefaults.Mainboard;

            DeckCard card = new()
            {
                Name = name,
                Quantity = GetInt(relation, "quantity") ?? 1,
                PrimaryCategory = primaryCategory,
                Categories = categoryNames.Count == 0 ? [primaryCategory] : categoryNames,
                ScryfallId = GetString(cardElement, "uid"),
                ScryfallOracleId = GetNestedString(cardElement, "oracleCard", "uid"),
                ArchidektCardId = GetString(cardElement, "id"),
                ArchidektDeckRelationId = GetInt(relation, "id"),
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
    /// Creates the card snapshot.
    /// </summary>
    private static CardSnapshot CreateCardSnapshot(JsonElement cardElement)
    {
        // Archidekt has used both top-level and nested oracle fields over time;
        // keep the mapper tolerant so cached workspaces survive response drift.
        return new CardSnapshot
        {
            ManaCost = GetString(cardElement, "manaCost")
                ?? GetString(cardElement, "mana_cost")
                ?? GetNestedString(cardElement, "oracleCard", "manaCost")
                ?? GetNestedString(cardElement, "oracleCard", "mana_cost"),
            TypeLine =
                GetNestedString(cardElement, "oracleCard", "typeLine")
                ?? GetNestedString(cardElement, "oracleCard", "type"),
            ManaValue =
                GetDouble(cardElement, "manaValue")
                ?? GetDouble(cardElement, "cmc")
                ?? GetNestedDouble(cardElement, "oracleCard", "manaValue")
                ?? GetNestedDouble(cardElement, "oracleCard", "cmc"),
            OracleText = GetString(cardElement, "oracleText")
                ?? GetString(cardElement, "oracle_text")
                ?? GetNestedString(cardElement, "oracleCard", "oracleText")
                ?? GetNestedString(cardElement, "oracleCard", "oracle_text"),
            ColorIdentity = ParseColorIdentity(cardElement),
            Set =
                GetString(cardElement, "edition")
                ?? GetString(cardElement, "set")
                ?? GetString(cardElement, "setCode"),
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
        };
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
        if (
            !string.IsNullOrWhiteSpace(color)
            && !colors.Any(value => value.Equals(color, StringComparison.OrdinalIgnoreCase))
        )
        {
            colors.Add(color);
        }
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
