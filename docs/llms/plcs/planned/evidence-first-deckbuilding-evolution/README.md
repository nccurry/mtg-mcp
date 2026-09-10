# Evidence-First Deckbuilding Evolution PLC Packet

> [!IMPORTANT]
> This is an umbrella planning packet. It does not authorize production edits.
> Each implementation child needs independent review, an explicit owner decision,
> and its own “Implementation authorized: Yes” before code changes begin.

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/evidence-first-deckbuilding-evolution/
- Owner: mtg-mcp
- Created: 2026-09-06
- Last updated: 2026-09-10
- Current phase: Phase 6 is complete. Phase 4 is deferred; Phases 4A and 5 are
  complete. Stabilization remains planned.
- Implementation authorized: No

## Summary

Stable 0.9 already has the right product boundary: mtg-mcp gathers evidence,
calculates declared mathematics, and performs explicit guarded workflows. The
LLM and player decide what a deck should do.

This program makes that boundary easier to maintain and expands it carefully.
The first delivery is structural: make the Scryfall and Archidekt owners real
instead of forwarding wrappers around large context classes. Later, separately
reviewed children may repair the existing official-tag grouping path, add more
reliable evidence sources, and add a narrowly bounded deck mana and on-curve
estimate. A new exact-analysis tool remains possible only if a future review
shows a real gap in the current tools.

The product can be described in three verbs:

1. Collect card, deck, and source evidence.
2. Calculate exact answers from declared inputs.
3. Execute explicit local or remote deck workflows.

It must not add a fourth verb: decide.

## Packet Contents

- [AUDIT.md](AUDIT.md): baseline findings, validation evidence, and what to
  keep versus change.
- [SRD.md](SRD.md): product outcomes, requirements, scope, and acceptance
  criteria.
- [SADD.md](SADD.md): target architecture, provider rules, error policy, and
  sampled-estimate boundary.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): independently reviewable
  delivery sequence.
- [FIXTURES.md](FIXTURES.md): future fixture, contract, and calibration
  inventory.
- [SOURCE_FEASIBILITY.md](SOURCE_FEASIBILITY.md): Phase 5 source decisions and
  current provider-access evidence.
- [Latest MCP and Toolchain child](../../completed/latest-mcp-and-toolchain/README.md): the
  current-only protocol and version-lock work for Phase 2.
- [Commander Spellbook Evidence child](../../completed/commander-spellbook-evidence/README.md):
  the completed opt-in evidence adapter for Phase 3.
- [Deck Mana and On-Curve Estimate child](../../completed/deck-mana-on-curve-simulation/README.md):
  the completed public read-only estimate for Phase 6.

## Decision Snapshot

