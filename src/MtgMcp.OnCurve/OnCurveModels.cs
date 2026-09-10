namespace MtgMcp.OnCurve;

/// <summary>
/// States how the calculation may use one caller-identified land.
/// </summary>
internal enum OnCurveLandRule
{
    /// <summary>
    /// Allows the land to provide one listed mana color on the turn it is played.
    /// </summary>
    OneManaSameTurn,

    /// <summary>
    /// Allows the land to provide one listed mana color only after the turn it is played.
    /// </summary>
    OneManaNextTurn,

    /// <summary>
    /// Keeps the land in the library but never plays it as a mana source.
    /// </summary>
    NotModeled,
}

/// <summary>
/// Names the one mutually exclusive reason a completed modeled hand did not cast the target.
/// </summary>
internal enum OnCurveMissReason
{
    /// <summary>
    /// The target was not in hand by the final modeled turn.
    /// </summary>
    TargetNotDrawn,

    /// <summary>
    /// The target was in hand but no eligible modeled land was played.
    /// </summary>
    NoModeledLandPlayed,

    /// <summary>
    /// A modeled land was played but none was ready by the final modeled turn.
    /// </summary>
    SourceNotReady,

    /// <summary>
    /// Active sources could not pay every required colored or colorless symbol.
    /// </summary>
    MissingRequiredColor,

    /// <summary>
    /// Required colored or colorless symbols could be paid but generic mana was short.
    /// </summary>
    NotEnoughMana,
}

/// <summary>
/// Names one bounded event in a captured modeled hand.
/// </summary>
internal enum OnCurveTraceEventKind
{
    /// <summary>
    /// A card entered the modeled hand.
    /// </summary>
    Draw,

    /// <summary>
    /// A caller-modeled land was played.
    /// </summary>
    LandPlayed,

    /// <summary>
    /// A candidate land stayed in hand because the caller excluded it from the model.
    /// </summary>
    LandNotModeled,

    /// <summary>
    /// The target was cast under the declared model.
    /// </summary>
    TargetCast,

    /// <summary>
    /// The modeled hand reached the turn limit without casting the target.
    /// </summary>
    Miss,
}

/// <summary>
/// Records that the resolved card source did not contain a produced-mana field.
/// </summary>
internal sealed record OnCurveProducedManaMissing;

/// <summary>
/// Records that the resolved card source explicitly set produced-mana to null.
/// </summary>
internal sealed record OnCurveProducedManaNull;

/// <summary>
/// Records the ordered produced-mana values from one resolved card source.
/// </summary>
internal sealed record OnCurveProducedManaValues
{
    /// <summary>
    /// Creates one direct list of resolved produced-mana values.
    /// </summary>
    internal OnCurveProducedManaValues(IReadOnlyList<string> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Colors = Array.AsReadOnly(colors.ToArray());
    }

    /// <summary>
    /// Gets an immutable copy of the resolved produced-mana values.
    /// </summary>
    internal IReadOnlyList<string> Colors { get; }
}

/// <summary>
/// Preserves whether a resolved card source omitted, nullified, or listed mana colors.
/// </summary>
internal readonly union OnCurveProducedMana(
    OnCurveProducedManaMissing,
    OnCurveProducedManaNull,
    OnCurveProducedManaValues);

/// <summary>
/// Identifies the selected target and its exact printed mana cost.
/// </summary>
internal sealed record OnCurveTarget(
    Guid EntryId,
    Guid PrintingId,
    string? ManaCost);

/// <summary>
/// Identifies one resolved mainboard entry, its copy count, and its source fact.
/// </summary>
internal sealed record OnCurveDeckEntry(
    Guid EntryId,
    Guid PrintingId,
    int Quantity,
    OnCurveProducedMana ProducedMana);

/// <summary>
/// Associates one eligible land with the caller's stated model rule.
/// </summary>
internal sealed record OnCurveLandRuleInput(
    Guid EntryId,
    OnCurveLandRule Rule);

/// <summary>
/// Holds only the resolved values needed for one future on-curve calculation.
/// </summary>
internal sealed record OnCurveRequest(
    Guid DeckId,
    long DeckRevision,
    OnCurveTarget? Target,
    IReadOnlyList<OnCurveDeckEntry>? Mainboard,
    IReadOnlyList<Guid>? CandidateLandEntryIds,
    IReadOnlyList<OnCurveLandRuleInput>? LandRules,
    int TurnLimit,
    int SampleCount,
    bool OnThePlay,
    string Seed);

/// <summary>
/// Records one source used to pay one target-cost symbol in a captured trace.
/// </summary>
internal sealed record OnCurvePaymentSource(
    Guid EntryId,
    string PaidFor);

/// <summary>
/// Records one bounded event from a modeled hand trace.
/// </summary>
internal sealed record OnCurveTraceEvent(
    OnCurveTraceEventKind Kind,
    int Turn,
    Guid? EntryId = null,
    OnCurveLandRule? LandRule = null,
    OnCurveMissReason? MissReason = null,
    IReadOnlyList<OnCurvePaymentSource>? PaymentSources = null);

/// <summary>
/// Records one successful or unsuccessful modeled hand without changing later random values.
/// </summary>
internal sealed record OnCurveTrace(
    int TrialNumber,
    bool Succeeded,
    IReadOnlyList<OnCurveTraceEvent> Events,
    int OmittedEventCount);

/// <summary>
/// Records the lower and upper bounds of one two-sided Wilson interval.
/// </summary>
internal sealed record OnCurveWilsonInterval(
    double LowerBound,
    double UpperBound);

/// <summary>
/// Records the number of modeled misses with one stable final reason.
/// </summary>
internal sealed record OnCurveFailureCount(
    OnCurveMissReason Reason,
    int Count);

/// <summary>
/// Returns one complete repeatable sampled result from resolved deck and source facts.
/// </summary>
internal sealed record OnCurveRunReport(
    string ModelVersion,
    string PolicyId,
    string RandomVersion,
    string InputFingerprint,
    string Seed,
    int SampleCount,
    int CompletedTrialCount,
    int TurnLimit,
    bool OnThePlay,
    int SuccessCount,
    double SuccessRate,
    OnCurveWilsonInterval Interval,
    IReadOnlyList<OnCurveFailureCount> FailureCounts,
    IReadOnlyList<OnCurveTrace> Traces);
