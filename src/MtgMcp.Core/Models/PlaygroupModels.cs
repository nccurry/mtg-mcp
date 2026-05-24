namespace MtgMcp.Core;

/// <summary>
/// Describes available Playgroup.gg authentication material without exposing secret values.
/// </summary>
public sealed class PlaygroupAuthStatus
{
    /// <summary>
    /// Gets or sets the Playgroup API base address used by the gateway.
    /// </summary>
    public string BaseAddress { get; set; } = "";

    /// <summary>
    /// Gets or sets whether an API key is available from configuration, environment, or a credentials file.
    /// </summary>
    public bool HasApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether the configured credentials file exists.
    /// </summary>
    public bool HasCredentialsFile { get; set; }

    /// <summary>
    /// Gets or sets a sanitized credentials-file parse error.
    /// </summary>
    public string? CredentialsFileError { get; set; }

    /// <summary>
    /// Gets whether credential-file parsing failed.
    /// </summary>
    public bool HasCredentialsFileError => !string.IsNullOrWhiteSpace(CredentialsFileError);

    /// <summary>
    /// Gets the effective Playgroup authentication mode.
    /// </summary>
    public string Mode =>
        HasCredentialsFileError ? "credentials-file-error"
        : HasApiKey ? "api-key"
        : "anonymous";
}

/// <summary>
/// Represents a Playgroup.gg user identity.
/// </summary>
public sealed class PlaygroupUser
{
    /// <summary>
    /// Gets or sets the Playgroup user id.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the display username.
    /// </summary>
    public string Username { get; set; } = "";
}

/// <summary>
/// Summarizes one Playgroup.gg playgroup visible to a user.
/// </summary>
public sealed class PlaygroupSummary
{
    /// <summary>
    /// Gets or sets the Playgroup id.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the Playgroup name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of finished games reported by Playgroup.
    /// </summary>
    public int? GameCount { get; set; }

    /// <summary>
    /// Gets or sets the active member count reported by Playgroup.
    /// </summary>
    public int? MemberCount { get; set; }

    /// <summary>
    /// Gets or sets when the Playgroup was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets league summaries associated with the Playgroup.
    /// </summary>
    public IReadOnlyList<PlaygroupLeagueSummary> Leagues { get; set; } = [];
}

/// <summary>
/// Describes a league within a Playgroup.gg playgroup.
/// </summary>
public sealed class PlaygroupLeagueSummary
{
    /// <summary>
    /// Gets or sets the league id.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the league name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the league is active.
    /// </summary>
    public bool? Active { get; set; }
}

/// <summary>
/// Represents a completed Playgroup.gg game returned by the public API.
/// </summary>
public sealed class PlaygroupGame
{
    /// <summary>
    /// Gets or sets the game id.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the playgroup id associated with the game.
    /// </summary>
    public long? PlaygroupId { get; set; }

    /// <summary>
    /// Gets or sets the total round count.
    /// </summary>
    public int? TotalRounds { get; set; }

    /// <summary>
    /// Gets or sets when the game started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when the game ended.
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the Playgroup win condition slug or label.
    /// </summary>
    public string? WinCondition { get; set; }

    /// <summary>
    /// Gets or sets player participations recorded for the game.
    /// </summary>
    public IReadOnlyList<PlaygroupParticipation> Participations { get; set; } = [];
}

/// <summary>
/// Represents one player's deck participation in a Playgroup.gg game.
/// </summary>
public sealed class PlaygroupParticipation
{
    /// <summary>
    /// Gets or sets the participation id.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets whether this participation won the game.
    /// </summary>
    public bool Winner { get; set; }

    /// <summary>
    /// Gets or sets the deck id used in the game.
    /// </summary>
    public long? DeckId { get; set; }

    /// <summary>
    /// Gets or sets the user id for the player.
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Gets or sets the deck name captured on the game participation.
    /// </summary>
    public string? DeckName { get; set; }

    /// <summary>
    /// Gets or sets the username captured on the game participation.
    /// </summary>
    public string? UserName { get; set; }
}

