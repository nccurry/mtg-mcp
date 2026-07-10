using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Adds exact overlapping-observation and one-use package allocation calculations.
/// </summary>
public sealed partial class ExactStatisticsCalculator
{
    /// <summary>
    /// Calculates one exact conjunction over caller-declared overlapping groups.
    /// </summary>
    public OperationResult<StatisticsCalculation<MultivariateResult>> CalculateMultivariate(
        MultivariateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PopulationPreparation preparation = StatisticsPopulationValidator.Prepare(request.Population);
        return preparation switch
        {
            CanonicalPopulation population => CalculateMultivariate(
                population,
                request,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            OperationInvalidInput invalid => invalid,
            StatisticsBoundedUnsupported bounded =>
                new OperationSuccess<StatisticsCalculation<MultivariateResult>>(bounded),
        };
    }

    /// <summary>
    /// Calculates exact package assembly using one-use capacity allocation.
    /// </summary>
    public OperationResult<StatisticsCalculation<PackageAssemblyResult>> CalculatePackageAssembly(
        PackageAssemblyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PopulationPreparation preparation = StatisticsPopulationValidator.Prepare(request.Population);
        return preparation switch
        {
            CanonicalPopulation population => CalculatePackageAssembly(
                population,
                request,
                new StatisticsWorkBudget(workLimit),
                cancellationToken),
            OperationInvalidInput invalid => invalid,
            StatisticsBoundedUnsupported bounded =>
                new OperationSuccess<StatisticsCalculation<PackageAssemblyResult>>(bounded),
        };
    }

    /// <summary>
    /// Evaluates one validated multivariate request.
    /// </summary>
    private OperationResult<StatisticsCalculation<MultivariateResult>> CalculateMultivariate(
        CanonicalPopulation population,
        MultivariateRequest request,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (request.DrawCount < 0 || request.DrawCount > population.TotalCount)
        {
            return Invalid<MultivariateResult>(
                "request.drawCount must be between zero and the selected population size.");
        }

        if (!TryPrepareConditions(
                population,
                request.Conditions,
                out StatisticsGroupConditionInput[] conditions,
                out string? error))
        {
            return Invalid<MultivariateResult>(error!);
        }

        string[] groups = conditions.Select(value => value.Group).ToArray();
        MembershipEnumerationResult enumeration = MembershipBucketEngine.Evaluate(
            population,
            request.DrawCount,
            groups,
            budget,
            state => MatchesConditions(state.GroupCounts, conditions),
            cancellationToken);
        if (enumeration.IsBounded)
        {
            return Bounded<MultivariateResult>(
                budget,
                enumeration.EstimatedStates,
                population,
                "Reduce the draw size or number of observed groups.");
        }

        ExactFraction probability = new(
            enumeration.FavorableWeight,
            enumeration.TotalWeight);
        MultivariateResult result = new(
            CreateDerivation(
                "multivariate-membership",
                [
                    "Population buckets and overlapping group membership were supplied explicitly.",
                    "Each physical copy contributes one combinatorial weight even when observed in several groups.",
                ]),
            population.ToSnapshot(),
            request.DrawCount,
            conditions,
            probability.ToProbability(),
            ExactFraction.One.Subtract(probability).ToProbability());
        return Exact(result);
    }

    /// <summary>
    /// Evaluates one validated package request.
    /// </summary>
    private OperationResult<StatisticsCalculation<PackageAssemblyResult>> CalculatePackageAssembly(
        CanonicalPopulation population,
        PackageAssemblyRequest request,
        StatisticsWorkBudget budget,
        CancellationToken cancellationToken)
    {
        if (request.DrawCount < 0 || request.DrawCount > population.TotalCount)
        {
            return Invalid<PackageAssemblyResult>(
                "request.drawCount must be between zero and the selected population size.");
        }

        if (!TryPrepareRequirements(
                population,
                request.Requirements,
                out PackageRequirementInput[] requirements,
                out string[] groups,
                out PreparedPackageRequirement[] prepared,
                out string? error))
        {
            return Invalid<PackageAssemblyResult>(error!);
        }

        MembershipEnumerationResult enumeration = MembershipBucketEngine.Evaluate(
            population,
            request.DrawCount,
            groups,
            budget,
            state => AllocationMatcher.CanAllocate(state, prepared),
            cancellationToken);
        if (enumeration.IsBounded)
        {
            return Bounded<PackageAssemblyResult>(
                budget,
                enumeration.EstimatedStates,
                population,
                "Reduce the draw size, group count, or package requirements.");
        }

        ExactFraction probability = new(
            enumeration.FavorableWeight,
            enumeration.TotalWeight);
        PackageAssemblyResult result = new(
            CreateDerivation(
                "package-capacity-allocation",
                [
                    "Requirement and card or tutor capability groups were supplied explicitly.",
                    "Each physical drawn copy may satisfy at most one required slot.",
                ]),
            population.ToSnapshot(),
            request.DrawCount,
            requirements,
            probability.ToProbability(),
            ExactFraction.One.Subtract(probability).ToProbability());
        return Exact(result);
    }

    /// <summary>
    /// Validates and canonically orders observed-group conditions.
    /// </summary>
    private static bool TryPrepareConditions(
        CanonicalPopulation population,
        IReadOnlyList<StatisticsGroupConditionInput>? inputs,
        out StatisticsGroupConditionInput[] conditions,
        out string? error)
    {
        conditions = [];
        error = null;
        if (inputs is null || inputs.Count is < 1 or > StatisticsPopulationValidator.MaximumGroups)
        {
            error = "request.conditions must contain between one and eight conditions.";
            return false;
        }

        HashSet<string> names = new(StringComparer.Ordinal);
        List<StatisticsGroupConditionInput> validated = [];
        foreach (StatisticsGroupConditionInput? input in inputs)
        {
            bool maximumInvalid = input?.Maximum is int maximum && maximum < input.Minimum;
            if (input is null ||
                !TryValidateExactGroup(input.Group, out string group) ||
                !population.HasGroup(group) ||
                input.Minimum < 0 ||
                maximumInvalid ||
                !names.Add(group))
            {
                error = "request.conditions must use unique declared groups and valid nonnegative ranges.";
                return false;
            }

            validated.Add(input with { Group = group });
        }

        validated.Sort(static (left, right) =>
            string.Compare(left.Group, right.Group, StringComparison.Ordinal));
        conditions = [.. validated];
        return true;
    }

    /// <summary>
    /// Tests one observed group-count vector against all canonical conditions.
    /// </summary>
    private static bool MatchesConditions(
        int[] groupCounts,
        IReadOnlyList<StatisticsGroupConditionInput> conditions)
    {
        for (int index = 0; index < conditions.Count; index++)
        {
            StatisticsGroupConditionInput condition = conditions[index];
            int count = groupCounts[index];
            if (count < condition.Minimum ||
                (condition.Maximum is int maximum && count > maximum))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates package requirements and maps their group capabilities to bit masks.
    /// </summary>
    private static bool TryPrepareRequirements(
        CanonicalPopulation population,
        IReadOnlyList<PackageRequirementInput>? inputs,
        out PackageRequirementInput[] requirements,
        out string[] groups,
        out PreparedPackageRequirement[] prepared,
        out string? error)
    {
        requirements = [];
        groups = [];
        prepared = [];
        error = null;
        if (inputs is null || inputs.Count is < 1 or > StatisticsPopulationValidator.MaximumGroups)
        {
            error = "request.requirements must contain between one and eight requirements.";
            return false;
        }

        HashSet<string> requirementNames = new(StringComparer.Ordinal);
        HashSet<string> allGroups = new(StringComparer.Ordinal);
        List<PackageRequirementInput> validated = [];
        foreach (PackageRequirementInput? input in inputs)
        {
            if (input is null ||
                !TryValidateExactGroup(input.Name, out string name) ||
                !requirementNames.Add(name) ||
                input.Count <= 0 ||
                !TryPrepareEligibleGroups(population, input.EligibleGroups, allGroups, out string[] eligible))
            {
                error = "request.requirements must use unique names, positive counts, and declared eligible groups.";
                return false;
            }

            validated.Add(new PackageRequirementInput(name, input.Count, eligible));
        }

        validated.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        groups = [.. allGroups];
        Array.Sort(groups, StringComparer.Ordinal);
        prepared = new PreparedPackageRequirement[validated.Count];
        for (int index = 0; index < validated.Count; index++)
        {
            PackageRequirementInput requirement = validated[index];
            int eligibleMask = 0;
            foreach (string group in requirement.EligibleGroups)
            {
                int groupIndex = Array.BinarySearch(groups, group, StringComparer.Ordinal);
                eligibleMask |= 1 << groupIndex;
            }

            prepared[index] = new PreparedPackageRequirement(requirement.Count, eligibleMask);
        }

        requirements = [.. validated];
        return true;
    }

    /// <summary>
    /// Validates and canonically orders one requirement's eligible groups.
    /// </summary>
    private static bool TryPrepareEligibleGroups(
        CanonicalPopulation population,
        IReadOnlyList<string>? inputs,
        HashSet<string> allGroups,
        out string[] eligible)
    {
        eligible = [];
        if (inputs is null || inputs.Count == 0)
        {
            return false;
        }

        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string? input in inputs)
        {
            if (!TryValidateExactGroup(input, out string group) ||
                !population.HasGroup(group) ||
                !unique.Add(group))
            {
                return false;
            }

            allGroups.Add(group);
        }

        eligible = [.. unique];
        Array.Sort(eligible, StringComparer.Ordinal);
        return true;
    }
}

/// <summary>
/// Stores one canonical package requirement as a group-capability mask.
/// </summary>
internal sealed record PreparedPackageRequirement(int Count, int EligibleMask);

/// <summary>
/// Proves one-use allocation from drawn membership buckets to package requirements.
/// </summary>
internal static class AllocationMatcher
{
    /// <summary>
    /// Reports whether the drawn copies can fill every required slot exactly once.
    /// </summary>
    internal static bool CanAllocate(
        ProjectedDrawState state,
        IReadOnlyList<PreparedPackageRequirement> requirements)
    {
        int required = requirements.Sum(value => value.Count);
        if (state.DrawnByBucket.Sum() < required)
        {
            return false;
        }

        int bucketCount = state.Buckets.Length;
        int source = 0;
        int firstBucket = 1;
        int firstRequirement = firstBucket + bucketCount;
        int sink = firstRequirement + requirements.Count;
        int[,] capacity = new int[sink + 1, sink + 1];
        for (int bucketIndex = 0; bucketIndex < bucketCount; bucketIndex++)
        {
            int available = state.DrawnByBucket[bucketIndex];
            capacity[source, firstBucket + bucketIndex] = available;
            for (int requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
            {
                if ((state.Buckets[bucketIndex].MembershipMask &
                    requirements[requirementIndex].EligibleMask) != 0)
                {
                    capacity[firstBucket + bucketIndex, firstRequirement + requirementIndex] = available;
                }
            }
        }

        for (int requirementIndex = 0; requirementIndex < requirements.Count; requirementIndex++)
        {
            capacity[firstRequirement + requirementIndex, sink] = requirements[requirementIndex].Count;
        }

        return IntegralMaximumFlow.Calculate(capacity, source, sink) == required;
    }
}

/// <summary>
/// Calculates deterministic integral maximum flow for exact one-use allocation checks.
/// </summary>
internal static class IntegralMaximumFlow
{
    /// <summary>
    /// Computes deterministic integral maximum flow over one small capacity graph.
    /// </summary>
    internal static int Calculate(int[,] capacity, int source, int sink)
    {
        int nodeCount = capacity.GetLength(0);
        int[,] residual = (int[,])capacity.Clone();
        int flow = 0;
        int[] parent = new int[nodeCount];
        while (FindPath(residual, source, sink, parent))
        {
            int pathCapacity = int.MaxValue;
            for (int node = sink; node != source; node = parent[node])
            {
                pathCapacity = Math.Min(pathCapacity, residual[parent[node], node]);
            }

            for (int node = sink; node != source; node = parent[node])
            {
                int previous = parent[node];
                residual[previous, node] -= pathCapacity;
                residual[node, previous] += pathCapacity;
            }

            flow += pathCapacity;
        }

        return flow;
    }

    /// <summary>
    /// Finds one deterministic breadth-first augmenting path.
    /// </summary>
    private static bool FindPath(int[,] residual, int source, int sink, int[] parent)
    {
        Array.Fill(parent, -1);
        parent[source] = source;
        Queue<int> pending = new();
        pending.Enqueue(source);
        while (pending.Count > 0)
        {
            int current = pending.Dequeue();
            for (int next = 0; next < parent.Length; next++)
            {
                if (parent[next] != -1 || residual[current, next] <= 0)
                {
                    continue;
                }

                parent[next] = current;
                if (next == sink)
                {
                    return true;
                }

                pending.Enqueue(next);
            }
        }

        return false;
    }
}
