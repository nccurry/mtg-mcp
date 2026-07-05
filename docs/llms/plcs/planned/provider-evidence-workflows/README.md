# Provider Evidence Workflows PLC Packet

> [!WARNING]
> **Rewrite disposition: absorbed/reference-only — do not implement this packet
> as a cross-provider layer.** Current owners are
> [Scryfall corpus and evidence](../../completed/scryfall-corpus-and-evidence/README.md),
> [Archidekt sync](../../completed/archidekt-deck-sync/README.md),
> [Playgroup public API](../../completed/playgroup-public-api/README.md), and
> and [deterministic deck categorization](../deterministic-deck-categorization/README.md).
> Popularity/tournament
> sources remain in the program's
> [post-cutover registry](../../in-progress/evidence-first-mcp-rewrite-program/README.md#post-cutover-registry).
> Reviewed against the rewrite on 2026-07-03; lifecycle movement is deferred to
> authorized foundation implementation.
> All transport and acquisition language elsewhere in this packet is historical
> and non-normative. Official Scryfall bulk/API contracts in the replacement
> packet are the only planned card/tag acquisition authority.

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/provider-evidence-workflows/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: implementation retired; principles absorbed by provider children

## Summary

This packet plans consistent provenance, freshness, population, cache, and
permission semantics across provider-backed evidence while preserving the
meaning and ownership of each distinct dataset.

## Packet Contents

- [SRD.md](SRD.md): provider and evidence requirements.
- [SADD.md](SADD.md): adapter ownership and normalized evidence design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): source-by-source delivery phases.
- [FIXTURES.md](FIXTURES.md): attribution, freshness, permission, and mutation matrices.

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Keep third-party wire contracts in adapters. | Accepted | Core must remain independent of provider payload churn. |
| Preserve source populations instead of blending them. | Proposed | EDHREC, tournaments, Tagger, and Playgroup answer different questions. |
| Treat Playgroup observations separately from local-meta scoring. | Proposed | Observations are evidence; scoring is a heuristic model. |
| Keep Archidekt mutation apply-only and checkpoint-aware. | Accepted | Remote deck changes require explicit authority and recovery. |
| Prohibit scraping and undocumented claims. | Accepted | Provider workflows must use supported access and attributable fields. |

## Rewrite Reconciliation

| Legacy principle | Rewrite disposition |
| --- | --- |
| Adapter-owned wire contracts and non-blended source populations | Retained by every provider child. |
| `plan`/`apply` operation modes | Superseded by `read-only`/`local`/`remote`; mode is mutation authority, not offline state. |
| Archidekt apply/checkpoints | Superseded by explicit preview/apply, three-way baseline, fingerprints, and no checkpoint surface. |
| PEW-010 blanket prohibition on scraping/undocumented access | Retained. AMEND-004 removes the former proposed exception and uses official Scryfall bulk/API contracts. |
| EDHREC, TopDeck, EDHTop16, and other popularity/tournament providers | Deferred to `popularity-evidence-sources`; not stable `0.9.0` scope. |
| Playgroup local-meta scoring | Removed; the official adapter returns provider-shaped observations only. |

Community-tag evidence remains separately labeled from official card facts even
though both are acquired and stored by the unified Scryfall child.

## Project And Surface Impact

Expected work spans adapter-owned DTOs and HTTP behavior, Core normalized
evidence records, App presenters and descriptions, provider fixtures, cache
diagnostics, and source documentation. It must align with
[mcp-trust-evidence](../mcp-trust-evidence/README.md).

## Current Open Questions

| Question | Impact | Resolution plan |
| --- | --- | --- |
| Which tournament providers have stable documented aggregate access? | Determines initial adapter scope. | Require documented terms and fixtureable contracts before selecting a provider. |
| What freshness defaults fit each source? | Affects cache warnings and refresh cost. | Define per-source defaults from volatility and provider guidance; do not use one global TTL. |

## Planning Readiness Checklist

- [x] Source meaning and non-merging rules are explicit.
- [x] Adapter/Core/App ownership is explicit.
- [x] Archidekt mutation safety is explicit.
- [x] Scraping and unsupported-claim non-goals are explicit.
- [ ] Initial provider contract and freshness table are approved.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Foundation follow-up packet drafted | Passed | Planning only; no provider calls or mutations added. |

## Completion Notes

Do not move this packet to `in-progress`. Provider work proceeds only through
the owning rewrite child or a separately reviewed post-cutover PLC.
