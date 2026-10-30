using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace MtgMcp.E2E.Tests;

/// <summary>
/// Calls the local deck write tools with <c>returnDeck</c> set, and hands back the whole deck as the data of the
/// result, for tests whose job is to inspect the deck after a write rather than the write summary.
/// </summary>
internal static class WholeDeck
{
    /// <summary>
    /// Names the tools whose default result is a summary and that take <c>returnDeck</c>.
    /// </summary>
    private static readonly HashSet<string> Tools = new(StringComparer.Ordinal)
    {
        "local_deck_create",
        "local_deck_apply_changes",
        "local_deck_identity_reconcile_apply",
    };

    /// <summary>
    /// Adds <c>returnDeck: true</c> to the arguments of a deck write tool and leaves other calls alone.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> Prepare(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        if (!Tools.Contains(toolName))
        {
            return arguments;
        }

        Dictionary<string, object?> sent = new(arguments, StringComparer.Ordinal) { ["returnDeck"] = true };
        return sent;
    }

    /// <summary>
    /// Replaces the data of a successful deck write result with the deck it carries; any other result is returned as is.
    /// </summary>
    internal static JsonElement Unwrap(string toolName, JsonElement result)
    {
        if (!Tools.Contains(toolName) ||
            !string.Equals(result.GetProperty("kind").GetString(), "success", StringComparison.Ordinal))
        {
            return result;
        }

        JsonNode node = JsonNode.Parse(result.GetRawText())!;
        node["data"] = node["data"]!["deck"]!.DeepClone();
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Calls a deck write tool with <c>returnDeck: true</c> and returns its successful data, which is the deck.
    /// </summary>
    internal static async Task<JsonElement> SuccessAsync(McpProcessSession session, string toolName, object arguments)
    {
        JsonElement result = await CallAsync(session, toolName, arguments).ConfigureAwait(false);
        Assert.Equal("success", result.GetProperty("kind").GetString());
        return result.GetProperty("data");
    }

    /// <summary>
    /// Calls a deck write tool with <c>returnDeck: true</c>. A success has its data replaced by the deck; any other
    /// result is returned as sent.
    /// </summary>
    internal static async Task<JsonElement> CallAsync(McpProcessSession session, string toolName, object arguments)
    {
        Dictionary<string, object?> raw = JsonSerializer.SerializeToElement(arguments)
            .EnumerateObject()
            .ToDictionary(value => value.Name, value => (object?)value.Value.Clone(), StringComparer.Ordinal);
        CallToolResult call = await session.Client.CallToolAsync(
            toolName,
            Prepare(toolName, raw),
            cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        JsonElement structured = Assert.IsType<JsonElement>(call.StructuredContent);
        bool success = string.Equals(structured.GetProperty("kind").GetString(), "success", StringComparison.Ordinal);
        Assert.Equal(!success, call.IsError == true);
        return Unwrap(toolName, structured);
    }
}
