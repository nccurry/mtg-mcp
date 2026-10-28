using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies how the real server binds tool arguments and reports binding and operation failures.
/// </summary>
public sealed class ToolInputBindingMcpTests
{
    /// <summary>
    /// Verifies a union change whose discriminator is the last property applies like one with it first.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ApplyChanges_KindAfterOtherProperties_AppliesChange()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, long revision) = await CreateDeckAsync(session).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "deck_apply_changes",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["changes"] = new[]
                {
                    new
                    {
                        entryDraft = new { quantity = 1, cardName = "Sol Ring" },
                        kind = "add-entry",
                    },
                },
            }).ConfigureAwait(false);

        Assert.NotEqual(true, result.IsError);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("success", structured.GetProperty("kind").GetString());
        Assert.Equal(2, structured.GetProperty("data").GetProperty("entries").GetArrayLength());
    }

    /// <summary>
    /// Verifies an unknown union discriminator returns a bounded invalid-input failure with its JSON path.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ApplyChanges_UnknownKind_ReturnsInvalidArgumentsWithPath()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, long revision) = await CreateDeckAsync(session).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "deck_apply_changes",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["changes"] = new[] { new { kind = "rename-entry" } },
            }).ConfigureAwait(false);

        AssertInvalidArguments(result, "changes[0]", "add-entry");
    }

    /// <summary>
    /// Verifies a wrongly typed nested value returns a bounded invalid-input failure with its JSON path.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task ApplyChanges_WrongValueType_ReturnsInvalidArgumentsWithPath()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        (Guid deckId, long revision) = await CreateDeckAsync(session).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "deck_apply_changes",
            new Dictionary<string, object?>
            {
                ["deckId"] = deckId,
                ["expectedRevision"] = revision,
                ["changes"] = new[]
                {
                    new
                    {
                        kind = "add-entry",
                        entryDraft = new { quantity = "three", cardName = "Sol Ring" },
                    },
                },
            }).ConfigureAwait(false);

        AssertInvalidArguments(result, "changes[0].entryDraft.quantity");
    }

    /// <summary>
    /// Verifies an operation failure is reported with the MCP error flag and an unchanged structured body.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task OperationFailure_SetsIsErrorAndKeepsStructuredBody()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "local",
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        CallToolResult result = await CallAsync(
            session,
            "deck_get",
            new Dictionary<string, object?> { ["deckId"] = Guid.CreateVersion7() }).ConfigureAwait(false);

        Assert.True(result.IsError);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("not-found", structured.GetProperty("kind").GetString());
        Assert.NotEmpty(result.Content);
    }

    /// <summary>
    /// Verifies every listed tool with required arguments fails an empty call with a flagged invalid-arguments body.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task EveryToolWithRequiredArguments_EmptyCall_ReturnsFlaggedInvalidArguments()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "remote",
            "all",
            TestContext.Current.CancellationToken).ConfigureAwait(false);
        IList<McpClientTool> tools = await session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        int checkedTools = 0;

        foreach (McpClientTool tool in tools)
        {
            string[] required = ReadRequiredNames(tool.ProtocolTool.InputSchema);
            if (required.Length == 0)
            {
                continue;
            }

            CallToolResult result = await CallAsync(session, tool.Name, new Dictionary<string, object?>())
                .ConfigureAwait(false);

            Assert.True(result.IsError, $"{tool.Name} did not set isError.");
            JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
            Assert.Equal("invalid-input", structured.GetProperty("kind").GetString());
            Assert.Equal("invalid-arguments", structured.GetProperty("reasonCode").GetString());
            Assert.Contains(structured.GetProperty("path").GetString(), required);
            checkedTools++;
        }

        Assert.True(checkedTools > 50, $"Only {checkedTools} tools with required arguments were checked.");
    }

    /// <summary>
    /// Reads the names of the arguments a tool's input schema requires.
    /// </summary>
    private static string[] ReadRequiredNames(JsonElement schema)
    {
        return schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array
            ? [.. required.EnumerateArray().Select(value => value.GetString()!)]
            : [];
    }

    /// <summary>
    /// Creates one small local deck and returns its identity and revision.
    /// </summary>
    private static async Task<(Guid DeckId, long Revision)> CreateDeckAsync(McpProcessSession session)
    {
        CallToolResult created = await CallAsync(
            session,
            "deck_create",
            new Dictionary<string, object?>
            {
                ["request"] = new
                {
                    name = "Binding Fixture",
                    format = "commander",
                    entries = new[] { new { quantity = 1, cardName = "Dummy Commander", zone = "commander" } },
                },
            }).ConfigureAwait(false);
        Assert.NotEqual(true, created.IsError);
        JsonElement data = Assert.IsType<JsonElement>(created.StructuredContent).GetProperty("data");
        return (data.GetProperty("deckId").GetGuid(), data.GetProperty("revision").GetInt64());
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
    /// Asserts the bounded binding failure shape, including the JSON path of the rejected value.
    /// </summary>
    private static void AssertInvalidArguments(
        CallToolResult result,
        string expectedPath,
        string? expectedMessageFragment = null)
    {
        Assert.True(result.IsError);
        string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.True(result.StructuredContent is not null, text);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("invalid-input", structured.GetProperty("kind").GetString());
        Assert.Equal("invalid-arguments", structured.GetProperty("reasonCode").GetString());
        Assert.Equal(expectedPath, structured.GetProperty("path").GetString());
        if (expectedMessageFragment is not null)
        {
            Assert.Contains(
                expectedMessageFragment,
                structured.GetProperty("message").GetString(),
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain("An error occurred invoking", structured.ToString(), StringComparison.Ordinal);
    }
}
