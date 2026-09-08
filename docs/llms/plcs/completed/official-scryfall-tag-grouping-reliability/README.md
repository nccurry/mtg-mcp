# Official Scryfall Tag Grouping Reliability PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/official-scryfall-tag-grouping-reliability/`
- Owner: mtg-mcp
- Created: 2026-09-07
- Last updated: 2026-09-07
- Current phase: Complete.
- Implementation authorized: Yes

## Summary

This repair makes the existing deck category-rule workflow faithfully use
official Scryfall community-tag data. It fixes three confirmed problems:

1. a direct child tag loses its source ancestry before Core evaluates it;
2. malformed source-tag selectors can silently act like a normal non-match; and
3. `common-v1` contains a source-tag mapping that is not supported by the
   checked-in historical reference data.

In this packet, *official tags* means the `oracle_tags` and `art_tags` datasets
that mtg-mcp downloads from Scryfall. It does not mean a direct integration
with `tagger.scryfall.com`. The server will not create, store, infer, or assign
its own card tags. The agent or player still chooses which official source tags
to group and which local deck categories receive the resulting cards.

The smallest useful delivery corrects the existing three category-rule tools in
place. It adds no MCP tool, provider, database, configuration key, or tag
system.

## Packet Contents

- [SRD.md](SRD.md): requirements, scope, and acceptance criteria.
- [SADD.md](SADD.md): ownership boundaries, data flow, and rejected designs.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): delivery order and gates.
- [FIXTURES.md](FIXTURES.md): offline Scryfall-format fixtures and acceptance cases.

## Decision Snapshot

