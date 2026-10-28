using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Scryfall;

/// <summary>
/// Holds the small set of card fields chosen for a concise card result, in a fixed order.
/// </summary>
[JsonConverter(typeof(ScryfallConciseCardJsonConverter))]
public sealed class ScryfallConciseCard
{
    /// <summary>
    /// Creates a concise card from ordered field values.
    /// </summary>
    internal ScryfallConciseCard(IReadOnlyList<KeyValuePair<string, object?>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        Fields = fields;
    }

    /// <summary>
    /// Gets the field names and values in output order. A null value is written as an explicit JSON null.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Fields { get; }
}

/// <summary>
/// Writes a concise card as one JSON object whose properties follow the card's field order.
/// </summary>
public sealed class ScryfallConciseCardJsonConverter : JsonConverter<ScryfallConciseCard>
{
    /// <inheritdoc/>
    public override ScryfallConciseCard Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException("Concise cards are only written by the server.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ScryfallConciseCard value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> field in value.Fields)
        {
            writer.WritePropertyName(field.Key);
            if (field.Value is null)
            {
                writer.WriteNullValue();
                continue;
            }

            JsonSerializer.Serialize(writer, field.Value, field.Value.GetType(), options);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Holds either the complete mapped card (standard and full formats) or the concise projection.
/// </summary>
[JsonConverter(typeof(ScryfallCardViewJsonConverter))]
public readonly union ScryfallCardView(ScryfallCard, ScryfallConciseCard);

/// <summary>
/// Writes whichever card shape a card view holds.
/// </summary>
public sealed class ScryfallCardViewJsonConverter : JsonConverter<ScryfallCardView>
{
    /// <inheritdoc/>
    public override ScryfallCardView Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException("Card views are only written by the server.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ScryfallCardView value, JsonSerializerOptions options)
    {
        object activeCase = value.Value ?? throw new JsonException("A card view must contain an active case.");
        JsonSerializer.Serialize(writer, activeCase, activeCase.GetType(), options);
    }
}
