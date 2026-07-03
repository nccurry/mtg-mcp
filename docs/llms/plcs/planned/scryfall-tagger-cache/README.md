# Scryfall Tagger Cache PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/scryfall-tagger-cache/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines a deterministic local `tagger.db` read model and a separate
explicit acquisition workflow for Scryfall Tagger's unsupported HTML/GraphQL
contract. Cached reads never use the network. Refresh is sequential, paced at
one request per second, capped at 100 Oracle IDs, bounded across known paper
printings, and stops immediately on 403 or 429.

The MCP returns actual cached community tag assignments and provenance. It
does not map them to deck categories; the calling LLM may use ordinary
`deck_category_*` tools for that choice.

## Dependencies

- [Local Deck Store](../local-deck-store/README.md)
- [Scryfall Evidence Snapshots](../scryfall-evidence-snapshots/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Keep cache reads and acquisition as separate services/tools. | Proposed | Deterministic reads must never cause unsupported traffic. |
| Key card assignment snapshots by Oracle ID. | Proposed | Functional card identity spans printings. |
| Preserve direct, ancestor, tag type, accepted/rejected, and raw association evidence. | Proposed | Consumers can distinguish what Tagger actually said. |
| Use only printing facts from an explicit Scryfall snapshot. | Proposed | Tagger does not depend on hidden live Scryfall calls. |
| Enable refresh conservatively, with non-configurable upper safety bounds. | Proposed | Personal-use acquisition remains polite by default. |
| Trip a process circuit breaker on 403/429. | Proposed | The server must not retry through provider refusal. |

## Policy Notice

Tagger's current `robots.txt` says `Allow: /`, but that is not API endorsement
or permission to impose load. Acquisition remains unsupported and subject to
Scryfall's terms and “undue burden” restriction. A contract or policy change
disables refresh until this packet is reviewed; existing cache reads remain
available.

## Provider Risk Acceptance

- Risk: refresh uses unsupported Tagger HTML, CSRF/session behavior, and an
  undocumented GraphQL operation rather than an endorsed public API.
- Required decision: repository owner accepts the permission, stability, and
  maintenance risk despite conservative pacing, bounds, and fail-closed drift.
- Status: Required; not yet accepted
- Accepted by: Not accepted
- Acceptance date/revision: Not accepted

Cache-only reads do not require this exception, but acquisition implementation
and packet approval remain blocked until the owner records acceptance.

## Current-State Disposition

The current curated tag catalog and Scryfall `otag:` searches do not provide
complete per-card Tagger assignments. They are reference evidence and possible
fixture inputs only; this child does not reuse their classification abstraction.
There is currently no production HTML/CSRF/GraphQL acquisition path to preserve.

## Guardrail Conformance

This child returns exact cached community assignments and explicit acquisition
status. It does not infer semantic roles, map tags to deck categories, refresh
implicitly, or make a deckbuilding decision. It owns only `tagger.db`, keeps
unsupported provider transport out of Core, and uses the program operation modes.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
