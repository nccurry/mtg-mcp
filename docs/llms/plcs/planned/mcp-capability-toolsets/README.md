# MCP Capability Toolsets PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/mcp-capability-toolsets/`
- Owner: mtg-mcp
- Created: 2026-07-04
- Last updated: 2026-07-04
- Current phase: Planning review

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
| Toolsets control relevance; modes control authority. | Proposed | Selection must never grant write permission. |
| Resolve selection at startup and keep it static for the MCP session. | Proposed | Deterministic discovery works across clients without relying on list-change support. |
| Support `default`, `all`, `none`, or an explicit comma-separated list. | Proposed | Covers ordinary, complete, health-only, and tailored sessions without per-tool configuration. |
| Keep toolset registration in App and out of Core. | Proposed | Toolsets are host composition metadata, not domain evidence. |
| Replace capability `modules` with a versioned toolset projection. | Proposed | Avoids two overlapping module/toolset inventories. |
| Do not add per-tool allowlists in this slice. | Proposed | One capability grouping mechanism is enough for the stable rewrite. |

## Public Configuration

- CLI: `--toolsets decks,scryfall` or `--toolsets=decks,scryfall`
- Environment: `MTGMCP__TOOLSETS=decks,scryfall`
- JSON: `"TOOLSETS": "decks,scryfall"`
- Omitted value: `default`

Stable names are `decks`, `scryfall`, `stats`, `archidekt`, `playgroup`, and
`tagger`. Only implemented descriptors are accepted and reported. `default`,
`all`, and `none` are reserved selections and cannot be mixed with explicit
names.

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

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No

## Completion Notes

Implementation must land before Scryfall or any later capability child starts.
Later children add one descriptor and registration group; they do not redesign
toolset parsing or mode semantics.
