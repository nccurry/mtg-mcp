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
    /// Verifies source metadata is trimmed, UTC-normalized, and rejects missing identifiers.
    /// </summary>
    [Fact]
    public void SourceCases_EnforceSemanticContract()
    {
        DateTimeOffset localTime = new(2026, 7, 3, 8, 0, 0, TimeSpan.FromHours(-4));
        SourceFactDescriptor fact = new(" scryfall ", localTime, " snapshot-1 ");
        SourceEvidenceDescriptor evidence = new(
            " archidekt ",
            localTime,
            " deck:42 ",
            null);

        Assert.Equal("scryfall", fact.Source);
        Assert.Equal(TimeSpan.Zero, fact.RetrievedAtUtc.Offset);
        Assert.Equal(localTime.ToUniversalTime(), fact.RetrievedAtUtc);
        Assert.Equal("snapshot-1", fact.SnapshotId);
        Assert.Equal("archidekt", evidence.Source);
        Assert.Equal("deck:42", evidence.SourceReference);
        Assert.Null(evidence.SnapshotId);
        Assert.ThrowsAny<ArgumentException>(() => new SourceFactDescriptor(" ", localTime, null));
        Assert.ThrowsAny<ArgumentException>(() => new SourceFactDescriptor("source", localTime, " "));
        Assert.ThrowsAny<ArgumentException>(() =>
            new SourceEvidenceDescriptor("source", localTime, " ", null));
    }

    /// <summary>
    /// Verifies derivation metadata owns normalized immutable assumptions and required versions.
    /// </summary>
    [Fact]
    public void DerivedCases_EnforceSemanticContract()
    {
        List<string> assumptions = [" first assumption "];
        ExactDerivationDescriptor exact = new(" hypergeometric ", assumptions);
        ParserClassificationDescriptor parser = new(" parser-v1 ", assumptions);
        HeuristicEstimateDescriptor heuristic = new(" heuristic-v1 ", assumptions);
        SampledEstimateDescriptor sampled = new(" sampler-v1 ", 100, 42, assumptions);
        assumptions[0] = "changed";
        assumptions.Add("new");

        Assert.Equal("hypergeometric", exact.Method);
        Assert.Equal(["first assumption"], exact.Assumptions);
        Assert.Equal("parser-v1", parser.ParserVersion);
        Assert.Equal(["first assumption"], parser.Assumptions);
        Assert.Equal("heuristic-v1", heuristic.ModelVersion);
        Assert.Equal(["first assumption"], heuristic.Assumptions);
        Assert.Equal("sampler-v1", sampled.ModelVersion);
        Assert.Equal(["first assumption"], sampled.Assumptions);
        Assert.Equal(100, sampled.SampleCount);
        Assert.Equal(42, sampled.Seed);

        Assert.ThrowsAny<ArgumentException>(() => new ExactDerivationDescriptor(" ", []));
        Assert.Throws<ArgumentNullException>(() => new ExactDerivationDescriptor("method", null!));
        Assert.ThrowsAny<ArgumentException>(() => new ExactDerivationDescriptor("method", [" "]));
        Assert.ThrowsAny<ArgumentException>(() => new ParserClassificationDescriptor(" ", []));
        Assert.ThrowsAny<ArgumentException>(() => new HeuristicEstimateDescriptor(" ", []));
        Assert.ThrowsAny<ArgumentException>(() => new SampledEstimateDescriptor(" ", 1, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SampledEstimateDescriptor("sampler-v1", 0, 0, []));
    }

    /// <summary>
    /// Verifies semantically invalid case payloads cannot enter through JSON round trips.
    /// </summary>
    [Fact]
    public void InvalidSemanticJson_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            JsonSerializer.Deserialize<EvidenceDescriptor>(
                "{\"kind\":\"source-fact\",\"source\":\" \",\"retrievedAtUtc\":\"2026-07-03T12:00:00Z\"}",
                SerializerOptions));
        Assert.ThrowsAny<ArgumentException>(() =>
            JsonSerializer.Deserialize<EvidenceDescriptor>(
                "{\"kind\":\"sampled-estimate\",\"modelVersion\":\"v1\",\"sampleCount\":0,\"seed\":1,\"assumptions\":[]}",
                SerializerOptions));
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
