using System.Net;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Archidekt.Tests;

/// <summary>
/// Exercises authentication, retry, throttle, lifecycle, verification, and partial-state behavior through fake HTTP.
/// </summary>
public sealed class ArchidektServiceTests
{
    /// <summary>
    /// Verifies auth status is redacted and authenticated list requests carry only the process token.
    /// </summary>
    [Fact]
    public async Task AuthAndList_UseConfiguredCredentialsWithoutReturningThem()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        using ArchidektService service = CreateService(handler);

        ArchidektAuthStatus auth = Success(service.GetAuthStatus());
        RemoteDeckPage page = Success(await service.ListDecksAsync(
            cursor: null,
            pageSize: 50,
            TestContext.Current.CancellationToken));

        Assert.True(auth.CredentialsConfigured);
        Assert.False(auth.SessionAuthenticated);
        Assert.DoesNotContain("user", JsonSerializer.Serialize(auth), StringComparison.OrdinalIgnoreCase);
        Assert.Single(page.Items);
        Assert.Equal("JWT", handler.Requests[1].AuthorizationScheme);
        Assert.Equal("test-token", handler.Requests[1].AuthorizationValue);
        Assert.DoesNotContain("secret", handler.Requests[0].Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies public detail reads avoid login when the provider answers anonymously.
    /// </summary>
    [Fact]
    public async Task GetDeck_PublicDeckDoesNotRequireCredentials()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        using ArchidektService service = CreateService(handler, username: null, password: null);

        RemoteDeckSnapshot deck = Success(await service.GetDeckAsync(
            "42",
            TestContext.Current.CancellationToken));

        Assert.Equal("42", deck.RemoteId);
        CapturedArchidektRequest request = Assert.Single(handler.Requests);
        Assert.Null(request.AuthorizationScheme);
    }

