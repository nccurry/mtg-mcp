# Evidence-First MCP Rewrite Program Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: repository owner and designated child PLC reviewers
- Last updated: 2026-07-12
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Author | Summary of change |
| --- | --- | --- |
| 2026-07-03 | Codex | Initial umbrella program design. |
| 2026-07-03 | Codex | Applied AMEND-001 for sequential drafting in one planning run. |
| 2026-07-03 | Codex | Applied AMEND-002 for Archidekt folder and named-snapshot scope. |
| 2026-07-04 | Codex | Applied AMEND-003 for static capability toolsets and north-star acceptance. |
| 2026-07-04 | Codex | Reconciled proposed AMEND-004 for one official Scryfall corpus, no separate Tagger capability, deterministic deck categorization, and deferred local query evaluation. |
| 2026-07-04 | Repository owner and Codex | Accepted AMEND-004 and activated the independently approved Scryfall corpus/evidence child. |
| 2026-07-06 | Codex | Drafted proposed AMEND-005 and the pre-statistics contract/adapter hardening child for independent review. |
| 2026-07-09 | Codex and independent review sub-agent | Locked child 10's exact result, selector, work-budget, schema, and format-neutral composition boundaries before implementation. |
| 2026-07-09 | Codex | Recorded child 10 implementation closure and retained categorization as the next independently review-gated boundary. |

## Executive Summary

The chosen design is a planning-only umbrella packet plus twelve sibling child
packets created sequentially in the PLC lifecycle folders. The umbrella owns
guardrails, registry state, authoring order, approval protocol, and amendments.
Each child owns one topic's detailed requirements, architecture, interfaces,
fixtures, and implementation phases.

The central constraint is that planning must remain independently reviewable.
Consequently, the durable Markdown registry and approval records are the source
of truth; conversation history is not. A monolithic rewrite PLC and overlapping
or parallel child drafting are both rejected.

## Goals, Non-Goals, And Design Drivers

Goals:

- Isolate each rewrite topic into a decision-complete, independently reviewed
  packet.
- Preserve consistent product, architecture, evidence, safety, and testing
  boundaries across children.
- Make the next permitted planning action discoverable from repository state.
- Separate planning completion from implementation and release completion.

Non-goals:

- Designing child tools, schemas, tables, adapters, or algorithms here.
- Starting rewrite code, creating the rewrite worktree, or moving a child to
  `in-progress`.
- Resolving the lifecycle of existing planned PLCs before the audit child.
- Drafting popularity or experimental PLCs in the required-child sequence.

Design drivers are focused review, durable handoff, least authority, explicit
dependencies, and evidence-backed removal decisions.

## Context And Scope

The umbrella sits above ordinary PLC packets but uses the same lifecycle and
five-file shape. It coordinates authors and reviewers; it is not a runtime
component. Child packets are siblings under `docs/llms/plcs/<lifecycle>/`, not
nested inside the umbrella, so each can move through implementation lifecycle
independently.

Current source, tests, project files, task definitions, scoped instructions,
and human-facing docs remain authoritative. Existing PLCs are inputs to the
future audit and do not become dependencies merely because their topics overlap.

## Constraints

- Follow the repository PLC template and lifecycle guidance.
- Complete and validate one child packet before drafting the next.
- Follow the exact queue even when a later child has fewer technical dependencies.
- Keep the umbrella at `planned` until child 1 drafting begins.
- Do not equate child planning approval with implementation authorization.
- Use documentation-only validation for this umbrella.
- Re-verify temporal provider facts in their owning child session.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Monolithic rewrite PLC | Put every capability and cutover decision in one packet. | One index and one review. | Oversized sessions, coupled review, hidden assumptions, and high drift risk. | Rejected |
| Generate children in parallel | Create all packets from the current research concurrently. | Fast initial decomposition. | Encourages conflicting contracts and incomplete cross-topic handoff. | Rejected |
| Ordinary plans without an umbrella | Create plans ad hoc as work begins. | Minimal process. | No shared guardrails, queue, approval protocol, or program completion state. | Rejected |
| Umbrella plus sequential sibling PLCs | Keep shared decisions central and finish one independently reviewable child before drafting the next. | Durable boundaries, focused validation, and explicit dependencies. | More documentation and later per-child reviews. | Chosen |

