using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MtgMcp.Core.Results;
using MtgMcp.Scryfall;
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises the complete Scryfall toolset through the official stdio MCP client.
/// </summary>
public sealed class ScryfallMcpTests
{
    /// <summary>
    /// Lists the exact fourteen tools visible without local mutation authority.
    /// </summary>
    private static readonly string[] ReadTools =
    [
        "scryfall_autocomplete",
        "scryfall_bulk_metadata",
        "scryfall_card_collection",
        "scryfall_card_get",
        "scryfall_card_prints",
        "scryfall_card_rulings",
        "scryfall_cards_by_tag",
        "scryfall_catalog",
        "scryfall_corpus_status",
        "scryfall_search",
        "scryfall_sets",
        "scryfall_snapshot_get",
        "scryfall_snapshot_list",
        "scryfall_tag_search",
    ];

    /// <summary>
    /// Lists the four explicit local evidence mutations.
    /// </summary>
    private static readonly string[] WriteTools =
    [
        "scryfall_corpus_delete",
        "scryfall_corpus_rollback",
        "scryfall_corpus_sync",
        "scryfall_snapshot_delete",
    ];

    /// <summary>
    /// Verifies the exact schemas, annotations, and per-mode visibility for all eighteen tools.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ScryfallTools_PublishExactModeSurfacesSchemasAndAnnotations()
    {
        await using (McpProcessSession readOnly = await McpProcessSession.StartAsync(
            "read-only",
            "scryfall",
            TestContext.Current.CancellationToken).ConfigureAwait(false))
        {
            IList<McpClientTool> tools = await readOnly.Client.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
            Assert.Equal(ReadTools, tools.Select(value => value.Name));
            JsonElement miss = await CallAsync(
                readOnly,
                "scryfall_search",
                new Dictionary<string, object?> { ["query"] = "name:knight" }).ConfigureAwait(false);
            Assert.Equal("unavailable", miss.GetProperty("kind").GetString());
            Assert.Equal("local-write-required", miss.GetProperty("reasonCode").GetString());
            Assert.False(File.Exists(Path.Combine(readOnly.DataRoot, "scryfall.db")));
        }

        await using McpProcessSession local = await McpProcessSession.StartAsync(
            "local",
            "scryfall",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        IList<McpClientTool> localTools = await local.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        string[] expected = ReadTools.Concat(WriteTools).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, localTools.Select(value => value.Name));

        Dictionary<string, string[]> expectedProperties = new(StringComparer.Ordinal)
        {
            ["scryfall_autocomplete"] = ["cursor", "freshnessPolicy", "includeExtras", "pageSize", "query"],
            ["scryfall_bulk_metadata"] = ["freshnessPolicy"],
            ["scryfall_card_collection"] = ["cursor", "freshnessPolicy", "includeRaw", "lookups", "pageSize"],
            ["scryfall_card_get"] = ["freshnessPolicy", "includeRaw", "lookup"],
            ["scryfall_card_prints"] = ["cursor", "freshnessPolicy", "includeRaw", "oracleId", "pageSize"],
            ["scryfall_card_rulings"] = ["cursor", "freshnessPolicy", "includeRaw", "oracleId", "pageSize", "scryfallCardId"],
            ["scryfall_cards_by_tag"] = ["cursor", "includeDescendants", "includeRaw", "minimumWeight", "pageSize", "tagIdentity", "tagType"],
            ["scryfall_catalog"] = ["catalog", "cursor", "freshnessPolicy", "pageSize"],
            ["scryfall_corpus_delete"] = ["acknowledgeDataLoss", "expectedActiveGeneration"],
            ["scryfall_corpus_rollback"] = ["acknowledgeActivationChange", "expectedActiveGeneration", "expectedPreviousGeneration"],
            ["scryfall_corpus_status"] = [],
            ["scryfall_corpus_sync"] = ["expectedActiveGeneration", "metadataPolicy"],
            ["scryfall_search"] = ["cursor", "direction", "freshnessPolicy", "includeExtras", "includeMultilingual", "includeRaw", "includeVariations", "order", "pageSize", "query", "unique"],
            ["scryfall_sets"] = ["codeOrId", "cursor", "freshnessPolicy", "includeRaw", "pageSize"],
            ["scryfall_snapshot_delete"] = ["acknowledgeDataLoss", "expectedChecksum", "snapshotId"],
            ["scryfall_snapshot_get"] = ["cursor", "includeRaw", "pageSize", "snapshotId"],
            ["scryfall_snapshot_list"] =
                ["cursor", "operation", "pageSize", "retrievedAfterUtc", "retrievedBeforeUtc"],
            ["scryfall_tag_search"] = ["cursor", "includeRaw", "pageSize", "query", "tagType"],
        };
        HashSet<string> writes = [.. WriteTools];
        HashSet<string> destructive =
        [
            "scryfall_corpus_delete",
            "scryfall_corpus_rollback",
            "scryfall_snapshot_delete",
        ];
        HashSet<string> openWorld =
        [
            "scryfall_autocomplete",
            "scryfall_bulk_metadata",
            "scryfall_card_collection",
            "scryfall_card_get",
            "scryfall_card_prints",
            "scryfall_card_rulings",
            "scryfall_catalog",
            "scryfall_search",
            "scryfall_sets",
            "scryfall_corpus_sync",
        ];
        foreach (McpClientTool tool in localTools)
        {
            Assert.Equal(
                expectedProperties[tool.Name],
                tool.ProtocolTool.InputSchema.GetProperty("properties")
                    .EnumerateObject()
                    .Select(value => value.Name)
                    .Order(StringComparer.Ordinal));
            Assert.NotNull(tool.ProtocolTool.OutputSchema);
            Assert.NotNull(tool.ProtocolTool.Annotations);
            Assert.Equal(!writes.Contains(tool.Name), tool.ProtocolTool.Annotations.ReadOnlyHint);
            Assert.Equal(destructive.Contains(tool.Name), tool.ProtocolTool.Annotations.DestructiveHint);
            Assert.Equal(!writes.Contains(tool.Name), tool.ProtocolTool.Annotations.IdempotentHint);
            Assert.Equal(openWorld.Contains(tool.Name), tool.ProtocolTool.Annotations.OpenWorldHint);
        }
    }

