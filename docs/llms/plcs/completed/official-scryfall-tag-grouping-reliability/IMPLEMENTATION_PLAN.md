# Official Scryfall Tag Grouping Reliability Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)
- Implementation authorized: Yes

## Implementation Strategy

Start with pure grammar validation because it turns unsafe silent non-matches
into explicit failures without provider work. Then add a small Scryfall source
read for exact tag identities and all ancestors. Finally, join the repaired
source evidence to the current deck workflow and correct `common-v1` in place.

The implementation adds no new tool or tag system. It should split only where
that gives a real named responsibility: Core grammar/evaluation, Scryfall source
graph reading, App source-rule resolution, and thin MCP wrappers. It must not
replace those concrete owners with a generic framework.

## Phase Summary

| Phase | Goal | Requirements | Code areas | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Close and test category-rule grammar. | TGR-002 | Core rule records/validator, Core tests | Focused Core tests | Bad rule data fails before evaluation; valid current cases stay stable. | Complete |
| 2 | Read exact Scryfall tag identities and all ancestors. | TGR-001, TGR-003, TGR-004 | Scryfall tag reader/service, Scryfall fixtures/tests | Focused Scryfall tests | One Scryfall-format data generation proves ID/slug lookup and all-parent ancestry. | Complete |
| 3 | Repair deck composition and `common-v1` in place. | TGR-005, TGR-006, TGR-007 | App coordinator/resolver/wrappers, App tests, docs | Focused App/surface tests | Existing workflow produces exact source-ID rules and correct grouping without surface growth. | Complete |
| 4 | Validate and finish. | TGR-001 to TGR-008 | Tests, docs, quality gates | Task lint/test/coverage/surface and review | Required evidence is recorded; no P1, P2, or deferred P3 finding remains. | Complete |

## Phase Details

### Phase 1: Closed rule grammar

- Problems solved: malformed category selectors and rule sets can reach the
  evaluator and look like normal unmatched cards.
- Included requirements: TGR-002.
- Out of scope: Scryfall lookup, preset mapping, new MCP behavior.
- Expected edits:
  - Keep category records and pure validation together only where that remains
    easy to scan; otherwise split a small validator from the evaluator.
  - Validate selector identity, tag kind, weight, collections, category rules,
    and primary priorities before source access.
- Tests added: table-driven invalid-selector and invalid-rule tests, plus valid
  direct/descendant cases to prove no behavior regression.
- Validation: affected Core test project, then `task lint` if public records or
  serializers change.
- Exit criteria: every malformed input has a deterministic `OperationInvalidInput`
  outcome before preview evaluation.
- Rollback/fallback: revert only the new validator if it rejects a valid existing
  schema case; do not add a permissive silent fallback.
- Cleanup: delete duplicated ad-hoc validation branches.

### Phase 2: Official source identity and ancestry

- Problems solved: direct tag evidence does not carry enough source ancestry for
  a parent selector, and slug selectors are not yet exact source IDs.
- Included requirements: TGR-001, TGR-003, TGR-004.
- Out of scope: deck mutation, new provider, full card-data download changes.
- Expected edits:
  - Add a narrow Scryfall read for exact tag ID/slug resolution in a retained
    data generation.
  - Add generation-bound card/tag reads and one upward traversal for all
    distinct direct tag IDs in a preview. The reads use the data version chosen
    at the start of the preview, not whichever version becomes active mid-loop.
    The traversal returns the direct ID and all reachable parents in stable
    order.
  - Keep direct card-tag output semantics distinct from this internal grouping
    evidence; do not pretend one selected path describes a multi-parent graph.
- Tests added: Scryfall-format fixture with Oracle/art rows, direct assignments,
  parent/child relations, and one tag with two parents; exact/missing/ambiguous
  lookup cases; no source graph writes.
- Validation: affected Scryfall test project; inspect SQL/query bounds and run
  `task lint`.
- Exit criteria: the adapter returns complete source ancestry and exact
  identity without a Tagger-site call or local tag data.
- Rollback/fallback: keep current direct-tag reads if source data cannot be
  queried safely; stop for an owner decision rather than infer ancestry.
- Cleanup: reuse/remove conflicting one-path logic only where it serves the
  same internal grouping purpose; retain public search behavior unchanged.

### Phase 3: Deck workflow and in-place preset repair

- Problems solved: App does not join source ancestry to Core correctly, and the
  built-in preset selects an unsupported source slug.