## Chosen Design

### Planning And Implementation Boundary

The umbrella authorizes only child PLC authoring and review. A production change
requires all of the following:

1. The applicable child is approved.
2. The user explicitly requests that child's implementation.
3. The child moves to `in-progress/` before the first production edit.
4. The child implementation plan names the active phase.

The umbrella's `in-progress` state means child planning is underway; it never
means rewrite code is authorized.

### Child Packet Decomposition

Each required topic is a sibling packet with its own README, SRD, SADD,
implementation plan, and fixture matrix. The fixed authoring order is:

1. `legacy-surface-audit-and-disposition`
2. `rewrite-skeleton-foundation`
3. `local-deck-store`
4. `manual-deck-interchange`
5. `mcp-capability-toolsets`
6. `scryfall-corpus-and-evidence`
7. `archidekt-deck-sync`
8. `playgroup-public-api`
9. `mcp-contract-and-adapter-hardening`
10. `exact-deck-statistics`
11. `deterministic-deck-categorization`
12. `rewrite-stabilization-cutover`

The first child is a docs-only audit. It creates the proposed deletion and reuse
allowlists consumed by the foundation draft. Under AMEND-001, later packets may
be drafted after their predecessors are complete and validated. Approval still
gates implementation, and the final child implementation remains gated on all
prerequisite implementations and validation.

### Minimum Child Charters

These charters preserve the agreed topic boundary without pre-writing each
child's detailed requirements or design.

#### 1. Legacy Surface Audit And Disposition

- Inventory every tool, resource, prompt, adapter, persistence path, background
  workflow, fixture family, and live-test claim.
- Classify each item as `rebuild`, `remove`, `experimental`, `unsupported`,
  `misleading`, or `fixture-only` using code and test evidence.
- Investigate the empty live-test suite, incomplete legacy tag acquisition,
  Playgroup limitations, heuristic decision tools, unofficial provider
  contracts, and documented analysis defects.
- Produce authoritative deletion and reuse allowlists without changing code.

#### 2. Rewrite Skeleton And Repository Foundation

- Define the sibling worktree and branch procedure while preserving Git history.
- Define removal according to the approved audit and retention of task,
  analyzer, coverage, package, and release wiring.
- Define the minimal server-information and capability surface, versioned data
  directory, operation modes, evidence states, module boundaries, and
  `0.9.0-preview.N` versioning.

#### 3. Local Deck Domain And SQLite Store

- Define revisioned decks, entries, zones, printings, categories, provider
  bindings, optimistic concurrency, and transactional migrations.
- Define exact `deck_*` CRUD and category mutation schemas.
- Define lossless native JSON backup, generic format support, Commander
  fixtures, canonical ordering, restoration, and failure behavior.

#### 4. Manual Deck Interchange

- Define import parsing and structured export artifact bundles.
- Define Archidekt-compatible manual artifacts and Moxfield deck text plus
  separate Bulk Edit or tag artifacts when required.
- Report unpreserved metadata explicitly and require golden plus manual UI
  verification.
- Exclude Moxfield network automation.

#### 5. MCP Capability Toolsets

- Define startup-selected `decks`, `scryfall`, `stats`, `archidekt`, and
  `playgroup` toolsets in App without adding Core dependencies. The completed
  packet's historical Tagger placeholder is superseded by accepted AMEND-004
  before any such toolset was implemented.
- Keep toolsets orthogonal to `read-only`, `local`, and `remote` authority.
- Define deterministic `default`, `all`, and `none` selections, configuration
  precedence, sanitized failures, capability reporting, and exact discovery
  tests without runtime tool-list changes.
- Assign current deck and interchange tools to `decks` and establish the
  registration contract inherited by every later child.

#### 6. Scryfall Corpus And Evidence

