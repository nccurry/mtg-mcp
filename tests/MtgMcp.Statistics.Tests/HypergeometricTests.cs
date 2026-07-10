using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies exact univariate probability behavior against independent labeled-card enumeration.
/// </summary>
public sealed class HypergeometricTests
{
    /// <summary>
    /// Verifies a realistic 99-card opening-hand result against direct combinations.
    /// </summary>
    [Fact]
    public void CalculateHypergeometric_RealisticOpeningHandMatchesDirectFormula()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        HypergeometricRequest request = Request(
            population: 99,
            successes: 36,
            drawCount: 7,
            new HypergeometricAtLeastEvent(3));

        HypergeometricResult result = RequireExact(calculator.CalculateHypergeometric(request));
        BigInteger expectedNumerator = BigInteger.Zero;
        for (int lands = 3; lands <= 7; lands++)
        {
            expectedNumerator += Choose(36, lands) * Choose(63, 7 - lands);
        }

        ExactFraction expected = new(expectedNumerator, Choose(99, 7));
        Assert.Equal(expected.Numerator.ToString(CultureInfo.InvariantCulture), result.Probability.Numerator);
        Assert.Equal(expected.Denominator.ToString(CultureInfo.InvariantCulture), result.Probability.Denominator);
        Assert.Equal("hypergeometric-range", result.Derivation.FormulaId);
        Assert.Equal("exact-v1", result.Derivation.CalculationVersion);
        Assert.Equal("test-version", result.Derivation.ImplementationVersion);
        Assert.Equal("exact-derivation", result.Derivation.Evidence.Kind);
        Assert.Equal(36, result.SuccessCount);
        Assert.Equal("at-least", result.Event.Kind);
        Assert.Equal("1", new ExactFraction(
            BigInteger.Parse(result.Probability.Numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(result.Probability.Denominator, CultureInfo.InvariantCulture))
            .Add(new ExactFraction(
                BigInteger.Parse(result.Complement.Numerator, CultureInfo.InvariantCulture),
                BigInteger.Parse(result.Complement.Denominator, CultureInfo.InvariantCulture)))
            .Numerator.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Verifies every small exact-success mass against enumerated labeled hands.
    /// </summary>
    [Fact]
    public void CalculateHypergeometric_AllSmallMassesMatchLabeledEnumeration()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        for (int population = 1; population <= 8; population++)
        {
            for (int successes = 0; successes <= population; successes++)
            {
                for (int draws = 0; draws <= population; draws++)
                {
                    AssertAllTargetMasses(calculator, population, successes, draws);
                }
            }
        }
    }

    /// <summary>
    /// Verifies all closed event variants and expectation/variance boundaries.
    /// </summary>
    [Fact]
    public void CalculateHypergeometric_AllEventVariantsReturnExactValues()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        HypergeometricResult zero = RequireExact(calculator.CalculateHypergeometric(
            Request(10, 4, 3, new HypergeometricZeroEvent())));
        HypergeometricResult atMost = RequireExact(calculator.CalculateHypergeometric(
            Request(10, 4, 3, new HypergeometricAtMostEvent(1))));
        HypergeometricResult range = RequireExact(calculator.CalculateHypergeometric(
            Request(10, 4, 3, new HypergeometricRangeEvent(1, 2))));
        HypergeometricResult impossible = RequireExact(calculator.CalculateHypergeometric(
            Request(10, 4, 3, new HypergeometricExactlyEvent(9))));
        HypergeometricResult oneCard = RequireExact(calculator.CalculateHypergeometric(
            Request(1, 1, 1, new HypergeometricAtLeastEvent(1))));

        Assert.Equal("zero", zero.Event.Kind);
        Assert.Equal("at-most", atMost.Event.Kind);
        Assert.Equal("range", range.Event.Kind);
        Assert.Equal("0", impossible.Probability.Numerator);
        Assert.Equal("1", oneCard.Probability.Numerator);
        Assert.Equal("0", oneCard.Variance.Numerator);
        Assert.Equal("6/5", $"{range.Expectation.Numerator}/{range.Expectation.Denominator}");
    }

