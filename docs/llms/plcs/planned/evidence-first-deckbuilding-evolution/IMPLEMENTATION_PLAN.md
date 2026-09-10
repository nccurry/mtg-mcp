# Evidence-First Deckbuilding Evolution Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-10
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
useful outcome: a maintainer can safely change Scryfall card data/snapshots or
Archidekt decks/folders/snapshots without navigating a god class.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 0 | Ratify the target and activate one narrow child. | EFD-001–013 | PLC/docs only | Review, link check, diff check | Owner selects a child and records implementation authority. | Complete |
| 1A | Give Scryfall stores real ownership. | EFD-002–005, EFD-010, EFD-013 | Scryfall, focused tests, architecture docs | Characterization, focused tests, lint/test/coverage | No behavior or surface change. The database owner only owns connection, schema, and disposal. | Complete |
| 1B | Give Archidekt domains real ownership. | EFD-002–005, EFD-010, EFD-013 | Archidekt, focused tests, architecture docs | Characterization, fake HTTP, lint/test/coverage | No behavior or surface change; shared session and named domains own their code. | Complete |
| 2 | [Pin the current MCP and toolchain](../../completed/latest-mcp-and-toolchain/README.md). | EFD-005, EFD-010, EFD-012, EFD-013 | App, E2E, packaging, version and lock files | Process/client/schema/package checks | The current-only protocol and reproducible toolchain pass. | Complete |
| 3 | [Add Commander Spellbook evidence](../../completed/commander-spellbook-evidence/README.md). | EFD-001, EFD-003–007, EFD-010, EFD-013 | New concrete adapter, App, fixtures/docs | Source contract, fake HTTP, surface/E2E checks | Opt-in source evidence is attributable, bounded, and readable. | Complete |
| 4 | Review exact-analysis gaps. | EFD-001, EFD-003–005, EFD-008, EFD-010–011, EFD-013 | Statistics and/or explicit deck analysis, App/tests | Current-tool review and existing formula tests | Current tools cover the questions reviewed; the record says when to reopen this phase. | Deferred |
| 4A | [Repair official Scryfall tag grouping](../../completed/official-scryfall-tag-grouping-reliability/README.md). | EFD-001, EFD-003–005, EFD-007, EFD-010, EFD-013–014 | Core, Scryfall, App, focused tests/docs | Official-shaped fixture, hierarchy/validation tests, Task checks, surface check | Existing grouping follows source hierarchy and rejects bad source selectors without adding a tag system. | Complete |
| 5 | [Decide community and deck-group source feasibility](SOURCE_FEASIBILITY.md). | EFD-006–007, EFD-010, EFD-013 | Research/docs; source-specific child only if supported | Current API and access-rule check | Each source is explicitly added, deferred, or rejected. | Complete |
| 6 | [Build the authorized bounded deck mana and on-curve estimate](../../completed/deck-mana-on-curve-simulation/README.md). | EFD-001, EFD-003–005, EFD-009–011, EFD-013 | New OnCurve project and one read-only deck tool | Real-deck fixtures, replay, calibration, policy review | A clear public estimate is implemented and validated. | Complete |
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

### Phase 4A: Official Scryfall tag grouping repair

- Problems solved:
  - A direct tag on a card currently reaches the rule evaluator with only its
    own ID, so a parent selector cannot match the child even when
    `includeDescendants` is true.
  - The current validator checks deck category ownership but does not reject a
    selector with both or neither identity, an invalid tag type or weight, or
    duplicate primary priorities. Such a selector can behave as an ordinary
    non-match during synchronize mode.
  - `common-v1` uses `card-draw`, while the historical source snapshot contains
    `draw` and shows that `draw`, `removal`, and `recursion` are parent tags with
    no direct assignments. The final mapping must be rechecked against current
    official Scryfall data before it is committed.
- Included requirements: EFD-001, EFD-003 through EFD-005, EFD-007, EFD-010,
  EFD-013, EFD-014.
- Out of scope:
  - Any local card-tag system, tag assignment, alias, or hierarchy.
  - Tagger-site scraping, a Tagger adapter/store/toolset, or a new MCP tool.
  - A recommendation, inferred category meaning, or automatic category apply.
  - A new `common-v2`, compatibility alias, migration, or fallback mapping.
- Expected edits:
  - Add a narrow source-tag repair child with a pure rule grammar check in
    Core, source-ID and ancestry reads in Scryfall, and deck composition in
    App.
  - Correct `common-v1` in place using reviewed source tag IDs and declared
    descendant settings.
  - Split responsibilities if needed so MCP tool wrappers, source resolution,
    and pure rule evaluation do not accumulate in one file.
- Tests added:
  - Parent/child tag hierarchy tests using an official-shaped offline fixture.
  - Invalid selector, missing/ambiguous source tag, bad weight/type, and
    synchronize-no-removal tests.
  - `common-v1` source mapping and preset-to-inline equivalence tests.
  - Existing-surface and no-new-source-boundary checks.
