namespace MtgMcp.OnCurve;

/// <summary>
/// Represents one physical card copy in a modeled library and hand.
/// </summary>
internal sealed record OnCurveLibraryCard(
    Guid EntryId,
    IReadOnlyList<OnCurveManaSymbol> SourceColors);

/// <summary>
/// Runs one shuffled hand through the fixed target-first-v1 turn policy.
/// </summary>
internal static class OnCurveTrialRunner
{
    /// <summary>
    /// Runs one complete trial and optionally records its bounded trace.
    /// </summary>
    internal static OnCurveTrialOutcome Run(
        IReadOnlyList<OnCurveLibraryCard> libraryTemplate,
        Guid targetEntryId,
        IReadOnlyDictionary<Guid, OnCurveLandRule> landRules,
        OnCurveManaCost targetCost,
        int turnLimit,
        bool onThePlay,
        SplitMix64Random random,
        int trialNumber,
        bool captureTrace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(libraryTemplate);
        ArgumentNullException.ThrowIfNull(random);

        List<OnCurveLibraryCard> library = [.. libraryTemplate];
        random.Shuffle(library, cancellationToken);
        return RunLibrary(
            library,
            targetEntryId,
            landRules,
            targetCost,
            turnLimit,
            onThePlay,
            trialNumber,
            captureTrace,
            cancellationToken);
    }

    /// <summary>
    /// Runs one trial from the supplied library order without changing that order.
    /// </summary>
    internal static OnCurveTrialOutcome RunWithLibraryOrder(
        IReadOnlyList<OnCurveLibraryCard> libraryTemplate,
        Guid targetEntryId,
        IReadOnlyDictionary<Guid, OnCurveLandRule> landRules,
        OnCurveManaCost targetCost,
        int turnLimit,
        bool onThePlay,
        int trialNumber,
        bool captureTrace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(libraryTemplate);
        return RunLibrary(
            [.. libraryTemplate],
            targetEntryId,
            landRules,
            targetCost,
            turnLimit,
            onThePlay,
            trialNumber,
            captureTrace,
            cancellationToken);
    }

    /// <summary>
    /// Applies the fixed turn model to an already ordered library.
    /// </summary>
    private static OnCurveTrialOutcome RunLibrary(
        IReadOnlyList<OnCurveLibraryCard> library,
        Guid targetEntryId,
        IReadOnlyDictionary<Guid, OnCurveLandRule> landRules,
        OnCurveManaCost targetCost,
        int turnLimit,
        bool onThePlay,
        int trialNumber,
        bool captureTrace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(landRules);
        ArgumentNullException.ThrowIfNull(targetCost);

        List<OnCurveLibraryCard> hand = [];
        List<OnCurvePlayedLand> playedLands = [];
        HashSet<Guid> recordedNotModeledEntries = [];
        OnCurveTraceRecorder? trace = captureTrace ? new OnCurveTraceRecorder(trialNumber) : null;
        int nextLibraryIndex = 0;
        for (int index = 0; index < 7; index++)
        {
            DrawCard(library, hand, ref nextLibraryIndex, 0, trace, cancellationToken);
        }

        for (int turn = 1; turn <= turnLimit; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (turn > 1 || !onThePlay)
            {
                DrawCard(library, hand, ref nextLibraryIndex, turn, trace, cancellationToken);
            }

            RecordNotModeledLands(
                hand,
                landRules,
                recordedNotModeledEntries,
                turn,
                trace,
                cancellationToken);
            LandChoice? choice = ChooseLand(hand, playedLands, landRules, targetCost, turn);
            if (choice is not null)
            {
                OnCurveLibraryCard land = hand[choice.HandIndex];
                hand.RemoveAt(choice.HandIndex);
                int readyTurn = choice.Rule == OnCurveLandRule.OneManaSameTurn ? turn : turn + 1;
                playedLands.Add(new OnCurvePlayedLand(land.EntryId, land.SourceColors, readyTurn));
                trace?.Add(
                    new OnCurveTraceEvent(
                        OnCurveTraceEventKind.LandPlayed,
                        turn,
                        land.EntryId,
                        choice.Rule),
                    cancellationToken);
            }

            if (!ContainsTarget(hand, targetEntryId))
            {
                continue;
            }

            IReadOnlyList<OnCurveActiveSource> activeSources = ActiveSources(playedLands, turn);
            OnCurvePaymentAnalysis payment = OnCurveManaPayment.Analyze(targetCost, activeSources);
            if (!payment.CoversFullCost(targetCost))
            {
                continue;
            }

            trace?.Add(
                new OnCurveTraceEvent(
                    OnCurveTraceEventKind.TargetCast,
                    turn,
                    targetEntryId,
                    PaymentSources: payment.PaymentSources),
                cancellationToken);
            return new OnCurveTrialOutcome(true, null, trace?.Complete(true));
        }

        OnCurveMissReason missReason = DetermineMissReason(
            hand,
            targetEntryId,
            playedLands,
            landRules,
            targetCost,
            turnLimit);
        trace?.Add(
            new OnCurveTraceEvent(OnCurveTraceEventKind.Miss, turnLimit, MissReason: missReason),
            cancellationToken);
        return new OnCurveTrialOutcome(false, missReason, trace?.Complete(false));
    }

