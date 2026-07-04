# MCP Capability Toolsets PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/mcp-capability-toolsets/`
- Owner: mtg-mcp
- Created: 2026-07-04
- Last updated: 2026-07-04
- Current phase: Phase 4 complete

## Summary

This packet keeps the model-visible MCP surface manageable as stable
capabilities grow. It adds static startup-selected capability toolsets while
preserving `read-only`, `local`, and `remote` as independent authority modes.
Every tool remains typed and belongs to exactly one toolset; no generic router,
runtime surface mutation, or compatibility alias is introduced.

## Dependencies

- [AMEND-003 program guardrail](../../in-progress/evidence-first-mcp-rewrite-program/README.md#program-amendments)
- [Completed rewrite foundation](../../completed/rewrite-skeleton-foundation/README.md)
- [Completed local deck store](../../completed/local-deck-store/README.md)
- [Manual deck interchange](../../in-progress/manual-deck-interchange/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Toolsets control relevance; modes control authority. | Accepted | Selection must never grant write permission. |
| Resolve selection at startup and keep it static for the MCP session. | Accepted | Deterministic discovery works across clients without relying on list-change support. |
| Support `default`, `all`, `none`, or an explicit comma-separated list. | Accepted | Covers ordinary, complete, health-only, and tailored sessions without per-tool configuration. |
| Keep toolset registration in App and out of Core. | Accepted | Toolsets are host composition metadata, not domain evidence. |
| Replace capability `modules` with a versioned toolset projection. | Accepted | Avoids two overlapping module/toolset inventories. |
| Do not add per-tool allowlists in this slice. | Accepted | One capability grouping mechanism is enough for the stable rewrite. |

## Public Configuration

- CLI: `--toolsets decks` or `--toolsets=decks`
- Environment: `MTGMCP__TOOLSETS=decks`
- JSON: `"TOOLSETS": "decks"`
- Omitted value: `default`

Stable names are `decks`, `scryfall`, `stats`, `archidekt`, `playgroup`, and
`tagger`. Only implemented descriptors are accepted and reported. `default`,
`all`, and `none` are reserved selections and cannot be mixed with explicit
names.

The examples use the only currently implemented descriptor. Later children
make their own names selectable when their tool registrations land.

## North-Star Acceptance

- Player outcome: an LLM sees the smallest coherent surface needed for its
  current deckbuilding workflow while complete stable capabilities remain
  explicitly selectable.
- Evidence class: configuration and capability metadata, not card or deck
  evidence.
- Determinism boundary: the same build, configuration, and operation mode
  produce the same ordered tool discovery and capability document.
- Unknown states: unknown, duplicate, mixed-reserved, or unimplemented names
  fail startup explicitly; missing provider credentials do not silently change
  selection.
- Decision boundary: the server filters surface relevance but makes no
  deckbuilding decision.
- Representative workflow: initialize with the default profile, inspect the
  capability resource, manage/import a deck, and observe no unrelated provider
  tools; restart with `all` to discover the complete stable surface.

## Guardrail Conformance

This packet changes only App configuration, registration, capability metadata,
tests, and documentation. It preserves all evidence boundaries, operation-mode
guards, dependency directions, offline tests, and the clean `0.9.0` break.

## Planning Approval

- Status: Approved
- Reviewed by: Nick Curry, repository owner
- Review date: 2026-07-04
- Reviewed revision: `16c395e`
- Implementation authorized: Yes

## Completion Evidence

- The App owns one explicit descriptor registry. All 23 current tools belong
  exactly once to `decks`; Core has no MCP or toolset dependency.
- `default`, `all`, explicit `decks`, and `none` reconcile exactly with
  official-client discovery in all three operation modes. The current surface
  is 7/23/23 for enabled profiles and 0/0/0 for `none`.
- Capability schema version 2 reports selection, relevance/authority boundary,
  availability, stability, enablement, defaults, counts, and descriptions.
- Source and installed-package MCP smoke tests run the disposable Commander
  workflow and prove `none` avoids creating the deck data root.
- All 160 deterministic offline tests pass. Coverage is App 97.12%, Core 100%,
  and Decks 94.29%; lint, surface, package, process smoke, MCP smoke, and
  installed-tool smoke gates pass.
- Abstraction, code-quality, visual, dead-code, dependency, test-coverage,
  test-quality, and docs-sync audits have no remaining findings. NuGet reports
  no vulnerable, deprecated, or outdated packages.

Later children add one descriptor and explicit registration group. They do not
redesign toolset parsing, mode authority, or static-session semantics.
