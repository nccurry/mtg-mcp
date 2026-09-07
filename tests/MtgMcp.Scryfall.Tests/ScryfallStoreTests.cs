using System.IO.Compression;
using System.Text.Json;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies Scryfall stores through their named operations.
/// </summary>
public sealed class ScryfallStoreTests
{
    /// <summary>
    /// Verifies card-data state changes keep the active, previous, and metadata-check fields consistent.
    /// </summary>
    [Fact]
    public async Task CardDataStore_PreservesStateAcrossLifecycleChanges()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallDatabase database = new(temporary.Path);
        ScryfallCorpusStore store = new(database);
        DateTimeOffset firstActivation = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset secondActivation = firstActivation.AddHours(1);
        DateTimeOffset metadataCheck = secondActivation.AddMinutes(30);

        Guid firstGeneration = await InstallGenerationAsync(
            store,
            revision: 1,
            activatedAtUtc: firstActivation,
            cancellationToken: TestContext.Current.CancellationToken);
        Guid secondGeneration = await InstallGenerationAsync(
            store,
            revision: 2,
            activatedAtUtc: secondActivation,
            cancellationToken: TestContext.Current.CancellationToken);

        ScryfallCorpusStatus activated = RequireSuccess(await store.GetStatusAsync(
            secondActivation,
            TimeSpan.FromDays(1),
            TestContext.Current.CancellationToken));
        Assert.Equal(secondGeneration, activated.Active!.GenerationId);
        Assert.Equal(firstGeneration, activated.Previous!.GenerationId);
        Assert.Equal(secondActivation, activated.LastMetadataCheckAtUtc);

        await store.RecordMetadataCheckAsync(metadataCheck, TestContext.Current.CancellationToken);
        ScryfallCorpusStatus metadataUpdated = RequireSuccess(await store.GetStatusAsync(
            metadataCheck,
            TimeSpan.FromDays(1),
            TestContext.Current.CancellationToken));
        Assert.Equal(secondGeneration, metadataUpdated.Active!.GenerationId);
        Assert.Equal(firstGeneration, metadataUpdated.Previous!.GenerationId);
        Assert.Equal(metadataCheck, metadataUpdated.LastMetadataCheckAtUtc);

        _ = RequireSuccess(await store.RollbackAsync(
            secondGeneration,
            firstGeneration,
            acknowledgeActivationChange: true,
            cancellationToken: TestContext.Current.CancellationToken));
        ScryfallCorpusStatus rolledBack = RequireSuccess(await store.GetStatusAsync(
            metadataCheck,
            TimeSpan.FromDays(1),
            TestContext.Current.CancellationToken));
        Assert.Equal(firstGeneration, rolledBack.Active!.GenerationId);
        Assert.Equal(secondGeneration, rolledBack.Previous!.GenerationId);
        Assert.Equal(metadataCheck, rolledBack.LastMetadataCheckAtUtc);

        _ = RequireSuccess(await store.DeleteAsync(
            firstGeneration,
            acknowledgeDataLoss: true,
            cancellationToken: TestContext.Current.CancellationToken));
        ScryfallCorpusStatus deleted = RequireSuccess(await store.GetStatusAsync(
            metadataCheck,
            TimeSpan.FromDays(1),
            TestContext.Current.CancellationToken));
        Assert.Equal("not-cached", deleted.State);
        Assert.Null(deleted.Active);
        Assert.Null(deleted.Previous);
        Assert.Null(deleted.LastMetadataCheckAtUtc);
    }

    /// <summary>
    /// Verifies the snapshot store preserves one immutable request record through its named operations.
    /// </summary>
    [Fact]
    public async Task SnapshotStore_SavesFindsReplaysAndDeletesOneSnapshot()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallDatabase database = new(temporary.Path);
        ScryfallSnapshotStore store = new(database);
        DateTimeOffset retrievedAtUtc = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        string requestJson = "{\"q\":\"name:knight\"}";
        string[] pages = ["{\"object\":\"list\"}"];
        string[] members = ["{\"id\":\"first\"}", "{\"id\":\"second\"}"];

        StoredSnapshot saved = await store.SaveAsync(
            "search",
            requestJson,
            "search:name:knight",
            pages,
            members,
            retrievedAtUtc,
            TestContext.Current.CancellationToken);
        StoredSnapshot found = Assert.IsType<StoredSnapshot>(await store.FindAsync(
            "search:name:knight",
            retrievedAtUtc,
            TestContext.Current.CancellationToken));
        Assert.Equal(saved.Header, found.Header);
        Assert.Equal(members, found.Members);

        ScryfallPage<ScryfallSnapshotSummary> summaries = RequireSuccess(await store.ListAsync(
            "search",
            null,
            null,
            null,
            pageSize: 25,
            cancellationToken: TestContext.Current.CancellationToken));
        ScryfallSnapshotSummary summary = Assert.Single(summaries.Items);
        Assert.Equal(saved.Header.SnapshotId, summary.SnapshotId);

        ScryfallSnapshotPage replay = RequireSuccess(await store.GetAsync(
            saved.Header.SnapshotId,
            null,
            pageSize: 1,
            includeRaw: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(replay.Items);
        Assert.Equal("first", replay.Items[0].Raw!.Value.GetProperty("id").GetString());
        Assert.NotNull(replay.NextCursor);

        ScryfallSnapshotDeleteResult deleted = RequireSuccess(await store.DeleteAsync(
            saved.Header.SnapshotId,
            saved.Header.Checksum,
            acknowledgeDataLoss: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(saved.Header.SnapshotId, deleted.SnapshotId);
        Assert.Null(await store.FindByIdAsync(saved.Header.SnapshotId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Installs one complete fixture generation through the named card-data store.
    /// </summary>
    private static async Task<Guid> InstallGenerationAsync(
        ScryfallCorpusStore store,
        int revision,
        DateTimeOffset activatedAtUtc,
        CancellationToken cancellationToken)
    {
        Guid generationId = await store.BeginGenerationAsync(activatedAtUtc, cancellationToken);
        IReadOnlyDictionary<string, byte[]> compressedDatasets = ScryfallTestFixture.CompressedCorpus();
        using JsonDocument metadataDocument = JsonDocument.Parse(ScryfallTestFixture.BulkMetadata(revision));
        foreach (JsonElement rawMetadata in metadataDocument.RootElement.GetProperty("data").EnumerateArray())
        {
            ScryfallBulkData metadata = ScryfallMapper.BulkData(rawMetadata);
            using MemoryStream compressed = new(compressedDatasets[metadata.Type], writable: false);
            using GZipStream jsonl = new(compressed, CompressionMode.Decompress);
            _ = await store.ImportDatasetAsync(generationId, metadata, jsonl, cancellationToken);
        }

        return RequireSuccess(await store.ActivateGenerationAsync(
            generationId,
            activatedAtUtc,
            cancellationToken)).GenerationId;
    }

    /// <summary>
    /// Extracts successful data while making a typed failure fail the test with its stable details.
    /// </summary>
    private static T RequireSuccess<T>(OperationResult<T> result)
    {
        return Assert.IsType<OperationSuccess<T>>(result.Value).Data;
    }
}
