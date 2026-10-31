using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MtgMcp.App.Archidekt;
using MtgMcp.App.Configuration;
using MtgMcp.Archidekt;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;

namespace MtgMcp.App.Tests;

/// <summary>
/// Exercises the App-owned boundary that composes Archidekt evidence with revisioned local decks.
/// </summary>
public sealed class ArchidektCoordinatorTests
{
    /// <summary>
    /// Provides one complete remote deck used by composed synchronization workflows.
    /// </summary>
    private const string RemoteDeck = """
        {
          "id": 42,
          "name": "Rate Safe Weenies",
          "description": "Dummy deck",
          "deckFormat": 3,
          "private": true,
          "categories": [
            { "id": 10, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true, "sortOrder": 0 }
          ],
          "cards": [
            {
              "deckRelationId": 101,
              "quantity": 3,
              "categories": ["Mainboard"],
              "card": {
                "id": 501,
                "uid": "33333333-3333-3333-3333-333333333333",
                "edition": { "editioncode": "dmu" },
                "collectorNumber": "278",
                "oracleCard": {
                  "uid": "44444444-4444-4444-4444-444444444444",
                  "name": "Island"
                }
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Verifies pull creation, diff, push replay, and remote-shell binding preserve exact guarded state.
    /// </summary>
    [Fact]
    public async Task SynchronizationWorkflows_ComposeProviderAndLocalState()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            for (int index = 0; index < 20; index++)
            {
                handler.Add(HttpMethod.Get, "api/decks/42/", RemoteDeck);
            }

            handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
            handler.Add(HttpMethod.Post, "api/decks/v2/", RemoteDeck, HttpStatusCode.Created);
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);

            ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            ArchidektApplyResult pulled = Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest(
                    "42",
                    LocalDeckId: null,
                    ExpectedLocalRevision: null,
                    pull.RemoteFingerprint,
                    pull.PreviewFingerprint),
                TestContext.Current.CancellationToken));
            Guid localDeckId = Assert.IsType<Guid>(pulled.LocalDeckId);
            DeckDocument local = Success(await store.GetAsync(
                localDeckId,
                TestContext.Current.CancellationToken));

            Assert.Equal("Rate Safe Weenies", local.Name);
            Assert.Single(local.Entries);
            Assert.Single(local.ProviderBindings);

            ArchidektSyncDiff diff = Success(await coordinator.DiffAsync(
                localDeckId,
                TestContext.Current.CancellationToken));
            Assert.DoesNotContain(diff.Differences, value => value.State == "remote-changed");

            ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                localDeckId,
                TestContext.Current.CancellationToken));
            ArchidektApplyResult pushed = Success(await coordinator.ApplyPushAsync(
                new ArchidektPushApplyRequest(
                    localDeckId,
                    local.Revision,
                    push.RemoteFingerprint,
                    push.PreviewFingerprint),
                TestContext.Current.CancellationToken));
            Assert.Equal("applied", pushed.Outcome);
            Assert.True(pushed.LocalRevision > local.Revision);
            Assert.Null(pushed.UnlinkedLocalIds);
            using JsonDocument pushedJson = JsonDocument.Parse(
                JsonSerializer.Serialize(pushed, MtgMcp.App.Hosting.ToolCallPolicy.SerializerOptions));
            Assert.False(pushedJson.RootElement.TryGetProperty("unlinkedLocalIds", out _));

            DeckDocument afterPush = Success(await store.GetAsync(
                localDeckId,
                TestContext.Current.CancellationToken));
            ArchidektSyncPreview replace = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId,
                TestContext.Current.CancellationToken));
            OperationResult<ArchidektApplyResult> stalePull = await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest(
                    "42",
                    localDeckId,
                    afterPush.Revision,
                    replace.RemoteFingerprint,
                    "wrong-preview"),
                TestContext.Current.CancellationToken);
            OperationConflict staleConflict = Assert.IsType<OperationConflict>(stalePull.Value);
            Assert.Equal("pull-preview-changed", staleConflict.ReasonCode);
            Assert.Equal(new ConflictState(afterPush.Revision, replace.RemoteFingerprint), staleConflict.Current);

            ArchidektApplyResult replaced = Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest(
                    "42",
                    localDeckId,
                    afterPush.Revision,
                    replace.RemoteFingerprint,
                    replace.PreviewFingerprint),
                TestContext.Current.CancellationToken));
            Assert.Equal("applied", replaced.Outcome);
            Assert.True(replaced.LocalRevision > afterPush.Revision);

            DeckDocument unbound = Success(await store.CreateAsync(
                new DeckCreateRequest("Unbound", Format: "commander"),
                TestContext.Current.CancellationToken));
            RemoteDeckSnapshot created = Success(await coordinator.CreateRemoteDeckAsync(
                unbound.DeckId,
                unbound.Revision,
                new ArchidektDeckCreateRequest("Rate Safe Weenies", "commander"),
                TestContext.Current.CancellationToken));
            DeckDocument bound = Success(await store.GetAsync(
                unbound.DeckId,
                TestContext.Current.CancellationToken));

            Assert.Equal("42", created.RemoteId);
            Assert.Equal("42", Assert.Single(bound.ProviderBindings).RemoteId);
            Assert.Null(Assert.Single(bound.ProviderBindings).BaselineFingerprint);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies a freshly pulled deck whose entry has several categories plans no push operation.
    /// </summary>
    [Fact]
    public async Task PreviewPush_AfterPullWithSeveralCategories_PlansNoOperations()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string deck = RemoteDeck
            .Replace(
                "{ \"id\": 10, \"name\": \"Mainboard\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 0 }",
                "{ \"id\": 10, \"name\": \"Removal\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 0 }," +
                "{ \"id\": 11, \"name\": \"Sideboard\", \"includedInDeck\": false, \"includedInPrice\": true, \"sortOrder\": 1 }," +
                "{ \"id\": 12, \"name\": \"Maybeboard\", \"includedInDeck\": false, \"includedInPrice\": false, \"sortOrder\": 2 }",
                StringComparison.Ordinal)
            .Replace(
                "\"categories\": [\"Mainboard\"]",
                "\"categories\": [\"Maybeboard\", \"Sideboard\", \"Removal\"]",
                StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            for (int index = 0; index < 4; index++)
            {
                handler.Add(HttpMethod.Get, "api/decks/42/", deck);
            }

            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            Guid localDeckId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, pull.RemoteFingerprint, pull.PreviewFingerprint),
                TestContext.Current.CancellationToken)).LocalDeckId);

            ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                localDeckId,
                TestContext.Current.CancellationToken));

            Assert.Empty(push.Operations);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies a remote deck with categories that differ only by case previews, applies, and plans no push change.
    /// </summary>
    [Fact]
    public async Task PullApply_WithCategoriesThatDifferOnlyByCase_KeepsBothAndPlansNoPush()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string deck = RemoteDeck
            .Replace(
                "{ \"id\": 10, \"name\": \"Mainboard\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 0 }",
                "{ \"id\": 10, \"name\": \"Mainboard\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 0 }," +
                "{ \"id\": 11, \"name\": \"Sideboard\", \"includedInDeck\": false, \"includedInPrice\": true, \"sortOrder\": 1 }," +
                "{ \"id\": 12, \"name\": \"SIDEBOARD\", \"includedInDeck\": false, \"includedInPrice\": true, \"sortOrder\": 2 }",
                StringComparison.Ordinal)
            .Replace(
                "\"categories\": [\"Mainboard\"]",
                "\"categories\": [\"Mainboard\", \"SIDEBOARD\"]",
                StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            for (int index = 0; index < 4; index++)
            {
                handler.Add(HttpMethod.Get, "api/decks/42/", deck);
            }

            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            Guid localDeckId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, pull.RemoteFingerprint, pull.PreviewFingerprint),
                TestContext.Current.CancellationToken)).LocalDeckId);

            DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
            ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                localDeckId,
                TestContext.Current.CancellationToken));

            Assert.Equal(
                ["Mainboard", "SIDEBOARD", "Sideboard"],
                local.Categories.Select(category => category.Name).Order(StringComparer.Ordinal));
            Assert.Empty(push.Operations);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies the same remote deck can be imported into two local decks, and each keeps its own rows and a clean
    /// push preview.
    /// </summary>
    [Fact]
    public async Task ApplyPull_ImportingTheSameRemoteDeckTwice_GivesEachLocalDeckItsOwnRows()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            for (int index = 0; index < 12; index++)
            {
                handler.Add(HttpMethod.Get, "api/decks/42/", RemoteDeck);
            }

            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            List<DeckDocument> decks = [];
            for (int import = 0; import < 2; import++)
            {
                ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId: null,
                    TestContext.Current.CancellationToken));
                Guid localDeckId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest("42", null, null, pull.RemoteFingerprint, pull.PreviewFingerprint),
                    TestContext.Current.CancellationToken)).LocalDeckId);
                decks.Add(Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken)));
                ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));
                Assert.Empty(push.Operations);
            }

            Assert.NotEqual(decks[0].DeckId, decks[1].DeckId);
            Assert.Empty(decks[0].Entries.Select(entry => entry.EntryId).Intersect(
                decks[1].Entries.Select(entry => entry.EntryId)));
            Assert.Empty(decks[0].Categories.Select(category => category.CategoryId).Intersect(
                decks[1].Categories.Select(category => category.CategoryId)));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Provides a remote deck with several entries and categories listed in a different order from the one the
    /// local store returns them in.
    /// </summary>
    private const string RichRemoteDeck = """
        {
          "id": 42,
          "name": "Rich",
          "description": "",
          "deckFormat": 3,
          "private": true,
          "categories": [
            { "id": 12, "name": "Maybeboard", "includedInDeck": false, "includedInPrice": false, "sortOrder": 2 },
            { "id": 11, "name": "Sideboard", "includedInDeck": false, "includedInPrice": true, "sortOrder": 1 },
            { "id": 10, "name": "Removal", "includedInDeck": true, "includedInPrice": true, "sortOrder": 0 }
          ],
          "cards": [
            {
              "deckRelationId": 103, "quantity": 1, "categories": ["Maybeboard"],
              "card": { "id": 503, "uid": "33333333-3333-3333-3333-333333333303", "edition": { "editioncode": "dmu" },
                "collectorNumber": "3", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444403", "name": "Charm" } }
            },
            {
              "deckRelationId": 101, "quantity": 2, "categories": ["Removal", "Sideboard"],
              "card": { "id": 501, "uid": "33333333-3333-3333-3333-333333333301", "edition": { "editioncode": "dmu" },
                "collectorNumber": "1", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444401", "name": "Bolt" } }
            },
            {
              "deckRelationId": 102, "quantity": 1, "categories": ["Sideboard"],
              "card": { "id": 502, "uid": "33333333-3333-3333-3333-333333333302", "edition": { "editioncode": "dmu" },
                "collectorNumber": "2", "oracleCard": { "uid": "44444444-4444-4444-4444-444444444402", "name": "Anger" } }
            }
          ]
        }
        """;

    /// <summary>
    /// Verifies a freshly pulled deck is in sync with its own baseline: no pull differences, no conflict, and no
    /// push operation. Editing it is seen as a local change, and reverting the edit clears it.
    /// </summary>
    [Fact]
    public async Task PullThenPreview_OnAFreshDeck_ShowsNoDifferences_AndRevertingClearsLocalChange()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            for (int index = 0; index < 12; index++)
            {
                handler.Add(HttpMethod.Get, "api/decks/42/", RichRemoteDeck);
            }

            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            Guid localDeckId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, pull.RemoteFingerprint, pull.PreviewFingerprint),
                TestContext.Current.CancellationToken)).LocalDeckId);

            ArchidektSyncPreview fresh = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId,
                TestContext.Current.CancellationToken));
            Assert.False(fresh.HasConflicts);
            Assert.Empty(fresh.Differences);

            DeckDocument original = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
            DeckEntry entry = original.Entries.First(value => value.CardName == "Bolt");
            DeckDocument edited = Success(await store.ApplyChangesAsync(
                localDeckId,
                original.Revision,
                [new UpdateDeckEntryChange(entry with { Quantity = 3 })],
                TestContext.Current.CancellationToken));
            ArchidektSyncPreview changed = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId,
                TestContext.Current.CancellationToken));
            Assert.True(changed.HasConflicts);

            DeckDocument reverted = Success(await store.ApplyChangesAsync(
                localDeckId,
                edited.Revision,
                [new UpdateDeckEntryChange(entry)],
                TestContext.Current.CancellationToken));
            ArchidektSyncPreview clean = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId,
                TestContext.Current.CancellationToken));

            Assert.True(reverted.Revision > original.Revision);
            Assert.False(clean.HasConflicts);
            Assert.Empty(clean.Differences);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies the same edit made on both sides is converged: no conflict and no operation, in a push and a pull,
    /// and applying either advances the baseline.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdenticalEditsOnBothSides_Converge(bool throughPull)
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string changedRemote = RichRemoteDeck.Replace(
            "\"deckRelationId\": 101, \"quantity\": 2",
            "\"deckRelationId\": 101, \"quantity\": 3",
            StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            handler.SetStanding("api/decks/42/", RichRemoteDeck);
            handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");

            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview first = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            Guid localDeckId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, first.RemoteFingerprint, first.PreviewFingerprint),
                TestContext.Current.CancellationToken)).LocalDeckId);
            DeckDocument original = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
            DeckEntry bolt = original.Entries.First(value => value.CardName == "Bolt");
            DeckDocument edited = Success(await store.ApplyChangesAsync(
                localDeckId,
                original.Revision,
                [new UpdateDeckEntryChange(bolt with { Quantity = 3 })],
                TestContext.Current.CancellationToken));
            handler.SetStanding("api/decks/42/", changedRemote);

            if (throughPull)
            {
                ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    TestContext.Current.CancellationToken));
                Assert.False(pull.HasConflicts);
                Assert.Contains(pull.Differences, value => value.State == "converged");
                ArchidektApplyResult applied = Success(await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest(
                        "42",
                        localDeckId,
                        edited.Revision,
                        pull.RemoteFingerprint,
                        pull.PreviewFingerprint),
                    TestContext.Current.CancellationToken));
                Assert.Equal("applied", applied.Outcome);
            }
            else
            {
                ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));
                Assert.False(push.HasConflicts);
                Assert.Empty(push.Operations);
                Assert.Contains(push.Differences, value => value.State == "converged");
                ArchidektApplyResult applied = Success(await coordinator.ApplyPushAsync(
                    new ArchidektPushApplyRequest(
                        localDeckId,
                        edited.Revision,
                        push.RemoteFingerprint,
                        push.PreviewFingerprint),
                    TestContext.Current.CancellationToken));
                Assert.Equal("applied", applied.Outcome);
            }

            ArchidektSyncPreview after = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId,
                TestContext.Current.CancellationToken));
            Assert.False(after.HasConflicts);
            Assert.Empty(after.Differences);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Pulls the rich deck and returns the coordinator, store, and local deck ID for push tests.
    /// </summary>
    private static async Task<(ArchidektCoordinator Coordinator, SqliteDeckStore Store, Guid LocalDeckId)>
        PullRichDeckAsync(QueueHandler handler, string root)
    {
        handler.SetStanding("api/decks/42/", RichRemoteDeck);
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        ArchidektService service = CreateService(handler);
        SqliteDeckStore store = new(root, "test");
        ArchidektCoordinator coordinator = new(service, store);
        ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
            "42",
            localDeckId: null,
            TestContext.Current.CancellationToken));
        Guid localDeckId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
            new ArchidektPullApplyRequest("42", null, null, pull.RemoteFingerprint, pull.PreviewFingerprint),
            TestContext.Current.CancellationToken)).LocalDeckId);
        return (coordinator, store, localDeckId);
    }

    /// <summary>
    /// Verifies a push is refused, naming the cards, when a card is put in the deck zone while its primary category is
    /// not included in the deck; both the preview and the apply refuse, and nothing is sent.
    /// </summary>
    [Fact]
    public async Task Push_WithADeckZoneEntryWhosePrimaryCategoryIsOutsideTheDeck_IsRefusedByPreviewAndApply()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                DeckEntry anger = local.Entries.First(value => value.CardName == "Anger");
                DeckDocument moved = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    [new UpdateDeckEntryChange(anger with { Zone = "main" })],
                    TestContext.Current.CancellationToken));
                int before = handler.Requests.Count(value => value.Method != HttpMethod.Get);

                OperationInvalidInput preview = Assert.IsType<OperationInvalidInput>(
                    (await coordinator.PreviewPushAsync(localDeckId, TestContext.Current.CancellationToken)).Value);
                OperationInvalidInput apply = Assert.IsType<OperationInvalidInput>(
                    (await coordinator.ApplyPushAsync(
                        new ArchidektPushApplyRequest(localDeckId, moved.Revision, "remote", "preview"),
                        TestContext.Current.CancellationToken)).Value);

                Assert.Equal("entry-zone-contradicts-category", preview.ReasonCode);
                Assert.Equal(preview.ReasonCode, apply.ReasonCode);
                Assert.Contains("'Anger'", preview.Message, StringComparison.Ordinal);
                Assert.Contains("'sideboard'", preview.Message, StringComparison.Ordinal);
                Assert.Equal(before, handler.Requests.Count(value => value.Method != HttpMethod.Get));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a push whose verification finds a difference records the verified remote state but keeps the
    /// difference Archidekt did not take visible as a local change: a pull conflicts, and a push plans only that
    /// difference.
    /// </summary>
    [Fact]
    public async Task ApplyPush_WithAVerificationMismatch_RecordsTheAppliedBaseline()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                DeckEntry bolt = local.Entries.First(value => value.CardName == "Bolt");
                DeckDocument edited = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    [new UpdateDeckEntryChange(bolt with { Quantity = 3 })],
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview preview = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));

                ArchidektApplyResult result = Success(await coordinator.ApplyPushAsync(
                    new ArchidektPushApplyRequest(
                        localDeckId,
                        edited.Revision,
                        preview.RemoteFingerprint,
                        preview.PreviewFingerprint),
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview again = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));

                Assert.Equal("verification-mismatch", result.Outcome);
                Assert.True(result.LocalRevision > edited.Revision);
                Assert.True(pull.HasConflicts);
                Assert.Contains(pull.Differences, value => value.State == "local-changed");
                Assert.DoesNotContain(again.Operations, value => value.Kind == "entry-add");
                Assert.Equal(["entry-update:Bolt"], again.Operations.Select(value => $"{value.Kind}:{value.Subject}"));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a push that adds a card whose verification then differs links the created card to its Archidekt
    /// row, so the next push does not add it again, and a pull does not silently revert the local deck.
    /// </summary>
    [Fact]
    public async Task ApplyPush_AddingACardWithAVerificationMismatch_LinksTheCreatedCard()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        const string shock = "{ \"deckRelationId\": 104, \"quantity\": 2, \"categories\": [\"Removal\"], " +
            "\"card\": { \"id\": 9001, \"uid\": \"33333333-3333-3333-3333-333333333309\", " +
            "\"edition\": { \"editioncode\": \"dmu\" }, \"collectorNumber\": \"9\", " +
            "\"oracleCard\": { \"uid\": \"44444444-4444-4444-4444-444444444409\", \"name\": \"Shock\" } } },";
        string withShock = RichRemoteDeck.Replace("\"cards\": [", "\"cards\": [" + shock, StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            // The preview searches once, and the apply runs the preview again.
            handler.SetStanding(
                "api/cards/v2/?name=Shock&pageSize=25",
                "{ \"count\": 1, \"next\": null, \"results\": [ { \"id\": 9001, " +
                "\"uid\": \"33333333-3333-3333-3333-333333333309\", \"collectorNumber\": \"9\", " +
                "\"edition\": { \"editioncode\": \"dmu\" }, \"oracleCard\": { \"name\": \"Shock\" } } ] }");
            handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                Guid entryId = Guid.NewGuid();
                DeckCategory removal = local.Categories.First(value => value.Name == "Removal");
                DeckDocument edited = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    [
                        new AddDeckEntryChange(new DeckEntryDraft(1, "Shock", EntryId: entryId)),
                        new AssignDeckCategoryChange(entryId, removal.CategoryId, true),
                    ],
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview preview = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));
                Assert.Contains(preview.Operations, value => value.Kind == "entry-add");
                for (int count = 0; count < 3; count++)
                {
                    handler.Add(HttpMethod.Get, "api/decks/42/", RichRemoteDeck);
                }

                handler.SetStanding("api/decks/42/", withShock);

                ArchidektApplyResult result = Success(await coordinator.ApplyPushAsync(
                    new ArchidektPushApplyRequest(
                        localDeckId,
                        edited.Revision,
                        preview.RemoteFingerprint,
                        preview.PreviewFingerprint),
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview again = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    TestContext.Current.CancellationToken));

                Assert.Equal("verification-mismatch", result.Outcome);
                Assert.Null(result.UnlinkedLocalIds);
                Assert.DoesNotContain(again.Operations, value => value.Kind is "entry-add" or "entry-remove");
                Assert.Equal(["entry-update:Shock"], again.Operations.Select(value => $"{value.Kind}:{value.Subject}"));
                Assert.True(pull.HasConflicts);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies Archidekt renumbering entry order numbers, without any user edit, is not a remote change, so a local
    /// edit still pushes without a conflict.
    /// </summary>
    [Fact]
    public async Task Push_AfterArchidektRenumbersEntryOrder_HasNoRemoteChange()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string renumbered = RichRemoteDeck
            .Replace("\"deckRelationId\": 103,", "\"deckRelationId\": 103, \"order\": 50,", StringComparison.Ordinal)
            .Replace("\"deckRelationId\": 101,", "\"deckRelationId\": 101, \"order\": 60,", StringComparison.Ordinal)
            .Replace("\"deckRelationId\": 102,", "\"deckRelationId\": 102, \"order\": 70,", StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                DeckEntry bolt = local.Entries.First(value => value.CardName == "Bolt");
                _ = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    [new UpdateDeckEntryChange(bolt with { Quantity = 3 })],
                    TestContext.Current.CancellationToken));
                handler.SetStanding("api/decks/42/", renumbered);

                ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));

                Assert.False(push.HasConflicts);
                Assert.DoesNotContain(push.Differences, value => value.State.StartsWith("remote-", StringComparison.Ordinal));
                Assert.Equal(["entry-update:Bolt"], push.Operations.Select(value => $"{value.Kind}:{value.Subject}"));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a deck that is stuck as locally changed has a supported way out: a pull preview that discards local
    /// changes lists them without a conflict, its apply takes the Archidekt state, and a plain apply still refuses.
    /// </summary>
    [Fact]
    public async Task Pull_WithDiscardLocalChanges_TakesTheRemoteStateOnlyWhenAskedFor()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                DeckEntry bolt = local.Entries.First(value => value.CardName == "Bolt");
                DeckDocument edited = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    [new UpdateDeckEntryChange(bolt with { Quantity = 9 })],
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview plain = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview discard = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    discardLocalChanges: true,
                    TestContext.Current.CancellationToken));

                Assert.True(plain.HasConflicts);
                Assert.False(discard.HasConflicts);
                Assert.NotEqual(plain.PreviewFingerprint, discard.PreviewFingerprint);
                Assert.Contains(discard.Differences, value => value.State.StartsWith("local-", StringComparison.Ordinal));
                OperationConflict refused = Assert.IsType<OperationConflict>((await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest(
                        "42",
                        localDeckId,
                        edited.Revision,
                        discard.RemoteFingerprint,
                        discard.PreviewFingerprint),
                    TestContext.Current.CancellationToken)).Value);
                Assert.Equal("pull-conflict", refused.ReasonCode);

                ArchidektApplyResult applied = Success(await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest(
                        "42",
                        localDeckId,
                        edited.Revision,
                        discard.RemoteFingerprint,
                        discard.PreviewFingerprint,
                        DiscardLocalChanges: true),
                    TestContext.Current.CancellationToken));
                DeckDocument after = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                ArchidektSyncPreview clean = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    TestContext.Current.CancellationToken));

                Assert.Equal("applied", applied.Outcome);
                Assert.Equal(2, after.Entries.First(value => value.CardName == "Bolt").Quantity);
                Assert.False(clean.HasConflicts);
                Assert.Empty(clean.Differences);
                Assert.IsType<OperationInvalidInput>((await coordinator.PreviewPullAsync(
                    "42",
                    null,
                    discardLocalChanges: true,
                    TestContext.Current.CancellationToken)).Value);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a converged edit beside a local edit to a field no row shows (category color, entry order) still
    /// blocks a pull, so the pull cannot silently overwrite it.
    /// </summary>
    [Theory]
    [InlineData("color")]
    [InlineData("sortOrder")]
    public async Task Pull_ConvergedEditBesideAHiddenLocalEdit_IsAConflict(string hidden)
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string changedRemote = RichRemoteDeck.Replace(
            "\"deckRelationId\": 101, \"quantity\": 2",
            "\"deckRelationId\": 101, \"quantity\": 3",
            StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                DeckEntry bolt = local.Entries.First(value => value.CardName == "Bolt");
                DeckCategory removal = local.Categories.First(value => value.Name == "Removal");
                List<DeckChange> changes = hidden == "color"
                    ? [new UpdateDeckCategoryChange(removal with { Color = "#ff0000" }),
                        new UpdateDeckEntryChange(bolt with { Quantity = 3 })]
                    : [new UpdateDeckEntryChange(bolt with { Quantity = 3, SortOrder = 777 })];
                DeckDocument edited = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    changes,
                    TestContext.Current.CancellationToken));
                handler.SetStanding("api/decks/42/", changedRemote);

                ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    TestContext.Current.CancellationToken));
                OperationConflict refused = Assert.IsType<OperationConflict>((await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest(
                        "42",
                        localDeckId,
                        edited.Revision,
                        pull.RemoteFingerprint,
                        pull.PreviewFingerprint),
                    TestContext.Current.CancellationToken)).Value);

                Assert.True(pull.HasConflicts);
                Assert.Contains(pull.Differences, value => value.State == "converged");
                Assert.Contains(pull.Differences, value => value.Path == "/localOnly/content");
                Assert.Equal("pull-conflict", refused.ReasonCode);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a deck that 0.10.0 pulled, with a baseline made by the 0.10.0 pull hash, reads as unchanged through the
    /// store even when the store normalized the drafted values (mixed-case set code and language), and that one local
    /// edit then reads as a local change.
    /// </summary>
    [Fact]
    public async Task Preview_OfADeckPulledByVersion0100_ReadsCleanThroughTheStoreUntilEdited()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string mixedCase = RichRemoteDeck
            .Replace("\"id\": 501,", "\"id\": 501, \"setCode\": \"DMU\",", StringComparison.Ordinal)
            .Replace("\"deckRelationId\": 102,", "\"deckRelationId\": 102, \"language\": \"EN\",", StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, ArchidektService service, DeckDocument created) =
                await CreateEarlierVersionDeckAsync(handler, root, mixedCase, faithfulPullFingerprint: true);
            using (service)
            using (store)
            {
                ArchidektSyncPreview clean = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    TestContext.Current.CancellationToken));
                DeckEntry bolt = created.Entries.First(value => value.CardName == "Bolt");
                _ = Success(await store.ApplyChangesAsync(
                    created.DeckId,
                    created.Revision,
                    [new UpdateDeckEntryChange(bolt with { Quantity = 5 })],
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview edited = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    TestContext.Current.CancellationToken));

                Assert.Contains(created.Entries, value => value.SetCode == "dmu" && value.CardName == "Bolt");
                Assert.Contains(created.Entries, value => value.Language == "en" && value.CardName == "Anger");
                Assert.False(clean.HasConflicts);
                Assert.Empty(clean.Differences);
                Assert.True(edited.HasConflicts);
                Assert.Contains(edited.Differences, value => value.State.StartsWith("local-", StringComparison.Ordinal));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies that on a deck 0.10.0 pulled, an edit to a value the store normalizes (set code, language, finish,
    /// collector number) still reads as a local change, so the drafted values put back for the 0.10.0 hash never hide
    /// a real edit.
    /// </summary>
    [Theory]
    [InlineData("set-code")]
    [InlineData("language")]
    [InlineData("finish")]
    [InlineData("collector-number")]
    public async Task Preview_OfADeckPulledByVersion0100_ShowsAnEditToANormalizedField(string field)
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string mixedCase = RichRemoteDeck
            .Replace("\"id\": 501,", "\"id\": 501, \"setCode\": \"DMU\",", StringComparison.Ordinal)
            .Replace("\"deckRelationId\": 102,", "\"deckRelationId\": 102, \"language\": \"EN\",", StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, ArchidektService service, DeckDocument created) =
                await CreateEarlierVersionDeckAsync(handler, root, mixedCase, faithfulPullFingerprint: true);
            using (service)
            using (store)
            {
                DeckEntry bolt = created.Entries.First(value => value.CardName == "Bolt");
                DeckEntry anger = created.Entries.First(value => value.CardName == "Anger");
                DeckEntry edited = field switch
                {
                    "set-code" => bolt with { SetCode = "one" },
                    "language" => anger with { Language = "ja" },
                    "finish" => bolt with { Finish = bolt.Finish == "foil" ? "nonfoil" : "foil" },
                    _ => bolt with { CollectorNumber = "999" },
                };
                _ = Success(await store.ApplyChangesAsync(
                    created.DeckId,
                    created.Revision,
                    [new UpdateDeckEntryChange(edited)],
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview preview = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    TestContext.Current.CancellationToken));

                Assert.True(preview.HasConflicts);
                Assert.Contains(preview.Differences, value => value.State.StartsWith("local-", StringComparison.Ordinal));
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a deck made by an earlier version (provider-only IDs, a schema 1 baseline that never matched the stored
    /// row order and recorded no category colors or ordering) reads as an unverified local change, and a pull is
    /// refused with a message that says why and names both exits.
    /// </summary>
    [Fact]
    public async Task Pull_OfADeckMadeByAnEarlierVersion_IsRefusedAsAnUnverifiedLocalChange()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, ArchidektService service, DeckDocument created) =
                await CreateEarlierVersionDeckAsync(handler, root);
            using (service)
            using (store)
            {
                ArchidektSyncDiff diff = Success(await coordinator.DiffAsync(
                    created.DeckId,
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview plain = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    TestContext.Current.CancellationToken));
                OperationConflict refused = Assert.IsType<OperationConflict>((await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest(
                        "42",
                        created.DeckId,
                        created.Revision,
                        plain.RemoteFingerprint,
                        plain.PreviewFingerprint),
                    TestContext.Current.CancellationToken)).Value);

                ArchidektDifference row = Assert.Single(plain.Differences);
                Assert.Equal("/localOnly/content", row.Path);
                Assert.Equal("local-changed-unverified", row.State);
                Assert.Contains(diff.Differences, value => value.State == "local-changed-unverified");
                Assert.True(plain.HasConflicts);
                Assert.Equal("pull-conflict", refused.ReasonCode);
                Assert.Contains("did not record category colors or ordering", refused.Message, StringComparison.Ordinal);
                Assert.Contains("Push the deck to keep the local content", refused.Message, StringComparison.Ordinal);
                Assert.Contains("discardLocalChanges", refused.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a pull with discardLocalChanges takes the Archidekt state over an unverified local change, binds to its
    /// own preview, and leaves a schema 2 baseline so the next preview shows nothing.
    /// </summary>
    [Fact]
    public async Task Pull_OfADeckMadeByAnEarlierVersion_WithDiscardLocalChanges_SucceedsAndClearsTheRow()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, ArchidektService service, DeckDocument created) =
                await CreateEarlierVersionDeckAsync(handler, root);
            using (service)
            using (store)
            {
                ArchidektSyncPreview plain = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview discard = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    discardLocalChanges: true,
                    TestContext.Current.CancellationToken));
                OperationConflict mismatched = Assert.IsType<OperationConflict>((await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest(
                        "42",
                        created.DeckId,
                        created.Revision,
                        plain.RemoteFingerprint,
                        plain.PreviewFingerprint,
                        DiscardLocalChanges: true),
                    TestContext.Current.CancellationToken)).Value);
                ArchidektApplyResult applied = Success(await coordinator.ApplyPullAsync(
                    new ArchidektPullApplyRequest(
                        "42",
                        created.DeckId,
                        created.Revision,
                        discard.RemoteFingerprint,
                        discard.PreviewFingerprint,
                        DiscardLocalChanges: true),
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview clean = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    TestContext.Current.CancellationToken));

                Assert.False(discard.HasConflicts);
                Assert.Contains(discard.Differences, value => value.State == "local-changed-unverified");
                Assert.Equal("pull-preview-changed", mismatched.ReasonCode);
                Assert.Equal("applied", applied.Outcome);
                Assert.False(clean.HasConflicts);
                Assert.Empty(clean.Differences);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a push is not blocked by the unverified row and plans nothing from it: a push with no edit has no
    /// operation, a push with a real edit (which the rows then explain) plans only that edit, and either one leaves a
    /// schema 2 baseline, so the row is gone and a pull then sees no change.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Push_OfADeckMadeByAnEarlierVersion_IsNotBlockedAndClearsTheRow(bool withEdit)
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string accepted = RichRemoteDeck.Replace(
            "\"deckRelationId\": 101, \"quantity\": 2",
            "\"deckRelationId\": 101, \"quantity\": 5",
            StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, ArchidektService service, DeckDocument created) =
                await CreateEarlierVersionDeckAsync(handler, root);
            using (service)
            using (store)
            {
                if (withEdit)
                {
                    handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
                    DeckEntry bolt = created.Entries.First(value => value.CardName == "Bolt");
                    _ = Success(await store.ApplyChangesAsync(
                        created.DeckId,
                        created.Revision,
                        [new UpdateDeckEntryChange(bolt with { Quantity = 5 })],
                        TestContext.Current.CancellationToken));
                }

                ArchidektSyncPreview preview = Success(await coordinator.PreviewPushAsync(
                    created.DeckId,
                    TestContext.Current.CancellationToken));
                if (withEdit)
                {
                    handler.SetStanding("api/decks/42/", accepted);
                }

                ArchidektApplyResult pushed = await PushAsync(coordinator, created.DeckId);
                ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                    "42",
                    created.DeckId,
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview again = Success(await coordinator.PreviewPushAsync(
                    created.DeckId,
                    TestContext.Current.CancellationToken));

                Assert.False(preview.HasConflicts);
                Assert.Equal(
                    !withEdit,
                    preview.Differences.Any(value => value.State == "local-changed-unverified"));
                Assert.Equal(
                    withEdit ? ["entry-update:Bolt"] : [],
                    preview.Operations.Select(value => $"{value.Kind}:{value.Subject}"));
                Assert.Equal("applied", pushed.Outcome);
                Assert.False(pull.HasConflicts);
                Assert.Empty(pull.Differences);
                Assert.Empty(again.Operations);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Builds a local deck the way the earlier version did: provider-only IDs and a schema 1 baseline whose local
    /// fingerprint matches no current ordering and that has no hidden-field fingerprint.
    /// </summary>
    private static async Task<(
        ArchidektCoordinator Coordinator,
        SqliteDeckStore Store,
        ArchidektService Service,
        DeckDocument Deck)>
        CreateEarlierVersionDeckAsync(
            QueueHandler handler,
            string root,
            string remoteJson = RichRemoteDeck,
            bool faithfulPullFingerprint = false)
    {
        handler.SetStanding("api/decks/42/", remoteJson);
        handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
        ArchidektService service = CreateService(handler);
        SqliteDeckStore store = new(root, "test");
        try
        {
            return await BuildEarlierVersionDeckAsync(service, store, remoteJson, faithfulPullFingerprint)
                .ConfigureAwait(false);
        }
        catch
        {
            store.Dispose();
            service.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Inserts the earlier-version deck into the store; the caller owns the service and store.
    /// </summary>
    private static async Task<(
        ArchidektCoordinator Coordinator,
        SqliteDeckStore Store,
        ArchidektService Service,
        DeckDocument Deck)>
        BuildEarlierVersionDeckAsync(
            ArchidektService service,
            SqliteDeckStore store,
            string remoteJson,
            bool faithfulPullFingerprint)
    {
        RemoteDeckSnapshot remote = ParseRemote(remoteJson);
        Guid bindingId = Guid.CreateVersion7();
        DeckProviderBinding binding = new(
            bindingId,
            "archidekt",
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            remote.RemoteFingerprint,
            DateTimeOffset.UtcNow,
            null);
        DeckCreateRequest modern = ArchidektLocalMapper.ToCreateRequest(remote, binding);
        Guid deckId = modern.DeckId!.Value;
        Dictionary<Guid, Guid> ids = [];
        foreach (RemoteDeckEntry entry in remote.Entries)
        {
            ids[ModernId("entry", deckId, entry.ProviderRelationId)] = LegacyId("entry", entry.ProviderRelationId);
        }

        foreach (RemoteDeckCategory category in remote.Categories)
        {
            ids[ModernId("category", deckId, category.ProviderCategoryId)] =
                LegacyId("category", category.ProviderCategoryId);
        }

        DeckCreateRequest legacy = modern with
        {
            Entries = [.. modern.Entries!.Select(value => value with { EntryId = ids[value.EntryId!.Value] })],
            Categories = [.. modern.Categories!.Select(value => value with { CategoryId = ids[value.CategoryId!.Value] })],
            CategoryAssignments =
            [
                .. modern.CategoryAssignments!.Select(value => value with
                {
                    EntryId = ids[value.EntryId],
                    CategoryId = ids[value.CategoryId],
                }),
            ],
        };
        JsonObject baseline = JsonNode.Parse(ArchidektLocalMapper.CreateBaseline(modern, remote))!.AsObject();
        baseline["schemaVersion"] = 1;
        baseline["localFingerprint"] = faithfulPullFingerprint
            ? Version0100PullFingerprint(legacy)
            : "legacy-draft-order-fingerprint";
        baseline.Remove("localInvisibleFingerprint");
        baseline["entryRelations"] = RemapKeys(baseline["entryRelations"]!.AsObject(), ids);
        baseline["categoryRelations"] = RemapKeys(baseline["categoryRelations"]!.AsObject(), ids);
        DeckDocument created = Success(await store.CreateSynchronizedAsync(
            legacy,
            new DeckSyncBaseline(bindingId, baseline.ToJsonString()),
            TestContext.Current.CancellationToken));
        return (new ArchidektCoordinator(service, store), store, service, created);
    }

    /// <summary>
    /// Port of the fingerprint 0.10.0 recorded when it pulled a deck: a hash of the creation draft with its rows in
    /// drafted order and values as drafted, before the store trimmed or lowercased any of them.
    /// </summary>
    private static string Version0100PullFingerprint(DeckCreateRequest draft)
    {
        return ArchidektContract.Fingerprint(new
        {
            draft.Name,
            draft.Description,
            draft.Format,
            entries = draft.Entries!.Select(value => new
            {
                value.EntryId,
                value.Quantity,
                value.CardName,
                value.OracleId,
                value.PrintingId,
                value.SetCode,
                value.CollectorNumber,
                value.Language,
                value.Finish,
                value.Zone,
                value.SortOrder,
            }),
            categories = draft.Categories!.Select(value => new
            {
                value.CategoryId,
                value.Name,
                value.Color,
                value.SortOrder,
            }),
            assignments = draft.CategoryAssignments!.Select(value => new
            {
                value.EntryId,
                value.CategoryId,
                value.IsPrimary,
            }),
        });
    }

    /// <summary>
    /// Replaces the keys of a JSON map of local IDs.
    /// </summary>
    private static JsonObject RemapKeys(JsonObject map, Dictionary<Guid, Guid> ids)
    {
        JsonObject result = [];
        foreach ((string key, JsonNode? value) in map)
        {
            result[ids[Guid.Parse(key)].ToString("D")] = value!.DeepClone();
        }

        return result;
    }

    /// <summary>
    /// Derives an ID the way the current version does: from the local deck and the provider row.
    /// </summary>
    private static Guid ModernId(string scope, Guid deckId, string providerId)
    {
        return StableGuid($"{scope}:{deckId:D}", providerId);
    }

    /// <summary>
    /// Derives an ID the way the earlier version did: from the provider row alone.
    /// </summary>
    private static Guid LegacyId(string scope, string providerId)
    {
        return StableGuid(scope, providerId);
    }

    /// <summary>
    /// Hashes a scope and provider ID to a version 5 style GUID, as the adapter does.
    /// </summary>
    private static Guid StableGuid(string scope, string providerId)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"archidekt:{scope}:{providerId}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes, bigEndian: true);
    }

    /// <summary>
    /// Holds a remote deck whose categories include two that differ only by case, with the card in one of them.
    /// </summary>
    private static readonly string CaseVariantDeck = RemoteDeck
        .Replace(
            "{ \"id\": 10, \"name\": \"Mainboard\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 0 }",
            "{ \"id\": 10, \"name\": \"Mainboard\", \"includedInDeck\": true, \"includedInPrice\": true, \"sortOrder\": 0 }," +
            "{ \"id\": 11, \"name\": \"Sideboard\", \"includedInDeck\": false, \"includedInPrice\": true, \"sortOrder\": 1 }," +
            "{ \"id\": 12, \"name\": \"SIDEBOARD\", \"includedInDeck\": false, \"includedInPrice\": true, \"sortOrder\": 2 }",
            StringComparison.Ordinal)
        .Replace("\"categories\": [\"Mainboard\"]", "\"categories\": [\"SIDEBOARD\"]", StringComparison.Ordinal);

    /// <summary>
    /// Verifies a push that moves a card between two categories that differ only by case sends the exact names and
    /// is planned as one card update, with no category change.
    /// </summary>
    [Fact]
    public async Task Push_MovingACardBetweenCaseVariantCategories_SendsTheExactName()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            handler.SetStanding("api/decks/42/", CaseVariantDeck);
            handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
            handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            Guid localDeckId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, pull.RemoteFingerprint, pull.PreviewFingerprint),
                TestContext.Current.CancellationToken)).LocalDeckId);
            DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
            DeckEntry island = Assert.Single(local.Entries);
            DeckCategory lower = local.Categories.First(value => value.Name == "Sideboard");
            DeckCategory upper = local.Categories.First(value => value.Name == "SIDEBOARD");
            DeckDocument moved = Success(await store.ApplyChangesAsync(
                localDeckId,
                local.Revision,
                [
                    new UnassignDeckCategoryChange(island.EntryId, upper.CategoryId),
                    new AssignDeckCategoryChange(island.EntryId, lower.CategoryId, true),
                ],
                TestContext.Current.CancellationToken));

            ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                localDeckId,
                TestContext.Current.CancellationToken));
            _ = await coordinator.ApplyPushAsync(
                new ArchidektPushApplyRequest(localDeckId, moved.Revision, push.RemoteFingerprint, push.PreviewFingerprint),
                TestContext.Current.CancellationToken);
            string body = handler.Bodies[handler.Requests.FindIndex(value => value.Method == HttpMethod.Patch)];

            Assert.Equal(["entry-update:Island"], push.Operations.Select(value => $"{value.Kind}:{value.Subject}"));
            Assert.Contains("\"categories\":[\"Sideboard\"]", body, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies the same remote deck imports again after the first copy lost its binding and was renamed.
    /// </summary>
    [Fact]
    public async Task ApplyPull_ImportingAgainAfterTheBindingWasRemovedAndTheDeckRenamed_Succeeds()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            handler.SetStanding("api/decks/42/", RemoteDeck);
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview first = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            Guid firstId = Assert.IsType<Guid>(Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, first.RemoteFingerprint, first.PreviewFingerprint),
                TestContext.Current.CancellationToken)).LocalDeckId);
            DeckDocument firstDeck = Success(await store.GetAsync(firstId, TestContext.Current.CancellationToken));
            _ = Success(await store.ApplyChangesAsync(
                firstId,
                firstDeck.Revision,
                [
                    new RemoveDeckProviderBindingChange(Assert.Single(firstDeck.ProviderBindings).BindingId),
                    new UpdateDeckMetadataChange("Renamed copy", null, firstDeck.Format),
                ],
                TestContext.Current.CancellationToken));

            ArchidektSyncPreview second = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));
            ArchidektApplyResult imported = Success(await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, second.RemoteFingerprint, second.PreviewFingerprint),
                TestContext.Current.CancellationToken));

            Assert.Equal("applied", imported.Outcome);
            Assert.NotEqual(firstId, imported.LocalDeckId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies edits to the same field that end in different values are still a conflict, and one converged row does
    /// not hide another row that really conflicts.
    /// </summary>
    [Fact]
    public async Task Push_WhenTheSameFieldEndsInDifferentValues_IsStillAConflict()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string remoteChanged = RichRemoteDeck
            .Replace("\"deckRelationId\": 101, \"quantity\": 2", "\"deckRelationId\": 101, \"quantity\": 4", StringComparison.Ordinal)
            .Replace("\"deckRelationId\": 102, \"quantity\": 1", "\"deckRelationId\": 102, \"quantity\": 6", StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                DeckEntry bolt = local.Entries.First(value => value.CardName == "Bolt");
                DeckEntry anger = local.Entries.First(value => value.CardName == "Anger");
                DeckDocument edited = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    [
                        new UpdateDeckEntryChange(bolt with { Quantity = 3 }),
                        new UpdateDeckEntryChange(anger with { Quantity = 6 }),
                    ],
                    TestContext.Current.CancellationToken));
                handler.SetStanding("api/decks/42/", remoteChanged);

                ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));
                OperationConflict refused = Assert.IsType<OperationConflict>((await coordinator.ApplyPushAsync(
                    new ArchidektPushApplyRequest(
                        localDeckId,
                        edited.Revision,
                        push.RemoteFingerprint,
                        push.PreviewFingerprint),
                    TestContext.Current.CancellationToken)).Value);

                Assert.True(push.HasConflicts);
                Assert.Contains(push.Differences, value => value.State == "concurrent-changed");
                Assert.Contains(push.Differences, value => value.State == "converged");
                Assert.Equal("push-conflict", refused.ReasonCode);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies the way out of a mismatch: after a first push that does not verify, a second push that does leaves the
    /// pull and push previews with no local change and no operation.
    /// </summary>
    [Fact]
    public async Task ApplyPush_AfterAMismatchThenAVerifiedPush_LeavesNothingToDo()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string accepted = RichRemoteDeck.Replace(
            "\"deckRelationId\": 101, \"quantity\": 2",
            "\"deckRelationId\": 101, \"quantity\": 3",
            StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
            handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
            (ArchidektCoordinator coordinator, SqliteDeckStore store, Guid localDeckId) =
                await PullRichDeckAsync(handler, root);
            using (store)
            {
                DeckDocument local = Success(await store.GetAsync(localDeckId, TestContext.Current.CancellationToken));
                DeckEntry bolt = local.Entries.First(value => value.CardName == "Bolt");
                _ = Success(await store.ApplyChangesAsync(
                    localDeckId,
                    local.Revision,
                    [new UpdateDeckEntryChange(bolt with { Quantity = 3 })],
                    TestContext.Current.CancellationToken));

                // First push: Archidekt ignores the change, so the verification differs.
                ArchidektApplyResult first = await PushAsync(coordinator, localDeckId);
                Assert.Equal("verification-mismatch", first.Outcome);

                // Second push: Archidekt now takes it.
                handler.SetStanding("api/decks/42/", accepted);
                ArchidektApplyResult second = await PushAsync(coordinator, localDeckId);
                ArchidektSyncPreview pull = Success(await coordinator.PreviewPullAsync(
                    "42",
                    localDeckId,
                    TestContext.Current.CancellationToken));
                ArchidektSyncPreview push = Success(await coordinator.PreviewPushAsync(
                    localDeckId,
                    TestContext.Current.CancellationToken));

                Assert.Equal("applied", second.Outcome);
                Assert.False(pull.HasConflicts);
                Assert.Empty(pull.Differences);
                Assert.Empty(push.Operations);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Previews and applies one push of a local deck.
    /// </summary>
    private static async Task<ArchidektApplyResult> PushAsync(ArchidektCoordinator coordinator, Guid localDeckId)
    {
        ArchidektSyncPreview preview = Success(await coordinator.PreviewPushAsync(
            localDeckId,
            TestContext.Current.CancellationToken));
        return Success(await coordinator.ApplyPushAsync(
            new ArchidektPushApplyRequest(
                localDeckId,
                preview.LocalRevision!.Value,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies a row the last push left unlinked does not block the next push, which removes and re-adds it.
    /// </summary>
    [Fact]
    public async Task PreviewPush_AfterPushLeftRowUnlinked_PlansRemoveAndAddWithoutDuplicate()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string withHelix = RemoteDeck.Replace(
            "\"cards\": [",
            "\"cards\": [ { \"deckRelationId\": 102, \"quantity\": 1, \"categories\": [\"Mainboard\"], " +
            "\"card\": { \"id\": 777, \"uid\": \"55555555-5555-5555-5555-555555555555\", " +
            "\"edition\": { \"editioncode\": \"dmu\" }, \"collectorNumber\": \"1\", " +
            "\"oracleCard\": { \"uid\": \"66666666-6666-6666-6666-666666666666\", \"name\": \"Lightning Helix\" } } },",
            StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            for (int index = 0; index < 4; index++)
            {
                handler.Add(HttpMethod.Get, "api/decks/42/", withHelix);
            }

            handler.SetStanding(
                "api/cards/v2/?name=Boros%20Charm&pageSize=25",
                "{ \"count\": 1, \"next\": null, \"results\": [ { \"id\": 778, " +
                "\"uid\": \"55555555-5555-5555-5555-555555555578\", \"collectorNumber\": \"2\", " +
                "\"edition\": { \"editioncode\": \"dmu\" }, \"oracleCard\": { \"name\": \"Boros Charm\" } } ] }");
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            RemoteDeckSnapshot pulled = ParseRemote(RemoteDeck);
            RemoteDeckSnapshot afterPush = ParseRemote(withHelix);
            Guid bindingId = Guid.CreateVersion7();
            DeckProviderBinding binding = new(
                bindingId,
                "archidekt",
                pulled.RemoteId,
                pulled.RemoteUri,
                pulled.Evidence.ContractVersion,
                pulled.RemoteFingerprint,
                DateTimeOffset.UtcNow,
                null);
            DeckCreateRequest create = ArchidektLocalMapper.ToCreateRequest(pulled, binding);
            DeckDocument created = Success(await store.CreateSynchronizedAsync(
                create,
                new DeckSyncBaseline(bindingId, ArchidektLocalMapper.CreateBaseline(create, pulled)),
                TestContext.Current.CancellationToken));
            Guid charmId = Guid.NewGuid();
            DeckDocument withCharm = Success(await store.ApplyChangesAsync(
                created.DeckId,
                created.Revision,
                [new AddDeckEntryChange(new DeckEntryDraft(1, "Boros Charm", EntryId: charmId, SortOrder: 9))],
                TestContext.Current.CancellationToken));
            ArchidektSyncBaseline pullBaseline = ArchidektLocalMapper.ParseBaseline(
                ArchidektLocalMapper.CreateBaseline(create, pulled));
            RemoteDeckSnapshot pushed = ArchidektLocalMapper.ToRemoteTarget(withCharm, pullBaseline, pulled);

            // The provider created a row for the charm that the push could not match to it by content.
            (string baselineJson, IReadOnlyList<Guid>? unlinkedLocalIds) =
                ArchidektLocalMapper.CreatePushBaseline(withCharm, afterPush, pushed);
            Assert.Equal([charmId], unlinkedLocalIds);
            _ = Success(await store.ApplyChangesAsync(
                withCharm.DeckId,
                withCharm.Revision,
                [new UpsertDeckProviderBindingChange(
                    binding with { BaselineFingerprint = afterPush.RemoteFingerprint },
                    baselineJson)],
                TestContext.Current.CancellationToken));
            DeckDocument stored = Success(await store.GetAsync(
                created.DeckId,
                TestContext.Current.CancellationToken));
            DeckEntry charm = stored.Entries.Single(value => value.EntryId == charmId);
            _ = Success(await store.ApplyChangesAsync(
                stored.DeckId,
                stored.Revision,
                [new UpdateDeckEntryChange(charm with { Quantity = 2 })],
                TestContext.Current.CancellationToken));
            ArchidektCoordinator coordinator = new(service, store);

            ArchidektSyncPreview preview = Success(await coordinator.PreviewPushAsync(
                created.DeckId,
                TestContext.Current.CancellationToken));

            Assert.Equal(
                ["entry-add:Boros Charm", "entry-remove:Lightning Helix"],
                preview.Operations.Select(value => $"{value.Kind}:{value.Subject}").Order().ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies a pull apply over local edits made since the baseline reports the current revision and remote state.
    /// </summary>
    [Fact]
    public async Task ApplyPull_LocalEditsSinceBaseline_ReturnsPullConflictWithCurrentState()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Get, "api/decks/42/", RemoteDeck);
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            RemoteDeckSnapshot remote = ParseRemote(RemoteDeck);
            DeckDocument bound = await BindLocalDeckAsync(store, remote);
            DeckEntry entry = bound.Entries.Single();
            DeckDocument edited = Success(await store.ApplyChangesAsync(
                bound.DeckId,
                bound.Revision,
                [new UpdateDeckEntryChange(entry with { Quantity = 5 })],
                TestContext.Current.CancellationToken));
            ArchidektCoordinator coordinator = new(service, store);

            OperationResult<ArchidektApplyResult> result = await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", edited.DeckId, edited.Revision, "remote", "preview"),
                TestContext.Current.CancellationToken);

            OperationConflict conflict = Assert.IsType<OperationConflict>(result.Value);
            Assert.Equal("pull-conflict", conflict.ReasonCode);
            Assert.Equal(new ConflictState(edited.Revision, remote.RemoteFingerprint), conflict.Current);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a pull apply whose remote deck changes after the preview reports the new remote fingerprint.
    /// </summary>
    [Fact]
    public async Task ApplyPull_RemoteChangesAfterPreview_ReturnsRemoteDeckChangedWithCurrentFingerprint()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string changed = RemoteDeck.Replace("\"quantity\": 3", "\"quantity\": 4", StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Get, "api/decks/42/", RemoteDeck);
            handler.Add(HttpMethod.Get, "api/decks/42/", RemoteDeck);
            handler.Add(HttpMethod.Get, "api/decks/42/", changed);
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektSyncPreview preview = Success(await coordinator.PreviewPullAsync(
                "42",
                localDeckId: null,
                TestContext.Current.CancellationToken));

            OperationResult<ArchidektApplyResult> result = await coordinator.ApplyPullAsync(
                new ArchidektPullApplyRequest(
                    "42",
                    LocalDeckId: null,
                    ExpectedLocalRevision: null,
                    preview.RemoteFingerprint,
                    preview.PreviewFingerprint),
                TestContext.Current.CancellationToken);

            OperationConflict conflict = Assert.IsType<OperationConflict>(result.Value);
            Assert.Equal("remote-deck-changed", conflict.ReasonCode);
            Assert.Equal(
                new ConflictState(RemoteFingerprint: ParseRemote(changed).RemoteFingerprint),
                conflict.Current);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a push apply over a remote deck changed since the baseline reports both current markers.
    /// </summary>
    [Fact]
    public async Task ApplyPush_RemoteChangedSinceBaseline_ReturnsPushConflictWithCurrentState()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        string changed = RemoteDeck.Replace("\"quantity\": 3", "\"quantity\": 4", StringComparison.Ordinal);
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Get, "api/decks/42/", changed);
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            DeckDocument bound = await BindLocalDeckAsync(store, ParseRemote(RemoteDeck));
            ArchidektCoordinator coordinator = new(service, store);

            OperationResult<ArchidektApplyResult> result = await coordinator.ApplyPushAsync(
                new ArchidektPushApplyRequest(bound.DeckId, bound.Revision, "remote", "preview"),
                TestContext.Current.CancellationToken);

            OperationConflict conflict = Assert.IsType<OperationConflict>(result.Value);
            Assert.Equal("push-conflict", conflict.ReasonCode);
            Assert.Equal(
                new ConflictState(bound.Revision, ParseRemote(changed).RemoteFingerprint),
                conflict.Current);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Verifies a push apply with a stale preview fingerprint reports the current revision and remote state.
    /// </summary>
    [Fact]
    public async Task ApplyPush_StalePreview_ReturnsPushPreviewChangedWithCurrentState()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-app-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Get, "api/decks/42/", RemoteDeck);
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            RemoteDeckSnapshot remote = ParseRemote(RemoteDeck);
            DeckDocument bound = await BindLocalDeckAsync(store, remote);
            ArchidektCoordinator coordinator = new(service, store);

            OperationResult<ArchidektApplyResult> result = await coordinator.ApplyPushAsync(
                new ArchidektPushApplyRequest(bound.DeckId, bound.Revision, remote.RemoteFingerprint, "stale-preview"),
                TestContext.Current.CancellationToken);

            OperationConflict conflict = Assert.IsType<OperationConflict>(result.Value);
            Assert.Equal("push-preview-changed", conflict.ReasonCode);
            Assert.Equal(new ConflictState(bound.Revision, remote.RemoteFingerprint), conflict.Current);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    /// <summary>
    /// Creates a local deck bound to the remote deck with a matching baseline, as a completed pull would leave it.
    /// </summary>
    private static async Task<DeckDocument> BindLocalDeckAsync(SqliteDeckStore store, RemoteDeckSnapshot remote)
    {
        Guid bindingId = Guid.CreateVersion7();
        DeckProviderBinding binding = new(
            bindingId,
            "archidekt",
            remote.RemoteId,
            remote.RemoteUri,
            remote.Evidence.ContractVersion,
            remote.RemoteFingerprint,
            DateTimeOffset.UtcNow,
            null);
        DeckCreateRequest create = ArchidektLocalMapper.ToCreateRequest(remote, binding);
        return Success(await store.CreateSynchronizedAsync(
            create,
            new DeckSyncBaseline(bindingId, ArchidektLocalMapper.CreateBaseline(create, remote)),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Removes a test data folder when it was created.
    /// </summary>
    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a push result lists the local rows it could not link, under the published field name.
    /// </summary>
    [Fact]
    public void ApplyResult_WithUnlinkedRows_SerializesUnlinkedLocalIds()
    {
        Guid unlinked = Guid.NewGuid();
        ArchidektApplyResult result = new(
            "applied",
            LocalDeckId: null,
            LocalRevision: null,
            "42",
            "fingerprint",
            [])
        {
            UnlinkedLocalIds = [unlinked],
        };

        using JsonDocument json = JsonDocument.Parse(
            JsonSerializer.Serialize(result, MtgMcp.App.Hosting.ToolCallPolicy.SerializerOptions));

        Assert.Equal(
            [unlinked.ToString()],
            json.RootElement.GetProperty("unlinkedLocalIds").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// Verifies missing bindings, changed previews, and invocation-time mode guards remain explicit.
    /// </summary>
    [Fact]
    public async Task InvalidSynchronizationState_ReturnsStructuredFailuresWithoutProviderWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-invalid-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            DeckDocument local = Success(await store.CreateAsync(
                new DeckCreateRequest("Local", Format: "commander"),
                TestContext.Current.CancellationToken));

            OperationResult<ArchidektSyncDiff> diff = await coordinator.DiffAsync(
                local.DeckId,
                TestContext.Current.CancellationToken);
            OperationResult<ArchidektSyncPreview> missing = await coordinator.PreviewPushAsync(
                Guid.CreateVersion7(),
                TestContext.Current.CancellationToken);
            OperationResult<RemoteDeckSnapshot> staleCreate = await coordinator.CreateRemoteDeckAsync(
                local.DeckId,
                local.Revision + 1,
                new ArchidektDeckCreateRequest("Remote", "commander"),
                TestContext.Current.CancellationToken);

            Assert.Equal("binding-missing", Assert.IsType<OperationConflict>(diff.Value).ReasonCode);
            Assert.IsType<OperationNotFound>(missing.Value);
            OperationConflict staleCreateConflict = Assert.IsType<OperationConflict>(staleCreate.Value);
            Assert.Equal("local-deck-changed", staleCreateConflict.ReasonCode);
            Assert.Equal(new ConflictState(Revision: local.Revision), staleCreateConflict.Current);
            Assert.Empty(handler.Requests);

            DeckDocument ambiguous = Success(await store.CreateAsync(
                new DeckCreateRequest(
                    "Ambiguous",
                    Format: "commander",
                    ProviderBindings:
                    [
                        new(Guid.CreateVersion7(), "archidekt", "1", null, null, null, null, null),
                        new(Guid.CreateVersion7(), "ARCHIDEKT", "2", null, null, null, null, null),
                    ]),
                TestContext.Current.CancellationToken));
            OperationResult<ArchidektSyncPreview> ambiguousResult = await coordinator.PreviewPushAsync(
                ambiguous.DeckId,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                "binding-unavailable",
                Assert.IsType<OperationUnavailable>(ambiguousResult.Value).ReasonCode);

            ArchidektLocalWriteTools localTools = new(coordinator, OperationMode.ReadOnly);
            OperationResult<ArchidektApplyResult> deniedLocal = await localTools.ApplyPullAsync(
                new ArchidektPullApplyRequest("42", null, null, "fingerprint", "preview"),
                TestContext.Current.CancellationToken);
            Assert.IsType<OperationUnsupported>(deniedLocal.Value);

            ArchidektRemoteWriteTools remoteTools = new(coordinator, OperationMode.Local);
            await AssertEveryRemoteWriteIsDeniedAsync(remoteTools).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies a bound remote deck is called deleted only after fresh authenticated-list absence.
    /// </summary>
    [Fact]
    public async Task Diff_MissingBoundRemoteReturnsExplicitDeletedStateWithoutLocalMutation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-deleted-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Get, "api/decks/42/", "{}", HttpStatusCode.NotFound);
            handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
            handler.Add(HttpMethod.Get, "api/decks/42/", "{}", HttpStatusCode.NotFound);
            handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", "{\"results\":[]}");
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            using JsonDocument document = JsonDocument.Parse(RemoteDeck);
            RemoteDeckSnapshot remote = ArchidektDeckContractMapper.MapDeck(
                document.RootElement,
                RemoteDeck,
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
                "GET");
            Guid bindingId = Guid.CreateVersion7();
            DeckProviderBinding binding = new(
                bindingId,
                "archidekt",
                remote.RemoteId,
                remote.RemoteUri,
                remote.Evidence.ContractVersion,
                remote.RemoteFingerprint,
                DateTimeOffset.UtcNow,
                null);
            DeckCreateRequest create = ArchidektLocalMapper.ToCreateRequest(remote, binding);
            DeckDocument local = Success(await store.CreateSynchronizedAsync(
                create,
                new DeckSyncBaseline(bindingId, ArchidektLocalMapper.CreateBaseline(create, remote)),
                TestContext.Current.CancellationToken));
            ArchidektCoordinator coordinator = new(service, store);

            OperationResult<ArchidektSyncDiff> result = await coordinator.DiffAsync(
                local.DeckId,
                TestContext.Current.CancellationToken);
            DeckDocument unchanged = Success(await store.GetAsync(
                local.DeckId,
                TestContext.Current.CancellationToken));

            Assert.Equal("remote-deleted", Assert.IsType<OperationConflict>(result.Value).ReasonCode);
            Assert.Equal(local.Revision, unchanged.Revision);
            Assert.Equal(remote.RemoteId, Assert.Single(unchanged.ProviderBindings).RemoteId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies every Archidekt wrapper delegates when its operation mode permits the requested authority.
    /// </summary>
    [Fact]
    public async Task ToolWrappers_PermittedModesDelegateEveryOperation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"mtg-mcp-arch-tools-{Guid.NewGuid():N}");
        try
        {
            QueueHandler handler = new();
            handler.Add(HttpMethod.Post, "api/rest-auth/login/", "{\"token\":\"test-token\"}");
            using ArchidektService service = CreateService(handler);
            using SqliteDeckStore store = new(root, "test");
            ArchidektCoordinator coordinator = new(service, store);
            ArchidektReadTools reads = new(coordinator, OperationMode.Remote);
            CancellationToken token = TestContext.Current.CancellationToken;

            Assert.IsType<OperationSuccess<ArchidektAuthStatus>>(reads.AuthStatus().Value);
            AssertPermitted(await reads.ListDecksAsync(pageSize: 0, cancellationToken: token));
            AssertPermitted(await reads.GetDeckAsync(" ", cancellationToken: token));
            AssertPermitted(await reads.DiffAsync(Guid.NewGuid(), cancellationToken: token));
            AssertPermitted(await reads.PreviewPullAsync(" ", cancellationToken: token));
            AssertPermitted(await reads.PreviewPushAsync(Guid.NewGuid(), cancellationToken: token));
            AssertPermitted(await reads.GetFoldersAsync("list", cancellationToken: token));
            AssertPermitted(await reads.GetFoldersAsync("get", " ", cancellationToken: token));
            AssertPermitted(await reads.GetSnapshotsAsync("list", " ", cancellationToken: token));
            AssertPermitted(await reads.GetSnapshotsAsync("get", " ", " ", cancellationToken: token));
            AssertPermitted(await reads.PreviewSnapshotRestoreAsync(" ", " ", cancellationToken: token));

            ArchidektLocalWriteTools localWrites = new(coordinator, OperationMode.Local);
            AssertPermitted(await localWrites.ApplyPullAsync(
                new ArchidektPullApplyRequest(" ", null, null, "fingerprint", "preview"),
                token));

            ArchidektRemoteWriteTools writes = new(coordinator, OperationMode.Remote);
            AssertPermitted(await writes.CreateDeckAsync(new(" ", "commander"), cancellationToken: token));
            AssertPermitted(await writes.DeleteDeckAsync(new("1", "f", "wrong"), token));
            AssertPermitted(await writes.ApplyPushAsync(new(Guid.NewGuid(), 1, "f", "p"), token));
            AssertPermitted(await writes.CreateFolderAsync(new(" ", "private"), token));
            AssertPermitted(await writes.UpdateFolderAsync(new(" ", "f"), token));
            AssertPermitted(await writes.MoveFolderItemsAsync(new("f", [], null), token));
            AssertPermitted(await writes.DeleteFolderAsync(new("1", "x", "f", "wrong"), token));
            AssertPermitted(await writes.CreateSnapshotAsync(new(" ", "f", "x"), token));
            AssertPermitted(await writes.UpdateSnapshotAsync(new(" ", " ", "f", "x"), token));
            AssertPermitted(await writes.DeleteSnapshotAsync(new("1", "2", "f", "wrong"), token));
            AssertPermitted(await writes.ApplySnapshotRestoreAsync(new("1", "2", "s", "c", "r", "p", "wrong"), token));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// Invokes every remote-write wrapper to prove defense-in-depth mode rejection.
    /// </summary>
    private static async Task AssertEveryRemoteWriteIsDeniedAsync(ArchidektRemoteWriteTools tools)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        Assert.IsType<OperationUnsupported>((await tools.CreateDeckAsync(new("x", "commander"), cancellationToken: token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.DeleteDeckAsync(new("1", "f", "delete 1"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.ApplyPushAsync(new(Guid.NewGuid(), 1, "f", "p"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.CreateFolderAsync(new("x", "private"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.UpdateFolderAsync(new("1", "f"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.MoveFolderItemsAsync(new("f", [new("deck", "1", null)], null), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.DeleteFolderAsync(new("1", "x", "f", "delete folder 1"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.CreateSnapshotAsync(new("1", "f", "x"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.UpdateSnapshotAsync(new("1", "2", "f", "x"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.DeleteSnapshotAsync(new("1", "2", "f", "delete snapshot 2"), token)).Value);
        Assert.IsType<OperationUnsupported>((await tools.ApplySnapshotRestoreAsync(new("1", "2", "s", "c", "r", "p", "restore snapshot 2"), token)).Value);
    }

    /// <summary>
    /// Parses one provider deck payload into a remote snapshot.
    /// </summary>
    private static RemoteDeckSnapshot ParseRemote(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            json,
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            "GET");
    }

    /// <summary>
    /// Creates a zero-delay service over one deterministic response queue.
    /// </summary>
    private static ArchidektService CreateService(QueueHandler handler)
    {
        ArchidektOptions options = ArchidektOptions.CreateDefault("user", "secret") with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
        };
        HttpClient client = new(handler) { BaseAddress = options.BaseAddress };
        return new ArchidektService(
            new ArchidektSession(client, ownsHttpClient: true, options),
            options.MaximumRequestsPerOperation);
    }

    /// <summary>
    /// Extracts one successful union case.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Verifies an allowed wrapper reaches its implementation rather than the operation-mode guard.
    /// </summary>
    private static void AssertPermitted<T>(OperationResult<T> result)
    {
        if (result.Value is OperationUnsupported { ReasonCode: "operation-mode-denied" })
        {
            throw new Xunit.Sdk.XunitException("The permitted tool was rejected by its operation-mode guard.");
        }
    }

    /// <summary>
    /// Supplies exact queued HTTP responses and captures request starts.
    /// </summary>
    private sealed class QueueHandler : HttpMessageHandler
    {
        /// <summary>
        /// Stores responses by exact method and path.
        /// </summary>
        private readonly ConcurrentDictionary<string, Queue<HttpResponseMessage>> responses = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets requests in provider start order.
        /// </summary>
        internal List<HttpRequestMessage> Requests { get; } = [];

        /// <summary>
        /// Gets the text of each request body, in provider start order, read before the content is disposed.
        /// </summary>
        internal List<string> Bodies { get; } = [];

        /// <summary>
        /// Stores GET bodies that answer every request for a path, and can be replaced to change the remote deck.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> standing = new(StringComparer.Ordinal);

        /// <summary>
        /// Sets the body returned for every GET of a path that has no queued response.
        /// </summary>
        internal void SetStanding(string path, string json)
        {
            standing[$"GET {path}"] = json;
        }

        /// <summary>
        /// Queues one exact JSON response.
        /// </summary>
        internal void Add(HttpMethod method, string path, string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            Queue<HttpResponseMessage> queue = responses.GetOrAdd(
                $"{method.Method} {path}",
                static _ => new Queue<HttpResponseMessage>());
            queue.Enqueue(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
            string key = $"{request.Method.Method} {request.RequestUri!.PathAndQuery.TrimStart('/')}";
            if (standing.TryGetValue(key, out string? body) &&
                !(responses.TryGetValue(key, out Queue<HttpResponseMessage>? pending) && pending.Count > 0))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(
                responses.TryGetValue(key, out Queue<HttpResponseMessage>? queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                    });
        }
    }
}
