using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
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
            ArchidektReadTools reads = new(coordinator);
            CancellationToken token = TestContext.Current.CancellationToken;

            Assert.IsType<OperationSuccess<ArchidektAuthStatus>>(reads.AuthStatus().Value);
            AssertPermitted(await reads.ListDecksAsync(pageSize: 0, cancellationToken: token));
            AssertPermitted(await reads.GetDeckAsync(" ", token));
            AssertPermitted(await reads.DiffAsync(Guid.NewGuid(), token));
            AssertPermitted(await reads.PreviewPullAsync(" ", cancellationToken: token));
            AssertPermitted(await reads.PreviewPushAsync(Guid.NewGuid(), token));
            AssertPermitted(await reads.GetFoldersAsync("list", cancellationToken: token));
            AssertPermitted(await reads.GetFoldersAsync("get", " ", token));
            AssertPermitted(await reads.GetSnapshotsAsync("list", " ", cancellationToken: token));
            AssertPermitted(await reads.GetSnapshotsAsync("get", " ", " ", token));
            AssertPermitted(await reads.PreviewSnapshotRestoreAsync(" ", " ", token));

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
            _ = cancellationToken;
            Requests.Add(request);
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
