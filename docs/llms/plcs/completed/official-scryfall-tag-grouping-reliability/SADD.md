# Official Scryfall Tag Grouping Reliability Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: product owner, Core maintainer, Scryfall adapter maintainer, MCP contract maintainer
- Last updated: 2026-09-07
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-09-07 | mtg-mcp | Initial design for a narrow source-tag grouping repair. |
| 2026-09-07 | mtg-mcp | Completed the source-reader, rule-resolver, tests, and close-out checks. |

## Executive Summary

The repair has one important rule: Scryfall owns tag facts; Core evaluates
already-resolved evidence; App joins that evidence to a local deck. This fixes
the current break in that chain without creating a tag product inside mtg-mcp.

Today, a card's direct Scryfall tag arrives in Core with a list containing only
the direct tag. A parent selector therefore has no way to see that child. The
repair resolves every caller slug to an exact source ID, reads all source
ancestors for the direct tags in the selected Scryfall data generation, and
passes those IDs to the generic evaluator.

The design deliberately rejects a generic tag provider, a Tagger website
integration, and a local tag map. `common-v1` stays a small optional shortcut
to official source IDs; it is not a new tag system and does not decide
categories for the caller.

## Goals, Non-Goals, And Design Drivers

### Goals

- Make enabled-descendant matching correct for every source parent relationship.
- Make malformed, missing, and ambiguous selectors explicit typed failures.
- Resolve valid input to exact Scryfall IDs before Core evaluates it.
- Correct `common-v1` in place using current official Scryfall data.
- Keep Core pure, Scryfall-specific code in its adapter, App thin, and normal
  tests offline.
- Keep file and type ownership obvious enough to change one part safely.

### Non-goals

- Defining what deck categories mean or choosing them for a player.
- Adding custom tag labels, tag assignments, aliases, hierarchy, or a tag UI.
- Broadening Scryfall's public MCP tools, adding Tagger acquisition, or adding a
  new provider abstraction.
- Replacing exact matching with semantic similarity, text parsing, or an LLM.
- Changing tool names, operation modes, toolsets, or local database formats.

### Design drivers

- The source graph can contain multiple parent paths. A single retained path is
  not enough evidence for parent matching.
- `common-v1` is current product behavior, but the owner chose correctness over
  backwards compatibility.
- A selector must be safe in synchronize mode. Invalid input cannot become a
  reason to remove an existing category assignment.
- Existing `DeckCategorization.cs` mixes workflow composition, preset expansion,
  validation, and MCP wrappers. The repair must not make that file a larger
  catch-all owner.

## Context And Scope

```text
Official Scryfall bulk data
  oracle_tags / art_tags
           |
           v
MtgMcp.Scryfall
  exact ID lookup + direct assignments + all reachable ancestors
           |
           v
MtgMcp.App
  local deck + exact source-ID rules + one data generation
           |
           v
MtgMcp.Core
  pure rule validation and deterministic matching
           |
           v
Existing deck_category_rules_* tools
```

Scryfall continues to own download, cache, storage, source metadata, and graph
queries. App owns orchestration between a local deck and Scryfall. Core owns
only the closed rule grammar and deterministic matching. The existing tools
continue to expose the workflow and operation-mode guard.

## Constraints

- Use the existing Scryfall bulk datasets and `scryfall.db`. Category work
  reads installed data only: `default` and `cache-only` use that data, while
  `refresh` asks the caller to run explicit card-data sync first.
- Preserve source tag kind, direct assignment weight, and all reachable source
  ancestors. Do not infer equivalent tags.
- A source slug resolves exactly within the caller-selected Oracle or art kind;
  matching never falls back to a similar label or another kind.
- Core must not reference Scryfall, SQLite, HTTP, or MCP SDK types.
- App must not duplicate Scryfall tag SQL or keep a separate tag cache.
- No automatic migration, alias, or backward-compatible `common-v2` path.
- All normal tests use fake data and temporary databases.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Continue passing only direct tag IDs. | Keep the current card-tag projection. | No code change. | Parent rules cannot match direct child tags. | Rejected |
| Choose one shortest source path. | Reuse the existing one-path helper. | Small representation. | Loses valid parents in a multi-parent graph. | Rejected |
| Add local tag aliases and category mappings. | Maintain a project-owned grouping vocabulary. | Could hide source changes. | Violates evidence-first boundaries and the owner decision. | Rejected |
| Add a Tagger adapter/database/toolset. | Treat Tagger as a second source. | Superficially explicit. | Duplicates existing Scryfall data and violates repository rules. | Rejected |
| Create a generic tag-provider interface. | Abstract source reads behind one contract. | Looks reusable. | Adds a leaky abstraction for one concrete source. | Rejected |
| Read exact Scryfall tag IDs and all source ancestors, then map them to Core evidence with no Scryfall types. | Keep source logic in Scryfall and rule evaluation in Core. | Complete, inspectable, small, and testable. | Requires a narrow adapter read and clear data-generation handling. | Chosen |

