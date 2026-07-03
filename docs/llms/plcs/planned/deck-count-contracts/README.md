# Deck Count Contracts PLC Packet

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/deck-count-contracts/
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: planning

## Summary

Introduce one Core-owned deck count partition and expose it additively through workspace and summary surfaces. Existing maybeboardCards and roleCounts remain unchanged through 0.9.

## Packet Contents

- [SRD.md](SRD.md): canonical count and compatibility requirements.
- [SADD.md](SADD.md): partition algorithm and surface design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): three delivery phases.
- [FIXTURES.md](FIXTURES.md): category and quantity acceptance cases.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Count by primary category only | Accepted | Secondary tags must not change zone ownership | [Partition algorithm](SADD.md#partition-algorithm) |
| Add cardCounts | Accepted | Canonical semantics can ship without breaking old fields | [Public schema](SADD.md#public-schema) |
| Preserve legacy fields through 0.9 | Accepted | Their current semantics are already public | [Compatibility](SRD.md#scope-and-non-scope) |

## Project And Surface Impact

MtgMcp.Core gains DeckCardCountSummary and one partition function. MtgMcp.App adds cardCounts to workspace start/open and deck_summarize. Surface tests and compatibility docs change; simulation profiles and role classification do not.

## Current Open Questions

None.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements are testable and have acceptance criteria.
- [x] Major alternatives and tradeoffs are recorded.
- [x] Quality attributes are measurable or inspectable.
- [x] Core/App boundaries and dependency impact are explicit.
- [x] MCP and documentation impacts are clear.
- [x] Adapter concerns are not applicable.
- [x] Reuse and readability expectations are clear.
- [x] SRD maps Must requirements to validation.
- [x] Implementation plan has objective exits.
- [x] Deferred removal of legacy fields is visible.

## Implementation Checklist

- [ ] Packet moved to docs/llms/plcs/in-progress/deck-count-contracts/.
- [ ] Current phase is named before code changes start.
- [ ] SRD/SADD updated if implementation changes the contract.
- [ ] Validation evidence recorded.
- [ ] Duplicate count logic removed after migration.
- [ ] Requirements marked complete or deferred.
- [ ] Final review uses an outcome-oriented title.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Planning packet inspection | Pass | Compatibility and invariant matrix specified |

## Completion Notes

Implementation evidence will be added before completion.
