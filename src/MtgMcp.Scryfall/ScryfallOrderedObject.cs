using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Scryfall;

/// <summary>
/// Holds a small JSON object whose properties are written in the order they were added, for results whose shape
/// depends on what the caller asked for, such as grouped metrics and chosen row fields.
/// </summary>
[JsonConverter(typeof(ScryfallOrderedObjectJsonConverter))]
public sealed class ScryfallOrderedObject
{
    /// <summary>
    /// Holds the properties in write order.
    /// </summary>
    private readonly List<KeyValuePair<string, object?>> fields = [];

    /// <summary>
    /// Gets the property names and values in write order. A null value is written as an explicit JSON null.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Fields => fields;

    /// <summary>
    /// Adds one property after the existing ones.
    /// </summary>
    public ScryfallOrderedObject Add(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        fields.Add(new KeyValuePair<string, object?>(name, value));
        return this;
    }

    /// <summary>
    /// Finds the value of one property, or null when it is not there.
    /// </summary>
    public object? Get(string name)
    {
        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (string.Equals(field.Key, name, StringComparison.Ordinal))
            {
                return field.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// Writes an ordered object as one JSON object whose properties follow the order they were added.
/// </summary>
public sealed class ScryfallOrderedObjectJsonConverter : JsonConverter<ScryfallOrderedObject>
{
    /// <inheritdoc/>
    public override ScryfallOrderedObject Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        throw new NotSupportedException("Ordered objects are only written by the server.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ScryfallOrderedObject value, JsonSerializerOptions options)
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
