using System.Text.Json;
using ModelContextProtocol.Protocol;
using MtgMcp.Core.Decks;
using MtgMcp.Core.Results;
using MtgMcp.Decks;
using MtgMcp.Scryfall;
using MtgMcp.Scryfall.Tests;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises the fact-backed on-curve estimate through an official MCP client.
/// </summary>
public sealed class DeckOnCurveMcpTests
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
    /// Identifies the saved deck seeded before read-only mode starts.
    /// </summary>
    private static readonly Guid SeededDeckId = Guid.Parse("cccccccc-0000-4000-8000-000000000001");

    /// <summary>
    /// Identifies the target entry seeded before read-only mode starts.
    /// </summary>
    private static readonly Guid SeededTargetEntryId = Guid.Parse("cccccccc-0000-4000-8000-000000000002");

    /// <summary>
    /// Identifies the forest entry seeded before read-only mode starts.
    /// </summary>
    private static readonly Guid SeededForestEntryId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    /// <summary>
    /// Verifies a local saved deck produces a complete, replayable sampled estimate.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalMode_DeckOnCurveEstimate_ReturnsFactBackedReplayableResult()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            "decks",
            SeedAsync,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CallSuccessAsync(
            session,
            "deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "On-Curve E2E Fixture",
                    format = "custom",
                    entries = new object[]
                    {
                        new
                        {
                            quantity = 1,
                            cardName = "On-Curve Target",
                            oracleId = TargetOracleId,
                            printingId = TargetPrintingId,
                            zone = "main",
                        },
                        new
                        {
                            quantity = 6,
                            cardName = "On-Curve Forest",
                            oracleId = ForestOracleId,
                            printingId = ForestPrintingId,
                            zone = "main",
                        },
                    },
                },
            }).ConfigureAwait(false);
        Guid deckId = deck.GetProperty("deckId").GetGuid();
        long revision = deck.GetProperty("revision").GetInt64();
        Guid targetEntryId = FindEntryId(deck, "On-Curve Target");
        Guid forestEntryId = FindEntryId(deck, "On-Curve Forest");

        JsonElement estimate = await CallSuccessAsync(
            session,
            "deck_on_curve_estimate",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["targetEntryId"] = targetEntryId,
                ["turnLimit"] = 2,
                ["sampleCount"] = 100,
                ["onThePlay"] = true,
                ["seed"] = "0123456789abcdef",
                ["landRules"] = new[]
                {
                    new { entryId = forestEntryId, rule = "one-mana-same-turn" },
                },
            }).ConfigureAwait(false);

        Assert.Equal("sampled-on-curve-estimate", estimate.GetProperty("kind").GetString());
        Assert.Equal("On-Curve Target", estimate.GetProperty("target").GetProperty("name").GetString());
        Assert.Equal(TargetPrintingId, estimate.GetProperty("target").GetProperty("printingId").GetGuid());
        Assert.Equal("{1}{G}", estimate.GetProperty("target").GetProperty("manaCost").GetString());
        Assert.Equal("0123456789abcdef", estimate.GetProperty("seed").GetString());
        Assert.Equal(100, estimate.GetProperty("sampleCount").GetInt32());
        Assert.Equal(100, estimate.GetProperty("completedTrialCount").GetInt32());
        Assert.Equal("wilson-95", estimate.GetProperty("interval").GetProperty("method").GetString());
        Assert.Equal(
            "installed-scryfall-card-data",
            estimate.GetProperty("sourceEvidence").GetProperty("source").GetString());
        Assert.Equal(2, estimate.GetProperty("sourceFacts").GetArrayLength());
        JsonElement land = Assert.Single(
            estimate.GetProperty("landRuleCoverage").GetProperty("candidateLands").EnumerateArray());
        Assert.Equal(forestEntryId, land.GetProperty("entryId").GetGuid());
        Assert.Equal("values", land.GetProperty("producedMana").GetProperty("kind").GetString());
        Assert.Equal("G", land.GetProperty("producedMana").GetProperty("colors")[0].GetString());
        Assert.Equal("one-mana-same-turn", land.GetProperty("rule").GetString());
        Assert.True(estimate.GetProperty("traces").GetArrayLength() <= 2);

        JsonElement unchanged = await CallSuccessAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = deckId }).ConfigureAwait(false);
        Assert.Equal(revision, unchanged.GetProperty("revision").GetInt64());
    }

    /// <summary>
    /// Verifies every operation mode can read installed facts and a saved deck without changing either.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("read-only")]
    [InlineData("local")]
    [InlineData("remote")]
    public async Task EveryMode_DeckOnCurveEstimate_UsesOnlySeededLocalData(string mode)
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            mode,
            "decks",
            SeedDeckAndCardsAsync,
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement deck = await CallSuccessAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = SeededDeckId }).ConfigureAwait(false);
        long revision = deck.GetProperty("revision").GetInt64();

        JsonElement estimate = await CallSuccessAsync(
            session,
            "deck_on_curve_estimate",
            new Dictionary<string, object?>
            {
                ["deckId"] = SeededDeckId,
                ["expectedRevision"] = revision,
                ["targetEntryId"] = SeededTargetEntryId,
                ["turnLimit"] = 2,
                ["sampleCount"] = 100,
                ["onThePlay"] = true,
                ["seed"] = "0123456789abcdef",
                ["landRules"] = new[]
                {
                    new { entryId = SeededForestEntryId, rule = "one-mana-same-turn" },
                },
            }).ConfigureAwait(false);

        Assert.Equal("sampled-on-curve-estimate", estimate.GetProperty("kind").GetString());
        Assert.Equal("installed-scryfall-card-data", estimate.GetProperty("sourceEvidence").GetProperty("source").GetString());
        Assert.Equal("0123456789abcdef", estimate.GetProperty("seed").GetString());

        JsonElement unchanged = await CallSuccessAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = SeededDeckId }).ConfigureAwait(false);
        Assert.Equal(revision, unchanged.GetProperty("revision").GetInt64());
    }

    /// <summary>
    /// Finds one test entry by its fixed card name.
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
    /// Seeds the isolated Scryfall data root with direct facts for the two deck cards.
    /// </summary>
    private static async Task SeedAsync(string dataRoot, CancellationToken cancellationToken)
    {
        byte[] cards = ScryfallTestFixture.GzipLines(
        [
            ScryfallTestFixture.WhiteCard(),
            ScryfallTestFixture.RedCard(),
            Card(
                TargetPrintingId,
                TargetOracleId,
                "On-Curve Target",
                "{1}{G}",
                "Creature — Test",
                null),
            Card(
                ForestPrintingId,
                ForestOracleId,
                "On-Curve Forest",
                null,
                "Basic Land — Forest",
                ["G"]),
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
            ScryfallTestFixture.ApiBaseUri,
            handler: handler);
        OperationResult<ScryfallCorpusSyncResult> result = await service.SyncCorpusAsync(
            "refresh",
            null,
            cancellationToken).ConfigureAwait(false);
        Assert.IsType<OperationSuccess<ScryfallCorpusSyncResult>>(result.Value);
    }

    /// <summary>
    /// Seeds both direct card facts and one exact saved deck before a read-only server starts.
    /// </summary>
    private static async Task SeedDeckAndCardsAsync(string dataRoot, CancellationToken cancellationToken)
    {
        await SeedAsync(dataRoot, cancellationToken).ConfigureAwait(false);
        using SqliteDeckStore store = new(dataRoot, "0.9.0-preview.1");
        OperationResult<DeckDocument> result = await store.CreateAsync(
            new DeckCreateRequest(
                "Seeded On-Curve Fixture",
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
        Assert.IsType<OperationSuccess<DeckDocument>>(result.Value);
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
    /// Calls one tool and extracts its successful structured data.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
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
        Assert.Equal("success", structured.GetProperty("kind").GetString());
        return structured.GetProperty("data");
    }
}
