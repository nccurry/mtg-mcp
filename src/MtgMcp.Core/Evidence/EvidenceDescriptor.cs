using System.Text.Json.Serialization;

namespace MtgMcp.Core.Evidence;

/// <summary>
/// Identifies a fact returned directly by a named source at a known retrieval time.
/// </summary>
public sealed record SourceFactDescriptor(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("snapshotId"),
        JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SnapshotId)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "source-fact";
}

/// <summary>
/// Identifies supporting source material without treating it as a direct factual field.
/// </summary>
public sealed record SourceEvidenceDescriptor(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("retrievedAtUtc")] DateTimeOffset RetrievedAtUtc,
    [property: JsonPropertyName("sourceReference")] string SourceReference,
    [property: JsonPropertyName("snapshotId"),
        JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SnapshotId)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "source-evidence";
}

/// <summary>
/// Describes a value obtained through exact mathematics under explicit assumptions.
/// </summary>
public sealed record ExactDerivationDescriptor(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "exact-derivation";
}

/// <summary>
/// Describes a deterministic classification produced by a versioned parser.
/// </summary>
public sealed record ParserClassificationDescriptor(
    [property: JsonPropertyName("parserVersion")] string ParserVersion,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "parser-classification";
}

/// <summary>
/// Describes an estimate produced by a versioned heuristic model.
/// </summary>
public sealed record HeuristicEstimateDescriptor(
    [property: JsonPropertyName("modelVersion")] string ModelVersion,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "heuristic-estimate";
}

/// <summary>
/// Describes a sampled estimate with the replay metadata needed to interpret it.
/// </summary>
public sealed record SampledEstimateDescriptor(
    [property: JsonPropertyName("modelVersion")] string ModelVersion,
    [property: JsonPropertyName("sampleCount")] int SampleCount,
    [property: JsonPropertyName("seed")] long Seed,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions)
{
    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "sampled-estimate";
}

/// <summary>
/// Represents the complete evidence taxonomy used by stable evidence-first results.
/// </summary>
[JsonConverter(typeof(EvidenceDescriptorJsonConverter))]
public readonly union EvidenceDescriptor(
    SourceFactDescriptor,
    SourceEvidenceDescriptor,
    ExactDerivationDescriptor,
    ParserClassificationDescriptor,
    HeuristicEstimateDescriptor,
    SampledEstimateDescriptor);
