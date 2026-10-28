using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MtgMcp.App.Hosting;

/// <summary>
/// Applies the foundation's narrower advertised protocol surface to SDK responses.
/// </summary>
internal static class FoundationProtocolPolicy
{
    /// <summary>
    /// Removes SDK capabilities that the static foundation surface does not support.
    /// </summary>
    internal static McpMessageFilter OmitUnsupportedImplicitCapabilities()
    {
        return next => async (context, cancellationToken) =>
        {
            RemoveUnsupportedImplicitCapabilities(
                (context.JsonRpcMessage as JsonRpcResponse)?.Result);
            CanonicalizeToolList(
                (context.JsonRpcMessage as JsonRpcResponse)?.Result);
            CanonicalizePromptList(
                (context.JsonRpcMessage as JsonRpcResponse)?.Result);

            await next(context, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Removes implicit logging and dynamic tool-list and prompt-list claims from an initialization-shaped result.
    /// </summary>
    internal static void RemoveUnsupportedImplicitCapabilities(JsonNode? result)
    {
        if (result is JsonObject resultObject &&
            resultObject["serverInfo"] is JsonObject &&
            resultObject["capabilities"] is JsonObject capabilities)
        {
            capabilities.Remove("logging");
            if (capabilities["tools"] is JsonObject tools)
            {
                tools.Remove("listChanged");
            }

            if (capabilities["prompts"] is JsonObject prompts)
            {
                prompts.Remove("listChanged");
            }
        }
    }

    /// <summary>
    /// Sorts a tools-list result by exact name so discovery is stable across reflection order.
    /// </summary>
    internal static void CanonicalizeToolList(JsonNode? result)
    {
        SortByName(result, "tools");
    }

    /// <summary>
    /// Sorts a prompts-list result by exact name so discovery is stable across reflection order.
    /// </summary>
    internal static void CanonicalizePromptList(JsonNode? result)
    {
        SortByName(result, "prompts");
    }

    /// <summary>
    /// Sorts the named array of a list result by each item's exact name; an item with no name leaves it unchanged.
    /// </summary>
    private static void SortByName(JsonNode? result, string property)
    {
        if (result is not JsonObject resultObject ||
            resultObject[property] is not JsonArray items)
        {
            return;
        }

        List<(string Name, JsonNode Item)> sorted = [];
        foreach (JsonNode? item in items)
        {
            if (item is not JsonObject itemObject ||
                itemObject["name"] is not JsonValue nameValue ||
                !nameValue.TryGetValue(out string? name) ||
                string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            sorted.Add((name, item));
        }

        sorted.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        items.Clear();
        foreach ((string _, JsonNode item) in sorted)
        {
            items.Add(item);
        }
    }
}
