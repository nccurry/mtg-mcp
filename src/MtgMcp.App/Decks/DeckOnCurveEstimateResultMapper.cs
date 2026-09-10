using MtgMcp.Core.Decks;
using MtgMcp.OnCurve;

namespace MtgMcp.App.Decks;

/// <summary>
/// Maps a completed pure estimate to the stable public MCP result.
/// </summary>
internal static class DeckOnCurveEstimateResultMapper
{
    /// <summary>
    /// Lists the fixed facts assumed by every version-one result.
    /// </summary>
    private static readonly IReadOnlyList<string> Assumptions =
    [
        "Seven-card opening hand; no mulligan.",
        "On the play skips the normal draw on turn one; on the draw takes it.",
        "At most one caller-modeled land is played each turn.",
        "Each modeled land supplies one listed mana color under its caller rule.",
        "Only generic, W, U, B, R, G, and C target-cost symbols are modeled.",
    ];

    /// <summary>
    /// Lists limits that keep the sampled result from being mistaken for a real game.
    /// </summary>
    private static readonly IReadOnlyList<string> Warnings =
    [
        "This is a sampled estimate for the stated model, not an exact probability or a real-game prediction.",
        "Mana creatures, mana rocks, treasure, rituals, fetch lands, tutors, cost changes, and extra land plays are not modeled.",
        "Multi-face cards and lands with no listed mana remain visible but are not modeled as mana sources.",
    ];

    /// <summary>
    /// Builds the stable public result around one completed pure calculation.
    /// </summary>
    internal static DeckOnCurveEstimateResult Map(
        DeckDocument deck,
        DeckOnCurvePreparedEstimate prepared,
        OnCurveRunReport report)
    {
        List<DeckOnCurveFailureCount> failures = [];
        foreach (OnCurveFailureCount failure in report.FailureCounts)
        {
            failures.Add(new DeckOnCurveFailureCount(DeckOnCurveText.MissReason(failure.Reason), failure.Count));
        }

        List<DeckOnCurveTrace> traces = [];
        foreach (OnCurveTrace trace in report.Traces)
        {
            traces.Add(MapTrace(trace));
        }

        return new DeckOnCurveEstimateResult(
            "sampled-on-curve-estimate",
            report.ModelVersion,
            report.PolicyId,
            report.RandomVersion,
            deck.DeckId,
            deck.Revision,
            prepared.Target,
            prepared.SourceFacts,
            prepared.SourceEvidence,
            prepared.LandRuleCoverage,
            report.InputFingerprint,
            report.Seed,
            report.SampleCount,
            report.CompletedTrialCount,
            report.TurnLimit,
            report.OnThePlay,
            report.SuccessCount,
            report.SuccessRate,
            new DeckOnCurveInterval("wilson-95", report.Interval.LowerBound, report.Interval.UpperBound),
            failures,
            Assumptions,
            Warnings,
            traces);
    }

    /// <summary>
    /// Maps one pure trace to its public output shape.
    /// </summary>
    private static DeckOnCurveTrace MapTrace(OnCurveTrace trace)
    {
        List<DeckOnCurveTraceEvent> events = [];
        foreach (OnCurveTraceEvent traceEvent in trace.Events)
        {
            List<DeckOnCurvePaymentSource> payments = [];
            foreach (OnCurvePaymentSource payment in traceEvent.PaymentSources ?? [])
            {
                payments.Add(new DeckOnCurvePaymentSource(payment.EntryId, payment.PaidFor));
            }

            events.Add(new DeckOnCurveTraceEvent(
                DeckOnCurveText.TraceEvent(traceEvent.Kind),
                traceEvent.Turn,
                traceEvent.EntryId,
                traceEvent.LandRule is OnCurveLandRule rule ? DeckOnCurveText.LandRule(rule) : null,
                traceEvent.MissReason is OnCurveMissReason missReason ? DeckOnCurveText.MissReason(missReason) : null,
                payments));
        }

        return new DeckOnCurveTrace(trace.TrialNumber, trace.Succeeded, events, trace.OmittedEventCount);
    }
}
