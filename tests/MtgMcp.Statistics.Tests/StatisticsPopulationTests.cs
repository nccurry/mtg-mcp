using MtgMcp.Core.Results;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies population validation, exact group vocabulary, canonical ordering, and bounds.
/// </summary>
public sealed class StatisticsPopulationTests
{
    /// <summary>
    /// Verifies equivalent buckets merge and group/evidence ordering remains canonical.
    /// </summary>
    [Fact]
    public void Prepare_WithPermutedBuckets_ReturnsCanonicalPopulation()
    {
        StatisticsDeckSelectionEvidence evidence = new(
            Guid.CreateVersion7(),
            4,
            [new StatisticsEntryEvidence(Guid.CreateVersion7(), 2)],
            [new StatisticsEntryEvidence(Guid.CreateVersion7(), 1)]);
        StatisticsPopulation population = new(
            [
                new StatisticsPopulationBucket(2, ["b", "a"]),
                new StatisticsPopulationBucket(3, []),
                new StatisticsPopulationBucket(4, ["a", "b"]),
            ],
            ["b", "a"],
            evidence);

        CanonicalPopulation canonical = Assert.IsType<CanonicalPopulation>(
            StatisticsPopulationValidator.Prepare(population).Value);
        StatisticsPopulationSnapshot snapshot = canonical.ToSnapshot();

        Assert.Equal(9, canonical.TotalCount);
        Assert.Equal(["a", "b"], canonical.DeclaredGroups);
        Assert.Equal(6, canonical.CountGroup("a"));
        Assert.True(canonical.HasGroup("b"));
        Assert.False(canonical.HasGroup("missing"));
        Assert.Equal(2, snapshot.Buckets.Count);
        Assert.Equal(3, snapshot.Buckets[0].Count);
        Assert.Empty(snapshot.Buckets[0].Groups);
        Assert.Equal(6, snapshot.Buckets[1].Count);
        Assert.Same(evidence, snapshot.DeckEvidence);
    }

    /// <summary>
    /// Verifies malformed population shapes return sanitized invalid-input outcomes.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidPopulations))]
    public void Prepare_WithMalformedPopulation_ReturnsInvalid(StatisticsPopulation? population)
    {
        PopulationPreparation result = StatisticsPopulationValidator.Prepare(population);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
        Assert.Equal("invalid-statistics-population", invalid.ReasonCode);
        Assert.DoesNotContain("\\", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Supplies malformed population shapes for validation coverage.
    /// </summary>
    public static TheoryData<StatisticsPopulation?> InvalidPopulations => new()
    {
        null!,
        new StatisticsPopulation([], []),
        new StatisticsPopulation([new StatisticsPopulationBucket(1, [])], null!),
        new StatisticsPopulation([new StatisticsPopulationBucket(1, [])], [""]),
        new StatisticsPopulation([new StatisticsPopulationBucket(1, [])], [" padded "]),
        new StatisticsPopulation([new StatisticsPopulationBucket(1, [])], ["x", "x"]),
        new StatisticsPopulation([new StatisticsPopulationBucket(0, [])], []),
        new StatisticsPopulation([null!], []),
        new StatisticsPopulation([new StatisticsPopulationBucket(1, null!)], []),
        new StatisticsPopulation([new StatisticsPopulationBucket(1, ["missing"])], []),
        new StatisticsPopulation([new StatisticsPopulationBucket(1, ["x", "x"])], ["x"]),
    };

    /// <summary>
    /// Verifies population and group limits return structured bounded outcomes.
    /// </summary>
    [Fact]
    public void Prepare_OverConfiguredBounds_ReturnsStructuredLimit()
    {
        PopulationPreparation populationResult = StatisticsPopulationValidator.Prepare(
            new StatisticsPopulation([new StatisticsPopulationBucket(1_001, [])], []));
        PopulationPreparation groupResult = StatisticsPopulationValidator.Prepare(
            new StatisticsPopulation(
                [new StatisticsPopulationBucket(1, [])],
                ["a", "b", "c", "d", "e", "f", "g", "h", "i"]));
        PopulationPreparation overflowResult = StatisticsPopulationValidator.Prepare(
            new StatisticsPopulation(
                [
                    new StatisticsPopulationBucket(int.MaxValue, []),
                    new StatisticsPopulationBucket(1, []),
                ],
                []));

        AssertLimit(populationResult, "population", 1_001);
        AssertLimit(groupResult, "group-count", 0);
        AssertLimit(overflowResult, "population", int.MaxValue);
    }

    /// <summary>
    /// Asserts one prepared population contains a structured bounded result.
    /// </summary>
    private static void AssertLimit(
        PopulationPreparation preparation,
        string expectedKind,
        int expectedPopulation)
    {
        StatisticsBoundedUnsupported bounded = Assert.IsType<StatisticsBoundedUnsupported>(
            preparation.Value);
        Assert.Equal(expectedKind, bounded.Limit.LimitKind);
        Assert.Equal(expectedPopulation, bounded.Limit.Population);
        Assert.NotEmpty(bounded.Limit.ReductionOptions);
    }
}