- Define explicit streaming synchronization of official All Cards, Rulings,
  Oracle Tags, and Art Tags into one shared `scryfall.db` with current/previous
  generations and guarded rollback.
- Define local-first identity, printing, ruling, and tag evidence; exact-request
  24-hour caching; immutable replay; cross-process leases/pacing; and explicit
  `default`, `cache-only`, and `refresh` policies.
- Keep new arbitrary Scryfall syntax provider-authoritative, exclude random and
  background bulk operations, and preserve card facts separately from joined
  community tag evidence.
- Define the exact eighteen-tool `scryfall` surface and a migration-friendly
  lossless corpus without implementing local query evaluation.

#### 7. Archidekt Decks, Folders, Snapshots, And Synchronization

- Define authentication status, remote list/get/create/delete, and exact card,
  printing, zone, and category translation.
- Define pull preview/apply, diff, push preview/apply, remote fingerprints,
  stale-write refusal, and `remote` mode enforcement.
- Define folder tree/detail/create/update/move/empty-delete and named snapshot
  list/get/create/update/delete/restore preview/apply.
- Define sanitized fixtures and opt-in throwaway folder/snapshot/deck live tests
  with verified cleanup.
- Exclude automatic activity logs/recent-change history, packages, deck tags,
  collaboration, social, and account administration.

#### 8. Playgroup Official API

- Pin and implement planning against the documented public OpenAPI contract.
- Define one typed `playgroup_*` tool per documented operation and preserve
  provider-shaped outputs.
- Gate event-batch and live-session writes behind `remote` mode.
- Report absent deck updates as unsupported, detect contract drift, and prohibit
  reverse engineering of private endpoints.

#### 9. MCP Contract And Adapter Hardening

- Separate implemented toolsets from non-I/O credential configuration in the
  capability resource.
- Replace the flat deck batch input with a described discriminated union.
- Add exact-only, evidence-bound deck identity preview/apply without legality,
  fuzzy resolution, printing choice, or strategic judgment.
- Decompose proven Scryfall and Archidekt responsibility families while
  preserving public, provider, persistence, pacing, and failure behavior.

#### 10. Exact Deck Statistics

- Define exact univariate and multivariate hypergeometric calculations,
  cards-seen tables, explicit play/draw assumptions, and exact composition
  summaries.
- Define land, flood, screw, color-source, mana, combo, tutor-equivalent,
  mulligan, and inverse-copy calculations from caller-supplied groups and
  policies.
- Return exact numerator/denominator values plus documented rounded decimals.
- Require exhaustive enumeration and property-based verification without a
  provider dependency.

#### 11. Deterministic Deck Categorization

- Define caller-authored rules mapping existing category IDs to exact Oracle or
  art tag selectors, hierarchy behavior, weights, exclusions, assignment mode,
  and optional primary priority.
- Define read-only validation/preview and one fingerprint-guarded local apply.
- Bind every preview to canonical rules, deck revision, and Scryfall corpus
  generation; preserve unknown evidence and unrelated categories.
- Add exactly three `deck_*` tools to `decks` without provider acquisition,
  hidden default meanings, recommendation, or adapter dependency cycles.

#### 12. Stabilization And `0.9.0` Cutover

- Define cross-module architecture, MCP-schema, offline, packaging,
  documentation, coverage, and opt-in live-provider gates.
- Prove the stable surface contains no legacy advisor, intent, recommendation,
  simulation, or unofficial Moxfield automation.
- Define merge, release, rollback, legacy-version retention, PLC transitions,
  and superseded-document cleanup without executing the cutover.

### Child Packet Contract

Every child must contain:

- A narrow objective and explicit non-goals.
- Dependencies on approved umbrella and child decisions.
- Current-state evidence and reuse/removal disposition.
- Exact tools, resources, prompts, schemas, annotations, and operation-mode
  behavior when applicable.
- Data ownership, persistence, dependency, privacy, and security decisions.
- Deterministic behavior and explicit unknown, unavailable, and unsupported
  states.
