# Card Snapshot Integrity PLC Packet

> [!WARNING]
> **Rewrite disposition: superseded/reference-only — do not implement this
> legacy workspace-JSON migration.** The durable known-empty-versus-unknown
> principle is owned by [local deck identity](../../completed/local-deck-store/README.md) and
> [Scryfall corpus and evidence](../../completed/scryfall-corpus-and-evidence/README.md), including
> explicit root/face handling for multi-face cards. Moxfield mapping, legacy
> workspace migration, and `deck_refresh_card_metadata` mechanisms are removed.
> Reviewed against the rewrite on 2026-07-03; lifecycle movement is deferred to
> authorized foundation implementation.
> All hydration/provider mechanisms below are historical and non-normative;
> later identity resolution must use the replacement corpus boundary.

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/card-snapshot-integrity/
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: implementation retired; reference evidence only

## Summary

Make persisted card snapshots explicit about which metadata is known, preserve provider imports before optional enrichment, and make refresh selection safe. This packet supplies trustworthy card facts for analysis without changing goldfish or deck-count behavior.

## Packet Contents

- [SRD.md](SRD.md): requirements and compatibility contract.
- [SADD.md](SADD.md): coverage, migration, provider, and hydration design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): four independently validated phases.
- [FIXTURES.md](FIXTURES.md): old/new snapshots, provider payloads, and failure cases.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Persist field-group coverage at root and face level | Accepted | Known-empty and unknown data must remain distinguishable | [Coverage model](SADD.md#coverage-and-schema-model) |
| Save raw imports before hydration | Accepted | Provider success must survive enrichment failure | [Import flow](SADD.md#import-and-hydration-flow) |
| Reject unknown refresh scopes | Accepted | Silent fallback to all-card refresh is unsafe | [Refresh contract](SADD.md#refresh-contract) |

## Project And Surface Impact

MtgMcp.Core owns snapshot coverage, cloning, fingerprints, readiness, and migration. MtgMcp.Archidekt, MtgMcp.Moxfield, and MtgMcp.Scryfall map their payload evidence. MtgMcp.App changes deck_refresh_card_metadata validation. Workspace JSON and quality summaries gain additive fields; normal tests remain offline.

## Current Open Questions

None.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements are testable and have acceptance criteria.
- [x] Major alternatives and tradeoffs are recorded.
- [x] Quality attributes are measurable or inspectable.
- [x] Core/App/adapter/test boundaries and dependency impact are explicit.
- [x] MCP surface, operation-mode, and documentation impacts are clear.
- [x] Adapter auth, pacing, cache, retry, and error-sanitization impacts are clear when relevant.
- [x] Documentation, readability, and abstraction reuse expectations are clear.
- [x] SRD maps Must requirements to acceptance criteria and validation.
- [x] Implementation plan has phase exit criteria.
- [x] Deferred work is visible and not required by the first implementation phase.

## Implementation Checklist

- [x] Independent lifecycle move retired; packet remains reference-only pending foundation reconciliation.
- [ ] Current phase is named before code changes start.
- [ ] SRD/SADD updated when implementation changes the plan.
- [ ] Validation evidence recorded as phases complete.
- [ ] Obsolete or duplicate code is removed as replacement work lands.
- [ ] Completed or deferred requirements are marked in the implementation plan.
- [ ] Final review title uses a concise outcome-oriented subject.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Planning packet inspection | Pass | Requirements, phases, and fixtures are fully mapped |

## Completion Notes

Do not move this packet to `in-progress`. Preserve its fixtures/reasoning as
reference evidence; implementation proceeds through the linked rewrite children.
