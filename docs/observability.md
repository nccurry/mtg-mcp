# Observability

> Historical reference: this document describes the removed legacy host. The
> rewrite retains only its stdout/stderr and secret-redaction invariants; exact
> metric names and dimensions require approval in the owning App phase.

The legacy host used stdio for MCP traffic, so diagnostic text never mixed with
protocol traffic on stdout. The completed rewrite keeps that boundary by
clearing default console logging providers; stdout carries JSON-RPC only.
Broader telemetry remains future App work.

## Removed Legacy Telemetry

The legacy MCP host emitted one structured log template for each completed tool call.
The log includes:

- `ToolName`
- `Status` (`success`, `error`, or `exception`)
- elapsed milliseconds
- `detailLevel`, when the request supplied it
- exception type for unhandled exceptions

Tool arguments were not logged. The filter recorded only low-cardinality request
metadata so Archidekt credentials, API keys, decklist text, and other user
content do not appear in diagnostic output.

The same legacy host boundary exposed OpenTelemetry-ready primitives:

- Activity source: `MtgMcp.McpServer`
- Meter: `MtgMcp.McpServer`
- Counter: `mtg_mcp.tool.call.count`
- Histogram: `mtg_mcp.tool.call.duration` with unit `ms`

Metric dimensions are `tool.name`, `status`, optional `detail.level`, and
optional `error.type`.

## Removed Legacy Log Level

Legacy clients could call MCP `logging/setLevel`. The server mapped protocol logging
levels to `Microsoft.Extensions.Logging` levels and applied the threshold to
host-boundary diagnostics:

| MCP level | Host log level |
| --- | --- |
| `debug` | `Debug` |
| `info`, `notice` | `Information` |
| `warning` | `Warning` |
| `error` | `Error` |
| `critical`, `alert`, `emergency` | `Critical` |

`server_get_info` and `mtg://server/info` exposed the legacy
`mcpLoggingLevel` for client smoke tests and local diagnosis.

The legacy handler controlled server-side diagnostic emission. It did not
stream log notifications back to MCP clients.

## Verification

The current server does not advertise MCP logging, telemetry support, or
`server_get_info`. Any new observability work needs an App-owned PLC and must
keep stdout reserved for JSON-RPC.
