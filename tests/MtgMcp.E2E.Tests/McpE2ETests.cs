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

        tools.Select(tool => tool.Name).Should().Contain(
        [
            "search_cards",
            "start_deck_workspace",
            "add_card",
            "analyze_deck",
            "analyze_deck_performance",
            "compare_plan_performance",
            "get_server_info"
        ]);

        JsonElement serverInfo = await CallJsonAsync(
            session.Client,
            "get_server_info",
            new Dictionary<string, object?>());
        GetString(serverInfo, "assemblyName").Should().Be("MtgMcp.App");
        GetString(serverInfo, "operationMode").Should().Be("apply");
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
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Local",
                ["format"] = "modern"
            });
        string workspaceId = GetString(workspace, "id");

        JsonElement change = await CallJsonAsync(
            session.Client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 2,
                ["category"] = "Mainboard"
            });
        string export = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement analysis = await CallJsonAsync(
            session.Client,
            "analyze_deck",
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
        scryfall.GetJson(
            ScryfallSearchPath("(o:add or o:treasure or o:\"search your library for a land\") legal:commander usd<=5"),
            ArcaneSignetSearchJson);
        scryfall.GetJson("cards/named?fuzzy=Arcane%20Signet", ArcaneSignetJson);

        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "apply",
            TestContext.Current.CancellationToken);

        JsonElement workspace = await CallJsonAsync(
            session.Client,
            "import_decklist",
            new Dictionary<string, object?>
            {
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
            "analyze_deck_performance",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["simulations"] = 200,
                ["maxTurn"] = 3,
                ["seed"] = 2026,
                ["includeMulligans"] = false
            });
        JsonElement planResult = await CallJsonAsync(
            session.Client,
            "find_consistency_improvements",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["focus"] = "ramp",
                ["maxPrice"] = 5,
                ["limit"] = 1
            });
        string planId = GetString(GetObject(planResult, "plan"), "planId");
        JsonElement comparison = await CallJsonAsync(
            session.Client,
            "compare_plan_performance",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["simulations"] = 200,
                ["maxTurn"] = 3,
                ["seed"] = 2026
            });

        GetInt32(analysis, "deckSize").Should().Be(100);
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
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E Intent",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");
        string richDescription = """
        {"ops":[{"insert":"Primer","attributes":{"bold":true}},{"insert":" before\n"},{"insert":{"image":"https://example.test/card.jpg"}},{"insert":"\nPrimer after\n","attributes":{"italic":true}}]}
        """;

        await CallJsonAsync(
            session.Client,
            "update_deck_metadata",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["description"] = richDescription
            });
        JsonElement setResult = await CallJsonAsync(
            session.Client,
            "set_deck_intent",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["intentText"] = "Archetype: discard-control"
            });
        string setDescription = GetString(GetObject(setResult, "workspace"), "description");

        setDescription.Should().Contain("\"bold\":true");
        setDescription.Should().Contain("\"italic\":true");
        setDescription.Should().Contain("\"image\":\"https://example.test/card.jpg\"");
        setDescription.Should().Contain("MTG MCP Deck Intent");
        GetString(setResult, "persistence").Should().Be("local-only");

        JsonElement clearResult = await CallJsonAsync(
            session.Client,
            "clear_deck_intent",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        string clearedDescription = GetString(GetObject(clearResult, "workspace"), "description");

        clearedDescription.Should().Contain("\"bold\":true");
        clearedDescription.Should().Contain("\"italic\":true");
        clearedDescription.Should().Contain("\"image\":\"https://example.test/card.jpg\"");
        clearedDescription.Should().NotContain("MTG MCP Deck Intent");
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
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "local",
                ["name"] = "E2E One Stop",
                ["format"] = "commander"
            });
        string workspaceId = GetString(workspace, "id");

        JsonElement goal = await CallJsonAsync(
            session.Client,
            "find_cards_for_deck_goal",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["goal"] = "add a few cards that interact with the whole table",
                ["count"] = 1,
                ["maxPrice"] = 5
            });
        JsonElement best = await CallJsonAsync(
            session.Client,
            "analyze_deck_best_practices",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement goldfish = await CallJsonAsync(
            session.Client,
            "simulate_goldfish",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["targetTurn"] = 3,
                ["simulations"] = 100
            });
        JsonElement combos = await CallJsonAsync(
            session.Client,
            "find_deck_combos",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });
        JsonElement brainstorm = await CallJsonAsync(
            session.Client,
            "brainstorm_deck_improvements",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["goal"] = "add a few cards that interact with the whole table",
                ["budget"] = 5
            });

        GetObject(goal, "plan").GetProperty("operations").GetArrayLength().Should().BeGreaterThan(0);
        GetString(best, "recommendedProfile").Should().Be("commander-baseline");
        GetInt32(goldfish, "targetTurn").Should().Be(3);
        GetObject(combos, "pressure").ValueKind.Should().Be(JsonValueKind.Object);
        GetObject(brainstorm, "goalPackage").ValueKind.Should().Be(JsonValueKind.Object);
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
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            });
        string workspaceId = GetString(workspace, "id");
        JsonElement change = await CallJsonAsync(
            session.Client,
            "add_card",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["cardName"] = "Lightning Bolt",
                ["quantity"] = 1,
                ["category"] = "Mainboard"
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
            ScryfallSearchPath(
                "(o:add or o:treasure or o:\"search your library for a land\") legal:commander usd<=10"
            ),
            """
            {
              "has_more": false,
              "data": [
                { "id": "arcane-signet", "name": "Arcane Signet", "type_line": "Artifact" }
              ]
            }
            """);
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
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            });
        string workspaceId = GetString(workspace, "id");
        string beforePreviewExport = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        JsonElement planResult = await CallJsonAsync(
            session.Client,
            "find_consistency_improvements",
            new Dictionary<string, object?>
            {
                ["workspaceId"] = workspaceId,
                ["focus"] = "ramp",
                ["maxPrice"] = 10,
                ["limit"] = 1
            });
        string planId = GetString(GetObject(planResult, "plan"), "planId");
        JsonElement preview = await CallJsonAsync(
            session.Client,
            "preview_deck_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["resolveAddedCards"] = true
            });
        string afterPreviewExport = await CallTextAsync(
            session.Client,
            "export_deck",
            new Dictionary<string, object?> { ["workspaceId"] = workspaceId });

        GetInt32(GetObject(GetObject(preview, "before"), "consistency"), "rampCount").Should().Be(0);
        GetInt32(GetObject(GetObject(preview, "after"), "consistency"), "rampCount").Should().Be(1);
        beforePreviewExport.Should().NotContain("Arcane Signet");
        afterPreviewExport.Should().Be(beforePreviewExport);
        archidekt.Requests.Should().NotContain(request => request.Method == "PATCH");

        JsonElement apply = await CallJsonAsync(
            session.Client,
            "apply_deck_plan",
            new Dictionary<string, object?>
            {
                ["planId"] = planId,
                ["createCheckpoint"] = false
            });
        string afterApplyExport = await CallTextAsync(
            session.Client,
            "export_deck",
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
    /// Verifies that read-only mode rejects Archidekt workspace mutation before any HTTP call.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ReadOnlyMode_BlocksArchidektWorkspaceBeforeHttp()
    {
        await using FakeHttpServer scryfall = new();
        await using FakeHttpServer archidekt = new();
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            scryfall.BaseAddress,
            archidekt.BaseAddress,
            operationMode: "read-only",
            TestContext.Current.CancellationToken);

        CallToolResult result = await session.Client.CallToolAsync(
            "start_deck_workspace",
            new Dictionary<string, object?>
            {
                ["mode"] = "archidekt",
                ["archidektDeckIdOrUrl"] = "123",
                ["writeBack"] = true
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.Should().BeTrue();
        ReadText(result).Should().Contain("start_deck_workspace");
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
    /// Provides an empty Scryfall search payload.
    /// </summary>
    private const string EmptySearchJson = """
    {
      "has_more": false,
      "data": []
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
    /// Provides a Scryfall search payload for performance comparison E2E tests.
    /// </summary>
    private const string ArcaneSignetSearchJson = """
    {
      "has_more": false,
      "data": [
        {
          "id": "arcane-signet",
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
