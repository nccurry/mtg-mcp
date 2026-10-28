using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.Core.Results;

namespace MtgMcp.Playgroup.Tests;

/// <summary>
/// Verifies the pure game totals and the paged game read (PG-001, PG-002, cases P01 to P04).
/// </summary>
public sealed class PlaygroupGameStatsTests
{
    /// <summary>
    /// Verifies totals by deck over twelve games: exact games, wins, win rate, and the pod-size baseline (case P01).
    /// </summary>
    [Fact]
    public void Aggregate_ByDeck_GivesExactGamesWinsAndBaseline()
    {
        PlaygroupGameStatsResult result = PlaygroupGameStats.Aggregate(TwelveGames(), PlaygroupGameGroupBy.Deck,
            null, null);

        Assert.Equal(12, result.GamesRead);
        Assert.Equal(12, result.GamesCounted);
        Assert.Equal([10, 20, 30, 40], result.Rows.Select(row => row.Key));
        PlaygroupGameStatsRow alpha = result.Rows[0];
        Assert.Equal(("Deck 10", 12, 3), (alpha.Name, alpha.Games, alpha.Wins));
        Assert.Equal("0.250000", alpha.WinRate);
        Assert.Equal("0.250000", alpha.BaselineRate);
        Assert.Equal("11.500000", alpha.AverageTotalRounds);
        Assert.All(result.Rows, row => Assert.Equal(12, row.Games));
        Assert.Equal(12, result.Rows.Sum(row => row.Wins));
    }

    /// <summary>
    /// Verifies totals by player, and that a deck name written over time keeps the most recent one (case P02).
    /// </summary>
    [Fact]
    public void Aggregate_ByPlayer_GivesTotalsPerPlayerSortedByKey()
    {
        List<PlaygroupGame> games = [.. TwelveGames()];
        games[11] = games[11] with
        {
            Participations = [.. games[11].Participations.Select(value => value with { UserName = value.UserName
                + " (renamed)" })],
        };

        PlaygroupGameStatsResult result = PlaygroupGameStats.Aggregate(games, PlaygroupGameGroupBy.Player, null, null);

        Assert.Equal([1, 2, 3, 4], result.Rows.Select(row => row.Key));
        Assert.Equal("User 1 (renamed)", result.Rows[0].Name);
        Assert.Equal(3, result.Rows[0].Wins);
    }

    /// <summary>
    /// Verifies a date range keeps only games that ended inside it, whatever order they arrive in, counts the others
    /// apart, and treats a game with no end time as outside (case P03).
    /// </summary>
    [Fact]
    public void Aggregate_DateRange_CountsGamesOutsideAndIgnoresInputOrder()
    {
        List<PlaygroupGame> games = [.. TwelveGames().AsEnumerable().Reverse(), TwelveGames()[0] with { Id = 99,
            EndedAt = null }];

        PlaygroupGameStatsResult result = PlaygroupGameStats.Aggregate(
            games,
            PlaygroupGameGroupBy.Deck,
            new DateOnly(2026, 6, 4),
            new DateOnly(2026, 6, 8));

        Assert.Equal(5, result.GamesCounted);
        Assert.Equal(8, result.GamesOutsideRange);
        Assert.Equal(13, result.GamesRead);
        Assert.Equal(5, result.Rows.Sum(row => row.Games) / 4);
        PlaygroupGameStatsResult onlyFrom = PlaygroupGameStats.Aggregate(games, PlaygroupGameGroupBy.Deck,
            new DateOnly(2026, 6, 12), null);
        PlaygroupGameStatsResult onlyTo = PlaygroupGameStats.Aggregate(games, PlaygroupGameGroupBy.Deck, null,
            new DateOnly(2026, 6, 1));
        Assert.Equal(1, onlyFrom.GamesCounted);
        Assert.Equal(1, onlyTo.GamesCounted);
    }

