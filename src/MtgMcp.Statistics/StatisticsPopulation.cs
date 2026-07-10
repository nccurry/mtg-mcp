using System.ComponentModel;
using MtgMcp.Core.Results;

namespace MtgMcp.Statistics;

/// <summary>
/// Describes one caller-supplied disjoint population bucket and its observed groups.
/// </summary>
public sealed record StatisticsPopulationBucket(
    [Description("Positive number of indistinguishable card copies in this disjoint bucket.")]
    int Count,
    [Description("Exact caller-owned group names associated with every copy in this bucket.")]
    IReadOnlyList<string> Groups)
{
    /// <summary>
    /// Gets an immutable copy of the caller-owned group names.
    /// </summary>
    public IReadOnlyList<string> Groups { get; init; } = Groups is null
        ? null!
        : Array.AsReadOnly(Groups.ToArray());
}

/// <summary>
/// Records one selected or excluded local deck entry and its stored quantity.
/// </summary>
public sealed record StatisticsEntryEvidence(Guid EntryId, int Quantity);

/// <summary>
/// Freezes the local deck revision and exact population partition used by a calculation.
/// </summary>
public sealed record StatisticsDeckSelectionEvidence(
    Guid DeckId,
    long Revision,
    IReadOnlyList<StatisticsEntryEvidence> SelectedEntries,
    IReadOnlyList<StatisticsEntryEvidence> ExcludedEntries)
{
    /// <summary>
    /// Gets selected entries in canonical deck order.
    /// </summary>
    public IReadOnlyList<StatisticsEntryEvidence> SelectedEntries { get; init; } =
        Array.AsReadOnly(SelectedEntries.ToArray());

    /// <summary>
    /// Gets excluded entries in canonical deck order.
    /// </summary>
    public IReadOnlyList<StatisticsEntryEvidence> ExcludedEntries { get; init; } =
        Array.AsReadOnly(ExcludedEntries.ToArray());
}

/// <summary>
/// Carries one resolved population with explicit declared group vocabulary.
/// </summary>
public sealed record StatisticsPopulation(
    [Description("Disjoint positive population buckets; their counts sum to the total population.")]
    IReadOnlyList<StatisticsPopulationBucket> Buckets,
    [Description("Complete exact group vocabulary, including declared groups with zero members.")]
    IReadOnlyList<string> DeclaredGroups,
    [property: Description("Frozen local deck selection evidence, or null for a caller-supplied raw population.")]
    StatisticsDeckSelectionEvidence? DeckEvidence = null)
{
    /// <summary>
    /// Gets an immutable copy of population buckets.
    /// </summary>
    public IReadOnlyList<StatisticsPopulationBucket> Buckets { get; init; } = Buckets is null
        ? null!
        : Array.AsReadOnly(Buckets.ToArray());

    /// <summary>
    /// Gets an immutable copy of the declared group vocabulary.
    /// </summary>
    public IReadOnlyList<string> DeclaredGroups { get; init; } = DeclaredGroups is null
        ? null!
        : Array.AsReadOnly(DeclaredGroups.ToArray());
}

/// <summary>
/// Exposes the canonical population inputs retained with an exact result.
/// </summary>
public sealed record StatisticsPopulationSnapshot(
    int TotalCount,
    IReadOnlyList<StatisticsPopulationBucket> Buckets,
    IReadOnlyList<string> DeclaredGroups,
    StatisticsDeckSelectionEvidence? DeckEvidence)
{
    /// <summary>
    /// Gets canonical disjoint buckets ordered by ordinal membership signature.
    /// </summary>
    public IReadOnlyList<StatisticsPopulationBucket> Buckets { get; init; } =
        Array.AsReadOnly(Buckets.ToArray());

    /// <summary>
    /// Gets canonical ordinal group names.
    /// </summary>
    public IReadOnlyList<string> DeclaredGroups { get; init; } =
        Array.AsReadOnly(DeclaredGroups.ToArray());
}

/// <summary>
/// Stores one canonical disjoint bucket used by exact engines.
/// </summary>
internal sealed record CanonicalPopulationBucket(int Count, string[] Groups);

/// <summary>
/// Stores one validated canonical population for exact calculation.
/// </summary>
internal sealed record CanonicalPopulation(
    int TotalCount,
    CanonicalPopulationBucket[] Buckets,
    string[] DeclaredGroups,
    StatisticsDeckSelectionEvidence? DeckEvidence)
{
    /// <summary>
    /// Counts copies whose disjoint bucket contains one exact group.
    /// </summary>
    internal int CountGroup(string group)
    {
        int count = 0;
        foreach (CanonicalPopulationBucket bucket in Buckets)
        {
            if (Array.BinarySearch(bucket.Groups, group, StringComparer.Ordinal) >= 0)
            {
                count = checked(count + bucket.Count);
            }
        }

        return count;
    }

    /// <summary>
    /// Reports whether the caller declared one exact group name.
    /// </summary>
    internal bool HasGroup(string group)
    {
        return Array.BinarySearch(DeclaredGroups, group, StringComparer.Ordinal) >= 0;
    }

    /// <summary>
    /// Projects the validated canonical population into public replay evidence.
    /// </summary>
    internal StatisticsPopulationSnapshot ToSnapshot()
    {
        StatisticsPopulationBucket[] buckets = new StatisticsPopulationBucket[Buckets.Length];
        for (int index = 0; index < Buckets.Length; index++)
        {
            CanonicalPopulationBucket bucket = Buckets[index];
            buckets[index] = new StatisticsPopulationBucket(bucket.Count, bucket.Groups);
        }

        return new StatisticsPopulationSnapshot(TotalCount, buckets, DeclaredGroups, DeckEvidence);
    }
}