- Validation:
  - Focused Core, Scryfall, and App tests first; then `task lint`, `task test`,
    `task coverage`, and the affected MCP surface checks.
  - Recheck current official Scryfall tag metadata/data before choosing the
    checked-in source IDs. Keep that external check out of normal offline tests.
- Exit criteria:
  - A direct child tag matches a selected parent only when descendants are
    allowed, across every source parent relationship.
  - Bad selectors fail explicitly before a preview or apply can change a deck.
  - `common-v1` has the same ID and role keys but uses only verified official
    source tags and produces visible source-based evidence.
  - No separate tag system or public MCP surface is introduced.
- Rollback/fallback: Keep the existing tools and source data. If current source
  data cannot support a reviewed preset mapping, leave that role unavailable and
  stop for an owner decision; do not guess a substitute tag.
- Cleanup: Remove duplicate or misleading selector logic and stale wording that
  describes the preset as immutable when this owner-approved defect correction
  changes it in place.

### Phase 1A: Scryfall ownership extraction

- Problems solved: The earlier `ScryfallCorpusStore` (renamed
  `ScryfallCardDataStore` in this child), ScryfallSnapshotStore, and
  ScryfallRequestCoordinationStore currently forward every operation to one
  oversized ScryfallDatabase.
- Included requirements: EFD-002, EFD-003, EFD-004, EFD-005, EFD-010,
  EFD-013.
- Out of scope:
  - New Scryfall tools or schema fields.
  - New card-data formats or migration.
  - Local Scryfall query engine.
  - New tags or any tagger-site acquisition.
- Expected edits:
  - Move card-data SQL behavior, including every card-data-state
    (`corpus_state`) field, into
    ScryfallCardDataStore.
  - Move snapshot SQL behavior into ScryfallSnapshotStore.
  - Move lease and pacing SQL behavior into ScryfallRequestCoordinationStore.
  - Leave the database owner with path, connection, schema, and disposal.
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
  - ScryfallDatabase no longer contains card-data, snapshot, or coordination
    workflow
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
  retry, cooldown, provider requests, and sanitized provider faults.
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
- Result (2026-09-07): Complete. The shared session and named deck, folder,
  and snapshot owners replaced both Context layers and the forwarding facade.
  Direct fake-HTTP tests construct each owner and confirm the retired contexts
  are absent. `task lint`, `task test`, `task coverage`, and
  `task surface:report` passed; Archidekt line coverage reached 91.07%, and the
  MCP surface did not change.

### Phase 2: Latest MCP and toolchain

This completed phase is detailed in the [latest MCP and toolchain child](../../completed/latest-mcp-and-toolchain/README.md).
It added exact direct pins, lock files, action SHA pins, the Mise-managed registry
publisher, and the current-only MCP protocol test path.

### Phase 3: Commander Spellbook evidence

This completed phase is detailed in the [Commander Spellbook evidence child](../../completed/commander-spellbook-evidence/README.md).
It added one concrete adapter, source fixtures, a small opt-in toolset, and a
saved-deck combo lookup that returns source groups without advice.

### Phase 4: Exact-analysis gap review (deferred)

- Decision: Deferred on 2026-09-07. No child packet or production change is
  authorized.
- Scope deferred: A new MCP tool that packages saved-deck selection, Scryfall
  data, and exact Statistics without returning a new answer.
- Review finding:
  - The eight existing `stats_*` tools already cover exact draw, turn,
    mulligan, mana, package, copy-count, and deck-summary calculations.
  - Deck-backed statistics already select caller-named entry IDs, zones, and
    categories, while retaining the selected and excluded entries as evidence.
  - Direct Scryfall tag evidence can be used through caller-owned category
    rules; no automatic role inference is required.
- Affected requirements: EFD-008 is already met by the completed exact deck
  statistics child. EFD-001, EFD-003 through EFD-005, EFD-010, EFD-011, and
  EFD-013 remain rules for any later distinct tool.
- Owner: mtg-mcp.
- Reopen trigger: A player or agent supplies one exact question with fully
  stated inputs, a requested result, and a concrete reason the existing
  `deck_*`, `scryfall_*`, and `stats_*` workflows cannot return it.
- Validation: Compare the proposal with current tool contracts and the completed
  exact-statistics test evidence. This deferral needs only documentation checks
  because it adds no executable behavior.
- Exit criteria: The deferral record states the scope, rationale, owner,
  affected acceptance criteria, reopen trigger, and why EFD-008 remains met.
- Cleanup: None.

### Phase 5: Community and deck-group source feasibility

- Problems solved: Players want discussions and popularity context, but those
  sources have different access and population rules.
- Included requirements: EFD-006, EFD-007, EFD-010, EFD-013.
- Out of scope: Implementing a source merely because it is popular.
- Expected edits:
  - One add/defer/reject record per researched source.
  - A Reddit-specific API-access check before code; source-specific child only
    if supported.
  - An official-API deck-group-provider evaluation for EDHREC-style questions.
