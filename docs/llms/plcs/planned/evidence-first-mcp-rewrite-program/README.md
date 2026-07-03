# Evidence-First MCP Rewrite Program PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/evidence-first-mcp-rewrite-program/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: umbrella packet review

## Summary

This packet governs the decomposition of the evidence-first MCP rewrite into
ten smaller PLCs. Its deliverable is approved planning packets, not production
code. Each child is drafted in its own agent session, reviewed independently,
and approved before the next child is drafted.

The program exists to prevent one planning session from coupling unrelated
provider, persistence, statistics, and release decisions. It fixes the shared
product and architecture guardrails while leaving each child responsible for
the detailed contracts, fixtures, risks, and acceptance criteria in its topic.

## Packet Contents

- [SRD.md](SRD.md): program requirements, acceptance criteria, and child review rules.
- [SADD.md](SADD.md): packet decomposition, registry, lifecycle, and amendment design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): one-child-per-session authoring sequence.
- [FIXTURES.md](FIXTURES.md): document acceptance artifacts and review scenarios.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Use an umbrella PLC that authorizes planning only. | Accepted | The rewrite must be decomposed before any child implementation starts. | [Planning boundary](SADD.md#planning-and-implementation-boundary) |
| Draft one child PLC per agent session. | Accepted | Each topic needs focused investigation and an independent review checkpoint. | [Authoring protocol](IMPLEMENTATION_PLAN.md#per-child-authoring-protocol) |
| Require approval before drafting the next child. | Accepted | Later packets must consume reviewed decisions rather than unstable drafts. | [Review state](SADD.md#review-and-approval-state) |
| Keep ten required children in dependency order. | Accepted | The queue separates audit, foundation, capabilities, and cutover while preserving dependencies. | [Required child registry](#required-child-registry) |
| Register post-cutover topics without drafting them. | Accepted | Popularity and experimental work must not expand the stable rewrite or cutover gate. | [Post-cutover registry](#post-cutover-registry) |
| Amend this packet before changing a shared guardrail. | Accepted | Cross-topic changes must be visible to every dependent child. | [Amendments](SADD.md#guardrail-amendments) |

## Program Guardrails

Every child PLC shall inherit these decisions:

- The MCP returns evidence, provider data, explicit workflow operations, and
  exact mathematics. The client LLM makes deckbuilding decisions.
- Stable releases contain no advisor prompts, intent inference, weak-card
  judgments, replacement recommendations, blended quality scores, or
  strategic automation.
- Stable tool names use the `deck_*`, `scryfall_*`, `archidekt_*`,
  `playgroup_*`, `stats_*`, and `tagger_*` capability prefixes.
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

The registry records authoring dependencies, not implementation permission.
Slugs remain plain text until their packets exist so this packet has no broken
links.

| Order | Child slug | Purpose | Technical dependencies | Authoring gate | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | `legacy-surface-audit-and-disposition` | Inventory and classify the current product surface and reusable evidence. | Umbrella guardrails | Umbrella approved | Not drafted |
| 2 | `rewrite-skeleton-foundation` | Define the clean skeleton, repository wiring, modes, evidence, and module boundaries. | Approved audit disposition | Child 1 approved | Not drafted |
| 3 | `local-deck-store` | Define the local deck domain, SQLite persistence, and `deck_*` mutations. | Approved foundation boundaries | Child 2 approved | Not drafted |
| 4 | `manual-deck-interchange` | Define native, Archidekt, and Moxfield manual import/export artifacts. | Approved local deck model | Child 3 approved | Not drafted |
| 5 | `scryfall-evidence-snapshots` | Define immutable, rich, official Scryfall query snapshots. | Approved foundation and local card identity | Child 4 approved | Not drafted |
| 6 | `archidekt-deck-sync` | Define essential Archidekt operations and explicit pull/diff/push. | Approved deck, interchange, and Scryfall contracts | Child 5 approved | Not drafted |
| 7 | `playgroup-public-api` | Define the complete documented Playgroup public API surface. | Approved foundation boundaries | Child 6 approved | Not drafted |
| 8 | `exact-deck-statistics` | Define provider-independent exact probability and composition analysis. | Approved local deck model | Child 7 approved | Not drafted |
| 9 | `scryfall-tagger-cache` | Define exact cached Tagger assignments and conservative acquisition. | Approved local deck and Scryfall contracts | Child 8 approved | Not drafted |
| 10 | `rewrite-stabilization-cutover` | Define cross-module stabilization, release, rollback, and PLC cleanup. | Approved children 1 through 9 | Child 9 approved | Not drafted |

Although some technical dependencies are narrower, the authoring queue remains
sequential so each packet receives its own review before planning proceeds.

## Post-Cutover Registry

These topics are registered but are not required children, cutover dependencies,
or authorized drafts:

| Future slug | Boundary |
| --- | --- |
| `popularity-evidence-sources` | Permissioned popularity and tournament evidence without quality scoring. |
| `experimental-goldfish-feasibility` | Rules-engine and simulation feasibility before implementation commitment. |
| `experimental-deck-weakness-evidence` | Factual signals for LLM assessment without MCP-selected weak cards. |
| `experimental-budget-alternative-evidence` | Price and similarity evidence without MCP-selected replacements. |

## Project And Surface Impact

This umbrella packet changes documentation only. It creates no project,
package, tool, resource, prompt, operation-mode, configuration, persistence,
provider, or runtime behavior. Existing planned PLCs remain unchanged and
continue to be reference material until the audit child records their explicit
disposition.

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

- [ ] Umbrella packet reviewed and approved.
- [ ] Packet moved to `in-progress/` when the first child is drafted.
- [ ] Each required child drafted in a separate agent session.
- [ ] Each child reviewed and approved before the next child is drafted.
- [ ] Registry and validation evidence updated after each approval.
- [ ] All ten required child packets exist and are approved.
- [ ] Umbrella packet moved to `completed/` without implying code completion.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Packet structure, child-slug consistency, relative links, and anchors | Passed | All five files exist; the ten required slugs agree across the registry, design, and sequence; all local targets resolve. |
| 2026-07-03 | Template-residue inspection | Passed | No unresolved template marker remains in the packet. |
| 2026-07-03 | Scope inspection | Passed | The change creates only the umbrella packet and planned index entry; it creates no child PLC or production edit. |
| 2026-07-03 | External reference inspection | Passed with access notes | Playgroup and rules references resolved; Moxfield returned its JavaScript shell; Scryfall rejected automated document retrieval, so its canonical links remain recorded for child-session verification. |
| 2026-07-03 | `git diff --check` | Passed | No whitespace errors. |

## Completion Notes

Complete this packet when all ten required child PLCs have been authored and
approved. Child implementation and the `0.9.0` release have independent
lifecycle and completion evidence.
