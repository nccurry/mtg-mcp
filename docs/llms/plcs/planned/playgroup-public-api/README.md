# Playgroup Public API PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/playgroup-public-api/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet exposes every operation in Playgroup.gg Public API 1.0.0 as a
provider-shaped `playgroup_*` tool. The pinned 2026-07-03 OpenAPI 3.1 document
contains fifteen operations: thirteen reads and two writes. It contains no deck
update operation, so deck mutation is explicitly unsupported rather than
reverse-engineered.

| Surface category | Count |
| --- | ---: |
| Pinned provider operations | 15 |
| Provider GET/read tools | 13 |
| Provider POST/write tools | 2 |
| Local adapter-status tools | 1 (`playgroup_auth_status`) |
| **Registered `playgroup_*` tools** | **16** |

## Dependencies

- [Rewrite Foundation](../../in-progress/rewrite-skeleton-foundation/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Pin the official OpenAPI document and implement one typed tool per operation. | Proposed | Provider capability is explicit and drift is detectable. |
| Preserve provider-shaped facts rather than local ranking models. | Proposed | Playgroup observations must not become opaque quality scores. |
| Add no deck-update tool while the official API lacks one. | Proposed | Private endpoint reverse engineering is outside scope. |
| Gate event batch and live session creation behind `remote`. | Proposed | They mutate external state. |
| Never retry write requests automatically. | Proposed | Ambiguous provider acceptance could duplicate events/sessions. |
| Treat official contract drift as routine adapter maintenance. | Accepted | The goal is the best current API coverage, not compatibility with an obsolete schema. |

## Pinned Contract

- URL: `https://playgroup.gg/api/public/v1/openapi.yaml`
- Observed: 2026-07-03
- OpenAPI: 3.1.0
- API version: 1.0.0
- Bytes: 41,646
- SHA-256: `2996db9134045e255987dda80ec1110dc28d2a84f2705622833d2ab339cb7ad4`

The pinned OpenAPI document publishes no request-rate guidance. The 250 ms
serialized interval is a conservative client-owned default and must become
stricter if official guidance is later published.

## Live-Write Test Decision

The current official contract has two writes:

- batch-import events into an existing game; and
- create a live session.

It publishes no delete, undo, close-session, or event-removal operation. A live
write probe would therefore require changing a real game or leaving a session
whose cleanup cannot be verified through the same public API. For the pinned
contract, write behavior is fixture/contract tested only. This limitation is
accepted by Nick Curry, repository owner, on 2026-07-03 against the pinned
SHA-256 above. Safe authenticated reads may still receive opt-in live proof.

This is a test-safety decision, not a reason to omit the two documented remote
tools. If Playgroup later documents cleanup, ordinary reviewed adapter updates
may add disposable live-write tests.

## Guardrail Conformance

The adapter returns provider records, pagination, and retrieval metadata. It
does not infer power, rank deck quality, blend local meta scores, or silently
hydrate Archidekt decks.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
