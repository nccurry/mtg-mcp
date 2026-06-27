using System.Text.Json;
using System.Net;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises the MCP server through stdio with fake Scryfall and Archidekt HTTP backends.
/// </summary>
public sealed class McpE2ETests
{
    /// <summary>
    /// Stores the repeated ramp role filter used by explicit query-plan E2E flows.
    /// </summary>
    private static readonly string[] RampRequiredRoles = ["Ramp"];

    /// <summary>
    /// Stores the secondary category used by bulk category E2E flows.
    /// </summary>
    private static readonly string[] RemovalSecondaryCategories = ["Removal"];

    /// <summary>
    /// Stores the ramp secondary category used by bulk category E2E flows.
    /// </summary>
    private static readonly string[] RampSecondaryCategories = ["Ramp"];

    /// <summary>
    /// Verifies that the MCP server advertises card and workspace tool groups.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ToolDiscovery_ListsExpectedToolGroups()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        IList<McpClientResource> resources = await session.Client.ListResourcesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(tool => tool.Name).Should().Contain(
        [
            "card_search",
            "workspace_start",
            "deck_add_card",
            "deck_analyze_structure",
            "deck_analyze_performance",
            "deck_review_weak_spots",
            "deck_plan_compare_performance",
            "deck_plan_clone",
            "deck_preview_card_package",
            "deck_compare_workspaces_analysis",
            "workspace_diff",
            "workspace_reopen_with_writeback",
            "workspace_validate_legality",
            "deck_compare_goldfish",
            "deck_batch_tuning_report",
            "commander_search_candidates",
            "archidekt_compare_goldfish",
            "server_get_info"
        ]);

        McpClientTool serverInfoTool = tools.Single(tool => tool.Name == "server_get_info");
        serverInfoTool.Title.Should().Be("Server Get Info");
        serverInfoTool.ReturnJsonSchema.Should().NotBeNull();

        CallToolResult serverInfoResult = await session.Client.CallToolAsync(
            "server_get_info",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);
        serverInfoResult.IsError.Should().NotBeTrue(ReadText(serverInfoResult));
        serverInfoResult.StructuredContent.Should().NotBeNull();

        using JsonDocument serverInfoDocument = JsonDocument.Parse(ReadText(serverInfoResult));
        JsonElement serverInfo = serverInfoDocument.RootElement.Clone();
        JsonElement structuredServerInfo = serverInfoResult.StructuredContent!.Value;

        GetString(serverInfo, "assemblyName").Should().Be("MtgMcp.App");
        GetString(serverInfo, "operationMode").Should().Be("apply");
        GetString(structuredServerInfo, "assemblyName").Should().Be("MtgMcp.App");
        GetString(structuredServerInfo, "operationMode").Should().Be("apply");
        resources.Select(resource => resource.Uri).Should().Contain("mtg://workspaces");
    }

    /// <summary>
    /// Verifies that a local workspace can add, export, and analyze a Scryfall card without Archidekt calls.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalWorkspaceFlow_UsesScryfallWithoutArchidekt()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Lightning%20Bolt", LightningBoltJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Local",
                ["format"] = "modern"
            });
        string workspaceId = GetString(workspace, "id");

        JsonElement change = await CallJsonAsync(
            session.Client,
            "deck_add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 2,
                ["category"] = "Mainboard",
                ["detailLevel"] = "normal"
            });
        string export = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "deck_analyze_structure",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        GetString(change, "persistence").Should().Be("local-only");
        export.Should().Contain("2 Lightning Bolt");
        GetInt32(analysis, "totalCards").Should().Be(2);
        GetObject(analysis, "typeCounts").GetProperty("Instant").GetInt32().Should().Be(2);
        scryfall
            .Requests.Should()
            .ContainSingle(request =>
                request.Method == "GET"
                && DecodeRepeatedly(request.PathAndQuery) == "cards/named?fuzzy=Lightning Bolt");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that performance analysis and plan comparison work through MCP stdio.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task PerformanceFlow_AnalyzesAndComparesPlanThroughMcp()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.PostJson("cards/collection", PerformanceCollectionJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);
        archidekt.GetJson("api/decks/456/", RemoteSwampsDeckJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Performance",
                ["format"] = "commander",
                ["decklist"] = """
                    30 Forest
                    70 Blank Spell
                    """
            });
        string workspaceId = GetString(workspace, "id");

        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "deck_analyze_performance",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["simulations"] = 200,
                ["maxTurn"] = 3,
                ["seed"] = 2026,
                ["includeMulligans"] = false
            });
        JsonElement compactAnalysis = await CallJsonAsync(
            session.Client,
            "deck_analyze_performance",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["detailLevel"] = "summary",
                ["simulations"] = 50,
                ["maxTurn"] = 3,
                ["seed"] = 2026
            });
        JsonElement planResult = await CallJsonAsync(
            session.Client,
            "deck_plan_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["name"] = "Add one ramp card",
                ["rationale"] = "The caller selected Arcane Signet from deterministic card data.",
                ["addCards"] = new[] { ExplicitCardChange("Arcane Signet", 1, "Ramp", "Caller-selected ramp add.") }
            });
        string planId = GetString(planResult, "planId");
        JsonElement comparison = await CallJsonAsync(
            session.Client,
            "deck_plan_compare_performance",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["simulations"] = 200,
                ["maxTurn"] = 3,
                ["seed"] = 2026
            });
        JsonElement compactComparison = await CallJsonAsync(
            session.Client,
            "deck_plan_compare_performance",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["detailLevel"] = "normal",
                ["simulations"] = 50,
                ["maxTurn"] = 3,
                ["seed"] = 2026
            });
        CallToolResult invalidDetailLevel = await session.Client.CallToolAsync(
            "deck_analyze_performance",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["detailLevel"] = "verbose"
            },
            cancellationToken: TestContext.Current.CancellationToken);
        JsonElement goldfishComparison = await CallJsonAsync(
            session.Client,
            "archidekt_compare_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["deckIdOrUrl1"] = "456",
                ["targetTurn"] = 3,
                ["simulations"] = 100,
                ["seed"] = 2026
            });

        GetInt32(analysis, "deckSize").Should().Be(100);
        AssertPerformanceTrustContract(analysis, expectedSimulations: 200);
        GetString(compactAnalysis, "detailLevel").Should().Be("summary");
        compactAnalysis.TryGetProperty("turnProbabilities", out _).Should().BeFalse();
        GetProperty(compactAnalysis, "topStrandedCards").GetArrayLength().Should().BeLessThanOrEqualTo(5);
        FindNamedTurn(GetArray(analysis, "turnProbabilities"), "land-drop-by-turn", 3)
            .GetProperty("sampleSize")
            .GetInt32()
            .Should()
            .Be(200);
        FindNamed(GetArray(analysis, "scenarios"), "stranded-high-mana-risk-by-max-turn")
            .GetProperty("failureDriverCounts")
            .ValueKind.Should()
            .Be(JsonValueKind.Object);
        JsonElement rampDelta = FindNamed(GetArray(comparison, "deltas"), "ramp-cast-by-turn-3", metricProperty: "metric");
        rampDelta.GetProperty("after").GetDouble().Should().BeGreaterThan(rampDelta.GetProperty("before").GetDouble());
        rampDelta.GetProperty("beforeLowConfidenceInterval").ValueKind.Should().NotBe(JsonValueKind.Null);
        AssertPerformanceTrustContract(GetObject(comparison, "before"), expectedSimulations: 200);
        AssertPerformanceTrustContract(GetObject(comparison, "after"), expectedSimulations: 200);
        GetString(compactComparison, "detailLevel").Should().Be("normal");
        GetString(GetObject(compactComparison, "before"), "detailLevel").Should().Be("normal");
        GetProperty(GetObject(compactComparison, "before"), "traceSummary")
            .GetProperty("aggregateCounters")
            .GetProperty("total-runs")
            .GetInt32()
            .Should()
            .Be(100);
        invalidDetailLevel.IsError.Should().BeTrue();
        ReadText(invalidDetailLevel).Should().Contain("summary, normal, or full");
        invalidDetailLevel.StructuredContent.Should().NotBeNull();
        JsonElement error = invalidDetailLevel.StructuredContent!.Value.GetProperty("error");
        GetString(error, "code").Should().Be("validation");
        GetProperty(error, "retriable").GetBoolean().Should().BeFalse();
        GetString(GetObject(error, "details"), "tool").Should().Be("deck_analyze_performance");
        JsonElement reference = GetArray(goldfishComparison, "referenceDecks").Should().ContainSingle().Subject;
        GetString(reference, "source").Should().Be("archidekt");
        GetString(GetObject(goldfishComparison, "activeDeck"), "source").Should().Be("workspace");
        archidekt.Requests.Should().ContainSingle(request => request.Method == "GET" && request.PathAndQuery == "api/decks/456/");
    }

    /// <summary>
    /// Verifies that query-first data tools bind array filters and feed explicit plans through MCP.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task QueryRecommendationFlow_QueriesDataAndCreatesExplicitPlanThroughMcp()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        string query = "o:\"whenever an opponent discards\" or o:\"each opponent discards\" or o:\"draw a card\"";
        scryfall.GetJson(
            ScryfallSearchPath($"({query}) legal:commander usd<=10"),
            QueryRecommendationSearchJson);
        scryfall.PostJson("cards/collection", QueryRecommendationCollectionJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Query Recommendations",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        Dictionary<string, object?> args = new()
        {
            ["workspaceId"] = workspaceId,
            ["goal"] = "Improve Tinybones draw/discard engine",
            ["scryfallQuery"] = query,
            ["limit"] = 3,
            ["maxPrice"] = 10,
            ["requiredRoles"] = new[] { "Draw" },
            ["requiredTags"] = new[] { "Discard" },
            ["excludedRoles"] = new[] { "Wincons" },
            ["excludedTags"] = new[] { "Aristocrats", "Drain" }
        };
        JsonElement data = await CallJsonAsync(session.Client, "deck_query_cards", args);
        JsonElement gethsGrimoire = FindNamed(GetArray(data, "cards"), "Geth's Grimoire", "cardName");
        JsonElement rejected = FindNamed(GetArray(data, "rejected"), "Zulaport Cutthroat", "cardName");
        JsonElement rejectedWincon = FindNamed(GetArray(data, "rejected"), "Torment of Hailfire", "cardName");

        GetString(gethsGrimoire, "role").Should().Be("Draw");
        GetArray(rejected, "reasons")
            .Select(reason => reason.GetString())
            .Should()
            .Contain(reason => reason != null && reason.Contains("Excluded tag", StringComparison.OrdinalIgnoreCase));
        GetArray(rejectedWincon, "reasons")
            .Select(reason => reason.GetString())
            .Should()
            .Contain(reason => reason != null && reason.Contains("Excluded role", StringComparison.OrdinalIgnoreCase));

        JsonElement planResult = await CallJsonAsync(
            session.Client,
            "deck_plan_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["name"] = "Add caller-selected draw card",
                ["rationale"] = "The caller selected Geth's Grimoire after inspecting query data.",
                ["addCards"] = new[] { ExplicitCardChange("Geth's Grimoire", 1, "Draw", "Caller-selected draw/discard engine.") }
            });
        JsonElement addOperation = GetArray(planResult, "operations")
            .Single(operation => GetString(operation, "operation") == "deck_add_card");

        GetString(addOperation, "cardName").Should().Be("Geth's Grimoire");
        GetString(addOperation, "category").Should().Be("Draw");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that MCP analysis tools return accurate numeric payloads for a known local deck.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalAnalysisFlow_ReturnsAccuratePayloadMetrics()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.PostJson(
            "cards/collection",
            $$"""
            {
              "data": [
                {{SwampJson}}
              ]
            }
            """);
        scryfall.GetJson("cards/named?fuzzy=Tinybones%2C%20Trinket%20Thief", TinybonesJson);
        scryfall.GetJson("cards/named?fuzzy=Swamp", SwampJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);
        scryfall.GetJson("cards/named?fuzzy=Phyrexian%20Arena", PhyrexianArenaJson);
        scryfall.GetJson(ScryfallSearchPath("is:game-changer"), EmptySearchJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Analysis Metrics",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        await AddCardAsync(session.Client, workspaceId, "Tinybones, Trinket Thief", 1, "Commander");
        await AddCardAsync(session.Client, workspaceId, "Swamp", 36, "Lands");
        await AddCardAsync(session.Client, workspaceId, "Arcane Signet", 1, "Ramp");
        await AddCardAsync(session.Client, workspaceId, "Phyrexian Arena", 1, "Draw");

        JsonElement workspaceList = await CallJsonAsync(
            session.Client,
            "workspace_list",
            new Dictionary<string, object?>());
        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "deck_analyze_structure",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement cost = await CallJsonAsync(
            session.Client,
            "deck_analyze_cost",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["maxBudget"] = 20m
            });
        JsonElement mana = await CallJsonAsync(
            session.Client,
            "deck_analyze_mana",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement consistency = await CallJsonAsync(
            session.Client,
            "deck_analyze_consistency",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement odds = await CallJsonAsync(
            session.Client,
            "deck_analyze_draw_odds",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["targets"] = "Lands,Ramp,Draw",
                ["turn"] = 3,
                ["openingHandSize"] = 7,
                ["simulations"] = 100,
                ["seed"] = 42
            });
        JsonElement bracket = await CallJsonAsync(
            session.Client,
            "deck_estimate_commander_bracket",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement goldfish = await CallJsonAsync(
            session.Client,
            "deck_simulate_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["targetTurn"] = 3,
                ["simulations"] = 50,
                ["seed"] = 42
            });
        JsonElement goldfishComparison = await CallJsonAsync(
            session.Client,
            "deck_compare_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceIds"] = new string[] { workspaceId },
                ["archidektDeckIdsOrUrls"] = new List<string> { "https://example.com/decks/not-archidekt" },
                ["targetTurn"] = 3,
                ["simulations"] = 50,
                ["seed"] = 42
            });
        JsonElement normalGoldfishComparison = await CallJsonAsync(
            session.Client,
            "deck_compare_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceIds"] = new string[] { workspaceId },
                ["archidektDeckIdsOrUrls"] = new List<string> { "https://example.com/decks/not-archidekt" },
                ["detailLevel"] = "normal",
                ["targetTurn"] = 3,
                ["simulations"] = 50,
                ["seed"] = 42
            });
        JsonElement fullGoldfishComparison = await CallJsonAsync(
            session.Client,
            "deck_compare_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceIds"] = new string[] { workspaceId },
                ["archidektDeckIdsOrUrls"] = new List<string> { "https://example.com/decks/not-archidekt" },
                ["detailLevel"] = "full",
                ["targetTurn"] = 3,
                ["simulations"] = 50,
                ["seed"] = 42
            });
        JsonElement fullBatchReport = await CallJsonAsync(
            session.Client,
            "deck_batch_tuning_report",
            new Dictionary<string, object?>
            {
                ["workspaceIds"] = new string[] { workspaceId },
                ["detailLevel"] = "full",
                ["targetTurn"] = 3,
                ["simulations"] = 50,
                ["seed"] = 42
            });
        JsonElement batchReport = await CallJsonAsync(
            session.Client,
            "deck_batch_tuning_report",
            new Dictionary<string, object?>
            {
                ["workspaceIds"] = new string[] { workspaceId, "missing-workspace" },
                ["targetTurn"] = 3,
                ["simulations"] = 50,
                ["seed"] = 42
            });

        JsonElement workspaceSummary = GetArray(workspaceList, "items").Should().ContainSingle().Subject;
        if (workspaceList.TryGetProperty("nextCursor", out JsonElement nextCursor))
        {
            nextCursor.ValueKind.Should().Be(JsonValueKind.Null);
        }

        GetInt32(workspaceList, "totalCount").Should().Be(1);
        workspaceSummary.TryGetProperty("cards", out _).Should().BeFalse();
        GetString(workspaceSummary, "workspaceId").Should().Be(workspaceId);
        GetInt32(workspaceSummary, "totalCards").Should().Be(39);
        GetInt32(workspaceSummary, "includedCards").Should().Be(39);

        GetInt32(analysis, "totalCards").Should().Be(39);
        GetInt32(analysis, "includedCards").Should().Be(39);
        GetObject(analysis, "typeCounts").GetProperty("Land").GetInt32().Should().Be(36);
        GetObject(analysis, "typeCounts").GetProperty("Artifact").GetInt32().Should().Be(1);
        GetObject(analysis, "typeCounts").GetProperty("Enchantment").GetInt32().Should().Be(1);
        GetObject(analysis, "typeCounts").GetProperty("Creature").GetInt32().Should().Be(1);

        GetProperty(cost, "includedTotal").GetDecimal().Should().Be(10.80m);
        GetProperty(cost, "maxBudget").GetDecimal().Should().Be(20m);
        GetString(cost, "budgetStatus").Should().Be("under-budget");
        GetProperty(cost, "withinBudget").GetBoolean().Should().BeTrue();
        GetInt32(cost, "pricedIncludedCards").Should().Be(4);
        GetProperty(cost, "topCostDrivers").EnumerateArray()
            .Select(driver => GetString(driver, "cardName"))
            .Should()
            .Equal(["Phyrexian Arena", "Tinybones, Trinket Thief", "Swamp", "Arcane Signet"]);

        GetInt32(mana, "landCount").Should().Be(36);
        GetInt32(mana, "alwaysTappedLandCount").Should().Be(0);
        GetInt32(mana, "conditionalTappedLandCount").Should().Be(0);
        GetInt32(mana, "untappedLandCount").Should().Be(36);
        GetInt32(mana, "fixingCount").Should().Be(1);
        GetInt32(mana, "rampFixingCount").Should().Be(1);
        GetObject(mana, "colorSources").GetProperty("B").GetInt32().Should().Be(36);
        GetObject(mana, "producedManaSources").GetProperty("B").GetInt32().Should().Be(37);

        GetInt32(consistency, "deckSize").Should().Be(39);
        GetInt32(consistency, "rampCount").Should().Be(1);
        GetInt32(consistency, "drawCount").Should().Be(1);
        GetInt32(consistency, "lowCurveNonlandCount").Should().Be(2);

        GetInt32(odds, "deckSize").Should().Be(39);
        GetInt32(odds, "cardsSeen").Should().Be(9);
        JsonElement landRow = GetOddsRow(odds, "Lands");
        JsonElement rampRow = GetOddsRow(odds, "Ramp");
        GetInt32(landRow, "successesInDeck").Should().Be(36);
        GetProperty(landRow, "hypergeometricAtLeastOne").GetDouble().Should().Be(1);
        GetInt32(rampRow, "successesInDeck").Should().Be(1);
        GetProperty(rampRow, "hypergeometricAtLeastOne").GetDouble().Should().BeApproximately(9.0 / 39.0, 0.000001);
        GetProperty(rampRow, "hypergeometricAtLeastTwo").GetDouble().Should().Be(0);
        GetInt32(GetOddsRow(odds, "Draw"), "successesInDeck").Should().Be(1);

        GetInt32(bracket, "estimatedBracket").Should().Be(1);
        GetInt32(bracket, "gameChangerCount").Should().Be(0);
        GetInt32(goldfish, "targetTurn").Should().Be(3);
        GetInt32(goldfish, "simulations").Should().Be(100);
        GetProperty(goldfish, "turnSummaries").GetArrayLength().Should().Be(3);
        JsonElement summaryBaseline = GetObject(goldfishComparison, "baselineDeck");
        GetString(goldfishComparison, "detailLevel").Should().Be("summary");
        summaryBaseline.GetProperty("workspaceId").GetString().Should().Be(workspaceId);
        summaryBaseline.TryGetProperty("goldfish", out _).Should().BeFalse();
        GetObject(summaryBaseline, "metrics").TryGetProperty("boardDevelopmentScore", out _).Should().BeTrue();
        JsonElement normalBaseline = GetObject(normalGoldfishComparison, "baselineDeck");
        GetString(normalGoldfishComparison, "detailLevel").Should().Be("normal");
        JsonElement normalDetails = GetObject(normalBaseline, "details");
        normalDetails.TryGetProperty("targetTurnBoard", out _).Should().BeTrue();
        normalDetails.TryGetProperty("representativeLines", out _).Should().BeTrue();
        JsonElement fullBaseline = GetObject(fullGoldfishComparison, "baselineDeck");
        JsonElement fullGoldfish = GetObject(fullBaseline, "goldfish");
        fullGoldfish.TryGetProperty("profileResolution", out _).Should().BeTrue();
        GetObject(fullGoldfish, "winEstimate").TryGetProperty("routeEvidence", out _).Should().BeTrue();
        fullGoldfish.TryGetProperty("representativeLines", out _).Should().BeTrue();
        fullGoldfish.TryGetProperty("turnSummaries", out _).Should().BeTrue();
        GetArray(goldfishComparison, "failures").Count().Should().Be(1);
        JsonElement fullBatchDeck = GetArray(fullBatchReport, "decks").Single();
        GetObject(fullBatchDeck, "goldfish").TryGetProperty("profileResolution", out _).Should().BeTrue();
        GetArray(batchReport, "decks").Count().Should().Be(1);
        GetArray(batchReport, "failures").Count().Should().Be(1);
        JsonElement activeCards = await CallJsonAsync(
            session.Client,
            "deck_list_cards_by_zone",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["zone"] = "active"
            });
        GetArray(activeCards, "cards")
            .Select(card => GetString(card, "cardName"))
            .Should()
            .Contain("Arcane Signet");
        JsonElement bulkMove = await CallJsonAsync(
            session.Client,
            "deck_move_cards_bulk",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["moves"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["cardName"] = "Arcane Signet",
                        ["fromCategory"] = "Ramp",
                        ["toCategory"] = "Maybeboard"
                    }
                },
                ["detailLevel"] = "normal"
            });
        JsonElement maybeboardCards = await CallJsonAsync(
            session.Client,
            "deck_list_cards_by_zone",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["zone"] = "maybeboard"
            });
        JsonElement lastImportDiff = await CallJsonAsync(
            session.Client,
            "workspace_diff_last_import",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement reEvaluation = await CallJsonAsync(
            session.Client,
            "deck_re_evaluate",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["limit"] = 3
            });

        GetInt32(bulkMove, "moved").Should().Be(1);
        GetArray(maybeboardCards, "cards")
            .Select(card => GetString(card, "cardName"))
            .Should()
            .Contain("Arcane Signet");
        GetString(lastImportDiff, "status").Should().Be("workspaceHasNoSource");
        GetString(reEvaluation, "detailLevel").Should().Be("summary");
        GetString(reEvaluation.GetProperty("sourceRecommendations"), "status").Should().Be("notQueried");
        GetArray(reEvaluation, "topRisks").Count().Should().BeLessThanOrEqualTo(3);
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that deck intent tools preserve rich Quill descriptions through MCP stdio.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalIntentFlow_PreservesQuillDescriptionContent()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Intent",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");
        JsonElement compactSetResult = await CallJsonAsync(
            session.Client,
            "deck_intent_set",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["intentText"] = "Commander: Kenessos, Priest of Thassa"
            });
        compactSetResult.TryGetProperty("workspace", out _).Should().BeFalse();
        GetString(compactSetResult, "workspaceId").Should().Be(workspaceId);

        string richDescription = """
        {"ops":[{"insert":"Primer","attributes":{"bold":true}},{"insert":" before\n"},{"insert":{"image":"https://example.test/card.jpg"}},{"insert":"\nPrimer after\n","attributes":{"italic":true}}]}
        """;

        await CallJsonAsync(
            session.Client,
            "deck_update_metadata",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["description"] = richDescription
            });
        JsonElement setResult = await CallJsonAsync(
            session.Client,
            "deck_intent_set",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["intentText"] = "Archetype: discard-control",
                ["includeWorkspace"] = true
            });
        string setDescription = GetString(GetObject(setResult, "workspace"), "description");

        setDescription.Should().Contain("\"bold\":true");
        setDescription.Should().Contain("\"italic\":true");
        setDescription.Should().Contain("\"image\":\"https://example.test/card.jpg\"");
        setDescription.Should().Contain("MTG MCP Deck Intent");
        GetString(setResult, "persistence").Should().Be("local-only");

        JsonElement clearResult = await CallJsonAsync(
            session.Client,
            "deck_intent_clear",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["includeWorkspace"] = true
            });
        string clearedDescription = GetString(GetObject(clearResult, "workspace"), "description");

        clearedDescription.Should().Contain("\"bold\":true");
        clearedDescription.Should().Contain("\"italic\":true");
        clearedDescription.Should().Contain("\"image\":\"https://example.test/card.jpg\"");
        clearedDescription.Should().NotContain("MTG MCP Deck Intent");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that local MCP deck mutations compose across cards, quantities, categories, and exports.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task LocalMutationFlow_ExercisesCardQuantityAndCategoryChanges()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Lightning%20Bolt", LightningBoltJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Mutations",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        await CallJsonAsync(
            session.Client,
            "deck_create_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["category"] = "Removal",
                ["includedInDeck"] = true,
                ["includedInPrice"] = true
            });
        await CallJsonAsync(
            session.Client,
            "deck_add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 2,
                ["category"] = "Removal"
            });
        await CallJsonAsync(
            session.Client,
            "deck_set_card_quantity",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 3,
                ["category"] = "Removal"
            });
        await CallJsonAsync(
            session.Client,
            "deck_add_card_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["category"] = "Tempo"
            });
        await CallJsonAsync(
            session.Client,
            "deck_set_primary_card_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["category"] = "Tempo"
            });
        await CallJsonAsync(
            session.Client,
            "deck_remove_card_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["category"] = "Removal"
            });
        await CallJsonAsync(
            session.Client,
            "deck_rename_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["oldName"] = "Tempo",
                ["newName"] = "Interaction"
            });
        await CallJsonAsync(
            session.Client,
            "deck_add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Arcane Signet",
                ["quantity"] = 1,
                ["category"] = "Ramp"
            });
        await CallJsonAsync(
            session.Client,
            "deck_move_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Arcane Signet",
                ["fromCategory"] = "Ramp",
                ["toCategory"] = "Artifacts"
            });
        await CallJsonAsync(
            session.Client,
            "deck_delete_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["category"] = "Artifacts",
                ["replacementCategory"] = "Mainboard"
            });
        await CallJsonAsync(
            session.Client,
            "deck_remove_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 1,
                ["category"] = "Interaction"
            });
        await CallJsonAsync(
            session.Client,
            "deck_set_card_quantity",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Arcane Signet",
                ["quantity"] = 0,
                ["category"] = "Mainboard"
            });

        string export = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement resource = await CallJsonAsync(
            session.Client,
            "workspace_open",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "deck_analyze_structure",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        export.Should().Contain("Interaction");
        export.Should().Contain("2 Lightning Bolt");
        export.Should().NotContain("Arcane Signet");
        GetObject(analysis, "typeCounts").GetProperty("Instant").GetInt32().Should().Be(2);
        JsonElement card = GetProperty(resource, "cards").EnumerateArray().Should().ContainSingle().Subject;
        GetString(card, "name").Should().Be("Lightning Bolt");
        GetString(card, "primaryCategory").Should().Be("Interaction");
        GetInt32(card, "quantity").Should().Be(2);
        card.GetProperty("categories").EnumerateArray().Select(value => value.GetString())
            .Should()
            .BeEquivalentTo(["Interaction"]);
        resource.GetProperty("categories").EnumerateArray()
            .Select(value => GetString(value, "name"))
            .Should()
            .Contain(["Mainboard", "Removal", "Interaction", "Ramp"]);
        resource.GetProperty("categories").EnumerateArray()
            .Select(value => GetString(value, "name"))
            .Should()
            .NotContain("Artifacts");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that bulk card and category tools work through the MCP stdio surface.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task BulkCategoryFlow_ExercisesBulkToolsThroughMcp()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.PostJson("cards/collection", BulkCategoryCollectionJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Bulk Categories",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        await CallJsonAsync(
            session.Client,
            "deck_add_cards_bulk",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cards"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["cardName"] = "Lightning Bolt",
                        ["quantity"] = 1,
                        ["primaryCategory"] = "Sideboard",
                        ["secondaryCategories"] = RemovalSecondaryCategories
                    },
                    new Dictionary<string, object?>
                    {
                        ["cardName"] = "Arcane Signet",
                        ["quantity"] = 1,
                        ["primaryCategory"] = "Mainboard",
                        ["secondaryCategories"] = RampSecondaryCategories
                    }
                },
                ["detailLevel"] = "summary"
            });

        JsonElement firstSideboard = await CallJsonAsync(
            session.Client,
            "deck_list_cards_by_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["category"] = "Sideboard",
                ["includeSecondary"] = true
            });
        GetInt32(firstSideboard, "count").Should().Be(1);
        JsonElement firstSideboardCard = GetArray(firstSideboard, "cards").Should().ContainSingle().Subject;
        GetString(firstSideboardCard, "cardName").Should().Be("Lightning Bolt");
        firstSideboardCard.GetProperty("includedInDeck").GetBoolean().Should().BeFalse();
        firstSideboardCard.GetProperty("includedInPrice").GetBoolean().Should().BeFalse();

        await CallJsonAsync(
            session.Client,
            "deck_update_card_categories_bulk",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["changes"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["cardName"] = "Arcane Signet",
                        ["action"] = "add-secondary",
                        ["category"] = "Sideboard"
                    },
                    new Dictionary<string, object?>
                    {
                        ["cardName"] = "Lightning Bolt",
                        ["action"] = "set-primary",
                        ["category"] = "Mainboard"
                    }
                },
                ["detailLevel"] = "summary"
            });

        JsonElement secondSideboard = await CallJsonAsync(
            session.Client,
            "deck_list_cards_by_category",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["category"] = "Sideboard",
                ["includeSecondary"] = true
            });
        GetInt32(secondSideboard, "count").Should().Be(2);
        secondSideboard.GetProperty("cards").EnumerateArray()
            .Select(card => GetString(card, "cardName"))
            .Should()
            .BeEquivalentTo(["Arcane Signet", "Lightning Bolt"]);

        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that explicit local plans preview and apply through MCP.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ExplicitPlanFlow_PreviewsAndAppliesLocalPlan()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Mana%20Crypt", ManaCryptJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);
        scryfall.PostJson("cards/collection", ArcaneSignetCollectionJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Corpus Budget",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        await CallJsonAsync(
            session.Client,
            "deck_add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Mana Crypt",
                ["quantity"] = 1,
                ["category"] = "Ramp"
            });
        JsonElement replacement = await CallJsonAsync(
            session.Client,
            "deck_plan_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["name"] = "Add cheaper ramp consideration",
                ["rationale"] = "The caller selected Arcane Signet to consider alongside Mana Crypt.",
                ["addCards"] = new[] { ExplicitCardChange("Arcane Signet", 1, "Ramp", "Caller-selected ramp add.") },
                ["moveCards"] = new[] { ExplicitMoveChange("Mana Crypt", "Ramp", "Mainboard", "Caller-selected category move.") }
            });
        string planId = GetString(replacement, "planId");

        JsonElement transientPackage = await CallJsonAsync(
            session.Client,
            "deck_preview_card_package",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["name"] = "Transient ramp package",
                ["addCards"] = new[] { ExplicitCardChange("Arcane Signet", 1, "Ramp", "Caller-selected ramp add.") },
                ["moveCards"] = new[] { ExplicitMoveChange("Mana Crypt", "Ramp", "Mainboard", "Caller-selected category move.") },
                ["resolveAddedCards"] = true,
                ["sourceSupportDepth"] = "balanced",
                ["simulationProfile"] = "neutral",
                ["simulations"] = 100,
                ["maxTurn"] = 3,
                ["seed"] = 44
            });
        JsonElement preview = await CallJsonAsync(
            session.Client,
            "deck_plan_preview",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["detailLevel"] = "normal",
                ["resolveAddedCards"] = true
            });
        string beforeApplyExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement apply = await CallJsonAsync(
            session.Client,
            "deck_plan_apply",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["createCheckpoint"] = false,
                ["includeWorkspace"] = false,
                ["detailLevel"] = "normal"
            });
        string afterApplyExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        JsonElement beforeSnapshot = GetObject(preview, "before");
        JsonElement afterSnapshot = GetObject(preview, "after");
        GetInt32(GetObject(beforeSnapshot, "analysis"), "includedCards")
            .Should()
            .Be(1);
        GetInt32(GetObject(afterSnapshot, "analysis"), "includedCards")
            .Should()
            .Be(2);
        GetProperty(GetObject(afterSnapshot, "cost"), "includedTotal").GetDecimal()
            .Should()
            .BeGreaterThan(GetProperty(GetObject(beforeSnapshot, "cost"), "includedTotal").GetDecimal());
        GetProperty(transientPackage, "previewOnly").GetBoolean().Should().BeTrue();
        GetProperty(transientPackage, "canApply").GetBoolean().Should().BeFalse();
        GetProperty(transientPackage, "applyPlanId").ValueKind.Should().Be(JsonValueKind.Null);
        GetString(transientPackage, "sourceSupportDepth").Should().Be("balanced");
        GetArray(transientPackage, "sourceSupport")
            .Select(row => GetString(row, "status"))
            .Should()
            .Contain("source-backed-metadata");
        GetProperty(transientPackage, "performanceSkipped").GetBoolean().Should().BeTrue();
        GetString(transientPackage, "performanceSkipReason").Should().Contain("partial Commander decks");
        GetArray(GetObject(transientPackage, "performance"), "deltas")
            .Should()
            .BeEmpty();
        beforeApplyExport.Should().Contain("1 Mana Crypt");
        beforeApplyExport.Should().NotContain("Arcane Signet");
        GetInt32(apply, "added").Should().Be(1);
        GetInt32(apply, "moved").Should().Be(1);
        GetString(apply, "workspaceResourceUri").Should().StartWith("mtg://workspace/");
        afterApplyExport.Should().Contain("1 Arcane Signet");
        afterApplyExport.Should().Contain("1 Mana Crypt");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that deck-tuning workflow primitives compose around a small Inga and Esika fixture.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task DeckTuningWorkflowFlow_ExercisesEvidenceDiffAndPackageTools()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        RegisterIngaWorkflowCards(scryfall);
        scryfall.PostJson("cards/collection", IngaWorkflowCollectionJson);
        archidekt.GetJson("api/decks/23097041/", IngaArchidektDeckJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        IList<McpClientPrompt> prompts = await session.Client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        prompts.Select(prompt => prompt.Name).Should().Contain("iterative_deck_review");

        string baselineWorkspaceId = await CreateIngaWorkflowWorkspaceAsync(session.Client, "E2E Inga Baseline");
        string currentWorkspaceId = await CreateIngaWorkflowWorkspaceAsync(session.Client, "E2E Inga Current");
        JsonElement remoteWorkspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "https://archidekt.com/decks/23097041/inga_and_esika",
                ["writeBack"] = false
            });
        string remoteWorkspaceId = GetString(remoteWorkspace, "id");

        string stateText = await ReadResourceTextAsync(session.Client, $"mtg://workspace/{baselineWorkspaceId}/state");
        JsonElement state = JsonElement.Parse(stateText);
        string contextText = await ReadResourceTextAsync(
            session.Client,
            $"mtg://workspace/{baselineWorkspaceId}/assistant-context");
        JsonElement context = JsonElement.Parse(contextText);
        string simulationGuidanceText = await ReadResourceTextAsync(
            session.Client,
            "mtg://usage/simulation-tool-selection");
        string textExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["format"] = "text"
            });
        string markdownExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["format"] = "markdown"
            });
        string linkedExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["format"] = "markdown-links"
            });
        JsonElement rampExplanation = await CallJsonAsync(
            session.Client,
            "deck_explain_role_counts",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["role"] = "Ramp"
            });
        JsonElement drawExplanation = await CallJsonAsync(
            session.Client,
            "deck_explain_role_counts",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["role"] = "Draw"
            });
        JsonElement interactionExplanation = await CallJsonAsync(
            session.Client,
            "deck_explain_role_counts",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["role"] = "Interaction"
            });
        JsonElement winconExplanation = await CallJsonAsync(
            session.Client,
            "deck_explain_role_counts",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["role"] = "Wincons"
            });
        JsonElement weakSpots = await CallJsonAsync(
            session.Client,
            "deck_review_weak_spots",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["limit"] = 5
            });
        JsonElement goldfish = await CallJsonAsync(
            session.Client,
            "deck_simulate_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["targetTurn"] = 4,
                ["simulations"] = 100,
                ["seed"] = 2026
            });
        JsonElement performance = await CallJsonAsync(
            session.Client,
            "deck_analyze_performance",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = baselineWorkspaceId,
                ["maxTurn"] = 4,
                ["simulations"] = 100,
                ["seed"] = 2026
            });

        JsonElement package = await CallJsonAsync(
            session.Client,
            "deck_preview_card_package",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = currentWorkspaceId,
                ["name"] = "Move overrun to maybe",
                ["moveCards"] = new[] { ExplicitMoveChange("Overrun", "Wincons", "Maybeboard", "Caller moved a finisher out.") },
                ["simulations"] = 100,
                ["maxTurn"] = 4,
                ["seed"] = 2026
            });
        JsonElement plan = await CallJsonAsync(
            session.Client,
            "deck_plan_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = currentWorkspaceId,
                ["name"] = "Move overrun to maybe",
                ["moveCards"] = new[] { ExplicitMoveChange("Overrun", "Wincons", "Maybeboard", "Caller moved a finisher out.") }
            });
        string planId = GetString(plan, "planId");
        JsonElement preview = await CallJsonAsync(
            session.Client,
            "deck_plan_preview",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["detailLevel"] = "normal"
            });
        JsonElement apply = await CallJsonAsync(
            session.Client,
            "deck_plan_apply",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["createCheckpoint"] = false,
                ["includeWorkspace"] = false,
                ["detailLevel"] = "normal"
            });
        JsonElement diff = await CallJsonAsync(
            session.Client,
            "workspace_diff",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = currentWorkspaceId,
                ["previousWorkspaceId"] = baselineWorkspaceId
            });
        JsonElement analysisDiff = await CallJsonAsync(
            session.Client,
            "deck_compare_workspaces_analysis",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = currentWorkspaceId,
                ["baselineMode"] = "explicit",
                ["baselineWorkspaceId"] = baselineWorkspaceId,
                ["limit"] = 4
            });

        GetString(state, "workspaceId").Should().Be(baselineWorkspaceId);
        GetArray(state, "commanders").Select(value => value.GetString()).Should().Contain("Inga and Esika");
        GetString(GetObject(context, "state"), "workspaceId").Should().Be(baselineWorkspaceId);
        simulationGuidanceText.Should().Contain("deck_analyze_performance");
        simulationGuidanceText.Should().Contain("archidekt_compare_goldfish");
        GetString(remoteWorkspace, "mode").Should().Be("Archidekt");
        GetProperty(remoteWorkspace, "writeBack").GetBoolean().Should().BeFalse();
        GetString(remoteWorkspace, "archidektDeckId").Should().Be("23097041");
        remoteWorkspaceId.Should().NotBeEmpty();
        textExport.Should().Contain("1 Inga and Esika");
        markdownExport.Should().Contain("## Commander");
        linkedExport.Should().Contain("[Inga and Esika](");
        GetInt32(rampExplanation, "categoryCount").Should().BeGreaterThan(0);
        GetString(drawExplanation, "role").Should().Be("Draw");
        GetArray(drawExplanation, "cards").Should().NotBeEmpty();
        GetArray(interactionExplanation, "cards").Should().NotBeEmpty();
        GetArray(winconExplanation, "cards").Should().NotBeEmpty();
        GetArray(weakSpots, "sourceStatuses")
            .Select(row => GetString(row, "sourceKey"))
            .Should()
            .Contain("workspace");
        GetString(goldfish, "modelLabel").Should().Be("optimistic-goldfish-model");
        GetString(goldfish, "rngKind").Should().Be("system-random");
        GetArray(goldfish, "notes")
            .Select(note => note.GetString())
            .Should()
            .Contain(note => note != null && note.Contains("Inga and Esika", StringComparison.OrdinalIgnoreCase));
        GetString(performance, "modelLabel").Should().Be("strict-sequencing-model");
        AssertPerformanceTrustContract(performance, expectedSimulations: 100);
        GetArray(performance, "assumptions")
            .Select(note => note.GetString())
            .Should()
            .Contain(note => note != null && note.Contains("Inga and Esika", StringComparison.OrdinalIgnoreCase));
        GetArray(package, "roleDeltas").Should().NotBeEmpty();
        GetInt32(GetObject(GetObject(preview, "before"), "analysis"), "includedCards")
            .Should()
            .BeGreaterThan(GetInt32(GetObject(GetObject(preview, "after"), "analysis"), "includedCards"));
        GetInt32(apply, "moved").Should().Be(1);
        GetString(apply, "workspaceResourceUri").Should().StartWith("mtg://workspace/");
        GetInt32(diff, "includedCountDelta").Should().Be(-1);
        GetString(analysisDiff, "status").Should().Be("compared");
        GetInt32(GetObject(analysisDiff, "deltas"), "includedCountDelta").Should().Be(-1);
        GetString(GetObject(analysisDiff, "performance"), "status").Should().Be("notRequested");
        GetArray(diff, "primaryMoves")
            .Select(row => GetString(row, "cardName"))
            .Should()
            .Contain("Overrun");
        archidekt.Requests.Should().ContainSingle(request => request.Method == "GET" && request.PathAndQuery == "api/decks/23097041/");
        archidekt.Requests.Should().NotContain(request => request.Method == "PATCH");
    }

    /// <summary>
    /// Verifies that source refresh bypasses cached source facts through MCP.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task SourceRefreshFlow_UsesCacheAndBypassesSourceFacts()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        string searchPath = ScryfallSearchPath("legal:commander o:discard usd<=5");
        scryfall.GetJson(searchPath, HiddenGemSearchJson);
        scryfall.PostJson("cards/collection", HiddenGemCollectionJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Source Refresh",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        Dictionary<string, object?> args = new()
        {
            ["workspaceId"] = workspaceId,
            ["goal"] = "discard",
            ["maxPrice"] = 5,
            ["analysisDepth"] = "balanced"
        };
        JsonElement first = await CallJsonAsync(session.Client, "deck_find_lesser_known_cards", args);
        JsonElement second = await CallJsonAsync(session.Client, "deck_find_lesser_known_cards", args);
        args["bypassCache"] = true;
        JsonElement refreshed = await CallJsonAsync(session.Client, "deck_find_lesser_known_cards", args);

        GetProperty(first, "recommendations").GetArrayLength().Should().Be(1);
        GetProperty(second, "notes").EnumerateArray()
            .Select(note => note.GetString())
            .Should()
            .Contain(note => note != null && note.Contains("cache", StringComparison.OrdinalIgnoreCase));
        GetProperty(refreshed, "recommendations").GetArrayLength().Should().Be(1);
        scryfall.Requests.Count(request =>
                request.Method == "GET"
                && DecodeRepeatedly(request.PathAndQuery).Equals(
                    DecodeRepeatedly(searchPath),
                    StringComparison.OrdinalIgnoreCase))
            .Should()
            .Be(2);
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the one-stop deckbuilding tools work through the MCP stdio surface.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task OneStopDeckbuildingFlow_ExercisesNewToolsThroughMcp()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using FakeHttpServer spellbook = new();
        DateOnly defaultSince = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddYears(-1);
        scryfall.GetJson(
            ScryfallSearchPath("(o:goad or o:monarch or o:vote or o:\"tempting offer\" or o:\"each opponent\") legal:commander usd<=5"),
            TableEdictSearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("(o:\"each player\" or o:\"opponents choose\" or o:\"each creature\") legal:commander usd<=5"),
            EmptySearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("(o:\"each opponent\" or o:\"each player\" or o:\"each creature\") legal:commander usd<=5"),
            TableEdictSearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("(o:\"destroy all\" or o:\"exile all\") legal:commander usd<=5"),
            EmptySearchJson);
        scryfall.GetJson(
            ScryfallSearchPath("legal:commander -t:basic"),
            TableEdictSearchJson);
        scryfall.GetJson(
            ScryfallSearchPath($"legal:commander date>={defaultSince:yyyy-MM-dd} usd<=5"),
            TableEdictSearchJson);
        scryfall.PostJson("cards/collection", TableEdictCollectionJson);
        spellbook.PostJson("find-my-combos", EmptySpellbookJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken,
            commanderSpellbookBaseAddress: spellbook.BaseAddress);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E One Stop",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        JsonElement goal = await CallJsonAsync(
            session.Client,
            "deck_query_cards",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["goal"] = "add a few cards that interact with the whole table",
                ["scryfallQuery"] = "o:goad or o:monarch or o:vote or o:\"tempting offer\" or o:\"each opponent\"",
                ["limit"] = 1,
                ["maxPrice"] = 5
            });
        JsonElement best = await CallJsonAsync(
            session.Client,
            "deck_analyze_best_practices",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement goldfish = await CallJsonAsync(
            session.Client,
            "deck_simulate_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["targetTurn"] = 3,
                ["simulations"] = 100
            });
        JsonElement combos = await CallJsonAsync(
            session.Client,
            "deck_analyze_combos",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement newCards = await CallJsonAsync(
            session.Client,
            "deck_review_new_card_swaps",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["maxPrice"] = 5,
                ["limit"] = 1
            });

        GetProperty(goal, "cards").GetArrayLength().Should().BeGreaterThan(0);
        GetString(best, "recommendedProfile").Should().Be("commander-baseline");
        GetInt32(goldfish, "targetTurn").Should().Be(3);
        GetObject(combos, "pressure").ValueKind.Should().Be(JsonValueKind.Object);
        GetProperty(newCards, "candidates").GetArrayLength().Should().BeGreaterThan(0);
        scryfall.Requests.Should().Contain(request => request.PathAndQuery.Contains("date%3E%3D", StringComparison.OrdinalIgnoreCase)
            || DecodeRepeatedly(request.PathAndQuery).Contains("date>=", StringComparison.OrdinalIgnoreCase));
        spellbook.Requests.Should().ContainSingle(request => request.Method == "POST" && request.PathAndQuery == "find-my-combos");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that an Archidekt workspace writes card additions back to the fake deck API.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ArchidektWritebackFlow_PatchesFakeArchidektDeck()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson("cards/named?fuzzy=Lightning%20Bolt", LightningBoltJson);
        archidekt.GetJson("api/decks/123/", """
        {
          "id": 123,
          "name": "Remote E2E",
          "deckFormat": "commander",
          "categories": [
            { "id": 1, "name": "Mainboard", "includedInDeck": true, "includedInPrice": true }
          ],
          "cards": []
        }
        """);
        archidekt.GetJson("api/cards/v2/?name=Lightning%20Bolt&pageSize=25", """
        {
          "results": [
            { "id": 151147, "oracleCard": { "name": "Lightning Bolt" } }
          ]
        }
        """);
        archidekt.PatchJson("api/decks/123/modifyCards/v2/", "{}");

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            });
        string workspaceId = GetString(workspace, "id");
        JsonElement change = await CallJsonAsync(
            session.Client,
            "deck_add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 1,
                ["category"] = "Mainboard",
                ["detailLevel"] = "normal"
            });

        GetString(change, "persistence").Should().Be("archidekt-writeback");
        FakeHttpRequest patch = archidekt.Requests.Single(request => request.Method == "PATCH");
        patch.PathAndQuery.Should().Be("api/decks/123/modifyCards/v2/");
        using JsonDocument payload = JsonDocument.Parse(patch.Body);
        JsonElement card = payload.RootElement.GetProperty("cards")[0];
        card.GetProperty("action").GetString().Should().Be("add");
        card.GetProperty("cardid").GetInt32().Should().Be(151147);
        card.GetProperty("categories")[0].GetString().Should().Be("Mainboard");
        card.GetProperty("modifications").GetProperty("quantity").GetInt32().Should().Be(1);
        card.TryGetProperty("deckRelationId", out _).Should().BeFalse();
        card.GetProperty("modifications").TryGetProperty("modifier", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that workflow primitives preview locally and only write to Archidekt when applied.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ArchidektWorkflowPrimitiveFlow_PreviewsBeforeApplying()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        scryfall.GetJson(
            ScryfallSearchPath("is:game-changer"),
            """
            {
              "has_more": false,
              "data": []
            }
            """);
        scryfall.PostJson(
            "cards/collection",
            """
            {
              "data": [
                {
                  "id": "arcane-signet",
                  "oracle_id": "oracle-arcane-signet",
                  "name": "Arcane Signet",
                  "mana_cost": "{2}",
                  "cmc": 2,
                  "type_line": "Artifact",
                  "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
                  "produced_mana": ["W", "U", "B", "R", "G"],
                  "legalities": { "commander": "legal" },
                  "prices": { "usd": "1.00" },
                  "edhrec_rank": 5
                }
              ]
            }
            """);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);
        archidekt.GetJson("api/decks/123/", RemoteSwampsDeckJson);
        archidekt.GetJson(
            "api/cards/v2/?name=Arcane%20Signet&pageSize=25",
            """
            {
              "results": [
                { "id": 555, "oracleCard": { "name": "Arcane Signet" } }
              ]
            }
            """);
        archidekt.PatchJson("api/decks/123/modifyCards/v2/", "{}");

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            });
        string workspaceId = GetString(workspace, "id");
        string beforePreviewExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        JsonElement planResult = await CallJsonAsync(
            session.Client,
            "deck_plan_create",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["name"] = "Add one ramp card",
                ["rationale"] = "The caller selected Arcane Signet from deterministic card data.",
                ["addCards"] = new[] { ExplicitCardChange("Arcane Signet", 1, "Ramp", "Caller-selected ramp add.") }
            });
        string planId = GetString(planResult, "planId");
        JsonElement preview = await CallJsonAsync(
            session.Client,
            "deck_plan_preview",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["detailLevel"] = "normal",
                ["resolveAddedCards"] = true
            });
        string afterPreviewExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        GetInt32(GetObject(GetObject(preview, "before"), "consistency"), "rampCount").Should().Be(0);
        GetInt32(GetObject(GetObject(preview, "after"), "consistency"), "rampCount").Should().Be(1);
        beforePreviewExport.Should().NotContain("Arcane Signet");
        afterPreviewExport.Should().Be(beforePreviewExport);
        archidekt.Requests.Should().NotContain(request => request.Method == "PATCH");

        JsonElement apply = await CallJsonAsync(
            session.Client,
            "deck_plan_apply",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["createCheckpoint"] = false,
                ["detailLevel"] = "full"
            });
        string afterApplyExport = await CallTextAsync(
            session.Client,
            "workspace_export",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        GetInt32(apply, "appliedOperations").Should().Be(1);
        afterApplyExport.Should().Contain("1 Arcane Signet");
        FakeHttpRequest patch = archidekt.Requests.Single(request => request.Method == "PATCH");
        patch.PathAndQuery.Should().Be("api/decks/123/modifyCards/v2/");
        using JsonDocument payload = JsonDocument.Parse(patch.Body);
        JsonElement card = payload.RootElement.GetProperty("cards")[0];
        card.GetProperty("action").GetString().Should().Be("add");
        card.GetProperty("cardid").GetInt32().Should().Be(555);
        card.GetProperty("categories")[0].GetString().Should().Be("Ramp");
        card.TryGetProperty("deckRelationId", out _).Should().BeFalse();
        card.GetProperty("modifications").TryGetProperty("modifier", out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that read-only mode does not advertise Archidekt workspace mutation.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ReadOnlyMode_HidesArchidektWorkspaceMutationTools()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "read-only",
            TestContext.Current.CancellationToken);

        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        string[] names = tools.Select(tool => tool.Name).ToArray();

        names.Should().Contain("workspace_list");
        names.Should().NotContain("workspace_start");
        archidekt.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Calls a tool and parses the single text result as JSON.
    /// </summary>
    private static async Task<JsonElement> CallJsonAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        string text = await CallTextAsync(client, toolName, arguments);
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Calls a tool and returns the single text result after asserting success.
    /// </summary>
    private static async Task<string> CallTextAsync(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult result = await client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().NotBeTrue(ReadText(result));
        return ReadText(result);
    }

    /// <summary>
    /// Adds a card to a workspace through the public MCP tool surface.
    /// </summary>
    private static async Task AddCardAsync(
        McpClient client,
        string workspaceId,
        string cardName,
        int quantity,
        string category)
    {
        await CallJsonAsync(
            client,
            "deck_add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = cardName,
                ["quantity"] = quantity,
                ["category"] = category
            });
    }

    /// <summary>
    /// Builds a small local Inga and Esika workspace through the public MCP surface.
    /// </summary>
    private static async Task<string> CreateIngaWorkflowWorkspaceAsync(McpClient client, string name)
    {
        JsonElement workspace = await CallJsonAsync(
            client,
            "workspace_start",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = name,
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");
        await AddCardAsync(client, workspaceId, "Inga and Esika", 1, "Commander");
        await AddCardAsync(client, workspaceId, "Forest", 10, "Lands");
        await AddCardAsync(client, workspaceId, "Llanowar Elves", 1, "Ramp");
        await AddCardAsync(client, workspaceId, "Elvish Mystic", 1, "Ramp");
        await AddCardAsync(client, workspaceId, "Beast Within", 1, "Interaction");
        await AddCardAsync(client, workspaceId, "Reclamation Sage", 1, "Interaction");
        await AddCardAsync(client, workspaceId, "Overrun", 1, "Wincons");
        return workspaceId;
    }

    /// <summary>
    /// Registers fake Scryfall named-card routes for the Inga workflow fixture.
    /// </summary>
    private static void RegisterIngaWorkflowCards(FakeHttpServer scryfall)
    {
        scryfall.GetJson("cards/named?fuzzy=Inga%20and%20Esika", IngaAndEsikaJson);
        scryfall.GetJson("cards/named?fuzzy=Forest", ForestJson);
        scryfall.GetJson("cards/named?fuzzy=Llanowar%20Elves", LlanowarElvesJson);
        scryfall.GetJson("cards/named?fuzzy=Elvish%20Mystic", ElvishMysticJson);
        scryfall.GetJson("cards/named?fuzzy=Beast%20Within", BeastWithinJson);
        scryfall.GetJson("cards/named?fuzzy=Reclamation%20Sage", ReclamationSageJson);
        scryfall.GetJson("cards/named?fuzzy=Overrun", OverrunJson);
    }

    /// <summary>
    /// Reads one MCP resource text payload.
    /// </summary>
    private static async Task<string> ReadResourceTextAsync(McpClient client, string uri)
    {
        ReadResourceResult result = await client.ReadResourceAsync(
            uri,
            cancellationToken: TestContext.Current.CancellationToken);
        object block = result.Contents.Single();
        return block.GetType().GetProperty("Text")?.GetValue(block) as string
            ?? throw new InvalidOperationException($"Resource {uri} did not return text content.");
    }

    /// <summary>
    /// Builds an explicit card change payload for plan-creation tool calls.
    /// </summary>
    private static Dictionary<string, object?> ExplicitCardChange(
        string cardName,
        int quantity,
        string category,
        string rationale)
    {
        return new Dictionary<string, object?>
        {
            ["cardName"] = cardName,
            ["quantity"] = quantity,
            ["category"] = category,
            ["rationale"] = rationale
        };
    }

    /// <summary>
    /// Builds an explicit card move payload for plan-creation tool calls.
    /// </summary>
    private static Dictionary<string, object?> ExplicitMoveChange(
        string cardName,
        string fromCategory,
        string toCategory,
        string rationale)
    {
        return new Dictionary<string, object?>
        {
            ["cardName"] = cardName,
            ["fromCategory"] = fromCategory,
            ["toCategory"] = toCategory,
            ["rationale"] = rationale
        };
    }

    /// <summary>
    /// Reads the single text content block returned by a tool call.
    /// </summary>
    private static string ReadText(CallToolResult result)
    {
        TextContentBlock block = result.Content.OfType<TextContentBlock>().Single();
        return block.Text;
    }

    /// <summary>
    /// Reads a JSON string property using camelCase or PascalCase naming.
    /// </summary>
    private static string GetString(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName).GetString() ?? "";
    }

    /// <summary>
    /// Reads a JSON integer property using camelCase or PascalCase naming.
    /// </summary>
    private static int GetInt32(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName).GetInt32();
    }

    /// <summary>
    /// Reads a JSON object property using camelCase or PascalCase naming.
    /// </summary>
    private static JsonElement GetObject(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName);
    }

    /// <summary>
    /// Reads a JSON array property using camelCase or PascalCase naming.
    /// </summary>
    private static JsonElement.ArrayEnumerator GetArray(JsonElement element, string propertyName)
    {
        return GetProperty(element, propertyName).EnumerateArray();
    }

    /// <summary>
    /// Finds a named object inside a JSON array.
    /// </summary>
    private static JsonElement FindNamed(
        JsonElement.ArrayEnumerator values,
        string name,
        string metricProperty = "name")
    {
        foreach (JsonElement value in values)
        {
            if (GetString(value, metricProperty).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new InvalidOperationException($"Array item named '{name}' was not found.");
    }

    /// <summary>
    /// Finds a named turn metric inside a JSON array.
    /// </summary>
    private static JsonElement FindNamedTurn(
        JsonElement.ArrayEnumerator values,
        string name,
        int turn)
    {
        foreach (JsonElement value in values)
        {
            if (GetString(value, "name").Equals(name, StringComparison.OrdinalIgnoreCase)
                && GetInt32(value, "turn") == turn)
            {
                return value;
            }
        }

        throw new InvalidOperationException($"Array item named '{name}' for turn {turn} was not found.");
    }

    /// <summary>
    /// Reads one odds row by target name.
    /// </summary>
    private static JsonElement GetOddsRow(JsonElement odds, string target)
    {
        return GetProperty(odds, "rows")
            .EnumerateArray()
            .Single(row => string.Equals(GetString(row, "target"), target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies the MCP JSON shape for Stats Lab trust-contract fields.
    /// </summary>
    private static void AssertPerformanceTrustContract(JsonElement analysis, int expectedSimulations)
    {
        analysis.TryGetProperty("schemaVersion", out JsonElement schemaVersion).Should().BeTrue();
        schemaVersion.GetInt32().Should().Be(2);
        analysis.TryGetProperty("modelVersion", out JsonElement modelVersion).Should().BeTrue();
        modelVersion.GetString().Should().Be("stats-lab-1");
        analysis.TryGetProperty("deckFingerprint", out JsonElement deckFingerprint).Should().BeTrue();
        (deckFingerprint.GetString() ?? "").Should().HaveLength(64);
        analysis.TryGetProperty("cardDataFingerprint", out JsonElement cardDataFingerprint).Should().BeTrue();
        (cardDataFingerprint.GetString() ?? "").Should().HaveLength(64);
        analysis.TryGetProperty("profileFingerprint", out JsonElement profileFingerprint).Should().BeTrue();
        (profileFingerprint.GetString() ?? "").Should().HaveLength(64);
        analysis.TryGetProperty("rngKind", out JsonElement rngKind).Should().BeTrue();
        rngKind.GetString().Should().Be("mtgmcp-splitmix64-v1");

        analysis.TryGetProperty("scorecard", out JsonElement scorecard).Should().BeTrue();
        scorecard.TryGetProperty("dimensions", out JsonElement dimensions).Should().BeTrue();
        JsonElement manaStability = FindNamed(dimensions.EnumerateArray(), "mana-stability");
        manaStability.GetProperty("score").GetDouble().Should().BeInRange(0, 1);
        manaStability.GetProperty("sourceMetric").GetString().Should().NotBeNullOrWhiteSpace();

        analysis.TryGetProperty("traceSummary", out JsonElement traceSummary).Should().BeTrue();
        traceSummary.TryGetProperty("sampledRuns", out JsonElement sampledRuns).Should().BeTrue();
        sampledRuns.GetArrayLength().Should().Be(3);
        traceSummary.TryGetProperty("representativeRuns", out _).Should().BeFalse();
        traceSummary.TryGetProperty("aggregateCounters", out JsonElement aggregateCounters).Should().BeTrue();
        aggregateCounters.GetProperty("total-runs").GetInt32().Should().Be(expectedSimulations);
        aggregateCounters.GetProperty("no-mulligan-runs").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    /// <summary>
    /// Finds a JSON property while tolerating serializer casing differences.
    /// </summary>
    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement property))
        {
            return property;
        }

        string pascalCase = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        if (element.TryGetProperty(pascalCase, out property))
        {
            return property;
        }

        throw new InvalidOperationException($"JSON property '{propertyName}' was not found in {element}.");
    }

    /// <summary>
    /// Builds the Scryfall search path for fake HTTP routes.
    /// </summary>
    private static string ScryfallSearchPath(string query)
    {
        return $"cards/search?q={Uri.EscapeDataString(query)}&unique=cards&order=edhrec";
    }

    /// <summary>
    /// Decodes request targets until they stabilize across platform-specific escaping.
    /// </summary>
    private static string DecodeRepeatedly(string value)
    {
        string current = value;
        for (int index = 0; index < 3; index++)
        {
            string decoded = WebUtility.UrlDecode(current);
            if (decoded == current)
            {
                return decoded;
            }

            current = decoded;
        }

        return current;
    }

    /// <summary>
    /// Provides a Scryfall commander payload for local analysis E2E tests.
    /// </summary>
    private const string TinybonesJson = """
    {
      "id": "tinybones",
      "oracle_id": "oracle-tinybones",
      "name": "Tinybones, Trinket Thief",
      "mana_cost": "{1}{B}",
      "cmc": 2,
      "type_line": "Legendary Creature - Skeleton Rogue",
      "oracle_text": "At the beginning of each end step, if an opponent discarded a card this turn, you draw a card and you lose 1 life.",
      "legalities": { "commander": "legal" },
      "prices": { "usd": "2.00" },
      "color_identity": ["B"]
    }
    """;

    /// <summary>
    /// Provides a Scryfall basic land payload for local analysis E2E tests.
    /// </summary>
    private const string SwampJson = """
    {
      "id": "swamp",
      "oracle_id": "oracle-swamp",
      "name": "Swamp",
      "mana_cost": "",
      "cmc": 0,
      "type_line": "Basic Land - Swamp",
      "oracle_text": "{T}: Add {B}.",
      "produced_mana": ["B"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.05" },
      "color_identity": []
    }
    """;

    /// <summary>
    /// Provides an Inga and Esika payload for deck-tuning workflow E2E tests.
    /// </summary>
    private const string IngaAndEsikaJson = """
    {
      "id": "inga-and-esika",
      "oracle_id": "oracle-inga-and-esika",
      "name": "Inga and Esika",
      "mana_cost": "{2}{G}{U}",
      "cmc": 4,
      "type_line": "Legendary Creature - Human God",
      "oracle_text": "Creatures you control have vigilance and \"{T}: Add one mana of any color. Spend this mana only to cast a creature spell.\" Whenever you cast a creature spell, if three or more mana from creatures was spent to cast it, draw a card.",
      "color_identity": ["G", "U"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.50" },
      "scryfall_uri": "https://scryfall.com/card/mom/229/inga-and-esika"
    }
    """;

    /// <summary>
    /// Provides a Forest payload for deck-tuning workflow E2E tests.
    /// </summary>
    private const string ForestJson = """
    {
      "id": "forest",
      "oracle_id": "oracle-forest",
      "name": "Forest",
      "cmc": 0,
      "type_line": "Basic Land - Forest",
      "oracle_text": "({T}: Add {G}.)",
      "produced_mana": ["G"],
      "color_identity": [],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.05" },
      "scryfall_uri": "https://scryfall.com/card/forest"
    }
    """;

    /// <summary>
    /// Provides a Llanowar Elves payload for deck-tuning workflow E2E tests.
    /// </summary>
    private const string LlanowarElvesJson = """
    {
      "id": "llanowar-elves",
      "oracle_id": "oracle-llanowar-elves",
      "name": "Llanowar Elves",
      "mana_cost": "{G}",
      "cmc": 1,
      "type_line": "Creature - Elf Druid",
      "oracle_text": "{T}: Add {G}.",
      "produced_mana": ["G"],
      "color_identity": ["G"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.25" },
      "scryfall_uri": "https://scryfall.com/card/dom/168/llanowar-elves"
    }
    """;

    /// <summary>
    /// Provides an Elvish Mystic payload for deck-tuning workflow E2E tests.
    /// </summary>
    private const string ElvishMysticJson = """
    {
      "id": "elvish-mystic",
      "oracle_id": "oracle-elvish-mystic",
      "name": "Elvish Mystic",
      "mana_cost": "{G}",
      "cmc": 1,
      "type_line": "Creature - Elf Druid",
      "oracle_text": "{T}: Add {G}.",
      "produced_mana": ["G"],
      "color_identity": ["G"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.20" },
      "scryfall_uri": "https://scryfall.com/card/m14/169/elvish-mystic"
    }
    """;

    /// <summary>
    /// Provides a Beast Within payload for deck-tuning workflow E2E tests.
    /// </summary>
    private const string BeastWithinJson = """
    {
      "id": "beast-within",
      "oracle_id": "oracle-beast-within",
      "name": "Beast Within",
      "mana_cost": "{2}{G}",
      "cmc": 3,
      "type_line": "Instant",
      "oracle_text": "Destroy target permanent. Its controller creates a 3/3 green Beast creature token.",
      "color_identity": ["G"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "1.25" },
      "scryfall_uri": "https://scryfall.com/card/c21/186/beast-within"
    }
    """;

    /// <summary>
    /// Provides a Reclamation Sage payload for deck-tuning workflow E2E tests.
    /// </summary>
    private const string ReclamationSageJson = """
    {
      "id": "reclamation-sage",
      "oracle_id": "oracle-reclamation-sage",
      "name": "Reclamation Sage",
      "mana_cost": "{2}{G}",
      "cmc": 3,
      "type_line": "Creature - Elf Shaman",
      "oracle_text": "When Reclamation Sage enters the battlefield, you may destroy target artifact or enchantment.",
      "color_identity": ["G"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.30" },
      "scryfall_uri": "https://scryfall.com/card/m15/194/reclamation-sage"
    }
    """;

    /// <summary>
    /// Provides an Overrun payload for deck-tuning workflow E2E tests.
    /// </summary>
    private const string OverrunJson = """
    {
      "id": "overrun",
      "oracle_id": "oracle-overrun",
      "name": "Overrun",
      "mana_cost": "{2}{G}{G}{G}",
      "cmc": 5,
      "type_line": "Sorcery",
      "oracle_text": "Creatures you control get +3/+3 and gain trample until end of turn.",
      "color_identity": ["G"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "0.15" },
      "scryfall_uri": "https://scryfall.com/card/10e/288/overrun"
    }
    """;

    /// <summary>
    /// Provides a tiny fake Archidekt response for the public Inga and Esika deck URL path.
    /// </summary>
    private const string IngaArchidektDeckJson = """
    {
      "id": 23097041,
      "name": "Inga and Esika",
      "deckFormat": "commander",
      "categories": [
        { "id": 1, "name": "Commander", "includedInDeck": true, "includedInPrice": true },
        { "id": 2, "name": "Lands", "includedInDeck": true, "includedInPrice": true },
        { "id": 3, "name": "Ramp", "includedInDeck": true, "includedInPrice": true },
        { "id": 4, "name": "Interaction", "includedInDeck": true, "includedInPrice": true },
        { "id": 5, "name": "Wincons", "includedInDeck": true, "includedInPrice": true }
      ],
      "cards": [
        {
          "id": 1,
          "quantity": 1,
          "categories": ["Commander"],
          "card": {
            "oracleCard": {
              "name": "Inga and Esika",
              "manaCost": "{2}{G}{U}",
              "cmc": 4,
              "types": ["Legendary", "Creature"],
              "oracleText": "Creatures you control have vigilance and creature-spell mana.",
              "colorIdentity": ["G", "U"]
            }
          }
        },
        {
          "id": 2,
          "quantity": 10,
          "categories": ["Lands"],
          "card": {
            "oracleCard": {
              "name": "Forest",
              "cmc": 0,
              "types": ["Basic", "Land"],
              "oracleText": "{T}: Add {G}.",
              "colorIdentity": []
            }
          }
        }
      ]
    }
    """;

    /// <summary>
    /// Provides Scryfall collection hydration for the fake Archidekt Inga import.
    /// </summary>
    private const string IngaWorkflowCollectionJson = """
    {
      "data": [
        {
          "id": "inga-and-esika",
          "oracle_id": "oracle-inga-and-esika",
          "name": "Inga and Esika",
          "mana_cost": "{2}{G}{U}",
          "cmc": 4,
          "type_line": "Legendary Creature - Human God",
          "oracle_text": "Creatures you control have vigilance and \"{T}: Add one mana of any color. Spend this mana only to cast a creature spell.\" Whenever you cast a creature spell, if three or more mana from creatures was spent to cast it, draw a card.",
          "color_identity": ["G", "U"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.50" },
          "scryfall_uri": "https://scryfall.com/card/mom/229/inga-and-esika"
        },
        {
          "id": "forest",
          "oracle_id": "oracle-forest",
          "name": "Forest",
          "cmc": 0,
          "type_line": "Basic Land - Forest",
          "oracle_text": "({T}: Add {G}.)",
          "produced_mana": ["G"],
          "color_identity": [],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.05" },
          "scryfall_uri": "https://scryfall.com/card/forest"
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload shared by E2E flows that add Lightning Bolt.
    /// </summary>
    private const string LightningBoltJson = """
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "oracle_id": "00000000-0000-0000-0000-000000000002",
      "name": "Lightning Bolt",
      "mana_cost": "{R}",
      "cmc": 1,
      "type_line": "Instant",
      "oracle_text": "Lightning Bolt deals 3 damage to any target.",
      "set": "clu",
      "collector_number": "141",
      "scryfall_uri": "https://scryfall.example/card/clu/141",
      "color_identity": ["R"]
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload for local analysis E2E tests.
    /// </summary>
    private const string PhyrexianArenaJson = """
    {
      "id": "phyrexian-arena",
      "oracle_id": "oracle-phyrexian-arena",
      "name": "Phyrexian Arena",
      "mana_cost": "{1}{B}{B}",
      "cmc": 3,
      "type_line": "Enchantment",
      "oracle_text": "At the beginning of your upkeep, you draw a card and you lose 1 life.",
      "legalities": { "commander": "legal" },
      "prices": { "usd": "6.00" },
      "color_identity": ["B"]
    }
    """;

    /// <summary>
    /// Provides an empty Scryfall search payload.
    /// </summary>
    private const string EmptySearchJson = """
    {
      "has_more": false,
      "data": []
    }
    """;

    /// <summary>
    /// Provides Scryfall search results for query-first recommendation E2E tests.
    /// </summary>
    private const string QueryRecommendationSearchJson = """
    {
      "has_more": false,
      "data": [
        {
          "id": "geths-grimoire",
          "name": "Geth's Grimoire"
        },
        {
          "id": "zulaport-cutthroat",
          "name": "Zulaport Cutthroat"
        },
        {
          "id": "torment-of-hailfire",
          "name": "Torment of Hailfire"
        }
      ]
    }
    """;

    /// <summary>
    /// Provides Scryfall collection data for query-first recommendation E2E tests.
    /// </summary>
    private const string QueryRecommendationCollectionJson = """
    {
      "data": [
        {
          "id": "geths-grimoire",
          "oracle_id": "oracle-geths-grimoire",
          "name": "Geth's Grimoire",
          "mana_cost": "{4}",
          "cmc": 4,
          "type_line": "Artifact",
          "oracle_text": "Whenever an opponent discards a card, you may draw a card.",
          "color_identity": [],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "4.00" },
          "edhrec_rank": 1800
        },
        {
          "id": "zulaport-cutthroat",
          "oracle_id": "oracle-zulaport-cutthroat",
          "name": "Zulaport Cutthroat",
          "mana_cost": "{1}{B}",
          "cmc": 2,
          "type_line": "Creature - Human Rogue Ally",
          "oracle_text": "Whenever Zulaport Cutthroat or another creature you control dies, each opponent loses 1 life and you gain 1 life.",
          "color_identity": ["B"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "1.00" },
          "edhrec_rank": 800
        },
        {
          "id": "torment-of-hailfire",
          "oracle_id": "oracle-torment-of-hailfire",
          "name": "Torment of Hailfire",
          "mana_cost": "{X}{B}{B}",
          "cmc": 2,
          "type_line": "Sorcery",
          "oracle_text": "Repeat the following process X times. Each opponent loses 3 life unless they sacrifice a nonland permanent or discard a card.",
          "color_identity": ["B"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "8.00" },
          "edhrec_rank": 400
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall search payload for one-stop deckbuilding E2E tests.
    /// </summary>
    private const string TableEdictSearchJson = """
    {
      "has_more": false,
      "data": [
        {
          "id": "table-edict",
          "name": "Table Edict",
          "type_line": "Sorcery",
          "oracle_text": "Each opponent sacrifices a creature. You draw a card.",
          "released_at": "2026-02-01",
          "set": "tst",
          "prices": { "usd": "0.50" },
          "edhrec_rank": 2500
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall collection payload for one-stop deckbuilding E2E tests.
    /// </summary>
    private const string TableEdictCollectionJson = """
    {
      "data": [
        {
          "id": "table-edict",
          "oracle_id": "oracle-table-edict",
          "name": "Table Edict",
          "mana_cost": "{3}{B}",
          "cmc": 4,
          "type_line": "Sorcery",
          "oracle_text": "Each opponent sacrifices a creature. You draw a card.",
          "released_at": "2026-02-01",
          "set": "tst",
          "color_identity": ["B"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.50" },
          "edhrec_rank": 2500
        }
      ]
    }
    """;

    /// <summary>
    /// Provides Scryfall collection data for performance E2E deck imports and candidate hydration.
    /// </summary>
    private const string PerformanceCollectionJson = """
    {
      "data": [
        {
          "id": "forest",
          "oracle_id": "oracle-forest",
          "name": "Forest",
          "cmc": 0,
          "type_line": "Basic Land - Forest",
          "oracle_text": "({T}: Add {G}.)",
          "produced_mana": ["G"],
          "color_identity": ["G"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.05" }
        },
        {
          "id": "blank-spell",
          "oracle_id": "oracle-blank-spell",
          "name": "Blank Spell",
          "mana_cost": "{3}",
          "cmc": 3,
          "type_line": "Sorcery",
          "oracle_text": "Scry 1.",
          "color_identity": [],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.05" }
        },
        {
          "id": "arcane-signet",
          "oracle_id": "oracle-arcane-signet",
          "name": "Arcane Signet",
          "mana_cost": "{2}",
          "cmc": 2,
          "type_line": "Artifact",
          "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
          "produced_mana": ["W", "U", "B", "R", "G"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "1.00" },
          "edhrec_rank": 5
        }
      ]
    }
    """;

    /// <summary>
    /// Provides an empty Commander Spellbook payload.
    /// </summary>
    private const string EmptySpellbookJson = """
    {
      "results": {
        "included": [],
        "almostIncluded": []
      }
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload for source-backed budget replacement E2E tests.
    /// </summary>
    private const string ManaCryptJson = """
    {
      "id": "mana-crypt",
      "oracle_id": "oracle-mana-crypt",
      "name": "Mana Crypt",
      "mana_cost": "{0}",
      "cmc": 0,
      "type_line": "Artifact",
      "oracle_text": "{T}: Add two colorless mana.",
      "legalities": { "commander": "legal" },
      "prices": { "usd": "180.00" },
      "edhrec_rank": 20
    }
    """;

    /// <summary>
    /// Provides a Scryfall card payload for consistency workflow E2E tests.
    /// </summary>
    private const string ArcaneSignetJson = """
    {
      "id": "arcane-signet",
      "oracle_id": "oracle-arcane-signet",
      "name": "Arcane Signet",
      "mana_cost": "{2}",
      "cmc": 2,
      "type_line": "Artifact",
      "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
      "produced_mana": ["W", "U", "B", "R", "G"],
      "legalities": { "commander": "legal" },
      "prices": { "usd": "1.00" },
      "edhrec_rank": 5
    }
    """;

    /// <summary>
    /// Provides a Scryfall collection payload for Arcane Signet.
    /// </summary>
    private const string ArcaneSignetCollectionJson = """
    {
      "data": [
        {
          "id": "arcane-signet",
          "oracle_id": "oracle-arcane-signet",
          "name": "Arcane Signet",
          "mana_cost": "{2}",
          "cmc": 2,
          "type_line": "Artifact",
          "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
          "produced_mana": ["W", "U", "B", "R", "G"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "1.00" },
          "edhrec_rank": 5
        }
      ]
    }
    """;

    /// <summary>
    /// Provides Scryfall collection data for bulk category E2E tests.
    /// </summary>
    private const string BulkCategoryCollectionJson = """
    {
      "data": [
        {
          "id": "00000000-0000-0000-0000-000000000001",
          "oracle_id": "00000000-0000-0000-0000-000000000002",
          "name": "Lightning Bolt",
          "mana_cost": "{R}",
          "cmc": 1,
          "type_line": "Instant",
          "oracle_text": "Lightning Bolt deals 3 damage to any target.",
          "set": "clu",
          "collector_number": "141",
          "scryfall_uri": "https://scryfall.example/card/clu/141",
          "color_identity": ["R"]
        },
        {
          "id": "arcane-signet",
          "oracle_id": "oracle-arcane-signet",
          "name": "Arcane Signet",
          "mana_cost": "{2}",
          "cmc": 2,
          "type_line": "Artifact",
          "oracle_text": "{T}: Add one mana of any color in your commander's color identity.",
          "produced_mana": ["W", "U", "B", "R", "G"],
          "legalities": { "commander": "legal" },
          "prices": { "usd": "1.00" },
          "edhrec_rank": 5
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall search payload for source refresh E2E tests.
    /// </summary>
    private const string HiddenGemSearchJson = """
    {
      "has_more": false,
      "data": [
        {
          "id": "hidden-gem",
          "name": "Hidden Gem of Discard",
          "type_line": "Enchantment",
          "oracle_text": "Whenever an opponent discards a card, draw a card.",
          "prices": { "usd": "0.50" },
          "edhrec_rank": 15000
        }
      ]
    }
    """;

    /// <summary>
    /// Provides a Scryfall collection payload for source refresh E2E tests.
    /// </summary>
    private const string HiddenGemCollectionJson = """
    {
      "data": [
        {
          "id": "hidden-gem",
          "oracle_id": "oracle-hidden-gem",
          "name": "Hidden Gem of Discard",
          "mana_cost": "{2}{B}",
          "cmc": 3,
          "type_line": "Enchantment",
          "oracle_text": "Whenever an opponent discards a card, draw a card.",
          "legalities": { "commander": "legal" },
          "prices": { "usd": "0.50" },
          "edhrec_rank": 15000
        }
      ]
    }
    """;

    /// <summary>
    /// Provides an Archidekt deck payload with enough real shape to drive workflow primitive E2E tests.
    /// </summary>
    private const string RemoteSwampsDeckJson = """
    {
      "id": 123,
      "name": "Remote Swamps",
      "deckFormat": 3,
      "edhBracket": null,
      "categories": [
        { "id": 1, "name": "Lands", "includedInDeck": true, "includedInPrice": true }
      ],
      "cards": [
        {
          "id": 44,
          "quantity": 36,
          "categories": ["Lands"],
          "card": {
            "id": 99,
            "uid": "swamp-print",
            "prices": { "tcg": 0.05 },
            "oracleCard": {
              "uid": "swamp-oracle",
              "name": "Swamp",
              "typeLine": "Basic Land - Swamp",
              "manaValue": 0,
              "colorIdentity": [],
              "text": "{T}: Add {B}."
            }
          }
        }
      ]
    }
    """;
}
