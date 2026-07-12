# Deterministic Deck Categorization Fixtures And Acceptance Matrix

## Canonical Rule Fixture

The primary fixture has one Commander deck, existing Ramp, Removal, and Theme
categories, resolved and unresolved card identities, direct Oracle tags,
inherited Oracle tags, art tags, weights, and unrelated user categories.

Rules exercise:

- `allOf`, `anyOf`, and `noneOf`;
- exact ID and exact slug resolution;
- direct-only and descendant-inclusive selectors;
- every weight threshold;
- multiple category matches;
- unique primary priorities;
- add-only and synchronize-listed-categories; and
- missing/ambiguous tag and unknown card evidence.

## Common Preset Fixture

The checked-in `common-v1` fixture has a stable preset ID, schema version,
checksum, closed role keys, exact Oracle/art-tag selectors, exclusions,
hierarchy and weight settings, and a short rationale for each role. Its initial
functional vocabulary stays intentionally small and distinguishes persistent
ramp from burst mana, cost reduction, and mana fixing. It also covers common
draw/selection/tutor, interaction/protection, graveyard-hate, and recursion
roles without asserting that any role is desirable in a particular deck.

The exact role/tag mapping is an owner-reviewed artifact, not an inferred
runtime taxonomy. A preset request binds selected roles to existing deck
category IDs and supplies the assignment mode and any primary priorities.

## Validation And Preview Cases

| Case | Expected result |
| --- | --- |
| Permuted equivalent rule input | Same canonical rules, preview order, and fingerprint. |
| Missing/duplicate category rule | Invalid input and no preview. |
| Selector with ID and slug or neither | Invalid input. |
| Ambiguous/missing slug | Explicit invalid/not-found; no fuzzy match. |
| Descendant match disabled/enabled | Direct-only miss versus inherited match with hierarchy path. |
| Weight below/at threshold | Deterministic miss versus match. |
| Multiple category matches | Every category retained. |
| Duplicate primary priority | Invalid input. |
| Unresolved card/tag group | Unknown evidence; no positive match or removal authority. |
| Unrelated user category | Never changed under either assignment mode. |
| Omitted rule source | Invalid input; no preset is selected. |
| Generated schema inspection | `inline`, `common-v1`, and every supported role key are discoverable without another tool. |
| `common-v1` with valid bindings | Validation returns the complete expanded canonical inline rules plus preset ID, schema version, and checksum. |
| Preset expansion versus equivalent inline request | Identical canonical rules, matches, evidence, and category changes. |
| Unknown/duplicate preset role or missing category binding | Invalid input and no preview. |
| Edited returned expansion resubmitted inline | Uses ordinary inline behavior; no override language or hidden state. |
| Changed bytes under `common-v1` | Fails the checksum fixture; semantic changes require a new preset ID. |

## Apply Cases

- Exact unchanged preview applies once and increments deck revision once.
- Changed deck revision conflicts with zero writes.
- Changed active corpus generation conflicts with zero writes.
- Changed canonical rules or expected fingerprint conflicts with zero writes.
- Add-only never removes an assignment.
- Synchronize changes only categories represented by rules.
- Unknown evidence blocks destructive removal and is reported.
- Store failure rolls back every proposed assignment/primary change.

## MCP Surface Matrix

| Tool | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `deck_category_rules_validate` | Visible | Visible | Visible |
| `deck_category_rules_preview` | Visible | Visible | Visible |
| `deck_category_rules_apply` | Hidden | Visible | Visible |

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| CAT-001 through CAT-006 | Canonical rule, validation, truth-table, hierarchy, weight, and priority cases |
| CAT-007 through CAT-011 | Preview evidence, fingerprint, apply, conflict, transaction, and unknown cases |
| CAT-012 through CAT-015 | Forbidden scans, mode/toolset matrix, dependency architecture, and full gates |
| CAT-016 through CAT-020 | Rule-source union, preset artifact/schema, explicit binding, expansion equality, and no-default/inference cases |

## North-Star Workflow

Given a revisioned local deck and an installed Scryfall corpus, explicitly
select `common-v1`, bind its desired roles to existing categories, and validate
its fully expanded rules. Preview exact multi-category assignments with
direct/inherited evidence, then copy one expansion, edit it, and resubmit it as
inline rules. Apply only an unchanged preview, then verify revision,
assignments, primary state, preserved unrelated categories, and backup/export
behavior. No tool selects a preset, invents category meaning, or recommends a
rule.
