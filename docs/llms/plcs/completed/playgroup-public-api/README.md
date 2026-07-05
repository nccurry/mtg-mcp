# Playgroup Public API PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/playgroup-public-api/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: Completed

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

- [Rewrite Foundation](../../completed/rewrite-skeleton-foundation/README.md)
- [MCP Capability Toolsets](../../completed/mcp-capability-toolsets/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Pin the official OpenAPI document and implement one typed tool per operation. | Accepted | Provider capability is explicit and drift is detectable. |
| Preserve provider-shaped facts rather than local ranking models. | Accepted | Playgroup observations must not become opaque quality scores. |
| Add no deck-update tool while the official API lacks one. | Accepted | Private endpoint reverse engineering is outside scope. |
| Gate event batch and live session creation behind `remote`. | Accepted | They mutate external state. |
| Never retry write requests automatically. | Accepted | Ambiguous provider acceptance could duplicate events/sessions. |
| Preserve provider payloads as detached JSON inside a typed evidence envelope. | Accepted | The pinned schemas contain broad nullable and additive provider records; lossless JSON keeps unknown fields and nulls without a second stale model hierarchy. Inputs, operations, metadata, and failure states remain typed. |
| Treat official contract drift as routine adapter maintenance. | Accepted | The goal is the best current API coverage, not compatibility with an obsolete schema. |

## Pinned Contract

- URL: `https://playgroup.gg/api/public/v1/openapi.yaml`
- Observed: 2026-07-03
- Reverified unchanged: 2026-07-04
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

## Toolset And North-Star Acceptance

- Toolset: `playgroup`, disabled by default and explicitly enabled by users who
  need this provider.
- Surface rule: the pinned public operations remain typed tools because their
  provider contracts differ; no private endpoint, generic router, or ranking
  alias is added.
- User question answered: what games, decks, users, commanders, playgroups, and
  provider-computed statistics does Playgroup currently report?
- Evidence type: provider-shaped observations with endpoint, API version,
  pagination, retrieval time, and limitations.
- Replay boundary: the pinned OpenAPI revision and captured response metadata
  identify the contract and observation; live provider state may later change.
- Unknown boundary: unsupported deck updates, missing credentials, partial
  pages, contract drift, and unavailable provider responses remain explicit.
- Decision boundary: the adapter never ranks deck quality or selects changes.
- Complete LLM workflow: enable Playgroup, inspect auth, fetch bounded provider
  evidence, correlate explicit IDs with user-supplied/local context, and let
  the client LLM explain the observed results.

## Planning Approval

- Status: Approved
- Reviewed by: Nick Curry
- Review date: 2026-07-04
- Reviewed revision: `bf62dfb`
- Implementation authorized: Yes, by the repository owner's explicit request to implement the next PLC on 2026-07-04

## Implementation Evidence

- The checked-in OpenAPI fixture is exactly 41,646 bytes with SHA-256
  `2996db9134045e255987dda80ec1110dc28d2a84f2705622833d2ab339cb7ad4`.
- All fifteen documented operations map one-to-one to typed `playgroup_*`
  tools; `playgroup_auth_status` makes sixteen total. Visibility is exactly
  14/14/16 in `read-only`/`local`/`remote`.
- Official-client tests verify discovery, input/output schemas, annotations,
  unsupported deck updates, redacted auth, missing-key behavior, and the
  current 46/67/80 complete-profile counts.
- Offline fixtures cover every route, provider pagination bounds, unknown and
  nullable fields, two-megabyte response limits, bearer headers, shared
  pacing, cancellation, transient reads, bounded `Retry-After`, terminal
  `401`/`403`, malformed data, and single-attempt writes.
- The safe live `/me` test is discoverable and opt-in. It was not executed for
  this closure because no Playgroup API key was configured. Both writes remain
  explicitly fixture-only and are not labeled live-tested because Public API
  1.0.0 has no documented cleanup operation.
- Abstraction, code-quality, dead-code, test-coverage, test-quality, visual,
  dependency, and documentation audits passed after fixes. Valid findings
  removed an unused DTO, omitted absent optional JSON fields, bounded response
  bodies, sanitized body-stream failures, and prevented E2E credential
  inheritance.
- `task lint`, `task test`, `task surface:report`, `task coverage`, `task pack`,
  `task smoke:process`, `task smoke:mcp`, and `task release:tool-smoke` passed.
  Final line coverage is App 91.22%, Archidekt 91.01%, Core 100.00%, Decks
  93.85%, Playgroup 98.26%, and Scryfall 93.75%.
- Vulnerability and deprecation scans are clean. Outdated inspection found no
  direct Playgroup dependency because the adapter is BCL-only; unrelated
  transitive updates remain owned by existing repository pins.
