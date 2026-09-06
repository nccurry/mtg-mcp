# Evidence-First Deckbuilding Evolution Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-06
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)
- Implementation authorized: No

## Implementation Strategy

Do the cleanup in thin vertical slices. The first two slices only move ownership
inside existing provider modules; they do not change user behavior. That gives
later source and simulation work a less fragile base.

Do not mix a provider extraction, a major MCP SDK upgrade, a new external
source, and a new public tool in one change. Each of those has a different
failure mode and should have a separate validation story.

The recommended first child is adapter ownership cleanup. It is the smallest
useful outcome: a maintainer can safely change Scryfall corpus/snapshots or
Archidekt decks/folders/snapshots without navigating a god class.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | Ratify the target and activate one narrow child. | EFD-001–013 | PLC/docs only | Review, link check, diff check | Owner selects a child and records implementation authority. | Planned |
| 1A | Give Scryfall stores real ownership. | EFD-002–005, EFD-010, EFD-013 | Scryfall, focused tests, architecture docs | Characterization, focused tests, lint/test/coverage | No behavior or surface change; database owner only owns connection/schema/composition. | Planned |
| 1B | Give Archidekt domains real ownership. | EFD-002–005, EFD-010, EFD-013 | Archidekt, focused tests, architecture docs | Characterization, fake HTTP, lint/test/coverage | No behavior or surface change; shared session and named domains own their code. | Planned |
| 2 | Prove MCP SDK/toolchain compatibility before upgrades. | EFD-005, EFD-010, EFD-012, EFD-013 | App, E2E, packaging, dependency docs | Process/client/schema/package checks | A version decision is evidence-backed and separately reviewable. | Planned |
| 3 | Admit one high-value source, likely Commander Spellbook. | EFD-001, EFD-003–007, EFD-010, EFD-013 | New concrete adapter, App, fixtures/docs | Admission review, fake HTTP, surface/E2E checks | Opt-in evidence tools are attributable, bounded, and policy-compliant. | Planned |
| 4 | Fill proven exact-analysis gaps. | EFD-001, EFD-003–005, EFD-008, EFD-010–011, EFD-013 | Statistics and/or explicit deck analysis, App/tests | Independent formulas, surface/E2E, performance review if needed | New deterministic workflow answers a real declared-input question without inferred card roles. | Planned |
| 5 | Decide community and cohort source feasibility. | EFD-006–007, EFD-010, EFD-013 | Research/docs; source-specific child only if admitted | Current terms/contract review | Each source is explicitly admitted, deferred, or rejected. | Planned |
| 6 | Decide goldfish feasibility before implementing a simulator. | EFD-001, EFD-003–005, EFD-009–011, EFD-013 | New feasibility packet; no stable surface initially | Toy traces, calibration, policy review | Owner records accept/defer/reject with evidence. | Planned |
| 7 | Stabilize selected completed children. | All selected requirements | Docs, release, validation | Full gates, audits, release review | Contracts, docs, deferred items, and follow-ups are accurate. | Planned |

## Phase Details

### Phase 0: Ratify and split the work

- Problems solved: Prevents an attractive roadmap from becoming unbounded
  implementation authority.
- Included requirements: EFD-001 through EFD-013 as planning constraints.
- Out of scope: Production code, package changes, new tools, and data migration.
- Expected edits: This umbrella and one selected child packet only.
- Tests added: None.
- Validation: Markdown links, git diff --check, independent review of the
  selected child.
- Exit criteria:
  - The owner chooses one child, normally Phase 1A.
  - The child states its exact public-surface and persistence impact.
  - The child is moved to in-progress only after authorization.
- Rollback/fallback: Leave this roadmap planned and defer all code work.
- Cleanup: None.

### Phase 1A: Scryfall ownership extraction

- Problems solved: ScryfallCorpusStore, ScryfallSnapshotStore, and
  ScryfallRequestCoordinationStore currently forward every operation to one
  oversized ScryfallDatabase.
- Included requirements: EFD-002, EFD-003, EFD-004, EFD-005, EFD-010,
  EFD-013.
