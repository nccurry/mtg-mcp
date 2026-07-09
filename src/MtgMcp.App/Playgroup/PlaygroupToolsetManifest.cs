using Microsoft.Extensions.DependencyInjection;
using MtgMcp.App.Capabilities;
using MtgMcp.App.Configuration;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Playgroup;

/// <summary>
/// Owns the exact opt-in Playgroup surface and operation-mode registration.
/// </summary>
internal static class PlaygroupToolsetManifest
{
    /// <summary>
    /// Gets the descriptor for thirteen reads, redacted auth status, and two remote writes.
    /// </summary>
    internal static CapabilityToolsetDescriptor Descriptor { get; } = new(
        CapabilityToolset.Playgroup,
        CapabilityToolsetStability.Stable,
        "Provider-shaped evidence from every documented Playgroup Public API 1.0.0 operation. Deck updates are explicitly unsupported; this provider toolset is opt-in.",
        [
            "playgroup_auth_status",
            "playgroup_commander_get",
            "playgroup_commander_get_by_name",
            "playgroup_commander_turn_damage_get",
            "playgroup_deck_elo_history_get",
            "playgroup_deck_get",
            "playgroup_me_get",
            "playgroup_playgroup_game_get",
            "playgroup_playgroup_games_list",
            "playgroup_playgroup_members_list",
            "playgroup_user_decks_list",
            "playgroup_user_get",
            "playgroup_user_playgroup_get",
            "playgroup_user_playgroups_list",
        ],
        [],
        [
            "playgroup_game_events_batch_create",
            "playgroup_live_session_create",
        ]);

    /// <summary>
    /// Registers the exact Playgroup tools visible in the effective operation mode.
    /// </summary>
    internal static void Register(IMcpServerBuilder builder, PlaygroupService service, OperationMode mode)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(service);
        builder.WithTools(new PlaygroupReadTools(service));
        if (OperationModeGuard.Allows(mode, OperationRequirement.RemoteWrite))
        {
            builder.WithTools(new PlaygroupRemoteWriteTools(service, mode));
        }
    }
}
