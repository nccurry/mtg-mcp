using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Hosting;
using MtgMcp.App.Playgroup;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies <c>playgroup_games_stats_get</c>: inputs, the date range, the read limit notes, and the size limit
/// (PG-001, PG-002, cases P01 to P05).
/// </summary>
public sealed class PlaygroupStatsToolTests
{
    /// <summary>
    /// Verifies totals by deck over twelve games, with exact rates, counts of what was read, and no ranking (case P01).
    /// </summary>
    [Fact]
    public async Task GetStats_ByDeck_ReturnsExactRowsSortedByKey()
    {
        using Setup setup = new(TwelveGames());

        PlaygroupGameStatsView view = Success(await setup.Tool.GetStatsAsync(
            567,
            "deck",
            100,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((12, 12, 0, 0, false), (view.GamesRead, view.GamesCounted, view.GamesSkipped,
            view.GamesOutsideRange, view.LimitReached));
        Assert.Equal([10, 20, 30, 40], view.Rows.Select(row => row.Key));
        Assert.All(view.Rows, row => Assert.Equal(("0.250000", "0.250000"), (row.WinRate, row.BaselineRate)));
        Assert.Equal(1, view.Evidence.PagesRead);
        Assert.Equal("listPlaygroupGames", view.Evidence.OperationId);
        string json = JsonSerializer.Serialize(view, ToolCallPolicy.SerializerOptions);
        Assert.DoesNotContain("rank", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("best", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies totals by player, and a commander group is refused listing deck and player (case P02).
    /// </summary>
    [Fact]
    public async Task GetStats_ByPlayerAndByCommander_GroupsOrRefuses()
    {
        using Setup setup = new(TwelveGames());

        PlaygroupGameStatsView players = Success(await setup.Tool.GetStatsAsync(
            567,
            "player",
            100,
            cancellationToken: TestContext.Current.CancellationToken));
        OperationInvalidInput commander = Assert.IsType<OperationInvalidInput>((await setup.Tool.GetStatsAsync(
            567,
            "commander",
            100,
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal([1, 2, 3, 4], players.Rows.Select(row => row.Key));
        Assert.Equal("invalid-group-by", commander.ReasonCode);
        Assert.Contains("deck or player", commander.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a date range counts the games outside it apart (case P03).
    /// </summary>
    [Fact]
    public async Task GetStats_DateRange_CountsGamesOutside()
    {
        using Setup setup = new(TwelveGames().AsEnumerable().Reverse().ToList());

        PlaygroupGameStatsView view = Success(await setup.Tool.GetStatsAsync(
            567,
            "deck",
            100,
            "2026-06-04",
            "2026-06-08",
            TestContext.Current.CancellationToken));

        Assert.Equal((12, 5, 7), (view.GamesRead, view.GamesCounted, view.GamesOutsideRange));
        Assert.Equal("2026-06-04", view.From);
    }

    /// <summary>
    /// Verifies a read that stops at maxGames says the date range may be incomplete, and games with no winner are
    /// listed as skipped (case P04).
    /// </summary>
    [Fact]
    public async Task GetStats_ReadStoppedAtTheLimit_SaysTheRangeMayBeIncompleteAndListsSkippedGames()
    {
        List<JsonObject> games = TwelveGames();
        games[0]["participations"]!.AsArray().ToList().ForEach(value => value!["winner"] = false);
        games[1]["participations"]!.AsArray().ToList().ForEach(value => value!["winner"] = false);
        using Setup setup = new(games);

        PlaygroupGameStatsView view = Success(await setup.Tool.GetStatsAsync(
            567,
            "deck",
            8,
            "2026-06-01",
            "2026-06-30",
            TestContext.Current.CancellationToken));

        Assert.Equal((8, true, 2), (view.GamesRead, view.LimitReached, view.GamesSkipped));
        Assert.Equal([(1, "no-winner"), (2, "no-winner")], view.Skipped.Select(item => (item.GameId, item.Reason)));
        Assert.Contains(view.Notes, note => note.Contains("date range may be incomplete", StringComparison.Ordinal));
        PlaygroupGameStatsView noRange = Success(await new Setup(games).Tool.GetStatsAsync(
            567,
            "deck",
            8,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(noRange.Notes, note => note.Contains("older or newer games", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a range whose start is after its end, a malformed date, and a bad limit are refused (case P05).
    /// </summary>
    [Fact]
    public async Task GetStats_BadDatesAndLimit_AreInvalidInput()
    {
        using Setup setup = new(TwelveGames());

        OperationInvalidInput order = Failure(await setup.Tool.GetStatsAsync(
            567, "deck", 10, "2026-07-01", "2026-06-01", TestContext.Current.CancellationToken));
        OperationInvalidInput badFrom = Failure(await setup.Tool.GetStatsAsync(
            567, "deck", 10, "July 1", null, TestContext.Current.CancellationToken));
        OperationInvalidInput badTo = Failure(await setup.Tool.GetStatsAsync(
            567, "deck", 10, null, "2026-13-01", TestContext.Current.CancellationToken));
        OperationInvalidInput limit = Failure(await setup.Tool.GetStatsAsync(
            567, "deck", 0, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(("invalid-date-range", "from"), (order.ReasonCode, order.Path));
        Assert.Equal(("invalid-date", "from"), (badFrom.ReasonCode, badFrom.Path));
        Assert.Equal(("invalid-date", "to"), (badTo.ReasonCode, badTo.Path));
        Assert.Equal("invalid-max-games", limit.ReasonCode);
        Assert.Empty(setup.Requests);
    }

    /// <summary>
    /// Verifies a provider failure is returned as its own result kind.
    /// </summary>
    [Fact]
    public async Task GetStats_ProviderFailure_IsATypedResult()
    {
        using Setup setup = new(null);

        OperationResult<PlaygroupGameStatsView> result = await setup.Tool.GetStatsAsync(
            567,
            "deck",
            10,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsNotType<OperationSuccess<PlaygroupGameStatsView>>(result.Value);
    }

    /// <summary>
    /// Verifies a playgroup with very many decks gives rows under the size limit and says how to narrow (SIZE-001).
    /// </summary>
    [Fact]
    public async Task GetStats_ManyDecks_AreCutToTheSizeLimit()
    {
        List<JsonObject> games = [];
        for (int index = 0; index < 400; index++)
        {
            games.Add(new JsonObject
            {
                ["id"] = index + 1,
                ["total_rounds"] = 9,
                ["ended_at"] = "2026-06-01T10:00:00Z",
                ["participations"] = new JsonArray(
                    new JsonObject
                    {
                        ["winner"] = true,
                        ["deck_id"] = index + 1,
                        ["deck_name"] = $"A deck with a fairly long name number {index}",
                        ["user_id"] = 1,
                    },
                    new JsonObject { ["winner"] = false, ["deck_id"] = 1000 + index, ["user_id"] = 2 }),
            });
        }

        using Setup setup = new(games);

        PlaygroupGameStatsView view = Success(await setup.Tool.GetStatsAsync(
            567,
            "deck",
            1000,
            cancellationToken: TestContext.Current.CancellationToken));

        int bytes = JsonSerializer.SerializeToUtf8Bytes(view, ToolCallPolicy.SerializerOptions).Length;
        Assert.True(bytes <= 40_000, $"The result is {bytes} bytes.");
        Assert.True(view.Truncated);
        Assert.Contains("from and to", view.Narrow, StringComparison.Ordinal);
        Assert.True(view.Rows.Count < 800);
    }

    /// <summary>
    /// Builds twelve games as JSON: four decks of four players, each deck winning three games, ended on June 1 to 12.
    /// </summary>
    private static List<JsonObject> TwelveGames()
    {
        List<JsonObject> games = [];
        for (int index = 0; index < 12; index++)
        {
            JsonArray participations = [];
            for (int seat = 0; seat < 4; seat++)
            {
                participations.Add(new JsonObject
                {
                    ["winner"] = seat == index % 4,
                    ["deck_id"] = (seat + 1) * 10,
                    ["deck_name"] = $"Deck {(seat + 1) * 10}",
                    ["user_id"] = seat + 1,
                    ["user_name"] = $"User {seat + 1}",
                });
            }

            games.Add(new JsonObject
            {
                ["id"] = index + 1,
                ["total_rounds"] = 6 + index,
                ["started_at"] = $"2026-06-{index + 1:D2}T08:00:00Z",
                ["ended_at"] = $"2026-06-{index + 1:D2}T10:00:00Z",
                ["participations"] = participations,
            });
        }

        return games;
    }

    /// <summary>
    /// Unwraps a successful result.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return result.Value is OperationSuccess<T> success
            ? success.Data
            : throw new Xunit.Sdk.XunitException(JsonSerializer.Serialize(result.Value));
    }

    /// <summary>
    /// Unwraps an invalid-input result.
    /// </summary>
    private static OperationInvalidInput Failure<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationInvalidInput>(result.Value);
    }

    /// <summary>
    /// Builds the tool over a fake Playgroup that serves a list of games in pages, honoring the page and limit.
    /// </summary>
    private sealed class Setup : IDisposable
    {
        /// <summary>
        /// Holds the service.
        /// </summary>
        private readonly PlaygroupService service;

        /// <summary>
        /// Holds the fake provider.
        /// </summary>
        private readonly FakePlaygroup provider;

        /// <summary>
        /// Creates the tool; a null list makes every request fail with a 503.
        /// </summary>
        internal Setup(List<JsonObject>? games)
        {
            provider = new FakePlaygroup(games);
            DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
            PlaygroupOptions options = PlaygroupOptions.CreateDefault("test-key");
            PlaygroupRequestPacer pacer = new(
                Guid.NewGuid().ToString("N"),
                options,
                () => now,
                (duration, _) =>
                {
                    now += duration;
                    return Task.CompletedTask;
                });
            HttpClient client = new(provider) { BaseAddress = new Uri("https://playgroup.gg/api/public/v1/") };
            service = new PlaygroupService(new PlaygroupTransport(client, ownsHttpClient: true, options, pacer));
            Tool = new PlaygroupStatsTools(service);
        }

        /// <summary>
        /// Gets the tool under test.
        /// </summary>
        internal PlaygroupStatsTools Tool { get; }

        /// <summary>
        /// Gets the request paths the provider saw.
        /// </summary>
        internal IReadOnlyList<string> Requests => provider.Requests;

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
        }
    }

    /// <summary>
    /// Serves the games list as the provider does: a page of games for a page number and a limit.
    /// </summary>
    private sealed class FakePlaygroup(List<JsonObject>? games) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the paths and queries the provider saw.
        /// </summary>
        internal List<string> Requests { get; } = [];

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            if (games is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            string query = request.RequestUri.Query.TrimStart('?');
            Dictionary<string, string> values = query.Split('&')
                .Select(part => part.Split('='))
                .ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
            int page = int.Parse(values["page"], System.Globalization.CultureInfo.InvariantCulture);
            int limit = int.Parse(values["limit"], System.Globalization.CultureInfo.InvariantCulture);
            JsonArray slice =
            [
                .. games.Skip((page - 1) * limit).Take(limit).Select(game => JsonNode.Parse(game.ToJsonString())),
            ];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(slice.ToJsonString(), Encoding.UTF8, "application/json"),
            });
        }
    }
}
