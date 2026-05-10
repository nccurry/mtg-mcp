using System.Text.Json;
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
            "analyze_deck"
        ]);
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
            .ContainSingle(request => request.PathAndQuery == "cards/named?fuzzy=Lightning%20Bolt");
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
}
