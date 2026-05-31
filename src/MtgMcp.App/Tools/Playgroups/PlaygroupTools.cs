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
    [McpServerTool(Name = "playgroup_get_auth_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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
    [McpServerTool(Name = "playgroup_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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
    [McpServerTool(Name = "playgroup_get_deck", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Get normalized Playgroup.gg deck details by deck id or URL, including source decklist_url when present.")]
    public Task<PlaygroupDeck> GetPlaygroupDeckAsync(
        [Description("Playgroup deck id or URL containing /decks/{id}.")]
        string deckIdOrUrl,
        CancellationToken cancellationToken = default
    )
    {
        return playgroups.GetDeckAsync(ParseDeckId(deckIdOrUrl), cancellationToken);
    }

    /// <summary>
    /// Lists decks seen in fetched games for a Playgroup.
    /// </summary>
    [McpServerTool(Name = "playgroup_list_observed_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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
    [McpServerTool(Name = "playgroup_list_observed_users", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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
    [McpServerTool(Name = "playgroup_list_user_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("List accessible decks for a Playgroup.gg user id or observed name. Use source='archidekt' to return only decks with Archidekt decklist URLs.")]
    public Task<PlaygroupUserDeckListResult> ListPlaygroupUserDecksAsync(
        string playgroupIdOrUrl,
        string userIdOrName,
        [Description("Deck source filter: any or archidekt.")]
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
    [McpServerTool(Name = "playgroup_rank_decks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Rank decks seen in fetched Playgroup.gg games. Metrics: estimated_power, elo, win_rate, competitive_rating, games_played, average_win_turn.")]
    public Task<PlaygroupDeckRankingResult> RankPlaygroupDecksAsync(
        string playgroupIdOrUrl,
        [Description("Ranking metric: estimated_power, elo, win_rate, competitive_rating, games_played, or average_win_turn.")]
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

    /// <summary>
    /// Reads a numeric Playgroup deck id from a raw id or URL-like value.
    /// </summary>
    private static long ParseDeckId(string deckIdOrUrl)
    {
        if (string.IsNullOrWhiteSpace(deckIdOrUrl))
        {
            throw new ArgumentException("Deck id or URL is required.", nameof(deckIdOrUrl));
        }

        string trimmed = deckIdOrUrl.Trim();
        if (long.TryParse(trimmed, out long deckId))
        {
            return deckId;
        }

        Uri? uri = Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsedUri)
            ? parsedUri
            : null;
        string path = uri?.AbsolutePath ?? trimmed;
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (!segments[index].Equals("decks", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (long.TryParse(segments[index + 1], out deckId))
            {
                return deckId;
            }
        }

        throw new ArgumentException(
            "Deck id or URL must contain a numeric Playgroup deck id.",
            nameof(deckIdOrUrl)
        );
    }
}
