using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies that tools list and report failures the same way under every protocol version the server speaks.
/// The schemas are read as raw JSON values. A typed property would hide the bug these tests guard against:
/// the SDK once published <c>true</c> as an output schema, which Claude Code rejects under 2026-07-28.
/// </summary>
public sealed class ToolProtocolMcpTests
{
    /// <summary>
    /// Verifies every tool publishes an input schema and an output schema that are JSON objects of type object.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("2024-11-05")]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ToolList_AtEachProtocolVersion_PublishesObjectSchemas(string version)
    {
        await using McpProcessSession session = await StartAsync(version).ConfigureAwait(false);
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(version, session.Client.NegotiatedProtocolVersion);
        Assert.Equal(86, tools.Count);
        foreach (McpClientTool tool in tools)
        {
            AssertObjectSchema(tool.Name, "inputSchema", tool.ProtocolTool.InputSchema);
            JsonElement output = tool.ProtocolTool.OutputSchema ??
                throw new Xunit.Sdk.XunitException($"{tool.Name} publishes no outputSchema.");
            AssertObjectSchema(tool.Name, "outputSchema", output);
            Assert.False(
                output.GetProperty("properties").TryGetProperty("result", out _),
                $"{tool.Name} wraps its output schema in a result property.");
        }
    }

    /// <summary>
    /// Verifies a successful call returns the unwrapped result, with a top-level kind the schema allows.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("2024-11-05")]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ToolCall_Success_ReturnsTopLevelKindFromTheSchema(string version)
    {
        await using McpProcessSession session = await StartAsync(version).ConfigureAwait(false);
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        McpClientTool tool = tools.Single(candidate => candidate.Name == "scryfall_card_data_status");

        CallToolResult result = await session.Client.CallToolAsync(
            tool.Name,
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
        string kind = body.GetProperty("kind").GetString()!;
        Assert.Equal("success", kind);
        Assert.NotEqual(true, result.IsError);
        Assert.Contains(kind, AllowedKinds(tool));
    }

    /// <summary>
    /// Verifies a failed call sets the MCP error flag and returns the unwrapped result with a top-level kind.
    /// </summary>
    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("2024-11-05")]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ToolCall_UnknownDeck_SetsIsErrorAndReturnsTopLevelKind(string version)
    {
        await using McpProcessSession session = await StartAsync(version).ConfigureAwait(false);
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        McpClientTool tool = tools.Single(candidate => candidate.Name == "local_deck_get");

        CallToolResult result = await session.Client.CallToolAsync(
            tool.Name,
            new Dictionary<string, object?> { ["deckId"] = Guid.NewGuid().ToString() },
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(result.IsError);
        JsonElement body = Assert.IsType<JsonElement>(result.StructuredContent);
        string kind = body.GetProperty("kind").GetString()!;
        Assert.Equal("not-found", kind);
        Assert.Contains(kind, AllowedKinds(tool));
    }

    /// <summary>
    /// Starts a server with every toolset at one protocol version.
    /// </summary>
    private static Task<McpProcessSession> StartAsync(string version)
    {
        return McpProcessSession.StartWithProtocolAsync(
            "remote",
            "all",
            version,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asserts a schema value is a JSON object whose type is object.
    /// </summary>
    private static void AssertObjectSchema(string toolName, string field, JsonElement schema)
    {
        Assert.True(
            schema.ValueKind == JsonValueKind.Object,
            $"{toolName} {field} is {schema.ValueKind}: {schema.GetRawText()}");
        Assert.True(
            schema.TryGetProperty("type", out JsonElement type) && type.GetString() == "object",
            $"{toolName} {field} does not have type object.");
    }

    /// <summary>
    /// Reads the result kinds an output schema allows.
    /// </summary>
    private static string[] AllowedKinds(McpClientTool tool)
    {
        JsonElement schema = tool.ProtocolTool.OutputSchema!.Value;
        return [.. schema.GetProperty("properties").GetProperty("kind").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()!)];
    }
}