- Included requirements: TGR-005, TGR-006, TGR-007.
- Out of scope: a fourth categorization tool, automatic category choice,
  custom tag data, compatibility aliases, or data migration.
- Expected edits:
  - Keep one named App resolver responsible for expanding preset/inline input
    and resolving it to exact source IDs.
  - Keep the coordinator responsible for deck/source orchestration and the MCP
    wrappers thin; split the current mixed file if that is the clearest result.
  - Require one source data generation across rules, tag evidence, fingerprint,
    and apply comparison.
  - Recheck current official data, record selected exact IDs and descendant
    settings in fixtures, then correct `common-v1` in place and update its
    checksum/rationale/docs.
- Tests added: temporary deck plus Scryfall-format data generation; parent match
  enabled/disabled; slug versus ID equality; invalid/missing source selector
  stops synchronize removal; preset versus inline equality; tool/mode/toolset
  stability.
- Validation: affected App/unit/integration/surface tests; then `task lint` and
  relevant `task test:*` commands.
- Exit criteria: the three existing tools return correct exact source-ID
  behavior and `common-v1` keeps its name/keys while using verified source tags.
- Rollback/fallback: a failing source mapping leaves the role unsupported and
  blocks authorization for that mapping. Do not guess a replacement or publish
  `common-v2`.
- Cleanup: remove stale preset wording and duplicated resolver/matcher paths.

### Phase 4: Repository validation and close-out

- Problems solved: ensure a narrow repair does not hide a source-boundary,
  schema, coverage, or documentation regression.
- Included requirements: TGR-001 through TGR-008.
- Out of scope: unrelated cleanup or a broad refactor because a file is nearby.
- Expected edits: update this packet, source/boundary docs, and only affected
  public descriptions.
- Tests added: only missing behavior tests found by the focused test audit.
- Validation:
  - `task lint`
  - `task test`
  - `task coverage`
  - affected surface/report and package checks required by current Task targets
  - `git diff --check`, Markdown-link inspection, and focused architecture,
    correctness, test, and plain-language re-review
- Exit criteria: all Must requirements have evidence; P1/P2 findings are fixed;
  P3 findings are fixed when inexpensive or documented; all docs use the
  source-evidence vocabulary consistently.
- Rollback/fallback: revert the repair as one coherent change if broad gates
  fail and a narrow correction cannot restore them.
- Cleanup: remove temporary test data/helpers that do not represent a durable
  source contract.

## Cross-Phase Risks

| Risk | Affected phases | Mitigation | Owner |
| --- | --- | --- | --- |
| Current official source data differs from the historical snapshot. | 2–3 | Verify current data before choosing preset IDs; preserve a small reviewed fixture. | mtg-mcp |
| A multi-parent graph is flattened accidentally. | 2–3 | Use an all-ancestor fixture and an explicit collection name, not a single path. | mtg-mcp |
| Validation change alters public JSON unexpectedly. | 1, 3 | Run schema/surface checks and preserve existing rule-source variants. | mtg-mcp |
| The repair grows one mixed coordinator file. | 3 | Split source resolution and MCP wrappers only along real responsibilities. | mtg-mcp |
| A source change enables destructive removal from incomplete data. | 1–3 | Test invalid/unavailable/incomplete selectors in synchronize mode. | mtg-mcp |

## Completion Criteria

- [x] Every Must requirement from the SRD appears in at least one phase.
- [x] The current official `common-v1` source mapping is reviewed before it is
      committed.
- [x] Every phase leaves normal tests offline and focused tests green.
- [x] Existing MCP names, toolset membership, and mode visibility are verified.
- [x] Core/App/Scryfall boundaries remain one-way and clear.
- [x] No Tagger website integration, custom tag storage, or generic provider
      abstraction is introduced.
- [x] Documentation and plain-language review are complete.

## Completion Record

| Phase | Completed work | Evidence |
| --- | --- | --- |
| 1 | Added a pure closed-grammar validator and changed Core evidence from one named path to complete ancestor IDs. | Core rule and evaluation tests passed. |
| 2 | Added exact installed-tag lookup and one upward traversal for all direct tags in a selected data generation. | Scryfall fixture tests proved exact lookup, two parents, no provider request after sync, and explicit-sync-only refresh. |
| 3 | Split rule resolution from deck coordination and corrected `common-v1` to the reviewed exact source IDs. | App tests proved parent matching, preset-to-inline equality, and synchronize safety. |
| 4 | Ran formatting, full tests, coverage gates, surface checks, documentation checks, and final audits. | All recorded checks passed; see [README.md](README.md#validation-evidence). |
