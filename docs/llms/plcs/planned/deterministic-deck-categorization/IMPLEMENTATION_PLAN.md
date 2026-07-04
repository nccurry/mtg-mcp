# Deterministic Deck Categorization Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Add closed rule/selector/evidence/preview contracts and canonical validation. | CAT-001 through CAT-006, CAT-011, CAT-012 | Contract, truth-table, unknown-state, and canonicalization tests pass. |
| 2 | Add deterministic preview evaluation and fingerprinting over fake deck/tag evidence. | CAT-005 through CAT-008, CAT-011, CAT-014 | Multi-match, hierarchy, priority, evidence, and fingerprint tests pass. |
| 3 | Add guarded exact application through the revisioned deck store. | CAT-004, CAT-009, CAT-010 | Conflict, transaction, rollback, and one-revision tests pass. |
| 4 | Register the three `decks` tools and validate the composed workflow. | CAT-013 through CAT-015 | Toolset/mode, official-client, coverage, package, and installed-tool gates pass. |

## Rules

- AMEND-004, the Scryfall corpus child, and this packet must be approved before
  implementation.
- Move this packet to `in-progress/` before production edits.
- Do not add provider HTTP, a Tagger adapter/store/toolset, default category
  meanings, LLM inference, or recommendation behavior.
- Do not persist rules in the first implementation; callers submit the complete
  canonicalizable rule set for validation, preview, and apply.

## Rollout And Rollback

The feature adds typed tools and no automatic deck migration. Disabling the
tools leaves existing category assignments untouched. A failed/stale apply
performs no write; successful changes use normal deck revision and backup/export
workflows.

## Completion Criteria

- [ ] All CAT requirements map to implementation and tests.
- [ ] The Scryfall generation dependency is explicit and conflict-safe.
- [ ] The exact preview/apply dummy-deck workflow passes from the package.
- [ ] All audits and full repository gates pass.
