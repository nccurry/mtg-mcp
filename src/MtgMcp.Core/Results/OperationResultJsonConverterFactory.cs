using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Core.Results;

/// <summary>
/// Creates closed generic converters that serialize operation results as their active case payload.
/// </summary>
public sealed class OperationResultJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType &&
            typeToConvert.GetGenericTypeDefinition() == typeof(OperationResult<>);
    }

    /// <inheritdoc/>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type dataType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(OperationResultJsonConverter<>).MakeGenericType(dataType);
        return (JsonConverter)(Activator.CreateInstance(converterType) ??
            throw new InvalidOperationException("The operation-result converter could not be created."));
    }

    /// <summary>
    /// Serializes and deserializes one closed operation-result type using its stable case discriminator.
    /// </summary>
    private sealed class OperationResultJsonConverter<T> : JsonConverter<OperationResult<T>>
    {
        /// <inheritdoc/>
        public override OperationResult<T> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("An operation result must be a JSON object.");
            }

            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            if (!document.RootElement.TryGetProperty("kind", out JsonElement kindElement) ||
                kindElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("The operation result is missing a string kind discriminator.");
            }

            return kindElement.GetString() switch
            {
                "success" => ReadCase<OperationSuccess<T>>(document.RootElement, options),
                "not-found" => ReadCase<OperationNotFound>(document.RootElement, options),
                "not-cached" => ReadCase<OperationNotCached>(document.RootElement, options),
                "unsupported" => ReadCase<OperationUnsupported>(document.RootElement, options),
                "unavailable" => ReadCase<OperationUnavailable>(document.RootElement, options),
                "conflict" => ReadCase<OperationConflict>(document.RootElement, options),
                "invalid-input" => ReadCase<OperationInvalidInput>(document.RootElement, options),
                _ => throw new JsonException("The operation result kind is unknown."),
            };
        }

        /// <inheritdoc/>
        public override void Write(
            Utf8JsonWriter writer,
            OperationResult<T> value,
            JsonSerializerOptions options)
        {
            object activeCase = value.Value ??
                throw new JsonException("An operation result must contain an active case.");
            JsonSerializer.Serialize(writer, activeCase, activeCase.GetType(), options);
        }

        /// <summary>
        /// Deserializes a nonnull case payload from the buffered union object.
        /// </summary>
        private static TCase ReadCase<TCase>(JsonElement element, JsonSerializerOptions options)
            where TCase : notnull
        {
            return element.Deserialize<TCase>(options) ??
                throw new JsonException("The operation result case payload is null.");
        }
    }
}
