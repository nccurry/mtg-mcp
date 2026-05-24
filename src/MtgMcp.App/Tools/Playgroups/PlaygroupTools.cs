using System.ComponentModel;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Exposes read-only MCP tools for Playgroup.gg data.
/// </summary>
[McpServerToolType]
public sealed class PlaygroupTools
{
    /// <summary>
    /// Aggregates Playgroup API data for tool responses.
    /// </summary>
    private readonly PlaygroupService playgroups;

    /// <summary>
    /// Creates Playgroup tools backed by the Core aggregation service.
    /// </summary>
    public PlaygroupTools(PlaygroupService playgroups)
    {
        this.playgroups = playgroups;
    }

    /// <summary>
    /// Gets redacted Playgroup authentication status.
    /// </summary>
    [McpServerTool(Name = "get_playgroup_auth_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get redacted Playgroup.gg API-key and credentials-file availability status.")]
    public Task<PlaygroupAuthStatus> GetPlaygroupAuthStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.GetAuthStatusAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a Playgroup summary visible to the configured or supplied user.
    /// </summary>
    [McpServerTool(Name = "get_playgroup", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Get a Playgroup.gg playgroup summary by id or URL. Omitting userId uses the configured API key to call /me.")]
    public Task<PlaygroupSummary> GetPlaygroupAsync(
        string playgroupIdOrUrl,
        long? userId = null,
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.GetPlaygroupAsync(playgroupIdOrUrl, userId, cancellationToken);
    }

    /// <summary>
    /// Gets one Playgroup deck by id.
    /// </summary>
    [McpServerTool(Name = "get_playgroup_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Get normalized Playgroup.gg deck details, including source decklist_url when present.")]
    public Task<PlaygroupDeck> GetPlaygroupDeckAsync(
        long deckId,
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.GetDeckAsync(deckId, cancellationToken);
    }

    /// <summary>
    /// Lists decks seen in fetched games for a Playgroup.
    /// </summary>
    [McpServerTool(Name = "list_playgroup_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("List decks seen in fetched Playgroup.gg games by id or URL. Results are derived from game participations because the public API has no direct playgroup deck-list endpoint.")]
    public Task<PlaygroupDeckListResult> ListPlaygroupDecksAsync(
        string playgroupIdOrUrl,
        int maxGames = 200,
        int limit = 100,
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.ListDecksAsync(playgroupIdOrUrl, maxGames, limit, cancellationToken);
    }

    /// <summary>
    /// Lists users seen in fetched games for a Playgroup.
    /// </summary>
    [McpServerTool(Name = "list_playgroup_users", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("List users seen in fetched Playgroup.gg games by playgroup id or URL. Results are derived from game participations.")]
    public Task<PlaygroupUserListResult> ListPlaygroupUsersAsync(
        string playgroupIdOrUrl,
        int maxGames = 200,
        int limit = 100,
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.ListUsersAsync(playgroupIdOrUrl, maxGames, limit, cancellationToken);
    }

    /// <summary>
    /// Lists accessible decks for a Playgroup user resolved by id or observed name.
    /// </summary>
    [McpServerTool(Name = "list_playgroup_user_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("List accessible decks for a Playgroup.gg user id or observed name. Use source='archidekt' to return only decks with Archidekt decklist URLs.")]
    public Task<PlaygroupUserDeckListResult> ListPlaygroupUserDecksAsync(
        string playgroupIdOrUrl,
        string userIdOrName,
        string source = PlaygroupUserDeckSources.Any,
        int maxGames = 200,
        int limit = 100,
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.ListUserDecksAsync(
            playgroupIdOrUrl,
            userIdOrName,
            source,
            maxGames,
            limit,
            cancellationToken
        );
    }

    /// <summary>
    /// Ranks decks seen in fetched games for a Playgroup.
    /// </summary>
    [McpServerTool(Name = "rank_playgroup_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Rank decks seen in fetched Playgroup.gg games. Metrics: estimated_power, elo, win_rate, competitive_rating, games_played, average_win_turn.")]
    public Task<PlaygroupDeckRankingResult> RankPlaygroupDecksAsync(
        string playgroupIdOrUrl,
        string metric = PlaygroupDeckRankingMetrics.EstimatedPower,
        int minGames = 0,
        bool includeLowConfidence = false,
        int maxGames = 200,
        int limit = 20,
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.RankDecksAsync(
            playgroupIdOrUrl,
            metric,
            minGames,
            includeLowConfidence,
            maxGames,
            limit,
            cancellationToken
        );
    }
}
