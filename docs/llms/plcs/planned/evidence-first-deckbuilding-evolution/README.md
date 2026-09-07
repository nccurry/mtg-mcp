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
- Last updated: 2026-09-07
- Current phase: Phase 3: Commander Spellbook evidence
- Implementation authorized: No

## Summary

Stable 0.9 already has the right product boundary: mtg-mcp gathers evidence,
calculates declared mathematics, and performs explicit guarded workflows. The
LLM and player decide what a deck should do.

This program makes that boundary easier to maintain and expands it carefully.
The first delivery is structural: make the Scryfall and Archidekt owners real
instead of forwarding wrappers around large context classes. Later, separately
reviewed children may add more reliable evidence sources, stronger exact deck
analysis, and a tightly bounded goldfish experiment.

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
  experimental-simulation boundary.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): independently reviewable
  delivery sequence.
- [FIXTURES.md](FIXTURES.md): future fixture, contract, and calibration
  inventory.
- [Latest MCP and Toolchain child](../../completed/latest-mcp-and-toolchain/README.md): the
  current-only protocol and version-lock work for Phase 2.
- [Commander Spellbook Evidence child](../../in-progress/commander-spellbook-evidence/README.md):
  the active opt-in evidence adapter for Phase 3.

## Decision Snapshot

| Decision | Status | Rationale | Detail |
| --- | --- | --- | --- |
| Preserve evidence before advice. | Inherited | It is the current north star and the right product boundary. | [SRD](SRD.md#requirements) |
| Refactor ownership before expanding sources. | Proposed | A new provider or simulation on top of forwarding contexts would compound the hardest maintenance problem. | [Audit](AUDIT.md#findings) |
| Keep vertical provider modules; do not add a generic provider framework. | Proposed | Scryfall, Archidekt, Playgroup, combo data, and community discussion have different contracts and safety rules. | [SADD](SADD.md#alternatives-considered) |
| Keep Statistics exact and provider-independent. | Inherited | A deck statistic is reliable only when its population and assumptions are explicit. | [SADD](SADD.md#exact-analysis-and-simulation) |
| Treat advanced goldfish as a feasibility experiment, not a stable feature promise. | Proposed | A useful bounded model may be possible, but it must not masquerade as a Magic rules engine or a matchup predictor. | [SRD](SRD.md#scope-and-non-scope) |
| Check every external source individually. | Proposed | “More sources” is valuable only when access, meaning, cache behavior, and provenance are clear. | [SADD](SADD.md#provider-check) |
| Require the newest MCP protocol in a focused child. | Complete | The owner wants the newest design with no legacy support. | [Phase 2 child](../../completed/latest-mcp-and-toolchain/README.md) |
| Add Commander Spellbook as a concrete evidence adapter. | In progress | Its documented API serves the core combo-evidence workflow without making a recommendation. | [Phase 3 child](../../in-progress/commander-spellbook-evidence/README.md) |
| Start with Scryfall card-data store ownership. | Complete | It is a small internal refactor with no public behavior change. | [Phase 1A child](../../completed/scryfall-store-ownership-extraction/README.md) |
| Keep all card-data state in ScryfallCardDataStore. | Implemented | The active generation, previous generation, and metadata-check time must change together. | [Phase 1A design](../../completed/scryfall-store-ownership-extraction/SADD.md#explicit-metadata-check-ownership) |
| Complete Archidekt ownership cleanup. | Complete | Shared session and named deck, folder, and snapshot owners now contain their code without changing provider or MCP behavior. | [Phase 1B child](../../completed/archidekt-ownership-cleanup/README.md) |

## Project And Surface Impact

The completed Phase 1 children affected MtgMcp.Scryfall, MtgMcp.Archidekt,
their focused tests, architecture tests, and documentation. They did not change
tool names, schemas, operation modes, SQLite formats, provider behavior, or the
MCP surface.

Future children may affect:

- MtgMcp.App static tool registrations and capability metadata;
- one new concrete provider project at a time;
- exact Statistics workflows without provider references;
- a new isolated simulation-lab project only after feasibility approval;
- test fixtures, source documentation, package compatibility, and Task tasks.

The Phase 2 child also owns version locks, action SHA pins, and the
Mise-managed MCP Registry publisher. The Phase 3 child adds one concrete
provider project and three opt-in read-only tools under its active child packet.

No child may introduce automatic legacy migration, a generic request router,
automatic website scraping, MCP-owned recommendations, or an unbounded rules
engine.

## Current Open Questions

| Question | Impact | Owner | Resolution plan |
| --- | --- | --- | --- |
| Does the saved-deck-only Spellbook interface fit the first workflow? | Public input contract | mtg-mcp | Approved on 2026-09-07. A raw-deck input needs a separate design later. |
| Does Reddit have a documented API path that fits a small, attributed MCP read? | Provider reliability | mtg-mcp | Do not build it until the published access and use rules support the exact workflow. |
| Is there a public deck-population API for EDHREC-style cohort analysis? | Product scope | mtg-mcp | Use an official public API only. Do not consume undocumented endpoints. |
| Can a small goldfish model be honest and useful? | Experimental scope | mtg-mcp | Run a feasibility child with toy decks, fixed policies, traces, and a stop decision before any public tool. |

## Deferral Rule

An unselected future child or a feasibility outcome can be deferred only with a
durable record in its owning child and a summary here. The record must name the
scope being deferred, rationale, owner, activation or review trigger, affected
acceptance criteria, and why the active phase still meets its exit criteria.

A currently authorized child cannot close an in-scope Must requirement by
calling it deferred. It must verify the requirement or obtain an approved
amendment that removes or replaces it.

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
- [x] The selected child has independent approval and implementation authorization.
- [x] Phase 2 and Phase 3 each have a narrow planned child.
- [x] An independent reviewer has checked the two new child packets and the findings are fixed.

## Implementation Checklist

- [x] Select and create the first narrow child packet.
- [x] Move only that approved child to in-progress.
- [x] Lock behavior with characterization fixtures before moving ownership.
- [ ] Update this umbrella if a cross-child guardrail changes.
- [ ] Record focused and broad validation as each child completes.
- [ ] Move this packet to completed only after every selected child is completed,
  explicitly deferred, or superseded with a recorded reason.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-06 | task lint | Passed | Formatting check and strict build passed with the pinned .NET 11 preview SDK. |
| 2026-09-06 | task test | Passed | 545 non-live tests passed across nine test assemblies. |
| 2026-09-06 | task coverage | Passed | Every production assembly cleared the 90% line-coverage gate. |
| 2026-09-06 | task surface:report | Passed | The current static surface has 93 tools, one resource, and zero prompts. |
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
| 2026-09-07 | Phase 1B close-out | Passed | Named session, transport, and workflow owners replaced both Context layers. Full lint, test, coverage, and surface checks passed; Archidekt line coverage reached 91.07%, and the MCP surface stayed unchanged. |
| 2026-09-07 | Current toolchain report | Passed | The direct SDK, NuGet, Mise, and local tool pins are current. The report lists only transitive package updates. |
| 2026-09-07 | Phase 2 and Phase 3 packet drafting | Passed | The current-only MCP and Commander Spellbook designs have scoped requirements, test cases, and owner decisions. |

## Completion Notes

Phase 1 ownership cleanup and Phase 2 are complete. This packet remains a
roadmap, not an implementation authorization. Phase 3 has its own active child;
later phases remain planned.
