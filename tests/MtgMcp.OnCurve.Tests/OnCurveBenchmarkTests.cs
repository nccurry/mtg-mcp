using System.Diagnostics;
using MtgMcp.Core.Results;
using static MtgMcp.OnCurve.Tests.OnCurveTestData;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Measures one fixed, representative sampling run outside normal test runs.
/// </summary>
public sealed class OnCurveBenchmarkTests
{
    /// <summary>
    /// Runs ten thousand repeatable samples for a ninety-nine-card deck with one target card and 38 green lands:
    /// the input the original on-curve estimator was measured with, written as the rules of the on-curve preset.
    /// The original estimator counted 796 samples in which the target was cast by turn 4.
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]
    public void NinetyNineCardFixture_RecordsReleaseMeasurement()
    {
        SimulationRequest request = Request(
            [Card(1), Card(2, 38), Card(3, 60)],
            [Colored("lands", ["G"], [2])],
            [Play("lands", SimulationPick.TargetFirst, targetCost: "{2}{G}")],
            [Condition("on-curve", new SimulationCanCast("{2}{G}", null, 4, Id(1)))],
            turns: 4,
            samples: 10_000,
            seed: 0x0123456789abcdefUL);

        Stopwatch stopwatch = Stopwatch.StartNew();
        OperationResult<SimulationReport> result = SimulationEngine.Run(request, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        SimulationReport report = Assert.IsType<OperationSuccess<SimulationReport>>(result.Value).Data;
        Console.WriteLine(
            $"Simulation benchmark: {report.Samples} samples in {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.Equal(10_000, report.Samples);
        Assert.Equal(796, report.Results[0].Hits);
    }
}
