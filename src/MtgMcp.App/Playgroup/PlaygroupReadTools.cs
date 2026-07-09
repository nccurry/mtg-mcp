using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Playgroup;

/// <summary>
/// Exposes redacted authentication state and every documented Playgroup GET operation.
/// </summary>
internal sealed class PlaygroupReadTools
{
    /// <summary>Provides validated provider operations.</summary>
    private readonly PlaygroupService service;

    /// <summary>Creates the complete read surface.</summary>
    internal PlaygroupReadTools(PlaygroupService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>Reports redacted API-key readiness without provider I/O.</summary>
    [McpServerTool(Name = "playgroup_auth_status", Title = "Inspect Playgroup Authentication", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reports only whether a Playgroup API key is configured; no key, account identity, or path is returned.")]
    internal OperationResult<PlaygroupAuthStatus> AuthStatus()
    {
        return service.GetAuthStatus();
    }

    /// <summary>Gets the authenticated Playgroup user.</summary>
    [McpServerTool(Name = "playgroup_me_get", Title = "Get Current Playgroup User", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets the current user from the documented Playgroup public API with retrieval evidence.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        return service.GetCurrentUserAsync(cancellationToken);
    }

    /// <summary>Gets one commander by provider identifier.</summary>
    [McpServerTool(Name = "playgroup_commander_get", Title = "Get Playgroup Commander", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets one provider-shaped commander observation by exact Playgroup identifier.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetCommanderAsync(
        [Description("Exact Playgroup commander identifier.")] int commanderId,
        CancellationToken cancellationToken = default)
    {
        return service.GetCommanderAsync(commanderId, cancellationToken);
    }

    /// <summary>Gets one commander through the provider's name lookup.</summary>
    [McpServerTool(Name = "playgroup_commander_get_by_name", Title = "Get Playgroup Commander By Name", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Uses Playgroup's documented commander-name lookup and returns the provider result without local inference.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetCommanderByNameAsync(
        [Description("Exact commander name sent to Playgroup's documented lookup.")] string name,
        CancellationToken cancellationToken = default)
    {
        return service.GetCommanderByNameAsync(name, cancellationToken);
    }

    /// <summary>Gets one provider-computed commander turn-damage observation.</summary>
    [McpServerTool(Name = "playgroup_commander_turn_damage_get", Title = "Get Commander Turn Damage", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets Playgroup's turn-damage observation for one exact commander ID as provider evidence, not a quality ranking.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetCommanderTurnDamageAsync(
        [Description("Exact Playgroup commander identifier.")] int commanderId,
        CancellationToken cancellationToken = default)
    {
        return service.GetCommanderTurnDamageAsync(commanderId, cancellationToken);
    }

    /// <summary>Gets one provider deck.</summary>
    [McpServerTool(Name = "playgroup_deck_get", Title = "Get Playgroup Deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets one Playgroup deck by provider identifier with an explicit archived-deck option.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetDeckAsync(
        [Description("Exact Playgroup deck identifier.")] int deckId,
        [Description("Whether an archived deck may be returned.")] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        return service.GetDeckAsync(deckId, includeArchived, cancellationToken);
    }

    /// <summary>Gets provider-computed deck ELO history.</summary>
    [McpServerTool(Name = "playgroup_deck_elo_history_get", Title = "Get Playgroup Deck ELO History", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets provider-computed ELO history with explicit optional playgroup and league scope; it does not rank deck quality locally.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetDeckEloHistoryAsync(
        [Description("Exact Playgroup deck identifier.")] int deckId,
        [Description("Optional exact playgroup scope for the provider-computed history.")] int? playgroupId = null,
        [Description("Optional exact league scope for the provider-computed history.")] int? leagueId = null,
        [Description("Whether an archived deck may be used.")] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        return service.GetDeckEloHistoryAsync(
            deckId,
            playgroupId,
            leagueId,
            includeArchived,
            cancellationToken);
    }

    /// <summary>Gets one provider user.</summary>
    [McpServerTool(Name = "playgroup_user_get", Title = "Get Playgroup User", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets one provider-shaped user observation by exact Playgroup identifier.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetUserAsync(
        [Description("Exact Playgroup user identifier visible to the authenticated key.")] int userId,
        CancellationToken cancellationToken = default)
    {
        return service.GetUserAsync(userId, cancellationToken);
    }

    /// <summary>Lists one user's provider decks.</summary>
    [McpServerTool(Name = "playgroup_user_decks_list", Title = "List Playgroup User Decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists the provider-shaped decks for one exact user with an explicit archived-deck option.")]
    internal Task<OperationResult<PlaygroupEvidence>> ListUserDecksAsync(
        [Description("Exact Playgroup user identifier visible to the authenticated key.")] int userId,
        [Description("Whether archived decks should be included.")] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        return service.ListUserDecksAsync(userId, includeArchived, cancellationToken);
    }

    /// <summary>Lists playgroups visible for one authenticated user.</summary>
    [McpServerTool(Name = "playgroup_user_playgroups_list", Title = "List User Playgroups", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists playgroups visible to the authenticated API key for one exact user.")]
    internal Task<OperationResult<PlaygroupEvidence>> ListUserPlaygroupsAsync(
        [Description("Exact Playgroup user identifier visible to the authenticated key.")] int userId,
        CancellationToken cancellationToken = default)
    {
        return service.ListUserPlaygroupsAsync(userId, cancellationToken);
    }

    /// <summary>Gets one user's playgroup relationship.</summary>
    [McpServerTool(Name = "playgroup_user_playgroup_get", Title = "Get User Playgroup", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets one authenticated user/playgroup relationship by exact provider identifiers.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetUserPlaygroupAsync(
        [Description("Exact Playgroup user identifier visible to the authenticated key.")] int userId,
        [Description("Exact playgroup identifier visible to the authenticated key.")] int playgroupId,
        CancellationToken cancellationToken = default)
    {
        return service.GetUserPlaygroupAsync(userId, playgroupId, cancellationToken);
    }

    /// <summary>Lists members of one authenticated playgroup.</summary>
    [McpServerTool(Name = "playgroup_playgroup_members_list", Title = "List Playgroup Members", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists provider-shaped member records for one authenticated playgroup.")]
    internal Task<OperationResult<PlaygroupEvidence>> ListPlaygroupMembersAsync(
        [Description("Exact playgroup identifier visible to the authenticated key.")] int playgroupId,
        CancellationToken cancellationToken = default)
    {
        return service.ListPlaygroupMembersAsync(playgroupId, cancellationToken);
    }

    /// <summary>Lists one bounded page of playgroup games.</summary>
    [McpServerTool(Name = "playgroup_playgroup_games_list", Title = "List Playgroup Games", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists one provider-bounded game page; page, limit, and event inclusion remain caller-controlled.")]
    internal Task<OperationResult<PlaygroupEvidence>> ListPlaygroupGamesAsync(
        [Description("Exact playgroup identifier visible to the authenticated key.")] int playgroupId,
        [Description("One-based provider page number.")] int page = 1,
        [Description("Provider page size from 1 through 100.")] int limit = 10,
        [Description("Whether each returned game should include provider event records.")] bool includeEvents = false,
        CancellationToken cancellationToken = default)
    {
        return service.ListPlaygroupGamesAsync(playgroupId, page, limit, includeEvents, cancellationToken);
    }

    /// <summary>Gets one game with optional event records.</summary>
    [McpServerTool(Name = "playgroup_playgroup_game_get", Title = "Get Playgroup Game", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Gets one exact playgroup game and optionally its provider event history without additional fan-out.")]
    internal Task<OperationResult<PlaygroupEvidence>> GetPlaygroupGameAsync(
        [Description("Exact playgroup identifier visible to the authenticated key.")] int playgroupId,
        [Description("Exact game identifier within the playgroup.")] int gameId,
        [Description("Whether the returned game should include provider event records.")] bool includeEvents = false,
        CancellationToken cancellationToken = default)
    {
        return service.GetPlaygroupGameAsync(playgroupId, gameId, includeEvents, cancellationToken);
    }
}
