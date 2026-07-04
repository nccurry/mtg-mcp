using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Owns the exact deck tool assignment and explicit registration group.
/// </summary>
internal static class DeckToolsetManifest
{
    /// <summary>
    /// Gets the one descriptor that owns every current local deck and interchange tool.
    /// </summary>
    internal static CapabilityToolsetDescriptor Descriptor { get; } = new(
        CapabilityToolset.Decks,
        CapabilityToolsetAvailability.Available,
        CapabilityToolsetStability.Stable,
        "Local deck storage and manual interchange. Toolset selection controls relevance; operation mode separately controls local writes.",
        [
            "deck_backup_list",
            "deck_export_bundle",
            "deck_get",
            "deck_import_preview",
            "deck_interchange_formats",
            "deck_list",
            "deck_validate",
        ],
        [
            "deck_apply_changes",
            "deck_backup_create",
            "deck_backup_delete",
            "deck_backup_restore",
            "deck_category_assign",
            "deck_category_create",
            "deck_category_delete",
            "deck_category_unassign",
            "deck_category_update",
            "deck_create",
            "deck_delete",
            "deck_entry_add",
            "deck_entry_remove",
            "deck_entry_update",
            "deck_import_create",
            "deck_update",
        ],
        []);

    /// <summary>
    /// Registers the deck family selected for this static session and active mode.
    /// </summary>
    internal static void Register(
        IMcpServerBuilder builder,
        SqliteDeckStore deckStore,
        OperationMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(deckStore);

        DeckInterchangeService interchangeService = new(deckStore);
        builder
            .WithTools(new DeckReadTools(deckStore))
            .WithTools(new DeckInterchangeReadTools(interchangeService));
        if (OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            builder
                .WithTools(new DeckWriteTools(deckStore, mode))
                .WithTools(new DeckInterchangeWriteTools(interchangeService, mode));
        }
    }
}
