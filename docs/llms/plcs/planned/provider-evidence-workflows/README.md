# Provider Evidence Workflows PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/provider-evidence-workflows/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: planning

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

Move this packet to `in-progress` only after one provider-specific slice and
its supported API contract are selected.
