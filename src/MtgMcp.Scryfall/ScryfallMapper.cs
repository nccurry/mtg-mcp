using System.Globalization;
using System.Text.Json;
using MtgMcp.Core.Evidence;

namespace MtgMcp.Scryfall;

/// <summary>
/// Maps lossless provider JSON into stable normalized evidence projections.
/// </summary>
internal static class ScryfallMapper
{
    /// <summary>
    /// Maps one card source object and already resolved tag evidence.
    /// </summary>
    internal static ScryfallCard Card(
        JsonElement raw,
        DateTimeOffset retrievedAtUtc,
        string snapshotId,
        IReadOnlyList<ScryfallTagEvidence>? tags = null,
        bool pricesStale = false,
        string tagCoverage = "not-cached",
        bool includeRaw = true)
    {
        Guid id = RequiredGuid(raw, "id");
        string name = RequiredString(raw, "name");
        string setCode = RequiredString(raw, "set");
        string collectorNumber = RequiredString(raw, "collector_number");
        string language = RequiredString(raw, "lang");
        List<ScryfallCardFace> faces = [];
        if (raw.TryGetProperty("card_faces", out JsonElement faceValues) &&
            faceValues.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement face in faceValues.EnumerateArray())
            {
                faces.Add(new ScryfallCardFace(
                    OptionalString(face, "name") ?? name,
                    OptionalString(face, "mana_cost"),
                    OptionalString(face, "type_line"),
                    OptionalString(face, "oracle_text"),
                    Strings(face, "colors"),
                    StringMap(face, "image_uris"),
                    OptionalGuid(face, "illustration_id"),
                    includeRaw ? face.Clone() : null));
            }
        }

        string freshness = pricesStale ? "stale" : "fresh";
        SourceFactDescriptor priceSource = new("scryfall-price", retrievedAtUtc, snapshotId);
        IReadOnlyDictionary<string, string?> prices = NullableStringMap(raw, "prices");
        List<ScryfallPriceEvidence> priceEvidence = [];
        AddPrice(priceEvidence, prices, "eur", "EUR", "nonfoil", freshness, priceSource);
        AddPrice(priceEvidence, prices, "eur_foil", "EUR", "foil", freshness, priceSource);
        AddPrice(priceEvidence, prices, "tix", "TIX", "digital", freshness, priceSource);
        AddPrice(priceEvidence, prices, "usd", "USD", "nonfoil", freshness, priceSource);
        AddPrice(priceEvidence, prices, "usd_etched", "USD", "etched", freshness, priceSource);
        AddPrice(priceEvidence, prices, "usd_foil", "USD", "foil", freshness, priceSource);
        List<ScryfallRankEvidence> rankEvidence = [];
        AddRank(rankEvidence, raw, "edhrec_rank", "edhrec-deck-inclusion", freshness, retrievedAtUtc, snapshotId);
        AddRank(rankEvidence, raw, "penny_rank", "penny-dreadful-popularity", freshness, retrievedAtUtc, snapshotId);

