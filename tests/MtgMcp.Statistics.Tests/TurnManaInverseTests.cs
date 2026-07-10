using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies explicit turn schedules, one-unit mana allocation, and closed inverse count solving.
/// </summary>
public sealed class TurnManaInverseTests
{
    /// <summary>
    /// Stores no observed groups for population remainder buckets.
    /// </summary>
    private static readonly string[] NoGroups = [];

    /// <summary>
    /// Stores the success group used by univariate and turn fixtures.
    /// </summary>
    private static readonly string[] SuccessGroup = ["success"];

    /// <summary>
    /// Stores exact white source capability.
    /// </summary>
    private static readonly string[] WhiteCapability = ["W"];

    /// <summary>
    /// Stores exact red source capability.
    /// </summary>
    private static readonly string[] RedCapability = ["R"];

    /// <summary>
    /// Stores exact white/red flexible source capabilities.
    /// </summary>
    private static readonly string[] WhiteRedCapabilities = ["W", "R"];

    /// <summary>
    /// Stores exact colorless source capability.
    /// </summary>
    private static readonly string[] ColorlessCapability = ["C"];

    /// <summary>
    /// Stores labeled source capability masks used by the independent mana oracle.
    /// </summary>
    private static readonly int[] ManaOracleCards = [1, 8, 9, 32, 0, 0];

    /// <summary>
    /// Stores W, R, and generic payment masks for the independent oracle.
    /// </summary>
    private static readonly int[] ManaOraclePayment = [1, 8, 63];

    /// <summary>
    /// Verifies only caller-supplied draw rows affect exact probability-by-turn results.
    /// </summary>
    [Fact]
    public void CalculateTurnTable_UsesOnlyExplicitDrawSchedule()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        StatisticsPopulation population = SuccessPopulation(99, 36);
        TurnTableRequest request = new(
            population,
            "success",
            7,
            [new TurnDrawInput(1, 0), new(2, 1), new(3, 2)],
            new HypergeometricAtLeastEvent(3));

        TurnTableResult result = RequireTurn(calculator.CalculateTurnTable(request));

        Assert.Equal([7, 8, 10], result.Rows.Select(value => value.CardsSeen));
        Assert.Equal([0, 1, 2], result.Rows.Select(value => value.Draws));
        Assert.Equal(
            DirectHypergeometric(99, 36, 7, 3),
            Fraction(result.Rows[0].Probability));
        Assert.Equal(
            DirectHypergeometric(99, 36, 10, 3),
            Fraction(result.Rows[2].Probability));
        foreach (TurnTableRow row in result.Rows)
        {
            Assert.Equal(ExactFraction.One, Fraction(row.Probability).Add(Fraction(row.Complement)));
        }