- Out of scope:
  - New Scryfall tools or schema fields.
  - New corpus formats or migration.
  - Local Scryfall query engine.
  - New tags or any tagger-site acquisition.
- Expected edits:
  - Move corpus SQL behavior into ScryfallCorpusStore.
  - Move snapshot SQL behavior into ScryfallSnapshotStore.
  - Move lease/pacing/metadata SQL behavior into ScryfallRequestCoordinationStore.
  - Leave the database owner with path, connection, schema, and composition.
  - Remove forwarding methods as real methods arrive.
- Tests added:
  - Pre-move characterization of result ordering, cache/snapshot state, import
    lifecycle, rollback/delete behavior, leases, pacing reservation, and typed
    failures.
  - Architecture assertion that each store owns its concrete operations.
- Validation:
  - Narrow Scryfall tests before and after each move.
  - task lint, task test, task coverage, and task surface:report.
  - Documentation and SQL-schema inspection.
- Exit criteria:
  - Tool schemas, modes, outputs, cache behavior, database format, and source
    semantics are unchanged.
  - ScryfallDatabase no longer contains corpus/snapshot/coordination workflow
    implementations.
  - No repository interface or generic persistence framework was introduced.
- Rollback/fallback: Revert only the extraction child; existing facade and
  database format remain stable.
- Cleanup: Delete forwarding bodies and stale summaries in the same change.

### Phase 1B: Archidekt ownership extraction

- Problems solved: Deck, folder, snapshot operations/transports forward to
  ArchidektOperationContext and ArchidektTransportContext, which retain the
  actual behavior.
- Included requirements: EFD-002, EFD-003, EFD-004, EFD-005, EFD-010,
  EFD-013.
- Out of scope:
  - New Archidekt provider routes.
  - Expanded account/social/collaboration automation.
  - Changes to write authority, request budget, or remote conflict semantics.
- Expected edits:
  - Create or retain one small shared HTTP/session owner for auth, pacing,
    retry, cooldown, request budget, and sanitized provider faults.
  - Move exact deck routes/workflows into deck transport/operations.
  - Move exact folder routes/workflows into folder transport/operations.
  - Move exact snapshot routes/workflows into snapshot transport/operations.
  - Keep ArchidektService as the stable public facade.
  - Delete ArchidektOperationContext and ArchidektTransportContext only after
    every behavior has a concrete home.
- Tests added:
  - Fake-HTTP characterization for deck, folder, and snapshot successes,
    failures, request counts, authentication retry, rate handling, fingerprints,
    confirmation, read-back verification, and redaction.
  - Source architecture tests that forbid the retired contexts.
- Validation:
  - Narrow Archidekt adapter tests first.
  - task lint, task test, task coverage, and task surface:report.
  - Existing opt-in live read-only checks only after all offline gates pass.
- Exit criteria:
  - No external behavior, tool, database file, or safety policy changes.
  - The common session owns cross-cutting transport state once.
  - Named domain classes contain the routes and workflows their names claim.
- Rollback/fallback: Revert the isolated child; public facade and adapter
  contract are unchanged.
- Cleanup: Remove pass-through contexts and correct the stale 90-tool test
  summary.

### Phase 2: MCP SDK and toolchain compatibility

- Problems solved: Package updates exist, including a major
  ModelContextProtocol version change, but the compatibility impact is unknown.
- Included requirements: EFD-005, EFD-010, EFD-012, EFD-013.
- Out of scope: New deck/product behavior or provider work.
- Expected edits:
  - Decide target SDK/protocol version from current official documentation.
  - Update a minimal package set in a dedicated branch/child.
  - Adapt static tool registration and structured-result code only where
    required.
  - Update analyzer/test packages only after the core MCP compatibility result
    is stable.
- Tests added:
  - Current and target official-client initialization.
  - JSON-schema/structured-content validation.
  - Installed package and process smoke coverage.
- Validation:
  - task lint, task test, task coverage, task surface:report.
  - task pack, task smoke:process, task smoke:mcp, and release:tool-smoke when
    package changes warrant them.
  - task deps:check and a vulnerable-package check.
