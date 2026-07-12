# Deterministic Deck Categorization Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-12
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are inline tag-based category rules, one explicit transparent
versioned preset, deterministic validation and expansion, evidence-preserving
preview, and guarded application to revisioned local decks. Provider
acquisition, tag scraping, runtime rule generation, semantic similarity,
recommendation, implicit preset selection, and hidden mutable category meanings
are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| CAT-001 | Must | A rule source shall reference an existing deck and bind one or more existing category IDs through complete inline rules or an explicitly selected bundled preset. | Missing/duplicate deck/category and rule-source fixtures fail without mutation. |
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
| CAT-016 | Must | Every request shall use a closed `inline` or `preset` rule-source variant; an omitted source shall never select a preset. | Variant schema, required-field, unknown-discriminator, and no-default fixtures pass. |
| CAT-017 | Must | The first bundled preset shall be `common-v1`, a checked-in immutable artifact with a schema version, checksum, closed role keys, exact selectors/exclusions/hierarchy settings, and rationale. | Artifact schema, checksum, duplicate-role, exact-tag, and stable-serialization fixtures pass. |
| CAT-018 | Must | A preset request shall name `common-v1`, an assignment mode, and nonempty role-to-existing-category bindings; supported preset IDs and role keys shall be discoverable in the generated tool schema. | Unknown role/preset, missing binding, duplicate binding, and schema-description fixtures pass. |
| CAT-019 | Must | Validation shall expand a preset into the complete canonical inline rule set and return preset identity/checksum; preview and apply fingerprints shall cover both source identity and expanded rules. | Preset-versus-expanded-inline equality and tamper/staleness fixtures pass. |
| CAT-020 | Must | The server shall never choose a preset, role binding, broad-role interpretation, or per-deck override. An agent may edit validated expanded rules and resubmit them through `inline`; no separate override language is introduced. | Forbidden-default/inference scans and preset-to-inline workflow fixtures pass. |

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
- `inline` contains the complete rule set. `preset` contains the exact preset
  ID, assignment mode, and caller bindings; only bound roles expand.
- `common-v1` distinguishes persistent ramp from burst mana, cost reduction,
  and fixing rather than silently treating all mana-adjacent evidence as one
  concept. Other roles remain a small reviewed functional vocabulary.
- A changed preset mapping requires a new preset ID. Corpus changes may change
  card matches but never the preset's selector meaning.

## Traceability

| Requirements | Validation |
| --- | --- |
| CAT-001 through CAT-006 | Contract, canonicalization, tag-resolution, and truth-table fixtures |
| CAT-007 through CAT-011 | Preview, fingerprint, conflict, transaction, and unknown-state fixtures |
| CAT-012 through CAT-015 | Forbidden behavior, mode/toolset, architecture, and full gates |
| CAT-016 through CAT-020 | Rule-source schemas, preset artifact, expansion equality, discoverability, and no-default/inference fixtures |

## Definition Of Done

- [x] The three-tool surface and exact mode visibility pass.
- [x] Equivalent rule inputs produce identical previews/fingerprints.
- [x] Apply cannot exceed or differ from an unchanged preview.
- [x] Unknown evidence never becomes a positive category claim.
- [x] No acquisition, category inference, recommendation, or project cycle is introduced.
- [x] `common-v1` is explicit, inspectable, immutable, and byte-stable; agents can use its expanded rules unchanged or resubmit edited inline rules.
