# Evidence-First MCP Rewrite Program Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

This plan implements a planning program, not the MCP rewrite. After the
umbrella is reviewed, each phase creates exactly one child PLC in a new agent
session, validates it, obtains independent approval, and updates the umbrella
registry. The next phase cannot begin while the current child is draft,
changes-requested, or in conflict with a guardrail.

Child implementation may be requested separately after child approval. Such
work follows that child's own implementation plan and lifecycle and does not
allow the current planning session to draft another child.

## Per-Child Authoring Protocol

Every child phase follows the same protocol:

1. Read the umbrella and all approved prerequisite children.
2. Re-inspect current code, tests, docs, provider contracts, and scoped
   `AGENTS.md` files relevant to the topic.
3. Create only one child directory from the repository PLC template.
4. Make the child decision-complete within its narrow scope.
5. Add guardrail conformance and the standard planning approval record.
6. Validate links, traceability, packet structure, and `git diff --check`.
7. Update the umbrella registry to `Draft` and record validation evidence.
8. End the authoring session without starting code or another child.
9. Record review feedback in the child. When approved, update both the child
   approval record and umbrella registry.

## Phase Summary

| Phase | Goal | Requirements | Documentation area | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 0 | Review the umbrella program. | PROG-001 through PROG-016 | This packet | Program rules approved and docs checks pass. | In progress |
| 1 | Audit and classify the legacy surface. | PROG-002 through PROG-009, PROG-014, PROG-016 | `legacy-surface-audit-and-disposition` | Complete inventory, disposition, deletion/reuse allowlists, and approval. | Planned |
| 2 | Plan the clean skeleton and repository foundation. | PROG-002 through PROG-015 | `rewrite-skeleton-foundation` | Foundation contracts are decision-complete and approved. | Planned |
| 3 | Plan the local deck domain and store. | PROG-002 through PROG-015 | `local-deck-store` | Deck model, persistence, tools, and validation are approved. | Planned |
| 4 | Plan manual deck interchange. | PROG-002 through PROG-015 | `manual-deck-interchange` | Native and provider artifact contracts are approved. | Planned |
| 5 | Plan Scryfall evidence snapshots. | PROG-002 through PROG-015 | `scryfall-evidence-snapshots` | Snapshot API, storage, fidelity, and provider safety are approved. | Planned |
| 6 | Plan Archidekt synchronization. | PROG-002 through PROG-015 | `archidekt-deck-sync` | Essential operations and conflict-safe sync are approved. | Planned |
| 7 | Plan the Playgroup public API. | PROG-002 through PROG-015 | `playgroup-public-api` | Pinned official surface and write safety are approved. | Planned |
| 8 | Plan exact deck statistics. | PROG-002 through PROG-014 | `exact-deck-statistics` | Exact functions, assumptions, and proofs are approved. | Planned |
| 9 | Plan the Scryfall Tagger cache. | PROG-002 through PROG-015 | `scryfall-tagger-cache` | Cache and conservative acquisition contracts are approved. | Planned |
| 10 | Plan stabilization and cutover. | PROG-002 through PROG-015 | `rewrite-stabilization-cutover` | Cross-module gates, release, and rollback plan are approved. | Planned |
| 11 | Close the planning program. | PROG-011 through PROG-014 | Umbrella packet | All approvals and evidence are recorded; umbrella moves to completed. | Planned |

## Phase Details

### Phase 0: Umbrella Review

- Problems solved: establishes the durable queue, guardrails, and approval
  protocol before any child is drafted.
- Included requirements: PROG-001 through PROG-016.
- Out of scope: creating a child packet or modifying production code.
- Expected edits: only this packet and the planned PLC index.
- Validation: inspect packet links and consistency, then run
  `git diff --check`.
- Exit criteria: repository owner accepts the packet or all review feedback is
  incorporated.
- Rollback: remove this new packet and planned-index entry.
- Cleanup: none; existing PLCs remain unchanged.

### Phase 1: Legacy Surface Audit And Disposition

- Draft only `legacy-surface-audit-and-disposition`.
- Inventory every tool, resource, prompt, adapter, persistence path, scheduled
  or background workflow, fixture family, and live-test claim.
- Classify each item as `rebuild`, `remove`, `experimental`, `unsupported`,
  `misleading`, or `fixture-only`, with code/test evidence.
- Produce explicit deletion and reuse allowlists for child 2.
- Do not delete or refactor production code.
- Exit only after independent approval is recorded.

### Phases 2 Through 9: Capability Planning

- Follow the per-child protocol and the exact queue.
- Resolve topic-specific interfaces, data, dependencies, failure modes,
  provider safety, tests, rollout, and rollback only in the owning packet.
- Preserve approved upstream contracts or propose an umbrella/dependency
  amendment before proceeding.
- Do not draft a later capability or start implementation in the same session.

### Phase 10: Stabilization And Cutover Planning

- Draft only after children 1 through 9 are approved.
- Define cross-module architecture, MCP-schema, offline, package, documentation,
  coverage, and opt-in live-provider gates.
- Define the `0.9.0` merge, release, rollback, legacy retention, and PLC cleanup
  process.
- Make implementation conditional on successful implementation and validation
  of every stable capability child.
- Do not execute the cutover.

### Phase 11: Program Closure

- Confirm all ten required directories exist and have approved records.
- Confirm every child has complete traceability and no unresolved guardrail
  conflict.
- Update umbrella registry and validation evidence.
- Move this packet to `completed/` and update lifecycle indexes.
- State explicitly that child implementation and release completion are
  independent.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| A child expands into adjacent topics. | 1 through 10 | Enforce one topic, explicit non-goals, and one new directory per session. | Child author and reviewer |
| Unreviewed decisions cascade. | 2 through 10 | Require prior approval before the next draft. | Program owner |
| Shared guardrails prove incorrect. | 1 through 10 | Pause and review an umbrella amendment. | Repository owner |
| Existing PLCs conflict with the rewrite. | 1 and 2 | Leave them untouched until the audit records disposition. | Audit child owner |
| Provider research becomes stale. | 5 through 9 | Re-verify during the owning child session. | Provider child owner |
| Planning completion is mistaken for code completion. | All | Keep approval, implementation authorization, lifecycle, and release evidence separate. | Program owner |

## Completion Criteria

- [ ] Every Must requirement appears in at least one phase.
- [ ] Each child was created in a separate agent session.
- [ ] Dependencies and approvals are recorded before subsequent drafting.
- [ ] Every child has the standard five-file packet and approval record.
- [ ] Every child maps Must requirements to design and objective validation.
- [ ] Provider children include complete safety and fixture strategies.
- [ ] No post-cutover packet was drafted as part of the required sequence.
- [ ] No production code was changed under umbrella authority alone.
- [ ] Registry, lifecycle paths, and validation evidence agree.
- [ ] Deferred work remains visible in the post-cutover registry.
