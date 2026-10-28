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
    /// Removes implicit logging and dynamic list claims (tools, prompts, and resources) from a capability result.
    /// Two shapes carry capabilities: the initialize result, which has a top-level <c>serverInfo</c>, and the
    /// <c>server/discover</c> result of the pinned protocol, which has <c>supportedVersions</c> and keeps the server
    /// information in <c>_meta</c>. The server registers everything at start and sends no list-changed notification.
    /// </summary>
    internal static void RemoveUnsupportedImplicitCapabilities(JsonNode? result)
    {
        if (result is not JsonObject resultObject ||
            resultObject["capabilities"] is not JsonObject capabilities ||
            !IsCapabilityResult(resultObject))
        {
            return;
        }

        capabilities.Remove("logging");
        foreach (string name in new[] { "tools", "prompts", "resources" })
        {
            if (capabilities[name] is JsonObject capability)
            {
                capability.Remove("listChanged");
            }
        }
    }

    /// <summary>
    /// Tells whether a result is an initialize result or a discover result.
    /// </summary>
    private static bool IsCapabilityResult(JsonObject result)
    {
        return result["serverInfo"] is JsonObject ||
            result["supportedVersions"] is JsonArray ||
            result["_meta"] is JsonObject meta && meta["io.modelcontextprotocol/serverInfo"] is JsonObject;
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
