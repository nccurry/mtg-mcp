namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies the 95 percent Wilson interval against hand-checked values.
/// </summary>
public sealed class OnCurveWilsonTests
{
    /// <summary>
    /// Verifies no successes start at zero and all successes end at one.
    /// </summary>
    [Fact]
    public void Calculate_AtTheEnds_PinsTheBoundToZeroOrOne()
    {
        OnCurveWilsonInterval none = OnCurveWilsonIntervalCalculator.Calculate(0, 100);
        OnCurveWilsonInterval all = OnCurveWilsonIntervalCalculator.Calculate(100, 100);

        Assert.Equal(0, none.LowerBound);
        Assert.Equal(0.036993, Math.Round(none.UpperBound, 6));
        Assert.Equal(1, all.UpperBound);
        Assert.Equal(0.963007, Math.Round(all.LowerBound, 6));
    }

    /// <summary>
    /// Verifies one half in a hundred gives the known interval, centered below one half.
    /// </summary>
    [Fact]
    public void Calculate_AtOneHalf_GivesTheKnownInterval()
    {
        OnCurveWilsonInterval half = OnCurveWilsonIntervalCalculator.Calculate(50, 100);

        Assert.Equal(0.403832, Math.Round(half.LowerBound, 6));
        Assert.Equal(0.596168, Math.Round(half.UpperBound, 6));
    }

    /// <summary>
    /// Verifies counts outside zero to the sample count are refused.
    /// </summary>
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(11, 10)]
    [InlineData(0, 0)]
    [InlineData(1, -3)]
    public void Calculate_WithBadCounts_Throws(int successes, int samples)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OnCurveWilsonIntervalCalculator.Calculate(successes, samples));
    }
}
