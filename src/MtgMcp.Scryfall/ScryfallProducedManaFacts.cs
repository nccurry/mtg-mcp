using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Scryfall;

/// <summary>
/// Records that the Scryfall card object did not contain a produced-mana field.
/// </summary>
public sealed record ScryfallProducedManaMissing
{
    /// <summary>
    /// Gets the stable source-field state name.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "missing";
}

/// <summary>
/// Records that the Scryfall card object explicitly set produced-mana to null.
/// </summary>
public sealed record ScryfallProducedManaNull
{
    /// <summary>
    /// Gets the stable source-field state name.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "null";
}

/// <summary>
/// Records the ordered mana colors that Scryfall supplied, including an empty list.
/// </summary>
public sealed record ScryfallProducedManaValues
{
    /// <summary>
    /// Creates one direct list of produced-mana colors.
    /// </summary>
    public ScryfallProducedManaValues(IReadOnlyList<string> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Colors = Array.AsReadOnly(colors.ToArray());
    }

    /// <summary>
    /// Gets the stable source-field state name.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "values";

    /// <summary>
    /// Gets an immutable copy of the provider-listed mana colors.
    /// </summary>
    [JsonPropertyName("colors")]
    public IReadOnlyList<string> Colors { get; }
}

/// <summary>
/// Preserves whether Scryfall omitted, nullified, or listed mana colors for a card.
/// </summary>
[JsonConverter(typeof(ScryfallProducedManaJsonConverter))]
public readonly union ScryfallProducedMana(
    ScryfallProducedManaMissing,
    ScryfallProducedManaNull,
    ScryfallProducedManaValues);

/// <summary>
/// Serializes and deserializes the active Scryfall produced-mana source-field state.
/// </summary>
public sealed class ScryfallProducedManaJsonConverter : JsonConverter<ScryfallProducedMana>
{
    /// <inheritdoc/>
    public override ScryfallProducedMana Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A produced-mana fact must be a JSON object.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty("kind", out JsonElement kindElement) ||
            kindElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("A produced-mana fact is missing a string kind discriminator.");
        }

        return kindElement.GetString() switch
        {
            "missing" => ReadCase<ScryfallProducedManaMissing>(document.RootElement, options),
            "null" => ReadCase<ScryfallProducedManaNull>(document.RootElement, options),
            "values" => ReadValues(document.RootElement),
            _ => throw new JsonException("The produced-mana fact kind is unknown."),
        };
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        ScryfallProducedMana value,
        JsonSerializerOptions options)
    {
        object activeCase = value.Value ??
            throw new JsonException("A produced-mana fact must contain an active case.");
        JsonSerializer.Serialize(writer, activeCase, activeCase.GetType(), options);
    }

    /// <summary>
    /// Deserializes one nonnull source-field state from a buffered JSON object.
    /// </summary>
    private static TCase ReadCase<TCase>(JsonElement element, JsonSerializerOptions options)
        where TCase : notnull
    {
        return element.Deserialize<TCase>(options) ??
            throw new JsonException("The produced-mana fact case payload is null.");
    }

    /// <summary>
    /// Reads the required string list for a listed produced-mana source state.
    /// </summary>
    private static ScryfallProducedManaValues ReadValues(JsonElement element)
    {
        if (!element.TryGetProperty("colors", out JsonElement colorsElement) ||
            colorsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A listed produced-mana fact must contain a colors array.");
        }

        List<string> colors = [];
        foreach (JsonElement colorElement in colorsElement.EnumerateArray())
        {
            if (colorElement.ValueKind != JsonValueKind.String || colorElement.GetString() is not string color)
            {
                throw new JsonException("A listed produced-mana fact colors array must contain only strings.");
            }

            colors.Add(color);
        }

        return new ScryfallProducedManaValues(colors);
    }
}
