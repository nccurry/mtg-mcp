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
| Use Archidekt's currently available web API as an observed, replaceable contract. | Accepted | There is no stable public specification; adapter drift is normal maintenance rather than a compatibility promise. |
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

## Provider Contract And Risk Acceptance

- Risk: authenticated create, push, and delete use Archidekt's observed web API
  without a stable public specification. Archidekt's terms restrict automated
  requests, while current staff guidance discusses API use below the provider's
  request threshold.
- Decision: use the available API for explicit operations on the configured
  user's own decks, identify the client honestly, pace below current staff
  guidance, and update or disable the adapter when the contract changes.
- Status: Accepted for planning
- Accepted by: Nick Curry, repository owner
- Acceptance date: 2026-07-03
- Reviewed evidence: [Archidekt terms](https://archidekt.com/terms), current
  [staff rate-limit guidance](https://archidekt.com/forum/thread/19112643),
  current frontend routes, and the live probe below.

This acceptance resolves the product-boundary question. It does not approve
this PLC or authorize implementation.

## 2026-07-03 Contract Probe

Using the existing configured credential file, a redacted live probe:

1. authenticated successfully;
2. created a uniquely named private empty Commander deck with
   `POST /api/decks/v2/` (`201`);
3. read it back and verified its name and private flag (`200`);
4. deleted it with `DELETE /api/decks/{id}/` (`204`); and
5. verified that no probe deck remained in the authenticated deck listing.

A direct read of the deleted ID returned `400`, not `404`. Tests and runtime
classification must preserve that observed provider behavior and verify
absence through a fresh list/read contract rather than assuming REST status
semantics. No credential value, token, path, or persistent remote identifier is
part of the retained evidence.

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