- Unit, integration, MCP-schema, fixture, and live-test requirements.
- Migration, rollout, rollback, and acceptance criteria.
- A requirement-to-design-to-test traceability table.
- For every public-surface child, one exact toolset assignment per tool and a
  tool-versus-resource rationale.
- A north-star acceptance section naming the player questions enabled,
  evidence class, determinism boundary, explicit unknown states, MCP decision
  boundary, and one representative composed LLM workflow.
- Guardrail conformance and an approval record in its README.

Provider-owning children also document authentication, permission sensitivity,
user agent, pacing, rate limits, retries, cache behavior, error sanitization,
fixture provenance, and live-test mutation safety.

### Capability Toolset Governance

Toolsets are static App composition metadata, not Core domain concepts and not
authorization. Every implemented tool belongs to one of `decks`, `scryfall`,
`stats`, `archidekt`, or `playgroup`. `decks`, `scryfall`, and `stats` are
default-enabled when implemented; Archidekt and Playgroup remain explicit.
Official community tag evidence belongs to `scryfall`; deterministic local
category-rule operations belong to `decks`.

At startup, App resolves `default`, `all`, `none`, or an explicit canonical
toolset list. Visible tools are the intersection of implemented toolsets,
selected toolsets, and mode permissions. Invocation-time mode guards remain
mandatory even when registration hides a tool. Toolsets never weaken mode
authority and credentials never silently enable a toolset.

Tool registration remains static for the MCP session. Under accepted
AMEND-005, the capability resource reports implementation, selection, and
configured-but-unverified credential state separately and without provider
I/O, but the server does not advertise `listChanged`. A selection change requires restart. Unknown names fail before
transport with a sanitized error. `all` includes every implemented stable
toolset but no experimental capability; `none` leaves only initialization and
the capability resource.

The default surface is the ordinary LLM working set. The all-toolset surface
proves complete access. Both are derived manifests rather than compatibility
targets. Every surface edit updates its child matrix, toolset manifest,
default/all per-mode totals, capability fixture, and canonical schema snapshot.

### North-Star Acceptance Gate

A remaining child is not decision-complete merely because it wraps all desired
provider operations. It must answer, with objective fixtures or E2E scenarios:

1. Which concrete player or deckbuilding questions become answerable?
2. Is each result a source fact, source evidence, exact derivation,
   parser-derived classification, sampled estimate, heuristic, or explicit
   unknown?
3. What input, stored revision, snapshot, assumptions, or provider time bounds
   determinism and replay?
4. Which missing, stale, partial, unsupported, or unavailable states remain
   visible?
5. Does the MCP return evidence or execute an explicit operation without
   making the deckbuilding judgment?
6. Can an LLM complete one representative workflow without choosing among
   redundant or ambiguously overlapping tools?

### Review And Approval State

Each child README must include this durable record:

```markdown
## Planning Approval

- Status: Draft | Changes requested | Approved
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
```

Approval means the packet is decision-complete. `Implementation authorized`
remains `No` until a later explicit request and lifecycle transition.

### Guardrail Amendments

When a child conflicts with a guardrail:

1. Mark that child and later registry entries blocked.
2. Record the proposed change and affected children in the umbrella README,
   SRD, and SADD.
3. Review and accept or reject the amendment.
4. Update already approved affected children if the change is accepted.
5. Resume authoring only when the registry has no unresolved conflict.

Topic detail that does not alter a guardrail stays within the child and does
not require an umbrella amendment.

## Data Design

The program persists only Markdown documents and Git history. The umbrella
README registry is the program status view. Child READMEs own their approvals.
The implementation plan owns authoring sequence and phase evidence. FIXTURES
owns reusable review checklists and scenarios.

