using System.Diagnostics;
using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Measures one fixed, representative on-curve calculation outside normal test runs.
/// </summary>
public sealed class OnCurveBenchmarkTests
{
    /// <summary>
    /// Runs ten thousand repeatable trials for a sanitized ninety-nine-card deck.
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]
    public void NinetyNineCardFixture_RecordsReleaseMeasurement()
    {
        OnCurveRequest request = CreateRequest();
        Stopwatch stopwatch = Stopwatch.StartNew();
        OnCurveRunReport report = RequireSuccess(
            OnCurveCalculator.Calculate(request, TestContext.Current.CancellationToken));
        stopwatch.Stop();

        Console.WriteLine(
            $"On-curve benchmark: {report.CompletedTrialCount} trials in {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.Equal(10_000, report.CompletedTrialCount);
        Assert.Equal("0123456789abcdef", report.Seed);
        Assert.Equal(10_000, report.SuccessCount + report.FailureCounts.Sum(value => value.Count));
    }

    /// <summary>
    /// Creates the fixed target, mana source, and inert filler deck used only for the release measurement.
    /// </summary>
    private static OnCurveRequest CreateRequest()
    {
        IReadOnlyList<OnCurveDeckEntry> mainboard =
        [
            OnCurveTestData.Entry(
                OnCurveTestData.TargetEntryId,
                OnCurveTestData.TargetPrintingId,
                1,
                OnCurveTestData.MissingMana()),
            OnCurveTestData.Entry(
                OnCurveTestData.GreenLandEntryId,
                OnCurveTestData.GreenLandPrintingId,
                38,
                OnCurveTestData.Mana("G")),
            OnCurveTestData.Entry(
                OnCurveTestData.FillerEntryId,
                OnCurveTestData.FillerPrintingId,
                60,
                OnCurveTestData.MissingMana()),
        ];
        return OnCurveTestData.Request(
            mainboard: mainboard,
            target: new OnCurveTarget(
                OnCurveTestData.TargetEntryId,
                OnCurveTestData.TargetPrintingId,
                "{2}{G}"),
            candidateLandEntryIds: [OnCurveTestData.GreenLandEntryId],
            landRules:
            [
                new OnCurveLandRuleInput(
                    OnCurveTestData.GreenLandEntryId,
                    OnCurveLandRule.OneManaSameTurn),
            ],
            turnLimit: 4,
            sampleCount: 10_000,
            onThePlay: true,
            seed: "0123456789abcdef");
    }

    /// <summary>
    /// Extracts a successful calculation report with a useful test failure when the benchmark input changes.
    /// </summary>
    private static OnCurveRunReport RequireSuccess(OperationResult<OnCurveRunReport> result)
    {
        Assert.True(result.Value is OperationSuccess<OnCurveRunReport>, result.Value.ToString());
        return Assert.IsType<OperationSuccess<OnCurveRunReport>>(result.Value).Data;
    }
}
