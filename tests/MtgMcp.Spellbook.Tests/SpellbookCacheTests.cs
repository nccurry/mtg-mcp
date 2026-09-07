using Microsoft.Data.Sqlite;

namespace MtgMcp.Spellbook.Tests;

/// <summary>
/// Verifies exact-response cache identity, cleanup, and privacy boundaries.
/// </summary>
public sealed class SpellbookCacheTests
{
    /// <summary>
    /// Verifies a fresh response is reused with its original retrieval time, then removed after expiry.
    /// </summary>
    [Fact]
    public async Task Cache_ReturnsFreshResponseThenRemovesExpiredRow()
    {
        using TemporarySpellbookDirectory directory = new();
        using SpellbookDatabase database = new(directory.Path);
        SpellbookCache cache = new(database);
        DateTimeOffset retrievedAtUtc = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        SpellbookRequestIdentity identity = Identity("Sol Ring");
        SpellbookNetworkResponse response = Response("{\"next\":null,\"results\":[]}", retrievedAtUtc);

        await cache.StoreAsync(identity, response, TimeSpan.FromMinutes(15), TestContext.Current.CancellationToken);
        SpellbookCachedResponse? fresh = await cache.TryGetAsync(
            identity,
            retrievedAtUtc.AddMinutes(14),
            TimeSpan.FromMinutes(15),
            TestContext.Current.CancellationToken);
        SpellbookCachedResponse? expired = await cache.TryGetAsync(
            identity,
            retrievedAtUtc.AddMinutes(16),
            TimeSpan.FromMinutes(15),
            TestContext.Current.CancellationToken);

        Assert.NotNull(fresh);
        Assert.Equal(retrievedAtUtc, fresh.RetrievedAtUtc);
        Assert.Null(expired);
    }

    /// <summary>
    /// Verifies a contract change clears an old response rather than reporting it as current evidence.
    /// </summary>
    [Fact]
    public async Task Cache_RejectsResponseStoredUnderOlderContractChecksum()
    {
        using TemporarySpellbookDirectory directory = new();
        using SpellbookDatabase database = new(directory.Path);
        SpellbookCache cache = new(database);
        DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        SpellbookRequestIdentity current = Identity("Sol Ring");
        SpellbookRequestIdentity old = new(current.Operation, current.RequestHash, "old-contract-checksum");

        await cache.StoreAsync(old, Response("{\"results\":[]}", now), TimeSpan.FromMinutes(15), TestContext.Current.CancellationToken);
        SpellbookCachedResponse? result = await cache.TryGetAsync(
            current,
            now,
            TimeSpan.FromMinutes(15),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        await using SqliteConnection connection = await database.OpenWriteAsync(TestContext.Current.CancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM response_cache;";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    /// <summary>
    /// Verifies cache schema stores hash-only request identity while preserving source-owned paging JSON unchanged.
    /// </summary>
    [Fact]
    public async Task Cache_StoresNoSeparateRawRequestFields()
    {
        using TemporarySpellbookDirectory directory = new();
        using SpellbookDatabase database = new(directory.Path);
        SpellbookCache cache = new(database);
        DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        const string sourceQuery = "card:\"Thassa's Oracle\"";
        const string json = "{\"next\":\"https://backend.commanderspellbook.com/variants/?q=card%3A%22Thassa%27s%20Oracle%22\",\"results\":[]}";

        await cache.StoreAsync(Identity(sourceQuery), Response(json, now), TimeSpan.FromMinutes(15), TestContext.Current.CancellationToken);
        await using SqliteConnection connection = await database.OpenWriteAsync(TestContext.Current.CancellationToken);
        List<string> columns = [];
        await using SqliteCommand columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(response_cache);";
        await using (SqliteDataReader reader = await columnsCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                columns.Add(reader.GetString(1));
            }
        }

        await using SqliteCommand rowCommand = connection.CreateCommand();
        rowCommand.CommandText = "SELECT request_hash, response_json FROM response_cache;";
        await using SqliteDataReader row = await rowCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await row.ReadAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(sourceQuery, row.GetString(0), StringComparison.Ordinal);
        Assert.Contains("q=card%3A", row.GetString(1), StringComparison.Ordinal);
        Assert.DoesNotContain(columns, value => value.Contains("query", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, value => value.Contains("deck", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            [
                "operation",
                "request_hash",
                "response_json",
                "response_checksum",
                "source_api_version",
                "contract_checksum",
                "retrieved_at_utc",
            ],
            columns);
    }

    /// <summary>
    /// Builds one current contract-bound search identity without exposing its raw cache representation.
    /// </summary>
    private static SpellbookRequestIdentity Identity(string sourceQuery)
    {
        SpellbookRequestDetails details = new(sourceQuery, new SpellbookPageRequest(20, 0, true), null);
        return SpellbookRequestIdentity.Create("variant-search", HttpMethod.Get, "/variants/", details, deck: null);
    }

    /// <summary>
    /// Builds one valid source response record for direct cache tests.
    /// </summary>
    private static SpellbookNetworkResponse Response(string json, DateTimeOffset retrievedAtUtc)
    {
        return new SpellbookNetworkResponse(json, SpellbookHash.Compute(json), retrievedAtUtc);
    }
}
