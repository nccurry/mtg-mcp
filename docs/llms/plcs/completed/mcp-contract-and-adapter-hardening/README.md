# MCP Contract And Adapter Hardening PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/mcp-contract-and-adapter-hardening/`
- Owner: mtg-mcp
- Created: 2026-07-06
- Last updated: 2026-07-06
- Current phase: all implementation and acceptance phases complete

## Summary

This packet closes the pre-statistics contract, model-usability, ownership, and
lifecycle findings without adding strategic judgment. It makes capability
readiness honest, replaces the flat batch-mutation input with a closed schema,
adds exact-only local-deck identity reconciliation over existing Scryfall
evidence, and decomposes oversized Scryfall and Archidekt owners without
changing their provider behavior.

## Dependencies

- [Rewrite program](../evidence-first-mcp-rewrite-program/README.md)
- [Local Deck Store](../../completed/local-deck-store/README.md)
- [MCP Capability Toolsets](../../completed/mcp-capability-toolsets/README.md)
- [Scryfall Corpus And Evidence](../../completed/scryfall-corpus-and-evidence/README.md)
- [Archidekt Deck Sync](../../completed/archidekt-deck-sync/README.md)
- [Playgroup Public API](../../completed/playgroup-public-api/README.md)

## Packet Contents

- [SRD.md](SRD.md): required behavior and acceptance criteria.
- [SADD.md](SADD.md): chosen contracts, component boundaries, and runtime flow.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): reviewable delivery phases.
- [FIXTURES.md](FIXTURES.md): schemas, identity cases, surface matrices, and traceability.

## Current-State Evidence And Disposition

- `FoundationResources` projects each manifest's static
  `CapabilityToolsetAvailability`; Archidekt and Playgroup therefore say
  `available` even when their redacted auth tools say no credential is
  configured. Rebuild this projection as schema 6; retain the static registry.
- `DeckChangeInput` has one discriminator plus fourteen unrelated optional
  fields and one generic failure. Rebuild the public input as eleven exact
  branches; retain the Core `DeckChange` union and transactional store path.
- Scryfall/Deck inputs usually describe bounds, but many Archidekt and
  Playgroup identifiers, cursors, page sizes, and guards have no input-schema
  description. Reuse the SDK description mechanism and add a complete schema
  lint rather than a second metadata registry.
- `ScryfallDatabase` and `ScryfallService` currently span roughly 2,439 and
  2,001 lines across corpus, snapshots, coordination, acquisition, and mapping.
  `ArchidektService`, transport, mapper, and App coordinator span roughly
  1,509, 1,096, 960, and 905 lines. Retain tested behavior and extract the
  already-visible capability families; copy no generic legacy abstraction.
- The existing `scryfall_card_collection` plus `deck_apply_changes` can resolve
  identities, but the LLM must manually join rows and construct safe mutations.
  Rebuild that composition as exact preview/apply tools while preserving both
  lower-level capabilities.

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Report implementation and credential configuration separately. | Implemented | Static registration is not proof that credentials are valid. |
| Use a discriminated batch-change union. | Implemented | Each change advertises only fields it can consume. |
| Keep exact-only identity resolution behind preview/apply guards. | Implemented | Useful normalization does not require fuzzy selection or deckbuilding judgment. |
| Keep public adapter facades while extracting cohesive internal owners. | Implemented | Existing callers stay stable while implementation responsibility becomes traceable. |
| Add no legality tool. | Accepted product boundary | Format and Commander legality are outside this evidence server's current mission. |

## Public Surface

The child adds exactly two tools to the default-enabled `decks` toolset:

- `deck_identity_reconcile_preview`: visible in `read-only`, `local`, and
  `remote`; read-only for deck state and open-world when acquisition is needed.
- `deck_identity_reconcile_apply`: visible in `local` and `remote`; performs
  one guarded local deck revision and never writes a remote provider.

The child also replaces, without an alias, the input schema of
`deck_apply_changes` and schema version 5 of `mtg://server/capabilities`.
No resource, prompt, database, configuration key, or production assembly is
added.

## Toolset And North-Star Acceptance

- Toolset: both new tools belong only to `decks`.
- User question answered: which exact Scryfall identities support these stored
  entries, which fields can be normalized without choosing a printing, and
  what atomic local revision would those exact matches produce?
- Evidence type: provider/corpus identity evidence and deterministic
  reconciliation, never a legality or quality judgment.
- Replay boundary: deck revision, selected entry IDs, ordered lookups, retained
  Scryfall generation or snapshot, algorithm version, preview fingerprint, and
  process-authenticated apply token. Restarting the MCP invalidates the token
  and requires a new preview.
- Unknown boundary: missing, conflicting, pruned, unacquired, or provider-failed
  evidence stays explicit and is never treated as a nonmatch.
- Decision boundary: fuzzy matching, printing choice for name/Oracle matches,
  partial application, legality, categories, and strategic interpretation stay
  with the caller LLM.
- Complete workflow: inspect a deck, preview exact identity reconciliation,
  review each result, explicitly allow a partial result when appropriate, and
  apply one revision-guarded local update.

## Planning Approval

- Status: Approved
- Reviewed by: Nick Curry, repository owner
- Review date: 2026-07-06
- Reviewed revision: `0ad3dcc` plus the accepted decision-complete plan
- Implementation authorized: Yes, by the repository owner's explicit request to implement this plan

AMEND-005 and this packet are approved. Production implementation proceeds in
the reviewable phases defined by the implementation plan.

## Completion Evidence

- Capability schema 6 reports implementation, credential configuration,
  selection, and mode-filtered counts without provider I/O.
- The eleven-variant batch schema exposes only relevant, fully described
  fields; indexed invalid-input diagnostics pass through the official client.
- Exact-only identity preview/apply preserves non-identity deck state and
  proves 75-item provider batching, the 150-entry bound, retained-evidence
  replay, partial authorization, stale/tampered/pruned refusal, and atomic
  revision behavior.
- Scryfall card, corpus, snapshot, storage, and coordination responsibilities
  and Archidekt deck/folder/snapshot, transport, mapper, and synchronization
  responsibilities are separated behind stable public facades.
- Offline gates, package/install smokes, dependency checks, per-assembly
  coverage above 90 percent, requested audits, and bounded read-only Scryfall
  and Archidekt live checks passed on 2026-07-06.

## Planning Readiness Checklist

- [x] Objective and non-goals are explicit.
- [x] Dependencies and current-state findings are recorded.
- [x] Public tools, schemas, modes, and exact counts are specified.
- [x] Identity precedence, mutation boundary, and unknown states are specified.
- [x] Adapter ownership boundaries avoid generic abstractions.
- [x] Unit, integration, schema, fixture, live, and audit gates are traceable.
- [x] Repository-owner review and AMEND-005 acceptance are recorded.
- [x] Implementation is authorized.
- [x] Every implementation phase and acceptance gate is complete.
