using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Contains Scryfall JSON mapping and card-name matching helpers.
/// </summary>
public sealed partial class ScryfallClient
{
    /// <summary>
    /// Maps one Scryfall card object into compact search result metadata.
    /// </summary>
    private static CardSearchResult MapSearchResult(JsonElement element)
    {
        return new CardSearchResult
        {
            Id = GetString(element, "id") ?? "",
            Name = GetString(element, "name") ?? "",
            ManaCost = GetString(element, "mana_cost") ?? GetFaceString(element, "mana_cost"),
            TypeLine = GetString(element, "type_line") ?? GetFaceString(element, "type_line"),
            Set = GetString(element, "set"),
            CollectorNumber = GetString(element, "collector_number"),
            ReleasedAt = GetDateOnly(element, "released_at"),
            ScryfallUri = GetString(element, "scryfall_uri"),
        };
    }

    /// <summary>
    /// Maps one Scryfall card object into provider-neutral card metadata.
    /// </summary>
    private static CardInfo MapCard(JsonElement element)
    {
        CardInfo card = new()
        {
            Id = GetString(element, "id") ?? "",
            OracleId = GetString(element, "oracle_id"),
            Name = GetString(element, "name") ?? "",
            ManaCost = GetString(element, "mana_cost") ?? GetFaceString(element, "mana_cost"),
            Layout = GetString(element, "layout"),
            ManaValue = GetDouble(element, "cmc"),
            TypeLine = GetString(element, "type_line") ?? GetFaceString(element, "type_line"),
            OracleText = GetString(element, "oracle_text") ?? GetFaceText(element, "oracle_text"),
            Power = GetString(element, "power"),
            Toughness = GetString(element, "toughness"),
            Loyalty = GetString(element, "loyalty"),
            Defense = GetString(element, "defense"),
            Set = GetString(element, "set"),
            CollectorNumber = GetString(element, "collector_number"),
            Rarity = GetString(element, "rarity"),
            Language = GetString(element, "lang"),
            ReleasedAt = GetDateOnly(element, "released_at"),
            ScryfallUri = GetString(element, "scryfall_uri"),
            EdhrecRank = GetInt(element, "edhrec_rank"),
        };

        AddStringArray(element, "colors", card.Colors);
        AddStringArray(element, "color_identity", card.ColorIdentity);
        AddStringArray(element, "keywords", card.Keywords);
        AddStringArray(element, "produced_mana", card.ProducedMana);
        AddStringArray(element, "games", card.Games);
        AddStringArray(element, "finishes", card.Finishes);
        AddStringDictionary(element, "legalities", card.Legalities);
        AddStringDictionary(element, "prices", card.Prices);
        AddStringDictionary(element, "image_uris", card.ImageUris);
        AddFaces(element, card.Faces);

        if (
            card.ImageUris.Count == 0
            && element.TryGetProperty("card_faces", out JsonElement faces)
        )
        {
            foreach (JsonElement face in faces.EnumerateArray())
            {
                AddStringDictionary(face, "image_uris", card.ImageUris);
                if (card.ImageUris.Count > 0)
                {
                    break;
                }
            }
        }

        return card;
    }