    /// <summary>
    /// Verifies games with no winner, a missing deck, or a missing player are skipped with reasons and are not hidden
    /// (case P04).
    /// </summary>
    [Fact]
    public void Aggregate_GamesWithoutAWinnerDeckOrPlayer_AreSkippedWithReasons()
    {
        List<PlaygroupGame> games = [.. TwelveGames()];
        games[0] = games[0] with
        {
            Participations = [.. games[0].Participations.Select(value => value with { Winner = false })],
        };
        games[1] = games[1] with
        {
            Participations = [games[1].Participations[0] with { DeckId = null }, .. games[1].Participations.Skip(1)],
        };
        games[2] = games[2] with
        {
            Participations = [games[2].Participations[0] with { UserId = null }, .. games[2].Participations.Skip(1)],
        };

        PlaygroupGameStatsResult byDeck = PlaygroupGameStats.Aggregate(games, PlaygroupGameGroupBy.Deck, null, null);
        PlaygroupGameStatsResult byPlayer = PlaygroupGameStats.Aggregate(games, PlaygroupGameGroupBy.Player, null,
            null);

        Assert.Equal([(1, "no-winner"), (2, "missing-deck")], byDeck.Skipped.Select(item => (item.GameId,
            item.Reason)));
        Assert.Equal(10, byDeck.GamesCounted);
        Assert.Equal([(1, "no-winner"), (3, "missing-player")], byPlayer.Skipped.Select(item => (item.GameId,
            item.Reason)));
        Assert.Equal(2, byDeck.GamesSkipped);
    }

    /// <summary>
    /// Verifies the baseline is the mean of 1 divided by the pod size when pods differ, and rates round half to even at
    /// six places.
    /// </summary>
    [Fact]
    public void Aggregate_MixedPodSizesAndRounding_AreExact()
    {
        PlaygroupGame three = Game(1, "2026-06-01", 3, winner: 1, players: 3);
        PlaygroupGame four = Game(2, "2026-06-02", 4, winner: 2, players: 4);

        PlaygroupGameStatsRow row = PlaygroupGameStats.Aggregate([three, four], PlaygroupGameGroupBy.Deck, null,
            null).Rows
            .Single(value => value.Key == 10);

        Assert.Equal("0.291667", row.BaselineRate);
        Assert.Equal("0.000000", row.WinRate);
        Assert.Equal("0.333333", PlaygroupGameStats.Format(1, 3));
        Assert.Equal("0.666667", PlaygroupGameStats.Format(2, 3));
        Assert.Equal("0.007812", PlaygroupGameStats.Format(1, 128));
        Assert.Equal("0.023438", PlaygroupGameStats.Format(3, 128));
        Assert.Equal("2.000000", PlaygroupGameStats.Format(4, 2));
    }

    /// <summary>
    /// Verifies games that report no total rounds leave the average null.
    /// </summary>
    [Fact]
    public void Aggregate_GamesWithoutTotalRounds_HaveANullAverage()
    {
        PlaygroupGame game = Game(1, "2026-06-01", 3, winner: 0, players: 4) with { TotalRounds = null };

        PlaygroupGameStatsRow row = Assert.Single(
            PlaygroupGameStats.Aggregate([game], PlaygroupGameGroupBy.Player, null, null).Rows,
                value => value.Key == 1);

        Assert.Null(row.AverageTotalRounds);
    }

    /// <summary>
    /// Verifies the game parser reads the documented fields and skips what it cannot read.
    /// </summary>
    [Fact]
    public void Parse_ReadsDocumentedFieldsAndSkipsUnreadableItems()
    {
        using JsonDocument document = JsonDocument.Parse("""
            [
              {"id": 5, "total_rounds": 9, "started_at": "2026-06-01T10:00:00Z", "ended_at": "bad",
               "participations": [
                 {"winner": true, "deck_id": 1, "deck_name": "D", "user_id": 2, "user_name": "U", "mulligans_taken": 1},
                 {"winner": "yes"}, 7 ]},
              {"id": "x"}, 3, {"no_id": true}
            ]
            """);

        PlaygroupGame game = Assert.Single(PlaygroupGameParser.Parse(document.RootElement));

        Assert.Equal((5, 9), (game.Id, game.TotalRounds));
        Assert.NotNull(game.StartedAt);
        Assert.Null(game.EndedAt);
        Assert.Equal(2, game.Participations.Count);
        Assert.True(game.Participations[0].Winner);
        Assert.False(game.Participations[1].Winner);
        Assert.Empty(PlaygroupGameParser.Parse(JsonElement.Parse("{}")));
        Assert.Empty(PlaygroupGameParser.Parse(JsonElement.Parse("[{\"id\":1}]")).Single().Participations);
    }

