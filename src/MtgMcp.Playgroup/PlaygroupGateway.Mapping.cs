using System.Text.Json;
using MtgMcp.Core;

namespace MtgMcp.Playgroup;

/// <summary>
/// Sends Playgroup.gg public API requests and maps responses to Core models.
/// </summary>
public sealed partial class PlaygroupGateway
{
    /// <summary>
    /// Maps a Playgroup user response.
    /// </summary>
    private static PlaygroupUser MapUser(JsonElement element)
    {
        return new PlaygroupUser
        {
            Id = GetLong(element, "id") ?? 0,
            Username = GetString(element, "username") ?? "",
        };
    }

    /// <summary>
    /// Maps a Playgroup summary response.
    /// </summary>
    private static PlaygroupSummary MapPlaygroup(JsonElement element)
    {
        return new PlaygroupSummary
        {
            Id = GetLong(element, "id") ?? 0,
            Name = GetString(element, "name") ?? "",
            GameCount = GetInt(element, "game_count"),
            MemberCount = GetInt(element, "member_count"),
            CreatedAt = GetDate(element, "created_at"),
            Leagues = MapLeagues(element),
        };
    }

    /// <summary>
    /// Maps embedded league summaries.
    /// </summary>
    private static IReadOnlyList<PlaygroupLeagueSummary> MapLeagues(JsonElement element)
    {
        if (
            !element.TryGetProperty("leagues", out JsonElement leagues)
            || leagues.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        return leagues
            .EnumerateArray()
            .Select(item => new PlaygroupLeagueSummary
            {
                Id = GetLong(item, "id") ?? 0,
                Name = GetString(item, "name") ?? "",
                Active = GetBool(item, "active"),
            })
            .ToList();
    }

    /// <summary>
    /// Maps one Playgroup game response.
    /// </summary>
    private static PlaygroupGame MapGame(JsonElement element)
    {
        return new PlaygroupGame
        {
            Id = GetLong(element, "id") ?? 0,
            PlaygroupId = GetLong(element, "playgroup_id"),
            TotalRounds = GetInt(element, "total_rounds"),
            StartedAt = GetDate(element, "started_at"),
            EndedAt = GetDate(element, "ended_at"),
            WinCondition = GetString(element, "win_con"),
            Participations = MapParticipations(element),
        };
    }

    /// <summary>
    /// Maps embedded game participations.
    /// </summary>
    private static IReadOnlyList<PlaygroupParticipation> MapParticipations(JsonElement element)
    {
        if (
            !element.TryGetProperty("participations", out JsonElement participations)
            || participations.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        return participations.EnumerateArray().Select(MapParticipation).ToList();
    }

    /// <summary>
    /// Maps one game participation response.
    /// </summary>
    private static PlaygroupParticipation MapParticipation(JsonElement element)
    {
        return new PlaygroupParticipation
        {
            Id = GetLong(element, "id") ?? 0,
            Winner = GetBool(element, "winner") ?? false,
            DeckId = GetLong(element, "deck_id"),
            UserId = GetLong(element, "user_id"),
            DeckName = GetString(element, "deck_name"),
            UserName = GetString(element, "user_name"),
        };
    }

    /// <summary>
    /// Maps a Playgroup deck response.
    /// </summary>
    private static PlaygroupDeck MapDeck(JsonElement element)
    {
        return new PlaygroupDeck
        {
            Id = GetLong(element, "id") ?? 0,
            Name = GetString(element, "name") ?? "",
            UserId = GetLong(element, "user_id"),
            DecklistUrl = GetString(element, "decklist_url"),
            WinRatePercentage = GetDouble(element, "win_rate_percentage"),
            GamesWon = GetInt(element, "games_won"),
            GamesLost = GetInt(element, "games_lost"),
            AverageMulligans = GetDouble(element, "average_mulligans"),
            MostPopularWincon = GetString(element, "most_popular_wincon"),
            AverageWinsByRound = GetDouble(element, "average_wins_by_round"),
            CoverImage = GetString(element, "cover_image"),
            ColorIdentity = GetStringArray(element, "color_identity"),
            LastGamePlayedAt = GetDate(element, "last_game_played_at"),
            PowerLevel = GetDouble(element, "power_level"),
            ConfidenceFactor = GetDouble(element, "confidence_factor"),
            CompetitivenessRating = GetDouble(element, "competitiveness_rating"),
            Commander = MapCommander(element, "commander"),
            Partner = MapCommander(element, "partner"),
            Url = GetString(element, "url"),
        };
    }

    /// <summary>
    /// Maps an embedded commander object when present.
    /// </summary>
    private static PlaygroupCommander? MapCommander(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement commander)
            || commander.ValueKind != JsonValueKind.Object
        )
        {
            return null;
        }

        string? name = GetString(commander, "name");
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new PlaygroupCommander { Id = GetLong(commander, "id"), Name = name };
    }

    /// <summary>
    /// Maps a deck Elo history response.
    /// </summary>
    private static PlaygroupEloHistory MapEloHistory(JsonElement element)
    {
        return new PlaygroupEloHistory
        {
            DeckId = GetLong(element, "deck_id") ?? 0,
            CurrentRating = GetInt(element, "current_rating"),
            Scope = GetString(element, "scope") ?? "",
            PlaygroupId = GetLong(element, "playgroup_id"),
            LeagueId = GetLong(element, "league_id"),
            History = MapEloHistoryEntries(element),
        };
    }

    /// <summary>
    /// Maps chronological Elo history entries.
    /// </summary>
    private static IReadOnlyList<PlaygroupEloHistoryEntry> MapEloHistoryEntries(
        JsonElement element
    )
    {
        if (
            !element.TryGetProperty("history", out JsonElement history)
            || history.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        return history
            .EnumerateArray()
            .Select(item => new PlaygroupEloHistoryEntry
            {
                Rating = GetInt(item, "rating"),
                Delta = GetInt(item, "delta"),
                GameId = GetLong(item, "game_id"),
                PlayedAt = GetDate(item, "played_at"),
            })
            .ToList();
    }
}
