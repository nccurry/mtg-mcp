using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve.Tests;

/// <summary>
/// Verifies complete sampled reports, replay, cancellation, and interval math.
/// </summary>
public sealed class OnCurveCalculatorTests
{
    /// <summary>
    /// Verifies one seed and one complete resolved request always produce the same report.
    /// </summary>
    [Fact]
    public void Calculate_WithSameSeed_ReturnsSameReport()
    {
        OnCurveRequest request = MixedRequest("0123456789abcdef");

        OnCurveRunReport first = Calculate(request);
        OnCurveRunReport second = Calculate(request);

        Assert.Equal(ReportSignature(first), ReportSignature(second));
        Assert.Equal(OnCurveCalculator.ModelVersion, first.ModelVersion);
        Assert.Equal(OnCurveCalculator.PolicyId, first.PolicyId);
        Assert.Equal(OnCurveCalculator.RandomVersion, first.RandomVersion);
        Assert.Equal("0123456789abcdef", first.Seed);
        Assert.InRange(first.Traces.Count, 1, 2);
        Assert.All(first.Traces, trace => Assert.InRange(trace.Events.Count, 1, 64));
    }

    /// <summary>
    /// Verifies independent concurrent calculations keep their random state separate.
    /// </summary>
    [Fact]
    public async Task Calculate_Concurrently_KeepsEachSeedRepeatable()
    {
        OnCurveRequest firstRequest = MixedRequest("0000000000000001");
        OnCurveRequest secondRequest = MixedRequest("ffffffffffffffff");

        Task<OnCurveRunReport> firstTask = Task.Run(() => Calculate(firstRequest));
        Task<OnCurveRunReport> secondTask = Task.Run(() => Calculate(secondRequest));
        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(ReportSignature(Calculate(firstRequest)), ReportSignature(firstTask.Result));
        Assert.Equal(ReportSignature(Calculate(secondRequest)), ReportSignature(secondTask.Result));
    }

    /// <summary>
    /// Verifies unsupported target symbols return a typed failure before any sample completes.
    /// </summary>
    [Fact]
    public void Calculate_WithUnsupportedTargetCost_ReturnsUnsupported()
    {
        OnCurveRequest request = OnCurveTestData.Request(
            target: new OnCurveTarget(
                OnCurveTestData.TargetEntryId,
                OnCurveTestData.TargetPrintingId,
                "{X}{G}"));

        OperationResult<OnCurveRunReport> result = OnCurveCalculator.Calculate(request);

        OperationUnsupported unsupported = Assert.IsType<OperationUnsupported>(result.Value);
        Assert.Equal("unsupported-on-curve-mana-cost", unsupported.ReasonCode);
    }

    /// <summary>
    /// Verifies unsupported direct source values return a typed failure instead of a guessed source.
    /// </summary>
    [Fact]
    public void Calculate_WithUnsupportedSourceMana_ReturnsUnsupported()
    {
        OnCurveRequest request = OnCurveTestData.Request(
            mainboard:
            [
                OnCurveTestData.Entry(
                    OnCurveTestData.TargetEntryId,
                    OnCurveTestData.TargetPrintingId,
                    1,
                    OnCurveTestData.MissingMana()),
                OnCurveTestData.Entry(
                    OnCurveTestData.GreenLandEntryId,
                    OnCurveTestData.GreenLandPrintingId,
                    6,
                    OnCurveTestData.Mana("S")),
            ]);

        OperationResult<OnCurveRunReport> result = OnCurveCalculator.Calculate(request);

        OperationUnsupported unsupported = Assert.IsType<OperationUnsupported>(result.Value);
        Assert.Equal("unsupported-on-curve-source-mana", unsupported.ReasonCode);
    }

    /// <summary>
    /// Verifies cancellation after completed trial 17 produces no partial report.
    /// </summary>
    [Fact]
    public void Calculate_WhenCanceledBeforeTrial18_Throws()
    {
        using CancellationTokenSource cancellation = new();
        OnCurveRequest request = MixedRequest("0123456789abcdef");

        Assert.Throws<OperationCanceledException>(
            () => OnCurveCalculator.Calculate(
                request,
                trialNumber =>
                {
                    if (trialNumber == 18)
                    {
                        cancellation.Cancel();
                    }
                },
                cancellation.Token));
    }

    /// <summary>
    /// Verifies reports retain every miss label, including zero-count outcomes.
    /// </summary>
    [Fact]
    public void Calculate_WithNoModeledLand_ReportsEveryMissReason()
    {
        OnCurveRequest request = OnCurveTestData.Request(
            mainboard:
            [
                OnCurveTestData.Entry(
                    OnCurveTestData.TargetEntryId,
                    OnCurveTestData.TargetPrintingId,
                    1,
                    OnCurveTestData.MissingMana()),
                OnCurveTestData.Entry(
                    OnCurveTestData.FillerEntryId,
                    OnCurveTestData.FillerPrintingId,
                    7,
                    OnCurveTestData.MissingMana()),
            ],
            candidateLandEntryIds: [],
            landRules: []);

        OnCurveRunReport report = Calculate(request);

        Assert.Equal(0, report.SuccessCount);
        Assert.Equal(0, report.SuccessRate);
        Assert.Equal(100, report.FailureCounts.Sum(item => item.Count));
        Assert.Equal(Enum.GetValues<OnCurveMissReason>(), report.FailureCounts.Select(item => item.Reason));
        Assert.Equal(0, report.Interval.LowerBound);
        Assert.Equal(0.036993498206985678, report.Interval.UpperBound, 15);
    }

