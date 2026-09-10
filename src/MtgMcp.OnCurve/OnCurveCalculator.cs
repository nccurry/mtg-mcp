using MtgMcp.Core.Results;

namespace MtgMcp.OnCurve;

/// <summary>
/// Runs the bounded, repeatable deck mana and on-curve estimate from resolved facts.
/// </summary>
internal static class OnCurveCalculator
{
    /// <summary>
    /// Identifies the fixed modeled turn rules used by this result.
    /// </summary>
    internal const string ModelVersion = "on-curve-v1";

    /// <summary>
    /// Identifies the fixed land-play policy and its tie-break order.
    /// </summary>
    internal const string PolicyId = "target-first-v1";

    /// <summary>
    /// Identifies the fixed random-sequence and shuffle contract.
    /// </summary>
    internal const string RandomVersion = "splitmix64-v1";

    /// <summary>
    /// Runs one complete estimate without a test checkpoint.
    /// </summary>
    internal static OperationResult<OnCurveRunReport> Calculate(
        OnCurveRequest? request,
        CancellationToken cancellationToken = default)
    {
        return Calculate(request, null, cancellationToken);
    }

    /// <summary>
    /// Runs one complete estimate and invokes an optional test checkpoint before each trial.
    /// </summary>
    internal static OperationResult<OnCurveRunReport> Calculate(
        OnCurveRequest? request,
        Action<int>? beforeTrial,
        CancellationToken cancellationToken = default)
    {
        OperationResult<OnCurveRequest> validation = OnCurveRequestValidator.Validate(request);
        if (validation is not OperationSuccess<OnCurveRequest> valid)
        {
            return ForwardFailure<OnCurveRequest, OnCurveRunReport>(validation);
        }

        OperationResult<OnCurveManaCost> costResult = OnCurveManaSymbols.ReadCost(valid.Data.Target!.ManaCost);
        if (costResult is not OperationSuccess<OnCurveManaCost> cost)
        {
            return ForwardFailure<OnCurveManaCost, OnCurveRunReport>(costResult);
        }

        if (!OnCurveSeed.TryParse(valid.Data.Seed, out ulong seed))
        {
            return new OperationInvalidInput(
                "invalid-on-curve-seed",
                "The on-curve seed must contain exactly 16 hexadecimal characters.");
        }

        OperationResult<IReadOnlyList<OnCurveLibraryCard>> libraryResult = BuildLibrary(valid.Data);
        if (libraryResult is not OperationSuccess<IReadOnlyList<OnCurveLibraryCard>> library)
        {
            return ForwardFailure<IReadOnlyList<OnCurveLibraryCard>, OnCurveRunReport>(libraryResult);
        }

        Dictionary<Guid, OnCurveLandRule> landRules = valid.Data.LandRules!
            .ToDictionary(value => value.EntryId, value => value.Rule);
        SplitMix64Random random = new(seed);
        Dictionary<OnCurveMissReason, int> failureCounts = CreateFailureCounts();
        OnCurveTrace? firstSuccessfulTrace = null;
        OnCurveTrace? firstUnsuccessfulTrace = null;
        int successCount = 0;
        for (int trialNumber = 1; trialNumber <= valid.Data.SampleCount; trialNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            beforeTrial?.Invoke(trialNumber);
            cancellationToken.ThrowIfCancellationRequested();
            bool captureTrace = firstSuccessfulTrace is null || firstUnsuccessfulTrace is null;
            OnCurveTrialOutcome outcome = OnCurveTrialRunner.Run(
                library.Data,
                valid.Data.Target.EntryId,
                landRules,
                cost.Data,
                valid.Data.TurnLimit,
                valid.Data.OnThePlay,
                random,
                trialNumber,
                captureTrace,
                cancellationToken);
            if (outcome.Succeeded)
            {
                successCount++;
                firstSuccessfulTrace ??= outcome.Trace;
            }
            else
            {
                OnCurveMissReason reason = outcome.MissReason ??
                    throw new InvalidOperationException("An unsuccessful trial must name one miss reason.");
                failureCounts[reason]++;
                firstUnsuccessfulTrace ??= outcome.Trace;
            }
        }

        List<OnCurveTrace> traces = [];
        if (firstSuccessfulTrace is not null)
        {
            traces.Add(firstSuccessfulTrace);
        }

        if (firstUnsuccessfulTrace is not null)
        {
            traces.Add(firstUnsuccessfulTrace);
        }

        return new OperationSuccess<OnCurveRunReport>(new OnCurveRunReport(
            ModelVersion,
            PolicyId,
            RandomVersion,
            OnCurveFingerprint.Create(valid.Data),
            OnCurveSeed.Format(seed),
            valid.Data.SampleCount,
            valid.Data.SampleCount,
            valid.Data.TurnLimit,
            valid.Data.OnThePlay,
            successCount,
            (double)successCount / valid.Data.SampleCount,
            OnCurveWilsonIntervalCalculator.Calculate(successCount, valid.Data.SampleCount),
            FailureCounts(failureCounts),
            Array.AsReadOnly(traces.ToArray())));
    }

