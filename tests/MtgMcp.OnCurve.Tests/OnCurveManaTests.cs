using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies printed mana-cost reading and the one exact source-to-symbol payment matcher.
/// </summary>
public sealed class OnCurveManaTests
{
    /// <summary>
    /// Verifies a printed cost keeps every supported generic and specific symbol.
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
        Assert.False(cost.HasPhyrexian);
    }

    /// <summary>
    /// Verifies hybrid and Phyrexian symbols are read as symbols that listed colors can pay.
    /// </summary>
    [Fact]
    public void ReadCost_WithHybridAndPhyrexianSymbols_ListsTheirColors()
    {
        OperationResult<OnCurveManaCost> result = OnCurveManaSymbols.ReadCost("{1}{W/U}{G/P}{B/R/P}");

        OnCurveManaCost cost = Assert.IsType<OperationSuccess<OnCurveManaCost>>(result.Value).Data;
        Assert.Equal(1, cost.Generic);
        Assert.Equal(3, cost.SpecificSymbolCount);
        Assert.True(cost.HasPhyrexian);
        Assert.Collection(
            cost.EitherSymbols,
            symbol => Assert.Equal([OnCurveManaSymbol.White, OnCurveManaSymbol.Blue], symbol),
            symbol => Assert.Equal([OnCurveManaSymbol.Green], symbol),
            symbol => Assert.Equal([OnCurveManaSymbol.Black, OnCurveManaSymbol.Red], symbol));
    }

    /// <summary>
    /// Verifies unsupported or malformed costs are refused with one reason code.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{X}{G}")]
    [InlineData("{2/W}")]
    [InlineData("{C/P}")]
    [InlineData("{W/W}")]
    [InlineData("{W/U/R}")]
    [InlineData("{P}{G}")]
    [InlineData("{S}")]
    [InlineData("{G")]
    [InlineData("G")]
    [InlineData("{ 1}")]
    [InlineData("{99999999999}")]
    public void ReadCost_WithUnsupportedText_ReturnsUnsupported(string? manaCost)
    {
        OperationResult<OnCurveManaCost> result = OnCurveManaSymbols.ReadCost(manaCost);

        OperationUnsupported unsupported = Assert.IsType<OperationUnsupported>(result.Value);
        Assert.Equal("unsupported-mana-cost", unsupported.ReasonCode);
    }

    /// <summary>
    /// Verifies a cost text is bounded in length, symbols, and generic mana, so a payment test has bounded cost and
    /// no sum can overflow.
    /// </summary>
    [Theory]
    [InlineData("{2147483647}")]
    [InlineData("{100}")]
    [InlineData("{50}{50}")]
    [InlineData("{W}{W}{W}{W}{W}{W}{W}{W}{U}{U}{U}{U}{U}{U}{U}{U}{B}")]
    [InlineData("{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}{W/U}")]
    public void ReadCost_WithAnUnboundedCost_ReturnsUnsupported(string manaCost)
    {
        Assert.IsType<OperationUnsupported>(OnCurveManaSymbols.ReadCost(manaCost).Value);
    }

    /// <summary>
    /// Verifies the largest allowed costs are read.
    /// </summary>
    [Fact]
    public void ReadCost_AtTheLimits_IsRead()
    {
        OnCurveManaCost generic = Assert.IsType<OperationSuccess<OnCurveManaCost>>(
            OnCurveManaSymbols.ReadCost("{99}").Value).Data;
        OnCurveManaCost symbols = Assert.IsType<OperationSuccess<OnCurveManaCost>>(
            OnCurveManaSymbols.ReadCost(string.Concat(Enumerable.Repeat("{W}", 16))).Value).Data;

        Assert.Equal(99, generic.Generic);
        Assert.Equal(16, symbols.SpecificSymbolCount);
    }

    /// <summary>
    /// Verifies extra generic mana added to a huge cost saturates instead of wrapping negative.
    /// </summary>
    [Fact]
    public void WithExtraGeneric_OnAHugeCost_DoesNotWrap()
    {
        OnCurveManaCost cost = new OnCurveManaCost(int.MaxValue, 0, 0, 0, 0, 0, 0).WithExtraGeneric(5);

        Assert.Equal(int.MaxValue, cost.Generic);
        Assert.False(OnCurveManaPayment.Analyze(cost, [Source(OnCurveManaSymbol.Green)]).CoversFullCost(cost));
        Assert.False(OnCurveManaPayment.Analyze(
            new OnCurveManaCost(int.MaxValue, 0, 0, 0, 0, 0, 0),
            [Source(OnCurveManaSymbol.Green)],
            new OnCurveMatchScratch(),
            extraGeneric: 5).IsComplete);
    }

    /// <summary>
    /// Verifies one scratch can be reused for runs of different sizes without a stale assignment.
    /// </summary>
    [Fact]
    public void Analyze_ReusingScratch_GivesTheSameAnswers()
    {
        OnCurveMatchScratch scratch = new();
        OnCurveManaCost cost = new(0, 0, 0, 0, 0, 2, 0);
        IReadOnlyList<OnCurveActiveSource> many = [
            .. Enumerable.Range(0, 40).Select(_ => Source(OnCurveManaSymbol.Green))];
        IReadOnlyList<OnCurveActiveSource> one = [Source(OnCurveManaSymbol.Green)];

        Assert.True(OnCurveManaPayment.Analyze(cost, many, scratch).IsComplete);
        Assert.False(OnCurveManaPayment.Analyze(cost, one, scratch).IsComplete);
        Assert.True(OnCurveManaPayment.Analyze(cost, many, scratch).IsComplete);
    }

    /// <summary>
    /// Verifies direct Scryfall mana strings have one fixed supported set.
    /// </summary>
    [Theory]
    [InlineData("W", 0)]
    [InlineData("U", 1)]
    [InlineData("B", 2)]
    [InlineData("R", 3)]
    [InlineData("G", 4)]
    [InlineData("C", 5)]
    public void ProducedMana_WithSupportedValue_Parses(string value, int expectedValue)
    {
        bool parsed = OnCurveManaSymbols.TryParseProducedMana(value, out OnCurveManaSymbol symbol);

        Assert.True(parsed);
        Assert.Equal((OnCurveManaSymbol)expectedValue, symbol);
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
    /// Verifies matching reserves flexible sources for required colors before generic payment.
    /// </summary>
    [Fact]
    public void Analyze_WithSpecificAndGenericCost_UsesOneSourcePerSymbol()
    {
        OnCurveManaCost cost = new(1, 0, 0, 0, 0, 2, 0);
        IReadOnlyList<OnCurveActiveSource> sources =
        [
            Source(OnCurveManaSymbol.Green),
            Source(OnCurveManaSymbol.Blue),
            Source(OnCurveManaSymbol.Green),
        ];

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, sources);

        Assert.Equal(2, result.SpecificSymbolsCovered);
        Assert.Equal(3, result.TotalSymbolsCovered);
        Assert.Equal(cost.SpecificSymbolCount, result.SpecificSymbolsCovered);
        Assert.True(result.CoversFullCost(cost));
    }

    /// <summary>
    /// Verifies a source with the wrong color never pays a repeated colored cost.
    /// </summary>
    [Fact]
    public void Analyze_WithWrongColor_ReturnsPartialSpecificCoverage()
    {
        OnCurveManaCost cost = new(0, 0, 0, 0, 0, 2, 0);
        IReadOnlyList<OnCurveActiveSource> sources = [Source(OnCurveManaSymbol.Green), Source(OnCurveManaSymbol.Blue)];

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, sources);

        Assert.Equal(1, result.SpecificSymbolsCovered);
        Assert.Equal(1, result.TotalSymbolsCovered);
        Assert.NotEqual(cost.SpecificSymbolCount, result.SpecificSymbolsCovered);
        Assert.False(result.CoversFullCost(cost));
    }

    /// <summary>
    /// Verifies a dual source is moved to the symbol only it can pay, by an augmenting path.
    /// </summary>
    [Fact]
    public void Analyze_WithADualSource_FindsTheOnlyCompleteAssignment()
    {
        OnCurveManaCost cost = new(0, 0, 0, 1, 0, 1, 0);
        IReadOnlyList<OnCurveActiveSource> sources =
        [
            Source(OnCurveManaSymbol.Black, OnCurveManaSymbol.Green),
            Source(OnCurveManaSymbol.Black),
        ];

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, sources);

        Assert.True(result.CoversFullCost(cost));
    }

    /// <summary>
    /// Verifies a hybrid symbol is paid by either of its colors and a Phyrexian symbol only by its color.
    /// </summary>
    [Theory]
    [InlineData("{W/U}", "U", true)]
    [InlineData("{W/U}", "W", true)]
    [InlineData("{W/U}", "G", false)]
    [InlineData("{G/P}", "G", true)]
    [InlineData("{G/P}", "U", false)]
    public void Analyze_WithEitherSymbol_AcceptsOnlyItsColors(string manaCost, string color, bool expected)
    {
        OnCurveManaCost cost = Assert.IsType<OperationSuccess<OnCurveManaCost>>(
            OnCurveManaSymbols.ReadCost(manaCost).Value).Data;
        Assert.True(OnCurveManaSymbols.TryParseProducedMana(color, out OnCurveManaSymbol symbol));

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, [Source(symbol)]);

        Assert.Equal(expected, result.CoversFullCost(cost));
    }

    /// <summary>
    /// Verifies extra generic mana, such as mana already spent, makes a cost harder to pay.
    /// </summary>
    [Fact]
    public void Analyze_WithExtraGeneric_NeedsMoreSources()
    {
        OnCurveManaCost cost = new OnCurveManaCost(0, 0, 0, 0, 0, 1, 0).WithExtraGeneric(2);
        IReadOnlyList<OnCurveActiveSource> three =
            [Source(OnCurveManaSymbol.Green), Source(OnCurveManaSymbol.Blue), Source()];
        IReadOnlyList<OnCurveActiveSource> two = [Source(OnCurveManaSymbol.Green), Source(OnCurveManaSymbol.Blue)];

        Assert.True(OnCurveManaPayment.Analyze(cost, three).CoversFullCost(cost));
        Assert.False(OnCurveManaPayment.Analyze(cost, two).CoversFullCost(cost));
    }

    /// <summary>
    /// Verifies a very large generic source fact does not overflow full-cost checks.
    /// </summary>
    [Fact]
    public void Analyze_WithLargeGenericCost_RemainsIncomplete()
    {
        OnCurveManaCost cost = new(int.MaxValue, 0, 0, 0, 0, 1, 0);
        IReadOnlyList<OnCurveActiveSource> sources = [Source(OnCurveManaSymbol.Green)];

        OnCurvePaymentAnalysis result = OnCurveManaPayment.Analyze(cost, sources);

        Assert.Equal(cost.SpecificSymbolCount, result.SpecificSymbolsCovered);
        Assert.False(result.CoversFullCost(cost));
    }

    /// <summary>
    /// Creates one unit of mana with the given colors for matching tests.
    /// </summary>
    private static OnCurveActiveSource Source(params OnCurveManaSymbol[] symbols)
    {
        return new OnCurveActiveSource(Array.AsReadOnly(symbols));
    }
}