        return new ScryfallCard(
            id,
            OptionalGuid(raw, "oracle_id"),
            OptionalGuid(raw, "illustration_id"),
            name,
            setCode,
            collectorNumber,
            language,
            OptionalString(raw, "released_at"),
            OptionalString(raw, "mana_cost"),
            OptionalDecimal(raw, "cmc"),
            OptionalString(raw, "type_line"),
            OptionalString(raw, "oracle_text"),
            Strings(raw, "colors"),
            Strings(raw, "color_identity"),
            Strings(raw, "keywords"),
            StringMap(raw, "legalities"),
            StringMap(raw, "image_uris"),
            prices,
            priceEvidence,
            rankEvidence,
            faces,
            tags ?? [],
            tagCoverage,
            new SourceFactDescriptor("scryfall", retrievedAtUtc, snapshotId),
            includeRaw ? raw.Clone() : null);
    }

    /// <summary>
    /// Adds one known provider price field when the price object supplies it, including known-null values.
    /// </summary>
    private static void AddPrice(
        ICollection<ScryfallPriceEvidence> result,
        IReadOnlyDictionary<string, string?> prices,
        string field,
        string currency,
        string finish,
        string freshness,
        SourceFactDescriptor source)
    {
        if (prices.TryGetValue(field, out string? amount))
        {
            result.Add(new ScryfallPriceEvidence(
                field,
                amount,
                currency,
                finish,
                "scryfall-market-price",
                freshness,
                source));
        }
    }

    /// <summary>
    /// Adds one contextual provider rank without treating it as a quality score.
    /// </summary>
    private static void AddRank(
        ICollection<ScryfallRankEvidence> result,
        JsonElement raw,
        string field,
        string context,
        string freshness,
        DateTimeOffset retrievedAtUtc,
        string snapshotId)
    {
        if (raw.TryGetProperty(field, out JsonElement value) && value.TryGetInt64(out long rank))
        {
            result.Add(new ScryfallRankEvidence(
                field,
                rank,
                context,
                freshness,
                new SourceFactDescriptor("scryfall-rank", retrievedAtUtc, snapshotId)));
        }
    }

    /// <summary>
    /// Maps one ruling source object.
    /// </summary>
    internal static ScryfallRuling Ruling(
        JsonElement raw,
        DateTimeOffset retrievedAtUtc,
        string snapshotId,
        bool includeRaw = true)
    {
        return new ScryfallRuling(
            RequiredGuid(raw, "oracle_id"),
            RequiredString(raw, "source"),
            RequiredString(raw, "published_at"),
            RequiredStringAllowEmpty(raw, "comment"),
            new SourceFactDescriptor("scryfall", retrievedAtUtc, snapshotId),
            includeRaw ? raw.Clone() : null);
    }

    /// <summary>
    /// Maps one set source object.
    /// </summary>
    internal static ScryfallSet Set(
        JsonElement raw,
        DateTimeOffset retrievedAtUtc,
        string snapshotId,
        bool includeRaw = true)
    {
        return new ScryfallSet(
            RequiredGuid(raw, "id"),
            RequiredString(raw, "code"),
            RequiredString(raw, "name"),
            RequiredString(raw, "set_type"),
            OptionalString(raw, "released_at"),
            RequiredInt(raw, "card_count"),
            OptionalBoolean(raw, "digital"),
            new SourceFactDescriptor("scryfall", retrievedAtUtc, snapshotId),
            includeRaw ? raw.Clone() : null);
    }

    /// <summary>
    /// Maps one official bulk metadata object.
    /// </summary>
    internal static ScryfallBulkData BulkData(JsonElement raw)
    {
        return new ScryfallBulkData(
            RequiredGuid(raw, "id"),
            RequiredString(raw, "type"),
            RequiredString(raw, "name"),
            RequiredString(raw, "description"),
            RequiredDateTimeOffset(raw, "updated_at"),
            RequiredLong(raw, "size"),
            RequiredString(raw, "content_type"),
            RequiredString(raw, "content_encoding"),
            RequiredString(raw, "download_uri"),
            RequiredString(raw, "jsonl_download_uri"),
            raw.Clone());
    }

    /// <summary>
    /// Maps one installed community tag object.
    /// </summary>
    internal static ScryfallTag Tag(JsonElement raw, Guid generationId, bool includeRaw = true)
    {
        return new ScryfallTag(
            RequiredGuid(raw, "id"),
            RequiredString(raw, "label"),
            RequiredString(raw, "slug"),
            RequiredString(raw, "type") switch
            {
                "illustration" => "art",
                string value => value,
            },
            OptionalString(raw, "description"),
            Guids(raw, "parent_ids"),
            Guids(raw, "child_ids"),
            Strings(raw, "aliases"),
            generationId,
            includeRaw ? raw.Clone() : null);
    }

    /// <summary>
    /// Reads a required nonblank string property.
    /// </summary>
    internal static string RequiredString(JsonElement element, string name)
    {
        string? value = OptionalString(element, name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Scryfall object is missing required field '{name}'.");
    }

    /// <summary>
    /// Reads a required string property while preserving a provider-authored empty value.
    /// </summary>
    internal static string RequiredStringAllowEmpty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidDataException($"Scryfall object is missing required field '{name}'.");
    }

    /// <summary>
    /// Reads an optional string property without conflating null and empty values.
    /// </summary>
    internal static string? OptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Reads a required GUID string property.
    /// </summary>
    internal static Guid RequiredGuid(JsonElement element, string name)
    {
        string value = RequiredString(element, name);
        return Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new InvalidDataException($"Scryfall field '{name}' is not a UUID.");
    }

    /// <summary>
    /// Reads an optional GUID string property.
    /// </summary>
    internal static Guid? OptionalGuid(JsonElement element, string name)
    {
        string? value = OptionalString(element, name);
        return value is null
            ? null
            : Guid.TryParse(value, out Guid parsed)
                ? parsed
                : throw new InvalidDataException($"Scryfall field '{name}' is not a UUID.");
    }

    /// <summary>
    /// Copies one optional string array in provider order.
    /// </summary>
    internal static IReadOnlyList<string> Strings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> result = [];
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String && value.GetString() is string text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>
    /// Copies one optional UUID array in provider order.
    /// </summary>
    internal static IReadOnlyList<Guid> Guids(JsonElement element, string name)
    {
        List<Guid> result = [];
        foreach (string value in Strings(element, name))
        {
            if (!Guid.TryParse(value, out Guid parsed))
            {
                throw new InvalidDataException($"Scryfall field '{name}' contains a non-UUID value.");
            }

            result.Add(parsed);
        }

        return result;
    }

    /// <summary>
    /// Copies one optional string-valued object in ordinal property order.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> StringMap(JsonElement element, string name)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (!element.TryGetProperty(name, out JsonElement values) || values.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in values.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is string value)
            {
                result.Add(property.Name, value);
            }
        }

        return result;
    }

    /// <summary>
    /// Copies one optional nullable-string object in ordinal property order.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> NullableStringMap(JsonElement element, string name)
    {
        Dictionary<string, string?> result = new(StringComparer.Ordinal);
        if (!element.TryGetProperty(name, out JsonElement values) || values.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in values.EnumerateObject())
        {
            result.Add(
                property.Name,
                property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null);
        }

        return result;
    }

    /// <summary>
    /// Reads a required whole-number property.
    /// </summary>
    private static int RequiredInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int parsed)
            ? parsed
            : throw new InvalidDataException($"Scryfall field '{name}' is not an integer.");
    }

    /// <summary>
    /// Reads a required 64-bit whole-number property.
    /// </summary>
    private static long RequiredLong(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long parsed)
            ? parsed
            : throw new InvalidDataException($"Scryfall field '{name}' is not an integer.");
    }

    /// <summary>
    /// Reads an optional decimal number.
    /// </summary>
    private static decimal? OptionalDecimal(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.TryGetDecimal(out decimal parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Reads an optional Boolean value.
    /// </summary>
    private static bool OptionalBoolean(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            value.GetBoolean();
    }

    /// <summary>
    /// Reads a required timestamp and normalizes it to UTC.
    /// </summary>
    private static DateTimeOffset RequiredDateTimeOffset(JsonElement element, string name)
    {
        string value = RequiredString(element, name);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : throw new InvalidDataException($"Scryfall field '{name}' is not a timestamp.");
    }
}