    /// <summary>
    /// Builds every physical card copy once and rejects candidate source colors outside this model.
    /// </summary>
    private static OperationResult<IReadOnlyList<OnCurveLibraryCard>> BuildLibrary(OnCurveRequest request)
    {
        HashSet<Guid> candidateLandIds = request.CandidateLandEntryIds!.ToHashSet();
        List<OnCurveLibraryCard> library = [];
        foreach (OnCurveDeckEntry entry in request.Mainboard!)
        {
            IReadOnlyList<OnCurveManaSymbol> colors = [];
            if (candidateLandIds.Contains(entry.EntryId))
            {
                OnCurveProducedManaValues values = (OnCurveProducedManaValues)entry.ProducedMana.Value;
                OperationResult<IReadOnlyList<OnCurveManaSymbol>> colorsResult = ReadSourceColors(values.Colors);
                if (colorsResult is not OperationSuccess<IReadOnlyList<OnCurveManaSymbol>> parsedColors)
                {
                    return ForwardFailure<IReadOnlyList<OnCurveManaSymbol>, IReadOnlyList<OnCurveLibraryCard>>(colorsResult);
                }

                colors = parsedColors.Data;
            }

            for (int copy = 0; copy < entry.Quantity; copy++)
            {
                library.Add(new OnCurveLibraryCard(entry.EntryId, colors));
            }
        }

        return new OperationSuccess<IReadOnlyList<OnCurveLibraryCard>>(
            Array.AsReadOnly(library.ToArray()));
    }

    /// <summary>
    /// Maps one listed direct source fact to only the mana symbols this model supports.
    /// </summary>
    private static OperationResult<IReadOnlyList<OnCurveManaSymbol>> ReadSourceColors(
        IReadOnlyList<string> listedColors)
    {
        List<OnCurveManaSymbol> colors = [];
        foreach (string color in listedColors)
        {
            if (!OnCurveManaSymbols.TryParseProducedMana(color, out OnCurveManaSymbol symbol))
            {
                return new OperationUnsupported(
                    "unsupported-on-curve-source-mana",
                    "A modeled land has a produced-mana value outside W, U, B, R, G, and C.");
            }

            colors.Add(symbol);
        }

        return new OperationSuccess<IReadOnlyList<OnCurveManaSymbol>>(
            Array.AsReadOnly(colors.ToArray()));
    }

    /// <summary>
    /// Creates a zeroed result count for every stable miss reason.
    /// </summary>
    private static Dictionary<OnCurveMissReason, int> CreateFailureCounts()
    {
        Dictionary<OnCurveMissReason, int> counts = [];
        foreach (OnCurveMissReason reason in Enum.GetValues<OnCurveMissReason>())
        {
            counts.Add(reason, 0);
        }

        return counts;
    }

    /// <summary>
    /// Creates ordered failure counts so zero-count model outcomes remain visible.
    /// </summary>
    private static IReadOnlyList<OnCurveFailureCount> FailureCounts(
        IReadOnlyDictionary<OnCurveMissReason, int> counts)
    {
        List<OnCurveFailureCount> result = [];
        foreach (OnCurveMissReason reason in Enum.GetValues<OnCurveMissReason>())
        {
            result.Add(new OnCurveFailureCount(reason, counts[reason]));
        }

        return Array.AsReadOnly(result.ToArray());
    }

    /// <summary>
    /// Projects a common operation failure to a calculation result without changing its reason.
    /// </summary>
    private static OperationResult<T> ForwardFailure<TSource, T>(OperationResult<TSource> result)
    {
        return result switch
        {
            OperationNotFound value => value,
            OperationNotCached value => value,
            OperationUnsupported value => value,
            OperationUnavailable value => value,
            OperationConflict value => value,
            OperationInvalidInput value => value,
            OperationSuccess<TSource> => new OperationUnavailable(
                "unexpected-on-curve-result",
                "The on-curve calculation returned an unexpected result."),
            _ => new OperationUnavailable(
                "unexpected-on-curve-result",
                "The on-curve calculation returned an unexpected result."),
        };
    }
}
