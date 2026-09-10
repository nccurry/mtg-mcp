using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies simple mana-cost reading and fact-only source payment matching.
/// </summary>
public sealed class OnCurveManaTests
{
    /// <summary>
    /// Verifies a simple printed cost keeps every supported generic and specific symbol.
    /// </summary>
    [Fact]
    public void ReadCost_WithSupportedSymbols_ReturnsCounts()
    {
        OperationResult<OnCurveManaCost> result = OnCurveManaSymbols.ReadCost("{2}{W}{U}{B}{R}{G}{C}");

        OnCurveManaCost cost = Assert.IsType<OperationSuccess<OnCurveManaCost>>(result.Value).Data;
        Assert.Equal(2, cost.Generic);
        Assert.Equal(1, cost.White);
        Assert.Equal(1, cost.Blue);
        Assert.Equal(1, cost.Black);
        Assert.Equal(1, cost.Red);
        Assert.Equal(1, cost.Green);
        Assert.Equal(1, cost.Colorless);
        Assert.Equal(6, cost.SpecificSymbolCount);
        Assert.Equal(8, cost.TotalSymbolCount);
    }

    /// <summary>
    /// Verifies unsupported or malformed costs stop before a trial starts.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{X}{G}")]
    [InlineData("{G/U}")]
    [InlineData("{P}{G}")]
    [InlineData("{G")]
    [InlineData("G")]
    [InlineData("{ 1}")]
    public void ReadCost_WithUnsupportedText_ReturnsUnsupported(string? manaCost)
    {
        OperationResult<OnCurveManaCost> result = OnCurveManaSymbols.ReadCost(manaCost);

        OperationUnsupported unsupported = Assert.IsType<OperationUnsupported>(result.Value);
        Assert.Equal("unsupported-on-curve-mana-cost", unsupported.ReasonCode);
    }

    /// <summary>
    /// Verifies direct Scryfall mana strings have one fixed supported set.
    /// </summary>
    [Theory]
    [InlineData("W", 0, "W")]
    [InlineData("U", 1, "U")]
    [InlineData("B", 2, "B")]
    [InlineData("R", 3, "R")]
    [InlineData("G", 4, "G")]
    [InlineData("C", 5, "C")]
    public void ProducedMana_WithSupportedValue_ParsesAndFormats(
        string value,
        int expectedValue,
        string formatted)
    {
        bool parsed = OnCurveManaSymbols.TryParseProducedMana(value, out OnCurveManaSymbol symbol);

        Assert.True(parsed);
        Assert.Equal((OnCurveManaSymbol)expectedValue, symbol);
        Assert.Equal(formatted, OnCurveManaSymbols.Format(symbol));
    }

    /// <summary>
    /// Verifies values outside the model cannot become a mana source by accident.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S")]
    [InlineData("G ")]
    [InlineData("g")]
    public void ProducedMana_WithUnsupportedValue_ReturnsFalse(string? value)
    {
        bool parsed = OnCurveManaSymbols.TryParseProducedMana(value, out _);

        Assert.False(parsed);
    }

    /// <summary>
    /// Verifies invalid internal symbols do not format as a supported source value.
    /// </summary>
    [Fact]
    public void Format_WithUnknownSymbol_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OnCurveManaSymbols.Format((OnCurveManaSymbol)99));
    }

    /// <summary>
    /// Verifies matching reserves flexible sources for required colors before generic payment.
    /// </summary>
    [Fact]
    public void Analyze_WithSpecificAndGenericCost_UsesOneSourcePerSymbol()
    {
        OnCurveManaCost cost = new(1, 0, 0, 0, 0, 2, 0);
        IReadOnlyList<OnCurveActiveSource> sources =
        [
            Source(1, OnCurveManaSymbol.Green),
            Source(2, OnCurveManaSymbol.Blue),
            Source(3, OnCurveManaSymbol.Green),
        ];

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, sources);

        Assert.Equal(2, result.SpecificSymbolsCovered);
        Assert.Equal(3, result.TotalSymbolsCovered);
        Assert.True(result.CoversSpecificCost(cost));
        Assert.True(result.CoversFullCost(cost));
        Assert.Collection(
            result.PaymentSources,
            payment =>
            {
                Assert.Equal(OnCurveTestData.Id(1), payment.EntryId);
                Assert.Equal("G", payment.PaidFor);
            },
            payment =>
            {
                Assert.Equal(OnCurveTestData.Id(2), payment.EntryId);
                Assert.Equal("generic", payment.PaidFor);
            },
            payment =>
            {
                Assert.Equal(OnCurveTestData.Id(3), payment.EntryId);
                Assert.Equal("G", payment.PaidFor);
            });
    }

    /// <summary>
    /// Verifies a source with the wrong color never pays a repeated colored cost.
    /// </summary>
    [Fact]
    public void Analyze_WithWrongColor_ReturnsPartialSpecificCoverage()
    {
        OnCurveManaCost cost = new(0, 0, 0, 0, 0, 2, 0);
        IReadOnlyList<OnCurveActiveSource> sources =
        [
            Source(1, OnCurveManaSymbol.Green),
            Source(2, OnCurveManaSymbol.Blue),
        ];

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, sources);

        Assert.Equal(1, result.SpecificSymbolsCovered);
        Assert.Equal(1, result.TotalSymbolsCovered);
        Assert.False(result.CoversSpecificCost(cost));
        Assert.False(result.CoversFullCost(cost));
    }

    /// <summary>
    /// Verifies a very large generic source fact does not overflow full-cost checks.
    /// </summary>
    [Fact]
    public void Analyze_WithLargeGenericCost_RemainsIncomplete()
    {
        OnCurveManaCost cost = new(int.MaxValue, 0, 0, 0, 0, 1, 0);
        IReadOnlyList<OnCurveActiveSource> sources = [Source(1, OnCurveManaSymbol.Green)];

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, sources);

        Assert.True(result.CoversSpecificCost(cost));
        Assert.False(result.CoversFullCost(cost));
    }

    /// <summary>
    /// Creates one active source with a stable entry ID for matching tests.
    /// </summary>
    private static OnCurveActiveSource Source(int id, params OnCurveManaSymbol[] symbols)
    {
        return new OnCurveActiveSource(OnCurveTestData.Id(id), Array.AsReadOnly(symbols));
    }
}
