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
    /// Removes the logging capability that SDK 1.4 adds to every initialization response.
    /// </summary>
    internal static McpMessageFilter OmitImplicitLoggingCapability()
    {
        return next => async (context, cancellationToken) =>
        {
            RemoveImplicitLoggingCapability(
                (context.JsonRpcMessage as JsonRpcResponse)?.Result);

            await next(context, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <summary>
    /// Removes only the implicit logging capability from an initialization-shaped result.
    /// </summary>
    internal static void RemoveImplicitLoggingCapability(JsonNode? result)
    {
        if (result is JsonObject resultObject &&
            resultObject["serverInfo"] is JsonObject &&
            resultObject["capabilities"] is JsonObject capabilities)
        {
            capabilities.Remove("logging");
        }
    }
}
