using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.App.Hosting;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Core.Tests.Recorded;
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
    /// one, and the comparison treats them as the same card. A read-only query of the real Archidekt card search on
    /// 2026-10-29 showed both facts (see <see cref="FakeArchidekt"/>): the name search for the front face finds the
    /// card, and the card comes back named by both faces.
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
    /// Verifies a deck shaped like the real deck 22959335 reads the way Archidekt counts it: entries whose first
    /// category is not included in the deck are in the maybeboard, the other main entries and the commander make 100
    /// cards, a card under an excluded category and also a second entry under an included one stays in the deck, and a
    /// pull followed by a push preview gives no operation.
    /// </summary>
    [Fact]
    public async Task DeckLikeTheRealDeck_ExcludedFirstCategoriesAreMaybeboard_AndPullThenPushIsClean()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithExcludedCategories());
        CancellationToken token = TestContext.Current.CancellationToken;

        List<ArchidektFullDeckEntryView> entries = await ReadEveryEntryAsync(world, token);
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        List<ArchidektFullDeckEntryView> outOfDeck = [.. entries.Where(value => value.Zone == "maybeboard")];
        Assert.Equal(
            ["Bloodthirsty Blade", "Osteomancer Adept", "Revival // Revenge"],
            outOfDeck.Select(value => value.CardName).Order(StringComparer.Ordinal));
        Assert.All(outOfDeck, value => Assert.False(value.InDeck));
        Assert.All(entries.Where(value => value.Zone != "maybeboard"), value => Assert.True(value.InDeck));
        Assert.Equal(100, entries.Where(value => value.Zone is "main" or "commander").Sum(value => value.Quantity));
        ArchidektFullDeckEntryView blade = Assert.Single(
            entries,
            value => value.CardName == "Bloodthirsty Blade" && value.Zone == "main");
        Assert.Equal("Removal", blade.PrimaryCategoryName);
        Assert.Equal(3, local.Entries.Count(value => value.Zone == "maybeboard"));
        Assert.Equal(
            100,
            local.Entries.Where(value => value.Zone is "main" or "commander").Sum(value => value.Quantity));
        Assert.Empty(push.Operations);
        Assert.Empty(push.Differences);
        Assert.Empty(pull.Differences);
        Assert.DoesNotContain(
            local.Categories,
            value => value.Name.Equals("Maybeboard", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies a deck pulled by 0.13.0 keeps working: its stored baseline and local entries put the three entries
    /// whose first category is excluded in the main deck, and neither a diff nor a push preview shows a difference, an
    /// operation or a zone contradiction because of it, while a pull preview shows the move to the maybeboard for
    /// exactly those entries; after that pull the local deck matches Archidekt, a push preview is clean, and the
    /// entries did not get the Maybeboard category of Archidekt.
    /// </summary>
    [Fact]
    public async Task DeckPulledByTheEarlierVersion_ShowsNoFalseDifferenceAndTheNextPullMovesTheEntries()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithExcludedCategories());
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocument local = await StoreAsStoredByTheEarlierVersionAsync(world);

        ArchidektSyncDiffView diff = Success(await world.Reads.DiffAsync(local.DeckId, cancellationToken: token));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(local.DeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", local.DeckId, cancellationToken: token));

        Assert.Equal(0, diff.DifferenceCount);
        Assert.Empty(push.Operations);
        Assert.Equal(0, push.Summary.DifferenceCount);
        Assert.False(push.HasConflicts);
        Assert.Equal(3, pull.Differences.Count);
        Assert.All(pull.Differences, value => Assert.Equal("remote-changed", value.State));
        Assert.Equal(
            ["Bloodthirsty Blade", "Osteomancer Adept", "Revival // Revenge"],
            pull.Differences
                .Select(value => JsonNode.Parse(value.RemoteValue!)!["cardName"]!.GetValue<string>())
                .Order(StringComparer.Ordinal));
        Assert.All(
            pull.Differences,
            value => Assert.Equal("maybeboard", JsonNode.Parse(value.RemoteValue!)!["zone"]!.GetValue<string>()));

        ArchidektApplyResult applied = Success(await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                local.DeckId,
                local.Revision,
                pull.RemoteFingerprint,
                pull.PreviewFingerprint),
            token));
        DeckDocument after = Success(await world.Store.GetAsync(local.DeckId, token));
        ArchidektSyncPreviewView pushAfter = Success(
            await world.Reads.PreviewPushAsync(local.DeckId, cancellationToken: token));

        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(3, after.Entries.Count(value => value.Zone == "maybeboard"));
        Assert.Empty(pushAfter.Operations);
        Assert.Empty(pushAfter.Differences);
        Assert.DoesNotContain(
            after.Categories,
            value => value.Name.Equals("Maybeboard", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Stores the fake deck the way 0.13.0 pulled it: the same rows, but the three entries whose first category is
    /// excluded from the deck are in zone main, in the local deck and in the baseline.
    /// </summary>
    private static async Task<DeckDocument> StoreAsStoredByTheEarlierVersionAsync(World world)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RemoteDeckSnapshot remote = Success(await world.Coordinator.Service.GetDeckAsync("42", token));
        HashSet<string> excludedRelations = [.. remote.Entries
            .Where(value => value.Zone == "maybeboard")
            .Select(value => value.ProviderRelationId)];
        Assert.Equal(3, excludedRelations.Count);
        RemoteDeckSnapshot earlier = remote with
        {
            Entries = [.. remote.Entries.Select(value => excludedRelations.Contains(value.ProviderRelationId)
                ? value with { Zone = "main" }
                : value)],
        };
        DeckProviderBinding binding = new(
            Guid.CreateVersion7(),
            "archidekt",
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            "earlier-remote-fingerprint",
            DateTimeOffset.UtcNow,
            null);
        DeckCreateRequest create = ArchidektLocalMapper.ToCreateRequest(earlier, binding);
        string baseline = ArchidektLocalMapper.CreateBaseline(create, earlier);
        DeckDocument local = Success(await world.Store.CreateSynchronizedAsync(
            create,
            new DeckSyncBaseline(binding.BindingId, baseline),
            token));
        Assert.Equal(0, local.Entries.Count(value => value.Zone == "maybeboard"));
        return local;
    }

    /// <summary>
    /// Verifies a deck pulled by 0.13.0 survives two pushes: the first push writes the zones Archidekt gives the three
    /// entries into the local deck, and the second push, after another edit, plans no update for them and is not
    /// refused as a zone contradiction.
    /// </summary>
    [Fact]
    public async Task DeckPulledByTheEarlierVersion_PushesTwiceWithoutAZoneContradiction()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithExcludedCategories());
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocument local = await StoreAsStoredByTheEarlierVersionAsync(world);

        for (int round = 1; round <= 2; round++)
        {
            DeckDocument current = Success(await world.Store.GetAsync(local.DeckId, token));
            DeckEntry filler = current.Entries.Single(value => value.CardName == "Filler 000");
            DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
                local.DeckId,
                current.Revision,
                [new UpdateDeckEntryChange(filler with { Quantity = round + 1 })],
                token));
            ArchidektSyncPreviewView preview = Success(
                await world.Reads.PreviewPushAsync(local.DeckId, cancellationToken: token));

            Assert.False(preview.HasConflicts);
            Assert.Equal(
                ["entry-update:Filler 000"],
                preview.Operations.Select(value => $"{value.Kind}:{value.Subject}"));

            ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
                new ArchidektPushApplyRequest(
                    local.DeckId,
                    edited.Revision,
                    preview.RemoteFingerprint,
                    preview.PreviewFingerprint),
                token));

            Assert.Equal("applied", applied.Outcome);
            Assert.Equal(round + 1, world.Fake.QuantityOf("Filler 000"));
        }

        DeckDocument after = Success(await world.Store.GetAsync(local.DeckId, token));
        ArchidektSyncPreviewView clean = Success(
            await world.Reads.PreviewPushAsync(local.DeckId, cancellationToken: token));

        Assert.Equal(3, after.Entries.Count(value => value.Zone == "maybeboard"));
        Assert.Empty(clean.Operations);
        Assert.Empty(clean.Differences);
    }

    /// <summary>
    /// Verifies a card added locally to a deck pulled by 0.13.0 does not make the three untouched entries show as
    /// local changes: the pull preview has no conflict, keeps the card, and the pull applies.
    /// </summary>
    [Fact]
    public async Task DeckPulledByTheEarlierVersion_AddingACardThenPulling_KeepsTheCardWithoutAConflict()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithExcludedCategories());
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocument local = await StoreAsStoredByTheEarlierVersionAsync(world);
        DeckCategory removal = local.Categories.Single(value => value.Name == "Removal");
        Guid shockId = Guid.NewGuid();
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            local.DeckId,
            local.Revision,
            [
                new AddDeckEntryChange(new DeckEntryDraft(1, "Shock", EntryId: shockId)),
                new AssignDeckCategoryChange(shockId, removal.CategoryId, true),
            ],
            token));

        ArchidektSyncDiffView diff = Success(await world.Reads.DiffAsync(local.DeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", local.DeckId, cancellationToken: token));

        ArchidektDifference row = Assert.Single(
            diff.Differences,
            value => value.Path.StartsWith("/entries/", StringComparison.Ordinal));
        Assert.Equal("local-added", row.State);
        Assert.False(pull.HasConflicts);
        Assert.Contains(pull.Operations, value => value.Kind == "local-keep");

        ArchidektApplyResult applied = Success(await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                local.DeckId,
                edited.Revision,
                pull.RemoteFingerprint,
                pull.PreviewFingerprint),
            token));
        DeckDocument after = Success(await world.Store.GetAsync(local.DeckId, token));

        Assert.Equal("applied", applied.Outcome);
        Assert.Contains(after.Entries, value => value.CardName == "Shock");
        Assert.Equal(3, after.Entries.Count(value => value.Zone == "maybeboard"));
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
        Assert.Contains("private deck also needs Archidekt credentials", pull, StringComparison.Ordinal);
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
    /// Verifies a pull preview with no credentials still offers the pull apply tool in local and in remote mode,
    /// because the tool is registered by mode and reads public decks without credentials.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PullPreview_WithoutCredentials_StillOffersPullApply(bool remoteMode)
    {
        using World world = new(
            remoteMode ? OperationMode.Remote : OperationMode.Local,
            CreateSmallDeck(),
            credentials: false);

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
            OperationResult<ArchidektDeckView> result = await world.Reads.GetDeckAsync(
                "42",
                offset,
                responseFormat: "full",
                cancellationToken: token);
            ArchidektDeckView page = Success(result);
            Assert.True(
                JsonLength(result) < MaximumResultCharacters,
                $"page {pages} is {JsonLength(result)} characters");
            Assert.Equal(300, page.Summary.EntryCount);
            Assert.Equal(offset, page.Offset);
            Assert.Equal(offset == 0, page.Categories.Count > 0);
            relationIds.AddRange(
                page.Entries.Cast<ArchidektFullDeckEntryView>().Select(value => value.ProviderRelationId));
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
            Assert.Contains(
                $"Entries {offset + 1} to {page.NextOffset} of 300 are shown",
                page.Narrow,
                StringComparison.Ordinal);
            Assert.Contains($"with offset {page.NextOffset} for the rest", page.Narrow, StringComparison.Ordinal);
            offset = page.NextOffset.Value;
        }

        Assert.True(pages > 1);
        Assert.Equal(300, relationIds.Count);
        Assert.Equal(300, relationIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(0, 300).Select(index => $"Card Number {index:000}"), names);
    }

    /// <summary>
    /// Verifies a deck with a 60,000-character description and a 5,000-character name shows the description cut on
    /// the first page and empty on every later page, and the name cut on every page, so no page passes the size limit.
    /// </summary>
    [Fact]
    public async Task DeckGet_With60000CharacterDescription_CutsItOnTheFirstPageAndOmitsItAfter()
    {
        FakeArchidekt fake = CreateLargeDeck(300);
        fake.Description = new string('d', 60_000);
        fake.DeckName = new string('n', 5_000);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        List<ArchidektDeckView> pages = [];
        int offset = 0;

        while (true)
        {
            OperationResult<ArchidektDeckView> result = await world.Reads.GetDeckAsync(
                "42",
                offset,
                cancellationToken: token);
            Assert.True(JsonLength(result) < MaximumResultCharacters, $"page {pages.Count} is too large");
            ArchidektDeckView page = Success(result);
            pages.Add(page);
            if (page.NextOffset is not int next)
            {
                break;
            }

            offset = next;
        }

        Assert.True(pages.Count > 1);
        Assert.Equal(ResultText.MaximumDescriptionBytes, pages[0].Description.Length);
        Assert.All(pages, page => Assert.True(page.DescriptionTruncated));
        Assert.All(pages, page => Assert.Equal(60_000, page.DescriptionLength));
        Assert.All(pages.Skip(1), page => Assert.Equal(string.Empty, page.Description));
        Assert.All(pages, page => Assert.Equal(ResultText.MaximumNameBytes, page.Name.Length));
        Assert.All(pages, page => Assert.True(page.NameTruncated));
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
            operationOffset = Assert.NotNull(page.NextOperationOffset);
            differenceOffset = Assert.NotNull(page.NextDifferenceOffset);
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

        ArchidektDeckView atEnd = Success(await world.Reads.GetDeckAsync("42", 4, cancellationToken: token));
        OperationResult<ArchidektDeckView> pastEnd = await world.Reads.GetDeckAsync("42", 5, cancellationToken: token);
        OperationResult<ArchidektDeckView> negative = await world.Reads.GetDeckAsync(
            "42",
            -1,
            cancellationToken: token);
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
        int nextOperation = Assert.NotNull(first.NextOperationOffset);
        int nextDifference = Assert.NotNull(first.NextDifferenceOffset);

        ArchidektSyncPreviewView same = Success(await world.Reads.PreviewPushAsync(
            localDeckId,
            operationOffset: nextOperation,
            differenceOffset: nextDifference,
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
            differenceOffset: nextDifference,
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
    /// Verifies a later page of a pull preview is refused when the fingerprint is not the one of the preview, and is
    /// served when it is.
    /// </summary>
    [Fact]
    public async Task PullPreview_LaterPageWithAnotherFingerprint_IsRefused()
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
        ArchidektSyncPreviewView first = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            discardLocalChanges: true,
            cancellationToken: token));
        Assert.True(first.Truncated);
        int nextOperation = Assert.NotNull(first.NextOperationOffset);
        int nextDifference = Assert.NotNull(first.NextDifferenceOffset);

        ArchidektSyncPreviewView same = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            discardLocalChanges: true,
            operationOffset: nextOperation,
            differenceOffset: nextDifference,
            expectedPreviewFingerprint: first.PreviewFingerprint,
            cancellationToken: token));
        OperationResult<ArchidektSyncPreviewView> other = await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            discardLocalChanges: true,
            operationOffset: nextOperation,
            differenceOffset: nextDifference,
            expectedPreviewFingerprint: "another-preview",
            cancellationToken: token);

        Assert.Equal(first.PreviewFingerprint, same.PreviewFingerprint);
        Assert.Equal("preview-changed", Assert.IsType<OperationConflict>(other.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a later page of a snapshot restore preview is refused when the fingerprint is not the one of the
    /// preview, and is served when it is.
    /// </summary>
    [Fact]
    public async Task SnapshotRestorePreview_LaterPageWithAnotherFingerprint_IsRefused()
    {
        FakeArchidekt fake = CreateLargeDeck(700);
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchidektSnapshotRestorePreviewView first = Success(
            await world.Reads.PreviewSnapshotRestoreAsync("42", "77", cancellationToken: token));
        Assert.True(first.Truncated);
        int nextOperation = Assert.NotNull(first.NextOperationOffset);
        int nextDifference = Assert.NotNull(first.NextDifferenceOffset);

        ArchidektSnapshotRestorePreviewView same = Success(await world.Reads.PreviewSnapshotRestoreAsync(
            "42",
            "77",
            nextOperation,
            nextDifference,
            first.PreviewFingerprint,
            token));
        OperationResult<ArchidektSnapshotRestorePreviewView> other = await world.Reads.PreviewSnapshotRestoreAsync(
            "42",
            "77",
            nextOperation,
            nextDifference,
            "another-preview",
            token);

        Assert.Equal(first.PreviewFingerprint, same.PreviewFingerprint);
        Assert.Equal("preview-changed", Assert.IsType<OperationConflict>(other.Value).ReasonCode);
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

        OperationResult<RemoteFolderTree> whole = kind == "list"
            ? await world.Coordinator.Service.ListFoldersAsync(token)
            : await world.Coordinator.Service.GetFolderAsync(folderId!, token);
        Assert.True(pages > 1);
        Assert.Equal(total, ids.Count);
        Assert.Equal(Success(whole).Items.Select(value => value.FolderId), ids);
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
                cancellationToken: token);
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
            operationOffset = Assert.NotNull(page.NextOperationOffset);
            differenceOffset = Assert.NotNull(page.NextDifferenceOffset);
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
    /// Reproduces the report: 27 sideboard cards added by name only, with no card data installed, one of them a card
    /// with 18 printings on Archidekt. The preview names the printing it will use for each, the apply sends one write
    /// per card and verifies, and the next preview and pull are clean.
    /// </summary>
    [Fact]
    public async Task NameOnlySideboardAdds_PushWithArchidektsDefaultPrinting_AndTheNextPreviewIsClean()
    {
        using World world = new(OperationMode.Remote, CreateDeckForNameOnlyAdds());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(
                local,
                ["Assassin's Trophy", .. Enumerable.Range(1, 26).Select(value => $"Side Card {value:00}")]),
            token));

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.True(preview.ApplyAvailable);
        Assert.Null(preview.ApplyNotice);
        Assert.Equal(27, preview.Summary.OperationCount);
        Assert.Equal(
            "Add one exact card relation: Archidekt printing soc 294 (first of 18 Archidekt printings of this name; " +
            "no printing was set).",
            preview.Operations.Single(value => value.Subject == "Assassin's Trophy").Summary);
        Assert.All(
            preview.Operations,
            value => Assert.Contains("Archidekt printing", value.Summary, StringComparison.Ordinal));
        Assert.Equal(0, world.Fake.Mutations);

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token));

        Assert.Equal("applied", applied.Outcome);
        Assert.All(applied.Operations, value => Assert.Equal("applied", value.Status));
        Assert.Equal(27, world.Fake.Mutations);
        Assert.Equal(1, world.Fake.QuantityOf("Assassin's Trophy"));
        Assert.Equal("4000", world.Fake.CardIdOf("Assassin's Trophy"));

        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView pull = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
        Assert.Empty(push.Operations);
        Assert.Empty(pull.Differences);
    }

    /// <summary>
    /// Verifies a set printing that is on the second page of the search is found through push preview and apply: the
    /// preview names it after reading two pages, the apply reuses that choice and sends the card of the printing.
    /// </summary>
    [Fact]
    public async Task SetPrintingOnPageTwoOfTheSearch_IsPreviewedAndPushed()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 1, "Removal");
        for (int number = 1; number <= 30; number++)
        {
            _ = fake.AddCardToCatalog(7000 + number, "Test Land", "dmu", number.ToString(CultureInfo.InvariantCulture));
        }

        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddToRemoval(local, new DeckEntryDraft(1, "Test Land", SetCode: "dmu", CollectorNumber: "28")),
            token));

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        List<string> searches = [.. world.Fake.Requests.Where(
            value => value.Contains("api/cards/v2/", StringComparison.Ordinal))];

        Assert.True(preview.ApplyAvailable);
        Assert.Equal(2, searches.Count);
        Assert.Contains("exact=true", searches[0], StringComparison.Ordinal);
        Assert.Contains("edition=dmu", searches[0], StringComparison.Ordinal);
        Assert.Contains("page=2", searches[1], StringComparison.Ordinal);
        Assert.Contains(
            "Archidekt printing dmu 28 (the set and collector number on the entry).",
            preview.Operations.Single(value => value.Subject == "Test Land").Summary,
            StringComparison.Ordinal);

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token));

        Assert.Equal("applied", applied.Outcome);
        Assert.Equal("7028", world.Fake.CardIdOf("Test Land"));
    }

    /// <summary>
    /// Verifies a set printing of a card with 955 printings, as Forest has, is found only because the search filters by
    /// set: by name alone the printing is far past the twelve pages the server reads.
    /// </summary>
    [Fact]
    public async Task SetPrintingOfACardWithManyPrintings_IsFoundThroughTheSetFilter()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 1, "Removal");
        for (int number = 1; number <= 955; number++)
        {
            bool wanted = number is >= 900 and <= 905;
            _ = fake.AddCardToCatalog(
                10_000 + number,
                "Forest",
                wanted ? "dmu" : "old",
                (wanted ? number - 626 : number).ToString(CultureInfo.InvariantCulture));
        }

        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddToRemoval(local, new DeckEntryDraft(1, "Forest", SetCode: "dmu", CollectorNumber: "276")),
            token));

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.True(preview.ApplyAvailable);
        Assert.Contains(
            "Archidekt printing dmu 276",
            preview.Operations.Single(value => value.Subject == "Forest").Summary,
            StringComparison.Ordinal);
        Assert.Single(world.Fake.Requests, value => value.Contains("api/cards/v2/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a mock deck of three cards through read, pull, and push preview: an entry whose first category is
    /// excluded is out of the deck, an entry whose only excluded category is a later one stays in, and a card in two
    /// categories is one entry reported once.
    /// </summary>
    [Fact]
    public async Task MockDeck_WithExcludedAndSharedCategories_CountsAndReportsEachEntryOnce()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddCategory("Ramp");
        fake.AddCategory("Maybe", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 1, "Maybe", "Removal");
        fake.AddEntry(fake.AddCardToCatalog(502, "Signet"), 1, "Ramp", "Maybe");
        fake.AddEntry(fake.AddCardToCatalog(503, "Charm"), 2, "Removal", "Ramp");
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;

        List<ArchidektFullDeckEntryView> entries = await ReadEveryEntryAsync(world, token);
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal(["Bolt", "Charm", "Signet"], entries.Select(value => value.CardName).Order(StringComparer.Ordinal));
        Assert.Equal("maybeboard", entries.Single(value => value.CardName == "Bolt").Zone);
        Assert.Equal(
            [false, true, true],
            entries.OrderBy(value => value.CardName, StringComparer.Ordinal).Select(value => value.InDeck));
        Assert.Equal(3, entries.Where(value => value.InDeck).Sum(value => value.Quantity));
        Assert.Equal(["Ramp", "Removal"], entries.Single(value => value.CardName == "Charm").CategoryNames.Order(StringComparer.Ordinal));
        Assert.Equal(["Maybe", "Ramp"], entries.Single(value => value.CardName == "Signet").CategoryNames.Order(StringComparer.Ordinal));
        Assert.Equal(3, local.Entries.Count);
        Assert.Equal(1, local.Entries.Count(value => value.Zone == "maybeboard"));
        Assert.Empty(push.Operations);
    }

    /// <summary>
    /// Verifies a name-only add of Sol Ring resolves its printing from the recorded Archidekt search, previews it, applies it with that printing's card id, and leaves the next preview clean.
    /// </summary>
    [Fact]
    public async Task NameOnlyAdd_OfSolRingFromTheRecordedSearch_PushesTheFirstRecordedPrinting()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 1, "Removal");
        string firstPage = RecordedManifest.Get("search-sol-ring-page-1").ReadText();
        using JsonDocument recorded = JsonDocument.Parse(firstPage);
        JsonElement firstPrinting = recorded.RootElement.GetProperty("results")[0];
        fake.ServeRecordedSearch("Sol Ring", 1, firstPage);
        fake.ServeRecordedSearch("Sol Ring", 2, RecordedManifest.Get("search-sol-ring-page-2").ReadText());
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddToRemoval(local, new DeckEntryDraft(1, "Sol Ring")),
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

        Assert.Contains(
            $"{firstPrinting.GetProperty("edition").GetProperty("editioncode").GetString()} " +
            $"{firstPrinting.GetProperty("collectorNumber").GetString()} (first of {recorded.RootElement.GetProperty("count").GetInt32()} " +
            "Archidekt printings of this name",
            preview.Operations.Single(value => value.Subject == "Sol Ring").Summary,
            StringComparison.Ordinal);
        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(firstPrinting.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture), world.Fake.CardIdOf("Sol Ring"));
        Assert.Empty(Success(await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token)).Operations);
    }

    /// <summary>
    /// Verifies a name-only add of a card whose name is part of other card names is searched for exactly, so the card
    /// is chosen and not one of the longer names that Archidekt lists first for a plain name search.
    /// </summary>
    [Fact]
    public async Task NameOnlyAdd_OfANameThatOtherCardNamesContain_PicksTheCardOfThatName()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 1, "Removal");
        _ = fake.AddCardToCatalog(8001, "Shock Troops");
        _ = fake.AddCardToCatalog(8002, "Aftershock");
        _ = fake.AddCardToCatalog(8003, "Shock");
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddToRemoval(local, new DeckEntryDraft(1, "Shock")),
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

        Assert.Equal("applied", applied.Outcome);
        Assert.Equal("8003", world.Fake.CardIdOf("Shock"));
        Assert.Contains(
            "first of 1 Archidekt printings of this name",
            preview.Operations.Single(value => value.Subject == "Shock").Summary,
            StringComparison.Ordinal);
        Assert.All(
            world.Fake.Requests.Where(value => value.Contains("api/cards/v2/", StringComparison.Ordinal)),
            value => Assert.Contains("exact=true", value, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies a name Archidekt does not know makes the preview say the push cannot be applied and name the card,
    /// and that applying it anyway sends nothing.
    /// </summary>
    [Fact]
    public async Task NameWithNoArchidektMatch_BlocksThePushAndSendsNothing()
    {
        using World world = new(OperationMode.Remote, CreateDeckForNameOnlyAdds());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(local, "Side Card 01", "No Such Card"),
            token));

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.False(preview.ApplyAvailable);
        Assert.Contains("'No Such Card'", preview.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("would send nothing", preview.ApplyNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("'Side Card 01'", preview.ApplyNotice, StringComparison.Ordinal);

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        Assert.Equal("push-printing-unresolved", Assert.IsType<OperationInvalidInput>(refused.Value).ReasonCode);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies a push whose card lookups would pass the request limit is blocked, with nothing sent.
    /// </summary>
    [Fact]
    public async Task PushNeedingMoreRequestsThanTheLimit_IsBlockedAndSendsNothing()
    {
        FakeArchidekt fake = CreateDeckForNameOnlyAdds();
        using World world = new(OperationMode.Remote, fake, maximumRequestsPerOperation: 20);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(local, [.. Enumerable.Range(1, 25).Select(value => $"Side Card {value:00}")]),
            token));

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.False(preview.ApplyAvailable);
        Assert.Contains("limit of 20", preview.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("would send nothing", preview.ApplyNotice, StringComparison.Ordinal);
        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);
        Assert.Equal("request-limit-exceeded", Assert.IsType<OperationInvalidInput>(refused.Value).ReasonCode);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Reproduces the report: after a sync, the deck is edited on Archidekt (a new Removal category, and two sideboard
    /// cards moved under it) while three cards are added locally. The push preview must not offer the apply or list
    /// operations that would revert the Archidekt edit, and the apply must refuse and send nothing.
    /// </summary>
    [Fact]
    public async Task PushPreview_AfterTheRemoteChanged_OffersNoApplyAndNoOperations()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(local, "Side Card 01", "Side Card 02", "Side Card 03"),
            token));
        MoveTwoCardsUnderANewRemovalCategory(world.Fake);

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.True(preview.HasConflicts);
        Assert.False(preview.ApplyAvailable);
        Assert.Empty(preview.Operations);
        Assert.Equal(0, preview.Summary.OperationCount);
        Assert.Equal(
            ["remote-added", "remote-changed", "remote-changed"],
            preview.Differences.Where(value => value.State.StartsWith("remote-", StringComparison.Ordinal))
                .Select(value => value.State)
                .Order(StringComparer.Ordinal));
        Assert.Equal(3, preview.Differences.Count(value => value.State == "local-added"));
        Assert.Equal(
            "archidekt_push_apply would refuse this push: the Archidekt deck changed since the last sync (3 rows; " +
            "see differences with remote-* states). Run archidekt_pull_preview first; a pull keeps cards added " +
            "locally when those are your only local changes. Then push again.",
            preview.ApplyNotice);

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        OperationConflict conflict = Assert.IsType<OperationConflict>(refused.Value);
        Assert.Equal("push-conflict", conflict.ReasonCode);
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies a conflicting push preview in local mode puts the mode notice first and the conflict notice after it.
    /// </summary>
    [Fact]
    public async Task PushPreview_AfterTheRemoteChanged_InLocalMode_PutsTheModeNoticeFirst()
    {
        using World world = new(OperationMode.Local, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        MoveTwoCardsUnderANewRemovalCategory(world.Fake);

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.False(preview.ApplyAvailable);
        Assert.StartsWith(
            "archidekt_push_apply is not available because the server runs in local mode.",
            preview.ApplyNotice,
            StringComparison.Ordinal);
        Assert.Contains(
            "archidekt_push_apply would refuse this push: the Archidekt deck changed since the last sync (",
            preview.ApplyNotice,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a pull with no flag keeps the three cards added locally and takes the two cards Archidekt moved, and
    /// that the push after it adds exactly the three cards.
    /// </summary>
    [Fact]
    public async Task Pull_AfterTheRemoteChanged_KeepsLocalAdditionsAndTheNextPushAddsOnlyThem()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(local, "Side Card 01", "Side Card 02", "Side Card 03"),
            token));
        MoveTwoCardsUnderANewRemovalCategory(world.Fake);

        ArchidektSyncPreviewView pullPreview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.False(pullPreview.HasConflicts);
        Assert.True(pullPreview.ApplyAvailable);
        Assert.Equal(
            ["local-deck-replace", "local-keep"],
            pullPreview.Operations.Select(value => value.Kind));
        Assert.Equal(
            "Keep 3 cards added locally since the last sync; push them afterwards.",
            pullPreview.Operations.Single(value => value.Kind == "local-keep").Summary);
        Assert.Equal(
            "Replace the local deck with the Archidekt deck, except the cards added locally (see local-keep).",
            pullPreview.Operations.Single(value => value.Kind == "local-deck-replace").Summary);

        ArchidektApplyResult applied = Success(await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                edited.Revision,
                pullPreview.RemoteFingerprint,
                pullPreview.PreviewFingerprint),
            token));

        Assert.Equal("applied", applied.Outcome);
        DeckDocument pulled = Success(await world.Store.GetAsync(localDeckId, token));
        Assert.Equal(6, pulled.Entries.Count);
        foreach (string moved in new[] { "Assassin's Trophy", "Pick Your Poison" })
        {
            DeckEntry entry = pulled.Entries.Single(value => value.CardName == moved);
            Assert.Equal("main", entry.Zone);
            Assert.Equal(
                ["Removal"],
                CategoryNamesOf(pulled, entry));
        }

        foreach (string added in new[] { "Side Card 01", "Side Card 02", "Side Card 03" })
        {
            DeckEntry entry = pulled.Entries.Single(value => value.CardName == added);
            Assert.Equal("sideboard", entry.Zone);
            Assert.Equal(["Sideboard"], CategoryNamesOf(pulled, entry));
        }

        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.True(push.ApplyAvailable);
        Assert.False(push.HasConflicts);
        Assert.Equal(
            ["entry-add:Side Card 01", "entry-add:Side Card 02", "entry-add:Side Card 03"],
            push.Operations.Select(value => $"{value.Kind}:{value.Subject}").Order(StringComparer.Ordinal));
        Assert.Equal(0, world.Fake.Mutations);

        ArchidektApplyResult pushed = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                pulled.Revision,
                push.RemoteFingerprint,
                push.PreviewFingerprint),
            token));

        Assert.Equal("applied", pushed.Outcome);
        Assert.Equal(3, world.Fake.Mutations);
        Assert.Equal(1, world.Fake.QuantityOf("Side Card 02"));
    }

    /// <summary>
    /// Verifies a pull keeps a category added locally, creating it again when Archidekt has no category of that name.
    /// </summary>
    [Fact]
    public async Task Pull_WithALocalCategory_KeepsItAndTheNextPushCreatesIt()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        Guid drawId = Guid.NewGuid();
        Guid cardId = Guid.NewGuid();
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new AddDeckCategoryChange(new DeckCategoryDraft("Draw", CategoryId: drawId)),
                new AddDeckEntryChange(new DeckEntryDraft(1, "Side Card 01", EntryId: cardId)),
                new AssignDeckCategoryChange(cardId, drawId, true),
            ],
            token));
        MoveTwoCardsUnderANewRemovalCategory(world.Fake);

        ArchidektSyncPreviewView pullPreview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));
        Assert.False(pullPreview.HasConflicts);
        _ = Success(await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                edited.Revision,
                pullPreview.RemoteFingerprint,
                pullPreview.PreviewFingerprint),
            token));

        DeckDocument pulled = Success(await world.Store.GetAsync(localDeckId, token));
        Assert.Contains(pulled.Categories, value => value.Name == "Draw" && value.CategoryId == drawId);
        Assert.Equal(["Draw"], CategoryNamesOf(pulled, pulled.Entries.Single(value => value.EntryId == cardId)));
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        Assert.False(push.HasConflicts);
        Assert.Equal(
            ["category-create:Draw", "entry-add:Side Card 01"],
            push.Operations.Select(value => $"{value.Kind}:{value.Subject}").Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies a local edit to a card Archidekt also changed is a conflict: the preview says the apply would refuse,
    /// and the apply refuses with pull-conflict and changes nothing.
    /// </summary>
    [Fact]
    public async Task Pull_WithALocalChangeToACardTheRemoteChanged_IsAConflict()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry trophy = local.Entries.Single(value => value.CardName == "Assassin's Trophy");
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [new UpdateDeckEntryChange(trophy with { Quantity = 2 })],
            token));
        MoveTwoCardsUnderANewRemovalCategory(world.Fake);

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.True(preview.HasConflicts);
        Assert.False(preview.ApplyAvailable);
        Assert.Contains("discardLocalChanges", preview.ApplyNotice, StringComparison.Ordinal);
        Assert.DoesNotContain(preview.Operations, value => value.Kind == "local-keep");

        OperationResult<ArchidektApplyResult> refused = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        Assert.Equal("pull-conflict", Assert.IsType<OperationConflict>(refused.Value).ReasonCode);
        Assert.Equal(edited.Revision, Success(await world.Store.GetAsync(localDeckId, token)).Revision);
    }

    /// <summary>
    /// Verifies an edit to a field no row shows (the order number of a card) beside an added card is still a conflict,
    /// since the pull would drop the edit.
    /// </summary>
    [Fact]
    public async Task Pull_WithAnAddedCardAndAnEditToCardOrder_IsAConflict()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry bolt = local.Entries.Single(value => value.CardName == "Bolt");
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                .. AddSideboardCards(local, "Side Card 01"),
                new UpdateDeckEntryChange(bolt with { SortOrder = bolt.SortOrder + 50 }),
            ],
            token));

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.True(preview.HasConflicts);
        Assert.False(preview.ApplyAvailable);
        Assert.DoesNotContain(preview.Operations, value => value.Kind == "local-keep");
    }

    /// <summary>
    /// Verifies a pull keeps a card added locally to a category Archidekt renamed: the card follows the category to
    /// its new name, the pull applies, and the next push adds the card without creating the old category again.
    /// </summary>
    [Fact]
    public async Task Pull_WithACardAddedToACategoryArchidektRenamed_MovesTheCardToTheNewName()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckCategory ramp = local.Categories.Single(value => value.Name == "Ramp");
        Guid cardId = Guid.NewGuid();
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new AddDeckEntryChange(new DeckEntryDraft(1, "Side Card 01", EntryId: cardId)),
                new AssignDeckCategoryChange(cardId, ramp.CategoryId, true),
            ],
            token));
        world.Fake.RenameCategory("Ramp", "Acceleration");

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.False(preview.HasConflicts);
        Assert.Contains(preview.Operations, value => value.Kind == "local-keep");

        ArchidektApplyResult applied = Success(await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token));
        DeckDocument pulled = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry kept = pulled.Entries.Single(value => value.CardName == "Side Card 01");
        ArchidektSyncPreviewView push = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(["Acceleration"], CategoryNamesOf(pulled, kept));
        Assert.DoesNotContain(pulled.Categories, value => value.Name == "Ramp");
        Assert.Equal(
            ["entry-add:Side Card 01"],
            push.Operations.Select(value => $"{value.Kind}:{value.Subject}"));
    }

    /// <summary>
    /// Verifies a card kept in the main zone is refused when Archidekt renamed its category to Sideboard: the card
    /// follows the category, so its zone would change. The notice names the card and both zones.
    /// </summary>
    [Fact]
    public async Task Pull_WithACardAddedToACategoryArchidektRenamedToSideboard_IsAZoneConflict()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithOnlyRamp());
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocument edited = await AddSideCardToRampAsync(world);
        world.Fake.RenameCategory("Ramp", "Sideboard");

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", edited.DeckId, cancellationToken: token));

        Assert.Contains(
            "'Side Card 01' (zone 'main', but its categories give zone 'sideboard')",
            preview.ApplyNotice,
            StringComparison.Ordinal);
        await AssertPullIsRefusedAsync(world, edited.DeckId, edited.Revision);
    }

    /// <summary>
    /// Verifies the same for a category Archidekt renamed and also took out of the deck: the card would leave the
    /// deck, so its zone changes from main to maybeboard.
    /// </summary>
    [Fact]
    public async Task Pull_WithACardAddedToACategoryArchidektTookOutOfTheDeck_IsAZoneConflict()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithOnlyRamp());
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocument edited = await AddSideCardToRampAsync(world);
        world.Fake.RenameCategory("Ramp", "Acceleration", includedInDeck: false);

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", edited.DeckId, cancellationToken: token));

        Assert.Contains(
            "'Side Card 01' (zone 'main', but its categories give zone 'maybeboard')",
            preview.ApplyNotice,
            StringComparison.Ordinal);
        await AssertPullIsRefusedAsync(world, edited.DeckId, edited.Revision);
    }

    /// <summary>
    /// Verifies a pull that keeps a local card is refused with a plain message, not a crash, when Archidekt now has
    /// two categories of the same name: the local store allows one category per name, and the local deck is unchanged.
    /// </summary>
    [Fact]
    public async Task Pull_WithACardAddedAndTwoArchidektCategoriesOfTheSameName_IsRefusedAndLeavesTheDeck()
    {
        using World world = new(OperationMode.Remote, CreateDeckWithOnlyRamp());
        CancellationToken token = TestContext.Current.CancellationToken;
        DeckDocument edited = await AddSideCardToRampAsync(world);
        world.Fake.AddCategory("Ramp");

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", edited.DeckId, cancellationToken: token));
        OperationResult<ArchidektApplyResult> refused = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                edited.DeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(refused.Value);
        Assert.Contains("have the same name", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(edited.Revision, Success(await world.Store.GetAsync(edited.DeckId, token)).Revision);
    }

    /// <summary>
    /// Pulls the deck, then adds Side Card 01 to its Ramp category in the main zone.
    /// </summary>
    private static async Task<DeckDocument> AddSideCardToRampAsync(World world)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckCategory ramp = local.Categories.Single(value => value.Name == "Ramp");
        Guid cardId = Guid.NewGuid();
        return Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new AddDeckEntryChange(new DeckEntryDraft(1, "Side Card 01", EntryId: cardId)),
                new AssignDeckCategoryChange(cardId, ramp.CategoryId, true),
            ],
            token));
    }

    /// <summary>
    /// Makes a deck whose only category is Ramp, so a rename to Sideboard does not collide with another category.
    /// </summary>
    private static FakeArchidekt CreateDeckWithOnlyRamp()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Ramp");
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 2, "Ramp");
        _ = fake.AddCardToCatalog(601, "Side Card 01");
        return fake;
    }

    /// <summary>
    /// Verifies a card added locally beside the local removal of another card is a conflict: the pull would drop the
    /// removal, so the preview and the apply refuse, no local-keep is planned, and the local deck is not changed.
    /// </summary>
    [Fact]
    public async Task Pull_WithAnAddedCardAndALocalRemovalOfAnotherCard_IsAConflict()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry bolt = local.Entries.Single(value => value.CardName == "Bolt");
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. AddSideboardCards(local, "Side Card 01"), new RemoveDeckEntryChange(bolt.EntryId)],
            token));
        MoveTwoCardsUnderANewRemovalCategory(world.Fake);

        await AssertPullIsRefusedAsync(world, localDeckId, edited.Revision);
    }

    /// <summary>
    /// Verifies a card added locally beside a local quantity edit of another card is a conflict, for the same reason.
    /// </summary>
    [Fact]
    public async Task Pull_WithAnAddedCardAndAQuantityEditOfAnotherCard_IsAConflict()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry bolt = local.Entries.Single(value => value.CardName == "Bolt");
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. AddSideboardCards(local, "Side Card 01"), new UpdateDeckEntryChange(bolt with { Quantity = 3 })],
            token));
        MoveTwoCardsUnderANewRemovalCategory(world.Fake);

        await AssertPullIsRefusedAsync(world, localDeckId, edited.Revision);
    }

    /// <summary>
    /// Checks a pull preview is a conflict with no local-keep, and that the apply refuses with pull-conflict and leaves
    /// the local revision as it was.
    /// </summary>
    private static async Task AssertPullIsRefusedAsync(World world, Guid localDeckId, long revision)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.True(preview.HasConflicts);
        Assert.False(preview.ApplyAvailable);
        Assert.DoesNotContain(preview.Operations, value => value.Kind == "local-keep");

        OperationResult<ArchidektApplyResult> refused = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        Assert.Equal("pull-conflict", Assert.IsType<OperationConflict>(refused.Value).ReasonCode);
        Assert.Equal(revision, Success(await world.Store.GetAsync(localDeckId, token)).Revision);
    }

    /// <summary>
    /// Verifies a card added locally that Archidekt also added is refused, by name, in the preview and the apply.
    /// </summary>
    [Fact]
    public async Task Pull_WithACardBothSidesAdded_IsRefused()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            AddSideboardCards(local, "Side Card 01", "Side Card 02"),
            token));
        world.Fake.AddEntry(world.Fake.AddCardToCatalog(601, "Side Card 01"), 1, "Sideboard");

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.True(preview.HasConflicts);
        Assert.False(preview.ApplyAvailable);
        Assert.Contains("'Side Card 01'", preview.ApplyNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("'Side Card 02'", preview.ApplyNotice, StringComparison.Ordinal);

        OperationResult<ArchidektApplyResult> refused = await world.LocalWrites.ApplyPullAsync(
            new ArchidektPullApplyRequest(
                "42",
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        OperationConflict conflict = Assert.IsType<OperationConflict>(refused.Value);
        Assert.Equal("pull-conflict", conflict.ReasonCode);
        Assert.Contains("'Side Card 01'", conflict.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a card added locally in a zone its categories contradict is refused, naming the card and both zones.
    /// </summary>
    [Fact]
    public async Task Pull_WithAKeptCardWhoseCategoriesGiveAnotherZone_IsRefused()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckCategory sideboard = local.Categories.Single(value => value.Name == "Sideboard");
        Guid cardId = Guid.NewGuid();
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                new AddDeckEntryChange(new DeckEntryDraft(1, "Side Card 01", Zone: "main", EntryId: cardId)),
                new AssignDeckCategoryChange(cardId, sideboard.CategoryId, true),
            ],
            token));

        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPullAsync("42", localDeckId, cancellationToken: token));

        Assert.True(preview.HasConflicts);
        Assert.False(preview.ApplyAvailable);
        Assert.Contains(
            "'Side Card 01' (zone 'main', but its categories give zone 'sideboard')",
            preview.ApplyNotice,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the discard preview says what is thrown away: the cards and categories added, changed, and removed
    /// locally since the last sync.
    /// </summary>
    [Fact]
    public async Task PullPreview_WithDiscard_CountsWhatIsThrownAway()
    {
        using World world = new(OperationMode.Remote, CreateDeckForRemoteEdits());
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckEntry bolt = local.Entries.Single(value => value.CardName == "Bolt");
        DeckEntry poison = local.Entries.Single(value => value.CardName == "Pick Your Poison");
        DeckCategory ramp = local.Categories.Single(value => value.Name == "Ramp");
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                .. AddSideboardCards(local, "Side Card 01", "Side Card 02"),
                new UpdateDeckEntryChange(bolt with { Quantity = 4 }),
                new RemoveDeckEntryChange(poison.EntryId),
                new RemoveDeckCategoryChange(ramp.CategoryId),
                new AddDeckCategoryChange(new DeckCategoryDraft("Draw")),
            ],
            token));

        ArchidektSyncPreviewView preview = Success(await world.Reads.PreviewPullAsync(
            "42",
            localDeckId,
            discardLocalChanges: true,
            cancellationToken: token));

        Assert.False(preview.HasConflicts);
        Assert.Equal(
            "Replace the local deck with the Archidekt deck. This discards local changes made since the last sync: " +
            "2 cards added, 1 changed, 1 removed (categories: 1 added, 0 changed, 1 removed). They are listed in " +
            "differences with local-* states.",
            preview.Operations.Single(value => value.Kind == "local-deck-replace").Summary);
        Assert.DoesNotContain(preview.Operations, value => value.Kind == "local-keep");
    }

    /// <summary>
    /// Reproduces the report: a local deck that holds 79 of the 172 Archidekt entries would remove the other 93. The
    /// preview refuses to offer the apply and names the count, the apply refuses without sending anything, a wrong
    /// count is refused, and the exact count lets the push through.
    /// </summary>
    [Fact]
    public async Task Push_RemovingMostOfTheArchidektDeck_NeedsTheExactRemovalCount()
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

        ArchidektSyncPreviewView refusedPreview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        ArchidektSyncPreviewView wrongPreview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, confirmRemovals: 90, cancellationToken: token));
        ArchidektSyncPreviewView confirmedPreview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, confirmRemovals: 93, cancellationToken: token));

        Assert.False(refusedPreview.ApplyAvailable);
        Assert.Contains("removes 93 of 172 Archidekt entries", refusedPreview.ApplyNotice, StringComparison.Ordinal);
        Assert.Contains("confirmRemovals: 93", refusedPreview.ApplyNotice, StringComparison.Ordinal);
        Assert.Equal(93, refusedPreview.Summary.OperationsByKind["entry-remove"]);
        Assert.False(wrongPreview.ApplyAvailable);
        Assert.Contains("confirmRemovals 90 does not match", wrongPreview.ApplyNotice, StringComparison.Ordinal);
        Assert.True(confirmedPreview.ApplyAvailable);
        Assert.Null(confirmedPreview.ApplyNotice);
        Assert.Equal(refusedPreview.PreviewFingerprint, confirmedPreview.PreviewFingerprint);

        foreach (int? confirm in new int?[] { null, 90 })
        {
            OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
                new ArchidektPushApplyRequest(
                    localDeckId,
                    edited.Revision,
                    refusedPreview.RemoteFingerprint,
                    refusedPreview.PreviewFingerprint,
                    confirm),
                token);
            OperationInvalidInput invalid = Assert.IsType<OperationInvalidInput>(refused.Value);
            Assert.Equal("push-removals-unconfirmed", invalid.ReasonCode);
            Assert.Equal(0, world.Fake.Mutations);
        }

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                confirmedPreview.RemoteFingerprint,
                confirmedPreview.PreviewFingerprint,
                93),
            token));
        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(93, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies the removal rule at its edges, with nothing sent when the apply refuses: removing 11 of 200 entries
    /// needs confirmation by the count rule alone, removing 3 of 25 by the percent rule alone, removing 10 of 100 needs
    /// none, and removing fewer than 3 entries needs none even from a small deck.
    /// </summary>
    [Theory]
    [InlineData(200, 11, true)]
    [InlineData(25, 3, true)]
    [InlineData(100, 10, false)]
    [InlineData(30, 3, false)]
    [InlineData(15, 2, false)]
    [InlineData(5, 1, false)]
    public async Task Push_Removals_NeedConfirmationOnlyPastTheCountOrPercentRule(
        int deckSize,
        int removals,
        bool needsConfirmation)
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(deckSize));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Take(removals).Select(value => new RemoveDeckEntryChange(value.EntryId))],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        OperationResult<ArchidektApplyResult> result = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        Assert.Equal(!needsConfirmation, preview.ApplyAvailable);
        if (needsConfirmation)
        {
            Assert.Equal("push-removals-unconfirmed", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
            Assert.Equal(0, world.Fake.Mutations);
        }
        else
        {
            Assert.Equal("applied", Success(result).Outcome);
            Assert.Equal(removals, world.Fake.Mutations);
        }
    }

    /// <summary>
    /// Verifies an apply that the removal rule refuses sends no card search: the removals do not depend on the
    /// printings of the new cards, so the refusal comes before the lookups.
    /// </summary>
    [Fact]
    public async Task ApplyPush_WithUnconfirmedRemovalsAndANameOnlyAdd_SendsNoCardSearch()
    {
        FakeArchidekt fake = CreateLargeDeck(30);
        _ = fake.AddCardToCatalog(9001, "Shock");
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [
                .. local.Entries.Take(12).Select(value => new RemoveDeckEntryChange(value.EntryId)),
                new AddDeckEntryChange(new DeckEntryDraft(1, "Shock")),
            ],
            token));
        ArchidektSyncPreviewView preview = Success(await world.Reads.PreviewPushAsync(
            localDeckId,
            confirmRemovals: 12,
            cancellationToken: token));
        int searchesBefore = CountSearches(world.Fake);

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        Assert.Equal("push-removals-unconfirmed", Assert.IsType<OperationInvalidInput>(refused.Value).ReasonCode);
        Assert.True(searchesBefore > 0);
        Assert.Equal(searchesBefore, CountSearches(world.Fake));
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies an apply whose local deck changed after the preview is refused before any card search.
    /// </summary>
    [Fact]
    public async Task ApplyPush_WithAStaleLocalRevision_SendsNoCardSearch()
    {
        FakeArchidekt fake = CreateLargeDeck(30);
        _ = fake.AddCardToCatalog(9001, "Shock");
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [new AddDeckEntryChange(new DeckEntryDraft(1, "Shock"))],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        _ = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            edited.Revision,
            [new AddDeckEntryChange(new DeckEntryDraft(1, "Shock"))],
            token));
        int searchesBefore = CountSearches(world.Fake);

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        Assert.Equal("push-preview-changed", Assert.IsType<OperationConflict>(refused.Value).ReasonCode);
        Assert.True(searchesBefore > 0);
        Assert.Equal(searchesBefore, CountSearches(world.Fake));
        Assert.Equal(0, world.Fake.Mutations);
    }

    /// <summary>
    /// Verifies an apply refuses when the local deck changes after the apply checked it, while the card searches ran:
    /// nothing is sent to Archidekt and the local deck is not recorded as synchronized.
    /// </summary>
    [Fact]
    public async Task ApplyPush_WhenTheLocalDeckChangesDuringTheSearches_SendsNothing()
    {
        FakeArchidekt fake = CreateLargeDeck(12);
        _ = fake.AddCardToCatalog(9001, "Shock");
        using World world = new(OperationMode.Remote, fake);
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [new AddDeckEntryChange(new DeckEntryDraft(1, "Shock"))],
            token));
        ArchidektSyncPreviewView preview = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));
        bool changed = false;
        world.Fake.BeforeRead = path =>
        {
            if (!changed && path.StartsWith("api/cards/v2/", StringComparison.Ordinal))
            {
                changed = true;
                DeckDocument current = Success(world.Store.GetAsync(localDeckId, token).GetAwaiter().GetResult());
                _ = Success(world.Store.ApplyChangesAsync(
                    localDeckId,
                    current.Revision,
                    [new UpdateDeckEntryChange(current.Entries[0] with { Quantity = 4 })],
                    token).GetAwaiter().GetResult());
            }
        };

        OperationResult<ArchidektApplyResult> refused = await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token);

        Assert.True(changed);
        Assert.Equal("push-preview-changed", Assert.IsType<OperationConflict>(refused.Value).ReasonCode);
        Assert.Equal(0, world.Fake.Mutations);
        Assert.Equal(edited.Revision + 1, Success(await world.Store.GetAsync(localDeckId, token)).Revision);
    }

    /// <summary>
    /// Verifies a push whose final read of Archidekt fails after the writes keeps the statuses of the writes, says the
    /// result is not verified, and leaves the baseline and the local deck as they were.
    /// </summary>
    [Fact]
    public async Task ApplyPush_WhenTheReadAfterTheWritesFails_KeepsStatusesAndLeavesTheBaseline()
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
        world.Fake.DeckReadsThatSucceedAfterWrites = 1;

        ArchidektApplyResult applied = Success(await world.Writes.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                edited.Revision,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            token));
        world.Fake.DeckReadsThatSucceedAfterWrites = null;
        ArchidektSyncPreviewView after = Success(
            await world.Reads.PreviewPushAsync(localDeckId, cancellationToken: token));

        Assert.Equal("applied-unverified", applied.Outcome);
        Assert.Equal("entry-update", Assert.Single(applied.Operations).Kind);
        Assert.Equal("applied", applied.Operations[0].Status);
        Assert.Equal(ArchidektApplyResult.UnverifiedNotice, applied.Notice);
        Assert.Equal(3, world.Fake.QuantityOf("Bolt"));
        Assert.Equal(edited.Revision, Success(await world.Store.GetAsync(localDeckId, token)).Revision);
        Assert.NotEmpty(after.Differences);
    }

    /// <summary>
    /// Verifies a push that removes a few entries from a large deck needs no confirmation.
    /// </summary>
    [Fact]
    public async Task Push_RemovingAFewEntriesFromALargeDeck_NeedsNoConfirmation()
    {
        using World world = new(OperationMode.Remote, CreateLargeDeck(172));
        CancellationToken token = TestContext.Current.CancellationToken;
        Guid localDeckId = await world.PullAsync();
        DeckDocument local = Success(await world.Store.GetAsync(localDeckId, token));
        DeckDocument edited = Success(await world.Store.ApplyChangesAsync(
            localDeckId,
            local.Revision,
            [.. local.Entries.Take(10).Select(value => new RemoveDeckEntryChange(value.EntryId))],
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

        Assert.True(preview.ApplyAvailable);
        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(10, world.Fake.Mutations);
    }

    /// <summary>
    /// Creates a deck for the edit-on-both-sides tests: a Ramp category, a Sideboard category outside the deck, one
    /// main card, two sideboard cards, and a catalog of three cards to add by name.
    /// </summary>
    private static FakeArchidekt CreateDeckForRemoteEdits()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Ramp");
        fake.AddCategory("Sideboard", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 2, "Ramp");
        fake.AddEntry(fake.AddCardToCatalog(502, "Assassin's Trophy"), 1, "Sideboard");
        fake.AddEntry(fake.AddCardToCatalog(503, "Pick Your Poison"), 1, "Sideboard");
        _ = fake.AddCardToCatalog(601, "Side Card 01");
        _ = fake.AddCardToCatalog(602, "Side Card 02");
        _ = fake.AddCardToCatalog(603, "Side Card 03");
        return fake;
    }

    /// <summary>
    /// Makes the edit of the report on the fake: a new Removal category, with two sideboard cards moved under it.
    /// </summary>
    private static void MoveTwoCardsUnderANewRemovalCategory(FakeArchidekt fake)
    {
        fake.AddCategory("Removal");
        fake.SetCategories("Assassin's Trophy", "Removal");
        fake.SetCategories("Pick Your Poison", "Removal");
    }

    /// <summary>
    /// Lists the names of the categories one entry is assigned to.
    /// </summary>
    private static string[] CategoryNamesOf(DeckDocument deck, DeckEntry entry)
    {
        return [.. deck.CategoryAssignments
            .Where(value => value.EntryId == entry.EntryId)
            .Select(value => deck.Categories.Single(category => category.CategoryId == value.CategoryId).Name)];
    }

    /// <summary>
    /// Counts the card search requests the fake has answered.
    /// </summary>
    private static int CountSearches(FakeArchidekt fake)
    {
        return fake.Requests.Count(value => value.Contains("api/cards/v2/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Builds the changes that add one entry in the Removal category.
    /// </summary>
    private static List<DeckChange> AddToRemoval(DeckDocument local, DeckEntryDraft draft)
    {
        Guid entryId = Guid.NewGuid();
        DeckCategory removal = local.Categories.Single(value => value.Name == "Removal");
        return
        [
            new AddDeckEntryChange(draft with { EntryId = entryId }),
            new AssignDeckCategoryChange(entryId, removal.CategoryId, true),
        ];
    }

    /// <summary>
    /// Builds the changes that add each name as a sideboard entry with no card identity, in the Sideboard category.
    /// </summary>
    private static List<DeckChange> AddSideboardCards(DeckDocument local, params string[] names)
    {
        DeckCategory sideboard = local.Categories.Single(value => value.Name == "Sideboard");
        List<DeckChange> changes = [];
        foreach (string name in names)
        {
            Guid entryId = Guid.NewGuid();
            changes.Add(new AddDeckEntryChange(new DeckEntryDraft(1, name, Zone: "sideboard", EntryId: entryId)));
            changes.Add(new AssignDeckCategoryChange(entryId, sideboard.CategoryId, true));
        }

        return changes;
    }

    /// <summary>
    /// Creates a deck with a Sideboard category and one main card, and a catalog for cards added by name: Assassin's
    /// Trophy with 18 printings (the first is soc 294, as on Archidekt) and 26 cards with two printings each.
    /// </summary>
    private static FakeArchidekt CreateDeckForNameOnlyAdds()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Removal");
        fake.AddCategory("Sideboard", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(501, "Bolt"), 2, "Removal");
        string[] sets = ["soc", "tmc", "ecc", "acr", "mkm", "2x2", "grn", "pmkm", "sld"];
        for (int index = 0; index < 18; index++)
        {
            _ = fake.AddCardToCatalog(
                4000 + index,
                "Assassin's Trophy",
                sets[index % sets.Length],
                (294 - index).ToString(CultureInfo.InvariantCulture),
                oracleId: new Guid(77, 2, 2, 0, 0, 0, 0, 0, 0, 0, 2),
                printingId: new Guid(4000 + index, 1, 1, 0, 0, 0, 0, 0, 0, 0, 1));
        }

        for (int index = 1; index < 27; index++)
        {
            _ = fake.AddCardToCatalog(5000 + (index * 2), $"Side Card {index:00}", "dmu");
            _ = fake.AddCardToCatalog(5001 + (index * 2), $"Side Card {index:00}", "dmr");
        }

        return fake;
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
    /// Creates a deck shaped like the real public deck 22959335: one commander, 98 other main cards, a second entry
    /// of Bloodthirsty Blade under Removal, and three entries whose first category is not included in the deck
    /// (Bloodthirsty Blade under Multiplayer, Revival // Revenge under Reanimate then Burn, Osteomancer Adept under
    /// Reanimate). Counted the way Archidekt counts, the deck has 100 cards.
    /// </summary>
    private static FakeArchidekt CreateDeckWithExcludedCategories()
    {
        FakeArchidekt fake = new();
        fake.AddCategory("Commander");
        fake.AddCategory("Removal");
        fake.AddCategory("Burn");
        fake.AddCategory("Multiplayer", includedInDeck: false);
        fake.AddCategory("Reanimate", includedInDeck: false);
        fake.AddEntry(fake.AddCardToCatalog(3001, "Test Commander"), 1, "Commander");
        FakeArchidekt.FakeCard blade = fake.AddCardToCatalog(3002, "Bloodthirsty Blade");
        fake.AddEntry(blade, 1, "Multiplayer");
        fake.AddEntry(blade, 1, "Removal");
        fake.AddEntry(fake.AddCardToCatalog(3003, "Revival // Revenge"), 1, "Reanimate", "Burn");
        fake.AddEntry(fake.AddCardToCatalog(3004, "Osteomancer Adept"), 1, "Reanimate");
        for (int index = 0; index < 98; index++)
        {
            fake.AddEntry(fake.AddCardToCatalog(3100 + index, $"Filler {index:000}"), 1, "Removal");
        }

        return fake;
    }

    /// <summary>
    /// Reads every page of the deck in the full format and returns its entries.
    /// </summary>
    private static async Task<List<ArchidektFullDeckEntryView>> ReadEveryEntryAsync(
        World world,
        CancellationToken token)
    {
        List<ArchidektFullDeckEntryView> entries = [];
        int offset = 0;
        while (true)
        {
            ArchidektDeckView page = Success(await world.Reads.GetDeckAsync(
                "42",
                offset,
                responseFormat: "full",
                cancellationToken: token));
            entries.AddRange(page.Entries.Cast<ArchidektFullDeckEntryView>());
            if (page.NextOffset is null)
            {
                return entries;
            }

            offset = page.NextOffset.Value;
        }
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
        internal World(
            OperationMode mode,
            FakeArchidekt fake,
            bool credentials = true,
            int maximumRequestsPerOperation = 2_000)
        {
            Fake = fake;
            ArchidektOptions options = ArchidektOptions.CreateDefault(
                credentials ? "user" : null,
                credentials ? "secret" : null) with
            {
                BaseAddress = new Uri("https://archidekt.test/"),
                MinimumRequestInterval = TimeSpan.Zero,
                MaximumRequestsPerWindow = 100_000,
                MaximumRequestsPerOperation = maximumRequestsPerOperation,
            };
            service = new ArchidektService(
                new ArchidektSession(
                    new HttpClient(fake) { BaseAddress = options.BaseAddress },
                    ownsHttpClient: true,
                    options,
                    delay: (_, _) => Task.CompletedTask),
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
