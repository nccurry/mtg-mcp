# Official Scryfall Tag Grouping Reliability Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: product owner, Core maintainer, Scryfall adapter maintainer, MCP contract maintainer
- Last updated: 2026-09-07
- Related design: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-09-07 | mtg-mcp | Initial repair packet for source-tag grouping defects. |
| 2026-09-07 | mtg-mcp | Completed the repair, validation, and documentation close-out. |

## Executive Summary

Deck category rules are meant to group cards from explicit source evidence, not
to decide what a card should do. That promise is currently weakened by lost tag
ancestry, incomplete selector validation, and an incorrect built-in mapping.

This packet repairs those defects while retaining the existing category-rule
workflow. It makes every rule resolve to official Scryfall data in one installed
data generation, passes complete ancestry to the generic evaluator, and makes
bad selector input fail before preview or apply. It also corrects `common-v1`
in place, as the owner requested.

## Audience

This document is for the repository owner and implementation agents working on
the existing `deck_category_rules_*` workflow. Readers should know the
evidence-first boundary and the Core/App/Scryfall project split.

## References

- [Evidence-first deckbuilding evolution umbrella](../../planned/evidence-first-deckbuilding-evolution/README.md)
- [North Star](../../../../north-star.md)
- [Design goals](../../../../design-goals.md)
- [Rewrite guide](../../../../rewrite-guide.md)
- [Completed deterministic categorization packet](../../completed/deterministic-deck-categorization/README.md)
- [Scryfall adapter instructions](../../../../../src/MtgMcp.Scryfall/AGENTS.md)
- `src/MtgMcp.Scryfall/ScryfallCardDataStore.cs`
- `src/MtgMcp.App/Decks/DeckCategorization.cs`
- `src/MtgMcp.Core/Decks/DeckCategorization.cs`
- [Scryfall API access guidance](https://scryfall.com/docs/faqs/i-m-having-trouble-accessing-the-scryfall-api-or-i-m-blocked-17)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| An agent groups cards from source tags. | A parent selector matches direct child tags only when `includeDescendants` is true. | The agent still chooses the rule and local category. |
| An agent sees bad tag input immediately. | Missing identity, both identities, invalid type/weight, or unknown source tag returns a typed failure before preview. | A bad selector cannot look like an ordinary unmatched card. |
| A player can trust `common-v1` as a convenience start. | It keeps the same preset ID and role keys, but every selector maps to verified official source data. | It remains optional and never auto-applies. |
| A maintainer can change source-tag code safely. | Source lookup, source hierarchy, pure evaluation, and local deck composition have separate owners and focused tests. | No generic provider layer is added. |

## System Overview

The existing workflow has three parts:

1. Scryfall stores official card and community-tag data.
2. App gets a local deck, obtains tag evidence, and prepares category rules.
3. Core evaluates the supplied rules without provider or database references.

The repair keeps this split. Scryfall remains the only owner of tag identity and
relationships. App chooses one installed data generation and passes
source-derived evidence to Core. Core stays a pure evaluator of explicit rules.

## Assumptions, Dependencies, And Constraints

- Scryfall `oracle_tags` and `art_tags` remain part of the installed four-file
  card-data generation.
- A normal test must use small checked-in Scryfall-format fixtures; any current
  source check is explicit and marked `Category=Live`.
- The existing source graph may have tags with more than one parent. The repair
  must preserve every reachable ancestor for matching rather than pick one path.
- The clean-break current release has no backward-compatibility obligation.
  The owner explicitly chose an in-place `common-v1` correction.
- The repair must not introduce a Tagger website client, tag scraping, custom
  tag storage, automatic card tagging, recommendation behavior, or a rules
  engine.
- The three existing category-rule MCP tools and their mode guards stay in
  place. Their output values may correctly change after the fix.

## Use Cases

| ID | Actor and trigger | Expected outcome |
| --- | --- | --- |
| TGR-CASE-001 | An agent selects the official source parent tag for removal and enables descendants. | A card directly assigned to a source child tag matches, with the direct tag ID and source ancestor IDs available to the evaluator. |
| TGR-CASE-002 | An agent selects the same parent tag but disables descendants. | The direct child assignment does not match. |
| TGR-CASE-003 | An agent supplies both a tag ID and a slug, or neither. | Validation returns invalid input with no preview and no deck mutation. |
| TGR-CASE-004 | An agent supplies a valid-looking slug that is absent from the installed Scryfall data generation. | Validation returns explicit source-tag not-found; it does not guess a similar tag. |
| TGR-CASE-005 | A player explicitly chooses `common-v1` and binds `card-draw` to a local category. | The preset expands to a verified official source tag rule, not the nonexistent `card-draw` source slug. |

## Scope And Non-Scope

### In scope

- Closed rule grammar validation before evaluation.
- Exact source tag lookup by ID or slug in the selected Scryfall data generation.
- Complete ancestor matching for direct source tag assignments.
- One-generation consistency for source selector resolution and category preview.
- An in-place correction of `common-v1` to verified official source tag IDs and
  declared descendant settings.
- Focused Core, Scryfall, App, MCP surface, and documentation tests.

### Out of scope

- New MCP tools, resources, prompts, toolsets, modes, config, databases, or
  data migration.
- A separate Tagger site client, adapter, storage, cache, or search surface.
- Any local card-tag labels, tag assignments, aliases, hierarchy, or role
  inference.
- Automatic category binding, preview, or apply.
- Card recommendations, deck analysis, popularity scoring, or goldfish work.
- A compatibility alias or `common-v2` preset.

### Compatibility target

Keep the existing three category-rule tool names, parameter shape, toolset, and
mode visibility. Preserve the rule-source variants and `common-v1` role keys.
The preset checksum, expanded source tag IDs, and resulting matches may change
because they correct defects. No data or configuration migration is provided.

## Stakeholders And Affected Systems

- Players and agents using category validation, preview, and apply.
- `MtgMcp.Core`, `MtgMcp.Scryfall`, `MtgMcp.App`, and their focused test projects.
- Existing `scryfall.db` tag rows and the retained Scryfall data generations.
- Existing `decks.db` category assignment transactions.
- MCP clients that use the current category-rule schemas.
- The historical `docs/reference/scryfall-tagger-tags-2026-05-23.json` file,
  which remains reference-only and is not a runtime data source.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| TGR-001 | Must | Source boundary | The repair shall use only installed Scryfall `oracle_tags` and `art_tags` data for tag identity, direct assignment, weight, and hierarchy. | Card tags remain source evidence. | Source and architecture tests show no Tagger website client, custom tag table/file, tag assignment writer, or local hierarchy. |
| TGR-002 | Must | Validation | Before source lookup or evaluation, every selector shall name exactly one nonempty source identity, one supported tag type, and one supported minimum weight; category rules and primary priorities shall also meet their closed contract. | A malformed selector must not silently mean “no match.” | Unit tests reject both/neither ID and slug, an empty ID, blank slug, unknown type, unknown weight, null selector group, duplicate category rule, and duplicate primary priority. |
| TGR-003 | Must | Source resolution | The workflow shall resolve every valid selector to one exact official source tag ID in one installed Scryfall data generation before Core evaluates it. | Slugs are input convenience, not a fuzzy matching rule. | Exact ID and exact slug resolve to the same exact-ID rule; missing or ambiguous source identity returns a typed failure with no preview or mutation. |
| TGR-004 | Must | Hierarchy | For every direct tag assignment used in grouping, the evaluator shall receive the direct tag and all source ancestors reachable in the selected generation. | A child can have more than one source parent; choosing one path loses facts. | Offline fixture tests prove direct-only behavior, parent matching when enabled, no parent match when disabled, and matching through more than one parent. |
| TGR-005 | Must | Consistency and safety | One category preview shall use one Scryfall data generation for selector resolution, direct assignments, ancestry, and its apply guard. Invalid, unavailable, mixed, or changed source data shall not authorize a deck mutation. | A preview must be reproducible and safe to apply. | Integration tests prove the generation is recorded, an invalid selector stops before preview, and changed generation or incomplete evidence blocks destructive removal. |
| TGR-006 | Must | Built-in preset | `common-v1` shall keep its existing ID and role keys but expand only to reviewed exact official Oracle-tag IDs and declared descendant behavior. | The owner selected an in-place defect correction and does not want a local tag system. | A current-source review records each selected source ID; offline fixture and preset-to-inline tests prove the mapping, checksum, and resulting matches. No `common-v2`, alias, or fallback appears. |
| TGR-007 | Must | Surface and documentation | The repair shall preserve the existing category-rule MCP surface and explain source limits, in-place preset correction, and historical snapshot status. | A behavior correction must not hide a new capability or misleading documentation. | Surface tests keep the same three tool names/toolset/modes; docs link cleanly and explain that source tags are community evidence, not card facts. |
| TGR-008 | Must | Testability | Normal validation shall stay deterministic and offline, with focused Scryfall-format fixtures covering all new behavior. | Scryfall availability must not decide CI results. | Focused tests run without live network access; a separate optional live check, if added, is marked `Category=Live`; affected production assemblies meet the coverage floor. |

## Interfaces, Data, States, And Modes

The public tools remain unchanged:

| Tool | Mode visibility | Repair effect |
| --- | --- | --- |
| `deck_category_rules_validate` | read-only, local, remote | Returns resolved exact source-ID rules or a typed validation/source-resolution failure. |
| `deck_category_rules_preview` | read-only, local, remote | Returns matches based on complete source ancestry from one data generation. |
| `deck_category_rules_apply` | local, remote | Continues to require an unchanged complete preview and refuses invalid/incomplete source evidence. |

The existing `inline` and `preset` rule-source variants remain. A user may still
give a tag ID or exact slug, but the evaluated rule uses the exact Scryfall ID.
The direct source tag remains the evidence item; its reachable ancestor IDs are
derived from Scryfall's stored relationships and are not persisted as local tags.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Source fidelity | A source child tag has two parents. | Both parents are available to an enabled-descendant selector; no made-up relationship is added. |
| Safety | A malformed/missing selector is supplied with synchronize mode. | Typed failure occurs before a preview or apply can remove a category assignment. |
| Determinism | Equivalent source tag input repeats on one installed data generation. | Resolved rules, decisions, and preview fingerprint are stable. |
| Maintainability | A maintainer changes tag lookup or deck rules. | Scryfall graph reading, App composition, and Core evaluation are in separately named cohesive code. |
| Testability | Normal CI runs without Scryfall access. | Small Scryfall-format fixture covers source lookup, hierarchy, and preset behavior. |
| Usability | An agent asks why a category matched. | Existing preview shows exact rule identity and matching direct source tag IDs; Scryfall tools remain available for tag labels/descriptions. |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Define and test pure closed rule grammar validation. | TGR-002 | Invalid inputs fail before evaluation; existing valid Core cases remain stable. |
| 2 | Add narrow Scryfall source resolution and complete ancestor reads. | TGR-001, TGR-003, TGR-004, TGR-005 | One fixture generation proves exact source lookup and all-parent matching. |
| 3 | Join resolved source evidence to deck categorization and correct `common-v1` in place. | TGR-005, TGR-006, TGR-007 | Existing tools return correct exact-ID rules/matches with no new surface. |
| 4 | Finish focused and broad validation, coverage, docs, and review. | TGR-001 to TGR-008 | Required gates pass and residual risks are recorded. |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| TGR-001 | [Source boundary](SADD.md#source-boundary) | Architecture/source inspection | No-new-source boundary check |
| TGR-002 | [Rule grammar](SADD.md#rule-grammar) | Core unit tests | Invalid-selector matrix |
| TGR-003 | [Selector handling](SADD.md#selector-handling) | Scryfall/App integration tests | ID/slug/missing/ambiguous cases |
| TGR-004 | [Source ancestry](SADD.md#source-ancestry) | Scryfall-format fixture tests | Parent/child/multiple-parent matrix |
| TGR-005 | [Preview consistency](SADD.md#preview-consistency) | App integration and apply tests | One-generation and no-removal cases |
| TGR-006 | [Common-v1 correction](SADD.md#common-v1-correction) | Fixture, App, and schema tests | Reviewed mapping and expansion equality |
| TGR-007 | [MCP surface](SADD.md#mcp-surface) | Surface/docs checks | Existing tools/modes and Markdown links |
| TGR-008 | [Test architecture](SADD.md#test-architecture) | Task suite and coverage | Offline validation evidence |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Scryfall may change a selected tag or relationship. | Risk | A preset role may no longer have a valid source mapping. | mtg-mcp | Verify current official data before code; fail visibly at runtime if a selected ID is missing; review future source updates deliberately. |
| A source graph may contain more than one parent path. | Assumption | A single path would lose matching evidence. | mtg-mcp | Use the complete reachable ancestor set, not one chosen path. |
| The historical snapshot may be stale. | Assumption | It cannot decide the final mapping. | mtg-mcp | Use it only to reproduce the discovered defect; select final IDs from current official data. |
| `common-v1` behavior will change. | Decision | Existing users receive a correction under the same preset ID. | product owner | Owner explicitly chose no compatibility version or migration. |

## Validation

The reviewed `common-v1` source mapping is recorded in
[FIXTURES.md](FIXTURES.md#common-v1-source-review-record). Focused Core,
Scryfall, and App tests passed after their phases. The completed repository
checks are `task lint`, `task test`, `task coverage`, coverage gates, and
`task surface:report`. `git diff --check`, Markdown links, and final design,
reliability, test, performance, and plain-English reviews also passed.

## Definition Of Done

- [x] Every Must requirement has objective test or inspection evidence.
- [x] Source selector and hierarchy data come only from one installed Scryfall
      data generation.
- [x] Invalid or unresolved selectors cannot create a preview or mutation.
- [x] `common-v1` remains the public preset ID and uses reviewed source tags.
- [x] Existing category-rule tool names, modes, and toolset remain unchanged.
- [x] No local tag system, Tagger-site integration, provider framework, or
      recommendation behavior is introduced.
- [x] Normal tests remain offline and the full validation evidence is recorded.
