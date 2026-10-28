using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises <c>stats_simulate</c> through an official MCP client against real server processes: binding, a seeded run
/// on a local deck, the work limit, the stats-only toolset, and the on-curve preset.
/// </summary>
public sealed class StatisticsSimulateMcpTests
{
    /// <summary>
    /// Identifies the fixture target printing.
    /// </summary>
    private static readonly Guid TargetPrintingId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>
    /// Identifies the fixture target Oracle card.
    /// </summary>
    private static readonly Guid TargetOracleId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");

    /// <summary>
    /// Identifies the fixture land printing.
    /// </summary>
    private static readonly Guid ForestPrintingId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    /// <summary>
    /// Identifies the fixture land Oracle card.
    /// </summary>
    private static readonly Guid ForestOracleId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>
    /// Identifies the saved deck seeded before the server starts.
    /// </summary>
    private static readonly Guid SeededDeckId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    /// <summary>
    /// Identifies the target entry of the seeded deck.
    /// </summary>
    private static readonly Guid SeededTargetEntryId = Guid.Parse("cccccccc-0000-4000-8000-000000000002");

    /// <summary>
    /// Identifies the land entry of the seeded deck.
    /// </summary>
    private static readonly Guid SeededForestEntryId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    /// <summary>
    /// Verifies a seeded run on a deck made through the server needs no card data, binds a union with its kind last,
    /// reports its counts, and gives the same answer when run again with the same seed.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalDeck_RunsSeededRulesByEntryId_AndReplaysWithTheSameSeed()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks,stats",
            McpCredentials.None,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CallSuccessAsync(
            session,
            "local_deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Simulation E2E",
                    format = "custom",
                    entries = new object[]
                    {
                        new { quantity = 24, cardName = "Land", zone = "main" },
                        new { quantity = 36, cardName = "Spell", zone = "main" },
                    },
                },
            }).ConfigureAwait(false);
        Guid deckId = deck.GetProperty("deckId").GetGuid();
        Guid land = FindEntryId(deck, "Land");
        Dictionary<string, object?> arguments = Request(deckId, land);

        JsonElement first = await CallSuccessAsync(session, "stats_simulate", arguments).ConfigureAwait(false);
        JsonElement second = await CallSuccessAsync(session, "stats_simulate", arguments).ConfigureAwait(false);

        Assert.Equal("0123456789abcdef", first.GetProperty("seed").GetString());
        Assert.Equal("simulation-v1", first.GetProperty("modelVersion").GetString());
        Assert.Equal(2_000, first.GetProperty("samples").GetInt32());
        JsonElement result = Assert.Single(first.GetProperty("results").EnumerateArray());
        Assert.Equal("two-mana", result.GetProperty("id").GetString());
        Assert.InRange(result.GetProperty("hits").GetInt32(), 1_000, 2_000);
        Assert.Equal(2, result.GetProperty("wilson95").GetArrayLength());
        Assert.Equal(2, first.GetProperty("sampleHands").GetArrayLength());
        Assert.Equal(first.GetProperty("fingerprint").GetString(), second.GetProperty("fingerprint").GetString());
        Assert.Equal(first.GetRawText(), second.GetRawText());
    }

    /// <summary>
    /// Verifies a missing nested field, an unknown field, and an unknown kind are refused with the error flag and the
    /// JSON path of the value, through the real server.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task BadArguments_AreRefusedWithTheirPath()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "stats",
            McpCredentials.None,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        Guid id = Guid.NewGuid();

        Dictionary<string, object?> missing = Request(id, id);
        ((Dictionary<string, object?>)((object[])missing["eachTurn"]!)[0])!.Remove("maxPerTurn");
        Dictionary<string, object?> unknownField = Request(id, id);
        ((Dictionary<string, object?>)((object[])unknownField["eachTurn"]!)[0])["surprise"] = 1;
        Dictionary<string, object?> unknownKind = Request(id, id);
        ((Dictionary<string, object?>)((object[])unknownKind["eachTurn"]!)[0])["kind"] = "teleport";

        JsonElement missingBody = AssertFailure(
            await CallAsync(session, "stats_simulate", missing),
            "invalid-input",
            "invalid-arguments");
        JsonElement fieldBody = AssertFailure(
            await CallAsync(session, "stats_simulate", unknownField),
            "invalid-input",
            "invalid-arguments");
        JsonElement kindBody = AssertFailure(
            await CallAsync(session, "stats_simulate", unknownKind),
            "invalid-input",
            "invalid-arguments");

        Assert.Equal("eachTurn[0].maxPerTurn", missingBody.GetProperty("path").GetString());
        Assert.Contains("surprise", fieldBody.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("eachTurn[0]", kindBody.GetProperty("path").GetString());
        Assert.Contains("play", kindBody.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a request over the work limit is refused with the error flag and a message that states the limit.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task OverTheWorkLimit_IsRefusedWithTheLimit()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks,stats",
            McpCredentials.None,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CallSuccessAsync(
            session,
            "local_deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Too large",
                    format = "custom",
                    entries = new object[]
                    {
                        new { quantity = 40, cardName = "Land", zone = "main" },
                        new { quantity = 60, cardName = "Spell", zone = "main" },
                    },
                },
            }).ConfigureAwait(false);
        Dictionary<string, object?> arguments = Request(
            deck.GetProperty("deckId").GetGuid(),
            FindEntryId(deck, "Land"));
        arguments["samples"] = 100_000;
        arguments["turns"] = 20;

        JsonElement body = AssertFailure(
            await CallAsync(session, "stats_simulate", arguments),
            "invalid-input",
            "simulation-too-large");

        Assert.Contains("limit of 50000000", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the tool is listed and works when only the stats toolset is selected, reading the deck store itself.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task WithOnlyTheStatsToolset_ReadsTheSeededDeck()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "read-only",
            "stats",
            SeedDeckAndCardsAsync,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        JsonElement result = await CallSuccessAsync(
            session,
            "stats_simulate",
            Request(SeededDeckId, SeededForestEntryId)).ConfigureAwait(false);

        Assert.Contains(tools, tool => tool.Name == "stats_simulate");
        Assert.DoesNotContain(tools, tool => tool.Name.StartsWith("local_deck_", StringComparison.Ordinal));
        Assert.Equal(6, result.GetProperty("groups")[0].GetProperty("cards").GetInt32());
        Assert.Equal(2_000, result.GetProperty("samples").GetInt32());
    }

    /// <summary>
    /// Verifies every mode can run the on-curve preset over installed card data and a saved deck without changing
    /// either.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("read-only")]
    [InlineData("local")]
    [InlineData("remote")]
    public async Task EveryMode_OnCurvePreset_UsesOnlySeededLocalData(string mode)
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            mode,
            "decks,scryfall,stats",
            SeedDeckAndCardsAsync,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CallSuccessAsync(
            session,
            "local_deck_get",
            new Dictionary<string, object?> { ["deckId"] = SeededDeckId }).ConfigureAwait(false);
        long revision = deck.GetProperty("revision").GetInt64();

        JsonElement result = await CallSuccessAsync(
            session,
            "stats_simulate",
            new Dictionary<string, object?>
            {
                ["deck"] = new { deckId = SeededDeckId, expectedRevision = revision },
                ["preset"] = new
                {
                    targetEntryId = SeededTargetEntryId,
                    landRules = new[] { new { entryId = SeededForestEntryId, rule = "one-mana-same-turn" } },
                    turnLimit = 2,
                    onThePlay = true,
                    kind = "on-curve",
                },
                ["samples"] = 100,
                ["sampleHands"] = 0,
                ["seed"] = "0123456789abcdef",
            }).ConfigureAwait(false);

        Assert.Equal("on-curve", result.GetProperty("results")[0].GetProperty("id").GetString());
        Assert.Equal("{1}{G}", result.GetProperty("preset").GetProperty("target").GetProperty("manaCost").GetString());
        Assert.Equal(
            "On-Curve Target",
            result.GetProperty("preset").GetProperty("target").GetProperty("name").GetString());
        JsonElement land = Assert.Single(result.GetProperty("preset").GetProperty("candidateLands").EnumerateArray());
        Assert.Equal(SeededForestEntryId, land.GetProperty("entryId").GetGuid());
        Assert.Equal("one-mana-same-turn", land.GetProperty("rule").GetString());
        Assert.Equal(
            "target-first",
            result.GetProperty("appliedRules").GetProperty("eachTurn")[0].GetProperty("pick").GetString());
        JsonElement unchanged = await CallSuccessAsync(
            session,
            "local_deck_get",
            new Dictionary<string, object?> { ["deckId"] = SeededDeckId }).ConfigureAwait(false);
        Assert.Equal(revision, unchanged.GetProperty("revision").GetInt64());
    }

    /// <summary>
    /// Builds a request for one land group chosen by entry ID, two turns of play, and one condition.
    /// </summary>
    private static Dictionary<string, object?> Request(Guid deckId, Guid land)
    {
        return new Dictionary<string, object?>
        {
            ["deck"] = new { deckId },
            ["groups"] = new object[]
            {
                new
                {
                    id = "lands",
                    select = new { ids = new[] { land }, kind = "entry-ids" },
                },
            },
            ["openingHand"] = new { size = 7, onThePlay = true },
            ["eachTurn"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["kind"] = "play",
                    ["group"] = "lands",
                    ["pick"] = "random",
                    ["maxPerTurn"] = 1,
                    ["manaFrom"] = "same-turn",
                },
            },
            ["turns"] = 2,
            ["conditions"] = new object[]
            {
                new { id = "two-mana", check = new { amount = 1, turn = 2, kind = "mana-at-least" } },
            },
            ["samples"] = 2_000,
            ["sampleHands"] = 2,
            ["seed"] = "0123456789abcdef",
        };
    }

    /// <summary>
    /// Finds one test entry by its card name.
    /// </summary>
    private static Guid FindEntryId(JsonElement deck, string cardName)
    {
        return deck.GetProperty("entries")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("cardName").GetString() == cardName)
            .GetProperty("entryId")
            .GetGuid();
    }

    /// <summary>
    /// Seeds the isolated data root with card facts for the target and the land, and one saved deck.
    /// </summary>
    private static async Task SeedDeckAndCardsAsync(string dataRoot, CancellationToken cancellationToken)
    {
        byte[] cards = ScryfallTestFixture.GzipLines(
        [
            ScryfallTestFixture.WhiteCard(),
            ScryfallTestFixture.RedCard(),
            Card(TargetPrintingId, TargetOracleId, "On-Curve Target", "{1}{G}", "Creature — Test", null),
            Card(ForestPrintingId, ForestOracleId, "On-Curve Forest", null, "Basic Land — Forest", ["G"]),
        ]);
        RecordingHandler handler = ScryfallTestFixture.Provider(intercept: request =>
        {
            return request.RequestUri!.AbsolutePath == "/download/all_cards.jsonl.gz"
                ? ScryfallTestFixture.Bytes(cards)
                : null;
        });
        using ScryfallService service = new(
            dataRoot,
            allowLocalWrites: true,
            "0.9.0-preview.1",
            ScryfallTestFixture.ResultOptions,
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);
        OperationResult<ScryfallCorpusSyncResult> synced = await service.SyncCorpusAsync(
            "refresh",
            null,
            cancellationToken).ConfigureAwait(false);
        Assert.IsType<OperationSuccess<ScryfallCorpusSyncResult>>(synced.Value);
        using SqliteDeckStore store = new(dataRoot, "0.9.0-preview.1");
        OperationResult<DeckDocument> created = await store.CreateAsync(
            new DeckCreateRequest(
                "Seeded Simulation Fixture",
                Format: "custom",
                Entries:
                [
                    new DeckEntryDraft(
                        1,
                        "On-Curve Target",
                        TargetOracleId,
                        TargetPrintingId,
                        Zone: "main",
                        EntryId: SeededTargetEntryId),
                    new DeckEntryDraft(
                        6,
                        "On-Curve Forest",
                        ForestOracleId,
                        ForestPrintingId,
                        Zone: "main",
                        EntryId: SeededForestEntryId),
                ],
                DeckId: SeededDeckId),
            cancellationToken).ConfigureAwait(false);
        Assert.IsType<OperationSuccess<DeckDocument>>(created.Value);
    }

    /// <summary>
    /// Builds one small single-faced Scryfall source record for the process fixture.
    /// </summary>
    private static string Card(
        Guid printingId,
        Guid oracleId,
        string name,
        string? manaCost,
        string typeLine,
        IReadOnlyList<string>? producedMana)
    {
        return JsonSerializer.Serialize(new
        {
            @object = "card",
            id = printingId,
            oracle_id = oracleId,
            illustration_id = printingId,
            name,
            set = "tst",
            collector_number = printingId.ToString("N")[..6],
            lang = "en",
            released_at = "2026-07-04",
            mana_cost = manaCost,
            cmc = manaCost is null ? 0.0m : 2.0m,
            type_line = typeLine,
            oracle_text = "Fixture rules text.",
            colors = Array.Empty<string>(),
            color_identity = Array.Empty<string>(),
            keywords = Array.Empty<string>(),
            legalities = new Dictionary<string, string> { ["commander"] = "legal" },
            image_uris = new Dictionary<string, string> { ["normal"] = $"https://img.test/{printingId:D}.jpg" },
            prices = new Dictionary<string, string?> { ["usd"] = "0.10" },
            produced_mana = producedMana,
        });
    }

    /// <summary>
    /// Calls one tool through the official MCP client and returns the raw protocol result.
    /// </summary>
    private static async Task<CallToolResult> CallAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        return await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls one tool and extracts its successful structured data.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await CallAsync(session, toolName, arguments).ConfigureAwait(false);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.NotEqual(true, result.IsError);
        Assert.Equal("success", structured.GetProperty("kind").GetString());
        return structured.GetProperty("data");
    }

    /// <summary>
    /// Asserts a failure with the error flag set, the given kind, and the given reason code.
    /// </summary>
    private static JsonElement AssertFailure(CallToolResult result, string kind, string reasonCode)
    {
        string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.True(result.IsError, text);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(kind, structured.GetProperty("kind").GetString());
        Assert.Equal(reasonCode, structured.GetProperty("reasonCode").GetString());
        return structured;
    }
}
