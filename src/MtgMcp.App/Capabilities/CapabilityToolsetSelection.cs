using System.Collections.Immutable;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Capabilities;

/// <summary>
/// Holds one canonical startup selection independently of operation-mode authority.
/// </summary>
internal sealed class CapabilityToolsetSelection
{
    /// <summary>
    /// Creates a selection from an already canonical implemented descriptor order.
    /// </summary>
    internal CapabilityToolsetSelection(
        CapabilityToolsetSelectionKind kind,
        IEnumerable<CapabilityToolset> enabledToolsets)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown capability selection kind.");
        }

        ArgumentNullException.ThrowIfNull(enabledToolsets);
        Kind = kind;
        EnabledToolsets = [.. enabledToolsets];
        HashSet<CapabilityToolset> uniqueToolsets = [];
        foreach (CapabilityToolset toolset in EnabledToolsets)
        {
            if (!Enum.IsDefined(toolset) || !uniqueToolsets.Add(toolset))
            {
                throw new ArgumentException(
                    "Enabled toolsets must be defined and unique.",
                    nameof(enabledToolsets));
            }
        }
    }

    /// <summary>
    /// Gets how this selection was configured.
    /// </summary>
    internal CapabilityToolsetSelectionKind Kind { get; }

    /// <summary>
    /// Gets enabled implemented toolsets in registry order.
    /// </summary>
    internal ImmutableArray<CapabilityToolset> EnabledToolsets { get; }

    /// <summary>
    /// Gets the sanitized public selection label.
    /// </summary>
    internal string Label => CapabilityToolsetPolicy.Format(Kind);

    /// <summary>
    /// Reports whether one implemented toolset was selected at startup.
    /// </summary>
    internal bool Includes(CapabilityToolset toolset)
    {
        return EnabledToolsets.Contains(toolset);
    }
}

/// <summary>
/// Resolves reserved profiles and explicit names against implemented descriptors.
/// </summary>
internal static class CapabilityToolsetSelectionParser
{
    /// <summary>
    /// Resolves omitted or configured input into one canonical static selection.
    /// </summary>
    internal static OperationResult<CapabilityToolsetSelection> Parse(
        string? value,
        IReadOnlyList<CapabilityToolsetDescriptor> implementedDescriptors)
    {
        ArgumentNullException.ThrowIfNull(implementedDescriptors);
        string normalized = value is null ? "default" : value.Trim();
        if (normalized.Length == 0)
        {
            return InvalidSelection();
        }
        if (string.Equals(normalized, "default", StringComparison.Ordinal))
        {
            return Success(
                CapabilityToolsetSelectionKind.Default,
                implementedDescriptors,
                descriptor =>
                    descriptor.Stability == CapabilityToolsetStability.Stable &&
                    descriptor.DefaultEnabled);
        }

        if (string.Equals(normalized, "all", StringComparison.Ordinal))
        {
            return Success(
                CapabilityToolsetSelectionKind.All,
                implementedDescriptors,
                descriptor => descriptor.Stability == CapabilityToolsetStability.Stable);
        }

        if (string.Equals(normalized, "none", StringComparison.Ordinal))
        {
            return new OperationSuccess<CapabilityToolsetSelection>(
                new CapabilityToolsetSelection(CapabilityToolsetSelectionKind.None, []));
        }

        string[] requestedNames = normalized.Split(',', StringSplitOptions.None);
        HashSet<CapabilityToolset> requested = [];
        foreach (string rawName in requestedNames)
        {
            string name = rawName.Trim();
            CapabilityToolsetDescriptor? descriptor = FindByName(name, implementedDescriptors);
            if (descriptor is null || !requested.Add(descriptor.Toolset))
            {
                return InvalidSelection();
            }
        }

        if (requested.Count == 0)
        {
            return InvalidSelection();
        }

        return Success(
            CapabilityToolsetSelectionKind.Explicit,
            implementedDescriptors,
            descriptor => requested.Contains(descriptor.Toolset));
    }

    /// <summary>
    /// Finds an exact implemented lowercase name without accepting aliases or case variants.
    /// </summary>
    private static CapabilityToolsetDescriptor? FindByName(
        string name,
        IReadOnlyList<CapabilityToolsetDescriptor> descriptors)
    {
        foreach (CapabilityToolsetDescriptor descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a successful selection in implemented registry order.
    /// </summary>
    private static OperationResult<CapabilityToolsetSelection> Success(
        CapabilityToolsetSelectionKind kind,
        IReadOnlyList<CapabilityToolsetDescriptor> descriptors,
        Func<CapabilityToolsetDescriptor, bool> predicate)
    {
        List<CapabilityToolset> selected = [];
        foreach (CapabilityToolsetDescriptor descriptor in descriptors)
        {
            if (predicate(descriptor))
            {
                selected.Add(descriptor.Toolset);
            }
        }

        return new OperationSuccess<CapabilityToolsetSelection>(
            new CapabilityToolsetSelection(kind, selected));
    }

    /// <summary>
    /// Returns one stable path-free diagnostic for every rejected selection.
    /// </summary>
    private static OperationInvalidInput InvalidSelection()
    {
        return new OperationInvalidInput(
            "invalid-capability-toolsets",
            "Toolsets must name implemented lowercase capabilities or use default, all, or none.");
    }
}