No generated status file, database, or custom planning tool is introduced.
Lifecycle movement uses ordinary directory moves under `planned/`,
`in-progress/`, and `completed/`.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Validation |
| --- | --- | --- | --- | --- | --- |
| Umbrella README | Program summary, guardrails, registries, and status. | Program lifetime | Markdown index | SRD, SADD, implementation plan | Registry and link inspection |
| Umbrella SRD | Testable authoring and governance requirements. | Program lifetime | Requirement IDs | Repository PLC guidance | Traceability review |
| Umbrella SADD | Decomposition, state, amendment, and approval design. | Program lifetime | Planning protocol | SRD | Design review |
| Umbrella implementation plan | Sequential packet queue and exit criteria. | Until all children approved | Authoring phases | Registry and approvals | Phase-status inspection |
| Umbrella fixtures | Review artifacts and acceptance scenarios. | Program lifetime | Checklists | SRD requirements | Acceptance-matrix review |
| Child PLC | One topic's decision-complete implementation plan. | Independent lifecycle | Standard five-file packet | Approved prerequisites | Child-specific review |
| Hardening child | Pre-statistics contract correction, exact identity workflow, and provider ownership decomposition. | Independent lifecycle | Two `deck_*` tools and replaced schemas only after approval | Completed children 3, 5, 6, 7, and 8 | Schema, behavior-preservation, audit, and lifecycle review |

## Runtime And Data Flow

The planning workflow is:

1. Review and approve the umbrella packet.
2. Move the umbrella to `in-progress/` and draft child 1.
3. Validate that child's five-file packet and update the umbrella registry.
4. Draft the next child only after the preceding draft is complete and validated.
5. If a guardrail conflict appears, stop and process an umbrella amendment.
6. Repeat through child 12, leaving every unimplemented child in `planned`.
7. Review each child independently and record approval or requested changes.
8. Move the umbrella to `completed/` after all twelve approvals are recorded.

Child implementation may happen in separately authorized sessions. It does not
advance the authoring queue unless its findings require an approved amendment.

## MCP Surface, Schemas, And Diagnostics

This umbrella changes no MCP surface by itself. The capability-toolset child
owns startup selection and registration. Every later child inventories its
exact tool, resource, prompt, toolset, and mode effects. The stable target
permits no advisor prompts and no dynamic tool-list dependency.

## Adapter And Provider Contracts

The umbrella records only provider planning boundaries. Provider contracts are
re-verified and designed in their owning child:

- Scryfall uses official API reads plus explicit official bulk cards, rulings,
  Oracle tags, and art tags in one shared corpus with immutable request replay.
- Archidekt is an isolated observed contract with conservative mutation tests.
- Playgroup is bounded by its documented public API.
- Moxfield is manual interchange only.
- Scryfall community-tag evidence is acquired only through official bulk data
  and is separately labeled from card facts; no unsupported website transport exists.

Previously gathered observations are evidence leads, not substitutes for
child-session verification.

## Error Handling And Failure Modes

| Failure | Program response |
| --- | --- |
| A child draft is incomplete or unvalidated. | Complete its packet checks before drafting the next child. |
| A child conflicts with a guardrail. | Block the queue and process an umbrella amendment. |
| Two child drafts overlap or are developed in parallel. | Stop later drafting until the earlier packet is complete and validated. |
| A child starts implementation while planned. | Stop production edits and restore the lifecycle/authorization boundary. |
| Existing PLC disposition is unclear. | Leave it unchanged and resolve it in the audit child. |
| An external provider fact may be stale. | Re-verify it in the provider child; do not promote it to a guardrail. |

## Cross-Cutting Concepts

- Determinism applies both to future product behavior and to repeatable planning
  acceptance checks.
- Approval state and implementation authority are separate fields.
- Unknowns are recorded explicitly rather than resolved through optimistic
  assumptions.
- Secrets, provider payloads, and user data do not belong in the umbrella.
- Child scope remains narrow even when a provider offers adjacent features.
- Current code and tests outrank completed or stale planning documents.

## Project Boundaries

The umbrella changes documentation only. Children must preserve the agreed
future boundary: `MtgMcp.Core`, `MtgMcp.App`, `MtgMcp.Decks`,
`MtgMcp.Scryfall`, `MtgMcp.Archidekt`, `MtgMcp.Playgroup`, and
`MtgMcp.Statistics`. Core stays dependency-light; deck persistence, statistics,
and provider adapters stay isolated; and App owns MCP composition. Durable
runtime data is split only between `decks.db` and unified `scryfall.db`.
Architecture tests belong in the foundation and affected capability children,
not this packet.

