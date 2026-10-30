using System.Text.Json;
using System.Text.Json.Nodes;
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

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

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

        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

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
            ArchidektSyncPreviewView push = Success(
                await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
            ArchidektSyncPreviewView pull = Success(
                await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
            ArchidektSyncDiffView diff = Success(await world.Reads.DiffAsync(localDeckId, cancellationToken: token));

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

        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
        ArchidektRemoteWriteTools localModeWrites = new(world.Coordinator, OperationMode.Local);
        OperationResult<ArchidektApplyResult> denied = await localModeWrites.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, 1, "remote", "preview"),
            token);

        Assert.False(push.ApplyAvailable);
        Assert.Contains("archidekt_push_apply is not available", push.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("local mode", push.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("Ask the user to start the server in remote mode", push.ApplyNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("--mode", push.ApplyNotice, StringComparison.Ordinal);
        Assert.True(pull.ApplyAvailable);
        Assert.Null(pull.ApplyNotice);
        Assert.IsType<OperationUnsupported>(denied.Value);
    }

    /// <summary>
    /// Verifies a deck synchronized by the earlier version, whose stored baseline carries fingerprints from the old
    /// formula and category names in the order the provider sent them, shows no difference, no push operation, and no
    /// pull difference: the baseline is read again with the current rules.
    /// </summary>
    [Fact]
    public async Task BaselineFromTheEarlierFormula_ShowsNoDifferenceAndNoOperation()
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;
        RemoteDeckSnapshot remote = Success(await world.Coordinator.Service.GetDeckAsync("42", token));
        DeckProviderBinding binding = new(
            Guid.CreateVersion7(),
            "archidekt",
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            "old-formula-remote-fingerprint",
            DateTimeOffset.UtcNow,
            null);
        DeckCreateRequest create = ArchidektLocalMapper.ToCreateRequest(remote, binding);
        JsonNode baseline = JsonNode.Parse(ArchidektLocalMapper.CreateBaseline(create, remote))!;
        baseline["remoteFingerprint"] = "old-formula-remote-fingerprint";
        JsonObject snapshot = baseline["remoteSnapshot"]!.AsObject();
        snapshot["remoteFingerprint"] = "old-formula-remote-fingerprint";
        snapshot["contentFingerprint"] = "old-formula-content-fingerprint";
        foreach (JsonNode? entry in snapshot["entries"]!.AsArray())
        {
            JsonArray names = entry!["categoryNames"]!.AsArray();
            List<string> reversed = [.. names.Select(value => value!.GetValue<string>()).Reverse()];
            names.Clear();
            foreach (string name in reversed)
            {
                names.Add(name);
            }
        }

        DeckDocument local = Success(await world.Store.CreateSynchronizedAsync(
            create,
            new DeckSyncBaseline(binding.BindingId, baseline.ToJsonString()),
            token));

        ArchidektSyncDiffView diff = Success(await world.Reads.DiffAsync(local.DeckId, cancellationToken: token));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(local.DeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", local.DeckId, cancellationToken: token));

        Assert.Equal(0, diff.DifferenceCount);
        Assert.False(diff.HasConflicts);
        Assert.Empty(push.Operations);
        Assert.Equal(0, push.Summary.DifferenceCount);
        Assert.False(push.HasConflicts);
        Assert.Equal(0, pull.Summary.DifferenceCount);
        Assert.Empty(pull.Differences);
    }

    /// <summary>
    /// Verifies a double-faced card kept under its front-face name, because no card data resolved it, still finds its
    /// printing on Archidekt, pushes, and reads clean: Archidekt names the card by both faces and the local entry by
    /// one, and the comparison treats them as the same card.
    /// </summary>
    [Fact]
    public async Task DoubleFacedCard_KeptUnderItsFrontFaceName_PushesAndVerifiesCleanly()
    {
        FakeArchidekt fake = CreateSmallDeck();
        _ = fake.AddCardToCatalog(2001, "Delver of Secrets // Insectile Aberration", "inr", "60");
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        Guid delverId = Guid.NewGuid();
        DeckCategory removal = local.Categories.Single(value => value.Name == "Removal");
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new AddDeckEntryChange(new DeckEntryDraft(1, "Delver of Secrets", EntryId: delverId)),
                new AssignDeckCategoryChange(delverId, removal.CategoryId, true),
            ],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.Equal(1, preview.Summary.OperationCount);
        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(1, fake.QuantityOf("Delver of Secrets // Insectile Aberration"));
        Assert.Empty(push.Operations);
        Assert.Empty(push.Differences);
        Assert.Empty(pull.Differences);
    }

    /// <summary>
    /// Verifies the push notice names the mode and the credentials, and the pull notice depends on the mode alone:
    /// a pull reads a public deck without credentials.
    /// </summary>
    [Fact]
    public void ApplyNotices_PushNamesModeAndCredentials_PullNamesOnlyTheMode()
    {
        ArchidektAuthStatus configured = new("configured", true, false, string.Empty);
        ArchidektAuthStatus missing = new("missing", false, false, string.Empty);

        Assert.Null(ArchidektApplyAvailability.PushNotice(OperationMode.Remote, configured));
        Assert.Null(ArchidektApplyAvailability.PullNotice(OperationMode.Local));
        Assert.Null(ArchidektApplyAvailability.PullNotice(OperationMode.Remote));
        Assert.Contains(
            "read-only mode",
            ArchidektApplyAvailability.PushNotice(OperationMode.ReadOnly, configured),
            StringComparison.Ordinal);
        string pull = ArchidektApplyAvailability.PullNotice(OperationMode.ReadOnly)!;
        Assert.Contains("read-only mode", pull, StringComparison.Ordinal);
        Assert.Contains("local or remote mode", pull, StringComparison.Ordinal);
        string both = ArchidektApplyAvailability.PushNotice(OperationMode.Local, missing)!;
        Assert.Contains("local mode, and Archidekt credentials are not configured", both, StringComparison.Ordinal);
        Assert.Contains("remote mode", both, StringComparison.Ordinal);
        string credentialsOnly = ArchidektApplyAvailability.PushNotice(OperationMode.Remote, missing)!;
        Assert.Contains("credentials are not configured", credentialsOnly, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "mode",
            credentialsOnly.Replace("archidekt_push_apply", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a pull preview in remote mode with no credentials still offers the pull apply tool, because the tool
    /// is registered by mode and reads public decks without credentials.
    /// </summary>
    [Fact]
    public async Task PullPreview_WithoutCredentials_StillOffersPullApplyInLocalMode()
    {
        using World world = new(OperationMode.Local, CreateSmallDeck(), credentials: false);

        ArchidektSyncPreviewView pull = Success(await world.Reads.PreviewPullAsync(
            "42",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(pull.ApplyAvailable);
        Assert.Null(pull.ApplyNotice);
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
        List<string> names = [];
        int offset = 0;
        int pages = 0;

        while (true)
        {
            OperationResult<ArchidektDeckView> result = await world.Reads.GetDeckAsync("42", offset, token);
            ArchidektDeckView page = Success(result);
            Assert.True(
                JsonLength(result) < MaximumResultCharacters,
                $"page {pages} is {JsonLength(result)} characters");
            Assert.Equal(300, page.Summary.EntryCount);
            Assert.Equal(offset, page.Offset);
            Assert.Equal(offset == 0, page.Categories.Count > 0);
            relationIds.AddRange(page.Entries.Select(value => value.ProviderRelationId));
            names.AddRange(page.Entries.Select(value => value.CardName));
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
        Assert.Equal(300, relationIds.Count);
        Assert.Equal(300, relationIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(0, 300).Select(index => $"Card Number {index:000}"), names);
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
                cancellationToken: token);
            ArchidektSyncPreviewView page = Success(result);
            Assert.True(
                JsonLength(result) < MaximumResultCharacters,
                $"page {pages.Count} is {JsonLength(result)} characters");
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
        List<int> sequences = [.. pages.SelectMany(page => page.Operations).Select(value => value.Sequence)];
        List<string> paths = [.. pages.SelectMany(page => page.Differences).Select(value => value.Path)];
        Assert.Equal(300, sequences.Count);
        Assert.Equal(300, sequences.Distinct().Count());
        Assert.Equal(sequences.Order(), sequences);
        Assert.Equal(300, paths.Count);
        Assert.Equal(300, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);

        ArchidektSyncPreviewView last = pages[^1];
        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                last.RemoteFingerprint,
                last.PreviewFingerprint),
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
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, edited.Revision, preview.RemoteFingerprint, "not-the-preview"),
            token);

        Assert.Equal("push-preview-changed", Assert.IsType<OperationConflict>(refused.Value).ReasonCode);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies the one offset rule on the deck read and the preview: an offset from 0 up to the number of rows is
    /// valid (at the number of rows the page holds no row), and a negative offset or one past the end is refused with
    /// a message that names the list and its size.
    /// </summary>
    [Fact]
    public async Task Offsets_FollowOneRule_ForTheDeckReadAndThePreview()
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();

        ArchidektDeckView atEnd = Success(await world.Reads.GetDeckAsync("42", 4, token));
        OperationResult<ArchidektDeckView> pastEnd = await world.Reads.GetDeckAsync("42", 5, token);
        OperationResult<ArchidektDeckView> negative = await world.Reads.GetDeckAsync("42", -1, token);
        ArchidektSyncPreviewView previewAtEnd = Success(await world.Reads.PreviewPushAsync(
            localDeckId,
            operationOffset: 0,
            differenceOffset: 0,
            cancellationToken: token));
        OperationResult<ArchidektSyncPreviewView> previewPast = await world.Reads.PreviewPushAsync(
            localDeckId,
            operationOffset: 1,
            cancellationToken: token);

        Assert.Empty(atEnd.Entries);
        Assert.Equal(4, atEnd.Summary.EntryCount);
        Assert.Null(atEnd.NextOffset);
        OperationInvalidInput refused = Assert.IsType<OperationInvalidInput>(pastEnd.Value);
        Assert.Equal("offset", refused.Path);
        Assert.Contains("from 0 to 4", refused.Message, StringComparison.Ordinal);
        Assert.Contains("4 entries", refused.Message, StringComparison.Ordinal);
        Assert.IsType<OperationInvalidInput>(negative.Value);
        Assert.Empty(previewAtEnd.Operations);
        OperationInvalidInput previewRefused = Assert.IsType<OperationInvalidInput>(previewPast.Value);
        Assert.Equal("operationOffset", previewRefused.Path);
        Assert.Contains("0 operations", previewRefused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a later page of a push preview is refused when the preview changed since the earlier page, and is
    /// served when the fingerprint is the same.
    /// </summary>
    [Fact]
    public async Task PushPreview_LaterPageWithAnotherFingerprint_IsRefused()
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
        ArchidektSyncPreviewView first = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        Assert.Contains("expectedPreviewFingerprint", first.Narrow, StringComparison.Ordinal);
        int nextOperation = first.NextOperationOffset ?? first.Operations.Count;

        ArchidektSyncPreviewView same = Success(await world.Reads.PreviewPushAsync(
            localDeckId,
            operationOffset: nextOperation,
            differenceOffset: first.NextDifferenceOffset ?? first.Differences.Count,
            expectedPreviewFingerprint: first.PreviewFingerprint,
            cancellationToken: token));
        DeckEntry changed = edited.Entries[0];
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            edited.Revision,
            [new UpdateDeckEntryChange(changed with { Quantity = changed.Quantity + 1 })],
            token));
        OperationResult<ArchidektSyncPreviewView> stale = await world.Reads.PreviewPushAsync(
            localDeckId,
            operationOffset: nextOperation,
            differenceOffset: first.NextDifferenceOffset ?? first.Differences.Count,
            expectedPreviewFingerprint: first.PreviewFingerprint,
            cancellationToken: token);
        ArchidektSyncPreviewView firstAgain = Success(await world.Reads.PreviewPushAsync(
            localDeckId,
            expectedPreviewFingerprint: "not-checked-on-the-first-page",
            cancellationToken: token));

        Assert.Equal(first.PreviewFingerprint, same.PreviewFingerprint);
        Assert.Equal("preview-changed", Assert.IsType<OperationConflict>(stale.Value).ReasonCode);
        Assert.NotEqual(first.PreviewFingerprint, firstAgain.PreviewFingerprint);
    }

    /// <summary>
    /// Verifies the differences of a local deck with 300 edits come in pages under the size limit, each difference
    /// exactly once and in path order.
    /// </summary>
    [Fact]
    public async Task SyncDiff_With300Changes_PagesUnderTheLimit_AndReachesEveryDifferenceOnce()
    {
        FakeArchidekt fake = CreateLargeDeck(300);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Select(value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
            token));
        List<string> paths = [];
        int pages = 0;
        int offset = 0;

        while (true)
        {
            OperationResult<ArchidektSyncDiffView> result = await world.Reads.DiffAsync(localDeckId, offset, token);
            ArchidektSyncDiffView page = Success(result);
            Assert.True(
                JsonLength(result) < MaximumResultCharacters,
                $"page {pages} is {JsonLength(result)} characters");
            Assert.Equal(300, page.DifferenceCount);
            paths.AddRange(page.Differences.Select(value => value.Path));
            pages++;
            if (page.NextDifferenceOffset is not int next)
            {
                break;
            }

            Assert.True(page.Truncated);
            Assert.Contains("differenceOffset", page.Narrow, StringComparison.Ordinal);
            offset = next;
        }

        Assert.True(pages > 1);
        Assert.Equal(300, paths.Count);
        Assert.Equal(300, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
    }

    /// <summary>
    /// Verifies the folder tree and one folder come in pages under the size limit that reach every folder record
    /// exactly once, in the order of the whole tree.
    /// </summary>
    [Theory]
    [InlineData("list", null)]
    [InlineData("get", "9")]
    public async Task FoldersGet_WithManyFolders_PagesUnderTheLimit_AndReachesEveryRecordOnce(
        string kind,
        string? folderId)
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;
        List<string> ids = [];
        int pages = 0;
        int offset = 0;
        int total;

        while (true)
        {
            OperationResult<ArchidektFoldersResult> result = await world.Reads.GetFoldersAsync(
                kind,
                folderId,
                offset,
                token);
            ArchidektFoldersResult data = Success(result);
            ArchidektFolderTreeView page = data switch
            {
                ArchidektFolderListResult list => list.Folders,
                ArchidektFolderGetResult get => get.Folder,
                _ => throw new InvalidOperationException("Unexpected folder result."),
            };
            Assert.True(
                JsonLength(result) < MaximumResultCharacters || (page.Items.Count == 1 && JsonLength(result) < 50_000),
                $"page {pages} has {page.Items.Count} records and is {JsonLength(result)} characters");
            total = page.FolderCount;
            ids.AddRange(page.Items.Select(value => value.FolderId));
            pages++;
            if (page.NextOffset is not int next)
            {
                break;
            }

            Assert.True(page.Truncated);
            Assert.Contains("offset", page.Narrow, StringComparison.Ordinal);
            offset = next;
        }

        Assert.True(pages > 1);
        Assert.Equal(total, ids.Count);
        Assert.Equal(total, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Verifies the saved deck of a snapshot with 300 cards comes in pages under the size limit that reach every entry
    /// exactly once, in card name order.
    /// </summary>
    [Fact]
    public async Task SnapshotsGet_With300Cards_PagesUnderTheLimit_AndReachesEveryEntryOnce()
    {
        FakeArchidekt fake = CreateLargeDeck(300);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        List<string> names = [];
        int pages = 0;
        int offset = 0;

        while (true)
        {
            OperationResult<ArchidektSnapshotsResult> result = await world.Reads.GetSnapshotsAsync(
                "get",
                "42",
                "77",
                offset,
                token);
            ArchidektSnapshotView snapshot = Assert.IsType<ArchidektSnapshotGetResult>(Success(result)).Snapshot;
            Assert.True(
                JsonLength(result) < MaximumResultCharacters,
                $"page {pages} is {JsonLength(result)} characters");
            Assert.Equal(300, snapshot.Deck.Summary.EntryCount);
            names.AddRange(snapshot.Deck.Entries.Select(value => value.CardName));
            pages++;
            if (snapshot.Deck.NextOffset is not int next)
            {
                break;
            }

            Assert.Contains("archidekt_snapshots_get", snapshot.Deck.Narrow, StringComparison.Ordinal);
            offset = next;
        }

        Assert.True(pages > 1);
        Assert.Equal(Enumerable.Range(0, 300).Select(index => $"Card Number {index:000}"), names);
    }

    /// <summary>
    /// Verifies a restore preview of a 700-card deck comes in pages under the size limit with the same guards on every
    /// page, and every operation and difference is reached exactly once.
    /// </summary>
    [Fact]
    public async Task SnapshotRestorePreview_With700Cards_PagesUnderTheLimit_AndReachesEveryRowOnce()
    {
        FakeArchidekt fake = CreateLargeDeck(700);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        List<ArchidektSnapshotRestorePreviewView> pages = [];
        int operationOffset = 0;
        int differenceOffset = 0;

        while (true)
        {
            OperationResult<ArchidektSnapshotRestorePreviewView> result = await world.Reads.PreviewSnapshotRestoreAsync(
                "42",
                "77",
                operationOffset,
                differenceOffset,
                pages.Count == 0 ? null : pages[0].PreviewFingerprint,
                token);
            ArchidektSnapshotRestorePreviewView page = Success(result);
            Assert.True(
                JsonLength(result) < MaximumResultCharacters,
                $"page {pages.Count} is {JsonLength(result)} characters");
            pages.Add(page);
            if (page.Truncated is null)
            {
                break;
            }

            Assert.Contains("expectedPreviewFingerprint", page.Narrow, StringComparison.Ordinal);
            operationOffset = page.NextOperationOffset ?? page.OperationOffset + page.Operations.Count;
            differenceOffset = page.NextDifferenceOffset ?? page.DifferenceOffset + page.Differences.Count;
        }

        List<int> sequences = [.. pages.SelectMany(page => page.Operations).Select(value => value.Sequence)];
        List<string> paths = [.. pages.SelectMany(page => page.Differences).Select(value => value.Path)];
        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.Equal(pages[0].PreviewFingerprint, page.PreviewFingerprint));
        Assert.Equal(pages[0].OperationCount, sequences.Count);
        Assert.Equal(sequences.Count, sequences.Distinct().Count());
        Assert.Equal(sequences.Order(), sequences);
        Assert.Equal(pages[0].DifferenceCount, paths.Count);
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
        Assert.True(sequences.Count >= 700);
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
        internal World(OperationMode mode, FakeArchidekt fake, bool credentials = true)
        {
            Fake = fake;
            ArchidektOptions options = ArchidektOptions.CreateDefault(
                credentials ? "user" : null,
                credentials ? "secret" : null) with
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                MinimumRequestInterval = TimeSpan.Zero,
                MaximumRequestsPerWindow = 100_000,
                MaximumRequestsPerOperation = 2_000,
            };
            service = new ArchidektService(
                new ArchidektSession(
                    new HttpClient(fake) { BaseAddress = options.BaseAddress },
                    ownsHttpClient: true,
                    options),
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