    /// <summary>
    /// Verifies a separate MCP process reuses an installed corpus and exact search snapshot without HTTP.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ScryfallTools_ReuseFixtureCorpusAndSnapshotForRedWhiteEvidence()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "scryfall",
            SeedAsync,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        JsonElement status = await CallSuccessAsync(
            session,
            "scryfall_corpus_status",
            new Dictionary<string, object?>()).ConfigureAwait(false);
        Assert.Equal("available", status.GetProperty("state").GetString());

        JsonElement white = await CallSuccessAsync(
            session,
            "scryfall_card_get",
            new Dictionary<string, object?>
            {
                ["lookup"] = new { kind = "exact-name", value = "Venerable Knight" },
                ["freshnessPolicy"] = "cache-only",
            }).ConfigureAwait(false);
        Assert.Equal("corpus", white.GetProperty("origin").GetString());
        Assert.Equal("Venerable Knight", white.GetProperty("card").GetProperty("name").GetString());
        Assert.Equal("complete-direct", white.GetProperty("card").GetProperty("tagCoverage").GetString());
        Assert.Equal("white-weenie", white.GetProperty("card").GetProperty("tags")[0].GetProperty("slug").GetString());
        Assert.False(white.GetProperty("card").TryGetProperty("raw", out _));

        JsonElement fullWhite = await CallSuccessAsync(
            session,
            "scryfall_card_get",
            new Dictionary<string, object?>
            {
                ["lookup"] = new { kind = "exact-name", value = "Venerable Knight" },
                ["freshnessPolicy"] = "cache-only",
                ["includeRaw"] = true,
            }).ConfigureAwait(false);
        Assert.True(fullWhite.GetProperty("card").TryGetProperty("raw", out _));

        JsonElement red = await CallSuccessAsync(
            session,
            "scryfall_card_get",
            new Dictionary<string, object?>
            {
                ["lookup"] = new { kind = "exact-name", value = "Monastery Swiftspear" },
                ["freshnessPolicy"] = "cache-only",
            }).ConfigureAwait(false);
        Assert.Equal("R", red.GetProperty("card").GetProperty("colors")[0].GetString());

