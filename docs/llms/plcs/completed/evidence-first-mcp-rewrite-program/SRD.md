# Evidence-First MCP Rewrite Program Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: repository owner and designated child PLC reviewers
- Last updated: 2026-07-12
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Author | Summary of change |
| --- | --- | --- |
| 2026-07-03 | Codex | Initial umbrella program draft. |
| 2026-07-03 | Codex | Applied AMEND-001 and recorded sequential drafting of all then-required ten children without implementation. |
| 2026-07-03 | Codex | Applied AMEND-002 to include Archidekt folder and named-snapshot workflows in the then-numbered child 6 and cutover. |
| 2026-07-04 | Codex | Applied AMEND-003 for capability toolsets, north-star acceptance, and an eleven-child queue. |
| 2026-07-04 | Codex | Reconciled proposed AMEND-004 for unified official Scryfall bulk/tag evidence, deterministic deck categorization, and deferred local query evaluation. |
| 2026-07-04 | Repository owner and Codex | Accepted AMEND-004 and authorized the Scryfall corpus/evidence child; implementation retains the manual full-corpus gate. |
| 2026-07-06 | Codex | Drafted proposed AMEND-005 and the independently reviewable pre-statistics hardening child without implementation authorization. |
| 2026-07-09 | Codex and independent review sub-agent | Reconciled and activated child 10 with explicit format-neutral selectors, exact allocation semantics, structured bounded outcomes, and post-child surface counts. |
| 2026-07-09 | Codex | Closed child 10 after its exact engines, MCP surface, offline/package gates, audits, and independent-formula deck workflow passed. |

## Executive Summary

