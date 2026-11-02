using System.Net;
using System.Text.Json.Nodes;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Holds the tests of a push that did not finish: what the next preview offers, which links the baseline keeps, and
/// an edit made on Archidekt while the push ran.
/// </summary>
public sealed partial class ArchidektWorkflowTests
{
    /// <summary>
    /// Verifies a partial push in which a metadata, category, update, or remove operation fails with a 404 keeps the
    /// statuses of the operations before it, records what landed, and leaves exactly the operations that did not land
    /// for the next preview; that push then finishes with nothing lost and nothing doubled.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ApplyPush_WhenOneOperationFails_OffersExactlyWhatDidNotLand(int failing)
    {
        FakeArchidekt fake = CreateDeckForNameOnlyAdds();
        fake.AddEntry(fake.AddCardToCatalog(601, "Rock"), 1, "Removal");
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new UpdateDeckMetadataChange("Renamed deck", local.Description, local.Format),
                new AddDeckCategoryChange(new DeckCategoryDraft("Ramp", SortOrder: 50)),
                new UpdateDeckEntryChange(local.Entries.Single(value => value.CardName == "Bolt") with { Quantity = 3 }),
                new RemoveDeckEntryChange(local.Entries.Single(value => value.CardName == "Rock").EntryId),
            ],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        Assert.Equal(
            ["metadata-update", "category-create", "entry-update", "entry-remove"],
            preview.Operations.Select(value => value.Kind));
        int writes = 0;
        fake.FailWrite = (_, _) => writes++ == failing ? HttpStatusCode.NotFound : null;

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, edited.Revision, preview.RemoteFingerprint, preview.PreviewFingerprint),
            token));
        fake.FailWrite = null;
        ArchidektSyncPreviewView next = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal("partial", applied.Outcome);
        Assert.Equal(
            [
                .. Enumerable.Repeat("applied", failing),
                "unknown",
                .. Enumerable.Repeat("not-attempted", 3 - failing),
            ],
            applied.Operations.Select(value => value.Status));
        Assert.NotNull(applied.LocalRevision);
        Assert.True(next.ApplyAvailable, next.ApplyNotice);
        Assert.Equal(
            preview.Operations.Skip(failing).Select(value => (value.Kind, value.Subject)),
            next.Operations.Select(value => (value.Kind, value.Subject)));
        Assert.DoesNotContain(next.Differences, value => value.State.StartsWith("remote-", StringComparison.Ordinal));

        ArchidektApplyResult finished = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                Assert.IsType<long>(applied.LocalRevision),
                next.RemoteFingerprint,
                next.PreviewFingerprint),
            token));
        ArchidektSyncPreviewView clean = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal("applied", finished.Outcome);
        Assert.Equal("Renamed deck", fake.DeckName);
        Assert.True(fake.IsIncludedInDeck("Ramp"));
        Assert.Equal((3, 0), (fake.QuantityOf("Bolt"), fake.QuantityOf("Rock")));
        Assert.Empty(clean.Operations);
        Assert.Empty(clean.Differences);
    }

    /// <summary>
    /// Verifies a partial push on a deck pulled by 0.13.0 writes the corrected zone of every stale entry, including
    /// one whose own update did not land, so the next preview offers that update with no zone difference.
    /// </summary>
    [Fact]
    public async Task ApplyPush_PartialOnADeckPulledByTheEarlierVersion_CorrectsEveryStaleZone()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithExcludedCategories());
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocument local = await StoreAsStoredByTheEarlierVersionAsync(world);
        DeckEntry adept = local.Entries.Single(value => value.CardName == "Osteomancer Adept");
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            local.DeckId,
            local.Revision,
            [
                new UpdateDeckEntryChange(local.Entries.Single(value => value.CardName == "Filler 000") with
                {
                    Quantity = 2,
                }),
                new UpdateDeckEntryChange(adept with { Quantity = 2 }),
            ],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(local.DeckId, cancellationToken: token));
        world.Fake.FailWrite = (_, body) => SendsCard(body, 3004) ? HttpStatusCode.NotFound : null;

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(local.DeckId, edited.Revision, preview.RemoteFingerprint, preview.PreviewFingerprint),
            token));
        world.Fake.FailWrite = null;
        DeckDocument after = Success(await world.Store.GetAsync(local.DeckId, token));
        ArchidektSyncPreviewView next = Success(
            await world.Reads.PreviewPushAsync(local.DeckId, cancellationToken: token));

        Assert.Equal("partial", applied.Outcome);
        Assert.Equal(
            "unknown",
            applied.Operations.Single(value => value.Subject == "Osteomancer Adept").Status);
        Assert.Equal(3, after.Entries.Count(value => value.Zone == "maybeboard"));
        Assert.Equal("maybeboard", after.Entries.Single(value => value.EntryId == adept.EntryId).Zone);
        Assert.True(next.ApplyAvailable, next.ApplyNotice);
        Assert.Equal(
            applied.Operations.Where(value => value.Status != "applied").Select(value => (value.Kind, value.Subject)),
            next.Operations.Select(value => (value.Kind, value.Subject)));
        Assert.All(
            next.Differences.Cast<ArchidektDifference>(),
            value => Assert.Equal(ZoneOf(value.RemoteValue), ZoneOf(value.LocalValue)));
    }

    /// <summary>
    /// Verifies two new rows of the same card, of which Archidekt refuses the first and takes the second, leave the
    /// landed row linked, so the next preview adds only the refused row and nothing is removed or doubled.
    /// </summary>
    [Fact]
    public async Task ApplyPush_WithTwoRowsOfOneCardAndOneRefused_LinksTheLandedRow()
    {
        FakeArchidekt fake = CreateDeckForNameOnlyAdds();
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(local, "Side Card 01", "Side Card 01"),
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        int cardRequests = 0;
        fake.FailWrite = (request, _) =>
            request.Contains("modifyCards", StringComparison.Ordinal) && ++cardRequests <= 2
                ? HttpStatusCode.BadRequest
                : null;

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, edited.Revision, preview.RemoteFingerprint, preview.PreviewFingerprint),
            token));
        fake.FailWrite = null;
        ArchidektSyncPreviewView next = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal("partial", applied.Outcome);
        Assert.Equal(["not-applied", "applied"], applied.Operations.Select(value => value.Status));
        Assert.Null(applied.UnlinkedLocalIds);
        Assert.Equal(1, fake.QuantityOf("Side Card 01"));
        Assert.True(next.ApplyAvailable, next.ApplyNotice);
        ArchidektRemoteOperation only = Assert.Single(next.Operations);
        Assert.Equal(("entry-add", "Side Card 01"), (only.Kind, only.Subject));

        ArchidektApplyResult finished = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                Assert.IsType<long>(applied.LocalRevision),
                next.RemoteFingerprint,
                next.PreviewFingerprint),
            token));

        Assert.Equal("applied", finished.Outcome);
        Assert.Equal(2, fake.QuantityOf("Side Card 01"));
    }

    /// <summary>
    /// Verifies a rename of a category an earlier push created, which did not land, keeps the category linked by its
    /// Archidekt ID, so the next preview offers only the rename (a new category in place of the old one), and applying
    /// it keeps the category's flags.
    /// </summary>
    [Fact]
    public async Task ApplyPush_WithACategoryRenameThatDidNotLand_KeepsTheCategoryLink()
    {
        FakeArchidekt fake = CreateDeckForNameOnlyAdds();
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument pulled = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument created = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            pulled.Revision,
            [new AddDeckCategoryChange(new DeckCategoryDraft("Maybeboard", SortOrder: 50))],
            token));
        ArchidektSyncPreviewView first = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektApplyResult pushed = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, created.Revision, first.RemoteFingerprint, first.PreviewFingerprint),
            token));
        Assert.Equal("applied", pushed.Outcome);
        Assert.False(fake.IsIncludedInDeck("Maybeboard"));
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new UpdateDeckMetadataChange("Renamed deck", local.Description, local.Format),
                new UpdateDeckCategoryChange(local.Categories.Single(value => value.Name == "Maybeboard") with
                {
                    Name = "Considering",
                }),
            ],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        fake.FailWrite = (request, _) =>
            request.Contains("createCategory", StringComparison.Ordinal) ? HttpStatusCode.NotFound : null;

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, edited.Revision, preview.RemoteFingerprint, preview.PreviewFingerprint),
            token));
        fake.FailWrite = null;
        ArchidektSyncPreviewView next = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal("partial", applied.Outcome);
        Assert.Equal(["applied", "unknown", "not-attempted"], applied.Operations.Select(value => value.Status));
        Assert.True(next.ApplyAvailable, next.ApplyNotice);
        Assert.Equal(
            [("category-create", "Considering"), ("category-delete", "Maybeboard")],
            next.Operations.Select(value => (value.Kind, value.Subject)));

        ArchidektApplyResult finished = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                Assert.IsType<long>(applied.LocalRevision),
                next.RemoteFingerprint,
                next.PreviewFingerprint),
            token));

        Assert.Equal("applied", finished.Outcome);
        Assert.False(fake.IsIncludedInDeck("Considering"));
    }

    /// <summary>
    /// Verifies an edit made on Archidekt to a row the push did not touch, while a partial push or a push whose
    /// verification differs ran, keeps the old baseline and the local deck, and says Archidekt changed during the push,
    /// so the next preview shows the Archidekt changes instead of offering to revert the edit.
    /// </summary>
    [Theory]
    [InlineData(true, "partial")]
    [InlineData(false, "verification-mismatch")]
    public async Task ApplyPush_WithAnArchidektEditDuringThePush_KeepsTheOldBaseline(bool refuse, string outcome)
    {
        FakeArchidekt fake = CreateDeckForNameOnlyAdds();
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(local, "Side Card 01", "Side Card 02", "Side Card 03"),
            token));
        if (refuse)
        {
            fake.RefusedCardIds.UnionWith([5004, 5005]);
        }

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        fake.BeforeRead = path =>
        {
            if (path == "api/decks/42/" && fake.Mutations > 0)
            {
                fake.SetCategories("Bolt", "Sideboard");
            }
        };

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(localDeckId, edited.Revision, preview.RemoteFingerprint, preview.PreviewFingerprint),
            token));
        fake.BeforeRead = null;
        ArchidektSyncPreviewView next = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal(outcome, applied.Outcome);
        Assert.Null(applied.LocalRevision);
        Assert.EndsWith(ArchidektSynchronizationContext.ChangedDuringPushNotice, applied.Notice, StringComparison.Ordinal);
        Assert.Equal(edited.Revision, Success(await world.Store.GetAsync(localDeckId, token)).Revision);
        Assert.False(next.ApplyAvailable);
        Assert.Contains(next.Differences, value => value.State == "remote-changed");
        Assert.Equal(refuse ? 2 : 3, next.Differences.Count(value => value.State == "remote-added"));
    }

    /// <summary>
    /// Tells whether a card request body sends a card with the given Archidekt card id.
    /// </summary>
    private static bool SendsCard(string body, int cardId)
    {
        return JsonNode.Parse(body)?["cards"] is JsonArray cards &&
            cards.Any(card => card?["cardid"]?.GetValue<int>() == cardId);
    }

    /// <summary>
    /// Reads the zone of a difference value, or null when the side holds no row.
    /// </summary>
    private static string? ZoneOf(string? value)
    {
        return value is null ? null : JsonNode.Parse(value)?["zone"]?.GetValue<string>();
    }
}
