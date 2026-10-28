using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Reads an integer argument the way JSON Schema "integer" allows: a number with no fractional part, such as
/// <c>328.0</c> or <c>3.28e2</c>, is accepted. A number with a fractional part or outside the range of the target
/// type is refused with a message that states the rule; the serializer adds the JSON path.
/// </summary>
internal static class WholeNumberConverters
{
    /// <summary>
    /// Gets the converters for the integer types tool inputs use, to be added to the tool serializer options.
    /// </summary>
    internal static IReadOnlyList<JsonConverter> All { get; } = [new Int32Converter(), new Int64Converter()];

    /// <summary>
    /// Reads a whole number from the current token, or throws a bounded <see cref="JsonException"/>.
    /// </summary>
    private static decimal ReadWhole(ref Utf8JsonReader reader, string typeName, decimal min, decimal max)
    {
        decimal value;
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (!reader.TryGetDecimal(out value))
            {
                double approximate = reader.GetDouble();
                if (double.IsNaN(approximate) || double.IsInfinity(approximate) || Math.Abs(approximate) >= 1e28)
                {
                    throw Refuse(typeName, min, max, "is out of range");
                }

                value = (decimal)approximate;
            }
        }
        else
        {
            throw new JsonException($"The JSON value could not be converted to {typeName}: it is not a number.");
        }

        if (value != decimal.Truncate(value))
        {
            throw Refuse(typeName, min, max, "has a fractional part");
        }

        if (value < min || value > max)
        {
            throw Refuse(typeName, min, max, "is out of range");
        }

        return value;
    }

    /// <summary>
    /// Builds the refusal that states the rule.
    /// </summary>
    private static JsonException Refuse(string typeName, decimal min, decimal max, string reason)
    {
        return new JsonException(
            $"The number {reason}: expected a whole number from {min.ToString(CultureInfo.InvariantCulture)} to " +
            $"{max.ToString(CultureInfo.InvariantCulture)} ({typeName}).");
    }

    /// <summary>
    /// Converts 32-bit integers.
    /// </summary>
    private sealed class Int32Converter : JsonConverter<int>
    {
        /// <inheritdoc/>
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return (int)ReadWhole(ref reader, "System.Int32", int.MinValue, int.MaxValue);
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }

    /// <summary>
    /// Converts 64-bit integers.
    /// </summary>
    private sealed class Int64Converter : JsonConverter<long>
    {
        /// <inheritdoc/>
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return (long)ReadWhole(ref reader, "System.Int64", long.MinValue, long.MaxValue);
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }
    }
}
