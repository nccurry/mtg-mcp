# Playgroup Public API Implementation Plan

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Phases

| Phase | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Pin spec and add drift/operation inventory. | PLAY-001, PLAY-002, PLAY-013 | Contract tests pass. |
| 2 | Add transport models, auth, and safe GET client. | PLAY-003 through PLAY-007, PLAY-010, PLAY-011, PLAY-014 | Adapter fixtures pass. |
| 3 | Add all thirteen read tools. | PLAY-002 through PLAY-007 | Surface and E2E tests pass. |
| 4 | Add two remote-gated write tools with fixture-only contract proof. | PLAY-008, PLAY-009, PLAY-015 | Write safety/contract fixtures pass; no live write path exists for the pinned contract. |
| 5 | Add optional safe live-read proof and full validation. | All | Live-read discovery, no-write guards, and offline gates pass. |

## Rules

- Re-fetch and compare the OpenAPI document before implementation starts.
- A changed operation/schema requires a recorded version/size/checksum and
  operation/schema/auth diff, followed by reviewed fixture/model/tool updates;
  never accept it through silent code generation.
- Do not add derived ranking, cross-provider hydration, or private endpoints.
- Do not run live writes against the pinned contract; it has no documented
  cleanup. Reconsider only if a future official contract adds safe disposal.

## Rollback

Unregister the adapter/tools. No local persistence or migration is owned by this
module, and no automatic remote cleanup is assumed.
