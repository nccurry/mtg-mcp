using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Adds exact independent mulligan attempts with caller-defined keep and bottom behavior.
/// </summary>
public sealed partial class ExactStatisticsCalculator
{
    /// <summary>
    /// Calculates one exact mulligan schedule without choosing a keep policy.
    /// </summary>
    public OperationResult<StatisticsCalculation<MulliganResult>> CalculateMulligan(
        MulliganRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PopulationPreparation preparation = StatisticsPopulationValidator.Prepare(request.Population);
        return preparation switch
        {
            CanonicalPopulation population => CalculateMulligan(
                population,
                request,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            OperationInvalidInput invalid => invalid,
            StatisticsBoundedUnsupported bounded =>
                new OperationSuccess<StatisticsCalculation<MulliganResult>>(bounded),
        };
    }

    /// <summary>
    /// Evaluates one validated caller-owned attempt schedule.
    /// </summary>
    private OperationResult<StatisticsCalculation<MulliganResult>> CalculateMulligan(
        CanonicalPopulation population,
        MulliganRequest request,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (request.KeepConditions is null)
        {
            return Invalid<MulliganResult>(
                "request.keepConditions is required; use an empty array for an always-keep conjunction.");
        }

        if (!TryPrepareAttempts(
                population.TotalCount,
                request.Attempts,
                out MulliganAttemptInput[] attempts,
                out string? attemptError))
        {
            return Invalid<MulliganResult>(attemptError!);
        }

        if (!TryPrepareOptionalConditions(
                population,
                request.KeepConditions,
                allowEmpty: true,
                out StatisticsGroupConditionInput[] keepConditions,
                out string? keepError))
        {
            return Invalid<MulliganResult>(keepError!);
        }

        if (!TryPrepareOptionalConditions(
                population,
                request.FinalConditions,
                allowEmpty: false,
                out StatisticsGroupConditionInput[] preparedFinalConditions,
                out string? finalError))
        {
            return Invalid<MulliganResult>(finalError!);
        }

        StatisticsGroupConditionInput[]? finalConditions = request.FinalConditions is null
            ? null
            : preparedFinalConditions;

        if (!TryPrepareBottomPriority(
                population,
                request.BottomPriority,
                out string[] bottomPriority,
                out string? bottomError))
        {
            return Invalid<MulliganResult>(bottomError!);
        }

        string[] groups = keepConditions
            .Select(value => value.Group)
            .Concat(finalConditions?.Select(value => value.Group) ?? [])
            .Concat(bottomPriority)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, int> groupIndexes = groups
            .Select((group, index) => (group, index))
            .ToDictionary(value => value.group, value => value.index, StringComparer.Ordinal);
        StatisticsGroupConditionInput[] indexedKeep = OrderConditionsForGroups(
            keepConditions,
            groupIndexes);
        StatisticsGroupConditionInput[]? indexedFinal = finalConditions is null
            ? null
            : OrderConditionsForGroups(finalConditions, groupIndexes);

        ExactFraction reach = ExactFraction.One;
        ExactFraction finalEvent = ExactFraction.Zero;
        List<MulliganAttemptResult> rows = [];
        for (int attemptIndex = 0; attemptIndex < attempts.Length; attemptIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MulliganAttemptInput attempt = attempts[attemptIndex];
            ExactFraction conditionalKeep;
            if (attempt.Forced)
            {
                conditionalKeep = ExactFraction.One;
            }
            else if (!TryEvaluateMulliganPredicate(
                population,
                attempt,
                groups,
                budget,
                state => MatchesIndexedConditions(state.GroupCounts, indexedKeep, groupIndexes),
                cancellationToken,
                out conditionalKeep,
                out long keepEstimate))
            {
                return Bounded<MulliganResult>(
                    budget,
                    StatisticsWorkBudget.SaturatingMultiply(keepEstimate, attempts.Length),
                    population,
                    "Reduce the attempt count, draw size, or group count.",
                    attemptCount: attempts.Length);
            }

            ExactFraction unconditionalKeep = reach.Multiply(conditionalKeep);
            rows.Add(new MulliganAttemptResult(
                attemptIndex + 1,
                attempt.DrawCount,
                attempt.BottomCount,
                attempt.Forced,
                reach.ToProbability(),
                conditionalKeep.ToProbability(),
                unconditionalKeep.ToProbability()));

            if (indexedFinal is not null)
            {
                if (!TryEvaluateMulliganPredicate(
                    population,
                    attempt,
                    groups,
                    budget,
                    state =>
                        (attempt.Forced || MatchesIndexedConditions(
                            state.GroupCounts,
                            indexedKeep,
                            groupIndexes)) &&
                        MatchesFinalAfterBottom(
                            state,
                            attempt.BottomCount,
                            bottomPriority,
                            groupIndexes,
                            indexedFinal),
                    cancellationToken,
                    out ExactFraction conditionalFinal,
                    out long finalEstimate))
                {
                    return Bounded<MulliganResult>(
                        budget,
                        StatisticsWorkBudget.SaturatingMultiply(finalEstimate, attempts.Length),
                        population,
                        "Reduce the attempt count, draw size, or final group count.",
                        attemptCount: attempts.Length);
                }

                finalEvent = finalEvent.Add(reach.Multiply(conditionalFinal));
            }

            reach = attempt.Forced
                ? ExactFraction.Zero
                : reach.Multiply(ExactFraction.One.Subtract(conditionalKeep));
        }

        MulliganResult result = new(
            CreateDerivation(
                "explicit-mulligan-schedule",
                [
                    "Every attempt is an independent full reshuffle with caller-supplied draw and bottom counts.",
                    "Keep constraints and deterministic bottom priority were supplied by the caller.",
                    "No free mulligan, forced attempt, or strategic keep rule was inferred.",
                ]),
            population.ToSnapshot(),
            keepConditions,
            bottomPriority,
            finalConditions,
            rows,
            reach.ToProbability(),
            indexedFinal is null ? null : finalEvent.ToProbability());
        return Exact(result);
    }

    /// <summary>
    /// Validates the ordered attempt schedule and forced-final invariant.
    /// </summary>
    private static bool TryPrepareAttempts(
        int population,
        IReadOnlyList<MulliganAttemptInput>? inputs,
        out MulliganAttemptInput[] attempts,
        out string? error)
    {
        attempts = [];
        error = null;
        if (inputs is null || inputs.Count is < 1 or > 8)
        {
            error = "request.attempts must contain between one and eight attempts.";
            return false;
        }

        for (int index = 0; index < inputs.Count; index++)
        {
            MulliganAttemptInput? input = inputs[index];
            if (input is null ||
                input.DrawCount < 0 ||
                input.DrawCount > population ||
                input.BottomCount < 0 ||
                input.BottomCount > input.DrawCount ||
                (input.Forced && index != inputs.Count - 1))
            {
                error = "request.attempts contain invalid draw, bottom, or forced-final values.";
                return false;
            }
        }

        attempts = inputs.ToArray();
        return true;
    }

    /// <summary>
    /// Validates an optional condition conjunction and returns canonical group order.
    /// </summary>
    private static bool TryPrepareOptionalConditions(
        CanonicalPopulation population,
        IReadOnlyList<StatisticsGroupConditionInput>? inputs,
        bool allowEmpty,
        out StatisticsGroupConditionInput[] conditions,
        out string? error)
    {
        conditions = [];
        error = null;
        if (inputs is null)
        {
            if (allowEmpty)
            {
                conditions = [];
                return true;
            }

            return true;
        }

        if (inputs.Count == 0)
        {
            if (allowEmpty)
            {
                conditions = [];
                return true;
            }

            error = "request.finalConditions must be null or contain at least one condition.";
            return false;
        }

        if (!TryPrepareConditions(population, inputs, out StatisticsGroupConditionInput[] prepared, out error))
        {
            return false;
        }

        conditions = prepared;
        return true;
    }

    /// <summary>
    /// Validates exact unique caller bottom-priority groups without reordering them.
    /// </summary>
    private static bool TryPrepareBottomPriority(
        CanonicalPopulation population,
        IReadOnlyList<string>? inputs,
        out string[] priority,
        out string? error)
    {
        priority = [];
        error = null;
        if (inputs is null)
        {
            error = "request.bottomPriority is required; use an empty array for canonical fallback only.";
            return false;
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string? input in inputs)
        {
            if (!TryValidateExactGroup(input, out string group) ||
                !population.HasGroup(group) ||
                !names.Add(group))
            {
                error = "request.bottomPriority must contain unique exact declared groups.";
                return false;
            }
        }

        priority = inputs.ToArray();
        return true;
    }

    /// <summary>
    /// Orders condition checks by projected group index while retaining caller ranges.
    /// </summary>
    private static StatisticsGroupConditionInput[] OrderConditionsForGroups(
        IReadOnlyList<StatisticsGroupConditionInput> conditions,
        IReadOnlyDictionary<string, int> groupIndexes)
    {
        return conditions.OrderBy(value => groupIndexes[value.Group]).ToArray();
    }

    /// <summary>
    /// Evaluates one attempt predicate over all exact hands.
    /// </summary>
    private static bool TryEvaluateMulliganPredicate(
        CanonicalPopulation population,
        MulliganAttemptInput attempt,
        IReadOnlyList<string> groups,
        StatisticsWorkBudget budget,
        Func<ProjectedDrawState, bool> predicate,
        CancellationToken cancellationToken,
        out ExactFraction probability,
        out long estimatedWork)
    {
        MembershipEnumerationResult enumeration = MembershipBucketEngine.Evaluate(
            population,
            attempt.DrawCount,
            groups,
            budget,
            predicate,
            cancellationToken);
        estimatedWork = enumeration.EstimatedStates;
        probability = enumeration.IsBounded
            ? ExactFraction.Zero
            : new ExactFraction(enumeration.FavorableWeight, enumeration.TotalWeight);
        return !enumeration.IsBounded;
    }

    /// <summary>
    /// Tests conditions whose array is ordered by the same projected group indices.
    /// </summary>
    private static bool MatchesIndexedConditions(
        int[] groupCounts,
        IReadOnlyList<StatisticsGroupConditionInput> conditions,
        IReadOnlyDictionary<string, int> groupIndexes)
    {
        foreach (StatisticsGroupConditionInput condition in conditions)
        {
            int count = groupCounts[groupIndexes[condition.Group]];
            if (count < condition.Minimum ||
                (condition.Maximum is int maximum && count > maximum))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies deterministic bottom priority and evaluates final conditions.
    /// </summary>
    private static bool MatchesFinalAfterBottom(
        ProjectedDrawState state,
        int bottomCount,
        IReadOnlyList<string> bottomPriority,
        IReadOnlyDictionary<string, int> groupIndexes,
        IReadOnlyList<StatisticsGroupConditionInput> finalConditions)
    {
        int[] remaining = (int[])state.DrawnByBucket.Clone();
        int toBottom = bottomCount;
        foreach (string group in bottomPriority)
        {
            int bit = 1 << groupIndexes[group];
            RemoveMatching(remaining, state.Buckets, bit, ref toBottom);
        }

        RemoveMatching(remaining, state.Buckets, requiredBit: 0, ref toBottom);
        int[] finalCounts = CountProjectedGroups(remaining, state.Buckets, groupIndexes.Count);
        return MatchesIndexedConditions(finalCounts, finalConditions, groupIndexes);
    }

    /// <summary>
    /// Removes copies from matching buckets in canonical membership-mask order.
    /// </summary>
    private static void RemoveMatching(
        int[] remaining,
        IReadOnlyList<ProjectedPopulationBucket> buckets,
        int requiredBit,
        ref int toBottom)
    {
        for (int index = 0; index < buckets.Count && toBottom > 0; index++)
        {
            if (requiredBit != 0 && (buckets[index].MembershipMask & requiredBit) == 0)
            {
                continue;
            }

            int removed = Math.Min(remaining[index], toBottom);
            remaining[index] -= removed;
            toBottom -= removed;
        }
    }

    /// <summary>
    /// Recounts projected groups after deterministic bottoming.
    /// </summary>
    private static int[] CountProjectedGroups(
        int[] drawn,
        IReadOnlyList<ProjectedPopulationBucket> buckets,
        int groupCount)
    {
        int[] counts = new int[groupCount];
        for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
        {
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                if ((buckets[bucketIndex].MembershipMask & (1 << groupIndex)) != 0)
                {
                    counts[groupIndex] += drawn[bucketIndex];
                }
            }
        }

        return counts;
    }
}