| Decision | Status | Rationale | Detail |
| --- | --- | --- | --- |
| Preserve evidence before advice. | Inherited | It is the current north star and the right product boundary. | [SRD](SRD.md#requirements) |
| Refactor ownership before expanding sources. | Proposed | A new provider or simulation on top of forwarding contexts would compound the hardest maintenance problem. | [Audit](AUDIT.md#findings) |
| Keep vertical provider modules; do not add a generic provider framework. | Proposed | Scryfall, Archidekt, Playgroup, combo data, and community discussion have different contracts and safety rules. | [SADD](SADD.md#alternatives-considered) |
| Keep Statistics exact and provider-independent. | Inherited | A deck statistic is reliable only when its population and assumptions are explicit. | [SADD](SADD.md#exact-analysis-and-sampled-estimates) |
| Add one bounded deck mana and on-curve estimate. | Complete | The result explains stated land rules and sampled castability without masquerading as a Magic rules engine or a matchup predictor. | [Phase 6 child](../../completed/deck-mana-on-curve-simulation/README.md) |
| Check every external source individually. | Proposed | “More sources” is valuable only when access, meaning, cache behavior, and provenance are clear. | [SADD](SADD.md#provider-check) |
| Require the newest MCP protocol in a focused child. | Complete | The owner wants the newest design with no legacy support. | [Phase 2 child](../../completed/latest-mcp-and-toolchain/README.md) |
| Add Commander Spellbook as a concrete evidence adapter. | Complete | Its documented API serves the core combo-evidence workflow without making a recommendation. | [Phase 3 child](../../completed/commander-spellbook-evidence/README.md) |
| Start with Scryfall card-data store ownership. | Complete | It is a small internal refactor with no public behavior change. | [Phase 1A child](../../completed/scryfall-store-ownership-extraction/README.md) |
| Keep all card-data state in ScryfallCardDataStore. | Implemented | The active generation, previous generation, and metadata-check time must change together. | [Phase 1A design](../../completed/scryfall-store-ownership-extraction/SADD.md#explicit-metadata-check-ownership) |
| Complete Archidekt ownership cleanup. | Complete | Shared session and named deck, folder, and snapshot owners now contain their code without changing provider or MCP behavior. | [Phase 1B child](../../completed/archidekt-ownership-cleanup/README.md) |
| Do not add a duplicate exact-analysis tool. | Deferred | The current Statistics and deck-selection workflows already answer the questions reviewed for this phase. | [Phase 4 record](IMPLEMENTATION_PLAN.md#phase-4-exact-analysis-gap-review-deferred) |
| Use only official Scryfall community tags when grouping deck cards. | Implemented | The server evaluates source evidence but does not create a second tag system or fetch from the Tagger website. | [Phase 4A child](../../completed/official-scryfall-tag-grouping-reliability/README.md#decision-snapshot) |
| Correct `common-v1` in place. | Implemented | The preset mapping now uses reviewed exact source IDs under the existing preset ID. | [Phase 4A design](../../completed/official-scryfall-tag-grouping-reliability/SADD.md#common-v1-correction) |
| Do not add Reddit or public deck-group acquisition now. | Complete | Reddit needs explicit approval; EDHREC, Moxfield, and public Archidekt collection do not currently support this automation. | [Phase 5 source record](SOURCE_FEASIBILITY.md) |
| Build Phase 6 around an actual saved deck. | Complete | The owner rejected a toy-card prototype and selected a clear, read-only mana and on-curve estimate with caller-supplied land rules. | [Phase 6 child](../../completed/deck-mana-on-curve-simulation/README.md) |

## Project And Surface Impact

The completed Phase 1 children affected MtgMcp.Scryfall, MtgMcp.Archidekt,
their focused tests, architecture tests, and documentation. They did not change
tool names, schemas, operation modes, SQLite formats, provider behavior, or the
MCP surface.

The completed Phase 4A child affected the existing category-rule models, Scryfall
tag reads, deck categorization composition, and focused tests. It adds no MCP
tool, resource, prompt, operation mode, toolset, configuration key, database,
or tag provider. Existing category-rule results may correctly return different
matches, exact source tag IDs, and validation errors after the repair.

The completed Phase 6 child added `MtgMcp.OnCurve`, direct Scryfall
produced-mana facts, one read-only `deck_on_curve_estimate` tool, focused tests,
and a separate Release benchmark. It added no generic simulator, rules engine,
tag system, or source-provider behavior.

Future children may affect:

- MtgMcp.App static tool registrations and capability metadata;
- one new concrete provider project at a time;
- exact Statistics workflows without provider references;
- a new isolated OnCurve calculation project and one read-only deck tool under
  the authorized Phase 6 child;
- test fixtures, source documentation, package compatibility, and Task tasks.

The Phase 2 child also owns version locks, action SHA pins, and the
Mise-managed MCP Registry publisher. The completed Phase 3 child added one
concrete provider project and three opt-in read-only tools.

No child may introduce automatic legacy migration, a generic request router,
automatic website scraping, MCP-owned recommendations, or an unbounded rules
engine.

## Phase 5 Source Decisions

Phase 5 is complete. Reddit is deferred until it explicitly approves the exact
workflow. Direct EDHREC, Moxfield, and public Archidekt deck-group automation
are rejected. The current Playgroup contract cannot supply complete deck entries
for card-use analysis. No source code or MCP surface was added. See the
[source-feasibility record](SOURCE_FEASIBILITY.md) for the source pages, scope,
and reopen triggers.

## Deferral Rule

An unselected future child or child outcome can be deferred only with a durable
record in its owning child and a summary here. The record must name the
scope being deferred, rationale, owner, activation or review trigger, affected
acceptance criteria, and why the active phase still meets its exit criteria.

When a phase ends before a child exists, its phase section in this umbrella is
the owning record.

A currently authorized child cannot close an in-scope Must requirement by
calling it deferred. It must verify the requirement or obtain an approved
amendment that removes or replaces it.

## Deferred Work

### Phase 4: Exact-analysis gap review

- Scope: Do not add a new exact deck-analysis MCP tool that only packages deck
  selection, Scryfall data, and Statistics into one call.
- Rationale: The eight existing `stats_*` tools already cover exact draw,
  mulligan, mana, package, copy-count, and deck-summary questions. Deck-backed
  selection already accepts explicit entry IDs, zones, and categories. A new
  wrapper would tie those parts together but would not return a new answer.
- Owner: mtg-mcp.
- Reopen trigger: A player or agent names one question with fully stated inputs
  and a result that the current `deck_*`, `scryfall_*`, and `stats_*` workflows
  cannot return.
- Affected acceptance criteria: EFD-008 remains satisfied by the completed
  [exact deck statistics child](../../completed/exact-deck-statistics/README.md).
  The other Phase 4 criteria apply only if a distinct public tool is proposed.
- Why the phase exit still holds: Phase 4 required a demonstrated gap before a
  new tool. The review found none, so there is no code, test, or public API work
  to complete.

## Completed Work

### Phase 4A: Official Scryfall tag grouping repair

- Scope completed: Category rules now resolve and match only installed official
  Scryfall `oracle_tags` and `art_tags` data.
- Results: Direct child tags retain all source parents, malformed selectors fail
  before a preview, and `common-v1` keeps its ID while using reviewed source
  tag IDs.
- Child: [Official Scryfall Tag Grouping Reliability](../../completed/official-scryfall-tag-grouping-reliability/README.md).
- Validation: Focused tests, full Task checks, coverage gates, surface checks,
  documentation checks, and final audits passed.

### Phase 5: Community and deck-group source feasibility

- Scope completed: Recorded the access and data-meaning decisions for Reddit,
  EDHREC, Moxfield, public Archidekt deck collection, and the current Playgroup
  API.
- Results: No source is admitted. Reddit is deferred pending explicit approval;
  direct EDHREC and Moxfield automation are rejected; the current Playgroup API
  cannot supply complete card lists for a defined deck group.
- Record: [Phase 5 Source Feasibility](SOURCE_FEASIBILITY.md).
- Validation: Current source-policy review, local API-contract review, link
  inspection, and documentation checks.

## Planned Next Work

Stabilization remains planned. Phase 4 remains deferred because the existing
exact statistics and explicit deck workflows cover the reviewed questions.

## Planning Readiness Checklist

- [x] Core target use cases and non-goals are explicit.
- [x] The present architecture and concrete ownership gaps were inspected.
- [x] Must requirements have acceptance criteria and traceability.
- [x] External source, MCP, C#, MTG-rule, and statistics research is recorded.
- [x] Core, App, adapter, test, persistence, and toolset boundaries are explicit.
- [x] Provider access, pacing, caching, expiry, and error-sanitization rules are explicit.
- [x] Exact mathematics and sampled estimates have separate contracts.
- [x] Each proposed delivery phase has an exit criterion.
- [x] Existing retired simulation and provider packets are treated as reference-only.
- [x] The owner has selected the first implementation child.
- [x] Every completed child had independent approval and implementation
  authorization.
- [x] The revised Phase 6 child has independent review and implementation
  authorization.
- [x] Phase 2 and Phase 3 each completed through a narrow child.
- [x] An independent reviewer checked the completed child packets and fixed the findings.
- [x] Phase 4A has a narrow repair packet with source, boundary, and test rules.
- [x] Phase 5 has a current source access-and-use record with explicit outcomes.

## Implementation Checklist

- [x] Select and create the first narrow child packet.
- [x] Move only that approved child to in-progress.
- [x] Lock behavior with characterization fixtures before moving ownership.
- [ ] Update this umbrella if a cross-child guardrail changes.
- [x] Record focused and broad validation as each child completes.
- [ ] Move this packet to completed only after every selected child is completed,
  explicitly deferred, or superseded with a recorded reason.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-06 | task lint | Passed | Formatting check and strict build passed with the pinned .NET 11 preview SDK. |
| 2026-09-06 | task test | Passed | 545 non-live tests passed across nine test assemblies. |
| 2026-09-06 | task coverage | Passed | Every production assembly cleared the 90% line-coverage gate. |
| 2026-09-06 | task surface:report | Passed | The then-current static surface had 93 tools, one resource, and zero prompts. |
| 2026-09-06 | task deps:check | Passed with follow-up | No failed check; ModelContextProtocol 2.2.0 and several analyzer/test packages are available. |
| 2026-09-06 | dotnet list package --vulnerable --include-transitive | Passed | NuGet reported no known vulnerable packages. |
| 2026-09-06 | Source and document audit | Completed | The two P2 ownership findings and one P3 documentation drift are recorded in [AUDIT.md](AUDIT.md). |
| 2026-09-06 | Independent packet review | Findings fixed | Corrected the dependency diagram, deferral control, volatile line-count claims, and an internal link. |
| 2026-09-06 | Documentation validation | Passed | git diff --check, trailing-whitespace scan, and local Markdown-link resolution passed. |
| 2026-09-07 | Phase 1A owner decision | Passed | The owner selected Scryfall store ownership first and kept all `corpus_state` operations in ScryfallCardDataStore. |
| 2026-09-07 | Phase 1A independent review | Passed | The corrected child packet passed final independent review and Phase 1 is authorized. |
| 2026-09-07 | Phase 2 and Phase 3 independent review | Passed after fixes | The new packets now name their exact version, pacing, cache, paging defaults, timeout, request facts, and process-test behavior. |
| 2026-09-07 | Phase 1A characterization | Passed | The offline Scryfall suite passed 38 tests. A focused test review found no missing coverage in the changed paths. |
| 2026-09-07 | Phase 1A close-out | Passed | The ownership boundary test, lint, all non-live tests, coverage gates, and MCP surface report passed. The final audit found no remaining blocking issue. |
| 2026-09-07 | Phase 1B independent design review | Passed | The packet clarified budget charging, client disposal tests, and the temporary transition boundary. Implementation is authorized. |
| 2026-09-07 | Phase 3 close-out | Passed | The completed child added three opt-in Spellbook tools. The `all` profile then had 96 tools, and CI, coverage, live, and package smoke checks passed. |
| 2026-09-07 | Phase 1B close-out | Passed | Named session, transport, and workflow owners replaced both Context layers. Full lint, test, coverage, and surface checks passed; Archidekt line coverage reached 91.07%, and the MCP surface stayed unchanged. |
| 2026-09-07 | Current toolchain report | Passed | The direct SDK, NuGet, Mise, and local tool pins are current. The report lists only transitive package updates. |
| 2026-09-07 | Phase 2 and Phase 3 packet drafting | Passed | The current-only MCP and Commander Spellbook designs have scoped requirements, test cases, and owner decisions. |
| 2026-09-07 | Phase 4 exact-analysis gap review | Deferred | Existing Statistics, explicit deck selection, and category workflows cover the proposed contract; no distinct tool is justified. |
| 2026-09-07 | Phase 4A tag-grouping repair | Passed | The completed child preserves all source parents, rejects malformed or missing selectors, corrects `common-v1` in place, and adds no MCP surface. Focused tests, full Task checks, coverage gates, surface checks, documentation checks, and final audits passed. |
| 2026-09-08 | Phase 5 source feasibility | Passed | Current official source pages support no new integration: Reddit is deferred pending approval, direct EDHREC and Moxfield automation are rejected, and no suitable public deck-group API was found. |
| 2026-09-08 | Phase 6 plan reset | Completed | The owner rejected the toy-card prototype and selected a real-deck, read-only mana and on-curve estimate. The revised packet needs a fresh independent review and authorization. |
| 2026-09-10 | Phase 6 independent review | Passed after fixes | Seven review findings clarified source-data states, cache-only reads, fingerprints, seed text, request bounds, multi-face cards, and miss labels. The owner authorized Phase 1. |
| 2026-09-10 | Phase 6 close-out | Passed | The completed child added one read-only decks tool, direct produced-mana facts, pure replayable calculation, all-mode MCP tests, coverage, surface and smoke checks, clear public docs, and a recorded Release benchmark. |

## Completion Notes

Phases 1 through 3, Phase 4A, Phase 5, and Phase 6 are complete. This packet
remains a roadmap, not an implementation authorization. Phase 4 is deferred and
stabilization remains planned.