    /// <summary>
    /// Verifies the read follows pages until a short page, takes at most the limit, and says when it stopped early
    /// (case P04).
    /// </summary>
    [Fact]
    public async Task ReadGames_FollowsPagesAndStopsAtTheLimit()
    {
        PlaygroupTestHttpHandler pages = new();
        pages.AddJson(GamesJson(0, 100));
        pages.AddJson(GamesJson(100, 100));
        pages.AddJson(GamesJson(200, 50));
        using PlaygroupService service = PlaygroupTestFactory.CreateService(pages);
        PlaygroupTestHttpHandler capped = new();
        capped.AddJson(GamesJson(0, 12));
        using PlaygroupService cappedService = PlaygroupTestFactory.CreateService(capped);

        PlaygroupGameRead all = Success(await service.ReadGamesAsync(7, 1000, TestContext.Current.CancellationToken));
        PlaygroupGameRead stopped = Success(await cappedService.ReadGamesAsync(7, 8,
            TestContext.Current.CancellationToken));

        Assert.Equal((250, false, 3), (all.Games.Count, all.LimitReached, all.PagesRead));
        Assert.Equal(
            [
                "/api/public/v1/playgroups/7/games?page=1&limit=100&include_events=false",
                "/api/public/v1/playgroups/7/games?page=2&limit=100&include_events=false",
                "/api/public/v1/playgroups/7/games?page=3&limit=100&include_events=false",
            ],
            pages.Requests.Select(request => request.PathAndQuery));
        Assert.Equal((8, true, 1), (stopped.Games.Count, stopped.LimitReached, stopped.PagesRead));
        Assert.Equal("/api/public/v1/playgroups/7/games?page=1&limit=8&include_events=false",
            Assert.Single(capped.Requests).PathAndQuery);
        Assert.Equal("listPlaygroupGames", stopped.Evidence.OperationId);
    }

    /// <summary>
    /// Verifies a list holding exactly the limit says the limit was not reached: it reads one more page to look.
    /// </summary>
    [Fact]
    public async Task ReadGames_ExactlyTheLimit_DoesNotClaimThereIsMore()
    {
        PlaygroupTestHttpHandler pages = new();
        pages.AddJson(GamesJson(0, 100));
        pages.AddJson("[]");
        using PlaygroupService service = PlaygroupTestFactory.CreateService(pages);

        PlaygroupGameRead read = Success(await service.ReadGamesAsync(7, 100, TestContext.Current.CancellationToken));

        Assert.Equal((100, false, 2), (read.Games.Count, read.LimitReached, read.PagesRead));
    }

