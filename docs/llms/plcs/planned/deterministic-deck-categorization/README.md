# Deterministic Deck Categorization PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/deterministic-deck-categorization/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: independent child review

## Summary

This packet defines deterministic evaluation of caller-supplied deck category
rules against locally installed Scryfall Oracle/art tag evidence. It performs
no provider acquisition and invents no category meaning. Validation and preview
are read operations; apply requires an exact preview fingerprint, unchanged
deck revision, unchanged Scryfall corpus generation, and local-write authority.

This packet supersedes the former Scryfall Tagger Cache design. Official tag
acquisition, storage, hierarchy, and tag lookup belong to the unified
[Scryfall Corpus And Evidence PLC](../../completed/scryfall-corpus-and-evidence/README.md).

## Dependencies

- [Accepted AMEND-004](../../in-progress/evidence-first-mcp-rewrite-program/README.md#program-amendments)
- [Completed local deck store](../../completed/local-deck-store/README.md)
- [Scryfall Corpus And Evidence](../../completed/scryfall-corpus-and-evidence/README.md)
- [Completed MCP capability toolsets](../../completed/mcp-capability-toolsets/README.md)

## Current-State Disposition

The current runtime already owns revisioned local categories, ordered
assignments, optional primary designation, optimistic mutations, and static
`decks` registration. Reuse those outcomes and the shared evidence/result
contracts. No deterministic tag-rule evaluator exists, and no former Tagger
transport, cache, classifier, or hidden taxonomy is an implementation source.

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Require explicit caller-supplied rules. | Proposed | The MCP evaluates evidence rather than deciding what a category means. |
| Use exact tag IDs or slugs and declared hierarchy behavior. | Proposed | Matching remains inspectable and reproducible. |
| Preview before apply. | Proposed | The LLM/user sees every assignment and supporting tag before mutation. |
| Bind apply to deck revision, corpus generation, rules, and preview result. | Proposed | Stale evidence cannot mutate a changed deck. |
| Preserve assignments outside rule-owned categories. | Proposed | Categorization cannot erase unrelated user organization. |
| Add the tools to `decks`, not a separate Tagger toolset. | Proposed | The operations validate and mutate local deck categories using already acquired evidence. |

## Public Surface

- `deck_category_rules_validate`: canonicalizes and validates a rule set against
  the deck categories and installed Scryfall tag corpus.
- `deck_category_rules_preview`: returns exact proposed additions/removals,
  primary-category effects, per-card tag evidence, warnings, and a fingerprint.
- `deck_category_rules_apply`: applies exactly one unchanged preview.

Validate and preview are visible in every operation mode. Apply is visible only
in `local` and `remote`. All three tools belong only to the default-enabled
`decks` toolset.
The child adds no resources or prompts. Parameterized validation, preview, and
guarded mutation are tools rather than static reference documents.

## Rule Boundary

Each rule identifies one existing deck category and declares `allOf`, `anyOf`,
and `noneOf` selectors. A selector names exactly one Oracle or art tag by ID or
slug, whether descendants count, and a minimum direct-assignment weight. The
rule set declares `add-only` or `synchronize-listed-categories` behavior and may
assign unique primary priorities. Ties or ambiguous identities are invalid,
not heuristically resolved.

## North-Star Acceptance

- Player outcome: a local deck receives repeatable multi-category assignments
  grounded in visible community tag evidence.
- Determinism: canonical rules, deck revision, corpus generation, and preview
  result produce one fingerprint and one exact mutation set.
- Unknown states: missing corpus, missing card identity, missing tag, ambiguous
  slug, stale deck, stale corpus, and unmatched card remain explicit.
- Decision boundary: the caller chooses categories, selectors, removal policy,
  and primary priority; the MCP only evaluates and applies them.

## Guardrail Conformance

This child performs deterministic evidence evaluation and an explicit local
workflow operation. It never acquires provider data, invents category meaning,
selects a strategy, or makes a recommendation. It uses `deck_*`, the `decks`
toolset, existing deck revision guards, provider-neutral Core inputs, and the
three program modes without a new database, adapter, prompt, or compatibility
surface.

## Planning Approval

- Status: Draft; independent child review required
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
