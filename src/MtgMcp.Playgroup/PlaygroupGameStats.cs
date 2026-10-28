using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace MtgMcp.Playgroup;

/// <summary>
/// Carries one player's part in a game, with only the fields the statistics read.
/// </summary>
public sealed record PlaygroupParticipation(
    bool Winner,
    int? DeckId,
    string? DeckName,
    int? UserId,
    string? UserName,
    int? MulligansTaken);

/// <summary>
/// Carries one completed game, with only the fields the statistics read: its rounds, end time, and participants.
/// </summary>
public sealed record PlaygroupGame(
    int Id,
    int? TotalRounds,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<PlaygroupParticipation> Participations);

/// <summary>
/// Carries the games read from a playgroup and whether the read stopped before the end of the list.
/// </summary>
/// <param name="Games">The games read, at most the limit asked for.</param>
/// <param name="LimitReached">Whether the read stopped at the limit, so more games may exist.</param>
/// <param name="PagesRead">The number of provider pages read.</param>
/// <param name="Evidence">The evidence of the last page, for source and freshness facts.</param>
public sealed record PlaygroupGameRead(
    IReadOnlyList<PlaygroupGame> Games,
    bool LimitReached,
    int PagesRead,
    PlaygroupEvidence Evidence);

/// <summary>
/// Names the field that games are grouped by.
/// </summary>
public enum PlaygroupGameGroupBy
{
    /// <summary>
    /// One row for each deck.
    /// </summary>
    Deck,

    /// <summary>
    /// One row for each player.
    /// </summary>
    Player,
}

/// <summary>
/// Lists a game that was not counted and why.
/// </summary>
public sealed record PlaygroupSkippedGame(int GameId, string Reason);

/// <summary>
/// Totals one deck or one player: games, wins, win rate, the pod-size baseline, and the average number of rounds.
/// </summary>
/// <param name="Key">The deck ID or the user ID.</param>
/// <param name="Name">The most recent deck or user name seen for the key.</param>
/// <param name="Games">The number of games counted.</param>
/// <param name="Wins">The number of those games that were won.</param>
/// <param name="WinRate">Wins divided by games, as a decimal string rounded to six places.</param>
/// <param name="BaselineRate">The mean of 1 divided by the number of players, rounded to six places.</param>
/// <param name="AverageTotalRounds">
/// The mean of total_rounds over the games that report it, rounded to six places, or null when none does.
/// </param>
public sealed record PlaygroupGameStatsRow(
    int Key,
    string? Name,
    int Games,
    int Wins,
    string WinRate,
    string BaselineRate,
    string? AverageTotalRounds);

/// <summary>
/// Carries the totals of a set of games with the counts that say what they cover.
/// </summary>
public sealed record PlaygroupGameStatsResult(
    IReadOnlyList<PlaygroupGameStatsRow> Rows,
    int GamesRead,
    int GamesCounted,
    int GamesSkipped,
    IReadOnlyList<PlaygroupSkippedGame> Skipped,
    int GamesOutsideRange);

/// <summary>
/// Reads typed games from the provider's game list. It keeps only the fields the statistics need and skips values it
/// cannot read, so a field the provider adds or drops later cannot break the tool.
/// </summary>
public static class PlaygroupGameParser
{
    /// <summary>
    /// Reads the games of one list response. A response that is not an array has no games.
    /// </summary>
    public static IReadOnlyList<PlaygroupGame> Parse(JsonElement list)
    {
        if (list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<PlaygroupGame> games = [];
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && Integer(item, "id") is int id)
            {
                games.Add(new PlaygroupGame(
                    id,
                    Integer(item, "total_rounds"),
                    Time(item, "started_at"),
                    Time(item, "ended_at"),
                    ParseParticipations(item)));
            }
        }

        return games;
    }

    /// <summary>
    /// Reads the participations of one game.
    /// </summary>
    private static List<PlaygroupParticipation> ParseParticipations(JsonElement game)
    {
        List<PlaygroupParticipation> participations = [];
        if (!game.TryGetProperty("participations", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
        {
            return participations;
        }

        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            participations.Add(new PlaygroupParticipation(
                value.TryGetProperty("winner", out JsonElement winner) && winner.ValueKind == JsonValueKind.True,
                Integer(value, "deck_id"),
                Text(value, "deck_name"),
                Integer(value, "user_id"),
                Text(value, "user_name"),
                Integer(value, "mulligans_taken")));
        }

        return participations;
    }

    /// <summary>
    /// Reads a whole-number property, or null when it is missing or not a whole number.
    /// </summary>
    private static int? Integer(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number)
                ? number
                : null;
    }

    /// <summary>
    /// Reads a text property, or null when it is missing or not text.
    /// </summary>
    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Reads a date-time property, or null when it is missing or not an ISO date-time.
    /// </summary>
    private static DateTimeOffset? Time(JsonElement element, string name)
    {
        return DateTimeOffset.TryParse(
            Text(element, name),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset value)
                ? value
                : null;
    }
}

/// <summary>
/// Totals typed games by deck or by player. It does no input or output and makes no ranking or judgment.
/// </summary>
public static class PlaygroupGameStats
{
    /// <summary>
    /// Gives the number of decimal places a rate is rounded to.
    /// </summary>
    public const int Decimals = 6;

