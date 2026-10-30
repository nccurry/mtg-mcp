using System.Text.Json;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Runs pull, edit, push preview, push apply, and pull preview against an offline fake that behaves like Archidekt
/// where it has caused false differences, and checks the size limit and the apply notice of the previews.
/// </summary>
public sealed class ArchidektWorkflowTests
{
    /// <summary>
    /// Holds the most characters a tool text result may have before Claude Code warns (10,000 tokens); the hard
    /// ceiling at which it saves the result to a file is 50,000.
    /// </summary>
    private const int MaximumResultCharacters = 40_000;

    /// <summary>
    /// Verifies the whole workflow in remote mode: after an edit and a push, a pull preview and a push preview are
    /// clean, although the fake returns category names, order numbers, and rows in a new order on every read.
    /// </summary>
    [Fact]
    public async Task Workflow_InRemoteMode_PushesTheEditAndReadsCleanAfterwards()
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;

        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry bolt = local.Entries.Single(value => value.CardName == "Bolt");
        DeckEntry dart = local.Entries.Single(value => value.CardName == "Dart");
        DeckCategory removal = local.Categories.Single(value => value.Name == "Removal");
        DeckCategory ramp = local.Categories.Single(value => value.Name == "Ramp");
        Guid shockId = Guid.NewGuid();
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new UpdateDeckEntryChange(bolt with { Quantity = 3 }),
                new AssignDeckCategoryChange(dart.EntryId, ramp.CategoryId, false),
                new AddDeckEntryChange(new DeckEntryDraft(1, "Shock", EntryId: shockId)),
                new AssignDeckCategoryChange(shockId, removal.CategoryId, true),
            ],
            token));

        ArchidektSyncPreviewView preview = Success(await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.True(preview.ApplyAvailable);
        Assert.Null(preview.ApplyNotice);
        Assert.False(preview.HasConflicts);
        Assert.Equal(3, preview.Summary.OperationCount);
        Assert.Equal(
            ["entry-add:Shock", "entry-update:Bolt", "entry-update:Dart"],
            preview.Operations.Select(value => $"{value.Kind}:{value.Subject}").Order(StringComparer.Ordinal));

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token));

        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(3, world.Fake.QuantityOf("Bolt"));
        Assert.Equal(1, world.Fake.QuantityOf("Shock"));
        Assert.Contains("Ramp", world.Fake.CategoriesOf("Dart"));
        Assert.Equal(3, world.Fake.Mutations);

        ArchidektSyncPreviewView pull = Success(await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView push = Success(await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.False(pull.HasConflicts);
        Assert.Empty(pull.Differences);
        Assert.Equal(0, pull.Summary.DifferenceCount);
        Assert.Empty(push.Operations);
        Assert.Equal(0, push.Summary.OperationCount);
        Assert.Equal(0, push.Summary.DifferenceCount);
        Assert.False(push.HasConflicts);
    }

    /// <summary>
    /// Verifies reading the fake again and again, in a new order each time, never shows a difference or an
    /// operation for a deck nobody edited.
    /// </summary>
    [Fact]
    public async Task UneditedDeck_ReadManyTimes_NeverShowsADifference()
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();

        for (int round = 0; round < 8; round++)
        {
            ArchidektSyncPreviewView push = Success(await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
            ArchidektSyncPreviewView pull = Success(await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
            ArchidektSyncDiff diff = Success(await world.Reads.DiffAsync(localDeckId, token));

            Assert.Empty(push.Operations);
            Assert.Empty(push.Differences);
            Assert.False(push.HasConflicts);
            Assert.Empty(pull.Differences);
            Assert.Empty(diff.Differences);
        }
    }

    /// <summary>
    /// Verifies in local mode a push preview says the apply tool is not available and names the setting, while a
    /// pull preview says the pull apply tool is, and that the apply tool refuses to run.
    /// </summary>
    [Fact]
    public async Task Workflow_InLocalMode_ExplainsTheMissingPushApply()
    {
        using World world = new(OperationMode.Local, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();

        ArchidektSyncPreviewView push = Success(await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
        OperationResult<ArchidektApplyResult> denied = await new ArchidektRemoteWriteTools(
            world.Coordinator,
            OperationMode.Local).ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, 1, "remote", "preview"),
            token);

        Assert.False(push.ApplyAvailable);
        Assert.Contains("archidekt_push_apply is not available", push.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("local mode", push.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("--mode=remote", push.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("--mode=local", push.ApplyNotice, StringComparison.Ordinal);
        Assert.True(pull.ApplyAvailable);
        Assert.Null(pull.ApplyNotice);
        Assert.IsType<OperationUnsupported>(denied.Value);
    }

    /// <summary>
    /// Verifies the apply notices for read-only mode and for missing credentials.
    /// </summary>
    [Fact]
    public void ApplyNotices_NameTheModeAndTheCredentials()
    {
        ArchidektAuthStatus configured = new("configured", true, false, string.Empty);
        ArchidektAuthStatus missing = new("missing", false, false, string.Empty);

        Assert.Null(ArchidektResultPaging.PushApplyNotice(OperationMode.Remote, configured));
        Assert.Null(ArchidektResultPaging.PullApplyNotice(OperationMode.Local, configured));
        Assert.Contains(
            "read-only mode",
            ArchidektResultPaging.PushApplyNotice(OperationMode.ReadOnly, configured),
            StringComparison.Ordinal);
        Assert.Contains(
            "--mode=remote",
            ArchidektResultPaging.PullApplyNotice(OperationMode.ReadOnly, configured),
            StringComparison.Ordinal);
        string both = ArchidektResultPaging.PushApplyNotice(OperationMode.Local, missing)!;
        Assert.Contains("--mode=remote", both, StringComparison.Ordinal);
        Assert.Contains("credentials are not configured", both, StringComparison.Ordinal);
        Assert.Contains(
            "credentials are not configured",
            ArchidektResultPaging.PushApplyNotice(OperationMode.Remote, missing),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a 300-card deck comes back in pages that each stay under the size limit, with a summary on every
    /// page and every entry reachable exactly once.
    /// </summary>
    [Fact]
    public async Task DeckGet_With300Cards_PagesUnderTheLimitAndReachesEveryEntry()
    {
        FakeArchidekt fake = CreateLargeDeck(300);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        List<string> relationIds = [];
        int offset = 0;
        int pages = 0;

        while (true)
        {
            OperationResult<ArchidektDeckView> result = await world.Reads.GetDeckAsync("42", offset, token);
            ArchidektDeckView page = Success(result);
            Assert.True(JsonLength(result) < MaximumResultCharacters, $"page {pages} is {JsonLength(result)} characters");
            Assert.Equal(300, page.Summary.EntryCount);
            Assert.Equal(offset, page.Offset);
            Assert.Equal(offset == 0, page.Categories.Count > 0);
            relationIds.AddRange(page.Entries.Select(value => value.ProviderRelationId));
            pages++;
            if (page.NextOffset is null)
            {
                Assert.Null(page.Truncated);
                Assert.Null(page.Narrow);
                break;
            }

            Assert.True(page.Truncated);
            Assert.Contains("offset", page.Narrow, StringComparison.Ordinal);
            offset = page.NextOffset.Value;
        }

        Assert.True(pages > 1);
        Assert.Equal(300, relationIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Verifies an offset outside the deck is refused, and a deck whose unknown fields are huge still fits.
    /// </summary>
    [Fact]
    public void DeckGet_BadOffsetAndHugeExtensions_AreHandled()
    {
        RemoteDeckSnapshot deck = ParseDeck(
            """{ "id": 1, "name": "Big", "deckFormat": 3, "categories": [], "cards": [], "blob": "__BLOB__" }"""
                .Replace("__BLOB__", new string('x', 20_000), StringComparison.Ordinal));

        OperationResult<ArchidektDeckView> bad = ArchidektResultPaging.PageDeck(deck, 5);
        OperationResult<ArchidektDeckView> negative = ArchidektResultPaging.PageDeck(deck, -1);
        ArchidektDeckView page = Success(ArchidektResultPaging.PageDeck(deck, 0));

        Assert.IsType<OperationInvalidInput>(bad.Value);
        Assert.IsType<OperationInvalidInput>(negative.Value);
        Assert.True(page.ExtensionsOmitted);
        Assert.Empty(page.Extensions);
        Assert.True(JsonLength(ArchidektResultPaging.PageDeck(deck, 0)) < MaximumResultCharacters);
    }

    /// <summary>
    /// Verifies a push of 300 operations previews in pages under the size limit with the same guards on every page,
    /// every operation is reachable, and applying with the guards of a later page still runs all 300.
    /// </summary>
    [Fact]
    public async Task PushPreview_With300Operations_PagesUnderTheLimit_AndApplyRunsEveryOperation()
    {
        FakeArchidekt fake = CreateLargeDeck(300);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Select(value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
            token));

        List<ArchidektSyncPreviewView> pages = [];
        int operationOffset = 0;
        int differenceOffset = 0;
        while (true)
        {
            OperationResult<ArchidektSyncPreviewView> result = await world.Reads.PreviewPushAsync(
                localDeckId,
                operationOffset,
                differenceOffset,
                token);
            ArchidektSyncPreviewView page = Success(result);
            Assert.True(JsonLength(result) < MaximumResultCharacters, $"page {pages.Count} is {JsonLength(result)} characters");
            pages.Add(page);
            if (page.Truncated is null)
            {
                break;
            }

            Assert.Contains("operationOffset", page.Narrow, StringComparison.Ordinal);
            operationOffset = page.NextOperationOffset ?? page.OperationOffset + page.Operations.Count;
            differenceOffset = page.NextDifferenceOffset ?? page.DifferenceOffset + page.Differences.Count;
        }

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.Equal(300, page.Summary.OperationCount));
        Assert.All(pages, page => Assert.Equal(300, page.Summary.OperationsByKind["entry-update"]));
        Assert.All(pages, page => Assert.Equal(300, page.Summary.DifferenceCount));
        Assert.All(pages, page => Assert.Equal(pages[0].PreviewFingerprint, page.PreviewFingerprint));
        Assert.All(pages, page => Assert.Equal(pages[0].RemoteFingerprint, page.RemoteFingerprint));
        Assert.Equal(300, pages.SelectMany(page => page.Operations).Select(value => value.Sequence).Distinct().Count());
        Assert.Equal(300, pages.SelectMany(page => page.Differences).Select(value => value.Path).Distinct().Count());

        ArchidektSyncPreviewView last = pages[^1];
        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, edited.Revision, last.RemoteFingerprint, last.PreviewFingerprint),
            token));

        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(300, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies a request to apply with the guards of a stale preview is still refused after paging.
    /// </summary>
    [Fact]
    public async Task PushApply_WithAFingerprintFromAnotherPreview_IsRefused()
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry bolt = local.Entries.Single(value => value.CardName == "Bolt");
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [new UpdateDeckEntryChange(bolt with { Quantity = 3 })],
            token));
        ArchidektSyncPreviewView preview = Success(await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, edited.Revision, preview.RemoteFingerprint, "not-the-preview"),
            token);

        Assert.Equal("push-preview-changed", Assert.IsType<OperationConflict>(refused.Value).ReasonCode);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies an offset outside a preview's rows is refused.
    /// </summary>
    [Fact]
    public async Task PushPreview_WithABadOffset_IsRefused()
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        Guid localDeckId = await world.PullAsync();

        OperationResult<ArchidektSyncPreviewView> result = await world.Reads.PreviewPushAsync(
            localDeckId,
            operationOffset: 5,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("operationOffset", Assert.IsType<OperationInvalidInput>(result.Value).Path);
    }

    /// <summary>
    /// Creates a small deck with two multi-category entries.
    /// </summary>
    private static FakeArchidekt CreateSmallDeck()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddCategory("Ramp");
        fake.AddCategory("Sideboard", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 2, "Removal", "Ramp");
        fake.AddEntry(fake.AddCardToCatalog(502, "Charm"), 1, "Ramp", "Removal");
        fake.AddEntry(fake.AddCardToCatalog(503, "Anger"), 1, "Sideboard");
        fake.AddEntry(fake.AddCardToCatalog(504, "Dart"), 1, "Removal");
        fake.AddCardToCatalog(9001, "Shock");
        return fake;
    }

    /// <summary>
    /// Creates a deck with many entries, each in three categories.
    /// </summary>
    private static FakeArchidekt CreateLargeDeck(int count)
    {
        FakeArchidekt fake = new();
        string[] names = ["Removal", "Ramp", "Draw", "Threats", "Lands", "Protection"];
        foreach (string name in names)
        {
            fake.AddCategory(name);
        }

        for (int index = 0; index < count; index++)
        {
            fake.AddEntry(
                fake.AddCardToCatalog(1000 + index, $"Card Number {index:000}"),
                1,
                names[index % names.Length],
                names[(index + 1) % names.Length],
                names[(index + 2) % names.Length]);
        }

        return fake;
    }

    /// <summary>
    /// Parses one provider deck payload into a remote snapshot.
    /// </summary>
    private static RemoteDeckSnapshot ParseDeck(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            json,
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            "GET");
    }

    /// <summary>
    /// Measures a result the way the server writes it as a tool text result.
    /// </summary>
    private static int JsonLength<T>(OperationResult<T> result)
    {
        return JsonSerializer.Serialize(result, ToolCallPolicy.SerializerOptions).Length;
    }

    /// <summary>
    /// Extracts one successful union case.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Holds the service, store, tools, and fake of one test.
    /// </summary>
    private sealed class World : IDisposable
    {
        /// <summary>
        /// Holds the provider service.
        /// </summary>
        private readonly ArchidektService service;

        /// <summary>
        /// Holds the temporary data folder.
        /// </summary>
        private readonly TemporaryDirectory directory = new();

        /// <summary>
        /// Creates the world around one fake Archidekt, in the given mode.
        /// </summary>
        internal World(OperationMode mode, FakeArchidekt fake)
        {
            Fake = fake;
            ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                MinimumRequestInterval = TimeSpan.Zero,
                MaximumRequestsPerWindow = 100_000,
                MaximumRequestsPerOperation = 2_000,
            };
            service = new ArchidektService(
                new ArchidektSession(new HttpClient(fake) { BaseAddress = options.BaseAddress }, ownsHttpClient: true, options),
                options.MaximumRequestsPerOperation);
            Store = new SqliteDeckStore(directory.Path, "test");
            Coordinator = new ArchidektCoordinator(service, Store);
            Reads = new ArchidektReadTools(Coordinator, mode);
            Writes = new ArchidektRemoteWriteTools(Coordinator, mode);
            LocalWrites = new ArchidektLocalWriteTools(Coordinator, mode);
        }

        /// <summary>
        /// Gets the fake Archidekt.
        /// </summary>
        internal FakeArchidekt Fake { get; }

        /// <summary>
        /// Gets the local deck store.
        /// </summary>
        internal SqliteDeckStore Store { get; }

        /// <summary>
        /// Gets the composition boundary.
        /// </summary>
        internal ArchidektCoordinator Coordinator { get; }

        /// <summary>
        /// Gets the read and preview tools.
        /// </summary>
        internal ArchidektReadTools Reads { get; }

        /// <summary>
        /// Gets the remote write tools.
        /// </summary>
        internal ArchidektRemoteWriteTools Writes { get; }

        /// <summary>
        /// Gets the local write tool.
        /// </summary>
        internal ArchidektLocalWriteTools LocalWrites { get; }

        /// <summary>
        /// Pulls the fake deck into a new local deck and returns its id.
        /// </summary>
        internal async Task<Guid> PullAsync()
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            ArchidektSyncPreviewView preview = Success(await Reads.PreviewPullAsync("42", cancellationToken: token));
            ArchidektApplyResult applied = Success(await LocalWrites.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, preview.RemoteFingerprint, preview.PreviewFingerprint),
                token));
            return Assert.IsType<Guid>(applied.LocalDeckId);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            service.Dispose();
            Store.Dispose();
            directory.Dispose();
        }
    }
}
