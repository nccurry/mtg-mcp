using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies stored-field deck composition and caller-value numeric distributions.
/// </summary>
public sealed class DeckSummaryTests
{
    /// <summary>
    /// Identifies the first selected entry.
    /// </summary>
    private static readonly Guid FirstEntryId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    /// <summary>
    /// Identifies the second selected entry.
    /// </summary>
    private static readonly Guid SecondEntryId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <summary>
    /// Identifies the third selected entry with missing numeric evidence.
    /// </summary>
    private static readonly Guid ThirdEntryId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    /// <summary>
    /// Identifies the fourth selected entry.
    /// </summary>
    private static readonly Guid FourthEntryId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    /// <summary>
    /// Identifies the overlapping mana category.
    /// </summary>
    private static readonly Guid ManaCategoryId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    /// <summary>
    /// Identifies the overlapping draw category.
    /// </summary>
    private static readonly Guid DrawCategoryId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>
    /// Verifies zones, overlapping categories, printing fallbacks, numeric values, and partitions exactly.
    /// </summary>
    [Fact]
    public void CalculateDeckSummary_StoredFieldsAndCallerValuesAreExact()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        DeckSummaryRequest request = CreateRequest();

        DeckSummaryResult result = RequireExact(calculator.CalculateDeckSummary(request));

        Assert.Equal(4, result.EntryCount);
        Assert.Equal(7, result.TotalQuantity);
        Assert.Equal(
            ["commander:1", "main:2", "maybeboard:3", "sideboard:1"],
            result.Zones.Select(value => $"{value.Key}:{value.Quantity}"));
        Assert.Equal(3, Assert.Single(result.Categories, value => value.CategoryId == ManaCategoryId).Quantity);
        Assert.Equal(1, Assert.Single(result.Categories, value => value.CategoryId == DrawCategoryId).Quantity);
        Assert.Equal(4, result.Printings.Count);
        Assert.Contains(result.Printings, value => value.Key.StartsWith("printing:", StringComparison.Ordinal));
        Assert.Contains(result.Printings, value => value.Key.StartsWith("set:TST", StringComparison.Ordinal));
        Assert.Contains(result.Printings, value => value.Key.StartsWith("oracle:", StringComparison.Ordinal));
        Assert.Contains(result.Printings, value => value.Key == "unresolved-name:Commander");

        DeckNumericSeriesResult series = Assert.Single(result.NumericSeries);
        Assert.Equal("nearest-rank", series.PercentileMethod);
        Assert.Equal(3, series.IncludedEntryCount);
        Assert.Equal(4, series.IncludedQuantity);
        Assert.Equal(1, series.MissingEntryCount);
        Assert.Equal(3, series.MissingQuantity);
        Assert.Equal(new ExactFraction(7, 8), Fraction(series.Average!));
        Assert.Equal(["-1", "1", "5/2"], series.Histogram.Select(value => RationalText(value.Value)));
        Assert.Equal([1, 2, 3, 4], series.Percentiles.Select(value => value.Rank));
        Assert.Equal(["-1", "1", "1", "5/2"], series.Percentiles.Select(value => RationalText(value.Value)));

