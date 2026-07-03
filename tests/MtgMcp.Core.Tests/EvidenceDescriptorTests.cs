using System.Text.Json;
using MtgMcp.Core.Evidence;

namespace MtgMcp.Core.Tests;

/// <summary>
/// Verifies the closed evidence taxonomy and case-specific metadata.
/// </summary>
public sealed class EvidenceDescriptorTests
{
    /// <summary>
    /// Provides the web serializer behavior used by the future MCP host.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies every evidence discriminator and its exact applicable field set.
    /// </summary>
    [Fact]
    public void Cases_SerializeStableDiscriminatorsAndApplicableMetadata()
    {
        DateTimeOffset retrievedAt = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
        EvidenceDescriptor[] descriptors =
        [
            new SourceFactDescriptor("scryfall", retrievedAt, null),
            new SourceEvidenceDescriptor("archidekt", retrievedAt, "deck:42", "snapshot-1"),
            new ExactDerivationDescriptor("hypergeometric", ["seven-card opening hand"]),
            new ParserClassificationDescriptor("deck-parser-v1", ["mainboard input"]),
            new HeuristicEstimateDescriptor("heuristic-v1", ["no opponent interaction"]),
            new SampledEstimateDescriptor("sampler-v1", 10_000, 42, ["on the play"]),
        ];
        string[][] expectedProperties =
        [
            ["kind", "retrievedAtUtc", "source"],
            ["kind", "retrievedAtUtc", "snapshotId", "source", "sourceReference"],
            ["assumptions", "kind", "method"],
            ["assumptions", "kind", "parserVersion"],
            ["assumptions", "kind", "modelVersion"],
            ["assumptions", "kind", "modelVersion", "sampleCount", "seed"],
        ];
        string[] expectedKinds =
        [
            "source-fact",
            "source-evidence",
            "exact-derivation",
            "parser-classification",
            "heuristic-estimate",
            "sampled-estimate",
        ];

        for (int index = 0; index < descriptors.Length; index++)
        {
            string json = JsonSerializer.Serialize(descriptors[index], SerializerOptions);
            using JsonDocument document = JsonDocument.Parse(json);
            string[] propertyNames = document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            EvidenceDescriptor roundTrip =
                JsonSerializer.Deserialize<EvidenceDescriptor>(json, SerializerOptions);

            Assert.Equal(expectedKinds[index], document.RootElement.GetProperty("kind").GetString());
            Assert.Equal(expectedKinds[index], Describe(descriptors[index]));
            Assert.Equal(expectedKinds[index], Describe(roundTrip));
            Assert.Equal(expectedProperties[index], propertyNames);
        }
    }

    /// <summary>
    /// Verifies missing, unknown, and empty union payloads fail instead of producing invented evidence.
    /// </summary>
    [Fact]
    public void InvalidJsonOrEmptyUnion_Throws()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EvidenceDescriptor>("{}", SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EvidenceDescriptor>("null", SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EvidenceDescriptor>(
                "{\"kind\":42}",
                SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EvidenceDescriptor>(
                "{\"kind\":\"future-evidence\"}",
                SerializerOptions));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Serialize(default(EvidenceDescriptor), SerializerOptions));
    }

    /// <summary>
    /// Exhaustively maps every closed evidence case for compile-time change detection.
    /// </summary>
    private static string Describe(EvidenceDescriptor descriptor)
    {
        return descriptor switch
        {
            SourceFactDescriptor value => value.Kind,
            SourceEvidenceDescriptor value => value.Kind,
            ExactDerivationDescriptor value => value.Kind,
            ParserClassificationDescriptor value => value.Kind,
            HeuristicEstimateDescriptor value => value.Kind,
            SampledEstimateDescriptor value => value.Kind,
        };
    }
}