    /// <summary>
    /// Verifies one 401 triggers exactly one paced relogin and one request replay.
    /// </summary>
    [Fact]
    public async Task ListDecks_UnauthorizedRefreshesExactlyOnce()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler, "first-token");
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", "{}", HttpStatusCode.Unauthorized);
        AddLogin(handler, "second-token");
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        using ArchidektService service = CreateService(handler);

        RemoteDeckPage page = Success(await service.ListDecksAsync(
            null,
            50,
            TestContext.Current.CancellationToken));

        Assert.Single(page.Items);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("first-token", handler.Requests[1].AuthorizationValue);
        Assert.Equal("second-token", handler.Requests[3].AuthorizationValue);
    }

    /// <summary>
    /// Verifies idempotent reads retry two transient failures while mutations never do.
    /// </summary>
    [Fact]
    public async Task ProviderFailures_RetryReadsButNotMutations()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/decks/42/", "{}", HttpStatusCode.InternalServerError);
        handler.Add(HttpMethod.Get, "api/decks/42/", "{}", HttpStatusCode.RequestTimeout);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        using ArchidektService service = CreateService(handler);

        RemoteDeckSnapshot deck = Success(await service.GetDeckAsync(
            "42",
            TestContext.Current.CancellationToken));

        Assert.Equal("42", deck.RemoteId);
        Assert.Equal(3, handler.Requests.Count);

        ArchidektTestHttpHandler mutationHandler = new();
        AddLogin(mutationHandler);
        mutationHandler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        mutationHandler.Add(HttpMethod.Delete, "api/decks/42/", "{}", HttpStatusCode.InternalServerError);
        using ArchidektService mutationService = CreateService(mutationHandler);
        ArchidektApplyResult result = Success(await mutationService.DeleteDeckAsync(
            new ArchidektDeckDeleteRequest(
                "42",
                deck.RemoteFingerprint,
                "delete 42"),
            TestContext.Current.CancellationToken));

        Assert.Equal("partial", result.Outcome);
        Assert.Equal("unknown", Assert.Single(result.Operations).Status);
        Assert.Single(mutationHandler.Requests.Where(value => value.Method == HttpMethod.Delete));
    }

    /// <summary>
    /// Verifies 429 and 403 responses stop immediately with sanitized availability outcomes.
    /// </summary>
    [Theory]
    [InlineData(429, "provider-rate-limited")]
    [InlineData(403, "provider-forbidden")]
    public async Task ListDecks_BlockResponsesStopImmediately(int statusCode, string reasonCode)
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(
            HttpMethod.Get,
            "api/decks/v3/?ownerUsername=user",
            "{\"secret\":\"must-not-leak\"}",
            (HttpStatusCode)statusCode,
            statusCode == 429 ? "5" : null);
        using ArchidektService service = CreateService(handler);

        OperationResult<RemoteDeckPage> result = await service.ListDecksAsync(
            null,
            50,
            TestContext.Current.CancellationToken);

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal(reasonCode, unavailable.ReasonCode);
        Assert.DoesNotContain("secret", unavailable.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies malformed payloads and invalid caller bounds become explicit structured failures.
    /// </summary>
    [Fact]
    public async Task ContractAndInputFailures_AreStructured()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/decks/42/", "not-json");
        using ArchidektService service = CreateService(handler, username: null, password: null);

        OperationResult<RemoteDeckSnapshot> malformed = await service.GetDeckAsync(
            "42",
            TestContext.Current.CancellationToken);
        OperationResult<RemoteDeckPage> invalidPage = await service.ListDecksAsync(
            null,
            101,
            TestContext.Current.CancellationToken);

        Assert.IsType<OperationUnsupported>(malformed.Value);
        Assert.IsType<OperationInvalidInput>(invalidPage.Value);
    }

    /// <summary>
    /// Verifies private-default deck creation and verified deletion use exact observed routes.
    /// </summary>
    [Fact]
    public async Task DeckLifecycle_CreatesReadsAndDeletesWithVerification()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Post, "api/decks/v2/", ArchidektTestPayloads.Deck, HttpStatusCode.Created);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Delete, "api/decks/42/", "{}", HttpStatusCode.NoContent);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", "{\"decks\":[]}");
        using ArchidektService service = CreateService(handler);

        RemoteDeckSnapshot created = Success(await service.CreateDeckAsync(
            new ArchidektDeckCreateRequest("Rate Safe Weenies", "commander"),
            TestContext.Current.CancellationToken));
        ArchidektApplyResult deleted = Success(await service.DeleteDeckAsync(
            new ArchidektDeckDeleteRequest(
                created.RemoteId,
                created.RemoteFingerprint,
                "delete 42"),
            TestContext.Current.CancellationToken));

        Assert.Equal("applied", deleted.Outcome);
        CapturedArchidektRequest create = handler.Requests.Single(value => value.Method == HttpMethod.Post && value.Path == "api/decks/v2/");
        using JsonDocument payload = JsonDocument.Parse(create.Body);
        Assert.True(payload.RootElement.GetProperty("private").GetBoolean());
        Assert.False(payload.RootElement.GetProperty("unlisted").GetBoolean());
        Assert.Single(handler.Requests.Where(value => value.Method == HttpMethod.Delete));
    }

    /// <summary>
    /// Verifies stale deletion guards and wrong confirmation perform zero remote writes.
    /// </summary>
    [Theory]
    [InlineData("wrong", "delete 42", "remote-deck-changed")]
    [InlineData("placeholder", "wrong phrase", "confirmation-required")]
    public async Task DeleteDeck_InvalidGuardsPerformZeroWrites(
        string fingerprint,
        string confirmation,
        string expectedReason)
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        using ArchidektService service = CreateService(handler);
        if (fingerprint == "placeholder")
        {
            fingerprint = "unused";
        }

        OperationResult<ArchidektApplyResult> result = await service.DeleteDeckAsync(
            new ArchidektDeckDeleteRequest("42", fingerprint, confirmation),
            TestContext.Current.CancellationToken);

        string actualReason = result.Value switch
        {
            OperationConflict value => value.ReasonCode,
            OperationInvalidInput value => value.ReasonCode,
            _ => "unexpected",
        };
        Assert.Equal(expectedReason, actualReason);
        Assert.DoesNotContain(handler.Requests, value => value.Method == HttpMethod.Delete);
    }

    /// <summary>
    /// Verifies folder create, update, move, and empty delete always preflight and verify fresh trees.
    /// </summary>
    [Fact]
    public async Task FolderLifecycle_UsesFreshGuardsAndVerifiedOutcomes()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        string createdFolder = "{\"id\":13,\"name\":\"Probe\",\"private\":true,\"parent\":9,\"children\":[],\"decks\":[]}";
        string treeWithProbe = ArchidektTestPayloads.FolderTree.Replace(
            "{ \"id\": 12",
            "{ \"id\": 13, \"name\": \"Probe\", \"parent\": 9, \"children\": [], \"decks\": [] }, { \"id\": 12",
            StringComparison.Ordinal);
        QueueTree(handler, ArchidektTestPayloads.FolderTree);
        handler.Add(HttpMethod.Post, "api/decks/folders/", createdFolder, HttpStatusCode.Created);
        QueueTree(handler, treeWithProbe);
        QueueTree(handler, treeWithProbe);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        handler.Add(HttpMethod.Patch, "api/massUpdate/", "{}");
        string renamedTree = treeWithProbe.Replace("\"Probe\"", "\"Renamed\"", StringComparison.Ordinal);
        QueueTree(handler, renamedTree);
        QueueTree(handler, renamedTree);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        handler.Add(HttpMethod.Patch, "api/massUpdate/", "{}");
        string movedTree = renamedTree.Replace("\"parent\": 9", "\"parent\": 12", StringComparison.Ordinal);
        QueueTree(handler, movedTree);
        QueueTree(handler, movedTree);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        handler.Add(HttpMethod.Post, "api/decks/folders/deleteItems/", "{}");
        QueueTree(handler, ArchidektTestPayloads.FolderTree);
        using ArchidektService service = CreateService(handler);

        RemoteFolderRecord created = Success(await service.CreateFolderAsync(
            new ArchidektFolderCreateRequest("Probe", "private", "9"),
            TestContext.Current.CancellationToken));
        RemoteFolderTree beforeUpdate = ParseTree(treeWithProbe);
        RemoteFolderRecord updated = Success(await service.UpdateFolderAsync(
            new ArchidektFolderUpdateRequest(
                created.FolderId,
                beforeUpdate.TreeFingerprint,
                Name: "Renamed"),
            TestContext.Current.CancellationToken));
        RemoteFolderTree beforeMove = ParseTree(renamedTree);
        ArchidektFolderMoveResult moved = Success(await service.MoveFolderItemsAsync(
            new ArchidektFolderMoveRequest(
                beforeMove.TreeFingerprint,
                [new ArchidektFolderMoveItem("folder", "13", "9")],
                "12"),
            TestContext.Current.CancellationToken));
        RemoteFolderTree beforeDelete = ParseTree(movedTree);
        ArchidektApplyResult deleted = Success(await service.DeleteFolderAsync(
            new ArchidektFolderDeleteRequest(
                "13",
                "Renamed",
                beforeDelete.TreeFingerprint,
                "delete folder 13"),
            TestContext.Current.CancellationToken));

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("applied", Assert.Single(moved.Items).Status);
        Assert.Equal("applied", deleted.Outcome);
        string deleteBody = handler.Requests.Single(value => value.Path == "api/decks/folders/deleteItems/").Body;
        Assert.Contains("\"type\":\"folder\"", deleteBody, StringComparison.Ordinal);
        Assert.DoesNotContain("deck", deleteBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies non-empty and cyclic folders refuse writes before any provider mutation.
    /// </summary>
    [Fact]
    public async Task FolderSafety_NonEmptyDeleteAndCyclePerformZeroWrites()
    {
        RemoteFolderTree tree = ParseTree(ArchidektTestPayloads.FolderTree);
        ArchidektTestHttpHandler deleteHandler = new();
        AddLogin(deleteHandler);
        QueueTree(deleteHandler, ArchidektTestPayloads.FolderTree);
        deleteHandler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        using ArchidektService deleteService = CreateService(deleteHandler);

        OperationResult<ArchidektApplyResult> delete = await deleteService.DeleteFolderAsync(
            new ArchidektFolderDeleteRequest(
                "9",
                "Tests",
                tree.TreeFingerprint,
                "delete folder 9"),
            TestContext.Current.CancellationToken);

        Assert.Equal("folder-not-empty", Assert.IsType<OperationConflict>(delete.Value).ReasonCode);
        Assert.DoesNotContain(deleteHandler.Requests, value => value.Method == HttpMethod.Post && value.Path.Contains("deleteItems", StringComparison.Ordinal));

        ArchidektTestHttpHandler cycleHandler = new();
        AddLogin(cycleHandler);
        QueueTree(cycleHandler, ArchidektTestPayloads.FolderTree);
        cycleHandler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", ArchidektTestPayloads.DeckList);
        using ArchidektService cycleService = CreateService(cycleHandler);
        OperationResult<ArchidektFolderMoveResult> cycle = await cycleService.MoveFolderItemsAsync(
            new ArchidektFolderMoveRequest(
                tree.TreeFingerprint,
                [new ArchidektFolderMoveItem("folder", "9", null)],
                "12"),
            TestContext.Current.CancellationToken);

        Assert.Equal("folder-cycle", Assert.IsType<OperationConflict>(cycle.Value).ReasonCode);
        Assert.DoesNotContain(cycleHandler.Requests, value => value.Method == HttpMethod.Patch);
    }

    /// <summary>
    /// Verifies named snapshot create, metadata update, and delete all use exact identities and verification reads.
    /// </summary>
    [Fact]
    public async Task SnapshotLifecycle_UsesExactChecksumsAndVerification()
    {
        RemoteDeckSnapshot deck = ParseDeck();
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Post, "api/decks/42/snapshots/", "{}", HttpStatusCode.Created);
        handler.Add(HttpMethod.Get, "api/decks/42/snapshots/", ArchidektTestPayloads.SnapshotList);
        handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", ArchidektTestPayloads.Snapshot);
        handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", ArchidektTestPayloads.Snapshot);
        handler.Add(HttpMethod.Patch, "api/decks/snapshots/77/", "{}");
        string renamedSnapshot = ArchidektTestPayloads.Snapshot.Replace("Before test", "Renamed", StringComparison.Ordinal);
        handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", renamedSnapshot);
        handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", renamedSnapshot);
        handler.Add(HttpMethod.Delete, "api/decks/snapshots/77/", "{}", HttpStatusCode.NoContent);
        handler.Add(HttpMethod.Get, "api/decks/42/snapshots/", "{\"results\":[]}");
        using ArchidektService service = CreateService(handler);

        RemoteNamedSnapshotSummary created = Success(await service.CreateSnapshotAsync(
            new ArchidektSnapshotCreateRequest("42", deck.RemoteFingerprint, "Before test", "safe"),
            TestContext.Current.CancellationToken));
        RemoteNamedSnapshot current = Success(await service.GetSnapshotAsync(
            "42",
            created.SnapshotId,
            TestContext.Current.CancellationToken));
        RemoteNamedSnapshotSummary updated = Success(await service.UpdateSnapshotAsync(
            new ArchidektSnapshotUpdateRequest("42", "77", current.Summary.Checksum, "Renamed"),
            TestContext.Current.CancellationToken));
        ArchidektApplyResult deleted = Success(await service.DeleteSnapshotAsync(
            new ArchidektSnapshotDeleteRequest(
                "42",
                "77",
                updated.Checksum,
                "delete snapshot 77"),
            TestContext.Current.CancellationToken));

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("applied", deleted.Outcome);
        Assert.Single(handler.Requests.Where(value => value.Method == HttpMethod.Delete));
    }

    /// <summary>
    /// Verifies snapshot restore preview/apply replays source, target, and preview guards with no local mutation.
    /// </summary>
    [Fact]
    public async Task SnapshotRestore_UnchangedContentNeedsNoMutationAndVerifies()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", ArchidektTestPayloads.Snapshot);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Get, "api/decks/snapshots/77/", ArchidektTestPayloads.Snapshot);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        using ArchidektService service = CreateService(handler);

        ArchidektSnapshotRestorePreview preview = Success(await service.PreviewSnapshotRestoreAsync(
            "42",
            "77",
            TestContext.Current.CancellationToken));
        ArchidektApplyResult applied = Success(await service.ApplySnapshotRestoreAsync(
            new ArchidektSnapshotRestoreApplyRequest(
                "42",
                "77",
                preview.SnapshotChecksum,
                preview.SnapshotContentFingerprint,
                preview.RemoteFingerprint,
                preview.PreviewFingerprint,
                "restore snapshot 77"),
            TestContext.Current.CancellationToken));

        Assert.Empty(preview.Operations);
        Assert.Equal("applied", applied.Outcome);
        Assert.DoesNotContain(handler.Requests, value => value.Method is { Method: "PATCH" or "POST" or "DELETE" } && value.Path != "api/rest-auth/login/");
    }

    /// <summary>
    /// Verifies a previewed metadata plan executes once and requires exact final provider verification.
    /// </summary>
    [Fact]
    public async Task ApplyRemoteTarget_ExecutesStablePlanAndVerifiesFinalState()
    {
        RemoteDeckSnapshot current = ParseDeck();
        string finalJson = ArchidektTestPayloads.Deck.Replace(
            "Rate Safe Weenies",
            "Updated Name",
            StringComparison.Ordinal);
        RemoteDeckSnapshot target = ParseDeck(finalJson);
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Patch, "api/decks/42/", "{}");
        handler.Add(HttpMethod.Get, "api/decks/42/", finalJson);
        using ArchidektService service = CreateService(handler);

        ArchidektApplyResult result = Success(await service.ApplyRemoteTargetAsync(
            target,
            current.RemoteFingerprint,
            plan.PlanFingerprint,
            TestContext.Current.CancellationToken));

        Assert.Equal("applied", result.Outcome);
        Assert.Equal("metadata-update", Assert.Single(result.Operations).Kind);
        Assert.Single(handler.Requests.Where(value => value.Method == HttpMethod.Patch));
    }

    /// <summary>
    /// Verifies every supported primitive mutation is emitted in stable order and verified as one target.
    /// </summary>
    [Fact]
    public async Task ApplyRemoteTarget_ExecutesMetadataCategoryAndCardPrimitives()
    {
        const string finalJson = """
            {
              "id": 42,
              "name": "Updated Weenies",
              "description": "Dummy deck",
              "deckFormat": 3,
              "private": true,
              "parentFolder": 9,
              "categories": [
                { "id": 10, "name": "Mainboard", "includedInDeck": true, "includedInPrice": false, "sortOrder": 0 },
                { "id": 12, "name": "Sideboard", "includedInDeck": false, "includedInPrice": true, "sortOrder": 2 }
              ],
              "cards": [
                {
                  "deckRelationId": 101,
                  "quantity": 4,
                  "categories": ["Mainboard"],
                  "card": {
                    "id": 501,
                    "uid": "33333333-3333-3333-3333-333333333333",
                    "setCode": "dmu",
                    "collectorNumber": "278",
                    "oracleCard": { "uid": "44444444-4444-4444-4444-444444444444", "name": "Island" }
                  }
                },
                {
                  "deckRelationId": 102,
                  "quantity": 2,
                  "categories": ["Sideboard"],
                  "modifier": "Etched",
                  "card": {
                    "id": 502,
                    "uid": "55555555-5555-5555-5555-555555555555",
                    "setCode": "2x2",
                    "collectorNumber": "117",
                    "oracleCard": { "uid": "66666666-6666-6666-6666-666666666666", "name": "Lightning Bolt" }
                  }
                }
              ]
            }
            """;
        RemoteDeckSnapshot current = ParseDeck();
        RemoteDeckSnapshot target = ParseDeck(finalJson);
        target = target with
        {
            Entries = target.Entries.Select(value => value.CardName == "Lightning Bolt"
                ? value with { ProviderRelationId = string.Empty }
                : value).ToArray(),
        };
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Patch, "api/decks/42/", "{}");
        handler.Add(HttpMethod.Patch, "api/decks/category/10/", "{}");
        handler.Add(HttpMethod.Post, "api/decks/createCategory/", "{}");
        handler.Add(HttpMethod.Delete, "api/decks/category/11/", "{}", HttpStatusCode.NoContent);
        handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
        handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
        handler.Add(HttpMethod.Patch, "api/decks/42/modifyCards/v2/", "{}");
        handler.Add(HttpMethod.Get, "api/decks/42/", finalJson);
        using ArchidektService service = CreateService(handler);

        ArchidektApplyResult result = Success(await service.ApplyRemoteTargetAsync(
            target,
            current.RemoteFingerprint,
            plan.PlanFingerprint,
            TestContext.Current.CancellationToken));

        Assert.Equal("applied", result.Outcome);
        Assert.Equal(
            ["metadata-update", "category-update", "category-create", "category-delete", "entry-update", "entry-add", "entry-remove"],
            result.Operations.Select(value => value.Kind));
        Assert.Equal(3, handler.Requests.Count(value => value.Path.EndsWith("modifyCards/v2/", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Verifies the hard operation ceiling counts login and provider reads on one shared budget.
    /// </summary>
    [Fact]
    public async Task OperationBudget_CountsLoginAndRequestTogether()
    {
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        using ArchidektService service = CreateService(handler, maximumRequestsPerOperation: 1);

        OperationResult<RemoteDeckPage> result = await service.ListDecksAsync(
            null,
            50,
            TestContext.Current.CancellationToken);

        Assert.Equal("request-limit-exceeded", Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// Verifies one composed scope cannot reset the hard cap by calling the service repeatedly.
    /// </summary>
    [Fact]
    public async Task ComposedOperationScope_SharesOneHardRequestCap()
    {
        ArchidektTestHttpHandler handler = new();
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        using ArchidektService service = CreateService(
            handler,
            username: null,
            password: null,
            maximumRequestsPerOperation: 2);
        ArchidektOperationScope scope = service.BeginOperation();

        Assert.IsType<OperationSuccess<RemoteDeckSnapshot>>((await service.GetDeckAsync(
            "42",
            scope,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationSuccess<RemoteDeckSnapshot>>((await service.GetDeckAsync(
            "42",
            scope,
            TestContext.Current.CancellationToken)).Value);
        OperationResult<RemoteDeckSnapshot> capped = await service.GetDeckAsync(
            "42",
            scope,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "request-limit-exceeded",
            Assert.IsType<OperationInvalidInput>(capped.Value).ReasonCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// Verifies requests consumed by authentication and guard reads reduce the plan budget before any mutation.
    /// </summary>
    [Fact]
    public async Task ApplyRemoteTarget_PreflightsRemainingBudgetBeforeMutation()
    {
        RemoteDeckSnapshot current = ParseDeck();
        RemoteDeckSnapshot target = ParseDeck(ArchidektTestPayloads.Deck.Replace(
            "Rate Safe Weenies",
            "Changed Name",
            StringComparison.Ordinal));
        ArchidektRemotePlan plan = ArchidektSyncPlanner.PlanRemoteApply(current, target);
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/42/", ArchidektTestPayloads.Deck);
        using ArchidektService service = CreateService(handler, maximumRequestsPerOperation: 2);

        OperationResult<ArchidektApplyResult> result = await service.ApplyRemoteTargetAsync(
            target,
            current.RemoteFingerprint,
            plan.PlanFingerprint,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "request-limit-exceeded",
            Assert.IsType<OperationInvalidInput>(result.Value).ReasonCode);
        Assert.DoesNotContain(handler.Requests, value => value.Method == HttpMethod.Patch);
    }

    /// <summary>
    /// Verifies owned-deck pagination uses a checksum-bound local cursor and detects changed evidence.
    /// </summary>
    [Fact]
    public async Task ListDecks_PaginatesOneFreshOwnedCollectionWithBoundCursor()
    {
        const string collection = """
            {"results":[
              {"id":1,"name":"Alpha","deckFormat":3,"private":true},
              {"id":2,"name":"Beta","deckFormat":3,"private":true}
            ]}
            """;
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", collection);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", collection);
        using ArchidektService service = CreateService(handler);

        RemoteDeckPage first = Success(await service.ListDecksAsync(
            null,
            1,
            TestContext.Current.CancellationToken));
        RemoteDeckPage second = Success(await service.ListDecksAsync(
            first.NextCursor,
            1,
            TestContext.Current.CancellationToken));

        Assert.Equal("Alpha", Assert.Single(first.Items).Name);
        Assert.Equal("Beta", Assert.Single(second.Items).Name);
        Assert.Null(second.NextCursor);
        Assert.Equal(
            "invalid-deck-list-cursor",
            Assert.IsType<OperationInvalidInput>((await service.ListDecksAsync(
                "not-a-cursor",
                1,
                TestContext.Current.CancellationToken)).Value).ReasonCode);
    }

    /// <summary>
    /// Verifies a continuation refuses to splice rows from a changed owned-deck observation.
    /// </summary>
    [Fact]
    public async Task ListDecks_ChangedCollectionInvalidatesCursor()
    {
        const string firstCollection = """
            {"results":[
              {"id":1,"name":"Alpha","deckFormat":3,"private":true},
              {"id":2,"name":"Beta","deckFormat":3,"private":true}
            ]}
            """;
        const string changedCollection = """
            {"results":[
              {"id":1,"name":"Alpha","deckFormat":3,"private":true},
              {"id":2,"name":"Changed","deckFormat":3,"private":true}
            ]}
            """;
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", firstCollection);
        handler.Add(HttpMethod.Get, "api/decks/v3/?ownerUsername=user", changedCollection);
        using ArchidektService service = CreateService(handler);

        RemoteDeckPage first = Success(await service.ListDecksAsync(
            null,
            1,
            TestContext.Current.CancellationToken));
        OperationResult<RemoteDeckPage> continuation = await service.ListDecksAsync(
            first.NextCursor,
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "deck-list-changed",
            Assert.IsType<OperationConflict>(continuation.Value).ReasonCode);
    }

    /// <summary>
    /// Verifies public folder evidence joins owned-deck rows when the provider tree omits them.
    /// </summary>
    [Fact]
    public async Task ListFolders_JoinsOwnedDecksMissingFromTreePayload()
    {
        string treeWithoutDeckRows = ArchidektTestPayloads.FolderTree.Replace(
            "\"decks\": [ { \"id\": 42, \"name\": \"Rate Safe Weenies\", \"deckFormat\": 3, \"private\": true } ]",
            "\"decks\": []",
            StringComparison.Ordinal);
        ArchidektTestHttpHandler handler = new();
        AddLogin(handler);
        QueueTree(handler, treeWithoutDeckRows);
        handler.Add(
            HttpMethod.Get,
            "api/decks/v3/?ownerUsername=user",
            ArchidektTestPayloads.DeckList);
        using ArchidektService service = CreateService(handler);

        RemoteFolderTree tree = Success(await service.ListFoldersAsync(
            TestContext.Current.CancellationToken));

        RemoteFolderRecord folder = Assert.Single(tree.Items, value => value.FolderId == "9");
        Assert.Equal("42", Assert.Single(folder.Decks).RemoteId);
        Assert.NotEqual(ParseTree(treeWithoutDeckRows).TreeFingerprint, tree.TreeFingerprint);
    }

    /// <summary>
    /// Adds one successful login response.
    /// </summary>
    private static void AddLogin(ArchidektTestHttpHandler handler, string token = "test-token")
    {
        handler.Add(
            HttpMethod.Post,
            "api/rest-auth/login/",
            $"{{\"token\":\"{token}\"}}");
    }

    /// <summary>
    /// Adds one folder-tree response.
    /// </summary>
    private static void QueueTree(ArchidektTestHttpHandler handler, string json)
    {
        handler.Add(HttpMethod.Get, "api/decks/folderTree/", json);
    }

    /// <summary>
    /// Creates a zero-delay fake-provider service with strict production request ceilings otherwise intact.
    /// </summary>
    private static ArchidektService CreateService(
        ArchidektTestHttpHandler handler,
        string? username = "user",
        string? password = "secret",
        int maximumRequestsPerOperation = 150)
    {
        ArchidektOptions options = ArchidektOptions.CreateDefault(username, password) with
        {
            BaseAddress = new Uri("https://archidekt.test/"),
            MinimumRequestInterval = TimeSpan.Zero,
            MaximumRequestsPerWindow = 1_000,
            MaximumRequestsPerOperation = maximumRequestsPerOperation,
        };
        HttpClient client = new(handler)
        {
            BaseAddress = options.BaseAddress,
        };
        ArchidektTransport transport = new(client, ownsHttpClient: true, options);
        return new ArchidektService(transport, maximumRequestsPerOperation);
    }

    /// <summary>
    /// Extracts one successful result while preserving useful union-case test failures.
    /// </summary>
    private static T Success<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Parses one canonical deck payload.
    /// </summary>
    private static RemoteDeckSnapshot ParseDeck(string? json = null)
    {
        json ??= ArchidektTestPayloads.Deck;
        using JsonDocument document = JsonDocument.Parse(json);
        return ArchidektDeckContractMapper.MapDeck(
            document.RootElement,
            json,
            DateTimeOffset.UtcNow,
            "GET");
    }

    /// <summary>
    /// Parses one canonical folder tree payload.
    /// </summary>
    private static RemoteFolderTree ParseTree(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return ArchidektFolderContractMapper.MapFolderTree(
            document.RootElement,
            json,
            DateTimeOffset.UtcNow,
            "GET");
    }
}
