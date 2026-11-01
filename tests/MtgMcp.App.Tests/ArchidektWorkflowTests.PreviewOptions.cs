using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Holds the tests of the concise difference format, the options a pull and a push apply must repeat, and the request
/// limit of a push preview.
/// </summary>
public sealed partial class ArchidektWorkflowTests
{
    /// <summary>
    /// Verifies an 18-edit push of a 347-entry deck reads in a concise result at most a third the size of the full one,
    /// whose page already holds fewer rows, with the same guards, summary, operations, and apply availability, and
    /// that the diff tool does the same.
    /// </summary>
    [Fact]
    public async Task ConciseFormat_OfAnEighteenEditPush_IsAThirdOfTheFullSizeWithTheSameGuards()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(347));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        await EditEighteenEntriesAsync(world, localDeckId, token);

        OperationResult<ArchidektSyncPreviewView> fullResult = await world.Reads.PreviewPushAsync(
            localDeckId,
            cancellationToken: token);
        OperationResult<ArchidektSyncPreviewView> conciseResult = await world.Reads.PreviewPushAsync(
            localDeckId,
            responseFormat: "concise",
            cancellationToken: token);
        ArchidektSyncPreviewView full = Success(fullResult);
        ArchidektSyncPreviewView concise = Success(conciseResult);

        TestContext.Current.SendDiagnosticMessage(
            $"push preview: full {JsonLength(fullResult)} characters, concise {JsonLength(conciseResult)} characters");
        Assert.Equal(full.Summary.DifferenceCount, concise.Differences.Count);
        Assert.Equal(18, concise.Differences.Count(value => value.Path.StartsWith("/entries/", StringComparison.Ordinal)));
        Assert.Null(concise.Truncated);
        Assert.True(JsonLength(conciseResult) * 3 <= JsonLength(fullResult));
        Assert.Equal(full.PreviewFingerprint, concise.PreviewFingerprint);
        Assert.Equal(full.RemoteFingerprint, concise.RemoteFingerprint);
        Assert.Equal(full.ContentFingerprint, concise.ContentFingerprint);
        Assert.True(full.ApplyAvailable);
        Assert.Equal(full.ApplyAvailable, concise.ApplyAvailable);
        Assert.Equal(full.ApplyNotice, concise.ApplyNotice);
        Assert.Equal(full.Summary.DifferencesByState, concise.Summary.DifferencesByState);
        Assert.Equal(full.Summary.OperationsByKind, concise.Summary.OperationsByKind);
        Assert.Equal(full.Operations, concise.Operations);
        Assert.Equal(
            full.Differences.Select(value => (value.Path, value.State)),
            concise.Differences.Take(full.Differences.Count).Select(value => (value.Path, value.State)));
        Assert.All(full.Differences, value => Assert.IsType<ArchidektDifference>(value));
        Assert.All(concise.Differences, value => Assert.IsType<ArchidektConciseDifference>(value));
        Assert.All(
            concise.Differences.Cast<ArchidektConciseDifference>()
                .Where(value => value.State == "local-changed" && value.Path.StartsWith("/entries/", StringComparison.Ordinal)),
            value => Assert.Equal("quantity", Assert.Single(value.Fields).Name));

        OperationResult<ArchidektSyncDiffView> fullDiff = await world.Reads.DiffAsync(
            localDeckId,
            cancellationToken: token);
        OperationResult<ArchidektSyncDiffView> conciseDiff = await world.Reads.DiffAsync(
            localDeckId,
            responseFormat: "concise",
            cancellationToken: token);
        Assert.True(JsonLength(conciseDiff) * 3 <= JsonLength(fullDiff));
        Assert.Equal(Success(fullDiff).DifferenceCount, Success(conciseDiff).DifferenceCount);
        Assert.Equal(Success(fullDiff).HasConflicts, Success(conciseDiff).HasConflicts);
    }

    /// <summary>
    /// Verifies a concise preview of a push that needs several pages holds more rows on each page, reaches every
    /// operation and difference once, and keeps the same fingerprint on every page.
    /// </summary>
    [Fact]
    public async Task ConciseFormat_PagesMeasureTheFormatReturned()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(300));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Select(value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
            token));

        List<ArchidektSyncPreviewView> fullPages = await ReadEveryPreviewPageAsync(world, localDeckId, "full", token);
        List<ArchidektSyncPreviewView> concisePages = await ReadEveryPreviewPageAsync(
            world,
            localDeckId,
            "concise",
            token);

        Assert.True(concisePages.Count < fullPages.Count);
        Assert.All(concisePages, page => Assert.Equal(fullPages[0].PreviewFingerprint, page.PreviewFingerprint));
        List<string> paths = [.. concisePages.SelectMany(page => page.Differences).Select(value => value.Path)];
        Assert.Equal(300, paths.Count);
        Assert.Equal(300, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(paths.Order(StringComparer.Ordinal), paths);
        Assert.Equal(
            300,
            concisePages.SelectMany(page => page.Operations).Select(value => value.Sequence).Distinct().Count());
        Assert.All(concisePages.Take(concisePages.Count - 1), page => Assert.Contains(
            "responseFormat \"concise\"",
            page.Narrow,
            StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a responseFormat other than full or concise is refused with the argument name, by the diff and both
    /// previews.
    /// </summary>
    [Fact]
    public async Task ResponseFormat_OfTheDifferenceTools_RefusesAnythingButFullOrConcise()
    {
        using World world = new(OperationMode.Remote, CreateSmallDeck());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();

        OperationResult<ArchidektSyncDiffView> diff = await world.Reads.DiffAsync(
            localDeckId,
            responseFormat: "huge",
            cancellationToken: token);
        OperationResult<ArchidektSyncPreviewView> pull = await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            responseFormat: "tiny",
            cancellationToken: token);
        OperationResult<ArchidektSyncPreviewView> push = await world.Reads.PreviewPushAsync(
            localDeckId,
            responseFormat: "",
            cancellationToken: token);

        foreach (OperationInvalidInput invalid in new[]
        {
            Assert.IsType<OperationInvalidInput>(diff.Value),
            Assert.IsType<OperationInvalidInput>(pull.Value),
            Assert.IsType<OperationInvalidInput>(push.Value),
        })
        {
            Assert.Equal("invalid-response-format", invalid.ReasonCode);
            Assert.Equal("responseFormat", invalid.Path);
        }
    }

    /// <summary>
    /// Verifies a pull apply that leaves out the discardLocalChanges its preview used is refused with a message that
    /// names the option and the value, and the same apply with the value goes through.
    /// </summary>
    [Fact]
    public async Task PullApply_WithoutTheDiscardTheDeckPreviewUsed_NamesTheOptionAndItsValue()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry bolt = local.Entries.Single(value => value.CardName == "Bolt");
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [new UpdateDeckEntryChange(bolt with { Quantity = 4 })],
            token));
        ArchidektSyncPreviewView preview = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            discardLocalChanges: true,
            cancellationToken: token));

        OperationResult<ArchidektApplyResult> refused = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(refused.Value);
        Assert.Equal("pull-discard-option-mismatch", invalid.ReasonCode);
        Assert.Equal("discardLocalChanges", invalid.Path);
        Assert.Contains("preview used discardLocalChanges true", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("Pass discardLocalChanges: true", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(edited.Revision, Success(await world.Store.GetAsync(localDeckId, token)).Revision);

        ArchidektApplyResult applied = Success(await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint,
                DiscardLocalChanges: true),
            token));
        Assert.Equal("applied", applied.Outcome);
    }

    /// <summary>
    /// Verifies a pull apply that passes discardLocalChanges when its preview did not is refused with the value the
    /// preview used, and a fingerprint that matches neither value keeps the generic refusal.
    /// </summary>
    [Fact]
    public async Task PullApply_WithADiscardThePreviewDidNotUse_NamesThePreviewValue()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        ArchidektSyncPreviewView preview = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            cancellationToken: token));

        OperationResult<ArchidektApplyResult> refused = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                local.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint,
                DiscardLocalChanges: true),
            token);
        OperationResult<ArchidektApplyResult> unrelated = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                local.Revision,
                preview.RemoteFingerprint,
                "not-a-fingerprint",
                DiscardLocalChanges: true),
            token);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(refused.Value);
        Assert.Equal("pull-discard-option-mismatch", invalid.ReasonCode);
        Assert.Contains("preview used discardLocalChanges false", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("Pass discardLocalChanges: false", invalid.Message, StringComparison.Ordinal);
        Assert.Equal("pull-preview-changed", Assert.IsType<OperationConflict>(unrelated.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a pull apply that needs no extra option reads the Archidekt deck once, so the check of the other
    /// discardLocalChanges value sends no provider request.
    /// </summary>
    [Fact]
    public async Task PullApply_WithAMismatchedOption_SendsNoSecondProviderRead()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        ArchidektSyncPreviewView preview = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            cancellationToken: token));
        int before = world.Fake.Requests.Count;

        _ = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                local.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint,
                DiscardLocalChanges: true),
            token);

        Assert.Equal(1, world.Fake.Requests.Count - before);
    }

    /// <summary>
    /// Verifies a pull preview names discardLocalChanges for its apply when it used it, and a push preview names
    /// confirmRemovals when its apply needs it; a preview whose apply needs no option names none.
    /// </summary>
    [Fact]
    public async Task PreviewResults_NameTheOptionsTheirApplyMustRepeat()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(172));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Skip(79).Select(value => new RemoveDeckEntryChange(value.EntryId))],
            token));

        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView discard = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            discardLocalChanges: true,
            cancellationToken: token));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView confirmed = Success(
            await world.Reads.PreviewPushAsync(localDeckId, confirmRemovals: 93, cancellationToken: token));

        Assert.Null(pull.ApplyOptions);
        Assert.Contains("discardLocalChanges: true", discard.ApplyOptions, StringComparison.Ordinal);
        Assert.Contains("archidekt_pull_apply", discard.ApplyOptions, StringComparison.Ordinal);
        Assert.Contains("confirmRemovals: 93", push.ApplyOptions, StringComparison.Ordinal);
        Assert.Contains("archidekt_push_apply", push.ApplyOptions, StringComparison.Ordinal);
        Assert.Equal(push.ApplyOptions, confirmed.ApplyOptions);
        Assert.True(confirmed.ApplyAvailable);
    }

    /// <summary>
    /// Verifies a small push preview names no option for its apply.
    /// </summary>
    [Fact]
    public async Task PushPreview_WithFewRemovals_NamesNoOptionForItsApply()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(100));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        await EditEntriesAsync(world, localDeckId, count: 3, token);

        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Null(push.ApplyOptions);
    }

    /// <summary>
    /// Verifies the refusal of a push apply without its confirmRemovals still names the option, and says it by path.
    /// </summary>
    [Fact]
    public async Task PushApply_WithoutConfirmRemovals_KeepsNamingTheOption()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(172));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Skip(79).Select(value => new RemoveDeckEntryChange(value.EntryId))],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(refused.Value);
        Assert.Equal("push-removals-unconfirmed", invalid.ReasonCode);
        Assert.Equal("confirmRemovals", invalid.Path);
        Assert.Contains("confirmRemovals: 93", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies a push preview over the request limit says the limit, the predicted count, how many operations fit, and
    /// that the push can be split, and that the summary of any preview shows the prediction beside the limit.
    /// </summary>
    [Fact]
    public async Task PushPreview_OverTheRequestLimit_NamesTheLimitTheCountTheOperationsThatFitAndTheSplit()
    {
        const int limit = 60;
        using World world = new(OperationMode.Remote, CreateLargeDeck(100), maximumRequestsPerOperation: limit);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        await EditEntriesAsync(world, localDeckId, count: 3, token);
        ArchidektSyncPreviewView under = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        _ = await EditEntriesAsync(world, localDeckId, count: 53, token);

        ArchidektSyncPreviewView over = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.True(under.ApplyAvailable);
        Assert.Equal(limit, under.ProviderRequestLimit);
        Assert.InRange(under.PredictedProviderRequests, 1, limit);
        Assert.Equal(limit, over.ProviderRequestLimit);
        Assert.True(over.PredictedProviderRequests > limit);
        Assert.False(over.ApplyAvailable);
        int operations = over.Summary.OperationCount;
        int fit = limit - (over.PredictedProviderRequests - operations);
        Assert.Contains($"limit of {limit}", over.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains(
            $"needs about {over.PredictedProviderRequests} provider requests",
            over.ApplyNotice,
            StringComparison.Ordinal);
        Assert.Contains($"About {fit} of its {operations} operations fit", over.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("can be split: apply part of the local changes first", over.ApplyNotice, StringComparison.Ordinal);
        Assert.Null(pull.ProviderRequestLimit);
    }

    /// <summary>
    /// Verifies the apply of a push over the request limit is refused with the same reason code, sending nothing.
    /// </summary>
    [Fact]
    public async Task PushApply_OverTheRequestLimit_IsRefusedWithTheSplitAdviceAndSendsNothing()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(100), maximumRequestsPerOperation: 60);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument edited = await EditEntriesAsync(world, localDeckId, count: 50, token);
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(refused.Value);
        Assert.Equal("request-limit-exceeded", invalid.ReasonCode);
        Assert.Contains("limit of 60", invalid.Message, StringComparison.Ordinal);
        Assert.Contains("operations fit", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Changes the quantity of the first entries of a local deck and returns the edited deck.
    /// </summary>
    private static async Task<DeckDocument> EditEntriesAsync(
        World world,
        Guid localDeckId,
        int count,
        CancellationToken token)
    {
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        return Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Take(count).Select(value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
            token));
    }

    /// <summary>
    /// Makes 18 edits to a local deck: 16 quantity changes and 2 removals.
    /// </summary>
    private static async Task EditEighteenEntriesAsync(World world, Guid localDeckId, CancellationToken token)
    {
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        List<DeckChange> changes =
        [
            .. local.Entries.Take(16).Select(value => new UpdateDeckEntryChange(value with { Quantity = 3 })),
            .. local.Entries.Skip(16).Take(2).Select(value => new RemoveDeckEntryChange(value.EntryId)),
        ];
        _ = Success(await world.Store.ApplyChangesAsync(localDeckId, local.Revision, changes, token));
    }

    /// <summary>
    /// Reads every page of a push preview in one response format.
    /// </summary>
    private static async Task<List<ArchidektSyncPreviewView>> ReadEveryPreviewPageAsync(
        World world,
        Guid localDeckId,
        string responseFormat,
        CancellationToken token)
    {
        List<ArchidektSyncPreviewView> pages = [];
        int operationOffset = 0;
        int differenceOffset = 0;
        while (true)
        {
            ArchidektSyncPreviewView page = Success(await world.Reads.PreviewPushAsync(
                localDeckId,
                operationOffset,
                differenceOffset,
                responseFormat: responseFormat,
                cancellationToken: token));
            pages.Add(page);
            if (page.Truncated is null)
            {
                return pages;
            }

            operationOffset = Assert.NotNull(page.NextOperationOffset);
            differenceOffset = Assert.NotNull(page.NextDifferenceOffset);
        }
    }
}