The evidence-first rewrite crosses the MCP surface, local persistence, provider
contracts, statistical analysis, security modes, and release strategy. One
monolithic PLC would make those decisions difficult to review and likely to be
implemented in oversized sessions. This program instead requires ten focused
PLCs plus cross-cutting surface-governance and hardening PLCs, authored one at a time and
independently approved.

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
- [Scryfall Bulk Data](https://scryfall.com/docs/api/bulk-data)
- [Scryfall Tags](https://scryfall.com/docs/api/tags)
- [Scryfall API usage guidance](https://scryfall.com/docs/faqs/i-m-having-trouble-accessing-the-scryfall-api-or-i-m-blocked-17)
- [Magic Comprehensive Rules](https://media.wizards.com/2026/downloads/MagicCompRules%2020260619.pdf)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| Focused planning | Each child packet covers exactly one queue topic and is completed before the next draft. | One planning run may contain multiple sequentially completed packets. |
| Independent review | Every child has its own approval record and can be reviewed without approving another child. | Approval is planning approval, not code authorization. |
| Stable direction | Every child repeats or links the inherited guardrails and declares no conflict. | Conflicts require an umbrella amendment. |
| Traceable decomposition | Twelve required topics under accepted AMEND-005 have distinct packets, dependencies, and acceptance criteria. | Post-cutover topics remain registered only. |
| Manageable active surface | Default sessions expose only implemented default-enabled toolsets while every stable capability remains selectable. | Toolsets control relevance; modes control authority. |
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
- One planning run may draft multiple children only in registry order and only
  after the preceding packet is complete and structurally validated.
- A child must use the standard five-file PLC shape and remain in `planned/`
  until separately authorized implementation begins.
- Existing planned PLCs are not automatically completed, superseded, or
  deleted by this program.
- External provider contracts must be re-verified by the child that owns them.

## Use Cases

| ID | Actor and trigger | Expected outcome |
| --- | --- | --- |
| CASE-001 | An agent is asked to draft the rewrite planning queue. | The agent completes and validates children sequentially and updates the umbrella registry without implementing them. |
| CASE-002 | A reviewer finishes reviewing a child. | The child records approved or changes-requested state with reviewer, date, and reviewed revision. |
| CASE-003 | A child discovers a conflict with a shared guardrail. | Planning pauses until an umbrella amendment is reviewed. |
| CASE-004 | An implementer is asked to build a child capability. | The implementer verifies that child approval and implementation authorization exist; the umbrella alone is insufficient. |
| CASE-005 | All required child plans are approved. | The umbrella moves to `completed/` without claiming that the rewrite code is complete. |
| CASE-006 | A child adds or changes an MCP operation. | The child assigns one toolset, justifies tool versus resource shape, and proves a north-star workflow before approval. |

## Scope And Non-Scope

- In scope: shared rewrite guardrails, the proposed twelve-child queue, authoring and review
  protocol, approval records, lifecycle transitions, amendment handling,
  traceability, and documentation validation.
- Out of scope: child API design, schema design, provider payload capture,
  production implementation, worktree creation, database migration, package
  publishing, and runtime verification.
- Compatibility target: current PLC lifecycle conventions and Markdown
  documentation tooling.
- Explicit non-goal: treating the existence of all child drafts as review,
  implementation authorization, or rewrite completion.

## Stakeholders And Affected Systems

The affected parties are repository owners, planning reviewers, future
implementation agents, and release maintainers. The affected repository area
is `docs/llms/plcs/`; there is no direct effect on MCP clients, provider
services, generated artifacts, local databases, or production assemblies.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| PROG-001 | Must | Functional | After AMEND-005 acceptance, the program shall define exactly twelve required child PLCs in the approved order. | Each stable topic and the pre-statistics hardening boundary need isolated ownership. | README and implementation plan list the same twelve slugs and order. |
| PROG-002 | Must | Process | Required child PLCs shall be drafted sequentially, with one packet completed and validated before drafting the next. | Prevents parallel drafts from diverging on shared contracts. | Creation history and validation evidence show registry order and no overlapping incomplete drafts. |
| PROG-003 | Must | Process | Every child shall remain independently reviewable and shall require approval before implementation. | Drafting completeness is not implementation authority. | Each child has its own approval record and remains `planned` until separately activated. |
| PROG-004 | Must | Safety | The umbrella shall not authorize production implementation. | Planning approval and mutation authority are different. | Every program document states this boundary; implementation requires the applicable child to be activated. |
| PROG-005 | Must | Architecture | Every child shall inherit the program guardrails or identify an approved umbrella amendment. | Cross-cutting contracts must remain consistent. | Each child README contains a guardrail-conformance section with no unresolved conflict. |
| PROG-006 | Must | Governance | A guardrail change shall be reviewed as an umbrella amendment before dependent child authoring continues. | Prevents silent cross-topic scope changes. | Registry is marked blocked and no later child is drafted until the amendment is accepted. |
| PROG-007 | Must | Documentation | Every child shall use the standard five-file PLC packet shape. | Keeps review and implementation handoff predictable. | README, SRD, SADD, IMPLEMENTATION_PLAN, and FIXTURES exist and contain no template placeholders. |
| PROG-008 | Must | Audit | The legacy audit child shall be approved before destructive rewrite implementation begins. | Deletion and reuse need reviewed evidence. | Child 1 contains inventories and allowlists and is approved before foundation implementation is authorized. |
| PROG-009 | Must | Review | Every child shall contain the review-gate content defined by this program. | A narrow packet still needs complete decisions and validation. | The acceptance checklist in FIXTURES passes for the child. |
| PROG-010 | Must | Lifecycle | Child packets shall remain `planned` until their own implementation is explicitly authorized. | Umbrella progress must not imply code authorization. | Lifecycle paths and README states agree. |
| PROG-011 | Must | Lifecycle | The umbrella shall move to `in-progress` when child 1 is first drafted and to `completed` only after all twelve children are approved. | Program state must describe planning progress accurately. | Folder, README status, registry, and validation evidence agree at each transition. |
| PROG-012 | Must | Traceability | The umbrella shall maintain authoring, review, dependency, and approval status for every required child. | Maintainers need one program-level view. | Registry is updated in the same change as every child status transition. |
| PROG-013 | Must | Deferral | Post-cutover topics shall be registered but not drafted as part of the required twelve-child sequence. | They must not expand the rewrite cutover. | No post-cutover child directory is created by this program sequence. |
| PROG-014 | Must | Quality | Every child shall map Must requirements to design and objective validation. | Passing review should imply implementability. | Child traceability has no unmapped Must requirement. |
| PROG-015 | Must | Safety | Provider-owning children shall document auth, permission sensitivity, pacing, retry, cache, sanitization, fixture, and live-test boundaries. | Provider automation has operational and trust risks. | Review checklist marks each applicable concern resolved or explicitly not applicable. |
| PROG-016 | Should | Maintainability | Existing PLCs shall remain untouched until the audit child records their disposition. | Historical planning may provide evidence. | No existing packet is moved or edited solely because this umbrella was created. |
| PROG-017 | Must | Architecture | Every stable MCP tool shall belong to exactly one startup-selectable capability toolset, and visible tools shall equal implemented tools intersected with selected toolsets and operation-mode authority. | Tool relevance and mutation authority are separate concerns. | Toolset/mode matrix tests detect unassigned, multiply assigned, hidden-required, or overexposed tools. |
| PROG-018 | Must | Usability | `default`, `all`, and `none` toolset selections shall be deterministic; `default` contains implemented default-enabled toolsets, `all` contains implemented stable toolsets, and `none` contains zero tools. | LLMs need a manageable ordinary surface without losing complete access. | Official-client discovery snapshots pass in all three modes for all three selections. |
| PROG-019 | Must | Diagnostics | Under AMEND-005, the capability resource shall distinguish implementation, credential configuration, enablement, default enablement, and disabled toolsets without claiming provider verification or advertising placeholders. | Clients need truthful surface discovery without metadata-triggered I/O. | Capability-resource fixtures reconcile exactly with `tools/list`, configuration state, and zero-I/O spies. |
| PROG-020 | Must | Review | Every remaining capability child shall include a north-star acceptance section naming player questions enabled, evidence class, determinism boundary, explicit unknowns, MCP decision boundary, representative composed workflow, toolset assignment, and tool-versus-resource rationale. | Endpoint coverage alone does not prove deckbuilding usefulness or simplicity. | Child review checklist rejects any packet missing one of these decisions or objective workflow evidence. |
| PROG-021 | Must | Stability | Toolset selection shall be resolved at startup and remain static for the session; stable `0.9.0` shall not depend on dynamic tool-list mutation or `listChanged`. | Static registration is easier to test and works consistently across clients. | Reconfiguration requires restart; initialization advertises no tool-list-change capability. |
| PROG-022 | Must | Architecture | Official Scryfall card/ruling facts and community tag evidence shall use one `scryfall.db`, one adapter, and the `scryfall` toolset while retaining separate evidence schemas; no separate tag store, adapter, or toolset remains planned. | The official bulk contract now owns both datasets and their identity joins. | Remaining child/cutover packets and durable guidance contain no active obsolete tag persistence, adapter, prefix, or unsupported acquisition design. |
| PROG-023 | Must | Safety | Multi-gigabyte Scryfall bulk synchronization shall be explicit, cross-process coordinated, and never triggered by startup/background/read paths; new uncached arbitrary search syntax remains provider-authoritative. | Provider load, bandwidth, disk mutation, and query correctness require visible boundaries. | Scryfall requirements and fixtures include download spies, leases/pacing, 24-hour freshness, cache-only behavior, and provider-query proof. |
| PROG-024 | Must | Deferral | Local Scryfall query evaluation shall remain a registered non-cutover future PLC with differential parity and explicit coverage/fallback gates. | The corpus should enable future work without pretending at syntax parity now. | No required child implements a local evaluator; the post-cutover registry and potential-features note contain the acceptance boundary. |
| PROG-025 | Must | Hardening | Before statistics implementation, one independently approved child shall correct capability and batch schemas, add exact-only identity preview/apply, decompose proven Scryfall/Archidekt ownership boundaries, and explicitly exclude legality and fuzzy resolution. | New capability work should build on honest, usable, maintainable contracts. | The child packet passes its traceability, surface, behavior-preservation, audit, and lifecycle gates before statistics is activated. |

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
| Focus | A child is drafted. | Its five-file packet is complete and validated before the next child begins. |
| Traceability | A reviewer inspects a Must requirement. | It maps to design and validation in that child. |
| Consistency | A shared decision is used by multiple children. | Its authoritative wording remains in the umbrella and children link or conform to it. |
| Safety | A provider child is reviewed. | Provider safety fields are complete before approval. |
| Maintainability | The program resumes in a later session. | Registry and approval records identify the next permitted child without reconstructing conversation history. |
| Model usability | A default client initializes. | Only default-enabled implemented toolsets are visible, and one representative deckbuilding workflow composes them successfully. |
| Documentation integrity | A packet is changed. | Relative links resolve and `git diff --check` passes. |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| Umbrella review | Approve program rules and proposed amendments. | PROG-001 through PROG-025 | Packet review and docs validation pass. |
| Child authoring | Draft one complete packet at a time in registry order. | PROG-001 through PROG-025 | Each draft is structurally validated before the next begins; approvals remain independent. |
| Program closure | Close planning decomposition. | PROG-011 through PROG-014 | All twelve children are approved and umbrella completion evidence is recorded. |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| PROG-001, PROG-002, PROG-003 | SADD: Child packet decomposition; planning flow | Document inspection and registry review | FIX-PROGRAM-REGISTRY, SCN-001, SCN-002 |
| PROG-004, PROG-010, PROG-011 | SADD: Planning boundary; lifecycle | Path and status inspection | SCN-003, SCN-004 |
| PROG-005, PROG-006 | SADD: Guardrail amendments | Child conformance and amendment review | FIX-GUARDRAILS, SCN-005 |
| PROG-007, PROG-009, PROG-014 | SADD: Child packet contract | Packet checklist and traceability review | FIX-CHILD-CHECKLIST |
| PROG-008, PROG-016 | SADD: Audit-first design | Registry and Git diff review | SCN-006 |
| PROG-022 through PROG-024 | SADD: Scryfall corpus charter; capability governance; deferred registry | Cross-packet terminology, surface, provider-boundary, and registry review | FIX-GUARDRAILS, FIX-PROGRAM-REGISTRY, SCN-012 |
| PROG-012, PROG-013 | SADD: Registries | Registry inspection | FIX-PROGRAM-REGISTRY |
| PROG-015 | SADD: Provider child requirements | Provider review checklist | FIX-PROVIDER-CHECKLIST |
| PROG-017 through PROG-019, PROG-021 | SADD: Capability toolset governance | Toolset/mode/resource contract review | FIX-TOOLSET-GUARDRAILS, SCN-007 through SCN-010 |
| PROG-020 | SADD: North-star acceptance gate | Child workflow and evidence-boundary review | FIX-NORTH-STAR-CHECKLIST, SCN-011 |
| PROG-025 | SADD: hardening child charter and capability governance | Child packet, schema/surface matrices, and behavior-preservation review | FIX-PROGRAM-REGISTRY, SCN-013 |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Child authoring may expose a guardrail conflict. | Risk | Later packets could inherit a faulty constraint. | Child author and repository owner | Pause the queue and review an umbrella amendment. |
| Existing PLCs may overlap rewrite topics. | Risk | Competing plans could confuse implementation. | Audit child owner | Classify each existing packet without moving it prematurely. |
| Provider contracts may change before their child is drafted. | Assumption | Previously gathered research may be stale. | Provider child owner | Re-verify official and permission-sensitive contracts during that child session. |
| A long planning run may blur packet boundaries. | Risk | Reviewers could mistake one change for one approval unit. | Program owner | Preserve separate directories, approval records, requirements, and validation for every child. |

## Validation

- Inspect all five umbrella files for template placeholders and contradictory
  lifecycle language.
- Check the required child order and slugs in README, SADD, implementation
  plan, and acceptance matrix.
- Resolve every relative Markdown link.
- Run `git diff --check`.
- Do not run a .NET build for this docs-only change.

## Definition Of Done

- [x] All Must requirements have objective acceptance criteria.
- [x] Requirements map to design and acceptance artifacts.
- [x] The packet contains no child implementation design masquerading as a program decision.
- [x] Documentation validation passes.
- [x] Remaining risks are recorded without unresolved planning decisions.
