# Deterministic Deck Categorization Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-12
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Add the closed rule-source union, rule/selector/evidence/preview contracts, reviewed `common-v1` artifact, deterministic expansion, and canonical validation. | CAT-001 through CAT-006, CAT-011, CAT-012, CAT-016 through CAT-020 | Complete: contract, preset-artifact, expansion-equivalence, truth-table, unknown-state, and canonicalization tests pass. |
| 2 | Add deterministic preview evaluation and fingerprinting over fake deck/tag evidence. | CAT-005 through CAT-008, CAT-011, CAT-014 | Complete: multi-match, hierarchy, priority, evidence, and fingerprint tests pass. |
| 3 | Add guarded exact application through the revisioned deck store. | CAT-004, CAT-009, CAT-010 | Complete: conflict, transaction, rollback, and one-revision tests pass. |
| 4 | Register the three `decks` tools, expose closed preset IDs/roles in their schemas, and validate the composed workflow. | CAT-013 through CAT-020 | Complete: toolset/mode/schema, preset and edited-inline official-client, coverage, package, and installed-tool gates pass. |

## Rules

- AMEND-004, the Scryfall corpus child, and this packet must be approved before
  implementation.
- Move this packet to `in-progress/` before production edits.
- Do not add provider HTTP, a Tagger adapter/store/toolset, implicit category
  meanings, LLM inference, recommendation behavior, another tool/resource, or
  a preset-override language.
- Do not persist caller rules in the first implementation. Every request
  explicitly submits either a complete inline rule set or `common-v1` plus
  role-to-existing-category bindings.
- The exact `common-v1` role/tag artifact was reviewed as part of implementation.
  Runtime never selects, binds, or applies it automatically.

## Rollout And Rollback

The feature adds typed tools and no automatic deck migration. Disabling the
tools leaves existing category assignments untouched. A failed/stale apply
performs no write; successful changes use normal deck revision and backup/export
workflows.

## Completion Criteria

- [x] All CAT requirements map to implementation and tests.
- [x] `common-v1` is independently reviewed, immutable, schema-discoverable,
      and expands byte-for-byte to the equivalent canonical inline rules.
- [x] The Scryfall generation dependency is explicit and conflict-safe.
- [x] Preset and edited-inline preview/apply dummy-deck workflows pass from the
      package without adding tools or automatic behavior.
- [x] All audits and full repository gates pass.
