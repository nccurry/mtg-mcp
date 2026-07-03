# Land Entry Classification PLC Packet

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/land-entry-classification/
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: planning

## Summary

Correct the shared LandEntryClassifier so reveal, pay, and discard conditions followed by an enters-tapped consequence are classified as conditional rather than normally untapped.

## Packet Contents

- [SRD.md](SRD.md): classifier requirements and edge cases.
- [SADD.md](SADD.md): ordered pattern design and consumer contract.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): three focused phases.
- [FIXTURES.md](FIXTURES.md): representative land text matrix.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Extend the existing classifier | Accepted | Stats Lab and goldfish already share this ownership point | [Chosen design](SADD.md#chosen-design) |
| Classify condition, do not evaluate a hand | Accepted | Hand-state rules belong to consumers | [Scope](SRD.md#scope-and-non-scope) |
| Use bounded explicit patterns | Accepted | A general rules parser is unnecessary | [Alternatives](SADD.md#alternatives-considered) |

## Project And Surface Impact

MtgMcp.Core classifier and tests change. Stats Lab and goldfish consumer tests verify continued reuse. Calibration documentation records expected metric movement; no public schema changes.

## Current Open Questions

None.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements and fixtures are testable.
- [x] Alternatives are recorded.
- [x] Quality attributes are measurable.
- [x] Project boundaries are explicit.
- [x] No MCP shape changes exist.
- [x] Adapter impacts are not applicable.
- [x] Existing abstraction reuse is required.
- [x] Traceability is complete.
- [x] Phase exits are objective.
- [x] General rules parsing is visibly deferred.

## Implementation Checklist

- [ ] Packet moved to docs/llms/plcs/in-progress/land-entry-classification/.
- [ ] Current phase named.
- [ ] Design updated if pattern precedence changes.
- [ ] Validation evidence recorded.
- [ ] Duplicate consumer parsing removed.
- [ ] Requirements marked complete or deferred.
- [ ] Final review title is outcome-oriented.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Planning packet inspection | Pass | Ordered land-text matrix is complete |

## Completion Notes

Implementation evidence will be recorded before completion.
