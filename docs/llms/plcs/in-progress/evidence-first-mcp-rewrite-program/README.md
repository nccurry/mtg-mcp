# Evidence-First MCP Rewrite Program PLC Packet

## Lifecycle

- Status: In progress
- Folder: `docs/llms/plcs/in-progress/evidence-first-mcp-rewrite-program/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: capability-toolset amendment and dependent child reconciliation

## Summary

This packet governs the decomposition of the evidence-first MCP rewrite into
eleven smaller PLCs. Its deliverable is independently reviewable planning packets,
not production code. The children are drafted sequentially, one complete packet
at a time, and remain individually subject to review before implementation.

The program exists to prevent one planning session from coupling unrelated
provider, persistence, statistics, and release decisions. It fixes the shared
product and architecture guardrails while leaving each child responsible for
the detailed contracts, fixtures, risks, and acceptance criteria in its topic.

## Packet Contents

- [SRD.md](SRD.md): program requirements, acceptance criteria, and child review rules.
- [SADD.md](SADD.md): packet decomposition, registry, lifecycle, and amendment design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): sequential child-authoring sequence and review closure.
- [FIXTURES.md](FIXTURES.md): document acceptance artifacts and review scenarios.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Use an umbrella PLC that authorizes planning only. | Accepted | The rewrite must be decomposed before any child implementation starts. | [Planning boundary](SADD.md#planning-and-implementation-boundary) |
| Draft children sequentially rather than in parallel. | Accepted | Each topic needs a complete boundary and validation pass before the next draft starts. | [Authoring protocol](IMPLEMENTATION_PLAN.md#per-child-authoring-protocol) |
| Review children independently before implementation. | Accepted | Drafting the queue does not approve or authorize any child implementation. | [Review state](SADD.md#review-and-approval-state) |
| Keep eleven required children in dependency order. | Accepted | The queue separates audit, foundation, surface governance, capabilities, and cutover while preserving dependencies. | [Required child registry](#required-child-registry) |
| Register post-cutover topics without drafting them. | Accepted | Popularity and experimental work must not expand the stable rewrite or cutover gate. | [Post-cutover registry](#post-cutover-registry) |
| Amend this packet before changing a shared guardrail. | Accepted | Cross-topic changes must be visible to every dependent child. | [Amendments](SADD.md#guardrail-amendments) |

## Program Amendments

| ID | Date | Status | Change | Authority |
| --- | --- | --- | --- | --- |
| AMEND-001 | 2026-07-03 | Accepted | Permit all ten children to be drafted sequentially in one planning run. Remove the prerequisite that one child be approved before the next is drafted. Preserve separate packets, per-child validation, independent review, and the prohibition on production implementation. | Explicit repository-owner request |
| AMEND-002 | 2026-07-03 | Accepted | Expand child 6 and stable cutover scope to include Archidekt folder organization and named snapshot lifecycle/guarded restore. Reconcile the audit disposition, child requirements, live cleanup, deferred registry, and derived surface baseline. | Explicit repository-owner request |
| AMEND-003 | 2026-07-04 | Accepted | Add capability toolsets as a cross-cutting guardrail, create a dedicated toolset child before provider implementations, require north-star acceptance checks in every remaining child, and distinguish default-profile discovery from the complete stable surface. | Explicit repository-owner request |

## Program Guardrails

Every child PLC shall inherit these decisions:

- The MCP returns evidence, provider data, explicit workflow operations, and
  exact mathematics. The client LLM makes deckbuilding decisions.
- Stable releases contain no advisor prompts, intent inference, weak-card
  judgments, replacement recommendations, blended quality scores, or
  strategic automation.
- Stable tool names use the `deck_*`, `scryfall_*`, `archidekt_*`,
  `playgroup_*`, `stats_*`, and `tagger_*` capability prefixes.
- Every stable tool belongs to exactly one startup-selectable capability
  toolset. Toolsets control relevance; operation modes continue to control
  authority. A tool is visible only when it is implemented, its toolset is
  selected, and the active mode permits it.
- Toolset selection is static for one MCP session. Stable `0.9.0` does not rely
  on runtime tool-list mutation or `listChanged`; selection changes require a
  process restart.
- `default` selects implemented default-enabled toolsets, `all` selects every
  implemented stable toolset, and `none` exposes no tools. Experimental
  capabilities never enter `default` or `all` implicitly.
- `decks`, `scryfall`, and `stats` are default-enabled when implemented.
  `archidekt`, `playgroup`, and `tagger` require explicit selection.
- The capability resource reports implemented, available, enabled,
  default-enabled, and disabled toolsets without advertising unimplemented
  placeholder modules.
- Operation modes are `read-only`, `local`, and `remote`; `local` is the
  default for the rewrite.
- Provider-neutral, dependency-light logic belongs in `MtgMcp.Core`.
  Persistence, statistics, provider adapters, and MCP hosting remain isolated.
- Local deck storage is format-neutral with Commander as the first fully
  tested workflow.
- Durable and rebuildable data use separate `decks.db`, `scryfall.db`, and
  `tagger.db` files.
- The rewrite is a clean break targeting `0.9.0`; it provides no automatic
  legacy data or tool-schema migration.
- Implementation begins on `ncurry/evidence-first-mcp-rewrite` in a sibling
  worktree only after the current foundation changes have landed.
- Normal tests are deterministic and offline and maintain at least 90 percent
  line coverage for every production assembly.
- Source facts, source evidence, exact derivations, sampled estimates,
  parser-derived classifications, heuristics, and blended scores remain
  visibly distinct.
- Existing implementation code is reference evidence, not a source of
  abstractions to copy by default.

A guardrail change requires an umbrella amendment and review before a child may
adopt the change.

## Required Child Registry

The registry records technical dependencies and drafting state, not
implementation permission. A later draft may depend on an earlier draft, but
no child becomes implementation-authorized until it is separately reviewed and
explicitly activated.

| Order | Child slug | Purpose | Technical dependencies | Authoring gate | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | [`legacy-surface-audit-and-disposition`](../../completed/legacy-surface-audit-and-disposition/README.md) | Inventory and classify the current product surface and reusable evidence. | Umbrella guardrails | Repository-owner approval recorded | Approved; completed |
| 2 | [`rewrite-skeleton-foundation`](../../completed/rewrite-skeleton-foundation/README.md) | Define the clean skeleton, repository wiring, modes, evidence, and module boundaries. | Approved audit disposition | Repository-owner implementation authorization recorded | Approved; implementation completed |
| 3 | [`local-deck-store`](../../completed/local-deck-store/README.md) | Define the local deck domain, SQLite persistence, and `deck_*` mutations. | Foundation boundaries | Repository-owner implementation authorization recorded | Approved; implementation completed |
| 4 | [`manual-deck-interchange`](../manual-deck-interchange/README.md) | Define native, Archidekt, and Moxfield manual import/export artifacts. | Local deck model | Repository-owner implementation authorization recorded | Automated implementation complete; manual provider acceptance open |
| 5 | [`mcp-capability-toolsets`](../../planned/mcp-capability-toolsets/README.md) | Define startup-selected capability groups, default/all/none profiles, mode intersection, and surface governance. | Foundation, local decks, and interchange registration | AMEND-003 accepted | Drafted; review required before implementation |
| 6 | [`scryfall-evidence-snapshots`](../../planned/scryfall-evidence-snapshots/README.md) | Define immutable, rich, official Scryfall query snapshots. | Toolset foundation and local card identity | Child 5 approval and implementation | Drafted; AMEND-003 reconciled, re-review required |
| 7 | [`archidekt-deck-sync`](../../planned/archidekt-deck-sync/README.md) | Define Archidekt deck sync, folder organization, and named snapshot lifecycle/restore. | Deck, interchange, toolset, and Scryfall contracts | Child 6 draft validated | Drafted; AMEND-002/003 reconciled, re-review required |
| 8 | [`playgroup-public-api`](../../planned/playgroup-public-api/README.md) | Define the complete documented Playgroup public API surface. | Foundation and toolset boundaries | Child 7 draft validated | Drafted; AMEND-003 reconciled, re-review required |
| 9 | [`exact-deck-statistics`](../../planned/exact-deck-statistics/README.md) | Define provider-independent exact probability and composition analysis. | Local deck and toolset contracts | Child 8 draft validated | Drafted; AMEND-003 reconciled, re-review required |
| 10 | [`scryfall-tagger-cache`](../../planned/scryfall-tagger-cache/README.md) | Define exact cached Tagger assignments and conservative acquisition. | Local deck, Scryfall, and toolset contracts | Child 9 draft validated | Drafted; AMEND-003 reconciled, re-review required |
| 11 | [`rewrite-stabilization-cutover`](../../planned/rewrite-stabilization-cutover/README.md) | Define cross-module stabilization, release, rollback, and PLC cleanup. | Children 1 through 10 | Child 10 draft validated | Drafted; AMEND-003 reconciled, re-review required |

Although some technical dependencies are narrower, drafting remains sequential
so each packet is complete and validated before work begins on the next.
Dependency approval remains a child review and implementation gate; AMEND-001
allowed later drafts to describe proposed upstream contracts without implying
that those upstream drafts were approved.

## Post-Cutover Registry

These topics are registered but are not required children, cutover dependencies,
or authorized drafts. Their durable idea notes and promotion rules live in
[Potential Features](../../../../potential-features.md).

| Future slug | Boundary |
| --- | --- |
| `popularity-evidence-sources` | Permissioned popularity and tournament evidence without quality scoring. |
| `experimental-goldfish-feasibility` | Rules-engine and simulation feasibility before implementation commitment. |
| `experimental-deck-weakness-evidence` | Factual signals for LLM assessment without MCP-selected weak cards. |
| `experimental-budget-alternative-evidence` | Price and similarity evidence without MCP-selected replacements. |

## Project And Surface Impact

The umbrella itself changes documentation only. The approved audit governed
the now-completed foundation deletion/reuse work. Other planned children remain
reference material and implementation-ineligible until separately approved and
activated.

## Current Open Questions

None. Topic-specific questions belong in the relevant child packet. A question
that changes a program guardrail must be raised as an umbrella amendment.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements are testable and have acceptance criteria.
- [x] Major alternatives and tradeoffs are recorded.
- [x] Quality attributes are measurable or inspectable.
- [x] Core/App/adapter/test boundaries and dependency impact are explicit.
- [x] MCP surface and operation-mode guardrails are clear.
- [x] Provider safety belongs to the affected child PLC.
- [x] Documentation and abstraction-reuse expectations are clear.
- [x] SRD requirements map to design and validation.
- [x] The authoring plan has per-child exit criteria.
- [x] Deferred and post-cutover work is visible.

## Implementation Checklist

- [x] Umbrella packet reviewed and authoring amendment approved.
- [x] Packet moved to `in-progress/` before child drafting.
- [x] Each original required child drafted sequentially as a separate packet.
- [x] AMEND-003 toolset child drafted as a separate packet.
- [x] Each child structurally validated before the next draft begins.
- [ ] Each child independently reviewed before its implementation.
- [ ] Registry and validation evidence updated after each approval.
- [ ] All eleven required child packets exist and are approved.
- [ ] Umbrella packet moved to `completed/` without implying code completion.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Packet structure, child-slug consistency, relative links, and anchors | Passed | All five files exist; the ten required slugs agree across the registry, design, and sequence; all local targets resolve. |
| 2026-07-03 | Template-residue inspection | Passed | No unresolved template marker remains in the packet. |
| 2026-07-03 | Scope inspection | Passed | The program change creates ten documentation-only child PLCs and no production edit. |
| 2026-07-03 | External reference inspection | Passed with access notes | Playgroup and rules references resolved; Moxfield returned its JavaScript shell; Scryfall rejected automated document retrieval, so its canonical links remain recorded for child-session verification. |
| 2026-07-03 | `git diff --check` | Passed | No whitespace errors. |
| 2026-07-03 | Required child drafting | Passed | All ten standard five-file packets exist in registry order, remain Draft, and set implementation authorization to No. |
| 2026-07-03 | Cross-packet traceability and surface reconciliation | Superseded by approved scope change | The original 71-tool baseline was internally consistent before folder and snapshot support entered the Archidekt child. |
| 2026-07-03 | Two independent PLC review sets | Findings addressed | Valid comments were incorporated packet by packet; nine legacy packets are visibly retired/reference-only; temporary review files were removed as requested. |
| 2026-07-03 | Provider follow-up research and owner decisions | Updated | Archidekt create/delete cleanup was proven live and its available-API risk accepted; Playgroup writes are owner-approved fixture-only for the pinned no-cleanup contract; Tagger is technically viable under bounded public acquisition, with owner implementation acceptance still pending; manual interchange syntax evidence was refreshed. |
| 2026-07-03 | Follow-up child review | Passed with edit | Confirmed the ten-child split and corrected the foundation packet to consistently state zero tools, one resource, and zero prompts. |
| 2026-07-03 | Durable guidance consistency audit | Passed | Agent instructions, README and `llms.txt`, product/design guidance, architecture/provider docs, compatibility/versioning rules, PLC workflow docs, and review playbooks now distinguish the current server from the planned clean-break target and route rewrite work through an approved active child. |
| 2026-07-03 | Archidekt folder/snapshot scope amendment | Superseded baseline; scope retained | Folder tree/detail/create/update/move/empty-delete and named snapshot lifecycle/guarded restore entered stable Archidekt scope. AMEND-003 later renumbered Archidekt to child 7 and recalculated the complete surface. |
| 2026-07-03 | Audit approval and foundation Phase 0 activation | Passed | Repository owner approved the audit disposition and foundation PLC, authorized foundation implementation, moved the audit to `completed/`, moved the foundation to `in-progress/`, and retained all later children as unauthorized drafts. |
| 2026-07-03 | Foundation Phase 1 worktree isolation | Passed | Fetch/preflight found no target collision; the required branch and sibling worktree were created from `c2aeec8`; HEAD/merge-base and clean-status checks passed without modifying existing worktrees. |
| 2026-07-03 | Foundation Phase 2 skeleton and repository reconciliation | Passed | The rewrite branch removed the audit-disposed legacy implementation, restored only Core/App plus focused tests, reconciled task/CI/coverage/package/release wiring, passed post-implementation audits, and recorded detailed evidence in the child packet. |
| 2026-07-03 | Foundation Phase 3 contracts and runtime boundaries | Passed | The rewrite branch now has exhaustive result/evidence unions, the accepted operation-mode matrix, layered and sanitized configuration, versioned data-root resolution, and non-mutating clean-break detection. All focused and repository gates pass with 38 tests and at least 96.06% line coverage per production assembly; detailed audit and reconciliation evidence is recorded in the child packet. |
| 2026-07-03 | Foundation Phases 4-5 and lifecycle closure | Passed | The branch now hosts an official-SDK resources-only stdio MCP server, exposes exact initialization identity and `mtg://server/capabilities`, and validates process, official-client, and installed-package paths. All 59 tests and the per-assembly coverage gates pass; the child moved to `completed/`. |
| 2026-07-03 | Local deck child approval and activation | Passed | Repository owner approved the Local Deck Store PLC at `c15476d`, authorized implementation, and moved the packet to `in-progress/` with Phase 1 active. |
| 2026-07-03 | Local deck child lifecycle closure | Passed | All eighteen requirements, five phases, audits, offline tests, per-assembly coverage, exact surface checks, package build, process smoke, official-client MCP smoke, and installed-tool smoke passed; the packet moved to `completed/`. |
| 2026-07-04 | Manual interchange child approval and activation | Passed | Repository owner approved the Manual Deck Interchange PLC at `4cc041b`, authorized implementation, and moved the packet to `in-progress/` with Phase 1 active. |
| 2026-07-04 | Manual interchange automated implementation and audits | Passed with explicit manual gate | All four consolidated tools, native/generic/provider artifact workflows, bounds, official-client dummy-deck workflows, package validation, audits, 112 tests, and per-assembly coverage gates pass. Archidekt and Moxfield remain experimental because authenticated manual UI acceptance XCHG-017 is recorded as not run. |
| 2026-07-04 | AMEND-003 capability-toolset guardrail | Accepted | Repository owner required startup-selectable toolsets, north-star acceptance checks in all remaining children, a dedicated toolset child before Scryfall, and immediate consolidation of the interchange format catalog tools. |
| 2026-07-04 | AMEND-003 dependent reconciliation | Passed for draft review | All remaining packets identify their toolset, surface rationale, decision/evidence boundaries, unknown states, and composed north-star workflow. The merged interchange catalog yields current 7/23/23 and final `all` 48/70/83 planning totals. |

## Completion Notes

Complete this packet when all eleven required child PLCs have been authored and
independently approved. Drafting all children leaves this umbrella in progress
until those reviews occur. Child implementation and the `0.9.0` release have
independent lifecycle and completion evidence.
