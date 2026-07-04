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

            await next(context, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Removes implicit logging and dynamic tool-list claims from an initialization-shaped result.
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
        }
    }

    /// <summary>
    /// Sorts a tools-list result by exact name so discovery is stable across reflection order.
    /// </summary>
    internal static void CanonicalizeToolList(JsonNode? result)
    {
        if (result is not JsonObject resultObject ||
            resultObject["tools"] is not JsonArray tools)
        {
            return;
        }

        List<(string Name, JsonNode Tool)> sorted = [];
        foreach (JsonNode? tool in tools)
        {
            if (tool is not JsonObject toolObject ||
                toolObject["name"] is not JsonValue nameValue ||
                !nameValue.TryGetValue(out string? name) ||
                string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            sorted.Add((name, tool));
        }

        sorted.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        tools.Clear();
        foreach ((string _, JsonNode tool) in sorted)
        {
            tools.Add(tool);
        }
    }
}
