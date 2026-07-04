# Deterministic Deck Categorization Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are explicit tag-based category rules, deterministic validation and
preview, evidence-preserving assignment, and guarded application to revisioned
local decks. Provider acquisition, tag scraping, automatic rule generation,
semantic similarity, recommendation, and hidden default categories are out of
scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| CAT-001 | Must | A rule set shall reference an existing deck and existing category IDs and contain one or more explicit category rules. | Missing/duplicate deck/category fixtures fail without mutation. |
| CAT-002 | Must | Each selector shall identify exactly one Oracle/art tag by ID or exact slug and declare descendant inclusion and minimum weight. | ID, slug, ambiguous, missing, hierarchy, and weight fixtures pass. |
| CAT-003 | Must | Rules shall support `allOf`, `anyOf`, and `noneOf` with documented empty-group semantics. | Truth-table fixtures exhaust every group combination. |
| CAT-004 | Must | A rule set shall explicitly select `add-only` or `synchronize-listed-categories`; categories absent from the rules shall never change. | Mutation-diff fixtures prove ownership boundaries. |
| CAT-005 | Must | Multiple matching categories shall be retained; optional primary priorities shall be unique and determine primary assignment by ascending value. | Multi-match, no-primary, priority, and tie-rejection fixtures pass. |
| CAT-006 | Must | Validation shall canonicalize rule/selector ordering and return deck/category/tag/corpus identities, warnings, and no mutation. | Permuted equivalent inputs serialize identically. |
| CAT-007 | Must | Preview shall return exact additions, removals, retained assignments, primary effects, unmatched/unknown cards, and supporting direct/inherited tag evidence. | Golden preview fixtures expose every decision input. |
| CAT-008 | Must | Preview fingerprint shall cover canonical rules, deck ID/revision, active Scryfall corpus generation, and canonical proposed changes. | Any changed component yields a different fingerprint. |
| CAT-009 | Must | Apply shall require deck ID, expected revision, corpus generation, canonical rules, and expected preview fingerprint and shall recompute before one transaction. | Stale deck/corpus/rule/preview fixtures return conflict and write zero rows. |
| CAT-010 | Must | Apply shall make exactly the previewed category changes and increment the deck revision once. | Transaction and rollback tests pass. |
| CAT-011 | Must | Missing card identity or tag evidence shall remain unknown and shall not match a positive selector or become removal authority unless explicitly included in synchronize behavior. | Unknown-versus-nonmatch fixtures pass without invented facts. |
| CAT-012 | Must | The evaluator shall infer no category meaning from names, descriptions, oracle text, card popularity, or an LLM. | Forbidden dependency/marker tests pass. |
| CAT-013 | Must | Validation and preview shall be visible in all modes; apply shall require `local` or `remote`; all three tools belong only to `decks`. | Exact toolset/mode and direct-guard tests pass. |
| CAT-014 | Must | Decks and Scryfall projects shall not reference one another; App composes store reads and Core evaluates provider-neutral rule inputs. | Project-reference and namespace architecture tests pass. |
| CAT-015 | Must | Normal tests shall remain deterministic/offline with at least 90 percent line coverage per affected production assembly. | Full quality gates pass. |

## Rule Semantics

- `allOf`: every selector must match; an empty group is satisfied.
- `anyOf`: at least one selector must match; an empty group is satisfied.
- `noneOf`: no selector may match; an empty group is satisfied.
- A direct assignment satisfies its own tag selector. Descendants satisfy a
  selector only when `includeDescendants=true` and the stored hierarchy path is
  valid.
- Weight order is `weak < median < strong < very-strong`.
- `add-only` adds matched assignments and never removes an assignment.
- `synchronize-listed-categories` makes only categories named by rules match
  the preview; unrelated category assignments remain untouched.
- Lowest unique `primaryPriority` among matched rules becomes primary. Without
  a priority, no new primary choice is made.

## Traceability

| Requirements | Validation |
| --- | --- |
| CAT-001 through CAT-006 | Contract, canonicalization, tag-resolution, and truth-table fixtures |
| CAT-007 through CAT-011 | Preview, fingerprint, conflict, transaction, and unknown-state fixtures |
| CAT-012 through CAT-015 | Forbidden behavior, mode/toolset, architecture, and full gates |

## Definition Of Done

- [ ] The three-tool surface and exact mode visibility pass.
- [ ] Equivalent rule inputs produce identical previews/fingerprints.
- [ ] Apply cannot exceed or differ from an unchanged preview.
- [ ] Unknown evidence never becomes a positive category claim.
- [ ] No acquisition, category inference, recommendation, or project cycle is introduced.