- Tests added: None until a source is selected.
- Validation:
  - Re-check current API documentation and access rules.
  - Review source meaning, cache expiry, attribution, rate limits, and
    downstream model handling.
- Exit criteria:
  - Each candidate has a durable decision and rationale.
  - No undocumented endpoint, browser automation, or cache is introduced.
- Rollback/fallback: Record defer/reject; exact and existing evidence workflows
  remain useful.
- Cleanup: Remove only research scaffolding that is no longer authoritative.
- Result (2026-09-08): Complete. The [source-feasibility record](SOURCE_FEASIBILITY.md)
  admits no new source. Reddit is deferred until it approves the exact
  read-only workflow. Direct EDHREC, Moxfield, and public Archidekt collection
  automation are rejected, and the current Playgroup API cannot provide the
  complete card entries needed for deck-group analysis. No source code, MCP
  tool, provider cache, configuration, credential, or fixture was added.

### Phase 6: Deck mana and on-curve estimate

- Problems solved: Answer one useful real-deck castability question without
  pretending to run a full Magic game or make a deckbuilding choice.
- Included requirements: EFD-001, EFD-003 through EFD-005, EFD-009 through
  EFD-011, EFD-013.
- Out of scope:
  - A rules engine, general goldfish player, automatic behavior parser, or
    recommendation system.
  - Multiplayer, opponents, stack, priority, combat, commander rules, mana
    creatures, mana rocks, hidden play-policy inference, and card-text rules.
- Expected edits:
  - The [Deck Mana and On-Curve Estimate child](../../completed/deck-mana-on-curve-simulation/README.md).
  - Direct Scryfall produced-mana facts, a Core-only OnCurve project, and one
    read-only deck tool under the authorized child.
  - Sanitized actual-deck fixtures, caller-supplied land rules, seeded replay,
    source facts, failure labels, traces, and a Wilson interval.
  - Updated App tool registration, tool count, capability record, Task checks,
    coverage, and benchmark baseline.
- Tests added:
  - Exact deck and printing resolution without fuzzy matching.
  - Missing source facts and missing caller rules return clear typed outcomes.
  - Same-seed replay, land-play tie breaks, mana/color checks, and no inferred
    card behavior.
  - Output caps, cancellation, uncertainty, read-only mode, and end-to-end
    tool checks.
- Validation:
  - Completed independent review record and offline saved-deck and source-fact
    fixtures.
  - Offline saved-deck and source-fact fixtures.
  - Full Task checks, tool inventory, MCP smoke check, documentation review,
    and a bounded named benchmark after correctness is proven.
- Exit criteria:
  - The owner authorizes the revised child after independent review.
  - The public result is visibly a sampled estimate with stated facts, rules,
    uncertainty, and limits.
  - No hidden card interpreter, tag behavior, or recommendation appears.
- Rollback/fallback: Defer the tool and keep exact analysis only.
- Cleanup: Do not revive the old broad simulator design or toy-card prototype.
- Result (2026-09-10): Complete. The completed child added direct
  produced-mana facts, a Core-only OnCurve calculation, and one read-only decks
  tool with source facts, caller rules, replay details, uncertainty, limits,
  cache-only reads, and all-mode process coverage. Full Task validation and the
  fixed 99-card Release benchmark passed.

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
| A source is technically reachable but not supported or meaningful. | 3, 5 | Require a source check and current API/access review before code. | Product/provider owner |
| Exact analysis grows role inference. | 4 | Require caller-declared groups and selected-card evidence. | Statistics child owner |
| The on-curve estimate grows into a rules engine or hides card-behavior guesses. | 6 | Model lands only, require caller land rules, use direct source facts, reject unsupported costs, and stop before adding a parser or tag behavior. | OnCurve child owner |
| Coverage hides semantic gaps. | All | Use characterization, independent formulas, E2E, and fixture quality review alongside coverage. | Reviewers |
| Dirty user changes overlap a child. | 1B | Rebase scope after the user’s mapper work is finalized; do not overwrite it. | Implementer |

## Completion Criteria

- [ ] Every selected Must requirement appears in an authorized child.
- [ ] A currently authorized child verifies every in-scope Must requirement or
  has an approved amendment that removes or replaces it; a deferral alone does
  not close the requirement.
- [ ] Every deferred future child or child outcome records its rationale,
  owner, activation or review trigger, affected acceptance criteria, and why
  the completed phase still meets its exit criteria.
- [ ] Phase 1A and 1B preserve the current public surface and provider
  behavior while removing forwarding ownership.
- [ ] Each new provider is selected or explicitly deferred before code starts.
- [ ] Exact and sampled analysis remain visibly distinct.
- [ ] No generic provider framework, scraper, rules engine, or recommendation
  system appears.
- [ ] Every completed child records focused and broad validation.
- [ ] Documentation, source limits, tool counts, and deferred work are current.
