using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Decks;
using MtgMcp.Scryfall;

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
        CapabilityToolsetStability.Stable,
        "Local deck storage and manual interchange. Toolset selection controls relevance; operation mode separately controls local writes.",
        [
            "deck_backup_list",
            "deck_category_rules_preview",
            "deck_category_rules_validate",
            "deck_export_bundle",
            "deck_get",
            "deck_import_preview",
            "deck_identity_reconcile_preview",
            "deck_interchange_formats",
            "deck_list",
            "deck_on_curve_estimate",
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
            "deck_category_rules_apply",
            "deck_create",
            "deck_delete",
            "deck_entry_add",
            "deck_entry_remove",
            "deck_entry_update",
            "deck_import_create",
            "deck_identity_reconcile_apply",
            "deck_update",
        ],
        []);

    /// <summary>
    /// Registers the deck family selected for this static session and active mode.
    /// </summary>
    internal static void Register(
        IMcpServerBuilder builder,
        SqliteDeckStore deckStore,
        ScryfallService scryfallService,
        OperationMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(deckStore);
        ArgumentNullException.ThrowIfNull(scryfallService);

        DeckInterchangeService interchangeService = new(deckStore);
        DeckIdentityReconciliationCoordinator identityCoordinator = new(deckStore, scryfallService);
        DeckCategorizationCoordinator categorizationCoordinator = new(deckStore, scryfallService);
        DeckOnCurveEstimateCoordinator onCurveCoordinator = new(deckStore, scryfallService);
        builder
            .WithTools(new DeckReadTools(deckStore), ToolCallPolicy.SerializerOptions)
            .WithTools(new DeckInterchangeReadTools(interchangeService), ToolCallPolicy.SerializerOptions)
            .WithTools(new DeckIdentityReconciliationReadTools(identityCoordinator), ToolCallPolicy.SerializerOptions)
            .WithTools(new DeckOnCurveEstimateTools(onCurveCoordinator), ToolCallPolicy.SerializerOptions);
        builder.WithTools(new DeckCategorizationReadTools(categorizationCoordinator), ToolCallPolicy.SerializerOptions);
        if (OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            builder
                .WithTools(new DeckWriteTools(deckStore, mode), ToolCallPolicy.SerializerOptions)
                .WithTools(new DeckInterchangeWriteTools(interchangeService, mode), ToolCallPolicy.SerializerOptions)
                .WithTools(new DeckIdentityReconciliationWriteTools(identityCoordinator, mode), ToolCallPolicy.SerializerOptions)
                .WithTools(new DeckCategorizationWriteTools(categorizationCoordinator, mode), ToolCallPolicy.SerializerOptions)
                .WithTools([DeckBatchWriteTools.Create(deckStore, mode)]);
        }
    }
}
