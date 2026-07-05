# MCP Compatibility

This document records compatibility evidence for the current rewrite branch.
It does not carry the released legacy surface forward.

## Current Rewrite Status

The completed foundation, local deck capability, offline interchange surface,
unified Scryfall evidence capability, and opt-in Archidekt capability
use official C# SDK 1.4.0 stdio hosting and client APIs. Official-client E2E tests prove standard initialization in `read-only`,
`local`, and `remote` modes, negotiated protocol reporting, resource listing
and reading, unknown-resource errors, sanitized pre-transport failures, and
clean process termination when stdin closes.

The current public surface is exactly:

- server name `io.github.nccurry/mtg-mcp`, title `mtg-mcp`, and the evaluated
  package version;
- one static `application/json` resource at `mtg://server/capabilities`;
- a default profile of 21/41/41 tools and complete `all` profile of 32/53/64
  tools by mode, plus zero prompts;
- static `default`, `all`, `none`, and explicit implemented-toolset selection,
  with capability schema version 4; and
- no logging, subscription, or list-changed capability advertisement.

`task smoke:process` is only a one-shot startup/configuration probe.
`task smoke:mcp` establishes a real MCP session, reads the resource, verifies
the deck, Scryfall, and Archidekt schemas/annotations, and runs local workflows, while
`task release:tool-smoke` repeats both checks against the installed package.
`task surface:report` enforces the exact source registration boundary.

## `0.9.0` Rewrite Boundary

The evidence-first rewrite is an approved clean break. It does not carry legacy
tools, prompts, resources, mode names, configuration, or data formats through
compatibility aliases or automatic migration. Compatibility testing will prove
the approved new manifest, not preservation of the historical 118-tool surface.

Maintenance of released legacy versions follows [Versioning](versioning.md).
Rewrite work follows the [Rewrite Guide](rewrite-guide.md) and active child PLC.
