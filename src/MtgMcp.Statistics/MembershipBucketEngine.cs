using System.Numerics;

namespace MtgMcp.Statistics;

/// <summary>
/// Stores one population bucket projected onto the groups relevant to an event.
/// </summary>
internal sealed record ProjectedPopulationBucket(int Count, int MembershipMask);

/// <summary>
/// Carries one complete feasible draw vector and its observed group counts.
/// </summary>
internal sealed record ProjectedDrawState(
    int[] DrawnByBucket,
    ProjectedPopulationBucket[] Buckets,
    int[] GroupCounts,
    BigInteger Weight);

/// <summary>
/// Reports exact enumeration totals or a request-wide budget stop.
/// </summary>
internal sealed record MembershipEnumerationResult(
    BigInteger FavorableWeight,
    BigInteger TotalWeight,
    bool IsBounded,
    long EstimatedStates);

/// <summary>
/// Enumerates disjoint membership-bucket draws with exact combinatorial weights.
/// </summary>
internal static class MembershipBucketEngine
{
    /// <summary>
    /// Evaluates one predicate over every feasible projected draw vector.
    /// </summary>
    internal static MembershipEnumerationResult Evaluate(
        CanonicalPopulation population,
        int drawCount,
        IReadOnlyList<string> relevantGroups,
        StatisticsWorkBudget budget,
        Func<ProjectedDrawState, bool> predicate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(relevantGroups);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(predicate);

        ProjectedPopulationBucket[] buckets = Project(population, relevantGroups);
        long estimatedStates = EstimateStates(buckets, drawCount);
        int[] drawn = new int[buckets.Length];
        CombinationCache combinations = new();
        BigInteger favorable = BigInteger.Zero;
        bool bounded = false;

        Enumerate(
            bucketIndex: 0,
            remaining: drawCount,
            weight: BigInteger.One,
            buckets,
            drawn,
            relevantGroups.Count,
            combinations,
            budget,
            predicate,
            cancellationToken,
            ref favorable,
            ref bounded);

        return new MembershipEnumerationResult(
            favorable,
            combinations.Choose(population.TotalCount, drawCount),
            bounded,
            estimatedStates);
    }

    /// <summary>
    /// Collapses canonical buckets by their membership mask for the requested groups.
    /// </summary>
    internal static ProjectedPopulationBucket[] Project(
        CanonicalPopulation population,
        IReadOnlyList<string> relevantGroups)
    {
        Dictionary<int, int> counts = [];
        foreach (CanonicalPopulationBucket bucket in population.Buckets)
        {
            int mask = 0;
            for (int groupIndex = 0; groupIndex < relevantGroups.Count; groupIndex++)
            {
                if (Array.BinarySearch(
                        bucket.Groups,
                        relevantGroups[groupIndex],
                        StringComparer.Ordinal) >= 0)
                {
                    mask |= 1 << groupIndex;
                }
            }

            counts[mask] = checked(counts.GetValueOrDefault(mask) + bucket.Count);
        }

        int[] masks = [.. counts.Keys];
        Array.Sort(masks);
        ProjectedPopulationBucket[] projected = new ProjectedPopulationBucket[masks.Length];
        for (int index = 0; index < masks.Length; index++)
        {
            projected[index] = new ProjectedPopulationBucket(counts[masks[index]], masks[index]);
        }

        return projected;
    }

    /// <summary>
    /// Recursively enumerates only vectors whose bucket draws sum to the requested draw count.
    /// </summary>
    private static void Enumerate(
        int bucketIndex,
        int remaining,
        BigInteger weight,
        ProjectedPopulationBucket[] buckets,
        int[] drawn,
        int groupCount,
        CombinationCache combinations,
        StatisticsWorkBudget budget,
        Func<ProjectedDrawState, bool> predicate,
        CancellationToken cancellationToken,
        ref BigInteger favorable,
        ref bool bounded)
    {
        if (bounded)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (bucketIndex == buckets.Length)
        {
            if (remaining != 0)
            {
                return;
            }

            if (!budget.TryConsume(1))
            {
                bounded = true;
                return;
            }

            int[] groupCounts = CountGroups(buckets, drawn, groupCount);
            ProjectedDrawState state = new((int[])drawn.Clone(), buckets, groupCounts, weight);
            if (predicate(state))
            {
                favorable += weight;
            }

            return;
        }

        ProjectedPopulationBucket bucket = buckets[bucketIndex];
        int remainingCapacity = 0;
        for (int index = bucketIndex + 1; index < buckets.Length; index++)
        {
            remainingCapacity += buckets[index].Count;
        }

        int minimum = Math.Max(0, remaining - remainingCapacity);
        int maximum = Math.Min(bucket.Count, remaining);
        for (int selected = minimum; selected <= maximum; selected++)
        {
            drawn[bucketIndex] = selected;
            Enumerate(
                bucketIndex + 1,
                remaining - selected,
                weight * combinations.Choose(bucket.Count, selected),
                buckets,
                drawn,
                groupCount,
                combinations,
                budget,
                predicate,
                cancellationToken,
                ref favorable,
                ref bounded);
            if (bounded)
            {
                return;
            }
        }

        drawn[bucketIndex] = 0;
    }

    /// <summary>
    /// Calculates observed group counts for one complete disjoint draw vector.
    /// </summary>
    private static int[] CountGroups(
        ProjectedPopulationBucket[] buckets,
        int[] drawn,
        int groupCount)
    {
        int[] counts = new int[groupCount];
        for (int bucketIndex = 0; bucketIndex < buckets.Length; bucketIndex++)
        {
            int membership = buckets[bucketIndex].MembershipMask;
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                if ((membership & (1 << groupIndex)) != 0)
                {
                    counts[groupIndex] += drawn[bucketIndex];
                }
            }
        }

        return counts;
    }

    /// <summary>
    /// Computes a safe saturating upper bound on projected draw vectors.
    /// </summary>
    private static long EstimateStates(ProjectedPopulationBucket[] buckets, int drawCount)
    {
        long estimate = 1;
        foreach (ProjectedPopulationBucket bucket in buckets)
        {
            estimate = StatisticsWorkBudget.SaturatingMultiply(
                estimate,
                Math.Min(bucket.Count, drawCount) + 1L);
        }

        return estimate;
    }
}
