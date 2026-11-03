using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;

namespace MtgMcp.App.Tests;

/// <summary>
/// Holds the tests of the change codes of the pull and push applies and of the cursors of the paged reads that follow
/// a deck, a local revision, or a preview.
/// </summary>
public sealed partial class ArchidektWorkflowTests
{
    /// <summary>
    /// Verifies a pull apply that names a local revision that is no longer current is refused as a stale deck revision
    /// with the current revision, before the discard option or the fingerprints are looked at.
    /// </summary>
    [Fact]
    public async Task PullApply_WithAStaleRevision_IsStaleDeckRevisionWithTheCurrentRevision()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));

        OperationResult<ArchidektApplyResult> refused = await world.LocalWrites.ApplyPullAsync(
            "42",
            "wrong",
            "wrong",
            localDeckId,
            local.Revision + 5,
            cancellationToken: token);

        OperationConflict conflict = Assert.IsType<OperationConflict>(refused.Value);
        Assert.Equal("stale-deck-revision", conflict.ReasonCode);
        Assert.Equal(local.Revision, conflict.Current?.Revision);
    }

    /// <summary>
    /// Verifies a pull apply and a push apply that name an Archidekt fingerprint that is no longer current are refused
    /// as a changed Archidekt deck with the current fingerprint, and nothing is written.
    /// </summary>
    [Fact]
    public async Task PullAndPushApply_WithAnotherRemoteFingerprint_AreRemoteDeckChanged()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [new AddDeckEntryChange(new DeckEntryDraft(1, "Shock"))],
            token));
        ArchidektSyncPreviewView pullPreview = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            cancellationToken: token));
        ArchidektSyncPreviewView pushPreview = Success(await world.Reads.PreviewPushAsync(
            localDeckId,
            cancellationToken: token));

        OperationResult<ArchidektApplyResult> pull = await world.LocalWrites.ApplyPullAsync(
            "42",
            "another-fingerprint",
            pullPreview.PreviewFingerprint,
            localDeckId,
            edited.Revision,
            cancellationToken: token);
        OperationResult<ArchidektApplyResult> push = await world.Writes.ApplyPushAsync(
            localDeckId,
            edited.Revision,
            "another-fingerprint",
            pushPreview.PreviewFingerprint,
            cancellationToken: token);

        foreach (OperationConflict conflict in new[]
        {
            Assert.IsType<OperationConflict>(pull.Value),
            Assert.IsType<OperationConflict>(push.Value),
        })
        {
            Assert.Equal("remote-deck-changed", conflict.ReasonCode);
            Assert.Equal(pushPreview.RemoteFingerprint, conflict.Current?.RemoteFingerprint);
        }

        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies a diff cursor is served while the local deck is unchanged and refused as stale, with the current
    /// revision, after the local deck changed.
    /// </summary>
    [Fact]
    public async Task SyncDiff_CursorAfterALocalEdit_IsStaleWithTheCurrentRevision()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(300));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Select(value => new UpdateDeckEntryChange(value with { Quantity = value.Quantity + 1 }))],
            token));
        ArchidektSyncDiffView first = Success(await world.Reads.DiffAsync(localDeckId, pageSize: 10, cancellationToken: token));
        string cursor = first.NextCursor!;

        ArchidektSyncDiffView second = Success(await world.Reads.DiffAsync(
            localDeckId,
            cursor,
            pageSize: 10,
            cancellationToken: token));
        DeckEntry changed = edited.Entries[0];
        DeckDocument after = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            edited.Revision,
            [new UpdateDeckEntryChange(changed with { Quantity = changed.Quantity + 1 })],
            token));
        OperationResult<ArchidektSyncDiffView> stale = await world.Reads.DiffAsync(
            localDeckId,
            cursor,
            pageSize: 10,
            cancellationToken: token);

        Assert.Equal(10, first.Differences.Count);
        Assert.Equal(10, second.Differences.Count);
        OperationConflict conflict = Assert.IsType<OperationConflict>(stale.Value);
        Assert.Equal("cursor-stale", conflict.ReasonCode);
        Assert.Equal(after.Revision, conflict.Current?.Revision);
    }

    /// <summary>
    /// Verifies a restore preview cursor is refused as stale, with the current preview fingerprint, after the
    /// Archidekt deck changed.
    /// </summary>
    [Fact]
    public async Task SnapshotRestorePreview_CursorAfterTheDeckChanged_IsStaleWithTheCurrentPreview()
    {
        FakeArchidekt fake = CreateLargeDeck(700);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchidektSnapshotRestorePreviewView first = Success(
            await world.Reads.PreviewSnapshotRestoreAsync("42", "77", cancellationToken: token));
        string cursor = first.NextCursor!;
        fake.DeckName = "Renamed after the first page";

        OperationResult<ArchidektSnapshotRestorePreviewView> stale = await world.Reads.PreviewSnapshotRestoreAsync(
            "42",
            "77",
            cursor,
            cancellationToken: token);

        OperationConflict conflict = Assert.IsType<OperationConflict>(stale.Value);
        Assert.Equal("cursor-stale", conflict.ReasonCode);
        Assert.NotNull(conflict.Current?.PreviewFingerprint);
        Assert.NotEqual(first.PreviewFingerprint, conflict.Current?.PreviewFingerprint);
    }
}
