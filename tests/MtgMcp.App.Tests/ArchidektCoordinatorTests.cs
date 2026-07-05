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
            Assert.Equal(
                "pull-preview-changed",
                Assert.IsType<OperationConflict>(stalePull.Value).ReasonCode);

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
            Assert.Equal("local-deck-changed", Assert.IsType<OperationConflict>(staleCreate.Value).ReasonCode);
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
            RemoteDeckSnapshot remote = ArchidektContractMapper.MapDeck(
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
            AssertPermitted(await reads.ListFoldersAsync(token));
            AssertPermitted(await reads.GetFolderAsync(" ", token));
            AssertPermitted(await reads.ListSnapshotsAsync(" ", token));
            AssertPermitted(await reads.GetSnapshotAsync(" ", " ", token));
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
            new ArchidektTransport(client, ownsHttpClient: true, options),
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
