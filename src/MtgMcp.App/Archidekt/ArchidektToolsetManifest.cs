using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Decks;

namespace MtgMcp.App.Archidekt;

/// <summary>
/// Owns the exact Archidekt tool assignment and explicit opt-in registration group.
/// </summary>
internal static class ArchidektToolsetManifest
{
    /// <summary>
    /// Gets the descriptor that owns all 23 provider tools and their mode visibility.
    /// </summary>
    internal static CapabilityToolsetDescriptor Descriptor { get; } = new(
        CapabilityToolset.Archidekt,
        CapabilityToolsetStability.Stable,
        "Fresh Archidekt evidence plus explicit guarded deck, folder, snapshot, and synchronization workflows. This provider toolset is opt-in; operation mode separately controls authority.",
        [
            "archidekt_auth_status",
            "archidekt_deck_get",
            "archidekt_deck_list",
            "archidekt_folder_get",
            "archidekt_folder_list",
            "archidekt_pull_preview",
            "archidekt_push_preview",
            "archidekt_snapshot_get",
            "archidekt_snapshot_list",
            "archidekt_snapshot_restore_preview",
            "archidekt_sync_diff",
        ],
        ["archidekt_pull_apply"],
        [
            "archidekt_deck_create",
            "archidekt_deck_delete",
            "archidekt_folder_create",
            "archidekt_folder_delete",
            "archidekt_folder_move_items",
            "archidekt_folder_update",
            "archidekt_push_apply",
            "archidekt_snapshot_create",
            "archidekt_snapshot_delete",
            "archidekt_snapshot_restore_apply",
            "archidekt_snapshot_update",
        ]);

    /// <summary>
    /// Registers the exact Archidekt surface selected for this static session and mode.
    /// </summary>
    internal static void Register(
        IMcpServerBuilder builder,
        ArchidektService service,
        SqliteDeckStore deckStore,
        OperationMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(deckStore);
        ArchidektCoordinator coordinator = new(service, deckStore);
        builder.WithTools(new ArchidektReadTools(coordinator));
        if (OperationModeGuard.Allows(mode, OperationRequirement.LocalWrite))
        {
            builder.WithTools(new ArchidektLocalWriteTools(coordinator, mode));
        }

        if (OperationModeGuard.Allows(mode, OperationRequirement.RemoteWrite))
        {
            builder.WithTools(new ArchidektRemoteWriteTools(coordinator, mode));
        }
    }
}