        DeckZonePartitionResult partition = Assert.IsType<DeckZonePartitionResult>(result.ZonePartition);
        Assert.Equal(3, partition.IncludedQuantity);
        Assert.Equal(1, partition.ExcludedQuantity);
        Assert.Equal(3, partition.UncoveredQuantity);
        Assert.Equal(partition.TotalQuantity, partition.IncludedQuantity + partition.ExcludedQuantity + partition.UncoveredQuantity);
        Assert.Equal("local-deck-composition", result.Derivation.FormulaId);
    }

    /// <summary>
    /// Verifies an all-missing numeric series returns explicit missing counts and no invented average.
    /// </summary>
    [Fact]
    public void CalculateDeckSummary_AllMissingValuesRemainMissing()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        DeckSummaryRequest request = CreateRequest() with
        {
            NumericSeries = [new DeckNumericSeriesInput("missing", [])],
            Percentiles = [50],
        };

        DeckNumericSeriesResult series = Assert.Single(
            RequireExact(calculator.CalculateDeckSummary(request)).NumericSeries);

        Assert.Equal(0, series.IncludedQuantity);
        Assert.Equal(7, series.MissingQuantity);
        Assert.Null(series.Average);
        Assert.Empty(series.Histogram);
        Assert.Empty(series.Percentiles);
    }

    /// <summary>
    /// Verifies malformed stored fields, values, percentiles, and partitions fail closed.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void CalculateDeckSummary_InvalidRequestReturnsInvalid(DeckSummaryRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        Assert.IsType<OperationInvalidInput>(calculator.CalculateDeckSummary(request).Value);
    }

    /// <summary>
    /// Supplies malformed deck summary requests.
    /// </summary>
    public static TheoryData<DeckSummaryRequest> InvalidRequests
    {
        get
        {
            DeckSummaryRequest valid = CreateRequest();
            return new TheoryData<DeckSummaryRequest>
            {
                valid with { DeckId = Guid.Empty },
                valid with { Revision = 0 },
                valid with { SelectedEntries = null! },
                valid with { ExcludedEntries = null! },
                valid with { SelectedEntries = [null!] },
                valid with { SelectedEntries = [valid.SelectedEntries[0] with { Quantity = 0 }] },
                valid with { SelectedEntries = [valid.SelectedEntries[0] with { Zone = " main " }] },
                valid with { SelectedEntries = [valid.SelectedEntries[0], valid.SelectedEntries[0]] },
                valid with { ExcludedEntries = [new StatisticsEntryEvidence(FirstEntryId, 1)] },
                valid with { Percentiles = null! },
                valid with { Percentiles = [0] },
                valid with { Percentiles = [50, 50] },
                valid with { NumericSeries = null! },
                valid with { NumericSeries = [null!] },
                valid with
                {
                    NumericSeries = [
                        new DeckNumericSeriesInput("same", []),
                        new DeckNumericSeriesInput("same", []),
                    ],
                },
                WithValue(valid, Guid.CreateVersion7(), "1"),
                WithValue(valid, FirstEntryId, "1e2"),
                WithValue(valid, FirstEntryId, ".5"),
                WithValue(valid, FirstEntryId, "1."),
                WithValue(valid, FirstEntryId, " 1"),
                valid with
                {
                    NumericSeries = [new DeckNumericSeriesInput(
                        "duplicate",
                        [new DeckNumericValueInput(FirstEntryId, "1"), new(FirstEntryId, "2")])],
                },
                valid with { ZonePartition = new DeckZonePartitionInput(["main"], ["main"]) },
                valid with { ZonePartition = new DeckZonePartitionInput(null!, []) },
            };
        }
    }

    /// <summary>
    /// Verifies population and request-wide work bounds return no partial summary.
    /// </summary>
    [Fact]
    public void CalculateDeckSummary_BoundsAndCancellationReturnNoPartialSummary()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        DeckSummaryRequest oversized = CreateRequest() with
        {
            SelectedEntries = [CreateRequest().SelectedEntries[0] with { Quantity = 1_001 }],
        };
        OperationSuccess<StatisticsCalculation<DeckSummaryResult>> populationBound =
            Assert.IsType<OperationSuccess<StatisticsCalculation<DeckSummaryResult>>>(
                calculator.CalculateDeckSummary(oversized).Value);
        Assert.IsType<StatisticsBoundedUnsupported>(populationBound.Data.Value);

        ExactStatisticsCalculator bounded = new("test-version", 1);
        OperationSuccess<StatisticsCalculation<DeckSummaryResult>> workBound =
            Assert.IsType<OperationSuccess<StatisticsCalculation<DeckSummaryResult>>>(
                bounded.CalculateDeckSummary(CreateRequest()).Value);
        Assert.IsType<StatisticsBoundedUnsupported>(workBound.Data.Value);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => calculator.CalculateDeckSummary(
            CreateRequest(),
            cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculateDeckSummary(null!));
    }

    /// <summary>
    /// Creates the canonical four-entry summary request.
    /// </summary>
    private static DeckSummaryRequest CreateRequest()
    {
        Guid printingId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        Guid oracleId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        StatisticsDeckCategoryInput mana = new(ManaCategoryId, "Mana");
        StatisticsDeckCategoryInput draw = new(DrawCategoryId, "Draw");
        return new DeckSummaryRequest(
            Guid.Parse("99999999-9999-4999-8999-999999999999"),
            7,
            [
                new StatisticsDeckEntryInput(
                    FirstEntryId, 2, "First", null, printingId, null, null, "en", "main", [mana]),
                new StatisticsDeckEntryInput(
                    SecondEntryId, 1, "Second", null, null, "TST", "2", "en", "sideboard", [mana, draw]),
                new StatisticsDeckEntryInput(
                    ThirdEntryId, 3, "Third", oracleId, null, null, null, "en", "maybeboard", []),
                new StatisticsDeckEntryInput(
                    FourthEntryId, 1, "Commander", null, null, null, null, "en", "commander", []),
            ],
            [new StatisticsEntryEvidence(Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"), 1)],
            [new DeckNumericSeriesInput(
                "mana-value",
                [
                    new DeckNumericValueInput(FirstEntryId, "1"),
                    new(SecondEntryId, "+2.5"),
                    new(FourthEntryId, "-1"),
                ])],
            [25, 50, 75, 100],
            new DeckZonePartitionInput(["commander", "main"], ["sideboard"]));
    }

    /// <summary>
    /// Replaces the numeric series with one supplied value.
    /// </summary>
    private static DeckSummaryRequest WithValue(
        DeckSummaryRequest request,
        Guid entryId,
        string value)
    {
        return request with
        {
            NumericSeries = [new DeckNumericSeriesInput(
                "value",
                [new DeckNumericValueInput(entryId, value)])],
        };
    }

    /// <summary>
    /// Extracts one exact deck summary result.
    /// </summary>
    private static DeckSummaryResult RequireExact(
        OperationResult<StatisticsCalculation<DeckSummaryResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<DeckSummaryResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<DeckSummaryResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<DeckSummaryResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Parses one public exact rational into the test rational type.
    /// </summary>
    private static ExactFraction Fraction(ExactRationalValue value)
    {
        return new ExactFraction(
            BigInteger.Parse(value.Numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(value.Denominator, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Formats one public rational for concise histogram assertions.
    /// </summary>
    private static string RationalText(ExactRationalValue value)
    {
        return value.Denominator == "1" ? value.Numerator : $"{value.Numerator}/{value.Denominator}";
    }
}
