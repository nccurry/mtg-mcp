# Provider Evidence Workflows Implementation Plan

> [!CAUTION]
> Historical reference only. Do not execute this broad legacy plan. Provider
> implementation is decomposed across the rewrite children linked from
> README.md; popularity/tournament sources require a future post-cutover PLC.

## Preconditions

- Move this packet to `in-progress`.
- Select one provider-specific slice with documented access.
- Record current schemas, cache policy, permissions, and fixture provenance.
- Align wire vocabulary with `mcp-trust-evidence`.

## Phase 1: Inventory And Contract Matrix

- Inventory Scryfall, Tagger, EDHREC, decklist, Playgroup, and Archidekt fields.
- Mark fact/evidence/heuristic classification, population, sample, freshness,
  permission, and cache ownership.
- Record unsupported and undocumented fields.

Exit: every exposed provider field has an owner and classification.

## Phase 2: Normalized Core Evidence

- Add the smallest source identity, retrieval context, limitation, and
  availability records needed by the selected slice.
- Keep Playgroup observations and heuristic scores separate.
- Add deterministic ordering and non-merging tests.

Exit: Core compiles without adapter references and tests remain offline.

## Phase 3: Adapter Mapping And Reliability

- Map documented payloads to normalized evidence.
- Add provider-specific cache, freshness, partial failure, pacing, permission,
  and redaction fixtures.
- Do not add scraping or browser behavior.

Exit: adapter fixture suite covers supported and degraded states.

## Phase 4: MCP Presentation

- Group evidence by source/population and expose limitations at every detail level.
- Keep schemas bounded and tool/resource descriptions accurate.
- Update source and adapter documentation.

Exit: surface inventory, structured schemas, and offline integration tests pass.

## Phase 5: Archidekt And Playgroup Safety Review

- Verify all Archidekt writes remain apply-only, guarded, sanitized, and checkpoint-aware.
- Verify raw Playgroup evidence is not relabeled as local-meta scoring.
- Run process smoke tests with fake endpoints.

Exit: guard/annotation tests, fake-HTTP tests, redaction tests, and full gates pass.

## Rollback

Add normalized metadata without deleting legacy fields until compatibility is
verified. Roll back one provider mapper independently; never replace unavailable
evidence with generated values.

## Cleanup

Remove duplicated source-label helpers only after every consumer uses the
normalized contract. Delete stale fixtures and docs when provider contracts are
retired, retaining migration notes where public output changed.
