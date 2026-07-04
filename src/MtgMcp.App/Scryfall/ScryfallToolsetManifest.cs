using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.Scryfall;

namespace MtgMcp.App.Scryfall;

/// <summary>
/// Owns the exact Scryfall tool assignment and explicit registration group.
/// </summary>
internal static class ScryfallToolsetManifest
{
    /// <summary>
    /// Gets the descriptor for official provider evidence, the shared corpus, snapshots, and tags.
    /// </summary>
    internal static CapabilityToolsetDescriptor Descriptor { get; } = new(
        CapabilityToolset.Scryfall,
        CapabilityToolsetAvailability.Available,
        CapabilityToolsetStability.Stable,
        "Official Scryfall evidence with exact-request snapshots, an explicitly synchronized local " +
        "corpus, and community tags kept distinct from card facts.",
        [
            "scryfall_autocomplete",
            "scryfall_bulk_metadata",
            "scryfall_card_collection",
            "scryfall_card_get",
            "scryfall_card_prints",
            "scryfall_card_rulings",
            "scryfall_cards_by_tag",
            "scryfall_catalog",
            "scryfall_corpus_status",
            "scryfall_search",
            "scryfall_sets",
            "scryfall_snapshot_get",
            "scryfall_snapshot_list",
            "scryfall_tag_search",
        ],
        [
            "scryfall_corpus_delete",
            "scryfall_corpus_rollback",
            "scryfall_corpus_sync",
            "scryfall_snapshot_delete",
        ],
        []);

    /// <summary>
    /// Registers the selected Scryfall family for the static session and active mode.
    /// </summary>
    internal static void Register(
        IMcpServerBuilder builder,
        ScryfallService service,
        OperationMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(service);

        builder.WithTools(new ScryfallReadTools(service));
        if (OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            builder.WithTools(new ScryfallWriteTools(service, mode));
        }
    }
}