/// <summary>
/// Represents population validation success, malformed input, or a deterministic bound.
/// </summary>
internal readonly union PopulationPreparation(
    CanonicalPopulation,
    OperationInvalidInput,
    StatisticsBoundedUnsupported);

/// <summary>
/// Validates and canonicalizes caller population buckets without semantic inference.
/// </summary>
internal static class StatisticsPopulationValidator
{
    /// <summary>
    /// Defines the largest exact population accepted by stable engines.
    /// </summary>
    internal const int MaximumPopulation = 1_000;

    /// <summary>
    /// Defines the largest caller group vocabulary accepted by stable engines.
    /// </summary>
    internal const int MaximumGroups = 8;

    /// <summary>
    /// Produces one canonical population or an explicit input/bound outcome.
    /// </summary>
    internal static PopulationPreparation Prepare(StatisticsPopulation? population)
    {
        if (population is null)
        {
            return Invalid("population is required.");
        }

        if (population.Buckets is null || population.Buckets.Count == 0)
        {
            return Invalid("population.buckets must contain at least one bucket.");
        }

        if (population.DeclaredGroups is null)
        {
            return Invalid("population.declaredGroups is required.");
        }

        HashSet<string> declared = new(StringComparer.Ordinal);
        foreach (string? rawGroup in population.DeclaredGroups)
        {
            if (!TryValidateGroup(rawGroup, out string group) || !declared.Add(group))
            {
                return Invalid(
                    "population.declaredGroups must contain unique, nonblank, unpadded names.");
            }
        }

        if (declared.Count > MaximumGroups)
        {
            return Bounded(
                "group-count",
                MaximumGroups,
                population: 0,
                groupCount: declared.Count,
                "Reduce the declared group count to eight or fewer.");
        }

        Dictionary<string, int> countsBySignature = new(StringComparer.Ordinal);
        long total = 0;
        foreach (StatisticsPopulationBucket? bucket in population.Buckets)
        {
            if (bucket is null || bucket.Count <= 0)
            {
                return Invalid("population.buckets[].count must be positive.");
            }

            if (bucket.Groups is null)
            {
                return Invalid("population.buckets[].groups is required.");
            }

            HashSet<string> bucketGroups = new(StringComparer.Ordinal);
            foreach (string? rawGroup in bucket.Groups)
            {
                if (!TryValidateGroup(rawGroup, out string group) ||
                    !declared.Contains(group) ||
                    !bucketGroups.Add(group))
                {
                    return Invalid(
                        "population.buckets[].groups must contain unique exact declared names.");
                }
            }

            string[] orderedGroups = [.. bucketGroups];
            Array.Sort(orderedGroups, StringComparer.Ordinal);
            string signature = string.Join('\u001f', orderedGroups);
            total += bucket.Count;
            if (total > int.MaxValue)
            {
                return Bounded(
                    "population",
                    MaximumPopulation,
                    population: int.MaxValue,
                    groupCount: declared.Count,
                    "Reduce the selected population to 1,000 cards or fewer.");
            }

            countsBySignature[signature] = checked(
                countsBySignature.GetValueOrDefault(signature) + bucket.Count);
        }

        if (total > MaximumPopulation)
        {
            return Bounded(
                "population",
                MaximumPopulation,
                population: (int)total,
                groupCount: declared.Count,
                "Reduce the selected population to 1,000 cards or fewer.");
        }

        string[] declaredGroups = [.. declared];
        Array.Sort(declaredGroups, StringComparer.Ordinal);
        string[] signatures = [.. countsBySignature.Keys];
        Array.Sort(signatures, StringComparer.Ordinal);
        CanonicalPopulationBucket[] buckets = new CanonicalPopulationBucket[signatures.Length];
        for (int index = 0; index < signatures.Length; index++)
        {
            string signature = signatures[index];
            string[] groups = signature.Length == 0 ? [] : signature.Split('\u001f');
            buckets[index] = new CanonicalPopulationBucket(countsBySignature[signature], groups);
        }

        return new CanonicalPopulation((int)total, buckets, declaredGroups, population.DeckEvidence);
    }

    /// <summary>
    /// Validates an exact nonblank unpadded group name.
    /// </summary>
    private static bool TryValidateGroup(string? rawGroup, out string group)
    {
        group = rawGroup ?? string.Empty;
        return group.Length > 0 &&
            !string.IsNullOrWhiteSpace(group) &&
            string.Equals(group, group.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates one stable malformed-population result.
    /// </summary>
    private static OperationInvalidInput Invalid(string message)
    {
        return new OperationInvalidInput("invalid-statistics-population", message);
    }

    /// <summary>
    /// Creates one structured population-bound result.
    /// </summary>
    private static StatisticsBoundedUnsupported Bounded(
        string limitKind,
        int limit,
        int population,
        int groupCount,
        string reductionOption)
    {
        return new StatisticsBoundedUnsupported(
            "statistics-bound-exceeded",
            "The exact statistics request exceeds a configured deterministic bound.",
            new StatisticsLimitDetail(
                limitKind,
                limit,
                null,
                population,
                groupCount,
                0,
                0,
                [reductionOption]));
    }
}
