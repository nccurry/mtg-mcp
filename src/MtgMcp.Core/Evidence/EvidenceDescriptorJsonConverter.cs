using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgMcp.Core.Evidence;

/// <summary>
/// Serializes evidence descriptors as their active case payload with a stable discriminator.
/// </summary>
public sealed class EvidenceDescriptorJsonConverter : JsonConverter<EvidenceDescriptor>
{
    /// <inheritdoc/>
    public override EvidenceDescriptor Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("An evidence descriptor must be a JSON object.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (!document.RootElement.TryGetProperty("kind", out JsonElement kindElement) ||
            kindElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("The evidence descriptor is missing a string kind discriminator.");
        }

        return kindElement.GetString() switch
        {
            "source-fact" => ReadCase<SourceFactDescriptor>(document.RootElement, options),
            "source-evidence" => ReadCase<SourceEvidenceDescriptor>(document.RootElement, options),
            "exact-derivation" => ReadCase<ExactDerivationDescriptor>(document.RootElement, options),
            "parser-classification" => ReadCase<ParserClassificationDescriptor>(document.RootElement, options),
            "heuristic-estimate" => ReadCase<HeuristicEstimateDescriptor>(document.RootElement, options),
            "sampled-estimate" => ReadCase<SampledEstimateDescriptor>(document.RootElement, options),
            _ => throw new JsonException("The evidence descriptor kind is unknown."),
        };
    }

    /// <inheritdoc/>
    public override void Write(
        Utf8JsonWriter writer,
        EvidenceDescriptor value,
        JsonSerializerOptions options)
    {
        object activeCase = value.Value ??
            throw new JsonException("An evidence descriptor must contain an active case.");
        JsonSerializer.Serialize(writer, activeCase, activeCase.GetType(), options);
    }

    /// <summary>
    /// Deserializes a nonnull case payload from the buffered union object.
    /// </summary>
    private static TCase ReadCase<TCase>(JsonElement element, JsonSerializerOptions options)
        where TCase : notnull
    {
        return element.Deserialize<TCase>(options) ??
            throw new JsonException("The evidence descriptor case payload is null.");
    }
}
