# Deterministic Deck Categorization Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-12
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

Core owns immutable provider-neutral category-rule, selector, evidence, preview,
and fingerprint contracts plus the deterministic evaluator. App owns one
checked-in, immutable preset catalog, expands an explicitly selected preset to
ordinary Core rules, reads one deck through `MtgMcp.Decks`, reads already
installed tag evidence through `MtgMcp.Scryfall`, invokes Core, and delegates an
authorized exact mutation to the deck store. Decks and Scryfall never reference
one another.

### Rule contracts

```text
CategoryRuleSource
  InlineRuleSource
    ruleSet
  CommonPresetRuleSource
    presetId: common-v1
    assignmentMode
    bindings[]: roleKey + categoryId + primaryPriority?

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

The generated MCP schema exposes the closed `common-v1` preset ID and role-key
vocabulary. App loads its reviewable artifact, verifies its schema version and
checksum, binds each requested role to an existing deck category, and returns
the fully expanded canonical `CategoryRuleSet`. The evaluator receives only
ordinary rules and remains unaware of product taxonomy.

### Evaluation flow

1. Validate the explicit rule-source variant and expand a preset, when selected.
2. Read the requested deck revision and active Scryfall corpus generation.
3. Resolve every deck entry to available Oracle/illustration identities without
   inventing missing IDs.
4. Read direct tag assignments and requested hierarchy paths.
5. Evaluate the exact `allOf`/`anyOf`/`noneOf` truth table.
6. Build canonical additions/removals/retentions and primary effects according
   to assignment mode.
7. Return every matched selector and unknown/unmatched reason.
8. Hash the source identity, expanded canonical rules, deck revision, corpus
   generation, and result.

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
| `deck_category_rules_validate` | `deckId` and complete `CategoryRuleSource`. | Source identity, fully expanded canonical rules, current deck revision, active corpus generation, resolved category/tag IDs, and warnings. |
| `deck_category_rules_preview` | `deckId`, `expectedRevision`, and complete `CategoryRuleSource`. | Source identity, expanded rules, canonical additions/removals/retentions, primary effects, unknown/unmatched rows, supporting evidence, corpus generation, and preview fingerprint. |
| `deck_category_rules_apply` | `deckId`, `expectedRevision`, `expectedCorpusGeneration`, complete `CategoryRuleSource`, and `expectedPreviewFingerprint`. | New deck revision, exact applied changes, retained unknown blocks, corpus generation, and applied fingerprint. |

Every call explicitly supplies either inline rules or a preset selection. The
first implementation stores no caller rules and never defaults to a preset.
`common-v1` is immutable: semantic changes require a new preset ID. Its small
functional vocabulary distinguishes persistent ramp from burst mana, cost
reduction, and mana fixing rather than silently treating those roles as
equivalent. Validation returns the expansion so an agent can copy, edit, and
resubmit it as inline rules; there is no preset-override language. Wire values
use the exact kebab-case enums shown above. Blank IDs, unknown enum values,
duplicate fields/rules/selectors, and missing required fields are invalid rather
than defaulted.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Map category names automatically | Rejected; category meaning belongs to the caller/LLM. |
| Apply a hidden or automatic default profile | Rejected; the caller must explicitly choose `common-v1` and bind its roles. |
| Offer one explicit, immutable, transparent preset | Accepted; it provides a reproducible starting point without deciding deck categories or applying changes. |
| Add a preset override mini-language | Rejected; edit the returned expansion and submit ordinary inline rules. |
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
unknowns, and fingerprints. App tests verify the checked-in preset artifact,
schema discoverability, byte-stable expansion, expansion equivalence to inline
rules, fake deck/Scryfall composition, and mode guards. Temporary SQLite
integration tests prove one revisioned transaction. Official-client E2E tests
install a tiny Scryfall fixture corpus, create a disposable Commander deck,
preview both preset and edited-inline multi-category assignments, apply them,
and verify exact evidence/cleanup. Independent child review must approve the
exact `common-v1` role/tag mapping artifact before implementation authorization.
