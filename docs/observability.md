# Observability

> Historical reference: this document describes the removed legacy host. The
> rewrite retains only its stdout/stderr and secret-redaction invariants; exact
> metric names and dimensions require approval in the owning App phase.

The legacy host used stdio for MCP traffic, so runtime diagnostics never wrote
protocol-adjacent text to stdout. The rewrite must preserve that boundary when
Foundation Phase 4 introduces stdio hosting.

## Tool Call Telemetry

The MCP host emits one structured log template for each completed tool call.
The log includes:

- `ToolName`
- `Status` (`success`, `error`, or `exception`)
- elapsed milliseconds
- `detailLevel`, when the request supplied it
- exception type for unhandled exceptions

Tool arguments are not logged. The filter records only low-cardinality request
metadata so Archidekt credentials, API keys, decklist text, and other user
content do not appear in diagnostic output.

The same host boundary also exposes OpenTelemetry-ready primitives:

- Activity source: `MtgMcp.McpServer`
- Meter: `MtgMcp.McpServer`
- Counter: `mtg_mcp.tool.call.count`
- Histogram: `mtg_mcp.tool.call.duration` with unit `ms`

Metric dimensions are `tool.name`, `status`, optional `detail.level`, and
optional `error.type`.

## Runtime Log Level

Clients can call MCP `logging/setLevel`. The server maps protocol logging
levels to `Microsoft.Extensions.Logging` levels and applies the threshold to
host-boundary diagnostics:

| MCP level | Host log level |
| --- | --- |
| `debug` | `Debug` |
| `info`, `notice` | `Information` |
| `warning` | `Warning` |
| `error` | `Error` |
| `critical`, `alert`, `emergency` | `Critical` |

`server_get_info` and `mtg://server/info` expose the current
`mcpLoggingLevel`, which is useful for client smoke tests and local diagnosis.

The current handler controls server-side diagnostic emission. It does not yet
stream log notifications back to MCP clients.

## Verification

The E2E stdio smoke path exercises `logging/setLevel` and confirms the updated
level is visible through `server_get_info`. Keep this check in place whenever
host logging, request filters, or the SDK version changes.