- Exit criteria:
  - Exact toolset/mode surface is preserved unless the child explicitly approves
    a contract version change.
  - Client and package smoke paths pass.
- Rollback/fallback: Keep the pinned versions; do not force an SDK major bump
  merely because it exists.
- Cleanup: Remove compatibility shims that are not needed by the selected SDK.

### Phase 3: First admitted provider evidence

- Problems solved: The stable server needs a safe route to richer factual
  context without a generic web scraper.
- Included requirements: EFD-001, EFD-003 through EFD-007, EFD-010, EFD-013.
- Candidate: Commander Spellbook, because it publicly documents combo search
  syntax and an API contract. It is a candidate, not approval.
- Out of scope:
  - Ranking cards, suggesting additions, or treating combo popularity as quality.
  - Cross-source popularity blending.
- Expected edits:
  - Complete provider-admission record.
  - Add one concrete source project with source DTOs, transport, mapper,
    pacing, bounded cache/retention, and typed failures.
  - Add a small opt-in toolset with source-specific output contracts.
- Tests added:
  - Sanitized search/detail/empty/error fixtures.
  - Ordering, pagination, provenance, freshness, redaction, and output-bound
    tests.
  - Process/surface tests for opt-in visibility and read-only mode.
- Validation:
  - Provider admission review.
  - Fake-HTTP and App/E2E tests.
  - task lint, task test, task coverage, and task surface:report.
- Exit criteria:
  - Every returned row says what source it came from and what it means.
  - No source data is converted into a recommendation.
  - Normal tests remain network-free.
- Rollback/fallback: Disable the opt-in toolset and remove the isolated module;
  no unrelated provider changes.
- Cleanup: None beyond temporary fixture helpers.

### Phase 4: Declarative exact deck analysis

- Problems solved: Existing exact Statistics is intentionally caller-supplied.
  A new workflow is justified only if a player cannot reasonably express a
  useful deck question with current tools.
- Included requirements: EFD-001, EFD-003 through EFD-005, EFD-008,
  EFD-010, EFD-011, EFD-013.
- Out of scope:
  - Auto-tagging card roles.
  - Legality decisions.
  - Strategic play choice or sampled goldfish.
- Expected edits:
  - Define a small explicit input contract for deck selection/grouping.
  - Resolve selected-card evidence transparently.
  - Delegate math to provider-independent Statistics.
  - Add only materially distinct tool(s), if needed.
- Tests added:
  - Independent formula cases for 60- and 99-card populations.
  - Explicit category/group selection, draw/mulligan assumptions, and unknown
    card evidence cases.
  - Surface/E2E tests for result labels and bounds.
- Validation:
  - Focused exact math tests, task lint, task test, task coverage.
  - task surface:report if a tool is added.
  - A performance measurement only if the selected-card workflow proves hot.
- Exit criteria:
  - Results are exact, declared-input, and explainable.
  - No automatic role inference appears.
- Rollback/fallback: Defer the tool if the proposed contract duplicates existing
  stats operations.
- Cleanup: Remove duplicate calculation/presentation paths if the new workflow
  replaces one.

### Phase 5: Community and cohort source feasibility

- Problems solved: Players want discussions and popularity context, but those
  sources have material policy and population differences.
- Included requirements: EFD-006, EFD-007, EFD-010, EFD-013.
- Out of scope: Implementing a source merely because it is popular.
- Expected edits:
  - One admission/defer/reject record per researched source.
  - A Reddit-specific policy review before code; source-specific child only if
    approved.
  - A permissioned cohort-provider evaluation for EDHREC-style questions.
- Tests added: None until a source is admitted.
- Validation:
  - Re-check current terms and official documentation.
  - Review use, display, retention/deletion, attribution, rate limits, and
    downstream model handling.
- Exit criteria:
  - Each candidate has a durable decision and rationale.
  - No undocumented endpoint, browser automation, or cache is introduced.
- Rollback/fallback: Record defer/reject; exact and existing evidence workflows
  remain useful.
- Cleanup: Remove only research scaffolding that is no longer authoritative.

### Phase 6: Experimental goldfish feasibility

