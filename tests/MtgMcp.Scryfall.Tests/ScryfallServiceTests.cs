using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Evidence;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies the complete Scryfall evidence, corpus, provider, and lifecycle boundary.
/// </summary>
public sealed class ScryfallServiceTests
{
    /// <summary>
    /// Verifies explicit sync atomically installs all four datasets and serves identity/tag reads without HTTP.
    /// </summary>
    [Fact]
    public async Task CorpusSync_InstallsCompleteGenerationAndServesLocalEvidence()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);

        ScryfallCorpusStatus absent = RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken));
        Assert.Equal("not-cached", absent.State);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "scryfall.db")));

        ScryfallCorpusSyncResult sync = RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            null,
            TestContext.Current.CancellationToken));
        Assert.Equal("activated", sync.Outcome);
        Assert.Equal(["all_cards", "art_tags", "oracle_tags", "rulings"], sync.Datasets.Select(value => value.Type));
        Assert.All(sync.Datasets, value => Assert.NotEqual(0, value.RowCount));
        int requestsAfterSync = handler.Requests.Count;
        Assert.Equal(5, requestsAfterSync);

        ScryfallCardResult card = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Venerable Knight"),
            "default",
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("corpus", card.Origin);
        Assert.Equal(ScryfallTestFixture.WhiteOracleId, card.Card.OracleId);
        Assert.Equal("complete-direct", card.Card.TagCoverage);
        ScryfallTagEvidence direct = Assert.Single(card.Card.Tags);
        Assert.Equal("direct", direct.Relationship);
        Assert.Equal(ScryfallTestFixture.WeenieTagId, direct.TagId);
        Assert.IsType<SourceEvidenceDescriptor>(direct.Evidence.Value);
        Assert.True(card.Card.Raw!.Value.GetProperty("fixture_extension").GetProperty("retained").GetBoolean());
        Assert.All(card.Card.PriceEvidence, value => Assert.Equal("stale", value.Freshness));

        ScryfallPrintsResult prints = RequireSuccess(await service.GetPrintsAsync(
            ScryfallTestFixture.WhiteOracleId,
            "cache-only",
            null,
            25,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(prints.Page.Items);
        Assert.Equal(sync.GenerationId, prints.CorpusGenerationId);

        ScryfallRulingsResult rulings = RequireSuccess(await service.GetRulingsAsync(
            ScryfallTestFixture.WhiteOracleId,
            null,
            "cache-only",
            null,
            25,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(["Fixture ruling.", string.Empty], rulings.Page.Items.Select(value => value.Comment));

        ScryfallPage<ScryfallTag> tagSearch = RequireSuccess(await service.SearchTagsAsync(
            "weenie",
            "oracle",
            null,
            25,
            cancellationToken: TestContext.Current.CancellationToken));
        ScryfallTag compactTag = Assert.Single(tagSearch.Items);
        Assert.Equal(ScryfallTestFixture.WeenieTagId, compactTag.Id);
        Assert.Null(compactTag.Raw);
        ScryfallPage<ScryfallTag> fullTagSearch = RequireSuccess(await service.SearchTagsAsync(
            "weenie",
            "oracle",
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(Assert.Single(fullTagSearch.Items).Raw);
        ScryfallPage<ScryfallTag> aliasSearch = RequireSuccess(await service.SearchTagsAsync(
            "beatdown",
            "oracle",
            null,
            25,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ScryfallTestFixture.AggroTagId, Assert.Single(aliasSearch.Items).Id);

        ScryfallCardsByTagResult inherited = RequireSuccess(await service.GetCardsByTagAsync(
            "aggro",
            "oracle",
            true,
            "weak",
            null,
            25,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("inherited", Assert.Single(inherited.Assignments).Relationship);
        Assert.Null(inherited.Tag.Raw);
        Assert.Null(Assert.Single(inherited.Page.Items).Raw);
        Assert.Equal(
            [ScryfallTestFixture.AggroTagId, ScryfallTestFixture.WeenieTagId],
            inherited.Assignments[0].HierarchyPath);

        ScryfallCardsByTagResult directOnly = RequireSuccess(await service.GetCardsByTagAsync(
            "aggro",
            "oracle",
            false,
            "weak",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(directOnly.Page.Items);
        Assert.Empty(directOnly.Assignments);

        ScryfallCardsByTagResult filteredByWeight = RequireSuccess(await service.GetCardsByTagAsync(
            "white-weenie",
            "oracle",
            false,
            "very_strong",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(filteredByWeight.Page.Items);
        Assert.Empty(filteredByWeight.Assignments);
        Assert.Equal(requestsAfterSync, handler.Requests.Count);

        Assert.Equal(ScryfallTestFixture.WhiteCardId, RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("scryfall-id", ScryfallTestFixture.WhiteCardId.ToString("D")),
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Card.Id);
        Assert.Equal(ScryfallTestFixture.WhiteOracleId, RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("oracle-id", ScryfallTestFixture.WhiteOracleId.ToString("D")),
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Card.OracleId);
        Assert.Equal("eld", RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("printing", SetCode: "eld", CollectorNumber: "35"),
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Card.SetCode);
        Assert.Equal(ScryfallTestFixture.WhiteCardId, RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", " Venerable Knight "),
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Card.Id);
        Assert.Equal("eld", RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("printing", SetCode: " ELD ", CollectorNumber: "35 "),
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Card.SetCode);

        ScryfallCollectionResult mixed = RequireSuccess(await service.GetCollectionAsync(
            [
                new ScryfallCardLookup("exact-name", "Venerable Knight"),
                new ScryfallCardLookup("exact-name", "Missing Fixture"),
                new ScryfallCardLookup("exact-name", "missing fixture"),
            ],
            "default",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(["found", "not-found", "not-found"], mixed.Page.Items.Select(value => value.Status));
        Assert.Equal("corpus", mixed.Page.Items[0].Origin);
        Assert.Null(mixed.Page.Items[1].Origin);
        RecordedRequest collectionRequest = handler.Requests.Last(value => value.Uri.AbsolutePath == "/cards/collection");
        using (JsonDocument requestDocument = JsonDocument.Parse(collectionRequest.Body!))
        {
            Assert.Single(requestDocument.RootElement.GetProperty("identifiers").EnumerateArray());
        }

        int requestsAfterCollection = handler.Requests.Count;
        ScryfallCollectionResult cachedMixed = RequireSuccess(await service.GetCollectionAsync(
            [
                new ScryfallCardLookup("exact-name", "Venerable Knight"),
                new ScryfallCardLookup("exact-name", "Missing Fixture"),
                new ScryfallCardLookup("exact-name", "missing fixture"),
            ],
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(["found", "not-found", "not-found"], cachedMixed.Page.Items.Select(value => value.Status));
        Assert.NotNull(cachedMixed.Snapshot);
        Assert.Equal(requestsAfterCollection, handler.Requests.Count);

        ScryfallCorpusStatus installed = RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken));
        Assert.Equal("available", installed.State);
        Assert.Equal(sync.GenerationId, installed.Active!.GenerationId);
        Assert.False(installed.RefreshEligible);
        Assert.NotNull(installed.CorpusAgeSeconds);

        ScryfallCorpusSyncResult unchanged = RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            sync.GenerationId,
            TestContext.Current.CancellationToken));
        Assert.Equal("unchanged", unchanged.Outcome);
        Assert.Equal(sync.GenerationId, unchanged.GenerationId);
    }

    /// <summary>
    /// Verifies authoritative provider reads capture complete immutable snapshots and reuse only exact requests.
    /// </summary>
    [Fact]
    public async Task ProviderReads_CaptureReplayAndDeleteImmutableSnapshots()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);

        ScryfallSearchResult first = RequireSuccess(await service.SearchAsync(
            "ci<=rw mv=1",
            pageSize: 1,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, first.Page.TotalCount);
        Assert.Single(first.Page.Items);
        Assert.NotNull(first.Page.NextCursor);
        Assert.Equal(["fixture warning"], first.Warnings);
        Assert.Equal("fresh", first.Snapshot.Freshness);
        Assert.Null(Assert.Single(first.Page.Items).Raw);
        int providerRequests = handler.Requests.Count;

        ScryfallSearchResult full = RequireSuccess(await service.SearchAsync(
            "ci<=rw mv=1",
            pageSize: 2,
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.All(full.Page.Items, value => Assert.NotNull(value.Raw));
        Assert.Equal(providerRequests, handler.Requests.Count);

        ScryfallSearchResult secondPage = RequireSuccess(await service.SearchAsync(
            "ci<=rw mv=1",
            cursor: first.Page.NextCursor,
            pageSize: 1,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Monastery Swiftspear", Assert.Single(secondPage.Page.Items).Name);
        Assert.Equal(first.Snapshot.SnapshotId, secondPage.Snapshot.SnapshotId);
        Assert.Equal(providerRequests, handler.Requests.Count);

        Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            "ci<=rw mv=1",
            cursor: "tampered",
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        ScryfallPage<ScryfallSnapshotSummary> snapshots = RequireSuccess(await service.ListSnapshotsAsync(
            operation: "search",
            pageSize: 25,
            cancellationToken: TestContext.Current.CancellationToken));
        ScryfallSnapshotSummary summary = Assert.Single(snapshots.Items);
        Assert.Equal(first.Snapshot.Checksum, summary.Checksum);
        Assert.Empty(RequireSuccess(await service.ListSnapshotsAsync(
            operation: "search",
            retrievedAfterUtc: first.Snapshot.RetrievedAtUtc.AddTicks(1),
            cancellationToken: TestContext.Current.CancellationToken)).Items);
        Assert.Single(RequireSuccess(await service.ListSnapshotsAsync(
            operation: "search",
            retrievedBeforeUtc: first.Snapshot.RetrievedAtUtc,
            cancellationToken: TestContext.Current.CancellationToken)).Items);
        Assert.IsType<OperationInvalidInput>((await service.ListSnapshotsAsync(
            retrievedAfterUtc: first.Snapshot.RetrievedAtUtc.AddMinutes(1),
            retrievedBeforeUtc: first.Snapshot.RetrievedAtUtc,
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        ScryfallSnapshotPage replay = RequireSuccess(await service.GetSnapshotAsync(
            summary.SnapshotId,
            null,
            1,
            true,
            TestContext.Current.CancellationToken));
        ScryfallSnapshotMember rawMember = Assert.Single(replay.Items);
        Assert.Equal(0, rawMember.Ordinal);
        Assert.NotEmpty(rawMember.Checksum);
        Assert.NotNull(rawMember.Raw);

        ScryfallSnapshotPage compactReplay = RequireSuccess(await service.GetSnapshotAsync(
            summary.SnapshotId,
            null,
            1,
            false,
            TestContext.Current.CancellationToken));
        ScryfallSnapshotMember compactMember = Assert.Single(compactReplay.Items);
        Assert.Equal(rawMember.Ordinal, compactMember.Ordinal);
        Assert.Equal(rawMember.Checksum, compactMember.Checksum);
        Assert.Null(compactMember.Raw);
        Assert.True(rawMember.Raw!.Value.TryGetProperty("fixture_extension", out _));
        Assert.Equal(1, replay.Request.GetProperty("adapterSchemaVersion").GetInt32());
        Assert.NotNull(replay.NextCursor);

        Assert.IsType<OperationInvalidInput>((await service.DeleteSnapshotAsync(
            summary.SnapshotId,
            summary.Checksum,
            false,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationConflict>((await service.DeleteSnapshotAsync(
            summary.SnapshotId,
            "wrong",
            true,
            TestContext.Current.CancellationToken)).Value);
        ScryfallSnapshotDeleteResult deleted = RequireSuccess(await service.DeleteSnapshotAsync(
            summary.SnapshotId,
            summary.Checksum,
            true,
            TestContext.Current.CancellationToken));
        Assert.Equal(summary.SnapshotId, deleted.SnapshotId);
        Assert.Empty(RequireSuccess(await service.ListSnapshotsAsync(
            operation: "search",
            pageSize: 25,
            cancellationToken: TestContext.Current.CancellationToken)).Items);
    }

    /// <summary>
    /// Verifies provider-shaped reads retain their official raw extensions and bounded projections.
    /// </summary>
    [Fact]
    public async Task ProviderOperations_ReturnStructuredOfficialEvidence()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);

        ScryfallCardResult card = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("scryfall-id", ScryfallTestFixture.WhiteCardId.ToString("D")),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("request-snapshot", card.Origin);
        Assert.NotNull(card.Snapshot);
        Assert.Equal("not-cached", card.Card.TagCoverage);
        Assert.Equal("0.10", card.Card.Prices["usd"]);
        ScryfallPriceEvidence usd = card.Card.PriceEvidence.Single(value => value.Field == "usd");
        Assert.Equal("USD", usd.Currency);
        Assert.Equal("nonfoil", usd.Finish);
        Assert.Equal("fresh", usd.Freshness);
        Assert.Equal("scryfall-price", Assert.IsType<SourceFactDescriptor>(usd.Evidence.Value).Source);
        Assert.Equal(42, card.Card.RankEvidence.Single(value => value.Field == "edhrec_rank").Rank);

        ScryfallCardResult fuzzy = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("fuzzy-name", "Venerable Kniht"),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Venerable Knight", fuzzy.Card.Name);

        ScryfallCollectionResult collection = RequireSuccess(await service.GetCollectionAsync(
            [new ScryfallCardLookup("exact-name", "Monastery Swiftspear")],
            cancellationToken: TestContext.Current.CancellationToken));
        ScryfallCollectionRow collectionRow = Assert.Single(collection.Page.Items);
        Assert.Equal("found", collectionRow.Status);
        Assert.Equal("request-snapshot", collectionRow.Origin);
        ScryfallCollectionResult fullCollection = RequireSuccess(await service.GetCollectionAsync(
            [new ScryfallCardLookup("exact-name", "Monastery Swiftspear")],
            freshnessPolicy: "cache-only",
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotNull(Assert.Single(fullCollection.Page.Items).Card!.Raw);

        ScryfallRulingsResult rulings = RequireSuccess(await service.GetRulingsAsync(
            ScryfallTestFixture.WhiteOracleId,
            ScryfallTestFixture.WhiteCardId,
            "refresh",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Provider ruling.", Assert.Single(rulings.Page.Items).Comment);

        ScryfallPrintsResult providerPrints = RequireSuccess(await service.GetPrintsAsync(
            ScryfallTestFixture.WhiteOracleId,
            "refresh",
            null,
            25,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, providerPrints.Page.TotalCount);

        ScryfallSetsResult sets = RequireSuccess(await service.GetSetsAsync(
            cancellationToken: TestContext.Current.CancellationToken,
            includeRaw: true));
        ScryfallSet set = Assert.Single(sets.Page.Items);
        Assert.Equal("tst", set.Code);
        Assert.True(set.Raw!.Value.GetProperty("fixture_extension").GetBoolean());

        ScryfallCatalogResult catalog = RequireSuccess(await service.GetCatalogAsync(
            "creature-types",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(["Human", "Knight"], catalog.Page.Items);

        ScryfallAutocompleteResult autocomplete = RequireSuccess(await service.AutocompleteAsync(
            "Venerable",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, autocomplete.Page.TotalCount);

        ScryfallBulkMetadataResult bulk = RequireSuccess(await service.GetBulkMetadataAsync(
            "refresh",
            TestContext.Current.CancellationToken));
        Assert.Equal(4, bulk.Datasets.Count);
        Assert.All(bulk.Datasets, dataset => Assert.True(dataset.Raw.TryGetProperty("fixture_extension", out _)));
        Assert.IsType<OperationInvalidInput>((await service.SyncCorpusAsync(
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Value);

        Assert.All(handler.Requests, request =>
        {
            Assert.Contains("mtg-mcp/0.9.0-preview.1", request.UserAgent, StringComparison.Ordinal);
            Assert.Contains("application/json", request.Accept, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Verifies exact cache eligibility across the 24-hour boundary and refresh lineage.
    /// </summary>
    [Fact]
    public async Task FreshnessPolicies_UseExactTtlAndPreserveLineage()
    {
        using TemporaryScryfallDirectory temporary = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero));
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler, clock);

        ScryfallSearchResult original = RequireSuccess(await service.SearchAsync(
            "name:knight",
            cancellationToken: TestContext.Current.CancellationToken));
        int firstCount = handler.Requests.Count;
        clock.Advance(TimeSpan.FromHours(24));
        ScryfallSearchResult boundary = RequireSuccess(await service.SearchAsync(
            "name:knight",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(original.Snapshot.SnapshotId, boundary.Snapshot.SnapshotId);
        Assert.Equal(firstCount, handler.Requests.Count);

        clock.Advance(TimeSpan.FromTicks(1));
        ScryfallSearchResult refreshed = RequireSuccess(await service.SearchAsync(
            "name:knight",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.NotEqual(original.Snapshot.SnapshotId, refreshed.Snapshot.SnapshotId);
        Assert.Equal(original.Snapshot.SnapshotId, refreshed.Snapshot.PredecessorId);

        await using (SqliteConnection connection = new(
            $"Data Source={Path.Combine(temporary.Path, "scryfall.db")};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2L, await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM request_snapshots;",
                TestContext.Current.CancellationToken));
            Assert.Equal(3L, await ScalarAsync(
                connection,
                "SELECT COUNT(*) FROM snapshot_payloads;",
                TestContext.Current.CancellationToken));
        }

        _ = RequireSuccess(await service.DeleteSnapshotAsync(
            original.Snapshot.SnapshotId,
            original.Snapshot.Checksum,
            true,
            TestContext.Current.CancellationToken));
        ScryfallSnapshotSummary retained = Assert.Single(RequireSuccess(await service.ListSnapshotsAsync(
            "search",
            cancellationToken: TestContext.Current.CancellationToken)).Items);
        Assert.Equal(refreshed.Snapshot.SnapshotId, retained.SnapshotId);
        Assert.Equal(original.Snapshot.SnapshotId, retained.PredecessorId);

        clock.Advance(TimeSpan.FromDays(10));
        ScryfallSearchResult stale = RequireSuccess(await service.SearchAsync(
            "name:knight",
            freshnessPolicy: "cache-only",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("stale", stale.Snapshot.Freshness);
        Assert.All(stale.Page.Items.SelectMany(value => value.PriceEvidence), value => Assert.Equal("stale", value.Freshness));
        Assert.IsType<OperationNotCached>((await service.SearchAsync(
            "different query",
            freshnessPolicy: "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
    }

    /// <summary>
    /// Verifies current-plus-previous retention, guarded rollback, and guarded deletion across real reopenings.
    /// </summary>
    [Fact]
    public async Task CorpusLifecycle_RetainsTwoGenerationsAndGuardsMutation()
    {
        using TemporaryScryfallDirectory temporary = new();
        ScryfallCorpusSyncResult first = await SyncRevisionAsync(temporary.Path, 1, "First Knight");
        ScryfallCorpusSyncResult second = await SyncRevisionAsync(temporary.Path, 2, "Second Knight");
        ScryfallCorpusSyncResult third = await SyncRevisionAsync(temporary.Path, 3, "Third Knight");

        RecordingHandler handler = ScryfallTestFixture.Provider(3, "Third Knight");
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallCorpusStatus status = RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken));
        Assert.Equal(third.GenerationId, status.Active!.GenerationId);
        Assert.Equal(second.GenerationId, status.Previous!.GenerationId);
        Assert.NotEqual(first.GenerationId, status.Active.GenerationId);

        Assert.IsType<OperationInvalidInput>((await service.RollbackCorpusAsync(
            third.GenerationId,
            second.GenerationId,
            false,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationConflict>((await service.RollbackCorpusAsync(
            Guid.NewGuid(),
            second.GenerationId,
            true,
            TestContext.Current.CancellationToken)).Value);
        ScryfallCorpusMutationResult rollback = RequireSuccess(await service.RollbackCorpusAsync(
            third.GenerationId,
            second.GenerationId,
            true,
            TestContext.Current.CancellationToken));
        Assert.Equal(second.GenerationId, rollback.ActiveGenerationId);
        Assert.Equal(third.GenerationId, rollback.PreviousGenerationId);

        Assert.IsType<OperationInvalidInput>((await service.DeleteCorpusAsync(
            second.GenerationId,
            false,
            TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationConflict>((await service.DeleteCorpusAsync(
            third.GenerationId,
            true,
            TestContext.Current.CancellationToken)).Value);
        ScryfallCorpusMutationResult deleted = RequireSuccess(await service.DeleteCorpusAsync(
            second.GenerationId,
            true,
            TestContext.Current.CancellationToken));
        Assert.Null(deleted.ActiveGenerationId);
        Assert.Null(deleted.PreviousGenerationId);
        ScryfallCorpusStatus empty = RequireSuccess(await service.GetCorpusStatusAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal("not-cached", empty.State);
        Assert.True(empty.RefreshEligible);
        Assert.Null(empty.LastMetadataCheckAtUtc);
    }

    /// <summary>
    /// Verifies read-only and invalid paths fail before HTTP or storage mutation.
    /// </summary>
    [Fact]
    public async Task ReadOnlyAndInvalidInputs_ReturnExplicitFailuresWithoutSideEffects()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = new(
            temporary.Path,
            allowLocalWrites: false,
            "0.9.0-preview.1",
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);

        AssertUnavailable(await service.SearchAsync(
            "name:knight",
            cancellationToken: TestContext.Current.CancellationToken), "local-write-required");
        AssertUnavailable(await service.SearchAsync(
            "name:knight",
            freshnessPolicy: "refresh",
            cancellationToken: TestContext.Current.CancellationToken), "local-write-required");
        AssertUnavailable(await service.SyncCorpusAsync(
            cancellationToken: TestContext.Current.CancellationToken), "local-write-required");
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "scryfall.db")));

        ScryfallCollectionResult cacheOnlyCollection = RequireSuccess(await service.GetCollectionAsync(
            [new ScryfallCardLookup("exact-name", "Missing Fixture")],
            freshnessPolicy: "cache-only",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("not-cached", Assert.Single(cacheOnlyCollection.Page.Items).Status);
        Assert.Empty(handler.Requests);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "scryfall.db")));

        Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            " ",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            "x",
            unique: "invalid",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            "x",
            order: "invented",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            "x",
            pageSize: 101,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.SearchAsync(
            "x",
            pageSize: 26,
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetCardAsync(
            new ScryfallCardLookup("printing", SetCode: "eld"),
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetCollectionAsync(
            [],
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetCollectionAsync(
            Enumerable.Repeat(new ScryfallCardLookup("exact-name", "x"), 151).ToArray(),
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetCollectionAsync(
            [new ScryfallCardLookup("exact-name", "x")],
            pageSize: 26,
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetCatalogAsync(
            "invented",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetSetsAsync(
            " ",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.AutocompleteAsync(
            " ",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.SearchTagsAsync(
            "x",
            "invalid",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetCardsByTagAsync(
            "x",
            "oracle",
            minimumWeight: "invalid",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationUnavailable>((await service.SyncCorpusAsync(
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.IsType<OperationInvalidInput>((await service.GetSnapshotAsync(
            Guid.NewGuid(),
            pageSize: 26,
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
    }

    /// <summary>
    /// Verifies blocking statuses stop immediately, transient failures are bounded, and stale evidence is named rather than substituted.
    /// </summary>
    [Fact]
    public async Task ProviderFailures_StopOrRetryWithBoundedHonestOutcomes()
    {
        using TemporaryScryfallDirectory blockedRoot = new();
        RecordingHandler blocked = new(_ => ScryfallTestFixture.Json("{}", HttpStatusCode.TooManyRequests));
        using (ScryfallService service = CreateService(blockedRoot.Path, blocked))
        {
            AssertUnavailable(await service.SearchAsync(
                "x",
                cancellationToken: TestContext.Current.CancellationToken), "scryfall-access-blocked");
        }

        Assert.Single(blocked.Requests);

        using TemporaryScryfallDirectory foreignRoot = new();
        RecordingHandler foreign = new(_ => ScryfallTestFixture.Json(JsonSerializer.Serialize(new
        {
            @object = "list",
            has_more = true,
            next_page = "https://unexpected.test/cards/search?page=2",
            data = new[] { JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.WhiteCard()) },
        })));
        using (ScryfallService service = CreateService(foreignRoot.Path, foreign))
        {
            AssertUnavailable(await service.SearchAsync(
                "foreign-page",
                cancellationToken: TestContext.Current.CancellationToken), "unexpected-provider-host");
        }

        Assert.Single(foreign.Requests);

        using TemporaryScryfallDirectory transportRoot = new();
        RecordingHandler transport = new((_, _) => throw new HttpRequestException("fixture transport failure"));
        using (ScryfallService service = CreateService(transportRoot.Path, transport))
        {
            AssertUnavailable(await service.SearchAsync(
                "transport-failure",
                cancellationToken: TestContext.Current.CancellationToken), "scryfall-unavailable");
        }

        Assert.Equal(3, transport.Requests.Count);
        AssertProviderStartsArePaced(transport.Requests);

        using TemporaryScryfallDirectory retryRoot = new();
        int attempts = 0;
        RecordingHandler retry = new(_ =>
        {
            attempts++;
            return attempts < 3
                ? ScryfallTestFixture.Json("{}", HttpStatusCode.ServiceUnavailable)
                : ScryfallTestFixture.Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    has_more = false,
                    data = new[] { JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.WhiteCard()) },
                }));
        });
        using (ScryfallService service = CreateService(retryRoot.Path, retry))
        {
            Assert.Single(RequireSuccess(await service.SearchAsync(
                "x",
                cancellationToken: TestContext.Current.CancellationToken)).Page.Items);
        }

        Assert.Equal(3, retry.Requests.Count);
        AssertProviderStartsArePaced(retry.Requests);

        using TemporaryScryfallDirectory staleRoot = new();
        MutableTimeProvider clock = new(new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero));
        RecordingHandler initial = ScryfallTestFixture.Provider();
        Guid snapshotId;
        using (ScryfallService service = CreateService(staleRoot.Path, initial, clock))
        {
            snapshotId = RequireSuccess(await service.SearchAsync(
                "stale-query",
                cancellationToken: TestContext.Current.CancellationToken)).Snapshot.SnapshotId;
        }

        clock.Advance(TimeSpan.FromHours(25));
        RecordingHandler failure = new(_ => ScryfallTestFixture.Json("{}", HttpStatusCode.Forbidden));
        using (ScryfallService service = CreateService(staleRoot.Path, failure, clock))
        {
            OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>((await service.SearchAsync(
                "stale-query",
                cancellationToken: TestContext.Current.CancellationToken)).Value);
            Assert.Contains(snapshotId.ToString("D"), unavailable.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies corrupt and cancelled bulk streams leave the previously active generation unchanged.
    /// </summary>
    [Fact]
    public async Task FailedCorpusSyncAndCancellation_LeaveActiveGenerationAtomic()
    {
        using TemporaryScryfallDirectory temporary = new();
        ScryfallCorpusSyncResult active = await SyncRevisionAsync(temporary.Path, 1, "Stable Knight");
        string[] corruptLines = ["not-json"];

        RecordingHandler corrupt = ScryfallTestFixture.Provider(
            2,
            "Corrupt Knight",
            request => request.RequestUri!.AbsolutePath == "/download/rulings.jsonl.gz"
                ? ScryfallTestFixture.Bytes(ScryfallTestFixture.GzipLines(corruptLines))
                : null);
        using (ScryfallService service = CreateService(temporary.Path, corrupt))
        {
            OperationUnavailable failure = Assert.IsType<OperationUnavailable>((await service.SyncCorpusAsync(
                "refresh",
                active.GenerationId,
                TestContext.Current.CancellationToken)).Value);
            Assert.Equal("invalid-scryfall-corpus", failure.ReasonCode);
            Assert.Contains("rulings dataset", failure.Message, StringComparison.Ordinal);
            Assert.Equal(
                active.GenerationId,
                RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken)).Active!.GenerationId);
        }

        byte[] cyclicTags = CycleOracleTags();
        RecordingHandler cyclic = ScryfallTestFixture.Provider(
            3,
            "Cycle Knight",
            request => request.RequestUri!.AbsolutePath == "/download/oracle_tags.jsonl.gz"
                ? ScryfallTestFixture.Bytes(cyclicTags)
                : null);
        using (ScryfallService service = CreateService(temporary.Path, cyclic))
        {
            OperationUnavailable failure = Assert.IsType<OperationUnavailable>((await service.SyncCorpusAsync(
                "refresh",
                active.GenerationId,
                TestContext.Current.CancellationToken)).Value);
            Assert.Equal("invalid-scryfall-corpus", failure.ReasonCode);
            Assert.Equal(
                active.GenerationId,
                RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken)).Active!.GenerationId);
        }

        string danglingRuling = JsonSerializer.Serialize(new
        {
            @object = "ruling",
            oracle_id = Guid.Parse("99999999-9999-4999-8999-999999999999"),
            source = "wotc",
            published_at = "2026-07-04",
            comment = "Dangling fixture ruling.",
        });
        RecordingHandler dangling = ScryfallTestFixture.Provider(
            4,
            "Dangling Knight",
            request => request.RequestUri!.AbsolutePath == "/download/rulings.jsonl.gz"
                ? ScryfallTestFixture.Bytes(ScryfallTestFixture.GzipLines([danglingRuling]))
                : null);
        using (ScryfallService service = CreateService(temporary.Path, dangling))
        {
            OperationUnavailable failure = Assert.IsType<OperationUnavailable>((await service.SyncCorpusAsync(
                "refresh",
                active.GenerationId,
                TestContext.Current.CancellationToken)).Value);
            Assert.Equal("invalid-scryfall-corpus", failure.ReasonCode);
            Assert.Equal(
                active.GenerationId,
                RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken)).Active!.GenerationId);
        }

        using CancellationTokenSource cancellation = new();
        IReadOnlyDictionary<string, byte[]> corpus = ScryfallTestFixture.CompressedCorpus("Cancelled Knight");
        RecordingHandler cancelled = ScryfallTestFixture.Provider(
            4,
            "Cancelled Knight",
            request =>
            {
                if (request.RequestUri!.AbsolutePath != "/download/all_cards.jsonl.gz")
                {
                    return null;
                }

                cancellation.Cancel();
                return ScryfallTestFixture.Bytes(corpus["all_cards"]);
            });
        using (ScryfallService service = CreateService(temporary.Path, cancelled))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await service.SyncCorpusAsync("refresh", active.GenerationId, cancellation.Token));
            Assert.Equal(
                active.GenerationId,
                RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken)).Active!.GenerationId);
        }
    }

    /// <summary>
    /// Verifies each index-backed tag relationship check rejects evidence whose referenced identity is absent.
    /// </summary>
    [Fact]
    public async Task DanglingTagRelationships_RejectHierarchyOracleAndArtTargets()
    {
        using TemporaryScryfallDirectory temporary = new();
        ScryfallCorpusSyncResult active = await SyncRevisionAsync(temporary.Path, 1, "Stable Knight");
        (string Dataset, string Kind)[] cases =
        [
            ("oracle_tags", "hierarchy"),
            ("oracle_tags", "oracle-assignment"),
            ("art_tags", "art-assignment"),
        ];
        for (int index = 0; index < cases.Length; index++)
        {
            (string dataset, string kind) = cases[index];
            byte[] invalidTags = DanglingTagDataset(dataset, kind);
            RecordingHandler handler = ScryfallTestFixture.Provider(
                index + 2,
                "Stable Knight",
                request => request.RequestUri!.AbsolutePath == $"/download/{dataset}.jsonl.gz"
                    ? ScryfallTestFixture.Bytes(invalidTags)
                    : null);
            using ScryfallService service = CreateService(temporary.Path, handler);

            OperationUnavailable failure = Assert.IsType<OperationUnavailable>((await service.SyncCorpusAsync(
                "refresh",
                active.GenerationId,
                TestContext.Current.CancellationToken)).Value);

            Assert.Equal("invalid-scryfall-corpus", failure.ReasonCode);
            Assert.Equal(
                active.GenerationId,
                RequireSuccess(await service.GetCorpusStatusAsync(TestContext.Current.CancellationToken)).Active!.GenerationId);
        }
    }

    /// <summary>
    /// Verifies a later provider page failure never publishes a partially complete request snapshot.
    /// </summary>
    [Fact]
    public async Task LaterPageFailure_DoesNotPublishPartialSnapshot()
    {
        using TemporaryScryfallDirectory temporary = new();
        int requests = 0;
        RecordingHandler handler = new(request =>
        {
            requests++;
            if (requests == 1)
            {
                return ScryfallTestFixture.Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    has_more = true,
                    next_page = "https://fixture.test/cards/search?page=2",
                    data = new[] { JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.WhiteCard()) },
                }));
            }

            return ScryfallTestFixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        });
        using ScryfallService service = CreateService(temporary.Path, handler);

        Assert.IsType<OperationUnavailable>((await service.SearchAsync(
            "two-page-query",
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Empty(RequireSuccess(await service.ListSnapshotsAsync(
            "search",
            cancellationToken: TestContext.Current.CancellationToken)).Items);
    }

    /// <summary>
    /// Verifies successful provider pagination stores every source page and ordered member before publishing.
    /// </summary>
    [Fact]
    public async Task MultiPageSuccess_PersistsEveryRawPageAndMember()
    {
        using TemporaryScryfallDirectory temporary = new();
        int requests = 0;
        RecordingHandler handler = new(_ =>
        {
            requests++;
            return requests == 1
                ? ScryfallTestFixture.Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    has_more = true,
                    next_page = "https://fixture.test/cards/search?page=2",
                    data = new[] { JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.WhiteCard()) },
                }))
                : ScryfallTestFixture.Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    has_more = false,
                    data = new[] { JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.RedCard()) },
                }));
        });
        using ScryfallService service = CreateService(temporary.Path, handler);

        ScryfallSearchResult result = RequireSuccess(await service.SearchAsync(
            "two-page-success",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(2, result.Page.TotalCount);
        Assert.Equal(2, handler.Requests.Count);
        AssertProviderStartsArePaced(handler.Requests);
        await using SqliteConnection connection = new($"Data Source={Path.Combine(temporary.Path, "scryfall.db")};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM snapshot_pages;",
            TestContext.Current.CancellationToken));
        Assert.Equal(2L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM snapshot_members;",
            TestContext.Current.CancellationToken));
        Assert.Equal(4L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM snapshot_payloads;",
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies the provider collection boundary accepts 75 distinct misses without silently truncating them.
    /// </summary>
    [Fact]
    public async Task Collection_AcceptsProviderLimitAndPreservesEveryPosition()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallCardLookup[] lookups = Enumerable.Range(0, 75)
            .Select(index => new ScryfallCardLookup("exact-name", $"Missing Fixture {index}"))
            .ToArray();

        ScryfallCollectionResult result = RequireSuccess(await service.GetCollectionAsync(
            lookups,
            pageSize: 100,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(75, result.Page.Items.Count);
        Assert.All(result.Page.Items, row => Assert.Equal("not-found", row.Status));
        RecordedRequest request = Assert.Single(handler.Requests.Where(
            value => value.Uri.AbsolutePath == "/cards/collection"));
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(75, body.RootElement.GetProperty("identifiers").GetArrayLength());

        using TemporaryScryfallDirectory doubleFacedRoot = new();
        JsonObject doubleFaced = JsonNode.Parse(ScryfallTestFixture.WhiteCard())!.AsObject();
        doubleFaced["name"] = "Front Fixture // Back Fixture";
        doubleFaced["card_faces"] = new JsonArray
        {
            new JsonObject { ["name"] = "Front Fixture" },
            new JsonObject { ["name"] = "Back Fixture" },
        };
        RecordingHandler doubleFacedHandler = new(_ => ScryfallTestFixture.Json(JsonSerializer.Serialize(new
        {
            @object = "list",
            data = new[] { JsonSerializer.Deserialize<JsonElement>(doubleFaced.ToJsonString()) },
            not_found = Array.Empty<object>(),
        })));
        using ScryfallService doubleFacedService = CreateService(doubleFacedRoot.Path, doubleFacedHandler);
        ScryfallCollectionResult doubleFacedResult = RequireSuccess(await doubleFacedService.GetCollectionAsync(
            [new ScryfallCardLookup("exact-name", "Front Fixture")],
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("found", Assert.Single(doubleFacedResult.Page.Items).Status);
    }

    /// <summary>
    /// Verifies Commander and sideboard-sized duplicate inputs are accepted without duplicate provider identifiers.
    /// </summary>
    [Theory]
    [InlineData(76)]
    [InlineData(100)]
    [InlineData(115)]
    public async Task Collection_AcceptsDeckSizedDuplicateRows(int count)
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallCardLookup[] lookups = Enumerable.Repeat(
            new ScryfallCardLookup("exact-name", "Missing Fixture"),
            count).ToArray();

        ScryfallCollectionResult first = RequireSuccess(await service.GetCollectionAsync(
            lookups,
            pageSize: 100,
            cancellationToken: TestContext.Current.CancellationToken));
        List<ScryfallCollectionRow> rows = [.. first.Page.Items];
        string? cursor = first.Page.NextCursor;
        while (cursor is not null)
        {
            ScryfallCollectionResult next = RequireSuccess(await service.GetCollectionAsync(
                lookups,
                freshnessPolicy: "refresh",
                cursor: cursor,
                pageSize: 100,
                cancellationToken: TestContext.Current.CancellationToken));
            rows.AddRange(next.Page.Items);
            cursor = next.Page.NextCursor;
        }

        Assert.Equal(count, first.Page.TotalCount);
        Assert.Equal(Enumerable.Range(0, count), rows.Select(value => value.Index));
        Assert.All(rows, row => Assert.Equal("not-found", row.Status));
        RecordedRequest request = Assert.Single(handler.Requests.Where(
            value => value.Uri.AbsolutePath == "/cards/collection"));
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Single(body.RootElement.GetProperty("identifiers").EnumerateArray());
    }

    /// <summary>
    /// Verifies 150 distinct misses become two provider batches and immutable bounded continuation pages.
    /// </summary>
    [Fact]
    public async Task Collection_BatchesOneHundredFiftyAndPaginatesWithoutReacquisition()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallCardLookup[] lookups = Enumerable.Range(0, 150)
            .Select(index => new ScryfallCardLookup("exact-name", $"Missing Fixture {index}"))
            .ToArray();

        ScryfallCollectionResult first = RequireSuccess(await service.GetCollectionAsync(
            lookups,
            pageSize: 100,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(150, first.Page.TotalCount);
        Assert.Equal(Enumerable.Range(0, 100), first.Page.Items.Select(value => value.Index));
        Assert.NotNull(first.Page.NextCursor);
        Assert.NotNull(first.Snapshot);
        Assert.Null(first.CorpusGenerationId);

        RecordedRequest[] requests = handler.Requests.Where(
            value => value.Uri.AbsolutePath == "/cards/collection").ToArray();
        Assert.Equal(2, requests.Length);
        AssertProviderStartsArePaced(requests);
        foreach (RecordedRequest request in requests)
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            Assert.Equal(75, body.RootElement.GetProperty("identifiers").GetArrayLength());
        }

        int requestCount = handler.Requests.Count;
        ScryfallCollectionResult second = RequireSuccess(await service.GetCollectionAsync(
            lookups,
            freshnessPolicy: "refresh",
            cursor: first.Page.NextCursor,
            pageSize: 25,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(Enumerable.Range(100, 25), second.Page.Items.Select(value => value.Index));
        Assert.NotNull(second.Page.NextCursor);
        ScryfallCollectionResult third = RequireSuccess(await service.GetCollectionAsync(
            lookups,
            cursor: second.Page.NextCursor,
            pageSize: 100,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(Enumerable.Range(125, 25), third.Page.Items.Select(value => value.Index));
        Assert.Null(third.Page.NextCursor);
        Assert.Equal(requestCount, handler.Requests.Count);

        ScryfallCardLookup[] wrongLookups = [.. lookups];
        wrongLookups[0] = new ScryfallCardLookup("exact-name", "Different Fixture");
        Assert.IsType<OperationInvalidInput>((await service.GetCollectionAsync(
            wrongLookups,
            cursor: first.Page.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        string originalCursor = first.Page.NextCursor!;
        string tampered = (originalCursor[0] == 'A' ? 'B' : 'A') + originalCursor[1..];
        Assert.IsType<OperationInvalidInput>((await service.GetCollectionAsync(
            lookups,
            cursor: tampered,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.Equal(requestCount, handler.Requests.Count);

        await using SqliteConnection connection = new(
            $"Data Source={Path.Combine(temporary.Path, "scryfall.db")};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM request_snapshots WHERE operation = 'card-collection';",
            TestContext.Current.CancellationToken));
        Assert.Equal(2L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM snapshot_pages;",
            TestContext.Current.CancellationToken));
        await connection.DisposeAsync();

        _ = RequireSuccess(await service.DeleteSnapshotAsync(
            first.Snapshot!.SnapshotId,
            first.Snapshot.Checksum,
            true,
            TestContext.Current.CancellationToken));
        OperationUnavailable deletedEvidence = Assert.IsType<OperationUnavailable>((await service.GetCollectionAsync(
            lookups,
            cursor: first.Page.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.Equal("collection-cursor-evidence-unavailable", deletedEvidence.ReasonCode);
    }

    /// <summary>
    /// Verifies exact deck-identity evidence deduplicates, skips unsupported language acquisition, and replays locally.
    /// </summary>
    [Fact]
    public async Task ExactCollectionEvidence_ResolvesAndReplaysRetainedEvidence()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallEvidenceLookup red = new(
            new ScryfallCardLookup("exact-name", "Monastery Swiftspear"));
        ScryfallEvidenceLookup nonEnglish = new(
            new ScryfallCardLookup("printing", SetCode: "eld", CollectorNumber: "35"),
            "fr");
        ScryfallEvidenceLookup[] lookups = [red, red, nonEnglish];

        ScryfallExactCollectionEvidence first = RequireSuccess(await service.ResolveExactCollectionAsync(
            lookups,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["found", "found", "not-cached"], first.Rows.Select(value => value.Status));
        Assert.Equal([0, 1, 2], first.Rows.Select(value => value.Index));
        Assert.NotNull(first.Binding.Snapshot);
        RecordedRequest request = Assert.Single(handler.Requests.Where(
            value => value.Uri.AbsolutePath == "/cards/collection"));
        using (JsonDocument body = JsonDocument.Parse(request.Body!))
        {
            Assert.Single(body.RootElement.GetProperty("identifiers").EnumerateArray());
        }

        int requestCount = handler.Requests.Count;
        ScryfallExactCollectionEvidence replay = RequireSuccess(await service.ReplayExactCollectionAsync(
            lookups,
            first.Binding,
            TestContext.Current.CancellationToken));
        Assert.Equal(
            JsonSerializer.Serialize(first.Rows),
            JsonSerializer.Serialize(replay.Rows));
        Assert.Equal(requestCount, handler.Requests.Count);

        ScryfallCollectionEvidenceBinding wrongChecksum = first.Binding with
        {
            EvidenceChecksum = new string('0', first.Binding.EvidenceChecksum.Length),
        };
        OperationUnavailable mismatch = Assert.IsType<OperationUnavailable>((await service.ReplayExactCollectionAsync(
            lookups,
            wrongChecksum,
            TestContext.Current.CancellationToken)).Value);
        Assert.Equal("identity-evidence-unavailable", mismatch.ReasonCode);

        _ = RequireSuccess(await service.DeleteSnapshotAsync(
            first.Binding.Snapshot!.SnapshotId,
            first.Binding.Snapshot.Checksum,
            true,
            TestContext.Current.CancellationToken));
        OperationUnavailable pruned = Assert.IsType<OperationUnavailable>((await service.ReplayExactCollectionAsync(
            lookups,
            first.Binding,
            TestContext.Current.CancellationToken)).Value);
        Assert.Equal("identity-evidence-unavailable", pruned.ReasonCode);
    }

    /// <summary>
    /// Verifies exact identity acquisition retains the 150-row MCP bound and 75-identifier provider batches.
    /// </summary>
    [Fact]
    public async Task ExactCollectionEvidence_BatchesOneHundredFiftyAndRejectsOneHundredFiftyOne()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallEvidenceLookup[] lookups = Enumerable.Range(0, 150)
            .Select(index => new ScryfallEvidenceLookup(
                new ScryfallCardLookup("exact-name", $"Missing Exact Fixture {index}")))
            .ToArray();

        ScryfallExactCollectionEvidence evidence = RequireSuccess(await service.ResolveExactCollectionAsync(
            lookups,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(150, evidence.Rows.Count);
        Assert.All(evidence.Rows, value => Assert.Equal("not-found", value.Status));
        RecordedRequest[] batches = handler.Requests.Where(
            value => value.Uri.AbsolutePath == "/cards/collection").ToArray();
        Assert.Equal(2, batches.Length);
        Assert.All(batches, request =>
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            Assert.Equal(75, body.RootElement.GetProperty("identifiers").GetArrayLength());
        });

        OperationInvalidInput rejected = Assert.IsType<OperationInvalidInput>(
            (await service.ResolveExactCollectionAsync(
                [.. lookups, lookups[0]],
                cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.Equal("invalid-scryfall-collection", rejected.ReasonCode);
        Assert.Equal(2, batches.Length);
    }

    /// <summary>
    /// Verifies the exact-evidence acquisition boundary uses one batch at 75 and two at 76.
    /// </summary>
    [Theory]
    [InlineData(75, 1)]
    [InlineData(76, 2)]
    public async Task ExactCollectionEvidence_UsesProviderBatchBoundary(
        int lookupCount,
        int expectedBatchCount)
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallEvidenceLookup[] lookups = Enumerable.Range(0, lookupCount)
            .Select(index => new ScryfallEvidenceLookup(
                new ScryfallCardLookup("exact-name", $"Boundary Fixture {index}")))
            .ToArray();

        ScryfallExactCollectionEvidence evidence = RequireSuccess(await service.ResolveExactCollectionAsync(
            lookups,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(lookupCount, evidence.Rows.Count);
        RecordedRequest[] batches = handler.Requests.Where(
            value => value.Uri.AbsolutePath == "/cards/collection").ToArray();
        Assert.Equal(expectedBatchCount, batches.Length);
        Assert.All(batches, request =>
        {
            using JsonDocument body = JsonDocument.Parse(request.Body!);
            Assert.InRange(body.RootElement.GetProperty("identifiers").GetArrayLength(), 1, 75);
        });
    }

    /// <summary>
    /// Verifies cache-only exact resolution remains a successful explicit miss and read-only acquisition never writes.
    /// </summary>
    [Fact]
    public async Task ExactCollectionEvidence_CacheOnlyAndReadOnlyPreserveZeroWriteBehavior()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler handler = ScryfallTestFixture.Provider();
        ScryfallEvidenceLookup[] lookups =
            [new(new ScryfallCardLookup("exact-name", "Monastery Swiftspear"))];
        using ScryfallService reader = new(
            temporary.Path,
            allowLocalWrites: false,
            "0.9.0-preview.1",
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);

        ScryfallExactCollectionEvidence cached = RequireSuccess(await reader.ResolveExactCollectionAsync(
            lookups,
            "cache-only",
            TestContext.Current.CancellationToken));
        Assert.Equal("not-cached", Assert.Single(cached.Rows).Status);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "scryfall.db")));
        Assert.Empty(handler.Requests);

        OperationUnavailable required = Assert.IsType<OperationUnavailable>((await reader.ResolveExactCollectionAsync(
            lookups,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.Equal("local-write-required", required.ReasonCode);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "scryfall.db")));
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Verifies a collection cursor replays its retained generation and fails explicitly after pruning.
    /// </summary>
    [Fact]
    public async Task CollectionCursor_ReplaysRetainedGenerationAndReportsPrunedEvidence()
    {
        using TemporaryScryfallDirectory temporary = new();
        ScryfallCorpusSyncResult firstGeneration = await SyncRevisionAsync(temporary.Path, 1, "Venerable Knight");
        RecordingHandler handler = ScryfallTestFixture.Provider();
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallCardLookup[] lookups = Enumerable.Repeat(
            new ScryfallCardLookup("exact-name", "Venerable Knight"),
            30).ToArray();

        ScryfallCollectionResult first = RequireSuccess(await service.GetCollectionAsync(
            lookups,
            freshnessPolicy: "cache-only",
            pageSize: 10,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(firstGeneration.GenerationId, first.CorpusGenerationId);
        Assert.All(first.Page.Items, value => Assert.Equal("corpus", value.Origin));
        string cursor = first.Page.NextCursor!;

        _ = await SyncRevisionAsync(temporary.Path, 2, "Renewed Knight");
        ScryfallCollectionResult retained = RequireSuccess(await service.GetCollectionAsync(
            lookups,
            freshnessPolicy: "refresh",
            cursor: cursor,
            pageSize: 10,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(firstGeneration.GenerationId, retained.CorpusGenerationId);
        Assert.All(retained.Page.Items, value => Assert.Equal("Venerable Knight", value.Card!.Name));
        Assert.Empty(handler.Requests);

        _ = await SyncRevisionAsync(temporary.Path, 3, "Newest Knight");
        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>((await service.GetCollectionAsync(
            lookups,
            cursor: cursor,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.Equal("collection-cursor-evidence-unavailable", unavailable.ReasonCode);
        Assert.Empty(handler.Requests);
    }

    /// <summary>
    /// Verifies a blocked second provider batch publishes no partial collection snapshot.
    /// </summary>
    [Fact]
    public async Task Collection_SecondBatchBlockPublishesNoPartialSnapshot()
    {
        using TemporaryScryfallDirectory temporary = new();
        int collectionRequests = 0;
        RecordingHandler handler = ScryfallTestFixture.Provider(intercept: request =>
        {
            if (request.RequestUri!.AbsolutePath != "/cards/collection")
            {
                return null;
            }

            collectionRequests++;
            return collectionRequests == 1
                ? ScryfallTestFixture.Json(JsonSerializer.Serialize(new
                {
                    @object = "list",
                    data = new[] { JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.WhiteCard()) },
                    not_found = Array.Empty<object>(),
                }))
                : ScryfallTestFixture.Json("{}", HttpStatusCode.TooManyRequests);
        });
        using ScryfallService service = CreateService(temporary.Path, handler);
        ScryfallCardLookup[] lookups = Enumerable.Range(0, 150)
            .Select(index => new ScryfallCardLookup("exact-name", $"Missing Fixture {index}"))
            .ToArray();

        OperationUnavailable unavailable = Assert.IsType<OperationUnavailable>((await service.GetCollectionAsync(
            lookups,
            cancellationToken: TestContext.Current.CancellationToken)).Value);
        Assert.Equal("scryfall-access-blocked", unavailable.ReasonCode);
        Assert.Equal(2, collectionRequests);
        Assert.Empty(RequireSuccess(await service.ListSnapshotsAsync(
            "card-collection",
            cancellationToken: TestContext.Current.CancellationToken)).Items);
    }

    /// <summary>
    /// Verifies art evidence attached to a face illustration is joined into a corpus card response.
    /// </summary>
    [Fact]
    public async Task CorpusCard_JoinsArtTagsFromFaceIllustrations()
    {
        using TemporaryScryfallDirectory temporary = new();
        Guid faceIllustrationId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        JsonObject faceCard = JsonNode.Parse(ScryfallTestFixture.RedCard())!.AsObject();
        faceCard.Remove("illustration_id");
        faceCard["name"] = "Monastery Swiftspear // Swift Celebration";
        faceCard["card_faces"] = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "Monastery Swiftspear",
                ["illustration_id"] = faceIllustrationId.ToString("D"),
            },
        };
        string artTag = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = ScryfallTestFixture.ArtTagId,
            label = "Running",
            slug = "running",
            type = "illustration",
            description = (string?)null,
            parent_ids = Array.Empty<Guid>(),
            child_ids = Array.Empty<Guid>(),
            aliases = Array.Empty<string>(),
            taggings = new[] { new { illustration_id = faceIllustrationId, weight = "strong" } },
        });
        RecordingHandler handler = ScryfallTestFixture.Provider(
            intercept: request => request.RequestUri!.AbsolutePath switch
            {
                "/download/all_cards.jsonl.gz" => ScryfallTestFixture.Bytes(ScryfallTestFixture.GzipLines(
                    [ScryfallTestFixture.WhiteCard(), faceCard.ToJsonString()])),
                "/download/art_tags.jsonl.gz" => ScryfallTestFixture.Bytes(ScryfallTestFixture.GzipLines([artTag])),
                _ => null,
            });
        using ScryfallService service = CreateService(temporary.Path, handler);
        _ = RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            cancellationToken: TestContext.Current.CancellationToken));

        ScryfallCardResult result = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Monastery Swiftspear"),
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken));

        ScryfallTagEvidence tag = Assert.Single(result.Card.Tags);
        Assert.Equal("art", tag.TagType);
        Assert.Equal(ScryfallTestFixture.ArtTagId, tag.TagId);
        Assert.Equal("complete-direct", result.Card.TagCoverage);
    }

    /// <summary>
    /// Verifies exact-name corpus lookup prefers a whole-card match over the same name on an art-series face.
    /// </summary>
    [Fact]
    public async Task CorpusCard_ExactNamePrefersWholeCardOverFaceMatch()
    {
        using TemporaryScryfallDirectory temporary = new();
        JsonObject artSeries = JsonNode.Parse(ScryfallTestFixture.RedCard())!.AsObject();
        artSeries["id"] = "77777777-7777-4777-8777-777777777777";
        artSeries["oracle_id"] = ScryfallTestFixture.RedOracleId.ToString("D");
        artSeries["name"] = "Monastery Swiftspear // Monastery Swiftspear";
        artSeries["set"] = "aaa";
        artSeries["collector_number"] = "1";
        artSeries["layout"] = "art_series";
        artSeries["card_faces"] = new JsonArray
        {
            new JsonObject { ["name"] = "Monastery Swiftspear" },
        };
        RecordingHandler handler = ScryfallTestFixture.Provider(
            intercept: request => request.RequestUri!.AbsolutePath == "/download/all_cards.jsonl.gz"
                ? ScryfallTestFixture.Bytes(ScryfallTestFixture.GzipLines(
                    [ScryfallTestFixture.WhiteCard(), ScryfallTestFixture.RedCard(), artSeries.ToJsonString()]))
                : null);
        using ScryfallService service = CreateService(temporary.Path, handler);
        OperationResult<ScryfallCorpusSyncResult> sync = await service.SyncCorpusAsync(
            "refresh",
            cancellationToken: TestContext.Current.CancellationToken);
        if (sync.Value is OperationUnavailable failure)
        {
            Assert.Fail($"{failure.ReasonCode}: {failure.Message}");
        }

        _ = RequireSuccess(sync);

        ScryfallCardResult result = RequireSuccess(await service.GetCardAsync(
            new ScryfallCardLookup("exact-name", "Monastery Swiftspear"),
            "cache-only",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ScryfallTestFixture.RedCardId, result.Card.Id);
        Assert.Equal("Monastery Swiftspear", result.Card.Name);
    }

    /// <summary>
    /// Verifies an expired corpus lease lets the next explicit sync remove abandoned staging data.
    /// </summary>
    [Fact]
    public async Task CorpusSync_RemovesAbandonedStagingGeneration()
    {
        using TemporaryScryfallDirectory temporary = new();
        using (ScryfallDatabase database = new(temporary.Path))
        {
            _ = await database.BeginGenerationAsync(
                new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                TestContext.Current.CancellationToken);
        }

        using ScryfallService service = CreateService(temporary.Path, ScryfallTestFixture.Provider());
        _ = RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            cancellationToken: TestContext.Current.CancellationToken));

        await using SqliteConnection connection = new(
            $"Data Source={Path.Combine(temporary.Path, "scryfall.db")};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0L, await ScalarAsync(
            connection,
            "SELECT COUNT(*) FROM corpus_generations WHERE status = 'staging';",
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies line-oriented corpus import hashes a large synthetic stream and rejects its declared-size bound.
    /// </summary>
    [Fact]
    public async Task CorpusImport_StreamsLargeInputAndEnforcesSizeBound()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallDatabase database = new(temporary.Path);
        Guid generationId = await database.BeginGenerationAsync(
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        string[] cards = Enumerable.Range(1, 500)
            .Select(index => ScryfallTestFixture.Card(
                Guid.Parse($"10000000-0000-4000-8000-{index:D12}"),
                Guid.Parse($"20000000-0000-4000-8000-{index:D12}"),
                Guid.Parse($"30000000-0000-4000-8000-{index:D12}"),
                $"Synthetic Card {index}",
                "tst",
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "{W}",
                ["W"]))
            .ToArray();
        byte[] payload = Encoding.UTF8.GetBytes(string.Join('\n', cards) + "\n");
        using JsonDocument raw = JsonDocument.Parse("{}");
        ScryfallBulkData metadata = new(
            Guid.Parse("99999999-9999-4999-8999-999999999999"),
            "all_cards",
            "All Cards",
            "Synthetic streaming fixture.",
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero),
            payload.Length,
            "application/json",
            "gzip",
            "https://fixture.test/all.json",
            "https://fixture.test/all.jsonl.gz",
            raw.RootElement.Clone());
        await using MemoryStream stream = new(payload, writable: false);

        ScryfallCorpusDatasetStatus imported = await database.ImportDatasetAsync(
            generationId,
            metadata,
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(500, imported.RowCount);
        Assert.Equal(payload.Length, imported.SourceBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), imported.Checksum);

        Guid oversizedGeneration = await database.BeginGenerationAsync(
            new DateTimeOffset(2026, 7, 4, 12, 1, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        ScryfallBulkData tinyDeclaration = metadata with { Size = 1 };
        byte[] oversizedPayload = Encoding.UTF8.GetBytes(new string('x', 1_048_578));
        await using MemoryStream oversized = new(oversizedPayload, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => database.ImportDatasetAsync(
            oversizedGeneration,
            tinyDeclaration,
            oversized,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Installs one fixture corpus generation through a separately opened service.
    /// </summary>
    private static async Task<ScryfallCorpusSyncResult> SyncRevisionAsync(
        string dataRoot,
        int revision,
        string whiteName)
    {
        using ScryfallService service = CreateService(dataRoot, ScryfallTestFixture.Provider(revision, whiteName));
        return RequireSuccess(await service.SyncCorpusAsync(
            "refresh",
            null,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Builds a pair of mutually ancestral tags so activation must reject the graph.
    /// </summary>
    private static byte[] CycleOracleTags()
    {
        Guid[] firstParent = [ScryfallTestFixture.WeenieTagId];
        Guid[] secondParent = [ScryfallTestFixture.AggroTagId];
        string first = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = ScryfallTestFixture.AggroTagId,
            label = "Aggro",
            slug = "aggro",
            type = "oracle",
            description = "Cycle fixture.",
            parent_ids = firstParent,
            child_ids = secondParent,
            aliases = Array.Empty<string>(),
            taggings = Array.Empty<object>(),
        });
        string second = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = ScryfallTestFixture.WeenieTagId,
            label = "White Weenie",
            slug = "white-weenie",
            type = "oracle",
            description = "Cycle fixture.",
            parent_ids = secondParent,
            child_ids = firstParent,
            aliases = Array.Empty<string>(),
            taggings = new[] { new { oracle_id = ScryfallTestFixture.WhiteOracleId, weight = "strong" } },
        });
        string[] lines = [first, second];
        return ScryfallTestFixture.GzipLines(lines);
    }

    /// <summary>
    /// Builds one well-shaped tag dataset containing the selected missing relationship target.
    /// </summary>
    private static byte[] DanglingTagDataset(string dataset, string kind)
    {
        Guid missingId = Guid.Parse("99999999-9999-4999-8999-999999999999");
        Guid[] parentIds = kind == "hierarchy" ? [missingId] : [];
        object[] taggings = kind switch
        {
            "oracle-assignment" => [new { oracle_id = missingId, weight = "strong" }],
            "art-assignment" => [new { illustration_id = missingId, weight = "strong" }],
            _ => [],
        };
        string tag = JsonSerializer.Serialize(new
        {
            @object = "tag",
            id = Guid.Parse("88888888-8888-4888-8888-888888888888"),
            label = "Dangling Fixture",
            slug = "dangling-fixture",
            type = dataset == "art_tags" ? "illustration" : "oracle",
            description = "Dangling relationship fixture.",
            parent_ids = parentIds,
            child_ids = Array.Empty<Guid>(),
            aliases = Array.Empty<string>(),
            taggings,
        });
        return ScryfallTestFixture.GzipLines([tag]);
    }

    /// <summary>
    /// Reads one integer SQLite scalar for storage-shape assertions.
    /// </summary>
    private static async Task<long> ScalarAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? -1L);
    }

    /// <summary>
    /// Confirms real request observations reflect the exact 500-millisecond reservation policy within timer tolerance.
    /// </summary>
    private static void AssertProviderStartsArePaced(IReadOnlyList<RecordedRequest> requests)
    {
        for (int index = 1; index < requests.Count; index++)
        {
            TimeSpan elapsed = requests[index].StartedAtUtc - requests[index - 1].StartedAtUtc;
            Assert.True(
                elapsed >= TimeSpan.FromMilliseconds(450),
                $"Provider starts were only {elapsed.TotalMilliseconds:F0} milliseconds apart.");
        }
    }

    /// <summary>
    /// Creates a write-authorized service around one fake provider.
    /// </summary>
    private static ScryfallService CreateService(
        string dataRoot,
        RecordingHandler handler,
        TimeProvider? timeProvider = null)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ScryfallTestFixture.ApiBaseUri,
            TimeSpan.FromHours(24),
            timeProvider,
            handler);
    }

    /// <summary>
    /// Extracts a successful result or fails the current assertion.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }

    /// <summary>
    /// Asserts a typed failure with a stable reason code.
    /// </summary>
    private static void AssertUnavailable<TData>(OperationResult<TData> result, string reasonCode)
    {
        OperationUnavailable failure = Assert.IsType<OperationUnavailable>(result.Value);
        Assert.Equal(reasonCode, failure.ReasonCode);
    }
}
