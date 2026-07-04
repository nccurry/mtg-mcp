using System.Text.Json.Serialization;

namespace MtgMcp.Core.Evidence;

/// <summary>
/// Identifies a fact returned directly by a named source at a known retrieval time.
/// </summary>
public sealed record SourceFactDescriptor
{
    /// <summary>
    /// Creates source-fact metadata with normalized source and time values.
    /// </summary>
    [JsonConstructor]
    public SourceFactDescriptor(string source, DateTimeOffset retrievedAtUtc, string? snapshotId)
    {
        Source = ContractValidation.RequiredText(source, nameof(source));
        RetrievedAtUtc = retrievedAtUtc.ToUniversalTime();
        SnapshotId = ContractValidation.OptionalText(snapshotId, nameof(snapshotId));
    }

    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "source-fact";

    /// <summary>
    /// Gets the source that supplied the fact.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; }

    /// <summary>
    /// Gets the retrieval instant normalized to UTC.
    /// </summary>
    [JsonPropertyName("retrievedAtUtc")]
    public DateTimeOffset RetrievedAtUtc { get; }

    /// <summary>
    /// Gets the optional immutable snapshot identifier.
    /// </summary>
    [JsonPropertyName("snapshotId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SnapshotId { get; }
}

/// <summary>
/// Identifies supporting source material without treating it as a direct factual field.
/// </summary>
public sealed record SourceEvidenceDescriptor
{
    /// <summary>
    /// Creates source-evidence metadata with normalized source and time values.
    /// </summary>
    [JsonConstructor]
    public SourceEvidenceDescriptor(
        string source,
        DateTimeOffset retrievedAtUtc,
        string sourceReference,
        string? snapshotId)
    {
        Source = ContractValidation.RequiredText(source, nameof(source));
        RetrievedAtUtc = retrievedAtUtc.ToUniversalTime();
        SourceReference = ContractValidation.RequiredText(sourceReference, nameof(sourceReference));
        SnapshotId = ContractValidation.OptionalText(snapshotId, nameof(snapshotId));
    }

    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "source-evidence";

    /// <summary>
    /// Gets the source that supplied the supporting material.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; }

    /// <summary>
    /// Gets the retrieval instant normalized to UTC.
    /// </summary>
    [JsonPropertyName("retrievedAtUtc")]
    public DateTimeOffset RetrievedAtUtc { get; }

    /// <summary>
    /// Gets the opaque source-specific reference.
    /// </summary>
    [JsonPropertyName("sourceReference")]
    public string SourceReference { get; }

    /// <summary>
    /// Gets the optional immutable snapshot identifier.
    /// </summary>
    [JsonPropertyName("snapshotId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SnapshotId { get; }
}

/// <summary>
/// Describes a value obtained through exact mathematics under explicit assumptions.
/// </summary>
public sealed record ExactDerivationDescriptor
{
    /// <summary>
    /// Creates exact-derivation metadata with an immutable assumption list.
    /// </summary>
    [JsonConstructor]
    public ExactDerivationDescriptor(string method, IReadOnlyList<string> assumptions)
    {
        Method = ContractValidation.RequiredText(method, nameof(method));
        Assumptions = ContractValidation.Assumptions(assumptions, nameof(assumptions));
    }

    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "exact-derivation";

    /// <summary>
    /// Gets the exact calculation method.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; }

    /// <summary>
    /// Gets the immutable ordered assumptions.
    /// </summary>
    [JsonPropertyName("assumptions")]
    public IReadOnlyList<string> Assumptions { get; }
}

/// <summary>
/// Describes a deterministic classification produced by a versioned parser.
/// </summary>
public sealed record ParserClassificationDescriptor
{
    /// <summary>
    /// Creates parser metadata with an immutable assumption list.
    /// </summary>
    [JsonConstructor]
    public ParserClassificationDescriptor(string parserVersion, IReadOnlyList<string> assumptions)
    {
        ParserVersion = ContractValidation.RequiredText(parserVersion, nameof(parserVersion));
        Assumptions = ContractValidation.Assumptions(assumptions, nameof(assumptions));
    }

    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "parser-classification";

    /// <summary>
    /// Gets the parser contract version.
    /// </summary>
    [JsonPropertyName("parserVersion")]
    public string ParserVersion { get; }

    /// <summary>
    /// Gets the immutable ordered assumptions.
    /// </summary>
    [JsonPropertyName("assumptions")]
    public IReadOnlyList<string> Assumptions { get; }
}

/// <summary>
/// Describes an estimate produced by a versioned heuristic model.
/// </summary>
public sealed record HeuristicEstimateDescriptor
{
    /// <summary>
    /// Creates heuristic metadata with an immutable assumption list.
    /// </summary>
    [JsonConstructor]
    public HeuristicEstimateDescriptor(string modelVersion, IReadOnlyList<string> assumptions)
    {
        ModelVersion = ContractValidation.RequiredText(modelVersion, nameof(modelVersion));
        Assumptions = ContractValidation.Assumptions(assumptions, nameof(assumptions));
    }

    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "heuristic-estimate";

    /// <summary>
    /// Gets the heuristic model version.
    /// </summary>
    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; }

    /// <summary>
    /// Gets the immutable ordered assumptions.
    /// </summary>
    [JsonPropertyName("assumptions")]
    public IReadOnlyList<string> Assumptions { get; }
}

/// <summary>
/// Describes a sampled estimate with the replay metadata needed to interpret it.
/// </summary>
public sealed record SampledEstimateDescriptor
{
    /// <summary>
    /// Creates sampled metadata with replay and immutable assumption values.
    /// </summary>
    [JsonConstructor]
    public SampledEstimateDescriptor(
        string modelVersion,
        int sampleCount,
        long seed,
        IReadOnlyList<string> assumptions)
    {
        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleCount),
                sampleCount,
                "Sample count must be positive.");
        }

        ModelVersion = ContractValidation.RequiredText(modelVersion, nameof(modelVersion));
        SampleCount = sampleCount;
        Seed = seed;
        Assumptions = ContractValidation.Assumptions(assumptions, nameof(assumptions));
    }

    /// <summary>
    /// Gets the stable serialized case discriminator.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonPropertyOrder(-100)]
    public string Kind => "sampled-estimate";

    /// <summary>
    /// Gets the sampled model version.
    /// </summary>
    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; }

    /// <summary>
    /// Gets the positive number of samples evaluated.
    /// </summary>
    [JsonPropertyName("sampleCount")]
    public int SampleCount { get; }

    /// <summary>
    /// Gets the deterministic replay seed.
    /// </summary>
    [JsonPropertyName("seed")]
    public long Seed { get; }

    /// <summary>
    /// Gets the immutable ordered assumptions.
    /// </summary>
    [JsonPropertyName("assumptions")]
    public IReadOnlyList<string> Assumptions { get; }
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
