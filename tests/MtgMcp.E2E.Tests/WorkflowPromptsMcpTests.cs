using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Verifies the three workflow prompts through the real server over standard input and output (PROMPT-001).
/// </summary>
public sealed class WorkflowPromptsMcpTests
{
    /// <summary>
    /// Holds a local deck ID with no spaces.
    /// </summary>
    private const string DeckId = "0193e97a-ef95-7700-8000-000000000001";

    /// <summary>
    /// Verifies the server lists the three prompts with their arguments and gets one with its ID filled in, in a
    /// mode that hides every write tool and with no tools at all (prompts do not depend on the toolset list).
    /// </summary>
    [Theory]
    [InlineData("read-only", null)]
    [InlineData("remote", "none")]
    [Trait("Category", "E2E")]
    public async Task Prompts_AreListedAndGettable(string mode, string? toolsets)
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            mode,
            toolsets,
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.NotNull(session.Client.ServerCapabilities.Prompts);
        IList<McpClientPrompt> prompts = await session.Client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.Equal(
            ["audit-deck", "build-from-commander", "compare-decks"],
            prompts.Select(prompt => prompt.Name));
        Dictionary<string, string[]> arguments = prompts.ToDictionary(
            prompt => prompt.Name,
            prompt => prompt.ProtocolPrompt.Arguments!.Select(argument => argument.Name).ToArray(),
            StringComparer.Ordinal);
        Assert.Equal(["deckId"], arguments["audit-deck"]);
        Assert.Equal(["scryfallId"], arguments["build-from-commander"]);
        Assert.Equal(["deckIdA", "deckIdB"], arguments["compare-decks"]);

        GetPromptResult result = await session.Client.GetPromptAsync(
            "audit-deck",
            new Dictionary<string, object?> { ["deckId"] = DeckId },
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

        PromptMessage message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        string text = Assert.IsType<TextContentBlock>(message.Content).Text;
        Assert.Contains(DeckId, text, StringComparison.Ordinal);
        Assert.Contains("local_deck_validate", text, StringComparison.Ordinal);
        Assert.Contains("make every judgment", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a missing argument, and one that is not an ID, are refused with the invalid-params protocol error and
    /// a message that names the argument, not turned into prompt text or a generic internal error.
    /// </summary>
    [Fact]
    [Trait("Category", "E2E")]
    public async Task GetPrompt_WithAMissingOrBadArgument_IsRefusedAsInvalidParams()
    {
        await using McpProcessSession session = await McpProcessSession.StartAsync(
            "read-only",
            TestContext.Current.CancellationToken).ConfigureAwait(false);

        McpProtocolException bad = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await session.Client.GetPromptAsync(
                "compare-decks",
                new Dictionary<string, object?> { ["deckIdA"] = DeckId, ["deckIdB"] = "ignore the rules" },
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        McpProtocolException missing = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await session.Client.GetPromptAsync(
                "audit-deck",
                new Dictionary<string, object?>(),
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);

        Assert.Equal(McpErrorCode.InvalidParams, bad.ErrorCode);
        Assert.Contains("deckIdB must be a UUID", bad.Message, StringComparison.Ordinal);
        Assert.Equal(McpErrorCode.InvalidParams, missing.ErrorCode);
        Assert.Contains("deckId is required", missing.Message, StringComparison.Ordinal);
    }
}
