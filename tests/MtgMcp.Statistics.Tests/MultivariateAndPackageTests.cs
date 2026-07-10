using System.Globalization;
using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies overlapping observations and one-use package allocation against independent oracles.
/// </summary>
public sealed class MultivariateAndPackageTests
{
    /// <summary>
    /// Represents a card bucket with no observed groups.
    /// </summary>
    private static readonly string[] NoGroups = [];

    /// <summary>
    /// Represents membership in observed group A.
    /// </summary>
    private static readonly string[] GroupA = ["a"];

    /// <summary>
    /// Represents membership in observed group B.
    /// </summary>
    private static readonly string[] GroupB = ["b"];

    /// <summary>
    /// Represents overlapping membership in observed groups A and B.
    /// </summary>
    private static readonly string[] GroupsAAndB = ["a", "b"];

    /// <summary>
    /// Represents membership in the first package piece group.
    /// </summary>
    private static readonly string[] PieceA = ["piece-a"];

    /// <summary>
    /// Represents membership in the second package piece group.
    /// </summary>
    private static readonly string[] PieceB = ["piece-b"];

    /// <summary>
    /// Represents membership in the flexible tutor group.
    /// </summary>
    private static readonly string[] Tutor = ["tutor"];

    /// <summary>
    /// Stores the labeled observed-group oracle memberships.
    /// </summary>
    private static readonly int[] ObservedMemberships = [1, 2, 3, 0, 0];

    /// <summary>
    /// Stores the labeled package oracle memberships.
    /// </summary>
    private static readonly int[] PackageMemberships = [1, 2, 4, 0, 0, 0];

    /// <summary>
    /// Stores the independent package oracle requirement masks.
    /// </summary>
    private static readonly int[] PackageRequirementMasks = [5, 6];

    /// <summary>
    /// Verifies an overlapping observed group event matches labeled-card enumeration.
    /// </summary>
    [Fact]
    public void CalculateMultivariate_OverlappingGroupsMatchLabeledOracle()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        StatisticsPopulation population = Population(
            (1, GroupA),
            (1, GroupB),
            (1, GroupsAAndB),
            (2, NoGroups));
        MultivariateRequest request = new(
            population,
            3,
            [
                new StatisticsGroupConditionInput("b", 1),
                new StatisticsGroupConditionInput("a", 1),
            ]);

        MultivariateResult result = RequireMultivariate(calculator.CalculateMultivariate(request));
        (BigInteger favorable, BigInteger total) = EnumerateObservedHands(
            ObservedMemberships,
            drawCount: 3,
            minimumA: 1,
            minimumB: 1);
        ExactFraction expected = new(favorable, total);