    /// <summary>
    /// Adds one card from the shuffled library to the modeled hand when a card remains.
    /// </summary>
    private static void DrawCard(
        IReadOnlyList<OnCurveLibraryCard> library,
        List<OnCurveLibraryCard> hand,
        ref int nextLibraryIndex,
        int turn,
        OnCurveTraceRecorder? trace,
        CancellationToken cancellationToken)
    {
        if (nextLibraryIndex >= library.Count)
        {
            return;
        }

        OnCurveLibraryCard card = library[nextLibraryIndex++];
        hand.Add(card);
        trace?.Add(new OnCurveTraceEvent(OnCurveTraceEventKind.Draw, turn, card.EntryId), cancellationToken);
    }

    /// <summary>
    /// Records caller-excluded candidate lands that were available to be considered this turn.
    /// </summary>
    private static void RecordNotModeledLands(
        IReadOnlyList<OnCurveLibraryCard> hand,
        IReadOnlyDictionary<Guid, OnCurveLandRule> landRules,
        ISet<Guid> recordedEntryIds,
        int turn,
        OnCurveTraceRecorder? trace,
        CancellationToken cancellationToken)
    {
        if (trace is null)
        {
            return;
        }

        foreach (OnCurveLibraryCard card in hand)
        {
            if (landRules.TryGetValue(card.EntryId, out OnCurveLandRule rule) &&
                rule == OnCurveLandRule.NotModeled &&
                recordedEntryIds.Add(card.EntryId))
            {
                trace.Add(
                    new OnCurveTraceEvent(
                        OnCurveTraceEventKind.LandNotModeled,
                        turn,
                        card.EntryId,
                        rule),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Selects one modeled land by target-cost progress, readiness, and stable entry identity.
    /// </summary>
    private static LandChoice? ChooseLand(
        IReadOnlyList<OnCurveLibraryCard> hand,
        IReadOnlyList<OnCurvePlayedLand> playedLands,
        IReadOnlyDictionary<Guid, OnCurveLandRule> landRules,
        OnCurveManaCost targetCost,
        int turn)
    {
        LandChoice? best = null;
        for (int handIndex = 0; handIndex < hand.Count; handIndex++)
        {
            OnCurveLibraryCard card = hand[handIndex];
            if (!landRules.TryGetValue(card.EntryId, out OnCurveLandRule rule) ||
                rule == OnCurveLandRule.NotModeled)
            {
                continue;
            }

            int readyTurn = rule == OnCurveLandRule.OneManaSameTurn ? turn : turn + 1;
            List<OnCurvePlayedLand> boardAfterPlay = new(playedLands)
            {
                new OnCurvePlayedLand(card.EntryId, card.SourceColors, readyTurn),
            };
            OnCurvePaymentAnalysis payment = OnCurveManaPayment.Analyze(
                targetCost,
                ActiveSources(boardAfterPlay, turn));
            LandChoice candidate = new(
                handIndex,
                card.EntryId,
                rule,
                payment.SpecificSymbolsCovered,
                payment.TotalSymbolsCovered);
            if (best is null || IsBetter(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Applies the declared target-first-v1 tie-break order to two land choices.
    /// </summary>
    private static bool IsBetter(LandChoice candidate, LandChoice current)
    {
        int comparison = candidate.SpecificSymbolsCovered.CompareTo(current.SpecificSymbolsCovered);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        comparison = candidate.TotalSymbolsCovered.CompareTo(current.TotalSymbolsCovered);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        bool candidateSameTurn = candidate.Rule == OnCurveLandRule.OneManaSameTurn;
        bool currentSameTurn = current.Rule == OnCurveLandRule.OneManaSameTurn;
        if (candidateSameTurn != currentSameTurn)
        {
            return candidateSameTurn;
        }

        return CompareIds(candidate.EntryId, current.EntryId) < 0;
    }

    /// <summary>
    /// Builds a stable active-source list for the current modeled turn.
    /// </summary>
    private static IReadOnlyList<OnCurveActiveSource> ActiveSources(
        IReadOnlyList<OnCurvePlayedLand> playedLands,
        int turn)
    {
        List<OnCurveActiveSource> sources = [];
        foreach (OnCurvePlayedLand land in playedLands)
        {
            if (land.ReadyTurn <= turn)
            {
                sources.Add(new OnCurveActiveSource(land.EntryId, land.SourceColors));
            }
        }

        sources.Sort(static (left, right) => CompareIds(left.EntryId, right.EntryId));
        return sources;
    }

    /// <summary>
    /// Applies the fixed priority order for completed unsuccessful trials.
    /// </summary>
    private static OnCurveMissReason DetermineMissReason(
        IReadOnlyList<OnCurveLibraryCard> hand,
        Guid targetEntryId,
        IReadOnlyList<OnCurvePlayedLand> playedLands,
        IReadOnlyDictionary<Guid, OnCurveLandRule> landRules,
        OnCurveManaCost targetCost,
        int turnLimit)
    {
        if (!ContainsTarget(hand, targetEntryId))
        {
            return OnCurveMissReason.TargetNotDrawn;
        }

        if (!playedLands.Any(land => landRules.TryGetValue(land.EntryId, out OnCurveLandRule rule) &&
                                    rule is OnCurveLandRule.OneManaSameTurn or OnCurveLandRule.OneManaNextTurn))
        {
            return OnCurveMissReason.NoModeledLandPlayed;
        }

        IReadOnlyList<OnCurveActiveSource> activeSources = ActiveSources(playedLands, turnLimit);
        if (activeSources.Count == 0)
        {
            return OnCurveMissReason.SourceNotReady;
        }

        OnCurvePaymentAnalysis payment = OnCurveManaPayment.Analyze(targetCost, activeSources);
        return payment.CoversSpecificCost(targetCost)
            ? OnCurveMissReason.NotEnoughMana
            : OnCurveMissReason.MissingRequiredColor;
    }

    /// <summary>
    /// Reports whether the modeled hand currently includes any copy of the selected target entry.
    /// </summary>
    private static bool ContainsTarget(IReadOnlyList<OnCurveLibraryCard> hand, Guid targetEntryId)
    {
        foreach (OnCurveLibraryCard card in hand)
        {
            if (card.EntryId == targetEntryId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Compares entry identities in their public stable string form.
    /// </summary>
    private static int CompareIds(Guid left, Guid right)
    {
        return string.CompareOrdinal(left.ToString("D"), right.ToString("D"));
    }
}

/// <summary>
/// Stores one selected land and its target-first-v1 comparison values.
/// </summary>
internal sealed record LandChoice(
    int HandIndex,
    Guid EntryId,
    OnCurveLandRule Rule,
    int SpecificSymbolsCovered,
    int TotalSymbolsCovered);

/// <summary>
/// Stores one played modeled land and the first turn on which it supplies mana.
/// </summary>
internal sealed record OnCurvePlayedLand(
    Guid EntryId,
    IReadOnlyList<OnCurveManaSymbol> SourceColors,
    int ReadyTurn);

/// <summary>
/// Stores the terminal outcome of one modeled hand and its optional bounded trace.
/// </summary>
internal sealed record OnCurveTrialOutcome(
    bool Succeeded,
    OnCurveMissReason? MissReason,
    OnCurveTrace? Trace);

/// <summary>
/// Collects a fixed number of trace events without changing the trial's random sequence.
/// </summary>
internal sealed class OnCurveTraceRecorder
{
    /// <summary>
    /// Defines the largest number of stored events in one trace.
    /// </summary>
    private const int MaximumEvents = 64;

    /// <summary>
    /// Stores events that fit inside the public trace limit.
    /// </summary>
    private readonly List<OnCurveTraceEvent> events = [];

    /// <summary>
    /// Identifies the sampled hand that owns this trace.
    /// </summary>
    private readonly int trialNumber;

    /// <summary>
    /// Counts terminal or intermediate events omitted after the fixed storage cap.
    /// </summary>
    private int omittedEventCount;

    /// <summary>
    /// Starts one recorder for a known one-based trial number.
    /// </summary>
    internal OnCurveTraceRecorder(int trialNumber)
    {
        this.trialNumber = trialNumber;
    }

    /// <summary>
    /// Stores one event when capacity remains, otherwise records only its omission.
    /// </summary>
    internal void Add(OnCurveTraceEvent traceEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (events.Count < MaximumEvents)
        {
            events.Add(traceEvent);
        }
        else
        {
            omittedEventCount++;
        }
    }

    /// <summary>
    /// Creates the immutable terminal trace for the completed hand.
    /// </summary>
    internal OnCurveTrace Complete(bool succeeded)
    {
        return new OnCurveTrace(
            trialNumber,
            succeeded,
            Array.AsReadOnly(events.ToArray()),
            omittedEventCount);
    }
}