        ScryfallCardLookup[] commanderLookups = CommanderLookups();
        JsonElement commanderFirst = await CallSuccessAsync(
            session,
            "scryfall_card_collection",
            new Dictionary<string, object?>
            {
                ["lookups"] = commanderLookups,
                ["freshnessPolicy"] = "cache-only",
                ["pageSize"] = 40,
            }).ConfigureAwait(false);
        JsonElement firstPage = commanderFirst.GetProperty("page");
        Assert.Equal(100, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(40, firstPage.GetProperty("items").GetArrayLength());
        Assert.Equal(0, firstPage.GetProperty("items")[0].GetProperty("index").GetInt32());
        string commanderCursor = firstPage.GetProperty("nextCursor").GetString()!;
        JsonElement commanderSecond = await CallSuccessAsync(
            session,
            "scryfall_card_collection",
            new Dictionary<string, object?>
            {
                ["lookups"] = commanderLookups,
                ["freshnessPolicy"] = "refresh",
                ["cursor"] = commanderCursor,
                ["pageSize"] = 100,
            }).ConfigureAwait(false);
        Assert.Equal(60, commanderSecond.GetProperty("page").GetProperty("items").GetArrayLength());
        Assert.Equal(40, commanderSecond.GetProperty("page").GetProperty("items")[0].GetProperty("index").GetInt32());
        Assert.False(commanderSecond.GetProperty("page").TryGetProperty("nextCursor", out _));

        JsonElement inherited = await CallSuccessAsync(
            session,
            "scryfall_cards_by_tag",
            new Dictionary<string, object?>
            {
                ["tagIdentity"] = "aggro",
                ["tagType"] = "oracle",
                ["includeDescendants"] = true,
            }).ConfigureAwait(false);
        Assert.Equal("inherited", inherited.GetProperty("assignments")[0].GetProperty("relationship").GetString());

        JsonElement rulings = await CallSuccessAsync(
            session,
            "scryfall_card_rulings",
            new Dictionary<string, object?>
            {
                ["oracleId"] = ScryfallTestFixture.WhiteOracleId,
                ["freshnessPolicy"] = "cache-only",
            }).ConfigureAwait(false);
        Assert.Equal("Fixture ruling.", rulings.GetProperty("page").GetProperty("items")[0].GetProperty("comment").GetString());

        JsonElement search = await CallSuccessAsync(
            session,
            "scryfall_search",
            new Dictionary<string, object?>
            {
                ["query"] = "ci<=rw mv=1",
                ["freshnessPolicy"] = "cache-only",
                ["pageSize"] = 1,
            }).ConfigureAwait(false);
        Guid snapshotId = search.GetProperty("snapshot").GetProperty("snapshotId").GetGuid();
        string checksum = search.GetProperty("snapshot").GetProperty("checksum").GetString()!;
        Assert.Equal(2, search.GetProperty("page").GetProperty("totalCount").GetInt32());

        JsonElement replay = await CallSuccessAsync(
            session,
            "scryfall_snapshot_get",
            new Dictionary<string, object?>
            {
                ["snapshotId"] = snapshotId,
                ["includeRaw"] = true,
            }).ConfigureAwait(false);
        Assert.Equal(snapshotId, replay.GetProperty("summary").GetProperty("snapshotId").GetGuid());
        Assert.Equal(2, replay.GetProperty("items").GetArrayLength());
        Assert.Equal(0, replay.GetProperty("items")[0].GetProperty("ordinal").GetInt32());
        Assert.True(replay.GetProperty("items")[0].TryGetProperty("raw", out _));

        JsonElement compactReplay = await CallSuccessAsync(
            session,
            "scryfall_snapshot_get",
            new Dictionary<string, object?> { ["snapshotId"] = snapshotId }).ConfigureAwait(false);
        Assert.Equal(2, compactReplay.GetProperty("items").GetArrayLength());
        Assert.False(compactReplay.GetProperty("items")[0].TryGetProperty("raw", out _));
        Assert.NotEmpty(compactReplay.GetProperty("items")[0].GetProperty("checksum").GetString()!);

        _ = await CallSuccessAsync(
            session,
            "scryfall_snapshot_delete",
            new Dictionary<string, object?>
            {
                ["snapshotId"] = snapshotId,
                ["expectedChecksum"] = checksum,
                ["acknowledgeDataLoss"] = true,
            }).ConfigureAwait(false);
        JsonElement deleted = await CallAsync(
            session,
            "scryfall_snapshot_get",
            new Dictionary<string, object?> { ["snapshotId"] = snapshotId }).ConfigureAwait(false);
        Assert.Equal("not-found", deleted.GetProperty("kind").GetString());
    }

