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
            "local_deck_duplicates_preview",
            "local_deck_export_bundle",
            "local_deck_get",
            "local_deck_import_preview",
            "local_deck_identity_reconcile_preview",
            "local_deck_interchange_formats",
            "local_deck_list",
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
        DeckCardViewCoordinator cardViewCoordinator = new(deckStore, scryfallService);
        builder
            .WithRegisteredTools(ToolRegistration.Create(
                new DeckReadTools(deckStore, cardViewCoordinator, new DeckRuleCoordinator(deckStore, scryfallService)),
                DeckReadTools.ItemChoices))
            .WithRegisteredTools(ToolRegistration.Create(new DeckInterchangeReadTools(interchangeService)))
            .WithRegisteredTools(ToolRegistration.Create(new DeckIdentityReconciliationReadTools(identityCoordinator)))
            .WithRegisteredTools(ToolRegistration.Create(
                new DeckDuplicatesReadTools(new DeckDuplicatesCoordinator(deckStore, scryfallService))));
        builder.WithRegisteredTools(
            ToolRegistration.Create(new DeckCategorizationReadTools(categorizationCoordinator)));
        if (OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            builder
                .WithRegisteredTools(ToolRegistration.Create(new DeckWriteTools(deckStore, mode)))
                .WithRegisteredTools(ToolRegistration.Create(new DeckInterchangeWriteTools(interchangeService, mode)))
                .WithRegisteredTools(ToolRegistration.Create(
                    new DeckIdentityReconciliationWriteTools(identityCoordinator, mode)))
                .WithRegisteredTools(ToolRegistration.Create(
                    new DeckCategorizationWriteTools(categorizationCoordinator, mode)))
                .WithRegisteredTools([DeckBatchWriteTools.Create(deckStore, mode)]);
        }
    }
}