## Chosen Design

### Source boundary

Scryfall is the only source for card-tag identity, relationship, direct
assignment, and weight. mtg-mcp does not make its own tags, alter source tags,
or acquire them from the Tagger website. The historical Tagger-directory file
remains a reference snapshot and never becomes an import or runtime fallback.

### Rule grammar

Core receives only valid rule data. A small pure validator checks the closed
rule grammar before any source lookup:

- exactly one of nonempty tag ID or nonblank exact slug;
- tag kind is `oracle` or `art`;
- minimum weight is `weak`, `median`, `strong`, or `very-strong`;
- selector groups are present and category IDs/rules are unique;
- primary priorities are unique when supplied.

The validator reports `OperationInvalidInput` for malformed request data. It
does not look up Scryfall, normalize a loose label, or decide which tag the
caller meant.

### Selector handling

After pure validation, App asks Scryfall to resolve each unique selector in one
installed data generation. A tag ID must exist in the declared kind. A slug must
resolve to exactly one tag in that same kind. The result is an exact-ID rule set
whose selectors use exact source IDs.

Missing source data returns a typed not-found outcome. An impossible duplicate
identity in the installed data returns an explicit unavailable/invalid-source
outcome. Neither case creates a preview, and neither tries a fuzzy alternative.

Core matches only resolved exact IDs. This makes `includeDescendants` apply equally
to requests that began as IDs and requests that began as slugs.

### Source ancestry

Scryfall adds a narrow data read for the direct source tag IDs collected from
the deck card data. It returns, for each direct tag, the direct tag ID plus every
reachable ancestor ID in the same stored source graph. It must walk upward from
the direct tags and preserve every parent route; it must not choose one shortest
path as the whole answer.

The returned ancestor IDs are derived data for this evaluation. They are not
saved into `decks.db`, written back to `scryfall.db`, or exposed as project-owned
tag facts. App maps the result to category evidence with no Scryfall types; Core uses
that evidence only when a selector explicitly enables descendants.

The ancestor read operates once for the distinct direct tags collected for a
preview rather than scanning the whole graph once per deck card. It is a small
tag-specific reader inside the Scryfall adapter, not a generic data framework.

### Preview consistency

App asks Scryfall to choose one complete installed data generation for selector
resolution, then binds every card and ancestor read to that generation. If a
card cannot be read from the selected generation, or if that generation changes
before an apply, the existing unknown/conflict protection applies and
destructive removal is blocked.

The preview fingerprint continues to cover the deck revision, resolved rules,
source data generation, and decisions. The repair must make the generation
chosen for source resolution the same generation recorded in the fingerprint.

### Common-v1 correction

`common-v1` stays the only preset ID and retains its role keys: `ramp`,
`card-draw`, `removal`, and `recursion`. Those words are input keys for binding
to a caller's local deck categories; they are not source card tags.

Before code is written, the implementer must inspect current official Scryfall
data and record the exact Oracle-tag IDs, descendant settings, and source
metadata in the fixture. The historical snapshot identifies why a repair is
needed but cannot decide the final mapping. The corrected preset expands to the
same exact source-ID rules as an equivalent inline request.

This explicitly supersedes the historical instruction that any changed preset
mapping needs `common-v2`. The owner decided that this mapping is defective and
must be corrected in place. No alias, migration, or fallback tag is allowed.

## Data Design

No persistent data format changes.

| Data | Owner | Use in this repair | Lifetime |
| --- | --- | --- | --- |
| Scryfall tags, relationships, assignments, and dataset metadata | `scryfall.db` / Scryfall adapter | Exact source lookup and ancestor derivation | Existing retained generation lifecycle |
| Local deck categories and assignments | `decks.db` / Decks | Existing preview/apply target | Existing revisioned deck lifecycle |
| Resolved exact source-ID rules and ancestor IDs | App/Core memory | One validation or preview evaluation | Request lifetime only |
| `common-v1` source mapping | Checked-in definition | Optional expansion into exact-ID rules | Build/runtime read only |

