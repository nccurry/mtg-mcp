using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Exercises the complete current deckbuilding workflow across static toolset profiles.
/// </summary>
public sealed class ToolsetNorthStarMcpTests
{
    /// <summary>
    /// Creates, inspects, exports, and removes a mock Commander deck before checking none and all profiles.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task DefaultNoneAndAll_CompleteMockCommanderWorkflow()
    {
        await using (McpProcessSession defaultSession = await McpProcessSession.StartAsync(
            "local",
            "default",
            TestContext.Current.CancellationToken).ConfigureAwait(false))
        {
            JsonElement capability = await ReadCapabilityAsync(defaultSession).ConfigureAwait(false);
            Assert.Equal("default", capability.GetProperty("toolsets").GetProperty("selection").GetString());
            Assert.Equal(43, capability.GetProperty("surface").GetProperty("toolCount").GetInt32());

            const string deckText = "[commander]\n1 Mock Commander\n[main]\n2 Island";
            JsonElement preview = await CallSuccessAsync(
                defaultSession,
                "deck_import_preview",
                new Dictionary<string, object?>
                {
                    ["formatId"] = "generic-text-v1",
                    ["content"] = deckText,
                    ["options"] = new
                    {
                        deckName = "Toolset Mock Commander",
                        format = "commander",
                    },
                }).ConfigureAwait(false);
            JsonElement imported = await CallSuccessAsync(
                defaultSession,
                "deck_import_create",
                new Dictionary<string, object?>
                {
                    ["formatId"] = "generic-text-v1",
                    ["content"] = deckText,
                    ["expectedFingerprint"] = preview.GetProperty("fingerprint").GetString(),
                    ["options"] = new
                    {
                        deckName = "Toolset Mock Commander",
                        format = "commander",
                    },
                }).ConfigureAwait(false);
            JsonElement importedDeck = imported.GetProperty("deck");
            Guid deckId = importedDeck.GetProperty("deckId").GetGuid();
            long revision = importedDeck.GetProperty("revision").GetInt64();

            JsonElement updated = await CallSuccessAsync(
                defaultSession,
                "deck_update",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                    ["name"] = "Toolset Mock Commander Updated",
                    ["description"] = "Disposable north-star workflow",
                    ["format"] = "commander",
                }).ConfigureAwait(false);
            revision = updated.GetProperty("revision").GetInt64();

            JsonElement loaded = await CallSuccessAsync(
                defaultSession,
                "deck_get",
                new Dictionary<string, object?> { ["deckId"] = deckId }).ConfigureAwait(false);
            JsonElement formats = await CallSuccessAsync(
                defaultSession,
                "deck_interchange_formats",
                new Dictionary<string, object?>()).ConfigureAwait(false);
            JsonElement exported = await CallSuccessAsync(
                defaultSession,
                "deck_export_bundle",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["formatId"] = "mtg-mcp-json-v1",
                }).ConfigureAwait(false);

            Assert.Equal("Toolset Mock Commander Updated", loaded.GetProperty("name").GetString());
            Assert.Equal("commander", loaded.GetProperty("format").GetString());
            Assert.Equal(2, loaded.GetProperty("entries").GetArrayLength());
            Assert.Equal(4, formats.GetArrayLength());
            Assert.Contains(
                exported.GetProperty("artifacts").EnumerateArray(),
                artifact => artifact.GetProperty("fileName").GetString() == "deck.mtg-mcp.json");

            _ = await CallSuccessAsync(
                defaultSession,
                "deck_delete",
                new Dictionary<string, object?>
                {
                    ["deckId"] = deckId,
                    ["expectedRevision"] = revision,
                }).ConfigureAwait(false);
            JsonElement remaining = await CallSuccessAsync(
                defaultSession,
                "deck_list",
                new Dictionary<string, object?>()).ConfigureAwait(false);
            Assert.Empty(remaining.GetProperty("items").EnumerateArray());
        }

        await using (McpProcessSession noneSession = await McpProcessSession.StartAsync(
            "local",
            "none",
            TestContext.Current.CancellationToken).ConfigureAwait(false))
        {
            JsonElement capability = await ReadCapabilityAsync(noneSession).ConfigureAwait(false);
            Assert.Null(noneSession.Client.ServerCapabilities.Tools);
            Assert.Equal("none", capability.GetProperty("toolsets").GetProperty("selection").GetString());
            Assert.Equal(0, capability.GetProperty("surface").GetProperty("toolCount").GetInt32());
            Assert.False(
                capability.GetProperty("toolsets").GetProperty("items")[0].GetProperty("enabled").GetBoolean());
            Assert.False(Directory.Exists(noneSession.DataRoot));
        }

        await using (McpProcessSession allSession = await McpProcessSession.StartAsync(
            "local",
            "all",
            TestContext.Current.CancellationToken).ConfigureAwait(false))
        {
            IList<McpClientTool> tools = await allSession.Client.ListToolsAsync(
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
            JsonElement capability = await ReadCapabilityAsync(allSession).ConfigureAwait(false);

            Assert.Equal(69, tools.Count);
            Assert.Equal(tools.Select(tool => tool.Name).Order(StringComparer.Ordinal), tools.Select(tool => tool.Name));
            Assert.Equal("all", capability.GetProperty("toolsets").GetProperty("selection").GetString());
            Assert.Equal(69, capability.GetProperty("surface").GetProperty("toolCount").GetInt32());
            Assert.False(Directory.Exists(allSession.DataRoot));
        }
    }

    /// <summary>
    /// Reads and parses the public capability resource from one initialized session.
    /// </summary>
    private static async Task<JsonElement> ReadCapabilityAsync(McpProcessSession session)
    {
        ReadResourceResult result = await session.Client.ReadResourceAsync(
            "mtg://server/capabilities",
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        TextResourceContents content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        return JsonSerializer.Deserialize<JsonElement>(content.Text);
    }

    /// <summary>
    /// Calls one tool and extracts its successful structured result payload.
    /// </summary>
    private static async Task<JsonElement> CallSuccessAsync(
        McpProcessSession session,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments)
    {
        CallToolResult call = await session.Client.CallToolAsync(
            toolName,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.NotEqual(true, call.IsError);
        JsonElement content = Assert.IsType<JsonElement>(call.StructuredContent);
        JsonElement result = content.GetProperty("result");
        Assert.Equal("success", result.GetProperty("kind").GetString());
        return result.GetProperty("data");
    }
}
