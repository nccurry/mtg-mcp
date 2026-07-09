using System.Collections.Immutable;
using MtgMcp.App.Configuration;

namespace MtgMcp.App.Capabilities;

/// <summary>
/// Owns the exact stable tool names and metadata assigned to one implemented capability family.
/// </summary>
internal sealed class CapabilityToolsetDescriptor
{
    /// <summary>
    /// Stores the canonical names visible in read-only mode.
    /// </summary>
    private readonly ImmutableArray<string> readOnlyToolNames;

    /// <summary>
    /// Stores the canonical names visible when local writes are permitted.
    /// </summary>
    private readonly ImmutableArray<string> localToolNames;

    /// <summary>
    /// Creates one immutable descriptor from disjoint authority groups.
    /// </summary>
    internal CapabilityToolsetDescriptor(
        CapabilityToolset toolset,
        CapabilityToolsetStability stability,
        string description,
        IEnumerable<string> readTools,
        IEnumerable<string> localWriteTools,
        IEnumerable<string> remoteWriteTools)
    {
        if (!Enum.IsDefined(toolset))
        {
            throw new ArgumentOutOfRangeException(nameof(toolset), toolset, "Unknown capability toolset.");
        }

        if (!Enum.IsDefined(stability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stability),
                stability,
                "Unknown capability stability.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Toolset = toolset;
        Stability = stability;
        Description = description.Trim();

        ImmutableArray<string> reads = CopyCanonicalNames(readTools, nameof(readTools));
        ImmutableArray<string> localWrites = CopyCanonicalNames(localWriteTools, nameof(localWriteTools));
        ImmutableArray<string> remoteWrites = CopyCanonicalNames(remoteWriteTools, nameof(remoteWriteTools));
        EnsureDisjoint(reads, localWrites, remoteWrites);

        readOnlyToolNames = reads;
        localToolNames = CombineCanonical(reads, localWrites);
        AllToolNames = CombineCanonical(reads, localWrites, remoteWrites);
    }

    /// <summary>
    /// Gets the stable capability identity.
    /// </summary>
    internal CapabilityToolset Toolset { get; }

    /// <summary>
    /// Gets the exact lowercase public name.
    /// </summary>
    internal string Name => CapabilityToolsetPolicy.Format(Toolset);

    /// <summary>
    /// Gets whether the ordinary profile enables this descriptor.
    /// </summary>
    internal bool DefaultEnabled => CapabilityToolsetPolicy.IsDefaultEnabled(Toolset);

    /// <summary>
    /// Gets whether the descriptor participates in stable reserved profiles.
    /// </summary>
    internal CapabilityToolsetStability Stability { get; }

    /// <summary>
    /// Gets the concise model-facing capability and authority explanation.
    /// </summary>
    internal string Description { get; }

    /// <summary>
    /// Gets every tool name assigned to this descriptor in canonical order.
    /// </summary>
    internal ImmutableArray<string> AllToolNames { get; }

    /// <summary>
    /// Gets the exact canonical names visible for one operation mode.
    /// </summary>
    internal ImmutableArray<string> GetVisibleToolNames(OperationMode mode)
    {
        return mode switch
        {
            OperationMode.ReadOnly => readOnlyToolNames,
            OperationMode.Local => localToolNames,
            OperationMode.Remote => AllToolNames,
            _ => [],
        };
    }

    /// <summary>
    /// Gets the number of assigned tools visible for one operation mode.
    /// </summary>
    internal int GetVisibleToolCount(OperationMode mode)
    {
        return GetVisibleToolNames(mode).Length;
    }

    /// <summary>
    /// Copies, validates, and ordinally sorts one tool-name authority group.
    /// </summary>
    private static ImmutableArray<string> CopyCanonicalNames(
        IEnumerable<string> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        string[] names = values.ToArray();
        foreach (string name in names)
        {
            bool invalid =
                string.IsNullOrWhiteSpace(name) ||
                !string.Equals(name, name.Trim(), StringComparison.Ordinal);
            if (invalid)
            {
                throw new ArgumentException("Tool names cannot be blank or padded.", parameterName);
            }
        }

        Array.Sort(names, StringComparer.Ordinal);
        for (int index = 1; index < names.Length; index++)
        {
            if (string.Equals(names[index - 1], names[index], StringComparison.Ordinal))
            {
                throw new ArgumentException("Tool names must be unique.", parameterName);
            }
        }

        return [.. names];
    }

    /// <summary>
    /// Rejects a tool assigned to more than one authority group in the same descriptor.
    /// </summary>
    private static void EnsureDisjoint(params ImmutableArray<string>[] groups)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (ImmutableArray<string> group in groups)
        {
            foreach (string name in group)
            {
                if (!names.Add(name))
                {
                    throw new ArgumentException("A tool can be assigned to only one authority group.");
                }
            }
        }
    }

    /// <summary>
    /// Combines authority groups into one canonical immutable name collection.
    /// </summary>
    private static ImmutableArray<string> CombineCanonical(params ImmutableArray<string>[] groups)
    {
        List<string> names = [];
        foreach (ImmutableArray<string> group in groups)
        {
            names.AddRange(group);
        }

        names.Sort(StringComparer.Ordinal);
        return [.. names];
    }
}
