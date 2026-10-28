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
            "local_deck_backup_list",
            "local_deck_category_rules_preview",
            "local_deck_export_bundle",
            "local_deck_get",
            "local_deck_import_preview",
            "local_deck_identity_reconcile_preview",
            "local_deck_interchange_formats",
            "local_deck_list",
            "local_deck_on_curve_estimate",
            "local_deck_validate",
        ],
        [
            "local_deck_apply_changes",
            "local_deck_backup_create",
            "local_deck_backup_delete",
            "local_deck_backup_restore",
            "local_deck_category_rules_apply",
            "local_deck_create",
            "local_deck_delete",
            "local_deck_import_create",
            "local_deck_identity_reconcile_apply",
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
        DeckCardViewCoordinator cardViewCoordinator = new(deckStore, scryfallService);
        builder
            .WithTools(ToolRegistration.Create(
                new DeckReadTools(deckStore, cardViewCoordinator),
                DeckReadTools.ItemChoices))
            .WithTools(ToolRegistration.Create(new DeckInterchangeReadTools(interchangeService)))
            .WithTools(ToolRegistration.Create(new DeckIdentityReconciliationReadTools(identityCoordinator)))
            .WithTools(ToolRegistration.Create(new DeckOnCurveEstimateTools(onCurveCoordinator)));
        builder.WithTools(ToolRegistration.Create(new DeckCategorizationReadTools(categorizationCoordinator)));
        if (OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            builder
                .WithTools(ToolRegistration.Create(new DeckWriteTools(deckStore, mode)))
                .WithTools(ToolRegistration.Create(new DeckInterchangeWriteTools(interchangeService, mode)))
                .WithTools(ToolRegistration.Create(new DeckIdentityReconciliationWriteTools(identityCoordinator, mode)))
                .WithTools(ToolRegistration.Create(new DeckCategorizationWriteTools(categorizationCoordinator, mode)))
                .WithTools([DeckBatchWriteTools.Create(deckStore, mode)]);
        }
    }
}