    /// <summary>
    /// Seeds a complete corpus and one exact search through the same production service boundary.
    /// </summary>
    private static async Task SeedAsync(string dataRoot, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> fixtureCards = CommanderFixtureCards();
        RecordingHandler handler = ScryfallTestFixture.Provider(intercept: request =>
        {
            if (request.RequestUri!.AbsolutePath != "/cards/collection")
            {
                return null;
            }

            string requestJson = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            using JsonDocument requestDocument = JsonDocument.Parse(requestJson);
            List<JsonElement> cards = [];
            foreach (JsonElement identifier in requestDocument.RootElement.GetProperty("identifiers").EnumerateArray())
            {
                string name = identifier.GetProperty("name").GetString()!;
                if (fixtureCards.TryGetValue(name, out string? raw))
                {
                    cards.Add(JsonSerializer.Deserialize<JsonElement>(raw));
                }
            }

            return ScryfallTestFixture.Json(JsonSerializer.Serialize(new
            {
                @object = "list",
                data = cards,
                not_found = Array.Empty<object>(),
            }));
        });
        using ScryfallService service = new(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);
        Assert.IsType<OperationSuccess<ScryfallCorpusSyncResult>>((await service.SyncCorpusAsync(
            "refresh",
            null,
            cancellationToken).ConfigureAwait(false)).Value);
        Assert.IsType<OperationSuccess<ScryfallCollectionResult>>((await service.GetCollectionAsync(
            CommanderLookups(),
            pageSize: 100,
            cancellationToken: cancellationToken).ConfigureAwait(false)).Value);
        Assert.IsType<OperationSuccess<ScryfallSearchResult>>((await service.SearchAsync(
            "ci<=rw mv=1",
            cancellationToken: cancellationToken).ConfigureAwait(false)).Value);
    }

    /// <summary>
    /// Builds a deterministic 100-identity Commander-shaped lookup list for MCP pagination.
    /// </summary>
    private static ScryfallCardLookup[] CommanderLookups()
    {
        return
        [
            new ScryfallCardLookup("exact-name", "Venerable Knight"),
            new ScryfallCardLookup("exact-name", "Monastery Swiftspear"),
            .. Enumerable.Range(2, 98).Select(index =>
                new ScryfallCardLookup("exact-name", $"Fixture Card {index}")),
        ];
    }

    /// <summary>
    /// Builds lossless fake provider cards for the 98 identities not present in the miniature corpus.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CommanderFixtureCards()
    {
        return Enumerable.Range(2, 98).ToDictionary(
            index => $"Fixture Card {index}",
            index => ScryfallTestFixture.Card(
                Guid.Parse($"00000000-0000-4000-8000-{index + 1000:D12}"),
                Guid.Parse($"00000000-0000-4000-8000-{index + 2000:D12}"),
                Guid.Parse($"00000000-0000-4000-8000-{index + 3000:D12}"),
                $"Fixture Card {index}",
                "tst",
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "{W}",
                ["W"]),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Calls one MCP tool and returns its closed operation result.
    /// </summary>
    private static async Task<JsonElement> CallAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, result.IsError);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        return structured.GetProperty("result");
    }

    /// <summary>
    /// Calls one tool and extracts its successful structured data.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        JsonElement result = await CallAsync(session, toolName, arguments).ConfigureAwait(false);
        Assert.Equal("success", result.GetProperty("kind").GetString());
        return result.GetProperty("data");
    }
}