| Decision | Status | Rationale | Detail |
| --- | --- | --- | --- |
| Use only official Scryfall tag data. | Accepted | Source identity, hierarchy, and assignment weight must remain source evidence rather than local claims. | [Source boundary](SADD.md#source-boundary) |
| Keep source hierarchy in the Scryfall adapter. | Accepted | Scryfall owns source relationships; Core must not depend on Scryfall types. | [Chosen design](SADD.md#chosen-design) |
| Resolve every selector to an exact source tag ID before evaluation. | Accepted | A slug is human-friendly input, but an exact source ID makes matching and descendants deterministic. | [Selector handling](SADD.md#selector-handling) |
| Correct `common-v1` in place. | Accepted | The current mapping has a defect. The owner chose a clean correction, not `common-v2` or a compatibility layer. | [Common-v1 correction](SADD.md#common-v1-correction) |
| Keep the existing three category-rule tools. | Accepted | Their workflow is sound once source evidence is correct; a fourth tool would duplicate it. | [MCP surface](SADD.md#mcp-surface) |
| Add a Tagger adapter or local tag database. | Rejected | It would duplicate Scryfall-owned data and create an unnecessary second tag system. | [Alternatives](SADD.md#alternatives-considered) |

## Current Evidence

- `MtgMcp.Scryfall` already imports Scryfall `oracle_tags` and `art_tags` into
  `scryfall.db`; it also stores direct assignments and tag relationships.
- `ScryfallDeckTagOperations` now resolves exact source IDs and reads every
  reachable parent for each direct assignment from one installed data
  generation.
- `DeckCategoryRuleResolver` validates rule grammar, checks local category
  ownership, expands the preset, and converts selectors to exact source IDs.
- `DeckCategorizationCoordinator` joins that source evidence to local deck
  entries before Core evaluates the rules.
- `CategoryRuleSetValidator` rejects malformed selectors and rule sets before
  Scryfall data is read.
- The 2026-05-23 reference snapshot is historical inspection evidence only. It
  shows no `card-draw` tag and shows `draw`, `removal`, and `recursion` as
  source parents without direct assignments. The implementation confirmed the
  selected IDs and relationships from current official Scryfall data.
- The 2026-09-07 official Oracle-tag export confirms the replacement mapping:
  `ramp`, `draw`, `removal`, and `recursion`, each with descendant matching
  enabled. `card-draw` remains a preset role key, not a source tag.

## Project And Surface Impact

| Surface | Completed effect |
| --- | --- |
| `MtgMcp.Core` | Added a pure rule grammar validator and evaluator that receives source-derived IDs and ancestor IDs, never Scryfall transport types. |
| `MtgMcp.Scryfall` | Added a narrow read for exact tag lookup and all source ancestors of supplied direct tags in one installed data generation. |
| `MtgMcp.App` | Resolves inputs and the preset through Scryfall, then joins source evidence to local deck entries before calling Core. |
| Tests | Added Core grammar tests plus Scryfall-format App integration tests. Normal tests remain offline. |
| MCP surface | Kept `deck_category_rules_validate`, `deck_category_rules_preview`, and `deck_category_rules_apply` with their current names, toolset, and mode visibility. |
| Persistent data | Kept `decks.db` and `scryfall.db` formats. Did not add a tag file, tag table, or migration. |
| Documentation | Corrected the current preset rule and labeled the historical Tagger-directory snapshot as non-runtime reference material. |

## Resolved Source Decision

`common-v1` keeps its four role keys and uses the reviewed exact Oracle tag
IDs recorded in [FIXTURES.md](FIXTURES.md#common-v1-source-review-record).
`refresh` does not download tag data during category work. It returns an
explicit result that asks the caller to run card-data sync first.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements are testable and have acceptance criteria.
- [x] Major alternatives and tradeoffs are recorded.
- [x] Quality attributes are measurable or inspectable.
- [x] Core/App/adapter/test boundaries and dependency impact are explicit.
- [x] MCP surface, operation-mode, and documentation impacts are clear.
- [x] Scryfall source, caching, retry, and error-sanitization boundaries are unchanged.
- [x] Documentation, readability, and abstraction reuse expectations are clear.
- [x] SRD maps Must requirements to acceptance criteria and validation.
- [x] Implementation plan has phase exit criteria.
- [x] Deferred work is visible and not required by the first implementation phase.

## Implementation Checklist

- [x] Owner authorizes implementation and the packet moves to `in-progress`.
- [x] Current official source data verifies the `common-v1` mapping.
- [x] Core grammar and source-resolution phases complete with focused tests.
- [x] Category preview/apply integration and current surface checks pass.
- [x] Full Task validation, coverage, docs, and final review evidence are recorded.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-07 | Source and code-path audit | Passed | Confirmed the ancestry, selector-validation, and preset-mapping defects; confirmed that runtime acquisition already uses Scryfall bulk tag data. |
| 2026-09-07 | Packet review | Passed | Requirements, ownership, failure behavior, fixture plan, and no-new-surface rule are explicit. |
| 2026-09-07 | Current official Oracle-tag review | Passed | Bulk record `bd8df61e-5d0a-47a2-9086-40137a645b98` confirms the four selected Oracle tags and that `card-draw` is not a source slug. |
| 2026-09-07 | Focused Core tests | Passed | 40 tests passed, including the closed rule grammar and all-parent matching cases. |
| 2026-09-07 | Focused Scryfall tests | Passed | Exact ID/slug lookup, all-parent ancestry, missing source tags, and explicit-sync-only refresh behavior passed from offline fixture data. |
| 2026-09-07 | Focused App tests | Passed | 120 tests passed, including preset-to-inline equality, synchronize safety, and no provider requests after explicit fixture install. |
| 2026-09-07 | `task lint` and `task test` | Passed | Formatting, build, and the full normal offline suite passed. |
| 2026-09-07 | `task coverage` and coverage gate | Passed | Every production assembly cleared the 90 percent line-coverage floor; Scryfall reached 94.23 percent and App reached 91.15 percent. |
| 2026-09-07 | `task surface:report` | Passed | The approved capability surface check passed; this repair adds no MCP tool, resource, prompt, mode, or toolset. |
| 2026-09-07 | Final design, reliability, test, performance, and plain-English review | Passed | No P1, P2, or deferred P3 finding remained in the changed paths. |
| 2026-09-07 | Documentation close-out | Passed | Markdown links were checked and `git diff --check` passed. |

## Completion Notes

The completed repair keeps source hierarchy in Scryfall, rule grammar in Core,
and deck workflow composition in App. The historic categorization packet
remains a record of the original delivery. This child records the approved
source-data repair and the in-place `common-v1` correction.