The mapping contains official source IDs and declared matching settings.
It does not contain local card assignments, aliases, or a tag hierarchy.

## Building Blocks

| Building block | Responsibility | Owned data/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| Core rule records and validator | Closed grammar and exact rule shape | Request lifetime | Records/results with no Scryfall types | Core only | Core unit tests |
| Core evaluator | Deterministic exact ID, weight, and ancestor matching | Request lifetime | Evaluation result with no Scryfall types | Core only | Truth-table tests |
| Scryfall tag reader | Exact source lookup and complete upward graph traversal | Read-only active/retained generation | Narrow internal adapter operation | Scryfall SQLite data | Scryfall fixture tests |
| App rule resolver | Combines pure grammar checks, source resolution, and `common-v1` expansion | Request lifetime | Internal exact-ID rules | Core + Scryfall | App unit/integration tests |
| App categorization coordinator | Reads deck, gathers source evidence, calls Core, fingerprints preview | Request lifetime | Existing tool result models | Decks + Scryfall + Core | App integration tests |
| Existing MCP tool wrappers | Request binding and operation-mode guard | Host request | Existing three tool names | App coordinator | Surface/E2E tests |

Keep these as small named classes/files when code is implemented. Do not add a
generic interface simply to make the list look symmetrical, and do not put
Scryfall SQL or MCP attributes into Core.

## Runtime And Data Flow

### Validation or preview

1. App reads the requested local deck and validates category ownership.
2. Core validates closed rule grammar without provider calls.
3. App expands `common-v1`, if selected, then asks Scryfall to resolve every
   distinct tag identity in one installed data generation.
4. App replaces valid selector slugs with exact source IDs and returns a typed
   failure if any source identity is missing or ambiguous.
5. App uses generation-bound card/tag reads, then asks Scryfall once for all
   needed direct-tag ancestor sets.
6. App maps direct tag, kind, weight, and source ancestor IDs to Core evidence.
7. Core evaluates the exact-ID rules. App builds the existing preview, with a
   fingerprint tied to the deck revision and source data generation.

### Apply

1. The existing local-write guard runs first.
2. App recomputes the same exact-ID preview.
3. A changed deck revision, source data generation, exact-ID rule set, or
   preview fingerprint returns conflict with zero writes.
4. Incomplete evidence or an invalid/unresolved source selector never permits a
   synchronize-mode removal.
5. The deck store performs the existing one-revision transaction only for an
   unchanged complete preview.

## MCP Surface

No tool is added, removed, renamed, or moved.

| Tool | Existing responsibility after repair | Mode |
| --- | --- | --- |
| `deck_category_rules_validate` | Validate closed grammar and return resolved exact official source-ID rules. | read-only, local, remote |
| `deck_category_rules_preview` | Show proposed grouping from direct source tags and selected source ancestors. | read-only, local, remote |
| `deck_category_rules_apply` | Apply only an unchanged complete preview. | local, remote |

The `decks` toolset remains the only owner. No new resource, prompt, setting, or
installation step is necessary.

## Error Handling And Failure Modes

| Situation | Result | Mutation allowed? |
| --- | --- | --- |
| Both/neither selector identities, blank slug, bad tag kind/weight, duplicate rule/priority | `OperationInvalidInput` | No |
| Source tag absent from selected generation | Typed not-found | No |
| Source data has an impossible ambiguous identity or broken graph read | Typed unavailable/invalid-source outcome | No |
| Category work requests `refresh` | Typed result that requires explicit card-data sync | No |
| Card lacks source tag evidence | Existing unknown result | Add-only: no match; synchronize: no removal authority |
| Source data generation changes after preview | Existing conflict outcome | No |
| Unchanged complete preview | Existing success result | Yes, under local/remote guard |

Errors must identify the tag kind/identity safely but must not expose database
paths, raw download URLs, credentials, or raw provider payloads.

## Project Boundaries

```text
MtgMcp.Core       <- no Scryfall/SQLite/MCP reference
      ^
      | rule/evidence records with no Scryfall types
MtgMcp.App        <- coordinates deck + Scryfall and owns MCP tools
      ^                         ^
      |                         |
MtgMcp.Decks      MtgMcp.Scryfall <- owns source tag SQL and graph
```

