# Archidekt Essentials And Synchronization Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are redacted auth status, deck list/get/create/delete, exact
card/printing/zone/category mapping, provider binding, three-way diff, and
preview/apply pull and push. All adjacent Archidekt features and any background
sync are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| ARCH-001 | Must | The adapter shall own all Archidekt transport payloads, authentication, pacing, retries, and mapping. | Architecture tests prevent Core/Decks from referencing transport types. |
| ARCH-002 | Must | Auth status shall report only configured/usable/error state, never usernames, tokens, passwords, or credential paths. | Redaction tests pass. |
| ARCH-003 | Must | List/get shall map deck metadata, exact printing identity, quantity, zone, categories, primary category, and remote relation IDs when present. | Sanitized fixture variants pass. |
| ARCH-004 | Must | Remote canonical fingerprints shall cover mapped metadata, ordered entries, zones, categories, and provider IDs. | Equivalent payload variants hash equally; meaningful changes differ. |
| ARCH-005 | Must | Pull/push previews shall fetch fresh remote state and return local/remote/baseline three-way differences. | Conflict matrix fixtures pass. |
| ARCH-006 | Must | No apply shall proceed without matching local revision, remote fingerprint, and preview fingerprint. | Stale local/remote tests perform zero writes. |
| ARCH-007 | Must | Pull apply shall create a local deck or replace the bound local canonical content transactionally without mutating Archidekt. | Temporary DB and fake HTTP tests pass. |
| ARCH-008 | Must | Push apply shall execute only the explicit previewed operations in stable order. | Captured request sequence matches preview exactly. |
| ARCH-009 | Must | Concurrent local and remote changes since baseline shall return conflict; no automatic merge policy shall exist. | Three-way conflict fixtures pass. |
| ARCH-010 | Must | Remote create shall default to private, create/bind a shell, and require a separate push for contents. | Create fixture and mode tests pass. |
| ARCH-011 | Must | Remote delete shall require exact deck ID, fresh fingerprint, and explicit confirmation. The adapter shall use the observed `DELETE /api/decks/{id}/` contract and verify absence without assuming that a deleted-ID read returns `404`. If delete or absence verification drifts, this child remains incomplete and cutover is blocked rather than emulating deletion. | Delete/absence fixtures pass and the unsupported case fails the completion gate. |
| ARCH-012 | Must | Requests shall be globally serialized per configured account with at least two seconds between starts and no more than 30 starts in any rolling 60 seconds. Each tool call shall also have a maximum of 150 provider requests. The bounds are client safety ceilings, not provider guarantees; a predicted call above the cap shall fail before the first mutation. | Fake-clock, rolling-window, preflight, zero-write, and cap tests pass. |
| ARCH-013 | Must | 403 shall stop immediately. A 429 shall stop the current operation, record a sanitized provider cooldown, and prevent another request before a valid `Retry-After`; it shall not retry the failed request automatically. Mutation requests shall not be retried automatically after ambiguous transport/5xx failure. | Request-count, cooldown, and partial-status tests pass. |
| ARCH-014 | Must | A partial remote apply shall return applied/unknown/not-attempted operations and require a fresh pull before another push. | Partial failure fixture passes. |
| ARCH-015 | Must | Read/previews shall be available in all modes, pull apply in `local|remote`, and remote mutations only in `remote`. | Surface tests pass. |
| ARCH-016 | Must | Contract fixtures shall be sanitized, dated, endpoint-versioned, and checked for drift. | Manifest and drift tests pass. |
| ARCH-017 | Must | Real mutations shall occur only in explicit `Category=Live` tests using the configured Archidekt credential file or equivalent host secret. Tests shall create uniquely named private dummy decks, delete in `finally`, and fail if a fresh authenticated listing still contains the probe. | Live discovery, secret-redaction, private-create, and residual-deck guards pass. |
| ARCH-018 | Must | An observed bulk mutation endpoint shall remain disabled unless a sanitized fixture, primitive-operation equivalence test, and opt-in live proof establish the same final remote snapshot and failure semantics. | Removing any one proof selects primitive operations; fixture-only proof cannot enable bulk writes. |
| ARCH-019 | Must | Missing baseline, stale/corrupt baseline, and deleted-remote states shall be explicit and shall never delete local data, silently rebind, or guess a synchronization direction. | State fixtures return conflict/unavailable/deleted evidence and perform zero unpreviewed writes. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Conflict safety | Any stale revision/fingerprint produces zero writes. |
| Provider safety | At most 30 starts/minute, two-second spacing, hard per-call cap, stop/cooldown on block, no ambiguous write retries. |
| Transparency | Preview and partial apply enumerate exact operations/status. |
| Isolation | No provider DTO crosses the adapter boundary. |
| Testability | Ordinary tests use sanitized fake HTTP only. |

## Definition Of Done

- [ ] Essential read and sync workflows pass fixtures.
- [ ] Conflict and partial-write behavior is proven.
- [ ] At least one discoverable live throwaway workflow is documented and opt-in.
- [ ] Verified remote deletion leaves no throwaway deck after the live workflow.
- [ ] Excluded Archidekt features are absent from the surface.