    /// <summary>
    /// Totals games. A game is counted for each of its participants. Games ended outside the date range are counted
    /// apart; games with no winner, or with a participant that has no deck (or no player), are skipped with a reason.
    /// </summary>
    public static PlaygroupGameStatsResult Aggregate(
        IReadOnlyList<PlaygroupGame> games,
        PlaygroupGameGroupBy groupBy,
        DateOnly? from,
        DateOnly? to)
    {
        ArgumentNullException.ThrowIfNull(games);
        Dictionary<int, Totals> totals = [];
        List<PlaygroupSkippedGame> skipped = [];
        int outside = 0;
        int counted = 0;
        foreach (PlaygroupGame game in games.OrderBy(game => game.Id))
        {
            if (!InRange(game, from, to))
            {
                outside++;
                continue;
            }

            string? reason = SkipReason(game, groupBy);
            if (reason is not null)
            {
                skipped.Add(new PlaygroupSkippedGame(game.Id, reason));
                continue;
            }

            counted++;
            // A deck or player that holds two seats of one game counts once for that game, as a win if any seat won.
            foreach (IGrouping<int, PlaygroupParticipation> seats in game.Participations.GroupBy(seat =>
                (groupBy == PlaygroupGameGroupBy.Deck ? seat.DeckId : seat.UserId)!.Value))
            {
                int key = seats.Key;
                PlaygroupParticipation participation = seats.Last();
                if (!totals.TryGetValue(key, out Totals? row))
                {
                    row = new Totals();
                    totals[key] = row;
                }

                row.Add(
                    groupBy == PlaygroupGameGroupBy.Deck ? participation.DeckName : participation.UserName,
                    seats.Any(seat => seat.Winner),
                    game.Participations.Count,
                    game.TotalRounds);
            }
        }

        List<PlaygroupGameStatsRow> rows =
            [.. totals.OrderBy(pair => pair.Key).Select(pair => pair.Value.ToRow(pair.Key))];
        return new PlaygroupGameStatsResult(rows, games.Count, counted, skipped.Count, skipped, outside);
    }

    /// <summary>
    /// Reports whether a game ended inside the date range; a game with no end time is outside any range.
    /// </summary>
    private static bool InRange(PlaygroupGame game, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null)
        {
            return true;
        }

        if (game.EndedAt is not DateTimeOffset ended)
        {
            return false;
        }

        DateOnly day = DateOnly.FromDateTime(ended.UtcDateTime);
        return (from is null || day >= from) && (to is null || day <= to);
    }

    /// <summary>
    /// Says why a game cannot be counted, or null when it can.
    /// </summary>
    private static string? SkipReason(PlaygroupGame game, PlaygroupGameGroupBy groupBy)
    {
        if (!game.Participations.Any(participation => participation.Winner))
        {
            return "no-winner";
        }

        if (groupBy == PlaygroupGameGroupBy.Deck && game.Participations.Any(value => value.DeckId is null))
        {
            return "missing-deck";
        }

        return groupBy == PlaygroupGameGroupBy.Player && game.Participations.Any(value => value.UserId is null)
            ? "missing-player"
            : null;
    }

    /// <summary>
    /// Formats a fraction as a decimal string rounded half to even at the shared number of places.
    /// </summary>
    internal static string Format(BigInteger numerator, BigInteger denominator)
    {
        BigInteger scale = BigInteger.Pow(10, Decimals);
        BigInteger scaled = numerator * scale;
        BigInteger quotient = BigInteger.DivRem(scaled, denominator, out BigInteger remainder);
        BigInteger twice = remainder * 2;
        if (twice > denominator || (twice == denominator && !quotient.IsEven))
        {
            quotient += 1;
        }

        BigInteger whole = BigInteger.DivRem(quotient, scale, out BigInteger fraction);
        string digits = fraction.ToString(CultureInfo.InvariantCulture).PadLeft(Decimals, '0');
        return $"{whole.ToString(CultureInfo.InvariantCulture)}.{digits}";
    }

    /// <summary>
    /// Collects the totals of one deck or player.
    /// </summary>
    private sealed class Totals
    {
        /// <summary>
        /// Holds the sum of 1 divided by the player count, as a fraction.
        /// </summary>
        private BigInteger baselineNumerator = BigInteger.Zero;

        /// <summary>
        /// Holds the denominator of the baseline fraction.
        /// </summary>
        private BigInteger baselineDenominator = BigInteger.One;

        /// <summary>
        /// Holds the total of the rounds of the games that report them.
        /// </summary>
        private long rounds;

        /// <summary>
        /// Holds the number of games that report their rounds.
        /// </summary>
        private int roundGames;

        /// <summary>
        /// Holds the most recent name seen.
        /// </summary>
        private string? name;

        /// <summary>
        /// Holds the number of games counted.
        /// </summary>
        private int games;

        /// <summary>
        /// Holds the number of wins.
        /// </summary>
        private int wins;

        /// <summary>
        /// Adds one game for this deck or player.
        /// </summary>
        internal void Add(string? seenName, bool won, int players, int? totalRounds)
        {
            games++;
            if (won)
            {
                wins++;
            }

            if (!string.IsNullOrWhiteSpace(seenName))
            {
                name = seenName;
            }

            BigInteger divisor = Math.Max(players, 1);
            baselineNumerator = (baselineNumerator * divisor) + baselineDenominator;
            baselineDenominator *= divisor;
            BigInteger gcd = BigInteger.GreatestCommonDivisor(baselineNumerator, baselineDenominator);
            baselineNumerator /= gcd;
            baselineDenominator /= gcd;
            if (totalRounds is int value)
            {
                rounds += value;
                roundGames++;
            }
        }

        /// <summary>
        /// Builds the output row.
        /// </summary>
        internal PlaygroupGameStatsRow ToRow(int key)
        {
            return new PlaygroupGameStatsRow(
                key,
                name,
                games,
                wins,
                Format(wins, games),
                Format(baselineNumerator, baselineDenominator * games),
                roundGames == 0 ? null : Format(rounds, roundGames));
        }
    }
}