No project reference changes are expected. The Core model may rename its
single-path field to an explicit ancestor-ID collection because the old name
cannot truthfully represent a multi-parent source graph. This is a clean-break
contract correction inside the existing workflow, not a provider leak.

## Readability And Documentation

- Use `official Scryfall community tags` for source data. Do not call the
  project-owned behavior a tag system or a tag manager.
- Call the preset values `role keys` and call the external data `source tags`.
- Keep source resolution, direct-tag ancestry, pure rule validation, and MCP
  wrappers in separately named cohesive code. Delete duplicated matcher or
  validation paths when their replacement lands.
- Update historical preset wording to state the owner-approved in-place repair.
- Keep comments short and concrete: explain source-graph or safety rules, not
  implementation history.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| TGR-001 | Scryfall adapter is the only source reader; no custom tag persistence exists. | Architecture/source inspection and Scryfall-format fixtures |
| TGR-002 | Core performs closed grammar checks before source access. | Core invalid-input matrix |
| TGR-003 | App resolves exact source IDs before evaluation. | ID/slug/missing/ambiguous tests |
| TGR-004 | Scryfall returns complete ancestor sets, not a selected path. | Parent/child/multiple-parent fixture |
| TGR-005 | One source data generation feeds resolution, evidence, and fingerprint. | Generation/conflict/no-removal tests |
| TGR-006 | `common-v1` expands to reviewed exact source IDs in place. | Preset fixture and inline-equivalence tests |
| TGR-007 | Existing three tools/modes/toolset remain fixed. | Surface/schema/docs checks |
| TGR-008 | Fake source data and temporary SQLite cover normal tests. | Focused Task checks and coverage |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Core rule records/validator and Core tests | TGR-002 | Closed grammar rejects every malformed selector before evaluation. |
| 2 | Scryfall tag reader/service and Scryfall fixtures/tests | TGR-001, TGR-003, TGR-004 | Exact source lookup and all-parent ancestor sets pass from an offline data generation. |
| 3 | App categorization resolver/coordinator, App tests, preset artifact/docs | TGR-005, TGR-006, TGR-007 | Existing workflow uses one generation and corrected `common-v1`; no surface growth. |
| 4 | Focused/broad Task checks, coverage, docs, specialist re-review | TGR-001 to TGR-008 | All P1/P2 findings fixed; remaining P3 findings fixed when inexpensive or recorded. |

## Test Architecture

- Core unit tests cover rule grammar independently of Scryfall: empty/both
  identity, tag kind, weight, null groups, duplicate rules, duplicate priorities,
  exact ID matching, direct-only matching, and enabled descendant matching.
- Scryfall tests import a small Scryfall-format data generation with one direct
  tag, a parent, a second parent, and direct source assignments. They prove
  exact lookup, absent lookup, complete ancestor IDs, and no graph mutation.
- App tests create a temporary deck, import that fixture, validate/preview
  parent selectors by ID and slug, test synchronize safety, and compare
  `common-v1` expansion with equivalent inline rules.
- MCP/surface tests prove tool names, `decks` membership, and mode visibility
  are unchanged.
- Any live source check is `Category=Live`, uses documented Scryfall pacing and
  headers, and is not part of ordinary `task test`.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Correct `common-v1` in place. | Decision | Existing preset behavior changes to fix source-data defects. | Owner approved; update checksum/tests/docs without `common-v2` or migration. |
| Preserve all reachable source ancestors. | Decision | Multi-parent tag graph becomes correct for parent matching. | Use an explicit ancestor-ID collection, not one chosen path. |
| Current source mapping may differ from historical snapshot. | Risk | Final preset IDs cannot be guessed from old data. | Verify current official data before coding and record the result. |
| Tagger website integration. | Deferred/rejected | Would duplicate source data and violate repository policy. | Do not implement. |
| New category tags or inference. | Deferred/rejected | Would turn source evidence into local classification. | Do not implement. |

## Glossary

| Term | Meaning |
| --- | --- |
| Official Scryfall community tag | A tag record, relationship, or assignment from Scryfall's installed `oracle_tags` or `art_tags` data. It is source evidence, not a card fact. |
| Direct tag | The tag Scryfall assigned directly to a card's Oracle or illustration identity. |
| Ancestor tag | A source tag reachable by following parent relationships upward from a direct tag. |
| Local deck category | A player-owned category stored with a local deck, such as a category the player labels “Removal.” |
| Role key | One of the existing `common-v1` input keys that a caller binds to a local deck category. It is not a card tag. |
