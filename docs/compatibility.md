# MCP Compatibility

This document records compatibility evidence for the current rewrite branch.
It does not carry the released legacy surface forward.

## Foundation Status

The completed foundation uses official C# SDK 1.4.0 stdio hosting and client
APIs. Official-client E2E tests prove standard initialization in `read-only`,
`local`, and `remote` modes, negotiated protocol reporting, resource listing
and reading, unknown-resource errors, sanitized pre-transport failures, and
clean process termination when stdin closes.

The public foundation surface is exactly:

- server name `io.github.nccurry/mtg-mcp`, title `mtg-mcp`, and the evaluated
  package version;
- one static `application/json` resource at `mtg://server/capabilities`;
- zero tools and zero prompts; and
- no logging, subscription, or list-changed capability advertisement.

`task smoke:process` is only a one-shot startup/configuration probe.
`task smoke:mcp` establishes a real MCP session and reads the resource, while
`task release:tool-smoke` repeats both checks against the installed package.
`task surface:report` enforces the exact source registration boundary.

## `0.9.0` Rewrite Boundary

The evidence-first rewrite is an approved clean break. It does not carry legacy
tools, prompts, resources, mode names, configuration, or data formats through
compatibility aliases or automatic migration. Compatibility testing will prove
the approved new manifest, not preservation of the historical 118-tool surface.

Maintenance of released legacy versions follows [Versioning](versioning.md).
Rewrite work follows the [Rewrite Guide](rewrite-guide.md) and active child PLC.