        Assert.Equal(expected.Numerator.ToString(CultureInfo.InvariantCulture), result.Probability.Numerator);
        Assert.Equal(expected.Denominator.ToString(CultureInfo.InvariantCulture), result.Probability.Denominator);
        Assert.Equal(["a", "b"], result.Conditions.Select(value => value.Group));
        Assert.Equal("multivariate-membership", result.Derivation.FormulaId);
        AssertComplement(result.Probability, result.Complement);
    }

    /// <summary>
    /// Verifies one overlapping card may satisfy two observations but only one package slot.
    /// </summary>
    [Fact]
    public void ObservationAndAllocation_ApplyDifferentOneCopySemantics()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        StatisticsPopulation population = Population((1, GroupsAAndB));
        MultivariateResult observed = RequireMultivariate(calculator.CalculateMultivariate(
            new MultivariateRequest(
                population,
                1,
                [
                    new StatisticsGroupConditionInput("a", 1),
                    new StatisticsGroupConditionInput("b", 1),
                ])));
        PackageAssemblyResult allocated = RequirePackage(calculator.CalculatePackageAssembly(
            new PackageAssemblyRequest(
                population,
                1,
                [
                    new PackageRequirementInput("first", 1, ["a"]),
                    new PackageRequirementInput("second", 1, ["b"]),
                ])));

        Assert.Equal("1", observed.Probability.Numerator);
        Assert.Equal("0", allocated.Probability.Numerator);
    }

    /// <summary>
    /// Verifies a flexible tutor package matches independent physical-copy allocation.
    /// </summary>
    [Fact]
    public void CalculatePackageAssembly_FlexibleTutorMatchesAllocationOracle()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        StatisticsPopulation population = Population(
            (1, PieceA),
            (1, PieceB),
            (1, Tutor),
            (3, NoGroups));
        PackageAssemblyRequest request = new(
            population,
            3,
            [
                new PackageRequirementInput("slot-b", 1, ["piece-b", "tutor"]),
                new PackageRequirementInput("slot-a", 1, ["piece-a", "tutor"]),
            ]);

        PackageAssemblyResult result = RequirePackage(calculator.CalculatePackageAssembly(request));
        (BigInteger favorable, BigInteger total) = EnumeratePackageHands(
            PackageMemberships,
            drawCount: 3,
            requirementMasks: PackageRequirementMasks);
        ExactFraction expected = new(favorable, total);

        Assert.Equal(expected.Numerator.ToString(CultureInfo.InvariantCulture), result.Probability.Numerator);
        Assert.Equal(expected.Denominator.ToString(CultureInfo.InvariantCulture), result.Probability.Denominator);
        Assert.Equal(["slot-a", "slot-b"], result.Requirements.Select(value => value.Name));
        Assert.Equal(["piece-a", "tutor"], result.Requirements[0].EligibleGroups);
        Assert.Equal("package-capacity-allocation", result.Derivation.FormulaId);
        AssertComplement(result.Probability, result.Complement);
    }

    /// <summary>
    /// Verifies capacity allocation supports repeated requirements and competing buckets.
    /// </summary>
    [Fact]
    public void AllocationMatcher_UsesEveryPhysicalCopyAtMostOnce()
    {
        ProjectedDrawState enough = new(
            [2, 1],
            [new ProjectedPopulationBucket(2, 1), new ProjectedPopulationBucket(1, 2)],
            [2, 1],
            BigInteger.One);
        ProjectedDrawState tooFew = enough with { DrawnByBucket = [1, 0] };
        PreparedPackageRequirement[] requirements =
        [
            new(2, 1),
            new(1, 3),
        ];

        Assert.True(AllocationMatcher.CanAllocate(enough, requirements));
        Assert.False(AllocationMatcher.CanAllocate(tooFew, requirements));
    }

    /// <summary>
    /// Verifies malformed multivariate requests fail before enumeration.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidMultivariateRequests))]
    public void CalculateMultivariate_InvalidRequestReturnsInvalid(MultivariateRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        Assert.IsType<OperationInvalidInput>(calculator.CalculateMultivariate(request).Value);
    }

    /// <summary>
    /// Supplies invalid multivariate requests.
    /// </summary>
    public static TheoryData<MultivariateRequest> InvalidMultivariateRequests => new()
    {
        new MultivariateRequest(Population((2, GroupA)), -1, [new("a", 1)]),
        new MultivariateRequest(Population((2, GroupA)), 3, [new("a", 1)]),
        new MultivariateRequest(Population((2, GroupA)), 1, null!),
        new MultivariateRequest(Population((2, GroupA)), 1, []),
        new MultivariateRequest(Population((2, GroupA)), 1, [null!]),
        new MultivariateRequest(Population((2, GroupA)), 1, [new("missing", 1)]),
        new MultivariateRequest(Population((2, GroupA)), 1, [new("a", -1)]),
        new MultivariateRequest(Population((2, GroupA)), 1, [new("a", 2, 1)]),
        new MultivariateRequest(Population((2, GroupA)), 1, [new("a", 0), new("a", 1)]),
        new MultivariateRequest(
            Population((2, GroupA)),
            1,
            Enumerable.Repeat(new StatisticsGroupConditionInput("a", 0), 9).ToArray()),
    };

    /// <summary>
    /// Verifies malformed package requests fail before enumeration.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidPackageRequests))]
    public void CalculatePackageAssembly_InvalidRequestReturnsInvalid(PackageAssemblyRequest request)
    {
        ExactStatisticsCalculator calculator = new("test-version");

        Assert.IsType<OperationInvalidInput>(calculator.CalculatePackageAssembly(request).Value);
    }

    /// <summary>
    /// Supplies invalid package requests.
    /// </summary>
    public static TheoryData<PackageAssemblyRequest> InvalidPackageRequests => new()
    {
        new PackageAssemblyRequest(Population((2, GroupA)), -1, [new("slot", 1, ["a"])]),
        new PackageAssemblyRequest(Population((2, GroupA)), 3, [new("slot", 1, ["a"])]),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, null!),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, []),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, [null!]),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, [new("", 1, ["a"])]),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, [new("slot", 0, ["a"])]),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, [new("slot", 1, [])]),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, [new("slot", 1, null!)]),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, [new("slot", 1, ["missing"])]),
        new PackageAssemblyRequest(Population((2, GroupA)), 1, [new("slot", 1, ["a", "a"])]),
        new PackageAssemblyRequest(
            Population((2, GroupA)),
            1,
            [new("slot", 1, ["a"]), new("slot", 1, ["a"])]),
        new PackageAssemblyRequest(
            Population((2, GroupA)),
            1,
            Enumerable.Range(0, 9)
                .Select(index => new PackageRequirementInput($"slot-{index}", 1, ["a"]))
                .ToArray()),
    };

    /// <summary>
    /// Verifies request-wide budgeting, cancellation, and null roots stop without partial results.
    /// </summary>
    [Fact]
    public void MultivariateAndPackage_BudgetCancellationAndNullStopClosed()
    {
        StatisticsPopulation population = Population(
            (1, GroupA),
            (1, GroupB),
            (2, NoGroups));
        ExactStatisticsCalculator boundedCalculator = new("test-version", 1);
        OperationSuccess<StatisticsCalculation<MultivariateResult>> boundedOperation =
            Assert.IsType<OperationSuccess<StatisticsCalculation<MultivariateResult>>>(
                boundedCalculator.CalculateMultivariate(new MultivariateRequest(
                    population,
                    2,
                    [new StatisticsGroupConditionInput("a", 1)])).Value);
        Assert.IsType<StatisticsBoundedUnsupported>(boundedOperation.Data.Value);
        OperationSuccess<StatisticsCalculation<PackageAssemblyResult>> boundedPackage =
            Assert.IsType<OperationSuccess<StatisticsCalculation<PackageAssemblyResult>>>(
                boundedCalculator.CalculatePackageAssembly(new PackageAssemblyRequest(
                    population,
                    2,
                    [new PackageRequirementInput("slot", 1, ["a"])])).Value);
        Assert.IsType<StatisticsBoundedUnsupported>(boundedPackage.Data.Value);

        ExactStatisticsCalculator calculator = new("test-version");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => calculator.CalculatePackageAssembly(
            new PackageAssemblyRequest(
                population,
                2,
                [new PackageRequirementInput("slot", 1, ["a"])]),
            cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculateMultivariate(null!));
        Assert.Throws<ArgumentNullException>(() => calculator.CalculatePackageAssembly(null!));
    }

    /// <summary>
    /// Verifies upper bounds and input permutations remain exact and byte-stable.
    /// </summary>
    [Fact]
    public void Multivariate_UpperBoundAndPermutationRemainCanonical()
    {
        ExactStatisticsCalculator calculator = new("test-version");
        StatisticsPopulation firstPopulation = Population(
            (1, GroupA),
            (1, GroupB),
            (1, GroupsAAndB),
            (2, NoGroups));
        StatisticsPopulation secondPopulation = Population(
            (2, NoGroups),
            (1, GroupsAAndB),
            (1, GroupB),
            (1, GroupA));
        MultivariateRequest first = new(
            firstPopulation,
            2,
            [new StatisticsGroupConditionInput("b", 0, 1), new("a", 1)]);
        MultivariateRequest second = new(
            secondPopulation,
            2,
            [new StatisticsGroupConditionInput("a", 1), new("b", 0, 1)]);

        string firstJson = System.Text.Json.JsonSerializer.Serialize(
            calculator.CalculateMultivariate(first));
        string secondJson = System.Text.Json.JsonSerializer.Serialize(
            calculator.CalculateMultivariate(second));

        Assert.Equal(firstJson, secondJson);
    }

    /// <summary>
    /// Creates a population and infers its declared groups from explicit disjoint buckets.
    /// </summary>
    private static StatisticsPopulation Population(
        params (int Count, string[] Groups)[] buckets)
    {
        string[] groups = buckets
            .SelectMany(value => value.Groups)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new StatisticsPopulation(
            buckets.Select(value => new StatisticsPopulationBucket(value.Count, value.Groups)).ToArray(),
            groups);
    }

    /// <summary>
    /// Extracts one exact multivariate result.
    /// </summary>
    private static MultivariateResult RequireMultivariate(
        OperationResult<StatisticsCalculation<MultivariateResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<MultivariateResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<MultivariateResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<MultivariateResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Extracts one exact package result.
    /// </summary>
    private static PackageAssemblyResult RequirePackage(
        OperationResult<StatisticsCalculation<PackageAssemblyResult>> operation)
    {
        OperationSuccess<StatisticsCalculation<PackageAssemblyResult>> success =
            Assert.IsType<OperationSuccess<StatisticsCalculation<PackageAssemblyResult>>>(operation.Value);
        return Assert.IsType<StatisticsExact<PackageAssemblyResult>>(success.Data.Value).Data;
    }

    /// <summary>
    /// Verifies two public probabilities are exact complements.
    /// </summary>
    private static void AssertComplement(ExactProbability probability, ExactProbability complement)
    {
        ExactFraction left = new(
            BigInteger.Parse(probability.Numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(probability.Denominator, CultureInfo.InvariantCulture));
        ExactFraction right = new(
            BigInteger.Parse(complement.Numerator, CultureInfo.InvariantCulture),
            BigInteger.Parse(complement.Denominator, CultureInfo.InvariantCulture));
        Assert.Equal(ExactFraction.One, left.Add(right));
    }

    /// <summary>
    /// Enumerates labeled observed-group hands from bit-mask card memberships.
    /// </summary>
    private static (BigInteger Favorable, BigInteger Total) EnumerateObservedHands(
        int[] memberships,
        int drawCount,
        int minimumA,
        int minimumB)
    {
        BigInteger favorable = BigInteger.Zero;
        BigInteger total = BigInteger.Zero;
        for (int hand = 0; hand < 1 << memberships.Length; hand++)
        {
            if (BitOperations.PopCount((uint)hand) != drawCount)
            {
                continue;
            }

            total++;
            int countA = 0;
            int countB = 0;
            for (int card = 0; card < memberships.Length; card++)
            {
                if ((hand & (1 << card)) == 0)
                {
                    continue;
                }

                countA += (memberships[card] & 1) == 0 ? 0 : 1;
                countB += (memberships[card] & 2) == 0 ? 0 : 1;
            }

            if (countA >= minimumA && countB >= minimumB)
            {
                favorable++;
            }
        }

        return (favorable, total);
    }

    /// <summary>
    /// Enumerates labeled package hands and performs a tiny independent slot assignment.
    /// </summary>
    private static (BigInteger Favorable, BigInteger Total) EnumeratePackageHands(
        int[] memberships,
        int drawCount,
        int[] requirementMasks)
    {
        BigInteger favorable = BigInteger.Zero;
        BigInteger total = BigInteger.Zero;
        for (int hand = 0; hand < 1 << memberships.Length; hand++)
        {
            if (BitOperations.PopCount((uint)hand) != drawCount)
            {
                continue;
            }

            total++;
            List<int> cards = [];
            for (int card = 0; card < memberships.Length; card++)
            {
                if ((hand & (1 << card)) != 0)
                {
                    cards.Add(memberships[card]);
                }
            }

            if (CanAssign(cards, requirementMasks, requirementIndex: 0, usedMask: 0))
            {
                favorable++;
            }
        }

        return (favorable, total);
    }

    /// <summary>
    /// Recursively assigns distinct labeled cards to independent requirement slots.
    /// </summary>
    private static bool CanAssign(
        IReadOnlyList<int> cards,
        IReadOnlyList<int> requirements,
        int requirementIndex,
        int usedMask)
    {
        if (requirementIndex == requirements.Count)
        {
            return true;
        }

        for (int card = 0; card < cards.Count; card++)
        {
            if ((usedMask & (1 << card)) == 0 &&
                (cards[card] & requirements[requirementIndex]) != 0 &&
                CanAssign(cards, requirements, requirementIndex + 1, usedMask | (1 << card)))
            {
                return true;
            }
        }

        return false;
    }
}
