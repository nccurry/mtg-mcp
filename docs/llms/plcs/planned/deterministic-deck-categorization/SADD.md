# Deterministic Deck Categorization Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

Core owns immutable provider-neutral category-rule, selector, evidence, preview,
and fingerprint contracts plus the deterministic evaluator. App reads one deck
through `MtgMcp.Decks`, reads already installed tag evidence through
`MtgMcp.Scryfall`, invokes Core, and delegates an authorized exact mutation to
the deck store. Decks and Scryfall never reference one another.

### Rule contracts

```text
CategoryRuleSet
  assignmentMode: add-only | synchronize-listed-categories
  rules[]

CategoryRule
  categoryId
  allOf[] | anyOf[] | noneOf[]
  primaryPriority?

TagSelector
  tagType: oracle | art
  tagId xor exactSlug
  includeDescendants
  minimumWeight: weak | median | strong | very-strong
```

Validation resolves every selector to a stable tag ID and canonicalizes rules
by category ID and selectors by tag type/ID/options. It rejects duplicate
category rules, duplicate selectors within a group, missing categories,
ambiguous slugs, invalid tag type, invalid hierarchy, and duplicate primary
priorities.

### Evaluation flow

1. Read the requested deck revision and active Scryfall corpus generation.
2. Resolve every deck entry to available Oracle/illustration identities without
   inventing missing IDs.
3. Read direct tag assignments and requested hierarchy paths.
4. Evaluate the exact `allOf`/`anyOf`/`noneOf` truth table.
5. Build canonical additions/removals/retentions and primary effects according
   to assignment mode.
6. Return every matched selector and unknown/unmatched reason.
7. Hash canonical rules, deck revision, corpus generation, and result.

Apply repeats this flow, compares the expected fingerprint, then asks the deck
store to commit exactly the previewed assignment changes in one optimistic
transaction. It never persists a partially applied rule set.

### Unknown and removal behavior

An unresolved card or unavailable tag group cannot satisfy a positive selector.
It remains `unknown`, not `false`. In `add-only`, unknown rows cause no change.
In synchronize mode, an unknown row cannot authorize removal; preview reports
the blocked removal explicitly. This prevents missing evidence from erasing
user organization.

### MCP surface

The three explicitly registered `deck_*` tools use typed request/result models.
Validation and preview are read-only/idempotent. Apply is local-write,
destructive only when synchronize mode removes assignments, and protected by
`OperationModeGuard` even under direct construction.

| Tool | Exact input | Success payload |
| --- | --- | --- |
| `deck_category_rules_validate` | `deckId` and complete `CategoryRuleSet`. | Canonical rules, current deck revision, active corpus generation, resolved category/tag IDs, and warnings. |
| `deck_category_rules_preview` | `deckId`, `expectedRevision`, and complete `CategoryRuleSet`. | Canonical additions/removals/retentions, primary effects, unknown/unmatched rows, supporting evidence, corpus generation, and preview fingerprint. |
| `deck_category_rules_apply` | `deckId`, `expectedRevision`, `expectedCorpusGeneration`, complete `CategoryRuleSet`, and `expectedPreviewFingerprint`. | New deck revision, exact applied changes, retained unknown blocks, corpus generation, and applied fingerprint. |

The complete rule set is present on every call; the first implementation stores
no hidden or named rule profile. Wire values use the exact kebab-case enums
shown above. Blank IDs, unknown enum values, duplicate fields/rules/selectors,
and missing required fields are invalid rather than defaulted.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Map category names automatically | Rejected; category meaning belongs to the caller/LLM. |
| Persist hidden default rules | Rejected; results would be harder to reproduce and review. |
| Put rules in Scryfall | Rejected; category mutation belongs to local deck workflow. |
| Let Decks reference Scryfall | Rejected; provider adapters must remain isolated. |
| Treat unknown evidence as no-match removal authority | Rejected; missing data cannot justify destructive changes. |

## Failure Modes

- Missing deck/category/tag/corpus returns structured not-found/not-cached.
- Ambiguous slug or invalid rule returns invalid input with no preview.
- Changed deck revision, corpus generation, rule set, or preview returns conflict.
- SQLite failure rolls back the entire deck mutation.
- Corpus rollback between preview/apply is a conflict, not implicit reevaluation.

## Test Architecture

Core truth-table/property tests cover canonicalization, selectors, priorities,
unknowns, and fingerprints. App tests use fake deck/Scryfall readers to verify
composition and mode guards. Temporary SQLite integration tests prove one
revisioned transaction. Official-client E2E tests install a tiny Scryfall
fixture corpus, create a disposable Commander deck, preview multi-category
assignments, apply them, and verify exact evidence/cleanup.
