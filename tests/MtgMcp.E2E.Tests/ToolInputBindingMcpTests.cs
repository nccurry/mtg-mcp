using System.Text.Json;
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

        AssertInvalidArguments(result, "changes[0]");
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
    private static void AssertInvalidArguments(CallToolResult result, string expectedPath)
    {
        Assert.True(result.IsError);
        string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.True(result.StructuredContent is not null, text);
        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("invalid-input", structured.GetProperty("kind").GetString());
        Assert.Equal("invalid-arguments", structured.GetProperty("reasonCode").GetString());
        Assert.StartsWith(expectedPath, structured.GetProperty("path").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("An error occurred invoking", structured.ToString(), StringComparison.Ordinal);
    }
}
