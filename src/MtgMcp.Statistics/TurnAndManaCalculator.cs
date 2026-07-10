using System.Numerics;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Adds explicit draw-schedule and one-unit source-allocation calculations.
/// </summary>
public sealed partial class ExactStatisticsCalculator
{
    /// <summary>
    /// Calculates one exact probability table from only the supplied draw schedule.
    /// </summary>
    public OperationResult<StatisticsCalculation<TurnTableResult>> CalculateTurnTable(
        TurnTableRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PopulationPreparation preparation = StatisticsPopulationValidator.Prepare(request.Population);
        return preparation switch
        {
            CanonicalPopulation population => CalculateTurnTable(
                population,
                request,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            OperationInvalidInput invalid => invalid,
            StatisticsBoundedUnsupported bounded =>
                new OperationSuccess<StatisticsCalculation<TurnTableResult>>(bounded),
        };
    }

    /// <summary>
    /// Calculates exact payment availability from caller-declared one-unit source capabilities.
    /// </summary>
    public OperationResult<StatisticsCalculation<ManaAvailabilityResult>> CalculateManaAvailability(
        ManaAvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PopulationPreparation preparation = StatisticsPopulationValidator.Prepare(request.Population);
        return preparation switch
        {
            CanonicalPopulation population => CalculateManaAvailability(
                population,
                request,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            OperationInvalidInput invalid => invalid,
            StatisticsBoundedUnsupported bounded =>
                new OperationSuccess<StatisticsCalculation<ManaAvailabilityResult>>(bounded),
        };
    }

    /// <summary>
    /// Evaluates one validated explicit turn schedule.
    /// </summary>
    private OperationResult<StatisticsCalculation<TurnTableResult>> CalculateTurnTable(
        CanonicalPopulation population,
        TurnTableRequest request,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (!TryValidateExactGroup(request.SuccessGroup, out string successGroup) ||
            !population.HasGroup(successGroup))
        {
            return Invalid<TurnTableResult>(
                "request.successGroup must be one exact declared population group.");
        }

        if (request.OpeningHandSize < 0 || request.OpeningHandSize > population.TotalCount)
        {
            return Invalid<TurnTableResult>(
                "request.openingHandSize must be between zero and the selected population size.");
        }

        if (!TryPrepareDrawSchedule(
                request.DrawsByTurn,
                request.OpeningHandSize,
                population.TotalCount,
                out TurnDrawInput[] schedule,
                out string? scheduleError))
        {
            return Invalid<TurnTableResult>(scheduleError!);
        }

        int successCount = population.CountGroup(successGroup);
        int cardsSeen = request.OpeningHandSize;
        List<TurnTableRow> rows = [];
        foreach (TurnDrawInput draw in schedule)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cardsSeen += draw.Draws;
            if (!TryResolveEvent(
                    request.Event,
                    cardsSeen,
                    out HypergeometricEventSnapshot? eventSnapshot,
                    out string? eventError))
            {
                return Invalid<TurnTableResult>(eventError!);
            }

            if (!TryCalculateHypergeometricFraction(
                    population.TotalCount,
                    successCount,
                    cardsSeen,
                    eventSnapshot!,
                    budget,
                    cancellationToken,
                    out ExactFraction probability,
                    out long estimatedWork))
            {
                return Bounded<TurnTableResult>(
                    budget,
                    StatisticsWorkBudget.SaturatingMultiply(estimatedWork, schedule.Length),
                    population,
                    "Reduce the supplied turn rows or event range.",
                    turnCount: schedule.Length);
            }

            rows.Add(new TurnTableRow(
                draw.Turn,
                draw.Draws,
                cardsSeen,
                eventSnapshot!,
                probability.ToProbability(),
                ExactFraction.One.Subtract(probability).ToProbability()));
        }

        TurnTableResult result = new(
            CreateDerivation(
                "explicit-turn-hypergeometric",
                [
                    "Opening cards and every additional card seen by turn were supplied explicitly.",
                    "No normal draw, play/draw, multiplayer, or replacement-effect rule was inferred.",
                ]),
            population.ToSnapshot(),
            successGroup,
            successCount,
            request.OpeningHandSize,
            rows);
        return Exact(result);
    }

    /// <summary>
    /// Evaluates one validated source-allocation request.
    /// </summary>
    private OperationResult<StatisticsCalculation<ManaAvailabilityResult>> CalculateManaAvailability(
        CanonicalPopulation population,
        ManaAvailabilityRequest request,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (request.DrawCount < 0 || request.DrawCount > population.TotalCount)
        {
            return Invalid<ManaAvailabilityResult>(
                "request.drawCount must be between zero and the selected population size.");
        }

        if (request.MaximumUsableSources < 0 ||
            request.MaximumUsableSources > population.TotalCount)
        {
            return Invalid<ManaAvailabilityResult>(
                "request.maximumUsableSources must be between zero and the selected population size.");
        }

        if (!TryPrepareManaSources(
                population,
                request.Sources,
                out ManaSourceInput[] sources,
                out string[] groups,
                out ManaCapability[] capabilities,
                out string? sourceError))
        {
            return Invalid<ManaAvailabilityResult>(sourceError!);
        }

        if (!TryPrepareManaRequirement(
                request.Requirement,
                out PreparedManaRequirement? requirement,
                out string? requirementError))
        {
            return Invalid<ManaAvailabilityResult>(requirementError!);
        }

        ProjectedPopulationBucket[] projected = MembershipBucketEngine.Project(population, groups);
        if (projected.Any(bucket => BitOperations.PopCount((uint)bucket.MembershipMask) > 1))
        {
            return Invalid<ManaAvailabilityResult>(
                "request.sources must not select the same population bucket through multiple source groups.");
        }

        if (!TryCalculateManaFraction(
                population,
                request.DrawCount,
                groups,
                capabilities,
                requirement!,
                request.MaximumUsableSources,
                budget,
                cancellationToken,
                out ExactFraction probability,
                out long estimatedWork))
        {
            return Bounded<ManaAvailabilityResult>(
                budget,
                estimatedWork,
                population,
                "Reduce the draw size or number of source groups.");
        }

        ManaAvailabilityResult result = new(
            CreateDerivation(
                "mana-capacity-allocation",
                [
                    "Source groups and W/U/B/R/G/C capabilities were supplied explicitly.",
                    "Each usable source produces one unit and may be assigned at most once.",
                    "No land, timing, activation, tapped-state, or alternate-cost rule was inferred.",
                ]),
            population.ToSnapshot(),
            request.DrawCount,
            sources,
            request.Requirement,
            request.MaximumUsableSources,
            probability.ToProbability(),
            ExactFraction.One.Subtract(probability).ToProbability());
        return Exact(result);
    }

    /// <summary>
    /// Calculates one mana event while sharing a composed request budget.
    /// </summary>
    private static bool TryCalculateManaFraction(
        CanonicalPopulation population,
        int drawCount,
        IReadOnlyList<string> groups,
        IReadOnlyList<ManaCapability> capabilities,
        PreparedManaRequirement requirement,
        int maximumUsableSources,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken,
        out ExactFraction probability,
        out long estimatedWork)
    {
        MembershipEnumerationResult enumeration = MembershipBucketEngine.Evaluate(
            population,
            drawCount,
            groups,
            budget,
            state => ManaPaymentMatcher.CanPay(
                state,
                capabilities,
                requirement,
                maximumUsableSources),
            cancellationToken);
        estimatedWork = enumeration.EstimatedStates;
        probability = enumeration.IsBounded
            ? ExactFraction.Zero
            : new ExactFraction(enumeration.FavorableWeight, enumeration.TotalWeight);
        return !enumeration.IsBounded;
    }

    /// <summary>
    /// Validates and copies one strictly increasing explicit draw schedule.
    /// </summary>
    private static bool TryPrepareDrawSchedule(
        IReadOnlyList<TurnDrawInput>? inputs,
        int openingHandSize,
        int populationSize,
        out TurnDrawInput[] schedule,
        out string? error)
    {
        schedule = [];
        error = null;
        if (inputs is null || inputs.Count is < 1 or > 50)
        {
            error = "request.drawsByTurn must contain between one and fifty rows.";
            return false;
        }

        List<TurnDrawInput> validated = [];
        int previousTurn = 0;
        int cardsSeen = openingHandSize;
        foreach (TurnDrawInput? input in inputs)
        {
            if (input is null || input.Turn <= previousTurn || input.Draws < 0)
            {
                error = "request.drawsByTurn must use increasing positive turns and nonnegative draws.";
                return false;
            }

            cardsSeen += input.Draws;
            if (cardsSeen > populationSize)
            {
                error = "request.drawsByTurn cannot exceed the selected population.";
                return false;
            }

            validated.Add(input);
            previousTurn = input.Turn;
        }

        schedule = [.. validated];
        return true;
    }

    /// <summary>
    /// Validates and canonically orders caller-declared source groups and capabilities.
    /// </summary>
    private static bool TryPrepareManaSources(
        CanonicalPopulation population,
        IReadOnlyList<ManaSourceInput>? inputs,
        out ManaSourceInput[] sources,
        out string[] groups,
        out ManaCapability[] capabilities,
        out string? error)
    {
        sources = [];
        groups = [];
        capabilities = [];
        error = null;
        if (inputs is null || inputs.Count > StatisticsPopulationValidator.MaximumGroups)
        {
            error = "request.sources must contain at most eight source groups.";
            return false;
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        List<(ManaSourceInput Source, ManaCapability Capability)> validated = [];
        foreach (ManaSourceInput? input in inputs)
        {
            if (input is null ||
                !TryValidateExactGroup(input.Group, out string group) ||
                !population.HasGroup(group) ||
                !names.Add(group) ||
                !TryParseCapabilities(input.Capabilities, out ManaCapability capability, out string[] canonical))
            {
                error = "request.sources must use unique declared groups and distinct W/U/B/R/G/C capabilities.";
                return false;
            }

            validated.Add((new ManaSourceInput(group, canonical), capability));
        }

        validated.Sort(static (left, right) =>
            string.Compare(left.Source.Group, right.Source.Group, StringComparison.Ordinal));
        sources = validated.Select(value => value.Source).ToArray();
        groups = sources.Select(value => value.Group).ToArray();
        capabilities = validated.Select(value => value.Capability).ToArray();
        return true;
    }

    /// <summary>
    /// Parses one exact source capability collection into a canonical mask.
    /// </summary>
    private static bool TryParseCapabilities(
        IReadOnlyList<string>? inputs,
        out ManaCapability capability,
        out string[] canonical)
    {
        capability = ManaCapability.None;
        canonical = [];
        if (inputs is null || inputs.Count == 0)
        {
            return false;
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string? input in inputs)
        {
            ManaCapability parsed = input switch
            {
                "W" => ManaCapability.White,
                "U" => ManaCapability.Blue,
                "B" => ManaCapability.Black,
                "R" => ManaCapability.Red,
                "G" => ManaCapability.Green,
                "C" => ManaCapability.Colorless,
                _ => ManaCapability.None,
            };
            if (parsed == ManaCapability.None || !names.Add(input!))
            {
                return false;
            }

            capability |= parsed;
        }

        canonical = [.. names];
        Array.Sort(canonical, StringComparer.Ordinal);
        return true;
    }

    /// <summary>
    /// Validates nonnegative mana requirements and calculates their total units.
    /// </summary>
    private static bool TryPrepareManaRequirement(
        ManaRequirementInput? input,
        out PreparedManaRequirement? requirement,
        out string? error)
    {
        requirement = null;
        error = null;
        if (input is null)
        {
            error = "request.requirement is required.";
            return false;
        }

        int[] counts =
        [
            input.White,
            input.Blue,
            input.Black,
            input.Red,
            input.Green,
            input.Colorless,
            input.Generic,
        ];
        if (counts.Any(value => value < 0))
        {
            error = "request.requirement values must be nonnegative.";
            return false;
        }

        long total = counts.Sum(value => (long)value);
        if (total > int.MaxValue)
        {
            error = "request.requirement total is too large.";
            return false;
        }

        requirement = new PreparedManaRequirement(counts, (int)total);
        return true;
    }
}

/// <summary>
/// Identifies the exact one-unit mana capabilities supported by the stable calculator.
/// </summary>
[Flags]
internal enum ManaCapability
{
    /// <summary>
    /// Produces no supported mana symbol.
    /// </summary>
    None = 0,

    /// <summary>
    /// Produces white mana.
    /// </summary>
    White = 1 << 0,

    /// <summary>
    /// Produces blue mana.
    /// </summary>
    Blue = 1 << 1,

    /// <summary>
    /// Produces black mana.
    /// </summary>
    Black = 1 << 2,

    /// <summary>
    /// Produces red mana.
    /// </summary>
    Red = 1 << 3,

    /// <summary>
    /// Produces green mana.
    /// </summary>
    Green = 1 << 4,

    /// <summary>
    /// Produces colorless mana.
    /// </summary>
    Colorless = 1 << 5,
}

/// <summary>
/// Stores normalized mana requirement counts in W/U/B/R/G/C/generic order.
/// </summary>
internal sealed record PreparedManaRequirement(int[] Counts, int Total);

/// <summary>
/// Matches exact one-unit payments to distinct drawn sources under one total source cap.
/// </summary>
internal static class ManaPaymentMatcher
{
    /// <summary>
    /// Reports whether one drawn source vector can satisfy the complete declared payment.
    /// </summary>
    internal static bool CanPay(
        ProjectedDrawState state,
        IReadOnlyList<ManaCapability> capabilities,
        PreparedManaRequirement requirement,
        int maximumUsableSources)
    {
        if (requirement.Total == 0)
        {
            return true;
        }

        if (requirement.Total > maximumUsableSources)
        {
            return false;
        }

        List<int> sourceBuckets = [];
        for (int index = 0; index < state.Buckets.Length; index++)
        {
            if (state.DrawnByBucket[index] > 0 && state.Buckets[index].MembershipMask != 0)
            {
                sourceBuckets.Add(index);
            }
        }

        int requirementNodeCount = requirement.Counts.Count(value => value > 0);
        int source = 0;
        int firstRequirement = 1;
        int firstSource = firstRequirement + requirementNodeCount;
        int pool = firstSource + sourceBuckets.Count;
        int sink = pool + 1;
        int[,] capacity = new int[sink + 1, sink + 1];
        int requirementNode = 0;
        for (int requirementIndex = 0; requirementIndex < requirement.Counts.Length; requirementIndex++)
        {
            int required = requirement.Counts[requirementIndex];
            if (required == 0)
            {
                continue;
            }

            int node = firstRequirement + requirementNode;
            capacity[source, node] = required;
            for (int sourceIndex = 0; sourceIndex < sourceBuckets.Count; sourceIndex++)
            {
                int bucketIndex = sourceBuckets[sourceIndex];
                int groupIndex = BitOperations.TrailingZeroCount(
                    (uint)state.Buckets[bucketIndex].MembershipMask);
                if (requirementIndex == 6 ||
                    (capabilities[groupIndex] & (ManaCapability)(1 << requirementIndex)) != 0)
                {
                    capacity[node, firstSource + sourceIndex] = required;
                }
            }

            requirementNode++;
        }

        for (int sourceIndex = 0; sourceIndex < sourceBuckets.Count; sourceIndex++)
        {
            int bucketIndex = sourceBuckets[sourceIndex];
            capacity[firstSource + sourceIndex, pool] = state.DrawnByBucket[bucketIndex];
        }

        capacity[pool, sink] = maximumUsableSources;
        return IntegralMaximumFlow.Calculate(capacity, source, sink) == requirement.Total;
    }
}