- Problems solved: Establish whether a limited game-flow model can be useful
  without misleading a player or becoming a rules engine.
- Included requirements: EFD-001, EFD-003 through EFD-005, EFD-009 through
  EFD-011, EFD-013.
- Out of scope:
  - Stable default toolset.
  - Multiplayer rules engine, stack/priority/layers, opponent decisions, deck
    recommendations, and hidden play-policy inference.
- Expected edits:
  - A standalone feasibility PLC with a closed model/policy vocabulary.
  - Toy decks with deliberately supported and unsupported mechanics.
  - Replay trace, coverage, seed, input fingerprint, and interval contract.
  - An explicit accept/defer/reject decision point before public implementation.
- Tests added:
  - Same-seed replay.
  - Policy/trace determinism.
  - Unsupported mechanic has no fabricated effect.
  - Output caps, cancellation, and uncertainty tests.
- Validation:
  - Offline toy fixtures and calibration review.
  - Independent review of assumptions and claimed meaning.
  - Performance measurement for a bounded named run if implementation proceeds.
- Exit criteria:
  - The owner accepts, defers, or rejects the experiment with documented
    evidence.
  - No stable simulation tool exists before acceptance.
- Rollback/fallback: Reject the experiment and keep exact analysis only.
- Cleanup: Do not revive old simulation architecture wholesale.

### Phase 7: Stabilize and release selected work

- Problems solved: A sequence of children can leave stale docs, ambiguous
  deferred items, or surface drift.
- Included requirements: All selected requirements.
- Out of scope: New capability work.
- Expected edits: Documentation, release notes, surface inventory, dependency
  record, and completed/deferred PLC disposition.
- Tests added: Regression tests only where a completed child exposed a gap.
- Validation: Full Task gates, relevant package/client smoke, documentation
  inspection, source audit, and git diff --check.
- Exit criteria:
  - Every selected child is complete, deferred through the packet's deferral
    rule, or superseded through an approved amendment.
  - Current docs match code and public contract.
  - No legacy compatibility shim or temporary forwarding code remains.
- Rollback/fallback: Keep the release queued until all selected criteria pass.
- Cleanup: Remove obsolete tests, code, fixtures, and docs only after their
  replacements prove behavior.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| A behavior-preserving refactor changes a provider edge case. | 1A, 1B | Characterize first, move one domain at a time, keep public facades, use fake HTTP/SQLite fixtures. | Adapter child owner |
| Public contract changes hide inside a package upgrade. | 2 | Isolate upgrade and run exact surface/schema/client/package tests. | MCP child owner |
| A source is technically reachable but not permitted or meaningful. | 3, 5 | Require admission record and current terms review before code. | Product/provider owner |
| Exact analysis grows role inference. | 4 | Require caller-declared groups and selected-card evidence. | Statistics child owner |
| Goldfish scope turns into rules-engine work. | 6 | Closed model, toy fixtures, explicit stop decision, no stable tool before approval. | Simulation child owner |
| Coverage hides semantic gaps. | All | Use characterization, independent formulas, E2E, and fixture quality review alongside coverage. | Reviewers |
| Dirty user changes overlap a child. | 1B | Rebase scope after the user’s mapper work is finalized; do not overwrite it. | Implementer |

## Completion Criteria

- [ ] Every selected Must requirement appears in an authorized child.
- [ ] A currently authorized child verifies every in-scope Must requirement or
  has an approved amendment that removes or replaces it; a deferral alone does
  not close the requirement.
- [ ] Every deferred future child or feasibility outcome records its rationale,
  owner, activation or review trigger, affected acceptance criteria, and why
  the completed phase still meets its exit criteria.
- [ ] Phase 1A and 1B preserve the current public surface and provider
  behavior while removing forwarding ownership.
- [ ] Each new provider is admitted or explicitly deferred before code starts.
- [ ] Exact and sampled analysis remain visibly distinct.
- [ ] No generic provider framework, scraper, rules engine, or recommendation
  system appears.
- [ ] Every completed child records focused and broad validation.
- [ ] Documentation, source limits, tool counts, and deferred work are current.
