# Archidekt Essentials And Synchronization PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/archidekt-deck-sync/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines an isolated Archidekt adapter for essential deck lifecycle
and explicit synchronization. Local decks remain authoritative. Every pull or
push is previewed against fresh remote evidence and guarded by local revision,
remote fingerprint, and preview fingerprint. The adapter never writes through
ordinary local edits.

## Dependencies

- [Local Deck Store](../local-deck-store/README.md)
- [Manual Deck Interchange](../manual-deck-interchange/README.md)
- [Scryfall Evidence Snapshots](../scryfall-evidence-snapshots/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Treat Archidekt HTTP as an observed, unsupported private contract. | Proposed | There is no stable public API specification. |
| Keep local editing and remote mutation separate. | Proposed | Users must see the diff before provider changes. |
| Use fresh canonical remote fingerprints rather than assumed ETags. | Proposed | The observed contract does not guarantee concurrency validators. |
| Refuse automatic conflict resolution. | Proposed | The MCP must expose differences, not choose winners. |
| Default newly created remote decks to private. | Proposed | Least exposure for remote writes. |
| Exclude folders, snapshots/history, collaboration, social, and account administration. | Proposed | They are outside the essential cutover. |

## Public Surface

Reads/previews: `archidekt_auth_status`, `archidekt_deck_list`,
`archidekt_deck_get`, `archidekt_sync_diff`, `archidekt_pull_preview`, and
`archidekt_push_preview`.

Local apply: `archidekt_pull_apply`.

Remote apply: `archidekt_deck_create`, `archidekt_deck_delete`, and
`archidekt_push_apply`.

## Provider Risk Acceptance

- Risk: authenticated create, push, and delete use an observed, unsupported
  private Archidekt contract rather than an official public API.
- Required decision: repository owner accepts that contract drift or provider
  policy can disable the adapter and block cutover.
- Status: Required; not yet accepted
- Accepted by: Not accepted
- Acceptance date/revision: Not accepted

Planning review and implementation authorization remain blocked until this
record is explicitly accepted. Technical mitigations do not substitute for the
product-boundary decision.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No

## Guardrail Conformance

The adapter translates provider facts and performs explicitly authorized
workflow operations. It does not recommend changes, infer categories, or claim
atomic rollback when Archidekt partially accepts a multi-request update.
