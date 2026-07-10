using System.Collections.Immutable;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.App.Decks;
using MtgMcp.App.Playgroup;
using MtgMcp.App.Scryfall;
using MtgMcp.App.Statistics;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Capabilities;

/// <summary>
/// Provides the single ordered inventory of capability descriptors implemented by this build.
/// </summary>
internal static class CapabilityToolsetRegistry
{
    /// <summary>
    /// Gets implemented descriptors in canonical public order.
    /// </summary>
    internal static ImmutableArray<CapabilityToolsetDescriptor> Implemented { get; } =
        [
            DeckToolsetManifest.Descriptor,
            ScryfallToolsetManifest.Descriptor,
            StatisticsToolsetManifest.Descriptor,
            ArchidektToolsetManifest.Descriptor,
            PlaygroupToolsetManifest.Descriptor,
        ];

    /// <summary>
    /// Resolves one configured startup selection against this build's implemented descriptors.
    /// </summary>
    internal static OperationResult<CapabilityToolsetSelection> Resolve(string? value)
    {
        return CapabilityToolsetSelectionParser.Parse(value, Implemented);
    }

    /// <summary>
    /// Counts selected tools visible in one operation mode from their owning descriptors.
    /// </summary>
    internal static int CountVisibleTools(
        CapabilityToolsetSelection selection,
        OperationMode mode)
    {
        ArgumentNullException.ThrowIfNull(selection);
        int count = 0;
        foreach (CapabilityToolsetDescriptor descriptor in Implemented)
        {
            if (selection.Includes(descriptor.Toolset))
            {
                count += descriptor.GetVisibleToolCount(mode);
            }
        }

        return count;
    }
}
