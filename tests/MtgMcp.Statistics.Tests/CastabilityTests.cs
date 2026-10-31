using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies the exact castability calculation against the existing mana availability engine and against brute-force
/// enumeration of every possible hand (CAST-001, cases K01, K02, and K06).
/// </summary>
public sealed class CastabilityTests
{
    /// <summary>
    /// Names the one source group of green sources.
    /// </summary>
    private static readonly string[] GreenGroup = ["lands"];

    /// <summary>
    /// Names the black source group.
    /// </summary>
    private static readonly string[] BlackGroup = ["rocks"];

    /// <summary>
    /// Verifies the unconditional value equals the mana availability engine for the same draw count and source cap
    /// (case K01).
    /// </summary>
    [Fact]
    public void CalculateCastability_Unconditional_EqualsManaAvailability()
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(40, GreenGroup), new StatisticsPopulationBucket(59, [])],
            GreenGroup);
        ManaSourceInput[] sources = [new ManaSourceInput("lands", ["G"])];
        ManaRequirementInput cost = new(Green: 2, Generic: 1);

        CastabilityResult result = Exact(calculator.CalculateCastability(new CastabilityRequest(
            population,
            sources,
            [new CastabilityCostInput("{1}{G}{G}", cost, 3)],
            OnThePlay: true,
            RequireLandDrops: false)));
        ManaAvailabilityResult availability = ExactAvailability(calculator.CalculateManaAvailability(
            new ManaAvailabilityRequest(population, 9, sources, cost, 3)));

        CastabilityRow row = Assert.Single(result.Rows);
        Assert.Equal(9, row.CardsSeen);
        Assert.Equal(availability.Probability.Numerator, row.Probability!.Numerator);
        Assert.Equal(availability.Probability.Denominator, row.Probability.Denominator);
        Assert.False(result.RequireLandDrops);
        Assert.True(result.OnThePlay);
    }

    /// <summary>
    /// Verifies on the draw sees one more card than on the play.
    /// </summary>
    [Fact]
    public void CalculateCastability_OnTheDraw_SeesOneMoreCard()
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(40, GreenGroup), new StatisticsPopulationBucket(59, [])],
            GreenGroup);

        CastabilityResult draw = Exact(calculator.CalculateCastability(new CastabilityRequest(
            population,
            [new ManaSourceInput("lands", ["G"])],
            [new CastabilityCostInput("g", new ManaRequirementInput(Green: 1), 2)],
            OnThePlay: false,
            RequireLandDrops: false)));

        Assert.Equal(9, Assert.Single(draw.Rows).CardsSeen);
    }

    /// <summary>
    /// Verifies the conditional and unconditional values equal brute-force enumeration of every hand on a small deck,
    /// on the play and on the draw (case K02).
    /// </summary>
    [Theory]
    [InlineData(true, true, 3, 1, 1, 1)]
    [InlineData(false, true, 3, 1, 1, 1)]
    [InlineData(true, false, 3, 1, 1, 1)]
    [InlineData(true, true, 2, 1, 0, 1)]
    [InlineData(false, true, 2, 2, 0, 0)]
    [InlineData(true, true, 4, 1, 1, 2)]
    [InlineData(true, true, 1, 1, 0, 0)]
    public void CalculateCastability_MatchesBruteForceEnumeration(
        bool onThePlay,
        bool requireLandDrops,
        int turn,
        int green,
        int black,
        int generic)
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [
                new StatisticsPopulationBucket(6, GreenGroup),
                new StatisticsPopulationBucket(4, BlackGroup),
                new StatisticsPopulationBucket(10, []),
            ],
            [.. GreenGroup, .. BlackGroup]);
        ManaRequirementInput cost = new(Green: green, Black: black, Generic: generic);

        CastabilityResult result = Exact(calculator.CalculateCastability(new CastabilityRequest(
            population,
            [new ManaSourceInput("lands", ["G"]), new ManaSourceInput("rocks", ["B"])],
            [new CastabilityCostInput("cost", cost, turn)],
            onThePlay,
            requireLandDrops)));

        int seen = 7 + turn - (onThePlay ? 1 : 0);
        (BigInteger numerator, BigInteger denominator) = BruteForce(seen, turn, green, black, generic,
            requireLandDrops);
        CastabilityRow row = Assert.Single(result.Rows);
        Assert.Equal(seen, row.CardsSeen);
        Assert.NotNull(row.Probability);
        Assert.Equal(
            numerator * BigInteger.Parse(row.Probability.Denominator, CultureInfo.InvariantCulture),
            BigInteger.Parse(row.Probability.Numerator, CultureInfo.InvariantCulture) * denominator);
    }

    /// <summary>
    /// Verifies a conditional probability is undefined, not zero, when the deck cannot show enough sources.
    /// </summary>
    [Fact]
    public void CalculateCastability_TooFewSourcesForTheTurn_IsUndefinedWhenConditional()
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(2, GreenGroup), new StatisticsPopulationBucket(30, [])],
            GreenGroup);
        CastabilityCostInput cost = new("g", new ManaRequirementInput(Green: 1), 3);

        CastabilityResult conditional = Exact(calculator.CalculateCastability(new CastabilityRequest(
            population, [new ManaSourceInput("lands", ["G"])], [cost], true, true)));
        CastabilityResult plain = Exact(calculator.CalculateCastability(new CastabilityRequest(
            population, [new ManaSourceInput("lands", ["G"])], [cost], true, false)));

        Assert.Null(Assert.Single(conditional.Rows).Probability);
        Assert.NotNull(Assert.Single(plain.Rows).Probability);
    }

    /// <summary>
    /// Verifies a cost that needs more sources than the turn has is impossible with probability zero.
    /// </summary>
    [Fact]
    public void CalculateCastability_CostLargerThanTheTurn_HasProbabilityZero()
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(20, GreenGroup), new StatisticsPopulationBucket(40, [])],
            GreenGroup);

        CastabilityResult result = Exact(calculator.CalculateCastability(new CastabilityRequest(
            population,
            [new ManaSourceInput("lands", ["G"])],
            [new CastabilityCostInput("big", new ManaRequirementInput(Green: 1, Generic: 4), 2)],
            true,
            true)));

        Assert.Equal("0", Assert.Single(result.Rows).Probability!.Numerator);
    }

    /// <summary>
    /// Verifies more costs than the work limit allows are refused with the limit stated (case K06).
    /// </summary>
    [Fact]
    public void CalculateCastability_AboveTheWorkLimit_IsInvalidAndStatesTheLimit()
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(20, GreenGroup), new StatisticsPopulationBucket(40, [])],
            GreenGroup);
        List<CastabilityCostInput> costs =
        [
            .. Enumerable.Range(0, ExactStatisticsCalculator.MaximumCastabilityWork + 1)
                .Select(index => new CastabilityCostInput($"cost {index}", new ManaRequirementInput(Green: 1), 2)),
        ];

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(calculator.CalculateCastability(
            new CastabilityRequest(population, [new ManaSourceInput("lands", ["G"])], costs, true, true)).Value);

        Assert.Contains("480", invalid.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies malformed requests are refused: no costs, a blank key, a turn below one, too few cards, overlapping
    /// source groups, and unknown source groups.
    /// </summary>
    [Fact]
    public void CalculateCastability_MalformedRequests_AreInvalid()
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(3, GreenGroup), new StatisticsPopulationBucket(5, [])],
            GreenGroup);
        ManaSourceInput[] sources = [new ManaSourceInput("lands", ["G"])];
        CastabilityCostInput good = new("g", new ManaRequirementInput(Green: 1), 1);
        StatisticsPopulation overlapping = new(
            [new StatisticsPopulationBucket(3, [.. GreenGroup, .. BlackGroup]), new StatisticsPopulationBucket(30, [])],
            [.. GreenGroup, .. BlackGroup]);

        CastabilityRequest[] requests =
        [
            new(population, sources, [], true, false),
            new(population, sources, [good with { Key = " " }], true, false),
            new(population, sources, [good with { Turn = 0 }], true, false),
            new(population, sources, [good with { Turn = 3 }], true, false),
            new(population, sources, [good with { Requirement = new ManaRequirementInput(Green: -1) }], true, false),
            new(population, [new ManaSourceInput("missing", ["G"])], [good], true, false),
            new(overlapping, [new ManaSourceInput("lands", ["G"]), new ManaSourceInput("rocks", ["B"])], [good],
                true, false),
        ];

        foreach (CastabilityRequest request in requests)
        {
            Assert.IsType<OperationInvalidInput>(calculator.CalculateCastability(request).Value);
        }
    }

    /// <summary>
    /// Verifies a request over the work budget returns the bounded result with no partial numbers.
    /// </summary>
    [Fact]
    public void CalculateCastability_OverTheCalculatorBudget_IsBounded()
    {
        ExactStatisticsCalculator calculator = new("test", 5);
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(20, GreenGroup), new StatisticsPopulationBucket(40, [])],
            GreenGroup);

        OperationResult<StatisticsCalculation<CastabilityResult>> result = calculator.CalculateCastability(
            new CastabilityRequest(
                population,
                [new ManaSourceInput("lands", ["G"])],
                [new CastabilityCostInput("g", new ManaRequirementInput(Green: 1), 3)],
                true,
                true));

        Assert.IsType<StatisticsBoundedUnsupported>(
            Assert.IsType<OperationSuccess<StatisticsCalculation<CastabilityResult>>>(result.Value).Data.Value);
    }

    /// <summary>
    /// Verifies the work budget covers the whole request: a limit that is enough for one cost is not enough for many
    /// costs together, and the answer is the bounded result with no partial numbers.
    /// </summary>
    [Fact]
    public void CalculateCastability_ManyCostsTogether_ShareOneBudget()
    {
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(20, GreenGroup), new StatisticsPopulationBucket(40, [])],
            GreenGroup);
        ManaSourceInput[] sources = [new ManaSourceInput("lands", ["G"])];
        static CastabilityCostInput One(int index)
        {
            return new CastabilityCostInput($"cost {index}", new ManaRequirementInput(Green: 1), 3);
        }

        long enoughForOne = new long[] { 10, 30, 100, 300, 1_000, 3_000, 10_000, 30_000, 100_000, 300_000 }
            .First(limit => new ExactStatisticsCalculator("test", limit).CalculateCastability(new CastabilityRequest(
                population, sources, [One(0)], true, false)).Value is OperationSuccess<
                    StatisticsCalculation<CastabilityResult>> { Data.Value: StatisticsExact<CastabilityResult> });
        List<CastabilityCostInput> many = [.. Enumerable.Range(0, 400).Select(One)];

        OperationResult<StatisticsCalculation<CastabilityResult>> result = new ExactStatisticsCalculator(
            "test",
            enoughForOne).CalculateCastability(new CastabilityRequest(population, sources, many, true, false));

        Assert.IsType<StatisticsBoundedUnsupported>(
            Assert.IsType<OperationSuccess<StatisticsCalculation<CastabilityResult>>>(result.Value).Data.Value);
    }

    /// <summary>
    /// Verifies a turn above the latest turn the calculation allows is refused, so a huge turn cannot overflow.
    /// </summary>
    [Fact]
    public void CalculateCastability_TurnAboveTheMaximum_IsInvalid()
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [new StatisticsPopulationBucket(20, GreenGroup), new StatisticsPopulationBucket(40, [])],
            GreenGroup);

        foreach (int turn in new[] { ExactStatisticsCalculator.MaximumCastabilityTurn + 1, int.MaxValue })
        {
            Assert.IsType<OperationInvalidInput>(calculator.CalculateCastability(new CastabilityRequest(
                population,
                [new ManaSourceInput("lands", ["G"])],
                [new CastabilityCostInput("g", new ManaRequirementInput(Green: 1), turn)],
                true,
                false)).Value);
        }
    }

    /// <summary>
    /// Verifies a source that can make either of two colors equals brute-force enumeration, so a dual source is
    /// counted once and assigned to the color that needs it (the one-color groups above cannot show this).
    /// </summary>
    [Theory]
    [InlineData(true, true, 3, 1, 1, 1)]
    [InlineData(false, true, 3, 2, 1, 0)]
    [InlineData(true, false, 4, 2, 2, 0)]
    [InlineData(true, true, 2, 1, 1, 0)]
    [InlineData(false, false, 3, 0, 2, 1)]
    public void CalculateCastability_DualSources_MatchBruteForceEnumeration(
        bool onThePlay,
        bool requireLandDrops,
        int turn,
        int green,
        int black,
        int generic)
    {
        ExactStatisticsCalculator calculator = new("test");
        StatisticsPopulation population = new(
            [
                new StatisticsPopulationBucket(5, GreenGroup),
                new StatisticsPopulationBucket(3, BlackGroup),
                new StatisticsPopulationBucket(3, ["duals"]),
                new StatisticsPopulationBucket(9, []),
            ],
            [.. GreenGroup, .. BlackGroup, "duals"]);

        CastabilityResult result = Exact(calculator.CalculateCastability(new CastabilityRequest(
            population,
            [
                new ManaSourceInput("lands", ["G"]),
                new ManaSourceInput("rocks", ["B"]),
                new ManaSourceInput("duals", ["B", "G"]),
            ],
            [new CastabilityCostInput(
                "cost",
                new ManaRequirementInput(Green: green, Black: black, Generic: generic),
                turn)],
            onThePlay,
            requireLandDrops)));

        int seen = 7 + turn - (onThePlay ? 1 : 0);
        (BigInteger numerator, BigInteger denominator) = BruteForceWithDuals(seen, turn, green, black, generic,
            requireLandDrops);
        CastabilityRow row = Assert.Single(result.Rows);
        Assert.NotNull(row.Probability);
        Assert.Equal(
            numerator * BigInteger.Parse(row.Probability.Denominator, CultureInfo.InvariantCulture),
            BigInteger.Parse(row.Probability.Numerator, CultureInfo.InvariantCulture) * denominator);
    }

    /// <summary>
    /// Counts every hand of a 20-card deck (5 green sources, 3 black sources, 3 dual sources that make either color,
    /// 9 other cards) by brute force. A dual source goes to whichever color needs it.
    /// </summary>
    private static (BigInteger Numerator, BigInteger Denominator) BruteForceWithDuals(
        int seen,
        int turn,
        int green,
        int black,
        int generic,
        bool requireLandDrops)
    {
        const int cards = 20;
        BigInteger payable = 0;
        BigInteger conditioning = 0;
        BigInteger total = 0;
        for (int mask = 0; mask < 1 << cards; mask++)
        {
            if (BitOperations.PopCount((uint)mask) != seen)
            {
                continue;
            }

            total++;
            int drawnGreen = BitOperations.PopCount((uint)(mask & 0b11111));
            int drawnBlack = BitOperations.PopCount((uint)(mask >> 5 & 0b111));
            int drawnDual = BitOperations.PopCount((uint)(mask >> 8 & 0b111));
            int sources = drawnGreen + drawnBlack + drawnDual;
            if (requireLandDrops && sources < turn)
            {
                continue;
            }

            conditioning++;
            bool pays = green + black + generic <= turn && sources >= green + black + generic &&
                Enumerable.Range(0, drawnDual + 1).Any(
                    toGreen => drawnGreen + toGreen >= green && drawnBlack + drawnDual - toGreen >= black);
            if (pays)
            {
                payable++;
            }
        }

        return requireLandDrops ? (payable, conditioning) : (payable, total);
    }

    /// <summary>
    /// Counts every hand of a 20-card deck (6 green sources, 4 black sources, 10 other cards) by brute force. A hand
    /// pays when the cost needs at most as many sources as the turn and the hand holds enough of each color; with
    /// the land drop condition the hand must also hold at least as many sources as the turn.
    /// </summary>
    private static (BigInteger Numerator, BigInteger Denominator) BruteForce(
        int seen,
        int turn,
        int green,
        int black,
        int generic,
        bool requireLandDrops)
    {
        const int cards = 20;
        BigInteger payable = 0;
        BigInteger conditioning = 0;
        BigInteger total = 0;
        for (int mask = 0; mask < 1 << cards; mask++)
        {
            if (BitOperations.PopCount((uint)mask) != seen)
            {
                continue;
            }

            total++;
            int drawnGreen = BitOperations.PopCount((uint)(mask & 0b111111));
            int drawnBlack = BitOperations.PopCount((uint)(mask >> 6 & 0b1111));
            int sources = drawnGreen + drawnBlack;
            int needed = green + black + generic;
            bool pays = needed <= turn && drawnGreen >= green && drawnBlack >= black && sources >= needed;
            if (requireLandDrops && sources < turn)
            {
                continue;
            }

            conditioning++;
            if (pays)
            {
                payable++;
            }
        }

        return requireLandDrops ? (payable, conditioning) : (payable, total);
    }

    /// <summary>
    /// Unwraps an exact castability result.
    /// </summary>
    private static CastabilityResult Exact(OperationResult<StatisticsCalculation<CastabilityResult>> result)
    {
        OperationSuccess<StatisticsCalculation<CastabilityResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<CastabilityResult>>>(result.Value);
        return Assert.IsType<StatisticsExact<CastabilityResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Unwraps an exact mana availability result.
    /// </summary>
    private static ManaAvailabilityResult ExactAvailability(
        OperationResult<StatisticsCalculation<ManaAvailabilityResult>> result)
    {
        OperationSuccess<StatisticsCalculation<ManaAvailabilityResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<ManaAvailabilityResult>>>(result.Value);
        return Assert.IsType<StatisticsExact<ManaAvailabilityResult>>(success.Data.Value).Data;
    }
}
