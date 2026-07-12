# Deterministic Deck Categorization PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/deterministic-deck-categorization/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-12
- Current phase: implementation complete; stabilization remains

## Summary

This packet defines deterministic evaluation of explicit deck category rules
against locally installed Scryfall Oracle/art tag evidence. A caller may submit
complete inline rules or explicitly select one transparent, versioned bundled
preset and bind its role keys to existing deck category IDs. It performs no
provider acquisition and never silently selects or applies category meaning.
Validation and preview are read operations; apply requires an exact preview
fingerprint, unchanged deck revision, unchanged Scryfall corpus generation,
and local-write authority.

This packet supersedes the former Scryfall Tagger Cache design. Official tag
acquisition, storage, hierarchy, and tag lookup belong to the unified
[Scryfall Corpus And Evidence PLC](../../completed/scryfall-corpus-and-evidence/README.md).

## Dependencies

- [Accepted AMEND-004](../../in-progress/evidence-first-mcp-rewrite-program/README.md#program-amendments)
- [Completed local deck store](../../completed/local-deck-store/README.md)
- [Scryfall Corpus And Evidence](../../completed/scryfall-corpus-and-evidence/README.md)
- [Completed MCP capability toolsets](../../completed/mcp-capability-toolsets/README.md)
- [MCP Contract And Adapter Hardening](../../completed/mcp-contract-and-adapter-hardening/README.md)

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
| Offer an explicit `common-v1` preset as an alternative to inline rules. | Proposed | Most agents need a sane inspectable starting point; requiring every session to reinvent common functional roles would reduce consistency. |
| Expand presets into ordinary canonical rules during validation. | Proposed | The caller can inspect the exact tags and then use the expansion unchanged or edit it as an inline rule set without a second override language. |

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

Every request contains a closed `ruleSource`: `inline` supplies a complete rule
set, while `preset` explicitly names `common-v1`, an assignment mode, and one
or more bindings from discoverable preset role keys to existing deck category
IDs. An omitted source never defaults to a preset.

Each expanded rule identifies one existing deck category and declares `allOf`,
`anyOf`, and `noneOf` selectors. A selector names exactly one Oracle or art tag
by ID or slug, whether descendants count, and a minimum direct-assignment
weight. The rule set declares `add-only` or
`synchronize-listed-categories` behavior and may assign unique primary
priorities. Ties or ambiguous identities are invalid, not heuristically
resolved.

## Sane Defaults Without Hidden Decisions

`common-v1` is a checked-in, reviewable data artifact with a stable ID, schema
version, checksum, closed role vocabulary, exact tag selectors, exclusions,
hierarchy behavior, and short rationale per role. Its implemented role keys are
intentionally limited to `ramp`, `card-draw`, `removal`, and `recursion`.
Burst mana, cost reduction, mana fixing, and other roles remain candidates for
later preset versions rather than being silently folded into these roles.

The schema exposes the supported preset ID and role keys. Validation returns
the fully expanded canonical inline rules plus preset identity and checksum.
An agent that wants deck-specific semantics edits that returned expansion and
submits it as `inline`; the MCP does not need an override mini-language. A
changed mapping creates `common-v2` rather than silently changing `common-v1`.
No preset is selected, bound, previewed, or applied automatically.

## North-Star Acceptance

- Player outcome: a local deck receives repeatable multi-category assignments
  grounded in visible community tag evidence.
- Determinism: canonical rules, deck revision, corpus generation, and preview
  result produce one fingerprint and one exact mutation set.
- Unknown states: missing corpus, missing card identity, missing tag, ambiguous
  slug, stale deck, stale corpus, and unmatched card remain explicit.
- Decision boundary: the caller chooses inline rules or an explicit preset,
  category bindings, removal policy, and primary priority; the MCP expands,
  evaluates, and applies only that visible choice.

## Guardrail Conformance

This child performs deterministic evidence evaluation and an explicit local
workflow operation. It never acquires provider data, invents category meaning,
selects a strategy, or makes a recommendation. It uses `deck_*`, the `decks`
toolset, existing deck revision guards, provider-neutral Core inputs, and the
three program modes without a new database, adapter, prompt, or compatibility
surface.

## Implementation Evidence

- Phase 1: Core contracts and evaluator implemented with exact, unknown,
  descendant, primary-priority, synchronization, and ordering tests.
- Phase 2: App composition resolves local deck entries through the shared
  Scryfall service, preserves corpus-generation evidence, and fingerprints the
  expanded rules and decisions.
- Phase 3: Apply recomputes the preview, verifies its token/fingerprint and
  revision, and commits only the exact category delta through one deck-store
  transaction.
- Phase 4: The three tools are registered in `decks`; official-client schema,
  mode, annotation, architecture, package, and full offline gates pass.
- Current surface after this child is 32/54/54 for `default` and 57/80/93 for
  `all` by mode. No provider write or legality decision was introduced.

## Planning Approval

- Status: Approved and implemented
- Reviewed by: Repository owner via implementation request
- Review date: 2026-07-12
- Reviewed revision: d7307f8 plus implementation changes
- Implementation authorized: Yes
