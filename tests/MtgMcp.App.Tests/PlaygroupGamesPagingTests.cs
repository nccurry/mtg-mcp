using System.Net;
using System.Text;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.App.Playgroup;
using MtgMcp.Core.Paging;
using MtgMcp.Core.Results;
using MtgMcp.Playgroup;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the Playgroup games list pages with an opaque cursor that follows every full page, and cuts a page that
/// does not fit the result size limit.
/// </summary>
public sealed class PlaygroupGamesPagingTests
{
    /// <summary>
    /// Verifies a full page returns a cursor for the next provider page and a short page ends the list.
    /// </summary>
    [Fact]
    public async Task ListGames_FullPage_HasNextCursorAndShortPageEndsTheList()
    {
        using PlaygroupWorld world = new(GamesJson(2), GamesJson(1));

        PlaygroupGamesPage first = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 2,
            cancellationToken: TestContext.Current.CancellationToken));
        PlaygroupGamesPage last = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 2,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotNull(first.NextCursor);
        Assert.False(first.Truncated);
        Assert.Null(last.NextCursor);
        Assert.Contains("page=1", world.Requests[0], StringComparison.Ordinal);
        Assert.Contains("page=2", world.Requests[1], StringComparison.Ordinal);
        Assert.Contains("limit=2", world.Requests[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a total that is an exact multiple of the page size ends with one empty page that has no cursor.
    /// </summary>
    [Fact]
    public async Task ListGames_TotalIsAnExactMultiple_EndsWithOneEmptyPage()
    {
        using PlaygroupWorld world = new(GamesJson(2), GamesJson(0));

        PlaygroupGamesPage first = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 2,
            cancellationToken: TestContext.Current.CancellationToken));
        PlaygroupGamesPage empty = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 2,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotNull(first.NextCursor);
        Assert.Equal(0, empty.Source.Data.GetArrayLength());
        Assert.Null(empty.NextCursor);
    }

    /// <summary>
    /// Verifies a cursor is refused for another playgroup, another page size, or damaged text, before any provider
    /// request.
    /// </summary>
    [Fact]
    public async Task ListGames_CursorOfAnotherRequest_IsInvalidAtCursor()
    {
        using PlaygroupWorld world = new(GamesJson(2));
        PlaygroupGamesPage first = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 2,
            cancellationToken: TestContext.Current.CancellationToken));

        OperationResult<PlaygroupGamesPage> otherGroup = await world.Tools.ListPlaygroupGamesAsync(
            6,
            pageSize: 2,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken);
        OperationResult<PlaygroupGamesPage> otherSize = await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 3,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken);
        OperationResult<PlaygroupGamesPage> damaged = await world.Tools.ListPlaygroupGamesAsync(
            5,
            cursor: "damaged",
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (OperationResult<PlaygroupGamesPage> result in new[] { otherGroup, otherSize, damaged })
        {
            OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
            Assert.Equal(SharedReasonCodes.InvalidCursor, invalid.ReasonCode);
            Assert.Equal("cursor", invalid.Path);
        }

        Assert.Single(world.Requests);
    }

    /// <summary>
    /// Verifies an out-of-range page size names the page size argument.
    /// </summary>
    [Fact]
    public async Task ListGames_PageSizeOutOfRange_IsInvalidAtPageSize()
    {
        using PlaygroupWorld world = new(GamesJson(1));

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 101,
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal("pageSize", invalid.Path);
        Assert.Empty(world.Requests);
    }

    /// <summary>
    /// Verifies a page over the result limit keeps leading games, says so, and the cursor continues with the games left
    /// out of the same provider page.
    /// </summary>
    [Fact]
    public async Task ListGames_PageOverTheResultLimit_IsCutAndContinuesWithTheRest()
    {
        using PlaygroupWorld world = new(GamesJson(20, rowBytes: 5_000));

        PlaygroupGamesPage first = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 20,
            cancellationToken: TestContext.Current.CancellationToken));
        int shown = first.Source.Data.GetArrayLength();
        PlaygroupGamesPage second = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 20,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(first.Truncated);
        Assert.InRange(shown, 1, 19);
        Assert.InRange(ResultSize.Measure(first), 1, ResultSizeLimit.MaximumBytes);
        Assert.Equal("g" + shown, second.Source.Data[0].GetProperty("id").GetString());
        Assert.Contains("page=1", world.Requests[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a cursor inside a provider page is stale when a game was added or removed since it was issued.
    /// </summary>
    [Fact]
    public async Task ListGames_PageChangedUnderACutCursor_IsCursorStale()
    {
        using PlaygroupWorld world = new(GamesJson(20, rowBytes: 5_000), GamesJson(19, rowBytes: 5_000));
        PlaygroupGamesPage first = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 20,
            cancellationToken: TestContext.Current.CancellationToken));

        OperationResult<PlaygroupGamesPage> second = await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 20,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SharedReasonCodes.CursorStale, Assert.IsType<OperationConflict>(second.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a cursor inside a provider page continues with the rows left out when the page is unchanged.
    /// </summary>
    [Fact]
    public async Task ListGames_PageUnchangedUnderACutCursor_ContinuesWithTheRest()
    {
        using PlaygroupWorld world = new(GamesJson(20, rowBytes: 5_000));
        PlaygroupGamesPage first = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 20,
            cancellationToken: TestContext.Current.CancellationToken));

        PlaygroupGamesPage second = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 20,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "g" + first.Source.Data.GetArrayLength(),
            second.Source.Data[0].GetProperty("id").GetString());
    }

    /// <summary>
    /// Verifies a cursor inside a provider page that was issued for another playgroup is refused before any provider
    /// request.
    /// </summary>
    [Fact]
    public async Task ListGames_CutCursorOfAnotherPlaygroup_IsInvalidWithoutAProviderRequest()
    {
        using PlaygroupWorld world = new(GamesJson(20, rowBytes: 5_000));
        PlaygroupGamesPage first = RequireSuccess(await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 20,
            cancellationToken: TestContext.Current.CancellationToken));

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await world.Tools.ListPlaygroupGamesAsync(
            6,
            pageSize: 20,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal(SharedReasonCodes.InvalidCursor, invalid.ReasonCode);
        Assert.Single(world.Requests);
    }

    /// <summary>
    /// Verifies a well-formed cursor that claims more rows shown than a page holds is refused at the cursor, before
    /// any provider request.
    /// </summary>
    [Fact]
    public async Task ListGames_CursorShowingMoreRowsThanThePageSize_IsInvalidAtCursor()
    {
        using PlaygroupWorld world = new(GamesJson(2));
        PageCursorBinding binding = PageCursorBinding.ForRequest(RequestHash.Of(
            (5, typeof(int)),
            (2, typeof(int)),
            (false, typeof(bool))));
        string forged = PageCursor.Encode(PageCursorPosition.AtProviderPage(0, 3), binding);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await world.Tools.ListPlaygroupGamesAsync(
            5,
            pageSize: 2,
            cursor: forged,
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal(SharedReasonCodes.InvalidCursor, invalid.ReasonCode);
        Assert.Equal("cursor", invalid.Path);
        Assert.Empty(world.Requests);
    }

    /// <summary>
    /// Builds a provider games array of rows of a given size.
    /// </summary>
    private static string GamesJson(int rows, int rowBytes = 40)
    {
        string filler = new('x', rowBytes);
        return "[" + string.Join(",", Enumerable.Range(0, rows).Select(index =>
            $$"""{"id":"g{{index}}","pad":"{{filler}}"}""")) + "]";
    }

    /// <summary>
    /// Extracts successful data while preserving useful failure output.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Holds the read tools over a service that replays queued provider pages and records each request.
    /// </summary>
    internal sealed class PlaygroupWorld : IDisposable
    {
        /// <summary>
        /// Owns the service and its transport.
        /// </summary>
        private readonly PlaygroupService service;

        /// <summary>
        /// Answers each provider request.
        /// </summary>
        private readonly Handler handler;

        /// <summary>
        /// Creates tools over the queued provider responses.
        /// </summary>
        internal PlaygroupWorld(params string[] responses)
        {
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
            handler = new Handler(responses, Requests);
            HttpClient client = new(handler)
            {
                BaseAddress = new Uri("https://playgroup.gg/api/public/v1/"),
            };
            service = new PlaygroupService(new PlaygroupTransport(client, ownsHttpClient: true, options, pacer));
            Tools = new PlaygroupReadTools(service);
            WriteTools = new PlaygroupRemoteWriteTools(service, OperationMode.Remote);
        }

        /// <summary>
        /// Gets the tools under test.
        /// </summary>
        internal PlaygroupReadTools Tools { get; }

        /// <summary>
        /// Gets the remote write tools over the same provider.
        /// </summary>
        internal PlaygroupRemoteWriteTools WriteTools { get; }

        /// <summary>
        /// Sets the status the provider answers with from now on.
        /// </summary>
        internal HttpStatusCode Status
        {
            set => handler.Status = value;
        }

        /// <summary>
        /// Gets the request URL of each provider call, in order.
        /// </summary>
        internal List<string> Requests { get; } = [];

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
        }
    }

    /// <summary>
    /// Replays queued JSON bodies in order and repeats the last one.
    /// </summary>
    private sealed class Handler(string[] responses, List<string> requests) : HttpMessageHandler
    {
        /// <summary>
        /// Counts the requests served.
        /// </summary>
        private int served;

        /// <summary>
        /// Gets or sets the status each response carries.
        /// </summary>
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!.ToString());
            string body = responses[Math.Min(served++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