    /// <summary>
    /// Verifies a fully successful zero-cost target has the exact full-sample interval bound.
    /// </summary>
    [Fact]
    public void Calculate_WithAlwaysCastTarget_ReportsFullSuccess()
    {
        OnCurveRequest request = OnCurveTestData.Request(
            mainboard:
            [
                OnCurveTestData.Entry(
                    OnCurveTestData.TargetEntryId,
                    OnCurveTestData.TargetPrintingId,
                    7,
                    OnCurveTestData.MissingMana()),
            ],
            target: new OnCurveTarget(
                OnCurveTestData.TargetEntryId,
                OnCurveTestData.TargetPrintingId,
                "{0}"),
            candidateLandEntryIds: [],
            landRules: []);

        OnCurveRunReport report = Calculate(request);

        Assert.Equal(100, report.SuccessCount);
        Assert.Equal(1, report.SuccessRate);
        Assert.Equal(0.96300650179301428, report.Interval.LowerBound, 15);
        Assert.Equal(1, report.Interval.UpperBound);
    }

    /// <summary>
    /// Verifies Wilson interval references for empty, full, and mixed samples.
    /// </summary>
    [Theory]
    [InlineData(0, 100, 0, 0.036993498206985678)]
    [InlineData(100, 100, 0.96300650179301428, 1)]
    [InlineData(50, 100, 0.40383153036599562, 0.59616846963400438)]
    public void WilsonIntervalCalculator_WithKnownCounts_ReturnsReferenceBounds(
        int successes,
        int samples,
        double expectedLower,
        double expectedUpper)
    {
        OnCurveWilsonInterval result = OnCurveWilsonIntervalCalculator.Calculate(successes, samples);

        Assert.Equal(expectedLower, result.LowerBound, 15);
        Assert.Equal(expectedUpper, result.UpperBound, 15);
    }

    /// <summary>
    /// Verifies invalid sample totals and success counts cannot make an interval.
    /// </summary>
    [Theory]
    [InlineData(-1, 100)]
    [InlineData(101, 100)]
    [InlineData(0, 0)]
    public void WilsonIntervalCalculator_WithInvalidCounts_Throws(int successes, int samples)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OnCurveWilsonIntervalCalculator.Calculate(successes, samples));
    }

    /// <summary>
    /// Creates a compact 8-card request that yields both sampled successes and misses.
    /// </summary>
    private static OnCurveRequest MixedRequest(string seed)
    {
        return OnCurveTestData.Request(
            mainboard:
            [
                OnCurveTestData.Entry(
                    OnCurveTestData.TargetEntryId,
                    OnCurveTestData.TargetPrintingId,
                    1,
                    OnCurveTestData.MissingMana()),
                OnCurveTestData.Entry(
                    OnCurveTestData.GreenLandEntryId,
                    OnCurveTestData.GreenLandPrintingId,
                    7,
                    OnCurveTestData.Mana("G")),
            ],
            sampleCount: 100,
            seed: seed);
    }

    /// <summary>
    /// Returns one stable comparison string for all visible parts of a sampled report.
    /// </summary>
    private static string ReportSignature(OnCurveRunReport report)
    {
        List<string> values =
        [
            report.ModelVersion,
            report.PolicyId,
            report.RandomVersion,
            report.InputFingerprint,
            report.Seed,
            report.SampleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            report.CompletedTrialCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            report.TurnLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            report.OnThePlay.ToString(),
            report.SuccessCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            report.SuccessRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            report.Interval.LowerBound.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            report.Interval.UpperBound.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        ];
        foreach (OnCurveFailureCount failure in report.FailureCounts)
        {
            values.Add($"{failure.Reason}:{failure.Count}");
        }

        foreach (OnCurveTrace trace in report.Traces)
        {
            values.Add($"{trace.TrialNumber}:{trace.Succeeded}:{trace.OmittedEventCount}");
            foreach (OnCurveTraceEvent traceEvent in trace.Events)
            {
                values.Add(
                    $"{traceEvent.Kind}:{traceEvent.Turn}:{traceEvent.EntryId}:{traceEvent.LandRule}:{traceEvent.MissReason}");
                foreach (OnCurvePaymentSource payment in traceEvent.PaymentSources ?? [])
                {
                    values.Add($"{payment.EntryId}:{payment.PaidFor}");
                }
            }
        }

        return string.Join('|', values);
    }

    /// <summary>
    /// Gets a successful sampled report or fails the test with its typed error.
    /// </summary>
    private static OnCurveRunReport Calculate(OnCurveRequest request)
    {
        OperationResult<OnCurveRunReport> result = OnCurveCalculator.Calculate(request);
        return Assert.IsType<OperationSuccess<OnCurveRunReport>>(result.Value).Data;
    }
}
