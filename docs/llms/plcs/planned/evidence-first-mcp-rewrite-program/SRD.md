# Evidence-First MCP Rewrite Program Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: repository owner and designated child PLC reviewers
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Author | Summary of change |
| --- | --- | --- |
| 2026-07-03 | Codex | Initial umbrella program draft. |

## Executive Summary

The evidence-first rewrite crosses the MCP surface, local persistence, provider
contracts, statistical analysis, security modes, and release strategy. One
monolithic PLC would make those decisions difficult to review and likely to be
implemented in oversized sessions. This program instead requires ten focused
PLCs, authored one at a time and independently approved.

The umbrella fixes shared product boundaries and the child authoring protocol.
It does not define detailed child APIs and does not authorize production code.

## Audience

- Repository owners reviewing rewrite scope and product direction.
- Agents drafting or reviewing a child PLC.
- Implementers verifying that an approved child is authorized and consistent
  with the program.
- Release maintainers evaluating when the planning program, implementation,
  and cutover are independently complete.

## References

- [Repository north star](../../../../north-star.md)
- [Design goals](../../../../design-goals.md)
- [Heuristic model boundaries](../../../../heuristic-models.md)
- [PLC lifecycle guidance](../../README.md)
- [PLC packet template](../../../templates/plc/README.md)
- [Playgroup.gg public API](https://playgroup.gg/api-docs/index.html)
- [Moxfield terms](https://moxfield.com/help/terms)
- [Scryfall terms](https://scryfall.com/docs/terms)
- [Scryfall Tagger robots policy](https://tagger.scryfall.com/robots.txt)
- [Magic Comprehensive Rules](https://media.wizards.com/2026/downloads/MagicCompRules%2020260619.pdf)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Focused planning | A review change introduces no more than one new child packet. | The umbrella packet itself is not a child. |
| Independent review | Every child README records approval before the next child is drafted. | Approval is planning approval, not code authorization. |
| Stable direction | Every child repeats or links the inherited guardrails and declares no conflict. | Conflicts require an umbrella amendment. |
| Traceable decomposition | Ten required topics have distinct packets, dependencies, and acceptance criteria. | Post-cutover topics remain registered only. |
| Safe implementation boundary | No production edit cites the umbrella alone as authorization. | The applicable child must be approved and explicitly activated. |

## System Overview

The system governed here is the repository's planning lifecycle. The umbrella
packet owns shared guardrails, the child registry, authoring order, amendment
rules, and program completion. Each child owns the detailed requirements and
design for one capability. Repository code, provider fixtures, and runtime
surfaces remain unchanged until later, explicitly authorized implementation.

## Assumptions, Dependencies, And Constraints

- The current code, tests, project files, task definitions, and scoped agent
  instructions remain the implementation source of truth.
- The audit child is the source of truth for legacy deletion and reuse; this
  umbrella records suspected areas but does not pre-approve deletions.
- One agent session may update this umbrella while drafting a child, but may
  create no more than one new child packet.
- A child must use the standard five-file PLC shape and remain in `planned/`
  until separately authorized implementation begins.
- Existing planned PLCs are not automatically completed, superseded, or
  deleted by this program.
- External provider contracts must be re-verified by the child that owns them.

## Use Cases

| ID | Actor and trigger | Expected outcome |
| --- | --- | --- |
| CASE-001 | An agent is asked to start the rewrite planning program. | The agent drafts only `legacy-surface-audit-and-disposition` and updates the umbrella registry. |
| CASE-002 | A reviewer finishes reviewing a child. | The child records approved or changes-requested state with reviewer, date, and reviewed revision. |
| CASE-003 | A child discovers a conflict with a shared guardrail. | Planning pauses until an umbrella amendment is reviewed. |
| CASE-004 | An implementer is asked to build a child capability. | The implementer verifies that child approval and implementation authorization exist; the umbrella alone is insufficient. |
| CASE-005 | All required child plans are approved. | The umbrella moves to `completed/` without claiming that the rewrite code is complete. |

## Scope And Non-Scope

- In scope: shared rewrite guardrails, the ten-child queue, authoring and review
  protocol, approval records, lifecycle transitions, amendment handling,
  traceability, and documentation validation.
- Out of scope: child API design, schema design, provider payload capture,
  production implementation, worktree creation, database migration, package
  publishing, and runtime verification.
- Compatibility target: current PLC lifecycle conventions and Markdown
  documentation tooling.
- Explicit non-goal: producing all child packets in this change or in one
  future agent session.

## Stakeholders And Affected Systems

The affected parties are repository owners, planning reviewers, future
implementation agents, and release maintainers. The affected repository area
is `docs/llms/plcs/`; there is no direct effect on MCP clients, provider
services, generated artifacts, local databases, or production assemblies.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PROG-001 | Must | Functional | The program shall define exactly ten required child PLCs in the approved order. | Each stable topic needs isolated ownership. | README and implementation plan list the same ten slugs and order. |
| PROG-002 | Must | Process | An agent session shall draft no more than one new child PLC. | Keeps each session and review focused. | A child-authoring change contains at most one newly created child directory. |
| PROG-003 | Must | Process | A child shall be reviewed and approved before the next child is drafted. | Prevents unstable decisions from cascading. | The preceding child README has a completed approval record before the next directory is created. |
| PROG-004 | Must | Safety | The umbrella shall not authorize production implementation. | Planning approval and mutation authority are different. | Every program document states this boundary; implementation requires the applicable child to be activated. |
| PROG-005 | Must | Architecture | Every child shall inherit the program guardrails or identify an approved umbrella amendment. | Cross-cutting contracts must remain consistent. | Each child README contains a guardrail-conformance section with no unresolved conflict. |
| PROG-006 | Must | Governance | A guardrail change shall be reviewed as an umbrella amendment before dependent child authoring continues. | Prevents silent cross-topic scope changes. | Registry is marked blocked and no later child is drafted until the amendment is accepted. |
| PROG-007 | Must | Documentation | Every child shall use the standard five-file PLC packet shape. | Keeps review and implementation handoff predictable. | README, SRD, SADD, IMPLEMENTATION_PLAN, and FIXTURES exist and contain no template placeholders. |
| PROG-008 | Must | Audit | The legacy audit child shall be approved before any destructive rewrite plan or implementation. | Deletion and reuse need evidence. | Child 1 contains inventories and allowlists and is approved before child 2 is drafted. |
| PROG-009 | Must | Review | Every child shall contain the review-gate content defined by this program. | A narrow packet still needs complete decisions and validation. | The acceptance checklist in FIXTURES passes for the child. |
| PROG-010 | Must | Lifecycle | Child packets shall remain `planned` until their own implementation is explicitly authorized. | Umbrella progress must not imply code authorization. | Lifecycle paths and README states agree. |
| PROG-011 | Must | Lifecycle | The umbrella shall move to `in-progress` when child 1 is first drafted and to `completed` only after all ten children are approved. | Program state must describe planning progress accurately. | Folder, README status, registry, and validation evidence agree at each transition. |
| PROG-012 | Must | Traceability | The umbrella shall maintain authoring, review, dependency, and approval status for every required child. | Maintainers need one program-level view. | Registry is updated in the same change as every child status transition. |
| PROG-013 | Must | Deferral | Post-cutover topics shall be registered but not drafted as part of the initial ten-child sequence. | They must not expand the rewrite cutover. | No post-cutover child directory is created by this program sequence. |
| PROG-014 | Must | Quality | Every child shall map Must requirements to design and objective validation. | Passing review should imply implementability. | Child traceability has no unmapped Must requirement. |
| PROG-015 | Must | Safety | Provider-owning children shall document auth, permission sensitivity, pacing, retry, cache, sanitization, fixture, and live-test boundaries. | Provider automation has operational and trust risks. | Review checklist marks each applicable concern resolved or explicitly not applicable. |
| PROG-016 | Should | Maintainability | Existing PLCs shall remain untouched until the audit child records their disposition. | Historical planning may provide evidence. | No existing packet is moved or edited solely because this umbrella was created. |

## Interfaces, Data, States, And Modes

The program introduces no MCP interface or runtime data. Its documentation
state consists of:

- Umbrella lifecycle: `planned`, `in-progress`, or `completed`.
- Child authoring state: `not drafted`, `draft`, or `approved`.
- Child review state: `not reviewed`, `changes requested`, or `approved`.
- Child implementation lifecycle, managed independently under the existing PLC
  lifecycle rules.

The required child approval record is defined in
[SADD.md](SADD.md#review-and-approval-state).

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Focus | A child is drafted. | The change creates exactly one child directory. |
| Traceability | A reviewer inspects a Must requirement. | It maps to design and validation in that child. |
| Consistency | A shared decision is used by multiple children. | Its authoritative wording remains in the umbrella and children link or conform to it. |
| Safety | A provider child is reviewed. | Provider safety fields are complete before approval. |
| Maintainability | The program resumes in a later session. | Registry and approval records identify the next permitted child without reconstructing conversation history. |
| Documentation integrity | A packet is changed. | Relative links resolve and `git diff --check` passes. |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| Umbrella review | Approve program rules. | PROG-001 through PROG-016 | Packet review and docs validation pass. |
| Child authoring | Draft and approve one child per session in registry order. | PROG-001 through PROG-015 | Current child approval is recorded before the next begins. |
| Program closure | Close planning decomposition. | PROG-011 through PROG-014 | All ten children are approved and umbrella completion evidence is recorded. |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| PROG-001, PROG-002, PROG-003 | SADD: Child packet decomposition; runtime flow | Document inspection and Git diff review | FIX-PROGRAM-REGISTRY, SCN-001, SCN-002 |
| PROG-004, PROG-010, PROG-011 | SADD: Planning boundary; lifecycle | Path and status inspection | SCN-003, SCN-004 |
| PROG-005, PROG-006 | SADD: Guardrail amendments | Child conformance and amendment review | FIX-GUARDRAILS, SCN-005 |
| PROG-007, PROG-009, PROG-014 | SADD: Child packet contract | Packet checklist and traceability review | FIX-CHILD-CHECKLIST |
| PROG-008, PROG-016 | SADD: Audit-first design | Registry and Git diff review | SCN-006 |
| PROG-012, PROG-013 | SADD: Registries | Registry inspection | FIX-PROGRAM-REGISTRY |
| PROG-015 | SADD: Provider child requirements | Provider review checklist | FIX-PROVIDER-CHECKLIST |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Child authoring may expose a guardrail conflict. | Risk | Later packets could inherit a faulty constraint. | Child author and repository owner | Pause the queue and review an umbrella amendment. |
| Existing PLCs may overlap rewrite topics. | Risk | Competing plans could confuse implementation. | Audit child owner | Classify each existing packet without moving it prematurely. |
| Provider contracts may change before their child is drafted. | Assumption | Previously gathered research may be stale. | Provider child owner | Re-verify official and permission-sensitive contracts during that child session. |
| Git history may not map perfectly to agent sessions. | Risk | One-child-per-session compliance could be unclear. | Program owner | Record the authoring session date and scope in each child revision history and inspect the creation diff. |

## Validation

- Inspect all five umbrella files for template placeholders and contradictory
  lifecycle language.
- Check the required child order and slugs in README, SADD, implementation
  plan, and acceptance matrix.
- Resolve every relative Markdown link.
- Run `git diff --check`.
- Do not run a .NET build for this docs-only change.

## Definition Of Done

- [ ] All Must requirements have objective acceptance criteria.
- [ ] Requirements map to design and acceptance artifacts.
- [ ] The packet contains no child implementation design masquerading as a program decision.
- [ ] Documentation validation passes.
- [ ] Remaining risks are recorded without unresolved planning decisions.
