# MCP Compatibility

This document records the client/protocol paths that are tested for this
branch. It is intentionally conservative: entries below are compatibility
evidence, not promises about clients that have not been exercised.

## Tested Clients

| Client | Transport | SDK/protocol package | Coverage | Status |
| --- | --- | --- | --- | --- |
| `ModelContextProtocol` .NET client | stdio | `1.4.0` | initialize, `tools/list`, representative `tools/call`, structured content, structured errors, `resources/list`, resource reads, `prompts/list`, and `logging/setLevel` | CI/local E2E |

The E2E tests use fake Scryfall and Archidekt HTTP backends, so normal test runs
do not require network access or mutate real Archidekt decks.

## Current Support Level

The server is still pre-1.0. Minor versions may change MCP tool names,
parameters, resources, prompts, and result shapes according to
[`docs/versioning.md`](versioning.md).

Before `1.0.0`, the compatibility bar is:

- every supported client row in this document is green in CI
- `task surface:report` matches the documented public surface
- `task smoke:mcp` confirms stdio framing and server metadata
- deprecation windows from the 0.x release train are complete

Additional client rows should name the client, transport, package or binary
version, and the exact MCP operations covered.

## `0.9.0` Rewrite Boundary

The evidence-first rewrite is an explicitly approved clean break. It does not
carry legacy tools, prompts, resources, mode names, configuration, or data
formats through compatibility aliases or automatic migration. During the
rewrite, compatibility testing proves MCP protocol behavior and the approved
new manifest; it does not require the 118-tool legacy surface to remain.

The ordinary pre-1.0 policy above still governs maintenance releases of the
current server. See [rewrite-guide.md](rewrite-guide.md) for routing.
