# MCP Compatibility

This document records compatibility evidence for the released `0.9.0` server.
It does not carry the released legacy API forward.

## Current `0.9.0` Status

The local deck store, manual deck-file support, Scryfall tools, Commander
Spellbook tools, and the Archidekt and Playgroup tools (on when their
credentials are present) use the official C# SDK
selected in `Directory.Packages.props`.
The server requires MCP `2026-07-28`; it does not support older
initialize-based clients. Official-client E2E tests prove current-protocol
connections in `read-only`, `local`, and `remote` modes, protocol reporting,
resource listing and reading, unknown-resource errors, sanitized pre-transport
failures, and clean process termination when stdin closes.

The current public MCP contract is:

- server name `io.github.nccurry/mtg-mcp`, title `mtg-mcp`, and the evaluated
  package version;
- one static `application/json` resource at `mtg://server/capabilities`;
- a default profile (the same as `all`) of 30/43/43 tools by mode with no
  provider credentials and 53/67/80 tools with Archidekt and Playgroup
  credentials, plus zero prompts, with the default mode `remote`;
- fixed `default`, `all`, `none`, and explicit toolset selection, with version 8
  of the capabilities-resource schema; and
- no logging, subscription, or changing-tool-list feature advertisement.

`task smoke:process` is only a one-shot startup/configuration probe.
`task smoke:mcp` establishes a real MCP session, reads the resource, verifies
the deck, Scryfall, Spellbook, Archidekt, and Playgroup schemas and annotations,
and runs local workflows. `task release:tool-smoke` repeats those checks against
the installed package.
`task install` adds a separate local-install check: after publishing the
self-contained command, it starts and stops a read-only server with the
Scryfall toolset enabled. This catches native SQLite and Scryfall startup
failures that `--smoke` cannot reach.
`task surface:report` checks that the source registers exactly those tools.

## `0.9.0` Rewrite Boundary

The evidence-first rewrite intentionally starts fresh. It does not carry legacy
tools, prompts, resources, mode names, configuration, or data formats forward
through compatibility aliases or automatic migration. Compatibility tests prove
the new tool list; they do not preserve the historical 118-tool server.

Maintenance of released legacy versions follows [Versioning](versioning.md).
The completed rewrite records remain useful design evidence. New work that
spans several modules or public APIs needs its own approved PLC; it does not
reopen a completed rewrite child.