    /// <summary>
    /// Verifies a failure on a later page returns that failure and not the games read before it.
    /// </summary>
    [Fact]
    public async Task ReadGames_FailureOnTheSecondPage_ReturnsTheFailureNotPartialGames()
    {
        PlaygroupTestHttpHandler pages = new();
        pages.AddJson(GamesJson(0, 100));
        for (int attempt = 0; attempt < 6; attempt++)
        {
            pages.AddStatus(HttpStatusCode.ServiceUnavailable);
        }

        using PlaygroupService service = PlaygroupTestFactory.CreateService(pages);

        OperationResult<PlaygroupGameRead> result = await service.ReadGamesAsync(
            7,
            500,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationUnavailable>(result.Value);
        Assert.All(
            pages.Requests.Skip(1),
            request => Assert.Contains("page=2", request.PathAndQuery, StringComparison.Ordinal));
        Assert.Contains("page=1", pages.Requests[0].PathAndQuery, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies pages that keep returning the same games or unreadable games stop at the page limit, and a game seen
    /// twice is counted once.
    /// </summary>
    [Fact]
    public async Task ReadGames_RepeatedAndUnreadablePages_StopAtThePageLimitAndCountGamesOnce()
    {
        PlaygroupTestHttpHandler pages = new();
        for (int page = 0; page < 10; page++)
        {
            pages.AddJson(GamesJson(0, 100));
        }

        using PlaygroupService service = PlaygroupTestFactory.CreateService(pages);

        PlaygroupGameRead read = Success(await service.ReadGamesAsync(7, 100, TestContext.Current.CancellationToken));

        Assert.Equal(100, read.Games.Count);
        Assert.True(read.LimitReached);
        Assert.Equal(2, pages.Requests.Count);
    }

    /// <summary>
    /// Verifies the date range compares UTC days: a game ended at 23:30 UTC on June 1 is in June 1, and one ended at
    /// 00:30 UTC on June 2 is not.
    /// </summary>
    [Fact]
    public void Aggregate_DateRange_ComparesUtcDays()
    {
        PlaygroupGame late = Game(1, "2026-06-01", 8, winner: 0, players: 2) with
        {
            EndedAt = DateTimeOffset.Parse("2026-06-01T23:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
        };
        PlaygroupGame early = Game(2, "2026-06-02", 8, winner: 0, players: 2) with
        {
            EndedAt = DateTimeOffset.Parse("2026-06-02T00:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
        };

        PlaygroupGameStatsResult result = PlaygroupGameStats.Aggregate(
            [late, early],
            PlaygroupGameGroupBy.Deck,
            null,
            new DateOnly(2026, 6, 1));

        Assert.Equal((1, 1), (result.GamesCounted, result.GamesOutsideRange));
    }

    /// <summary>
    /// Verifies a deck that holds two seats of one game counts once for that game, as a win when either seat won.
    /// </summary>
    [Fact]
    public void Aggregate_TwoSeatsWithOneDeck_CountTheDeckOncePerGame()
    {
        PlaygroupGame game = new(
            1,
            8,
            null,
            null,
            [
                new PlaygroupParticipation(false, 10, "Mirror", 1, "A", 0),
                new PlaygroupParticipation(true, 10, "Mirror", 2, "B", 0),
                new PlaygroupParticipation(false, 20, "Other", 3, "C", 0),
            ]);

        PlaygroupGameStatsResult result = PlaygroupGameStats.Aggregate([game], PlaygroupGameGroupBy.Deck, null, null);

        PlaygroupGameStatsRow mirror = result.Rows.Single(row => row.Key == 10);
        Assert.Equal((1, 1), (mirror.Games, mirror.Wins));
        PlaygroupGameStatsRow other = result.Rows.Single(row => row.Key == 20);
        Assert.Equal((1, 0), (other.Games, other.Wins));
    }

    /// <summary>
    /// Verifies a bad limit fails before any request and a provider failure is returned as its own kind.
    /// </summary>
    [Fact]
    public async Task ReadGames_BadLimitAndProviderFailure_AreTypedResults()
    {
        PlaygroupTestHttpHandler handler = new();
        handler.AddStatus(HttpStatusCode.Unauthorized);
        using PlaygroupService service = PlaygroupTestFactory.CreateService(handler);

        OperationResult<PlaygroupGameRead> zero = await service.ReadGamesAsync(7, 0,
            TestContext.Current.CancellationToken);
        OperationResult<PlaygroupGameRead> tooMany = await service.ReadGamesAsync(7, 1001,
            TestContext.Current.CancellationToken);
        OperationResult<PlaygroupGameRead> denied = await service.ReadGamesAsync(7, 10,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationInvalidInput>(zero.Value);
        Assert.IsType<OperationInvalidInput>(tooMany.Value);
        Assert.Equal("provider-unauthorized", Assert.IsType<OperationUnavailable>(denied.Value).ReasonCode);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Builds twelve games: four decks of four players, each deck winning three games, ended on June 1 to 12, with
    /// rounds from 6 to 17.
    /// </summary>
    private static List<PlaygroupGame> TwelveGames()
    {
        return
        [
            .. Enumerable.Range(0, 12).Select(index =>
                Game(index + 1, $"2026-06-{index + 1:D2}", 6 + index, winner: index % 4, players: 4)),
        ];
    }

    /// <summary>
    /// Builds one game with the given number of players, where players are numbered from 0; deck and user IDs derive
    /// from the player number.
    /// </summary>
    private static PlaygroupGame Game(int id, string endedOn, int rounds, int winner, int players)
    {
        return new PlaygroupGame(
            id,
            rounds,
            DateTimeOffset.Parse($"{endedOn}T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse($"{endedOn}T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [
                .. Enumerable.Range(0, players).Select(seat => new PlaygroupParticipation(
                    seat == winner,
                    (seat + 1) * 10,
                    $"Deck {(seat + 1) * 10}",
                    seat + 1,
                    $"User {seat + 1}",
                    0)),
            ]);
    }

    /// <summary>
    /// Builds the JSON list of a page of games, starting at an offset.
    /// </summary>
    private static string GamesJson(int start, int count)
    {
        JsonArray games = [];
        for (int index = start; index < start + count; index++)
        {
            games.Add(new JsonObject
            {
                ["id"] = index + 1,
                ["total_rounds"] = 8,
                ["ended_at"] = "2026-06-01T10:00:00Z",
                ["participations"] = new JsonArray(
                    new JsonObject { ["winner"] = true, ["deck_id"] = 1, ["user_id"] = 1 },
                    new JsonObject { ["winner"] = false, ["deck_id"] = 2, ["user_id"] = 2 }),
            });
        }

        return games.ToJsonString();
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
