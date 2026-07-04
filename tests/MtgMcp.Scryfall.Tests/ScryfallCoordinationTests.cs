using System.Text.Json;
using Microsoft.Data.Sqlite;
using MtgMcp.Core.Results;

namespace MtgMcp.Scryfall.Tests;

/// <summary>
/// Verifies cross-instance SQLite ownership and pacing used by separate MCP processes.
/// </summary>
public sealed class ScryfallCoordinationTests
{
    /// <summary>
    /// Verifies leases have one owner, reject incorrect release, and recover exactly at crash expiry.
    /// </summary>
    [Fact]
    public async Task Leases_CoordinateOwnersAndRecoverAfterExpiry()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallDatabase first = new(temporary.Path);
        using ScryfallDatabase second = new(temporary.Path);
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

        Assert.True(await first.TryAcquireLeaseAsync(
            "request",
            "owner-a",
            now,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));
        Assert.False(await second.TryAcquireLeaseAsync(
            "request",
            "owner-b",
            now,
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));
        await second.ReleaseLeaseAsync("request", "owner-b", TestContext.Current.CancellationToken);
        Assert.False(await second.TryAcquireLeaseAsync(
            "request",
            "owner-b",
            now.AddSeconds(59),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));
        Assert.True(await second.TryAcquireLeaseAsync(
            "request",
            "owner-b",
            now.AddMinutes(1),
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies independent database instances reserve globally ordered 500-millisecond request starts.
    /// </summary>
    [Fact]
    public async Task ProviderPacing_ReservesOneGlobalTimeline()
    {
        using TemporaryScryfallDirectory temporary = new();
        using ScryfallDatabase first = new(temporary.Path);
        using ScryfallDatabase second = new(temporary.Path);
        DateTimeOffset now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        TimeSpan interval = TimeSpan.FromMilliseconds(500);

        TimeSpan firstDelay = await first.ReserveProviderStartAsync(
            now,
            interval,
            TestContext.Current.CancellationToken);
        TimeSpan secondDelay = await second.ReserveProviderStartAsync(
            now,
            interval,
            TestContext.Current.CancellationToken);
        TimeSpan thirdDelay = await first.ReserveProviderStartAsync(
            now,
            interval,
            TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.Zero, firstDelay);
        Assert.Equal(interval, secondDelay);
        Assert.Equal(interval + interval, thirdDelay);
    }

    /// <summary>
    /// Verifies concurrent service instances publish one acquisition and both reuse its immutable result.
    /// </summary>
    [Fact]
    public async Task ConcurrentServices_DeduplicateSameExactRequest()
    {
        using TemporaryScryfallDirectory temporary = new();
        RecordingHandler firstHandler = ScryfallTestFixture.Provider();
        RecordingHandler secondHandler = ScryfallTestFixture.Provider();
        using ScryfallService first = CreateService(temporary.Path, firstHandler);
        using ScryfallService second = CreateService(temporary.Path, secondHandler);

        Task<OperationResult<ScryfallSearchResult>> firstTask = first.SearchAsync(
            "shared-query",
            cancellationToken: TestContext.Current.CancellationToken);
        Task<OperationResult<ScryfallSearchResult>> secondTask = second.SearchAsync(
            "shared-query",
            cancellationToken: TestContext.Current.CancellationToken);
        OperationResult<ScryfallSearchResult>[] results = await Task.WhenAll(firstTask, secondTask);
        ScryfallSearchResult firstResult = Assert.IsType<OperationSuccess<ScryfallSearchResult>>(results[0].Value).Data;
        ScryfallSearchResult secondResult = Assert.IsType<OperationSuccess<ScryfallSearchResult>>(results[1].Value).Data;

        Assert.Equal(firstResult.Snapshot.SnapshotId, secondResult.Snapshot.SnapshotId);
        Assert.Equal(1, firstHandler.Requests.Count + secondHandler.Requests.Count);
    }

    /// <summary>
    /// Verifies concurrent refresh callers both observe the replacement rather than reusing the pre-refresh snapshot.
    /// </summary>
    [Fact]
    public async Task ConcurrentRefresh_WaitsForReplacementSnapshot()
    {
        using TemporaryScryfallDirectory temporary = new();
        using (ScryfallService seed = CreateService(temporary.Path, ScryfallTestFixture.Provider()))
        {
            _ = Assert.IsType<OperationSuccess<ScryfallSearchResult>>((await seed.SearchAsync(
                "refresh-query",
                cancellationToken: TestContext.Current.CancellationToken)).Value).Data;
        }

        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingHandler firstHandler = new(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return ScryfallTestFixture.Json(JsonSerializer.Serialize(new
            {
                @object = "list",
                has_more = false,
                data = new[]
                {
                    JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.WhiteCard()),
                    JsonSerializer.Deserialize<JsonElement>(ScryfallTestFixture.RedCard()),
                },
            }));
        });
        RecordingHandler secondHandler = ScryfallTestFixture.Provider();
        using ScryfallService first = CreateService(temporary.Path, firstHandler);
        using ScryfallService second = CreateService(temporary.Path, secondHandler);

        Task<OperationResult<ScryfallSearchResult>> firstTask = first.SearchAsync(
            "refresh-query",
            freshnessPolicy: "refresh",
            cancellationToken: TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task<OperationResult<ScryfallSearchResult>> secondTask = second.SearchAsync(
            "refresh-query",
            freshnessPolicy: "refresh",
            cancellationToken: TestContext.Current.CancellationToken);
        release.TrySetResult();
        OperationResult<ScryfallSearchResult>[] results = await Task.WhenAll(firstTask, secondTask);
        ScryfallSearchResult firstResult = Assert.IsType<OperationSuccess<ScryfallSearchResult>>(results[0].Value).Data;
        ScryfallSearchResult secondResult = Assert.IsType<OperationSuccess<ScryfallSearchResult>>(results[1].Value).Data;

        Assert.Equal(firstResult.Snapshot.SnapshotId, secondResult.Snapshot.SnapshotId);
        Assert.Single(firstHandler.Requests);
        Assert.Empty(secondHandler.Requests);
    }

    /// <summary>
    /// Verifies an authored migration checksum mismatch is rejected before any data can be read.
    /// </summary>
    [Fact]
    public async Task Database_RejectsMismatchedMigrationChecksum()
    {
        using TemporaryScryfallDirectory temporary = new();
        using (ScryfallDatabase database = new(temporary.Path))
        {
            await database.ReserveProviderStartAsync(
                new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
                TimeSpan.FromMilliseconds(500),
                TestContext.Current.CancellationToken);
        }

        await using (SqliteConnection connection = new(
            $"Data Source={Path.Combine(temporary.Path, "scryfall.db")};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_migrations SET checksum = 'unexpected';";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        using ScryfallDatabase reopened = new(temporary.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.GetCorpusStatusAsync(
            new DateTimeOffset(2026, 7, 4, 12, 0, 1, TimeSpan.Zero),
            TimeSpan.FromHours(24),
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Creates one write-authorized service sharing the requested database root.
    /// </summary>
    private static ScryfallService CreateService(string dataRoot, RecordingHandler handler)
    {
        return new ScryfallService(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);
    }
}
