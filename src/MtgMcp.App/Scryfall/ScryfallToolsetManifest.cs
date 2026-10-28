using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
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
        CapabilityToolsetStability.Stable,
        "Official Scryfall evidence with exact-request snapshots, explicitly synchronized " +
        "installed card data, and community tags kept distinct from card facts.",
        [
            "scryfall_autocomplete",
            "scryfall_card_data_status",
            "scryfall_card_details",
            "scryfall_cards_get",
            "scryfall_installed_cards_by_tag",
            "scryfall_reference_get",
            "scryfall_search",
            "scryfall_search_snapshots_get",
            "scryfall_tags_search",
        ],
        [
            "scryfall_card_data_delete",
            "scryfall_card_data_rollback",
            "scryfall_card_data_sync",
            "scryfall_search_snapshot_delete",
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

        builder
            .WithRegisteredTools(ToolRegistration.Create(new ScryfallReadTools(service), ScryfallReadTools.ItemChoices))
            .WithRegisteredTools(ToolRegistration.Create(
                new ScryfallReferenceTools(service),
                ScryfallReferenceTools.ItemChoices));
        if (OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            builder.WithRegisteredTools(ToolRegistration.Create(new ScryfallWriteTools(service, mode)));
        }
    }
}
