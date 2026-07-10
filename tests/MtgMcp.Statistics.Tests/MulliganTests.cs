using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies explicit independent mulligan attempts and deterministic caller bottoming.
/// </summary>
public sealed class MulliganTests
{
    /// <summary>
    /// Stores the explicit land group used by small exhaustive fixtures.
    /// </summary>
    private static readonly string[] LandGroup = ["land"];

    /// <summary>
    /// Stores the population remainder with no observed group.
    /// </summary>
    private static readonly string[] NoGroups = [];

    /// <summary>
    /// Verifies attempt reach, forced keep, bottom priority, and final event probabilities exactly.
    /// </summary>
    [Fact]
    public void CalculateMulligan_ExplicitScheduleMatchesIndependentDerivation()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        MulliganRequest request = new(
            Population(),
            [new MulliganAttemptInput(2, 0, false), new(2, 1, true)],
            [new StatisticsGroupConditionInput("land", 1)],
            ["land"],
            [new StatisticsGroupConditionInput("land", 1)]);

        MulliganResult result = RequireExact(calculator.CalculateMulligan(request));

        Assert.Equal(2, result.Attempts.Count);
        Assert.Equal(new ExactFraction(5, 6), Fraction(result.Attempts[0].ConditionalKeepProbability));
        Assert.Equal(ExactFraction.One, Fraction(result.Attempts[0].ReachProbability));
        Assert.Equal(new ExactFraction(1, 6), Fraction(result.Attempts[1].ReachProbability));
        Assert.Equal(new ExactFraction(1, 6), Fraction(result.Attempts[1].KeepProbability));
        Assert.Equal(ExactFraction.Zero, Fraction(result.NoKeepProbability));
        Assert.Equal(new ExactFraction(31, 36), Fraction(result.FinalEventProbability!));
        Assert.Equal("explicit-mulligan-schedule", result.Derivation.FormulaId);
    }

    /// <summary>
    /// Verifies canonical fallback bottoming and an unforced final attempt remain explicit.
    /// </summary>
    [Fact]
    public void CalculateMulligan_CanonicalFallbackAndNoKeepRemainExact()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        MulliganResult fallback = RequireExact(calculator.CalculateMulligan(new MulliganRequest(
            Population(),
            [new MulliganAttemptInput(2, 1, true)],
            [],
            [],
            [new StatisticsGroupConditionInput("land", 1)])));
        MulliganResult unforced = RequireExact(calculator.CalculateMulligan(new MulliganRequest(
            Population(),
            [new MulliganAttemptInput(2, 0, false)],
            [new StatisticsGroupConditionInput("land", 2)],
            [],
            null)));

        Assert.Equal(new ExactFraction(5, 6), Fraction(fallback.FinalEventProbability!));
        Assert.Equal(new ExactFraction(5, 6), Fraction(unforced.NoKeepProbability));
        Assert.Null(unforced.FinalEventProbability);
    }

    /// <summary>
    /// Verifies malformed attempt, condition, and priority inputs fail before enumeration.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void CalculateMulligan_InvalidRequestReturnsInvalid(MulliganRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        Assert.IsType<OperationInvalidInput>(calculator.CalculateMulligan(request).Value);
    }

    /// <summary>
    /// Supplies malformed mulligan requests.
    /// </summary>
    public static TheoryData<MulliganRequest> InvalidRequests => new()
    {
        Request(null!, [], []),
        Request([], [], []),
        Request(Enumerable.Repeat(new MulliganAttemptInput(2, 0, false), 9).ToArray(), [], []),
        Request([null!], [], []),
        Request([new MulliganAttemptInput(-1, 0, false)], [], []),
        Request([new MulliganAttemptInput(5, 0, false)], [], []),
        Request([new MulliganAttemptInput(2, -1, false)], [], []),
        Request([new MulliganAttemptInput(2, 3, false)], [], []),
        Request([new MulliganAttemptInput(2, 0, true), new(2, 1, true)], [], []),
        Request([new MulliganAttemptInput(2, 0, false)], null!, []),
        Request([new MulliganAttemptInput(2, 0, false)], [new("missing", 1)], []),
        Request([new MulliganAttemptInput(2, 0, false)], [], null!),
        Request([new MulliganAttemptInput(2, 0, false)], [], ["missing"]),
        Request([new MulliganAttemptInput(2, 0, false)], [], ["land", "land"]),
        Request([new MulliganAttemptInput(2, 0, false)], [], [], []),
    };

    /// <summary>
    /// Verifies request-wide budget, cancellation, and null roots return no partial attempt rows.
    /// </summary>
    [Fact]
    public void CalculateMulligan_BudgetCancellationAndNullStopClosed()
    {
        ExactStatisticsCalculator boundedCalculator = new("test-version", 1);
        OperationSuccess<StatisticsCalculation<MulliganResult>> bounded =
            Assert.IsType<OperationSuccess<StatisticsCalculation<MulliganResult>>>(
                boundedCalculator.CalculateMulligan(new MulliganRequest(
                    Population(),
                    [new MulliganAttemptInput(2, 0, false), new(2, 1, true)],
                    [new StatisticsGroupConditionInput("land", 1)],
                    [],
                    null)).Value);
        Assert.IsType<StatisticsBoundedUnsupported>(bounded.Data.Value);

        ExactStatisticsCalculator calculator = new("test-version");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => calculator.CalculateMulligan(
            new MulliganRequest(
                Population(),
                [new MulliganAttemptInput(2, 0, false)],
                [new StatisticsGroupConditionInput("land", 1)],
                [],
                null),
            cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculateMulligan(null!));
    }

    /// <summary>
    /// Creates the four-card, two-land disjoint population.
    /// </summary>
    private static StatisticsPopulation Population()
    {
        return new StatisticsPopulation(
            [
                new StatisticsPopulationBucket(2, LandGroup),
                new StatisticsPopulationBucket(2, NoGroups),
            ],
            LandGroup);
    }

    /// <summary>
    /// Creates a request with optional explicit final conditions.
    /// </summary>
    private static MulliganRequest Request(
        IReadOnlyList<MulliganAttemptInput> attempts,
        IReadOnlyList<StatisticsGroupConditionInput> keep,
        IReadOnlyList<string> bottom,
        IReadOnlyList<StatisticsGroupConditionInput>? final = null)
    {
        return new MulliganRequest(Population(), attempts, keep, bottom, final);
    }

    /// <summary>
    /// Extracts one exact mulligan result.
    /// </summary>
    private static MulliganResult RequireExact(
        OperationResult<StatisticsCalculation<MulliganResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<MulliganResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<MulliganResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<MulliganResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Parses one public exact probability into the test rational type.
    /// </summary>
    private static ExactFraction Fraction(ExactProbability probability)
    {
        return new ExactFraction(
            BigInteger.Parse(probability.Numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(probability.Denominator, CultureInfo.InvariantCulture));
    }
}
