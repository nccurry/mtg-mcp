using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MtgMcp.Core;

namespace MtgMcp.App;

/// <summary>
/// Maps known App-boundary failures to structured MCP tool errors.
/// </summary>
public static class McpErrorMapping
{
    /// <summary>
    /// Serializes structured error payloads with the same casing as MCP tool results.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates the call-tool filter that converts known exceptions into coded tool errors.
    /// </summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> CreateCallToolFilter()
    {
        return next => async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationModeBlockedException exception)
            {
                return CreateErrorResult(
                    "operation-mode-blocked",
                    exception.Message,
                    retriable: false,
                    new
                    {
                        Tool = exception.ToolName,
                        CurrentMode = exception.CurrentMode,
                        RequiredMode = exception.RequiredMode
                    },
                    $"Restart the server with MTGMCP__OPERATION_MODE={exception.RequiredMode} to allow this tool.");
            }
            catch (ArgumentException exception)
            {
                return CreateErrorResult(
                    "validation",
                    exception.Message,
                    retriable: false,
                    new
                    {
                        Tool = request.Params?.Name,
                        Parameter = exception.ParamName
                    },
                    "Adjust the tool arguments and retry.");
            }
            catch (InvalidOperationException exception)
            {
                return CreateErrorResult(
                    "conflict",
                    exception.Message,
                    retriable: false,
                    new
                    {
                        Tool = request.Params?.Name
                    },
                    null);
            }
        };
    }

    /// <summary>
    /// Builds an MCP tool-error result with text and structured content.
    /// </summary>
    private static CallToolResult CreateErrorResult(
        string code,
        string message,
        bool retriable,
        object? details,
        string? hint)
    {
        string safeMessage = SecretRedactor.Redact(message);
        McpToolErrorEnvelope envelope = new(new McpToolError(
            code,
            safeMessage,
            retriable,
            details,
            hint));

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = safeMessage }],
            StructuredContent = JsonSerializer.SerializeToElement(envelope, JsonOptions)
        };
    }
}

/// <summary>
/// Wraps a structured MCP tool error under the top-level error key.
/// </summary>
public sealed record McpToolErrorEnvelope(McpToolError Error);

/// <summary>
/// Describes a machine-readable MCP tool error.
/// </summary>
public sealed record McpToolError(
    string Code,
    string Message,
    bool Retriable,
    object? Details,
    string? Hint);
