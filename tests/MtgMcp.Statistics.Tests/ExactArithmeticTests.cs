using System.Numerics;

namespace MtgMcp.Statistics.Tests;

/// <summary>
/// Verifies rational arithmetic, stable display rounding, combinations, and work accounting.
/// </summary>
public sealed class ExactArithmeticTests
{
    /// <summary>
    /// Verifies rational normalization and exact arithmetic never use rounded values.
    /// </summary>
    [Fact]
    public void ExactFraction_NormalizesAndCalculatesExactly()
    {
        ExactFraction half = new(2, 4);
        ExactFraction negativeHalf = new(1, -2);

        Assert.Equal(BigInteger.One, half.Numerator);
        Assert.Equal(new BigInteger(2), half.Denominator);
        Assert.Equal(new ExactFraction(0, 1), half.Add(negativeHalf));
        Assert.Equal(new ExactFraction(1, 1), half.Subtract(negativeHalf));
        Assert.Equal(new ExactFraction(-1, 4), half.Multiply(negativeHalf));
        Assert.Equal(new ExactFraction(-1, 1), half.Divide(negativeHalf));
        Assert.Equal(new ExactFraction(1, 8), half.Pow(3));
        Assert.Equal(new ExactFraction(7, 1), ExactFraction.FromInteger(7));
        Assert.True(half.CompareTo(negativeHalf) > 0);
    }

    /// <summary>
    /// Verifies public number strings use fixed invariant midpoint-to-even formatting.
    /// </summary>
    [Fact]
    public void ExactFraction_FormatsFixedMidpointToEvenValues()
    {
        ExactRationalValue roundsToEvenZero = new ExactFraction(1, 2_000_000_000_000).ToValue();
        ExactRationalValue roundsToEvenTwo = new ExactFraction(3, 2_000_000_000_000).ToValue();
        ExactRationalValue negative = new ExactFraction(-1, 8).ToValue();
        ExactProbability probability = new ExactFraction(1, 8).ToProbability();

        Assert.Equal("0.000000000000", roundsToEvenZero.Display);
        Assert.Equal("0.000000000002", roundsToEvenTwo.Display);
        Assert.Equal("-0.125000000000", negative.Display);
        Assert.Equal("0.125000000000", probability.Display);
        Assert.Equal("12.500000000000", probability.Percent);
    }

    /// <summary>
    /// Verifies invalid rational and probability operations fail before producing a display.
    /// </summary>
    [Fact]
    public void ExactFraction_InvalidOperationsFailClosed()
    {
        Assert.Throws<DivideByZeroException>(() => new ExactFraction(1, 0));
        Assert.Throws<DivideByZeroException>(
            () => ExactFraction.One.Divide(ExactFraction.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExactFraction.One.Pow(-1));
        Assert.Throws<InvalidOperationException>(() => new ExactFraction(-1, 2).ToProbability());
        Assert.Throws<InvalidOperationException>(() => new ExactFraction(3, 2).ToProbability());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExactNumberFormatter.FormatFixed(1, 0));
    }

    /// <summary>
    /// Verifies combination values, symmetry, impossible selections, and memoized reuse.
    /// </summary>
    [Fact]
    public void CombinationCache_ReturnsExactSymmetricValues()
    {
        CombinationCache cache = new();

        Assert.Equal(new BigInteger(120), cache.Choose(10, 3));
        Assert.Equal(new BigInteger(120), cache.Choose(10, 7));
        Assert.Equal(new BigInteger(120), cache.Choose(10, 3));
        Assert.Equal(BigInteger.One, cache.Choose(10, 0));
        Assert.Equal(BigInteger.Zero, cache.Choose(10, -1));
        Assert.Equal(BigInteger.Zero, cache.Choose(10, 11));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Choose(-1, 0));
    }

    /// <summary>
    /// Verifies the request-wide budget allows its exact limit and rejects the next unit.
    /// </summary>
    [Fact]
    public void StatisticsWorkBudget_EnforcesExactLimitAndSaturatingEstimates()
    {
        StatisticsWorkBudget budget = new(3);

        Assert.True(budget.TryConsume(2));
        Assert.True(budget.TryConsume(1));
        Assert.Equal(3, budget.Used);
        Assert.False(budget.TryConsume(1));
        Assert.Equal(long.MaxValue, StatisticsWorkBudget.SaturatingMultiply(long.MaxValue, 2));
        Assert.Equal(0, StatisticsWorkBudget.SaturatingMultiply(0, long.MaxValue));
        Assert.Equal(12, StatisticsWorkBudget.SaturatingMultiply(3, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatisticsWorkBudget(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryConsume(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatisticsWorkBudget.SaturatingMultiply(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatisticsWorkBudget.SaturatingMultiply(1, -1));
    }
}