        Assert.Equal("explicit-turn-hypergeometric", result.Derivation.FormulaId);
    }

    /// <summary>
    /// Verifies malformed explicit schedules and events fail before returning a table.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidTurnRequests))]
    public void CalculateTurnTable_InvalidRequestReturnsInvalid(TurnTableRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        Assert.IsType<OperationInvalidInput>(calculator.CalculateTurnTable(request).Value);
    }

    /// <summary>
    /// Supplies malformed explicit turn requests.
    /// </summary>
    public static TheoryData<TurnTableRequest> InvalidTurnRequests => new()
    {
        TurnRequest(-1, [new TurnDrawInput(1, 0)]),
        TurnRequest(11, [new TurnDrawInput(1, 0)]),
        TurnRequest(7, null!),
        TurnRequest(7, []),
        TurnRequest(7, [new TurnDrawInput(0, 1)]),
        TurnRequest(7, [new TurnDrawInput(1, 1), new(1, 1)]),
        TurnRequest(7, [new TurnDrawInput(1, -1)]),
        TurnRequest(7, [new TurnDrawInput(1, 4)]),
        TurnRequest(7, Enumerable.Range(1, 51).Select(turn => new TurnDrawInput(turn, 0)).ToArray()),
        TurnRequest(7, [new TurnDrawInput(1, 0)]) with { SuccessGroup = "missing" },
        TurnRequest(7, [new TurnDrawInput(1, 0)]) with { Event = null! },
    };

    /// <summary>
    /// Verifies flexible, colorless, and generic source allocation against a labeled-card oracle.
    /// </summary>
    [Fact]
    public void CalculateManaAvailability_FlexibleSourcesMatchIndependentOracle()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        StatisticsPopulation population = new(
            [
                new StatisticsPopulationBucket(1, ["w-source"]),
                new StatisticsPopulationBucket(1, ["r-source"]),
                new StatisticsPopulationBucket(1, ["dual-source"]),
                new StatisticsPopulationBucket(1, ["c-source"]),
                new StatisticsPopulationBucket(2, NoGroups),
            ],
            ["c-source", "dual-source", "r-source", "w-source"]);
        ManaAvailabilityRequest request = new(
            population,
            3,
            [
                new ManaSourceInput("w-source", WhiteCapability),
                new ManaSourceInput("r-source", RedCapability),
                new ManaSourceInput("dual-source", WhiteRedCapabilities),
                new ManaSourceInput("c-source", ColorlessCapability),
            ],
            new ManaRequirementInput(White: 1, Red: 1, Generic: 1),
            MaximumUsableSources: 3);

        ManaAvailabilityResult result = RequireMana(calculator.CalculateManaAvailability(request));
        (BigInteger favorable, BigInteger total) = EnumerateManaHands(
            ManaOracleCards,
            drawCount: 3,
            ManaOraclePayment,
            maximumUsableSources: 3);
        ExactFraction expected = new(favorable, total);

        Assert.Equal(expected, Fraction(result.Probability));
        Assert.Equal(ExactFraction.One, Fraction(result.Probability).Add(Fraction(result.Complement)));
        Assert.Equal(
            ["c-source", "dual-source", "r-source", "w-source"],
            result.Sources.Select(value => value.Group));
        Assert.Equal("mana-capacity-allocation", result.Derivation.FormulaId);
    }

    /// <summary>
    /// Verifies zero requirements, source caps, and colorless specificity remain mechanical.
    /// </summary>
    [Fact]
    public void CalculateManaAvailability_HandlesZeroCapAndColorlessExactly()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(1, ["white"]), new StatisticsPopulationBucket(1, ["colorless"])],
            ["colorless", "white"]);
        ManaAvailabilityResult zero = RequireMana(calculator.CalculateManaAvailability(
            new ManaAvailabilityRequest(population, 1, [], new ManaRequirementInput(), 0)));
        ManaAvailabilityResult capped = RequireMana(calculator.CalculateManaAvailability(
            new ManaAvailabilityRequest(
                population,
                2,
                [new ManaSourceInput("white", WhiteCapability), new("colorless", ColorlessCapability)],
                new ManaRequirementInput(White: 1, Colorless: 1),
                1)));
        ManaAvailabilityResult colorless = RequireMana(calculator.CalculateManaAvailability(
            new ManaAvailabilityRequest(
                population,
                1,
                [new ManaSourceInput("white", WhiteCapability), new("colorless", ColorlessCapability)],
                new ManaRequirementInput(Colorless: 1),
                1)));

        Assert.Equal(ExactFraction.One, Fraction(zero.Probability));
        Assert.Equal(ExactFraction.Zero, Fraction(capped.Probability));
        Assert.Equal(new ExactFraction(1, 2), Fraction(colorless.Probability));
    }

    /// <summary>
    /// Verifies malformed mana requests fail before allocation or probability work.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidManaRequests))]
    public void CalculateManaAvailability_InvalidRequestReturnsInvalid(ManaAvailabilityRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        Assert.IsType<OperationInvalidInput>(calculator.CalculateManaAvailability(request).Value);
    }

    /// <summary>
    /// Supplies malformed mana requests.
    /// </summary>
    public static TheoryData<ManaAvailabilityRequest> InvalidManaRequests => new()
    {
        ManaRequest(-1, [], new ManaRequirementInput(), 0),
        ManaRequest(11, [], new ManaRequirementInput(), 0),
        ManaRequest(1, null!, new ManaRequirementInput(), 0),
        ManaRequest(1, [], null!, 0),
        ManaRequest(1, [], new ManaRequirementInput(), -1),
        ManaRequest(1, [], new ManaRequirementInput(), 11),
        ManaRequest(1, [null!], new ManaRequirementInput(), 1),
        ManaRequest(1, [new ManaSourceInput("missing", WhiteCapability)], new ManaRequirementInput(), 1),
        ManaRequest(1, [new ManaSourceInput("source", [])], new ManaRequirementInput(), 1),
        ManaRequest(1, [new ManaSourceInput("source", ["X"])], new ManaRequirementInput(), 1),
        ManaRequest(1, [new ManaSourceInput("source", ["W", "W"])], new ManaRequirementInput(), 1),
        ManaRequest(
            1,
            [new ManaSourceInput("source", WhiteCapability), new("source", RedCapability)],
            new ManaRequirementInput(),
            1),
        ManaRequest(1, [], new ManaRequirementInput(White: -1), 1),
        ManaRequest(1, [], new ManaRequirementInput(White: int.MaxValue, Blue: int.MaxValue), 1),
        new ManaAvailabilityRequest(
            new StatisticsPopulation(
                [new StatisticsPopulationBucket(1, ["first", "second"])],
                ["first", "second"]),
            1,
            [new ManaSourceInput("first", WhiteCapability), new("second", RedCapability)],
            new ManaRequirementInput(),
            1),
    };

    /// <summary>
    /// Verifies the inverse hypergeometric solver returns the first passing count and neighbor proof.
    /// </summary>
    [Fact]
    public void CalculateMinimumCount_HypergeometricReturnsExactNeighborProof()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        MinimumCountRequest request = new(
            new MinimumHypergeometricCountEvent(99, 7, 1),
            "1",
            "2",
            0,
            99);

        MinimumCountResult result = RequireMinimum(calculator.CalculateMinimumCount(request));

        Assert.True(result.Found);
        Assert.NotNull(result.Count);
        Assert.NotNull(result.PreviousCount);
        Assert.True(Fraction(result.Probability!).CompareTo(new ExactFraction(1, 2)) >= 0);
        Assert.True(Fraction(result.PreviousProbability!).CompareTo(new ExactFraction(1, 2)) < 0);
        Assert.Equal(result.Count - 1, result.PreviousCount);
        Assert.Equal("hypergeometric-at-least", result.EventKind);
    }

    /// <summary>
    /// Verifies lower bounds, zero/one targets, no solution, explicit turns, and repeated sources.
    /// </summary>
    [Fact]
    public void CalculateMinimumCount_AllClosedCasesHandleBoundaries()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        MinimumCountResult zero = RequireMinimum(calculator.CalculateMinimumCount(new MinimumCountRequest(
            new MinimumHypergeometricCountEvent(10, 3, 1),
            "0",
            "1",
            4,
            10)));
        MinimumCountResult one = RequireMinimum(calculator.CalculateMinimumCount(new MinimumCountRequest(
            new MinimumHypergeometricCountEvent(10, 10, 1),
            "1",
            "1",
            0,
            10)));
        MinimumCountResult none = RequireMinimum(calculator.CalculateMinimumCount(new MinimumCountRequest(
            new MinimumHypergeometricCountEvent(10, 3, 3),
            "1",
            "1",
            0,
            2)));
        MinimumCountResult turn = RequireMinimum(calculator.CalculateMinimumCount(new MinimumCountRequest(
            new MinimumTurnCountEvent(10, 2, [new TurnDrawInput(1, 0), new(3, 2)], 3, 1),
            "1",
            "2",
            0,
            10)));
        MinimumCountResult mana = RequireMinimum(calculator.CalculateMinimumCount(new MinimumCountRequest(
            new MinimumManaCountEvent(
                10,
                3,
                WhiteCapability,
                new ManaRequirementInput(White: 1),
                1),
            "1",
            "2",
            0,
            10)));

        Assert.Equal(4, zero.Count);
        Assert.Equal(1, one.Count);
        Assert.False(none.Found);
        Assert.Null(none.Count);
        Assert.Equal(2, none.HighestTestedCount);
        Assert.Equal("turn-at-least", turn.EventKind);
        Assert.Equal("mana-availability", mana.EventKind);
        Assert.True(Fraction(mana.Probability!).CompareTo(new ExactFraction(1, 2)) >= 0);
    }

    /// <summary>
    /// Verifies malformed inverse targets, events, and ranges fail closed.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidMinimumRequests))]
    public void CalculateMinimumCount_InvalidRequestReturnsInvalid(MinimumCountRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        Assert.IsType<OperationInvalidInput>(calculator.CalculateMinimumCount(request).Value);
    }

    /// <summary>
    /// Supplies malformed inverse count requests.
    /// </summary>
    public static TheoryData<MinimumCountRequest> InvalidMinimumRequests => new()
    {
        MinimumRequest(null!, "1", "2", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(0, 0, 0), "1", "2", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 11, 1), "1", "2", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, -1), "1", "2", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, 1), "bad", "2", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, 1), "1", "0", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, 1), "-1", "2", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, 1), "3", "2", 0, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, 1), "1", "2", -1, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, 1), "1", "2", 2, 1),
        MinimumRequest(new MinimumHypergeometricCountEvent(10, 1, 1), "1", "2", 0, 11),
        MinimumRequest(
            new MinimumTurnCountEvent(10, 2, [new TurnDrawInput(1, 1)], 2, 1),
            "1",
            "2",
            0,
            10),
        MinimumRequest(
            new MinimumManaCountEvent(10, 3, ["X"], new ManaRequirementInput(), 1),
            "1",
            "2",
            0,
            10),
    };

    /// <summary>
    /// Verifies composed budgeting, cancellation, and null roots return no partial inverse result.
    /// </summary>
    [Fact]
    public void PhaseThreeOperations_BudgetCancellationAndNullStopClosed()
    {
        ExactStatisticsCalculator bounded = new("test-version", 1);
        OperationSuccess<StatisticsCalculation<TurnTableResult>> turn =
            Assert.IsType<OperationSuccess<StatisticsCalculation<TurnTableResult>>>(
                bounded.CalculateTurnTable(TurnRequest(
                    7,
                    [new TurnDrawInput(1, 0), new(2, 1)])).Value);
        Assert.IsType<StatisticsBoundedUnsupported>(turn.Data.Value);
        OperationSuccess<StatisticsCalculation<MinimumCountResult>> minimum =
            Assert.IsType<OperationSuccess<StatisticsCalculation<MinimumCountResult>>>(
                bounded.CalculateMinimumCount(new MinimumCountRequest(
                    new MinimumHypergeometricCountEvent(10, 3, 1),
                    "1",
                    "2",
                    0,
                    10)).Value);
        Assert.IsType<StatisticsBoundedUnsupported>(minimum.Data.Value);

        ExactStatisticsCalculator calculator = new("test-version");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => calculator.CalculateManaAvailability(
            ManaRequest(
                2,
                [new ManaSourceInput("source", WhiteCapability)],
                new ManaRequirementInput(White: 1),
                1),
            cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => calculator.CalculateMinimumCount(
            new MinimumCountRequest(
                new MinimumHypergeometricCountEvent(10, 3, 1),
                "1",
                "2",
                0,
                10),
            cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculateTurnTable(null!));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculateManaAvailability(null!));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculateMinimumCount(null!));
    }

    /// <summary>
    /// Creates a standard ten-card explicit turn request.
    /// </summary>
    private static TurnTableRequest TurnRequest(
        int openingHandSize,
        IReadOnlyList<TurnDrawInput> schedule)
    {
        return new TurnTableRequest(
            SuccessPopulation(10, 4),
            "success",
            openingHandSize,
            schedule,
            new HypergeometricAtLeastEvent(1));
    }

    /// <summary>
    /// Creates a standard ten-card mana request with one declared source group.
    /// </summary>
    private static ManaAvailabilityRequest ManaRequest(
        int drawCount,
        IReadOnlyList<ManaSourceInput> sources,
        ManaRequirementInput requirement,
        int maximumUsableSources)
    {
        return new ManaAvailabilityRequest(
            new StatisticsPopulation(
                [
                    new StatisticsPopulationBucket(4, ["source"]),
                    new StatisticsPopulationBucket(6, NoGroups),
                ],
                ["source"]),
            drawCount,
            sources,
            requirement,
            maximumUsableSources);
    }

    /// <summary>
    /// Creates one inverse request from explicit values.
    /// </summary>
    private static MinimumCountRequest MinimumRequest(
        MinimumCountEventInput eventInput,
        string numerator,
        string denominator,
        int minimum,
        int maximum)
    {
        return new MinimumCountRequest(eventInput, numerator, denominator, minimum, maximum);
    }

    /// <summary>
    /// Creates a canonical two-bucket success population.
    /// </summary>
    private static StatisticsPopulation SuccessPopulation(int population, int successes)
    {
        List<StatisticsPopulationBucket> buckets = [];
        if (successes > 0)
        {
            buckets.Add(new StatisticsPopulationBucket(successes, SuccessGroup));
        }

        if (population - successes > 0)
        {
            buckets.Add(new StatisticsPopulationBucket(population - successes, NoGroups));
        }

        return new StatisticsPopulation(buckets, SuccessGroup);
    }

    /// <summary>
    /// Extracts one exact turn-table result.
    /// </summary>
    private static TurnTableResult RequireTurn(
        OperationResult<StatisticsCalculation<TurnTableResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<TurnTableResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<TurnTableResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<TurnTableResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Extracts one exact mana result.
    /// </summary>
    private static ManaAvailabilityResult RequireMana(
        OperationResult<StatisticsCalculation<ManaAvailabilityResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<ManaAvailabilityResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<ManaAvailabilityResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<ManaAvailabilityResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Extracts one exact minimum-count result.
    /// </summary>
    private static MinimumCountResult RequireMinimum(
        OperationResult<StatisticsCalculation<MinimumCountResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<MinimumCountResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<MinimumCountResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<MinimumCountResult>>(success.Data.Value).Data;
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

    /// <summary>
    /// Computes one direct at-least hypergeometric vector for a test oracle.
    /// </summary>
    private static ExactFraction DirectHypergeometric(
        int population,
        int successes,
        int draws,
        int minimum)
    {
        BigInteger numerator = BigInteger.Zero;
        for (int count = minimum; count <= draws; count++)
        {
            numerator += Choose(successes, count) * Choose(population - successes, draws - count);
        }

        return new ExactFraction(numerator, Choose(population, draws));
    }

    /// <summary>
    /// Enumerates labeled hands and distinct-source payment assignments independently.
    /// </summary>
    private static (BigInteger Favorable, BigInteger Total) EnumerateManaHands(
        int[] cards,
        int drawCount,
        int[] paymentMasks,
        int maximumUsableSources)
    {
        BigInteger favorable = BigInteger.Zero;
        BigInteger total = BigInteger.Zero;
        for (int hand = 0; hand < 1 << cards.Length; hand++)
        {
            if (BitOperations.PopCount((uint)hand) != drawCount)
            {
                continue;
            }

            total++;
            List<int> sources = [];
            for (int card = 0; card < cards.Length; card++)
            {
                if ((hand & (1 << card)) != 0 && cards[card] != 0)
                {
                    sources.Add(cards[card]);
                }
            }

            if (paymentMasks.Length <= maximumUsableSources &&
                CanAssignPayments(sources, paymentMasks, paymentIndex: 0, usedMask: 0))
            {
                favorable++;
            }
        }

        return (favorable, total);
    }

    /// <summary>
    /// Recursively assigns distinct labeled sources to exact payment units.
    /// </summary>
    private static bool CanAssignPayments(
        IReadOnlyList<int> sources,
        IReadOnlyList<int> paymentMasks,
        int paymentIndex,
        int usedMask)
    {
        if (paymentIndex == paymentMasks.Count)
        {
            return true;
        }

        for (int source = 0; source < sources.Count; source++)
        {
            if ((usedMask & (1 << source)) == 0 &&
                (sources[source] & paymentMasks[paymentIndex]) != 0 &&
                CanAssignPayments(sources, paymentMasks, paymentIndex + 1, usedMask | (1 << source)))
            {
                return true;
            }
        }

        return false;
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