    /// <summary>
    /// Adds structured card-face data when Scryfall exposes it.
    /// </summary>
    private static void AddFaces(JsonElement element, List<CardFaceSnapshot> target)
    {
        if (
            !element.TryGetProperty("card_faces", out JsonElement faces)
            || faces.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        foreach (JsonElement face in faces.EnumerateArray())
        {
            CardFaceSnapshot snapshot = new()
            {
                Name = GetString(face, "name"),
                ManaCost = GetString(face, "mana_cost"),
                TypeLine = GetString(face, "type_line"),
                OracleText = GetString(face, "oracle_text"),
                Power = GetString(face, "power"),
                Toughness = GetString(face, "toughness"),
                Loyalty = GetString(face, "loyalty"),
                Defense = GetString(face, "defense"),
            };
            AddStringArray(face, "colors", snapshot.Colors);
            target.Add(snapshot);
        }
    }

    /// <summary>
    /// Reads an optional string property from a Scryfall JSON object.
    /// </summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    /// <summary>
    /// Reads an optional date-only property from a Scryfall JSON object.
    /// </summary>
    private static DateOnly? GetDateOnly(JsonElement element, string propertyName)
    {
        return DateOnly.TryParse(GetString(element, propertyName), out DateOnly date)
            ? date
            : null;
    }

    /// <summary>
    /// Reads an optional numeric property from a Scryfall JSON object.
    /// </summary>
    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result)
            ? result
            : null;
    }

    /// <summary>
    /// Reads an optional integer property from a Scryfall JSON object.
    /// </summary>
    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result : null;
    }

    /// <summary>
    /// Gets the face string.
    /// </summary>
    private static string? GetFaceString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty("card_faces", out JsonElement faces))
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
    /// Gets combined face text.
    /// </summary>
    private static string? GetFaceText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty("card_faces", out JsonElement faces))
        {
            return null;
        }

        List<string> values = [];
        foreach (JsonElement face in faces.EnumerateArray())
        {
            string? value = GetString(face, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values.Count == 0 ? null : string.Join("\n\n", values);
    }

    /// <summary>
    /// Builds lookup aliases for cards with multiple faces.
    /// </summary>
    private static List<string> BuildNameAliases(string name)
    {
        List<string> aliases = [];
        AddAlias(aliases, name);

        string[] faces = name.Split(
            ["//"],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        );
        if (faces.Length > 1)
        {
            AddAlias(aliases, string.Join(" // ", faces));
            foreach (string face in faces)
            {
                AddAlias(aliases, face);
            }
        }

        return aliases;
    }

    /// <summary>
    /// Adds a unique alias.
    /// </summary>
    private static void AddAlias(List<string> aliases, string alias)
    {
        string normalized = alias.Trim();
        if (
            !string.IsNullOrWhiteSpace(normalized)
            && !aliases.Any(value => value.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        )
        {
            aliases.Add(normalized);
        }
    }

    /// <summary>
    /// Finds a returned card for the requested aliases.
    /// </summary>
    private static CardInfo? FindReturnedCard(
        string requestedName,
        IReadOnlyList<string> aliases,
        IReadOnlyDictionary<string, CardInfo> returnedCards
    )
    {
        foreach (string alias in aliases)
        {
            if (returnedCards.TryGetValue(alias, out CardInfo? exact))
            {
                return exact;
            }
        }

        foreach (CardInfo card in returnedCards.Values)
        {
            if (CardNameMatches(card.Name, requestedName) || CardNameMatchesAnyAlias(card.Name, aliases))
            {
                return card;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether a returned card name matches any alias.
    /// </summary>
    private static bool CardNameMatchesAnyAlias(string returnedName, IEnumerable<string> aliases)
    {
        foreach (string alias in aliases)
        {
            if (CardNameMatches(returnedName, alias))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a returned card name matches a requested name or face.
    /// </summary>
    private static bool CardNameMatches(string returnedName, string requestedName)
    {
        List<string> returnedAliases = BuildNameAliases(returnedName);
        List<string> requestedAliases = BuildNameAliases(requestedName);
        foreach (string returnedAlias in returnedAliases)
        {
            foreach (string requestedAlias in requestedAliases)
            {
                if (returnedAlias.Equals(requestedAlias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Adds the string array.
    /// </summary>
    private static void AddStringArray(
        JsonElement element,
        string propertyName,
        List<string> target
    )
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            string? value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(value);
            }
        }
    }

    /// <summary>
    /// Adds the string dictionary.
    /// </summary>
    private static void AddStringDictionary(
        JsonElement element,
        string propertyName,
        Dictionary<string, string> target
    )
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement jsonObject)
            || jsonObject.ValueKind != JsonValueKind.Object
        )
        {
            return;
        }

        foreach (JsonProperty property in jsonObject.EnumerateObject())
        {
            string? value =
                property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                target[property.Name] = value;
            }
        }
    }
}
