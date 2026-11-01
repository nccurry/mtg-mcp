# Completed PLCs

Move PLC packets here after implementation is validated, abandoned with a clear
outcome, or superseded.

Completed PLCs are historical context. Current code, tests, docs, and
checked-in configuration remain authoritative.

Some historic packet names and quoted records use `corpus`. In those records,
it means the installed full Scryfall card data. Current guidance uses “card
data” instead.

## Packets

| Packet | Outcome |
| --- | --- |
| [local-deck-agent-safety](local-deck-agent-safety/README.md) | Reports adds of a card already in a deck and skips them safely for retries, settles identical duplicate copies in the duplicates preview, and adds card-name and concise reads to local_deck_get. Released as 0.13.3 on 2026-11-01 (0.13.2 tagged, not published). |
| [deck-read-and-search-clarity](deck-read-and-search-clarity/README.md) | Fixed tools not loading in Claude Code under MCP protocol 2026-07-28 and four live sync reports: Archidekt reads follow includedInDeck, default to concise entries with zones and deck facts; pushes resolve printings by exact name and set, refuse conflicts and large unconfirmed removals, and handle rate limits; pulls keep local additions; local deck pages carry deck-wide totals; Scryfall pages report counts. Released as 0.13.1 on 2026-10-31. |
| [agent-feedback-fixes](agent-feedback-fixes/README.md) | Fixed the problems agents reported with 0.12.0: deck write tools return summaries, every large result pages under 40,000 UTF-8 bytes, previews say when apply is unavailable, category order no longer shows false differences, names resolve at add time, and an offline test holds every tool under the size ceiling. Released as 0.13.0 on 2026-10-30. |
| [focused-agent-tool-surface](focused-agent-tool-surface/README.md) | Focused the default tool surface (merged, renamed, and default-on toolsets), added card list tools, Commander rule facts, castability, caller-ruled simulation, and MCP prompts, and fixed protocol, Archidekt sync, schema, and input bugs; released as 0.12.0 on 2026-10-29 (0.11.0 never published). EVAL-002 deferred. |
| [deck-mana-on-curve-simulation](deck-mana-on-curve-simulation/README.md) | Added one read-only, fact-backed deck mana and on-curve estimate with caller land rules, repeatable sampling, clear limits, broad validation, and a Release benchmark baseline. |
| [evidence-first-deckbuilding-evolution](evidence-first-deckbuilding-evolution/README.md) | Completed ownership cleanup, the current toolchain update, Commander Spellbook evidence, official-tag repair, source decisions, and the bounded on-curve estimate. |
| [commander-spellbook-evidence](commander-spellbook-evidence/README.md) | Added an opt-in Commander Spellbook adapter with three bounded, read-only evidence tools, a short-lived local cache, a live check, and package smoke coverage. |
| [agent-quality-foundation](agent-quality-foundation/README.md) | Added north-star guidance, tiered agent files, PLC templates, strict analyzers, 90 percent assembly coverage gates, and plan-mode defaults. |
| [legacy-surface-audit-and-disposition](legacy-surface-audit-and-disposition/README.md) | Approved the authoritative legacy deletion/rebuild/fixture dispositions and handed them to the rewrite foundation. |
| [rewrite-skeleton-foundation](rewrite-skeleton-foundation/README.md) | Replaced the legacy branch implementation with the validated Core/App foundation, exact resources-only MCP host, clean-break configuration, and preview package workflow. |
| [local-deck-store](local-deck-store/README.md) | Added immutable deck contracts, revisioned SQLite persistence, guarded backups, and the exact local `deck_*` MCP surface. |
| [manual-deck-interchange](manual-deck-interchange/README.md) | Added lossless native interchange plus deterministic generic, Archidekt, and Moxfield artifacts with accepted preservation limits and verified cleanup. |
| [mcp-capability-toolsets](mcp-capability-toolsets/README.md) | Added deterministic startup-selected toolsets, static default/all/none profiles, exact mode intersection, and schema-version-2 capability metadata. |
| [scryfall-corpus-and-evidence](scryfall-corpus-and-evidence/README.md) | Added unified official card data, authoritative API evidence, immutable request replay, community-tag joins, and the exact `scryfall_*` tools. |
| [archidekt-deck-sync](archidekt-deck-sync/README.md) | Added rate-safe Archidekt deck, folder, and snapshot evidence plus guarded synchronization and verified disposable cleanup. |
| [playgroup-public-api](playgroup-public-api/README.md) | Added every pinned official Playgroup operation, lossless provider evidence, exact mode guards, conservative pacing, and fixture-only write safety. |
| [mcp-contract-and-adapter-hardening](mcp-contract-and-adapter-hardening/README.md) | Added truthful capability/schema contracts, exact-only deck identity reconciliation, and behavior-preserving provider ownership boundaries. |
| [exact-deck-statistics](exact-deck-statistics/README.md) | Added eight exact, provider-independent statistics tools over caller-defined raw or revisioned deck populations with no legality or recommendation behavior. |
| [deterministic-deck-categorization](deterministic-deck-categorization/README.md) | Added explicit inline/common-v1 tag rules, evidence-bound category preview/apply, hierarchy and primary handling, and deterministic local deck mutations. |
| [evidence-first-mcp-rewrite-program](evidence-first-mcp-rewrite-program/README.md) | Approved all twelve rewrite packets and their shared guardrails; implementation and release lifecycle remain child-owned. |
| [rewrite-stabilization-cutover](rewrite-stabilization-cutover/README.md) | Validated, packaged, and released the clean-break `0.9.0` surface with rollback and provider evidence. |
| [latest-mcp-and-toolchain](latest-mcp-and-toolchain/README.md) | Pinned the current MCP protocol and toolchain, added reproducible locks, SHA-pinned actions, and a managed registry publisher. |
| [official-scryfall-tag-grouping-reliability](official-scryfall-tag-grouping-reliability/README.md) | Repaired deck grouping from installed official Scryfall tags: exact source IDs, all-parent ancestry, strict rule validation, and the in-place `common-v1` correction. |

## Superseded Reference Packets

These packets were drafted for the legacy server and retired by the rewrite
without implementation. They moved here on 2026-10-27 and remain reference
evidence only.

| Packet | Outcome |
| --- | --- |
| [card-snapshot-integrity](card-snapshot-integrity/README.md) | Superseded; known-empty/unknown and root/face principles are owned by local deck and Scryfall packets. |
| [deck-count-contracts](deck-count-contracts/README.md) | Superseded; zone-based storage and exact summaries remove the category-count root cause. |
| [land-entry-classification](land-entry-classification/README.md) | Reference-only; exact statistics uses caller-supplied source masks and does not parse oracle text. |
| [mcp-trust-evidence](mcp-trust-evidence/README.md) | Absorbed; evidence vocabulary is owned by the rewrite foundation, provider, and statistics packets. |
| [provider-evidence-workflows](provider-evidence-workflows/README.md) | Absorbed; provider principles are owned by the Scryfall, Archidekt, and Playgroup packets. |
| [configurable-decision-models](configurable-decision-models/README.md) | Retired post-cutover experimental seed; never stable scope. |
| [simulation-profile-evidence](simulation-profile-evidence/README.md) | Retired post-cutover simulation fixture input; never stable scope. |
| [stats-lab-interaction-readiness](stats-lab-interaction-readiness/README.md) | Retired post-cutover heuristic sequencing taxonomy; never stable scope. |
| [conservative-goldfish-v2](conservative-goldfish-v2/README.md) | Retired post-cutover goldfish kernel seed; server-side play decisions are out of scope. |
