# MCP Compatibility

This document records compatibility evidence for the current rewrite branch.
It does not carry the released legacy surface forward.

## Foundation Status

Foundation Phase 2 has no MCP transport or public MCP surface. The only
executable contract is `mtg-mcp --smoke`, covered through a real process test.
Client, protocol, initialization, tools, resources, and prompts are therefore
not yet claimable on this branch.

Foundation Phase 4 will add the first compatibility row after mocked process
E2E tests prove standard initialization and the single capability resource.
Until then, `task surface:report` verifies that no legacy MCP registrations
remain rather than reporting an MCP inventory.

## `0.9.0` Rewrite Boundary

The evidence-first rewrite is an approved clean break. It does not carry legacy
tools, prompts, resources, mode names, configuration, or data formats through
compatibility aliases or automatic migration. Compatibility testing will prove
the approved new manifest, not preservation of the historical 118-tool surface.

Maintenance of released legacy versions follows [Versioning](versioning.md).
Rewrite work follows the [Rewrite Guide](rewrite-guide.md) and active child PLC.
