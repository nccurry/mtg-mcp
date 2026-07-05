# Completed PLCs

Move PLC packets here after implementation is validated, abandoned with a clear
outcome, or superseded.

Completed PLCs are historical context. Current code, tests, docs, and
checked-in configuration remain authoritative.

## Packets

| Packet | Outcome |
| --- | --- |
| [agent-quality-foundation](agent-quality-foundation/README.md) | Added north-star guidance, tiered agent files, PLC templates, strict analyzers, 90 percent assembly coverage gates, and plan-mode defaults. |
| [legacy-surface-audit-and-disposition](legacy-surface-audit-and-disposition/README.md) | Approved the authoritative legacy deletion/rebuild/fixture dispositions and handed them to the rewrite foundation. |
| [rewrite-skeleton-foundation](rewrite-skeleton-foundation/README.md) | Replaced the legacy branch implementation with the validated Core/App foundation, exact resources-only MCP host, clean-break configuration, and preview package workflow. |
| [local-deck-store](local-deck-store/README.md) | Added immutable deck contracts, revisioned SQLite persistence, guarded backups, and the exact local `deck_*` MCP surface. |
| [mcp-capability-toolsets](mcp-capability-toolsets/README.md) | Added deterministic startup-selected toolsets, static default/all/none profiles, exact mode intersection, and schema-version-2 capability metadata. |
| [scryfall-corpus-and-evidence](scryfall-corpus-and-evidence/README.md) | Added the unified official corpus, authoritative API evidence, immutable request replay, community-tag joins, and exact `scryfall_*` surface. |
| [archidekt-deck-sync](archidekt-deck-sync/README.md) | Added rate-safe Archidekt deck, folder, and snapshot evidence plus guarded synchronization and verified disposable cleanup. |