## Readability And Documentation

Use stable child slugs and requirement IDs. Keep shared policy here and link to
it rather than copying volatile prose into every child. Child packets may quote
the guardrails needed for review but may not silently reinterpret them.
Remove template placeholders before requesting child approval.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| PROG-001 through PROG-003 | Fixed registry and sequential authoring workflow. | Registry, approval record, and creation-diff inspection. |
| PROG-004, PROG-010, PROG-011 | Separate planning approval, implementation authorization, and lifecycle states. | Path and README inspection. |
| PROG-005, PROG-006 | Central guardrails and blocking amendment flow. | Child conformance and amendment review. |
| PROG-007, PROG-009, PROG-014 | Standard child contract and acceptance checklist. | Packet and traceability inspection. |
| PROG-008, PROG-016 | Audit-first sequence and no premature PLC disposition. | Git diff and registry review. |
| PROG-012, PROG-013 | Required and post-cutover registries. | README comparison with implementation plan. |
| PROG-015 | Provider-specific review requirements. | Provider checklist in FIXTURES. |
| PROG-017 through PROG-019, PROG-021 | Static toolset registry, startup selection, mode intersection, and capability projection. | Default/all/none discovery matrices and capability reconciliation. |
| PROG-020 | North-star acceptance gate in every remaining child. | Child workflow/evidence checklist and representative E2E fixture. |
| PROG-022 through PROG-024 | Unified official Scryfall corpus, removed Tagger capability, explicit bulk boundary, authoritative queries, and deferred local evaluator. | Cross-packet terminology, surface/count, provider-contract, and deferred-registry review. |
| PROG-025 | Pre-statistics hardening child with exact identity and explicit non-goals. | Child traceability, schema/surface matrices, characterization tests, and audit plan. |

## Implementation Phases

The implementation phases are planning-document phases. They create no runtime
behavior. The exact sequence and exit criteria are in
[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).

## Test Architecture

Validation is documentation-focused:

- Structural checks confirm the five required files and lifecycle paths.
- Consistency checks compare child order, dependencies, slugs, and statuses.
- Review scenarios exercise approval, amendment, and blocked-state behavior.
- Relative-link inspection prevents dead navigation.
- `git diff --check` catches whitespace defects.
- Git diff review confirms no production files or multiple child directories
  entered a child-authoring change.

No .NET build or runtime provider test is justified for the umbrella packet.

## Framework And External Notes

Provider links in the SRD are planning references. The relevant child must
inspect current official documentation and terms because these contracts can
change. A permissive robots policy is not API endorsement, popularity is not
quality evidence, and deterministic processing does not make stale or inferred
data factual.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Sequential sibling child packets | Decision | More sessions but smaller review units. | Preserve the fixed queue. |
| Existing PLC overlap | Risk | Reviewers may see competing plans. | Audit and classify; do not move packets preemptively. |
| Provider drift | Risk | Child assumptions may age before drafting. | Re-verify in each provider child. |
| Post-cutover evidence sources | Deferred | Not required for `0.9.0` cutover planning. | Draft independent PLCs after cutover. |
| Judgment and simulation features | Deferred | Could violate the evidence-server boundary. | Require individual experimental feasibility PLCs. |

## Glossary

- **Umbrella PLC:** This planning-only packet governing child creation and review.
- **Child PLC:** One independently reviewed packet for a single rewrite topic.
- **Planning approval:** Confirmation that a child is decision-complete.
- **Implementation authorization:** A separate explicit request to begin code changes.
- **Guardrail:** A cross-child product, architecture, safety, or quality decision.
- **Required child:** One of the twelve packets needed to complete this planning program after AMEND-005 acceptance.
- **Post-cutover topic:** Registered work that is not part of the required queue.
