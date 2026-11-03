using System.Net;
using System.Text;
using MtgMcp.App.Hosting;
using MtgMcp.App.Spellbook;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Spellbook;

namespace MtgMcp.App.Tests;

/// <summary>
/// Verifies the Commander Spellbook tools page with an opaque cursor, end at the source offset cap, and cut a page that
/// does not fit the result size limit.
/// </summary>
public sealed class SpellbookPagingToolTests
{
    /// <summary>
    /// Verifies a page with a source next link returns a cursor that asks for the following source offset.
    /// </summary>
    [Fact]
    public async Task SearchVariants_NextPage_FollowsTheCursorToTheNextOffset()
    {
        using TemporaryDirectory temporary = new();
        using SpellbookWorld world = new(temporary, PageJson(2, hasNext: true), PageJson(2, hasNext: false));

        SpellbookVariantPage first = RequireSuccess(await world.Tools.SearchVariantsAsync(
            "type:combo",
            pageSize: 2,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(first.Truncated);
        Assert.NotNull(first.NextCursor);
        SpellbookVariantPage last = RequireSuccess(await world.Tools.SearchVariantsAsync(
            "type:combo",
            pageSize: 2,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Null(last.NextCursor);
        Assert.Contains("offset=0", world.Requests[0], StringComparison.Ordinal);
        Assert.Contains("offset=2", world.Requests[1], StringComparison.Ordinal);
        Assert.Equal(2, last.Source.Request.Page!.Offset);
    }

    /// <summary>
    /// Verifies the cursor ends at the source offset cap even when the source reports another page.
    /// </summary>
    [Fact]
    public async Task SearchVariants_PageReachingTheOffsetCap_HasNoNextCursor()
    {
        using TemporaryDirectory temporary = new();
        using SpellbookWorld world = new(temporary, PageJson(20, hasNext: true));
        SpellbookVariantPage page = RequireSuccess(await world.Tools.SearchVariantsAsync(
            "type:combo",
            pageSize: 20,
            cancellationToken: TestContext.Current.CancellationToken));
        while (page.Source.Request.Page!.Offset < 980)
        {
            page = RequireSuccess(await world.Tools.SearchVariantsAsync(
                "type:combo",
                pageSize: 20,
                cursor: page.NextCursor,
                cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.NotNull(page.NextCursor);
        SpellbookVariantPage capped = RequireSuccess(await world.Tools.SearchVariantsAsync(
            "type:combo",
            pageSize: 20,
            cursor: page.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(1_000, capped.Source.Request.Page!.Offset);
        Assert.Null(capped.NextCursor);
    }

    /// <summary>
    /// Verifies a cursor from another query and a damaged cursor are refused at the cursor argument before any source
    /// request.
    /// </summary>
    [Fact]
    public async Task SearchVariants_CursorOfAnotherRequest_IsInvalidAtCursor()
    {
        using TemporaryDirectory temporary = new();
        using SpellbookWorld world = new(temporary, PageJson(2, hasNext: true));
        SpellbookVariantPage first = RequireSuccess(await world.Tools.SearchVariantsAsync(
            "type:combo",
            pageSize: 2,
            cancellationToken: TestContext.Current.CancellationToken));

        OperationResult<SpellbookVariantPage> otherQuery = await world.Tools.SearchVariantsAsync(
            "type:other",
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken);
        OperationResult<SpellbookVariantPage> damaged = await world.Tools.SearchVariantsAsync(
            "type:combo",
            cursor: "not-a-cursor",
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (OperationResult<SpellbookVariantPage> result in new[] { otherQuery, damaged })
        {
            OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(result.Value);
            Assert.Equal(SharedReasonCodes.InvalidCursor, invalid.ReasonCode);
            Assert.Equal("cursor", invalid.Path);
        }

        Assert.Single(world.Requests);
    }

    /// <summary>
    /// Verifies an invalid page size names the page size argument.
    /// </summary>
    [Fact]
    public async Task SearchVariants_PageSizeOutOfRange_IsInvalidAtPageSize()
    {
        using TemporaryDirectory temporary = new();
        using SpellbookWorld world = new(temporary, "{}");

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>((await world.Tools.SearchVariantsAsync(
            "type:combo",
            pageSize: 26,
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.Equal("invalid-page-size", invalid.ReasonCode);
        Assert.Equal("pageSize", invalid.Path);
    }

    /// <summary>
    /// Verifies a page that does not fit the result limit keeps leading rows, says so, and continues after the last
    /// row shown.
    /// </summary>
    [Fact]
    public async Task SearchVariants_PageOverTheResultLimit_IsCutAndContinuesAfterTheLastRow()
    {
        using TemporaryDirectory temporary = new();
        using SpellbookWorld world = new(temporary, PageJson(20, hasNext: true, rowBytes: 5_000));

        SpellbookVariantPage page = RequireSuccess(await world.Tools.SearchVariantsAsync(
            "type:combo",
            cancellationToken: TestContext.Current.CancellationToken));

        int shown = page.Source.Data.GetProperty("results").GetArrayLength();
        Assert.True(page.Truncated);
        Assert.InRange(shown, 1, 19);
        Assert.InRange(ResultSize.Measure(page), 1, ResultSizeLimit.MaximumBytes);
        SpellbookVariantPage following = RequireSuccess(await world.Tools.SearchVariantsAsync(
            "type:combo",
            cursor: page.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(shown, following.Source.Request.Page!.Offset);
    }

    /// <summary>
    /// Verifies a deck page ties its cursor to the deck revision: a cursor read with another revision is refused as
    /// stale, and a revision that is no longer current returns the shared revision conflict.
    /// </summary>
    [Fact]
    public async Task FindDeckCombos_CursorAndRevision_AreGuarded()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = await CreateDeckAsync(store);
        using SpellbookWorld world = new(temporary, store, DeckPageJson(hasNext: true));

        SpellbookDeckComboEvidence first = RequireSuccess(await world.Tools.FindDeckCombosAsync(
            deck.DeckId,
            deck.Revision,
            pageSize: 5,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(first.NextCursor);
        OperationResult<SpellbookDeckComboEvidence> stale = await world.Tools.FindDeckCombosAsync(
            deck.DeckId,
            deck.Revision + 1,
            pageSize: 5,
            cursor: first.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken);
        OperationResult<SpellbookDeckComboEvidence> changed = await world.Tools.FindDeckCombosAsync(
            deck.DeckId,
            deck.Revision + 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SharedReasonCodes.CursorStale, Assert.IsType<OperationConflict>(stale.Value).ReasonCode);
        OperationConflict conflict = Assert.IsType<OperationConflict>(changed.Value);
        Assert.Equal(SharedReasonCodes.StaleDeckRevision, conflict.ReasonCode);
        Assert.Equal(deck.Revision, conflict.Current!.Revision);
    }

    /// <summary>
    /// Verifies a deck page over the result limit is cut, says so, and has no cursor because the source offset no
    /// longer names the first row that was left out.
    /// </summary>
    [Fact]
    public async Task FindDeckCombos_PageOverTheResultLimit_IsCutWithoutCursor()
    {
        using TemporaryDirectory temporary = new();
        using SqliteDeckStore store = new(temporary.Path, "0.9.0-preview.1");
        DeckDocument deck = await CreateDeckAsync(store);
        using SpellbookWorld world = new(temporary, store, DeckPageJson(hasNext: true, rows: 20, rowBytes: 5_000));

        SpellbookDeckComboEvidence page = RequireSuccess(await world.Tools.FindDeckCombosAsync(
            deck.DeckId,
            deck.Revision,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(page.Truncated);
        Assert.Null(page.NextCursor);
        Assert.InRange(
            page.Source.Data.GetProperty("results").GetProperty("included").GetArrayLength(),
            1,
            19);
    }

    /// <summary>
    /// Creates a one-entry deck that Commander Spellbook can be asked about.
    /// </summary>
    private static async Task<DeckDocument> CreateDeckAsync(SqliteDeckStore store)
    {
        return RequireSuccess(await store.CreateAsync(
            new DeckCreateRequest("Combos", Entries: [new DeckEntryDraft(1, "Main Card", Zone: "main")]),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Builds a variant search response of rows of a given size.
    /// </summary>
    private static string PageJson(int rows, bool hasNext, int rowBytes = 40)
    {
        string next = hasNext ? "\"https://example.invalid/next\"" : "null";
        return $$"""{"count":9999,"next":{{next}},"previous":null,"results":[{{Rows(rows, rowBytes)}}]}""";
    }

    /// <summary>
    /// Builds a find-my-combos response whose included list holds rows of a given size.
    /// </summary>
    private static string DeckPageJson(bool hasNext, int rows = 2, int rowBytes = 40)
    {
        string next = hasNext ? "\"https://example.invalid/next\"" : "null";
        return $$$"""{"count":9999,"next":{{{next}}},"previous":null,"results":{"identity":"U","included":[{{{Rows(rows, rowBytes)}}}],"almostIncluded":[]}}""";
    }

    /// <summary>
    /// Builds the JSON text of a list of object rows.
    /// </summary>
    private static string Rows(int rows, int rowBytes)
    {
        string filler = new('x', rowBytes);
        return string.Join(",", Enumerable.Range(0, rows).Select(index => $$"""{"id":"v{{index}}","pad":"{{filler}}"}"""));
    }

    /// <summary>
    /// Extracts successful data while preserving useful failure output.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Holds the tools over a service that replays queued source responses and records each request.
    /// </summary>
    private sealed class SpellbookWorld : IDisposable
    {
        /// <summary>
        /// Owns the service and its cache.
        /// </summary>
        private readonly SpellbookService service;

        /// <summary>
        /// Owns the deck store when the world made it.
        /// </summary>
        private readonly SqliteDeckStore? ownedStore;

        /// <summary>
        /// Creates tools over an empty deck store and the queued responses.
        /// </summary>
        internal SpellbookWorld(TemporaryDirectory directory, params string[] responses)
            : this(directory, null, responses)
        {
        }

        /// <summary>
        /// Creates tools over a deck store, or over a new empty one when none is given, and the queued responses.
        /// </summary>
        internal SpellbookWorld(TemporaryDirectory directory, SqliteDeckStore? store, params string[] responses)
        {
            ownedStore = store is null ? new SqliteDeckStore(Path.Combine(directory.Path, "decks"), "0.9.0-preview.1") : null;
            SqliteDeckStore deckStore = store ?? ownedStore!;
            Handler handler = new(responses, Requests);
            string root = Path.Combine(directory.Path, "spellbook");
            Directory.CreateDirectory(root);
            service = new SpellbookService(
                SpellbookOptions.CreateDefault(root),
                new SpellbookDatabase(root),
                new SpellbookTransport(new HttpClient(handler), ownsHttpClient: true, TimeProvider.System),
                TimeProvider.System);
            Tools = new SpellbookReadTools(service, new SpellbookDeckInputResolver(deckStore));
        }

        /// <summary>
        /// Gets the tools under test.
        /// </summary>
        internal SpellbookReadTools Tools { get; }

        /// <summary>
        /// Gets the request URL of each source call, in order.
        /// </summary>
        internal List<string> Requests { get; } = [];

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
            ownedStore?.Dispose();
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

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!.ToString());
            string body = responses[Math.Min(served++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
