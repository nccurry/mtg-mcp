using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MtgMcp.App.Tests;

/// <summary>
/// Records every invalid-input result that names no argument path, so a test run can prove that each refusal of a
/// caller's value says which value it refused. A test adds <see cref="Filter"/> to the call filters of the server it
/// runs, ahead of the server's own filter, so the audit sees the final result.
/// </summary>
internal sealed class InvalidInputPathAudit
{
    /// <summary>
    /// Holds a description of each result found without a path, in the order they were seen.
    /// </summary>
    private readonly ConcurrentQueue<string> findings = new();

    /// <summary>
    /// Holds the tool name prefixes that are checked, or none to check every tool.
    /// </summary>
    private readonly string[] prefixes;

    /// <summary>
    /// Creates an audit of the tools whose names start with one of the prefixes, or of every tool when none is given.
    /// </summary>
    internal InvalidInputPathAudit(params string[] toolPrefixes)
    {
        prefixes = toolPrefixes;
    }

    /// <summary>
    /// Gets the tool and reason code of each invalid-input result seen without a path.
    /// </summary>
    internal IReadOnlyList<string> Findings => [.. findings];

    /// <summary>
    /// Wraps the tool call pipeline so every result is observed after the inner pipeline made it.
    /// </summary>
    internal McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (context, cancellationToken) =>
        {
            CallToolResult result = await next(context, cancellationToken).ConfigureAwait(false);
            Observe(context.Params?.Name ?? string.Empty, result);
            return result;
        };
    }

    /// <summary>
    /// Records the result of one tool call when it is an invalid-input result with no path.
    /// </summary>
    internal void Observe(string toolName, CallToolResult result)
    {
        if (prefixes.Length > 0 && !prefixes.Any(prefix => toolName.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return;
        }

        if (result.StructuredContent is not { ValueKind: JsonValueKind.Object } body ||
            !body.TryGetProperty("kind", out JsonElement kind) ||
            kind.GetString() != "invalid-input")
        {
            return;
        }

        bool hasPath = body.TryGetProperty("path", out JsonElement path) &&
            path.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(path.GetString());
        if (!hasPath)
        {
            string reason = body.TryGetProperty("reasonCode", out JsonElement code) ? code.GetString() ?? "?" : "?";
            findings.Enqueue($"{toolName}: {reason}");
        }
    }
}