/// <summary>
/// Represents commander data embedded in Playgroup deck responses.
/// </summary>
public sealed class PlaygroupCommander
{
    /// <summary>
    /// Gets or sets the Playgroup commander id.
    /// </summary>
    public long? Id { get; set; }

    /// <summary>
    /// Gets or sets the commander name.
    /// </summary>
    public string Name { get; set; } = "";
}

/// <summary>
/// Represents Playgroup.gg deck details and source-provided deck statistics.
/// </summary>
public sealed class PlaygroupDeck
{
    /// <summary>
    /// Gets or sets the Playgroup deck id.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the owning Playgroup user id.
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Gets or sets the external decklist URL when one is configured.
    /// </summary>
    public string? DecklistUrl { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's deck page URL.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the source-reported win rate percentage.
    /// </summary>
    public double? WinRatePercentage { get; set; }

    /// <summary>
    /// Gets or sets source-reported wins.
    /// </summary>
    public int? GamesWon { get; set; }

    /// <summary>
    /// Gets or sets source-reported losses.
    /// </summary>
    public int? GamesLost { get; set; }

    /// <summary>
    /// Gets or sets the average mulligan count.
    /// </summary>
    public double? AverageMulligans { get; set; }

    /// <summary>
    /// Gets or sets the most common win condition.
    /// </summary>
    public string? MostPopularWincon { get; set; }

    /// <summary>
    /// Gets or sets the average round on which the deck wins.
    /// </summary>
    public double? AverageWinsByRound { get; set; }

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    public string? CoverImage { get; set; }

    /// <summary>
    /// Gets or sets the deck color identity.
    /// </summary>
    public IReadOnlyList<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets when the deck last played a recorded game.
    /// </summary>
    public DateTimeOffset? LastGamePlayedAt { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's estimated deck power, normalized from Elo to 0-10.
    /// </summary>
    public double? PowerLevel { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's confidence factor for the estimated deck power.
    /// </summary>
    public double? ConfidenceFactor { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's competitive rating.
    /// </summary>
    public double? CompetitivenessRating { get; set; }

    /// <summary>
    /// Gets or sets the primary commander.
    /// </summary>
    public PlaygroupCommander? Commander { get; set; }

    /// <summary>
    /// Gets or sets the partner commander when present.
    /// </summary>
    public PlaygroupCommander? Partner { get; set; }
}

/// <summary>
/// Represents a Playgroup deck's Elo history in a requested scope.
/// </summary>
public sealed class PlaygroupEloHistory
{
    /// <summary>
    /// Gets or sets the deck id.
    /// </summary>
    public long DeckId { get; set; }

    /// <summary>
    /// Gets or sets the current Elo rating in the requested scope.
    /// </summary>
    public int? CurrentRating { get; set; }

    /// <summary>
    /// Gets or sets the Elo scope, such as global, playgroup, or league.
    /// </summary>
    public string Scope { get; set; } = "";

    /// <summary>
    /// Gets or sets the playgroup id when scoped to a playgroup.
    /// </summary>
    public long? PlaygroupId { get; set; }

    /// <summary>
    /// Gets or sets the league id when scoped to a league.
    /// </summary>
    public long? LeagueId { get; set; }

    /// <summary>
    /// Gets or sets chronological Elo snapshots.
    /// </summary>
    public IReadOnlyList<PlaygroupEloHistoryEntry> History { get; set; } = [];
}

/// <summary>
/// Represents one Elo snapshot from Playgroup.gg.
/// </summary>
public sealed class PlaygroupEloHistoryEntry
{
    /// <summary>
    /// Gets or sets the Elo rating after the game.
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>
    /// Gets or sets the Elo delta for the game.
    /// </summary>
    public int? Delta { get; set; }

    /// <summary>
    /// Gets or sets the game id associated with the snapshot.
    /// </summary>
    public long? GameId { get; set; }

    /// <summary>
    /// Gets or sets when the game was played.
    /// </summary>
    public DateTimeOffset? PlayedAt { get; set; }
}

/// <summary>
/// Summarizes a Playgroup deck for list and ranking responses.
/// </summary>
public sealed class PlaygroupDeckSummary
{
    /// <summary>
    /// Gets or sets the Playgroup deck id.
    /// </summary>
    public long DeckId { get; set; }

    /// <summary>
    /// Gets or sets the deck name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the owning user id.
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Gets or sets the owner name observed from playgroup game participation.
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>
    /// Gets or sets commander names.
    /// </summary>
    public IReadOnlyList<string> CommanderNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the deck color identity.
    /// </summary>
    public IReadOnlyList<string> ColorIdentity { get; set; } = [];

    /// <summary>
    /// Gets or sets the external decklist URL.
    /// </summary>
    public string? DecklistUrl { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's deck page URL.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets source-reported total games.
    /// </summary>
    public int? Games { get; set; }

    /// <summary>
    /// Gets or sets source-reported wins.
    /// </summary>
    public int? Wins { get; set; }

    /// <summary>
    /// Gets or sets source-reported losses.
    /// </summary>
    public int? Losses { get; set; }

    /// <summary>
    /// Gets or sets source-reported win rate percentage.
    /// </summary>
    public double? WinRatePercentage { get; set; }

    /// <summary>
    /// Gets or sets how many fetched playgroup games included this deck.
    /// </summary>
    public int FetchedPlaygroupGames { get; set; }

    /// <summary>
    /// Gets or sets how many fetched playgroup games this deck won.
    /// </summary>
    public int FetchedPlaygroupWins { get; set; }

    /// <summary>
    /// Gets the win rate from fetched playgroup games.
    /// </summary>
    public double? FetchedPlaygroupWinRatePercentage =>
        FetchedPlaygroupGames > 0 ? 100d * FetchedPlaygroupWins / FetchedPlaygroupGames : null;

    /// <summary>
    /// Gets or sets the scoped Elo rating.
    /// </summary>
    public int? Elo { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's estimated deck power.
    /// </summary>
    public double? EstimatedPower { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's confidence factor for estimated power.
    /// </summary>
    public double? ConfidenceFactor { get; set; }

    /// <summary>
    /// Gets or sets Playgroup's competitive rating.
    /// </summary>
    public double? CompetitivenessRating { get; set; }

    /// <summary>
    /// Gets or sets the average winning round.
    /// </summary>
    public double? AverageWinsByRound { get; set; }

    /// <summary>
    /// Gets or sets the most recent fetched or source-reported game timestamp.
    /// </summary>
    public DateTimeOffset? LastPlayedAt { get; set; }

    /// <summary>
    /// Gets or sets warnings specific to this deck summary.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

/// <summary>
/// Contains decks derived from fetched Playgroup games.
/// </summary>
public sealed class PlaygroupDeckListResult
{
    /// <summary>
    /// Gets or sets the requested playgroup id.
    /// </summary>
    public long PlaygroupId { get; set; }

    /// <summary>
    /// Gets or sets the number of games fetched from the Playgroup API.
    /// </summary>
    public int FetchedGames { get; set; }

    /// <summary>
    /// Gets or sets the returned deck summaries.
    /// </summary>
    public IReadOnlyList<PlaygroupDeckSummary> Decks { get; set; } = [];

    /// <summary>
    /// Gets or sets warnings that apply to the deck list.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

/// <summary>
/// Represents a ranked Playgroup deck with the score used for sorting.
/// </summary>
public sealed class PlaygroupDeckRanking
{
    /// <summary>
    /// Gets or sets the one-based rank.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Gets or sets the score for the selected ranking metric.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Gets or sets the ranked deck summary.
    /// </summary>
    public PlaygroupDeckSummary Deck { get; set; } = new();
}

/// <summary>
/// Contains ranked Playgroup decks for a requested metric.
/// </summary>
public sealed class PlaygroupDeckRankingResult
{
    /// <summary>
    /// Gets or sets the requested playgroup id.
    /// </summary>
    public long PlaygroupId { get; set; }

    /// <summary>
    /// Gets or sets the metric used to rank decks.
    /// </summary>
    public string Metric { get; set; } = PlaygroupDeckRankingMetrics.EstimatedPower;

    /// <summary>
    /// Gets or sets ranked decks.
    /// </summary>
    public IReadOnlyList<PlaygroupDeckRanking> Rankings { get; set; } = [];

    /// <summary>
    /// Gets or sets warnings that apply to the ranking.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

/// <summary>
/// Summarizes a user observed in fetched Playgroup games.
/// </summary>
public sealed class PlaygroupUserSummary
{
    /// <summary>
    /// Gets or sets the Playgroup user id.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Gets or sets the observed username.
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// Gets or sets how many fetched games included this user.
    /// </summary>
    public int FetchedPlaygroupGames { get; set; }

    /// <summary>
    /// Gets or sets how many distinct decks this user played in fetched games.
    /// </summary>
    public int DecksSeen { get; set; }

    /// <summary>
    /// Gets or sets the latest fetched game timestamp for this user.
    /// </summary>
    public DateTimeOffset? LastPlayedAt { get; set; }
}

/// <summary>
/// Contains users derived from fetched Playgroup games.
/// </summary>
public sealed class PlaygroupUserListResult
{
    /// <summary>
    /// Gets or sets the requested playgroup id.
    /// </summary>
    public long PlaygroupId { get; set; }

    /// <summary>
    /// Gets or sets the number of games fetched from the Playgroup API.
    /// </summary>
    public int FetchedGames { get; set; }

    /// <summary>
    /// Gets or sets observed users.
    /// </summary>
    public IReadOnlyList<PlaygroupUserSummary> Users { get; set; } = [];

    /// <summary>
    /// Gets or sets warnings that apply to the user list.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

/// <summary>
/// Contains accessible decks for one Playgroup user.
/// </summary>
public sealed class PlaygroupUserDeckListResult
{
    /// <summary>
    /// Gets or sets the requested playgroup id used for user-name resolution.
    /// </summary>
    public long PlaygroupId { get; set; }

    /// <summary>
    /// Gets or sets the resolved Playgroup user id.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Gets or sets the resolved or observed username.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the source filter applied to decklist URLs.
    /// </summary>
    public string Source { get; set; } = PlaygroupUserDeckSources.Any;

    /// <summary>
    /// Gets or sets user deck summaries.
    /// </summary>
    public IReadOnlyList<PlaygroupDeckSummary> Decks { get; set; } = [];

    /// <summary>
    /// Gets or sets warnings that apply to the user deck list.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

/// <summary>
/// Lists supported Playgroup user deck source filters.
/// </summary>
public static class PlaygroupUserDeckSources
{
    /// <summary>
    /// Includes every accessible deck regardless of decklist URL host.
    /// </summary>
    public const string Any = "any";

    /// <summary>
    /// Includes only decks whose decklist URL points at Archidekt.
    /// </summary>
    public const string Archidekt = "archidekt";

    /// <summary>
    /// Contains every supported user deck source filter.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Any, Archidekt];
}

/// <summary>
/// Lists supported Playgroup deck ranking metrics.
/// </summary>
public static class PlaygroupDeckRankingMetrics
{
    /// <summary>
    /// Ranks by Playgroup's estimated power value, falling back to scoped Elo.
    /// </summary>
    public const string EstimatedPower = "estimated_power";

    /// <summary>
    /// Ranks by scoped Elo rating.
    /// </summary>
    public const string Elo = "elo";

    /// <summary>
    /// Ranks by fetched playgroup win rate, falling back to source win rate.
    /// </summary>
    public const string WinRate = "win_rate";

    /// <summary>
    /// Ranks by Playgroup's competitive rating.
    /// </summary>
    public const string CompetitiveRating = "competitive_rating";

    /// <summary>
    /// Ranks by fetched playgroup games.
    /// </summary>
    public const string GamesPlayed = "games_played";

    /// <summary>
    /// Ranks by average winning turn, where lower values rank higher.
    /// </summary>
    public const string AverageWinTurn = "average_win_turn";

    /// <summary>
    /// Contains every supported ranking metric.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        EstimatedPower,
        Elo,
        WinRate,
        CompetitiveRating,
        GamesPlayed,
        AverageWinTurn,
    ];
}