    /// <summary>
    /// Verifies malformed requests return common invalid-input outcomes.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void CalculateHypergeometric_WithMalformedInputReturnsInvalid(HypergeometricRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(
            calculator.CalculateHypergeometric(request).Value);

        Assert.Equal("invalid-statistics-request", invalid.ReasonCode);
    }

    /// <summary>
    /// Supplies malformed univariate requests.
    /// </summary>
    public static TheoryData<HypergeometricRequest> InvalidRequests => new()
    {
        Request(10, 4, -1, new HypergeometricZeroEvent()),
        Request(10, 4, 11, new HypergeometricZeroEvent()),
        Request(10, 4, 3, new HypergeometricExactlyEvent(-1)),
        Request(10, 4, 3, new HypergeometricAtLeastEvent(-1)),
        Request(10, 4, 3, new HypergeometricAtMostEvent(-1)),
        Request(10, 4, 3, new HypergeometricRangeEvent(2, 1)),
        Request(10, 4, 3, null!),
        Request(10, 4, 3, new HypergeometricZeroEvent()) with { SuccessGroup = "missing" },
        Request(10, 4, 3, new HypergeometricZeroEvent()) with { SuccessGroup = " success " },
    };

    /// <summary>
    /// Verifies canonical population permutations and cultures produce byte-equivalent results.
    /// </summary>
    [Fact]
    public void CalculateHypergeometric_PermutationAndCultureDoNotChangeResult()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        HypergeometricRequest firstRequest = new(
            new StatisticsPopulation(
                [
                    new StatisticsPopulationBucket(6, []),
                    new StatisticsPopulationBucket(2, ["success"]),
                    new StatisticsPopulationBucket(2, ["success"]),
                ],
                ["success"]),
            "success",
            3,
            new HypergeometricAtLeastEvent(1));
        HypergeometricRequest secondRequest = firstRequest with
        {
            Population = new StatisticsPopulation(
                [
                    new StatisticsPopulationBucket(4, ["success"]),
                    new StatisticsPopulationBucket(6, []),
                ],
                ["success"]),
        };
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string first = System.Text.Json.JsonSerializer.Serialize(
                calculator.CalculateHypergeometric(firstRequest));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            string second = System.Text.Json.JsonSerializer.Serialize(
                calculator.CalculateHypergeometric(secondRequest));
            Assert.Equal(first, second);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// Verifies cancellation and a test-sized work budget return no partial exact result.
    /// </summary>
    [Fact]
    public void CalculateHypergeometric_CancellationAndBudgetStopBeforeResult()
    {
        ExactStatisticsCalculator boundedCalculator = new("test-version", workLimit: 1);
        OperationSuccess<StatisticsCalculation<HypergeometricResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<HypergeometricResult>>>(
                boundedCalculator.CalculateHypergeometric(
                    Request(20, 10, 10, new HypergeometricRangeEvent(0, 10))).Value);
        StatisticsBoundedUnsupported bounded = Assert.IsType<StatisticsBoundedUnsupported>(
            success.Data.Value);
        Assert.Equal("work-units", bounded.Limit.LimitKind);
        Assert.Equal(11, bounded.Limit.EstimatedWork);

        ExactStatisticsCalculator calculator = new("test-version");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => calculator.CalculateHypergeometric(
            Request(20, 10, 10, new HypergeometricAtLeastEvent(1)),
            cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculateHypergeometric(null!));
        Assert.Throws<ArgumentException>(() => new ExactStatisticsCalculator(" "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExactStatisticsCalculator("test-version", 0));
    }

    /// <summary>
    /// Creates one canonical two-bucket population request.
    /// </summary>
    private static HypergeometricRequest Request(
        int population,
        int successes,
        int drawCount,
        HypergeometricEventInput eventInput)
    {
        List<StatisticsPopulationBucket> buckets = [];
        if (successes > 0)
        {
            buckets.Add(new StatisticsPopulationBucket(successes, ["success"]));
        }

        if (population - successes > 0)
        {
            buckets.Add(new StatisticsPopulationBucket(population - successes, []));
        }

        return new HypergeometricRequest(
            new StatisticsPopulation(buckets, ["success"]),
            "success",
            drawCount,
            eventInput);
    }

    /// <summary>
    /// Compares every exact-success target for one small population and draw size.
    /// </summary>
    private static void AssertAllTargetMasses(
        ExactStatisticsCalculator calculator,
        int population,
        int successes,
        int draws)
    {
        for (int target = 0; target <= draws; target++)
        {
            HypergeometricResult result = RequireExact(calculator.CalculateHypergeometric(
                Request(population, successes, draws, new HypergeometricExactlyEvent(target))));
            (BigInteger favorable, BigInteger total) = EnumerateHands(
                population,
                successes,
                draws,
                target);
            ExactFraction expected = new(favorable, total);

            Assert.Equal(
                expected.Numerator.ToString(CultureInfo.InvariantCulture),
                result.Probability.Numerator);
            Assert.Equal(
                expected.Denominator.ToString(CultureInfo.InvariantCulture),
                result.Probability.Denominator);
        }
    }

    /// <summary>
    /// Extracts one exact calculation or fails with the active outcome case.
    /// </summary>
    private static HypergeometricResult RequireExact(
        OperationResult<StatisticsCalculation<HypergeometricResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<HypergeometricResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<HypergeometricResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<HypergeometricResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Enumerates labeled-card subsets independently of the production combination cache.
    /// </summary>
    private static (BigInteger Favorable, BigInteger Total) EnumerateHands(
        int population,
        int successes,
        int draws,
        int target)
    {
        BigInteger favorable = BigInteger.Zero;
        BigInteger total = BigInteger.Zero;
        int maximumMask = 1 << population;
        for (int mask = 0; mask < maximumMask; mask++)
        {
            if (BitOperations.PopCount((uint)mask) != draws)
            {
                continue;
            }

            total++;
            int successMask = successes == 0 ? 0 : (1 << successes) - 1;
            if (BitOperations.PopCount((uint)(mask & successMask)) == target)
            {
                favorable++;
            }
        }

        return (favorable, total);
    }

    /// <summary>
    /// Computes a direct test-only binomial coefficient.
    /// </summary>
    private static BigInteger Choose(int population, int selected)
    {
        if (selected < 0 || selected > population)
        {
            return BigInteger.Zero;
        }

        int count = Math.Min(selected, population - selected);
        BigInteger result = BigInteger.One;
        for (int index = 1; index <= count; index++)
        {
            result = (result * (population - count + index)) / index;
        }

        return result;
    }
}
