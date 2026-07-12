# Evidence-First MCP Rewrite Program PLC Packet

## Lifecycle

- Status: In progress
- Folder: `docs/llms/plcs/in-progress/evidence-first-mcp-rewrite-program/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-12
- Current phase: children 1–10 complete; child 11's transparent-preset proposal awaits independent review

## Summary

This packet governs the decomposition of the evidence-first MCP rewrite into
twelve smaller PLCs under accepted AMEND-005. Its deliverable is independently reviewable planning packets,
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
| Add a pre-statistics hardening child and expand to twelve required children. | Accepted by AMEND-005 | Contract honesty, identity reconciliation, and proven adapter boundaries need independent review before statistics expands the system. | [Required child registry](#required-child-registry) |
| Register post-cutover topics without drafting them. | Accepted | Popularity and experimental work must not expand the stable rewrite or cutover gate. | [Post-cutover registry](#post-cutover-registry) |
| Amend this packet before changing a shared guardrail. | Accepted | Cross-topic changes must be visible to every dependent child. | [Amendments](SADD.md#guardrail-amendments) |

## Program Amendments

| ID | Date | Status | Change | Authority |
| --- | --- | --- | --- | --- |
| AMEND-001 | 2026-07-03 | Accepted | Permit all then-required ten children to be drafted sequentially in one planning run. Remove the prerequisite that one child be approved before the next is drafted. Preserve separate packets, per-child validation, independent review, and the prohibition on production implementation. | Explicit repository-owner request |
| AMEND-002 | 2026-07-03 | Accepted | Expand the then-numbered child 6 (Archidekt) and stable cutover scope to include folder organization and named snapshot lifecycle/guarded restore. Reconcile the audit disposition, child requirements, live cleanup, deferred registry, and derived surface baseline. | Explicit repository-owner request |
| AMEND-003 | 2026-07-04 | Accepted | Add capability toolsets as a cross-cutting guardrail, create a dedicated toolset child before provider implementations, require north-star acceptance checks in every remaining child, and distinguish default-profile discovery from the complete stable surface. | Explicit repository-owner request |
| AMEND-004 | 2026-07-04 | Accepted | Unify official Scryfall cards, rulings, Oracle tags, and art tags in `scryfall.db`; remove the planned Tagger adapter/store/toolset; expand child 6 to corpus/evidence; replace child 10 with deterministic caller-defined deck categorization; register local Scryfall query evaluation as deferred work. | Explicit repository-owner implementation direction |
| AMEND-005 | 2026-07-06 | Accepted | Add `mcp-contract-and-adapter-hardening` before statistics; separate implementation state from credential configuration; replace the flat batch-change schema; add exact-only deck identity preview/apply; decompose Scryfall and Archidekt owners without changing provider behavior; add no legality capability; recalculate the target surfaces. | Explicit repository-owner implementation request |

## Program Guardrails

Every child PLC shall inherit the accepted decisions. The remaining planned children
are reconciled to accepted AMEND-004 and AMEND-005, and each child still requires its own review and
implementation authorization:

- The MCP returns evidence, provider data, explicit workflow operations, and
  exact mathematics. The client LLM makes deckbuilding decisions.
- Stable releases contain no advisor prompts, intent inference, weak-card
  judgments, replacement recommendations, blended quality scores, or
  strategic automation.
- Stable tool names use the `deck_*`, `scryfall_*`, `archidekt_*`,
  `playgroup_*`, and `stats_*` capability prefixes.
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
  `archidekt` and `playgroup` require explicit selection.
- Under accepted AMEND-005, the capability resource reports implementation
  separately from credential configuration. It never treats static
  registration or configured-but-unverified credentials as proof of provider
  availability and never performs provider I/O while rendering metadata.
- Operation modes are `read-only`, `local`, and `remote`; `local` is the
  default for the rewrite.
- Provider-neutral, dependency-light logic belongs in `MtgMcp.Core`.
  Persistence, statistics, provider adapters, and MCP hosting remain isolated.
- Local deck storage is format-neutral with Commander as the first fully
  tested workflow.
- Durable and rebuildable data use separate `decks.db` and `scryfall.db` files.
  Official Scryfall card/ruling facts and community tag evidence share the
  latter but retain different evidence labels and schemas.
- Multi-gigabyte Scryfall corpus synchronization is explicit, never a startup,
  read, timer, or background side effect. Provider cache eligibility uses a
  configurable 24-hour default; immutable snapshots never expire.
- New arbitrary Scryfall queries remain provider-authoritative. A future local
  query engine requires its own PLC and is not a `0.9.0` dependency.
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
| 4 | [`manual-deck-interchange`](../../completed/manual-deck-interchange/README.md) | Define native, Archidekt, and Moxfield manual import/export artifacts. | Local deck model | Repository-owner implementation authorization recorded | Approved; implementation and cleanup completed |
| 5 | [`mcp-capability-toolsets`](../../completed/mcp-capability-toolsets/README.md) | Define startup-selected capability groups, default/all/none profiles, mode intersection, and surface governance. | Foundation, local decks, and interchange registration | Repository-owner implementation authorization recorded | Approved; implementation completed |
| 6 | [`scryfall-corpus-and-evidence`](../../completed/scryfall-corpus-and-evidence/README.md) | Define the shared official bulk corpus, authoritative query cache, immutable replay, and card/ruling/tag evidence. | Toolset foundation and local card identity | Repository-owner authorization recorded | Approved; implementation and retained full-corpus acceptance completed |
| 7 | [`archidekt-deck-sync`](../../completed/archidekt-deck-sync/README.md) | Define Archidekt deck sync, folder organization, and named snapshot lifecycle/restore. | Deck, interchange, toolset, and Scryfall contracts | Repository-owner implementation authorization recorded | Approved; implementation completed |
| 8 | [`playgroup-public-api`](../../completed/playgroup-public-api/README.md) | Define the complete documented Playgroup public API surface. | Foundation and toolset boundaries | Repository-owner implementation authorization recorded | Approved; implementation completed |
| 9 | [`mcp-contract-and-adapter-hardening`](../../completed/mcp-contract-and-adapter-hardening/README.md) | Define capability/schema honesty, exact-only deck identity reconciliation, and cohesive Scryfall/Archidekt ownership before statistics. | Children 3, 5, 6, 7, and 8 | Repository-owner approval and authorization recorded | Approved; implementation completed |
| 10 | [`exact-deck-statistics`](../../completed/exact-deck-statistics/README.md) | Define provider-independent exact probability and composition analysis over caller-supplied populations. | Local deck, toolset, and approved hardening contracts | Child 9 completion | Approved; implementation completed |
| 11 | [`deterministic-deck-categorization`](../../planned/deterministic-deck-categorization/README.md) | Define explicit inline or transparent preset tag rules, deterministic category preview, and guarded application. | Local deck, Scryfall corpus, toolset, and hardening contracts | Child 9 completion and child 10 review | Amended with `common-v1` proposal; independent child review required |
| 12 | [`rewrite-stabilization-cutover`](../../planned/rewrite-stabilization-cutover/README.md) | Define cross-module stabilization, release, rollback, and PLC cleanup. | Children 1 through 11 | Child 11 draft validated | Drafted; AMEND-005 reconciliation required |

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
| `popularity-evidence-sources` | Permissioned popularity/tournament cohorts plus deterministic predicate-based deck-composition distributions, with source populations and no quality scoring. |
| `experimental-goldfish-feasibility` | Rules-engine and simulation feasibility before implementation commitment. |
| `experimental-deck-weakness-evidence` | Factual signals for LLM assessment without MCP-selected weak cards. |
| `experimental-budget-alternative-evidence` | Price and similarity evidence without MCP-selected replacements. |
| `local-scryfall-query-engine` | Differentially verified local Scryfall query evaluation with explicit coverage and provider fallback whenever parity is unproven. |

## Project And Surface Impact

The umbrella itself changes documentation only. The approved audit governed
the now-completed foundation deletion/reuse work. Other planned children remain
reference material and implementation-ineligible until separately approved and
activated.

## Current Open Questions

AMEND-005, hardening, and exact statistics are complete. Categorization is the
next independently reviewable child and remains unauthorized until the
repository owner approves its packet and explicitly authorizes implementation.
Its packet now proposes one deliberately small sane-default mechanism: each
request must choose fully inline rules or explicitly select the immutable,
transparent `common-v1` preset and bind desired roles to existing categories.
The exact initial role/tag mapping must be independently reviewed before
implementation. This is child-11 contract work, not a deferred feature or a
new toolset; it adds no automatic selection, extra tool/resource, stored rule
profile, or override language.

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
- [ ] All twelve required child packets exist and are approved after AMEND-005 acceptance.
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
| 2026-07-03 | Provider follow-up research and owner decisions | Partly superseded by AMEND-004 | Archidekt create/delete cleanup was proven live and its available-API risk accepted; Playgroup writes are owner-approved fixture-only for the pinned no-cleanup contract; manual interchange syntax evidence was refreshed. The former website-acquisition direction is no longer planned. |
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
| 2026-07-04 | Manual interchange implementation, audits, provider UI acceptance, and cleanup | Passed | All four consolidated tools, native/generic/provider artifact workflows, bounds, official-client dummy-deck workflows, package validation, audits, and per-assembly coverage gates pass. Dated authenticated UI checks make Archidekt and Moxfield available with empirical companion-only limits; the repository owner confirmed both disposable decks were deleted, closing XCHG-017. |
| 2026-07-04 | Archidekt child approval and activation | Passed | Repository owner approved the Archidekt PLC at `e256a37`, authorized implementation with strong rate-limit handling, and activated Phase 0. |
| 2026-07-04 | AMEND-003 capability-toolset guardrail | Accepted | Repository owner required startup-selectable toolsets, north-star acceptance checks in all remaining children, a dedicated toolset child before Scryfall, and immediate consolidation of the interchange format catalog tools. |
| 2026-07-04 | AMEND-003 dependent reconciliation | Superseded by proposed AMEND-004 | All remaining packets gained toolset, surface-rationale, evidence-boundary, and north-star checks; AMEND-004 replaces its derived surface totals. |
| 2026-07-04 | Capability-toolset implementation and closure | Passed | Static default/all/none/explicit selection, exact deck ownership, mode intersection, schema-version-2 capability metadata, source and installed-package Commander workflows, all audits, 160 offline tests, and per-assembly coverage gates passed; the child moved to `completed/`. |
| 2026-07-04 | Official Scryfall bulk/tag research | Supersedes unsupported Tagger acquisition plan | The official metadata endpoint exposed `all_cards`, `rulings`, `oracle_tags`, and `art_tags` with gzip JSONL downloads. Observed tag objects included hierarchy/alias metadata and weighted, optionally annotated Oracle/illustration assignments. Proposed AMEND-004 replaces scraping and separate Tagger persistence with one supported corpus. |
| 2026-07-04 | AMEND-004 dependent reconciliation | Drafted for review | Child 6 and child 10 were replaced, all incomplete dependents and durable guidance were reconciled, and proposed current/default/all baselines became 7/23/23, 31/52/52, and 56/78/91 respectively. No implementation was authorized. |
| 2026-07-04 | AMEND-004 and Scryfall child approval | Accepted and activated | The repository owner approved the unified official corpus direction and child packet. The eighteen-tool implementation and automated gates are complete; the explicit retained-directory full-corpus acceptance remains open. |
| 2026-07-04 | Scryfall automated implementation, audits, and bounded live acceptance | Passed with explicit manual gate | All 18 tools, 197 offline tests, exact mode/toolset surfaces, per-assembly coverage, package/install smokes, official metadata/card access, and the official-client 60-card Red/White Weenies workflow pass. Implemented mode totals are 21/41/41. Only the separately consented multi-gigabyte full-corpus acceptance remains open. |
| 2026-07-04 | Archidekt implementation and lifecycle closure | Passed | All 23 opt-in tools, exact 11/12/23 mode visibility, provider/local composition, composed request budgets, audits, full gates, package smokes, and the final production-paced private deck/folder/snapshot lifecycle pass. At that milestone, `all` mode totals were 32/53/64; default remained 21/41/41. |
| 2026-07-04 | Playgroup child approval and activation | Passed | Repository owner approved the pinned official API child, authorized implementation, retained fixture-only writes because the contract has no cleanup, and activated the packet on the rewrite branch. |
| 2026-07-04 | Playgroup implementation and lifecycle closure | Passed | All 15 official operations plus redacted auth status, exact 14/14/16 visibility, lossless provider evidence, conservative rate handling, fixture-only writes, audits, coverage, packages, and installed MCP smokes passed. No key was configured, so the safe `/me` live test remains explicitly unexecuted. Current `all` totals are 46/67/80; default remains 21/41/41. |
| 2026-07-06 | Playgroup packaged live acceptance | Passed | All 14 safe reads passed through the installed MCP against the owner-authorized playgroup, both writes remained fixture-only, and zero writes were sent. The run exposed and resolved the unbounded all-commander turn-damage result by requiring exact caller-selected row evidence with a bounded aggregate fetch and full-source checksum. |
| 2026-07-06 | AMEND-005 and hardening child approval | Accepted and activated | The repository owner approved the decision-complete plan, authorized implementation, and activated Phase 1. The accepted targets are 22/43/43 default, 47/69/82 all, 32/54/54 final default, and 57/80/93 final all. |
| 2026-07-06 | Hardening implementation and lifecycle closure | Passed | Capability schema 6, the closed batch union, 25 deck tools including exact identity preview/apply, 47/69/82 complete-profile counts, provider ownership refactors, full offline/package/coverage/dependency/audit gates, and bounded read-only Scryfall/Archidekt live checks passed. Child 9 moved to completed; child 10 remains unauthorized pending owner review. |
| 2026-07-09 | Exact-statistics independent review and activation | Passed | Structured bounded outcomes, explicit format-neutral deck selectors, caller numeric values, exact turn/mulligan/mana/package semantics, one request-wide work budget, schema descriptions, and the 90-tool post-child surface were locked. The owner authorized implementation and Phase 1 became active. |
| 2026-07-09 | Exact-statistics implementation and lifecycle closure | Passed | All eight exact read tools, structured failure outcomes, explicit selector evidence, 30/51/51 default and 55/77/90 all surfaces, 630 offline tests, 96.27 percent Statistics line coverage, package/install smokes, dependency checks, audits, and the independent-formula 99-card workflow passed. Child 10 moved to completed; child 11 remains unauthorized pending independent review. |
| 2026-07-12 | Categorization sane-default proposal | Drafted for review | Child 11 now permits an explicit immutable `common-v1` preset or complete inline rules through the same three tools. Preset schema discovery, full expansion, category binding, fingerprinting, and no-default/no-override boundaries are specified; the exact role/tag artifact still requires independent approval before implementation. |

## Completion Notes

Complete this packet when all twelve required child PLCs under accepted AMEND-005 have been authored and
independently approved. Drafting all children leaves this umbrella in progress
until those reviews occur. Child implementation and the `0.9.0` release have
independent lifecycle and completion evidence.
