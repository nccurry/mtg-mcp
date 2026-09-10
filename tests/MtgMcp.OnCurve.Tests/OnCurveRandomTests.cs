namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies the versioned random sequence and replay seed boundary.
/// </summary>
public sealed class OnCurveRandomTests
{
    /// <summary>
    /// Verifies the version-one SplitMix64 values for the zero seed.
    /// </summary>
    [Fact]
    public void NextUInt64_WithZeroSeed_ReturnsKnownValues()
    {
        SplitMix64Random random = new(0);

        Assert.Equal(0xe220a8397b1dcdafUL, random.NextUInt64());
        Assert.Equal(0x6e789e6aa1b965f4UL, random.NextUInt64());
        Assert.Equal(0x06c45d188009454fUL, random.NextUInt64());
        Assert.Equal(0xf88bb8a8724c81ecUL, random.NextUInt64());
    }

    /// <summary>
    /// Verifies Fisher-Yates has one checked-in order for a known library and seed.
    /// </summary>
    [Fact]
    public void Shuffle_WithZeroSeed_ReturnsKnownOrder()
    {
        SplitMix64Random random = new(0);
        List<int> values = [0, 1, 2, 3, 4, 5, 6, 7];

        random.Shuffle(values);

        Assert.Equal([2, 5, 0, 3, 4, 6, 1, 7], values);
    }

    /// <summary>
    /// Verifies each bounded random index stays inside its exclusive upper limit.
    /// </summary>
    [Fact]
    public void NextIndex_WithPositiveBound_ReturnsBoundedValues()
    {
        SplitMix64Random random = new(1);

        for (int index = 0; index < 100; index++)
        {
            int value = random.NextIndex(3);
            Assert.InRange(value, 0, 2);
        }
    }

    /// <summary>
    /// Verifies zero and negative random bounds fail instead of biasing an index.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextIndex_WithNonpositiveBound_Throws(int exclusiveUpperBound)
    {
        SplitMix64Random random = new(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextIndex(exclusiveUpperBound));
    }

    /// <summary>
    /// Verifies replay seeds keep leading zeroes and normalize output case.
    /// </summary>
    [Theory]
    [InlineData("0000000000000001", 1UL, "0000000000000001")]
    [InlineData("FFFFFFFFFFFFFFFF", ulong.MaxValue, "ffffffffffffffff")]
    public void TryParse_WithFixedWidthHex_ReturnsNormalizedSeed(
        string text,
        ulong expectedSeed,
        string expectedText)
    {
        bool parsed = OnCurveSeed.TryParse(text, out ulong seed);

        Assert.True(parsed);
        Assert.Equal(expectedSeed, seed);
        Assert.Equal(expectedText, OnCurveSeed.Format(seed));
    }

    /// <summary>
    /// Verifies shortened, padded, and nonhexadecimal seeds cannot be replayed.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdef0")]
    [InlineData("0123456789abcdeg")]
    [InlineData(" 123456789abcdef")]
    public void TryParse_WithMalformedText_ReturnsFalse(string? text)
    {
        bool parsed = OnCurveSeed.TryParse(text, out ulong seed);

        Assert.False(parsed);
        Assert.Equal(0UL, seed);
    }

    /// <summary>
    /// Verifies a canceled shuffle stops before it changes a list.
    /// </summary>
    [Fact]
    public void Shuffle_WithCanceledToken_ThrowsBeforeChangingValues()
    {
        SplitMix64Random random = new(1);
        List<int> values = [0, 1, 2];
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => random.Shuffle(values, cancellation.Token));
        Assert.Equal([0, 1, 2], values);
    }
}
