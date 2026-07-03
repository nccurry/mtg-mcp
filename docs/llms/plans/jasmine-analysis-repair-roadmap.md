# Jasmine Analysis Repair Roadmap

## Status

- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- State: Planned

## Purpose

This roadmap replaces the earlier monolithic Jasmine analysis proposal with six independently reviewable PLC packets. Each finding has one owner. Packets cross-link prerequisites instead of copying schemas or requirements.

The changing card count observed across live Archidekt imports is not a seventh MCP defect: investigation showed that deck 22958528 changed remotely between imports. The frozen fixture is deterministic test truth; live remote edits are external state, not an MCP count race.

## Finding Ownership

| Original finding | Owning PLC | Ownership boundary |
| --- | --- | --- |
| Unknown metadata looked complete; known-empty looked missing | [card-snapshot-integrity](../plcs/planned/card-snapshot-integrity/README.md) | Persisted field-group coverage and readiness |
| Archidekt lacked produced mana, root colors, and direct nested combat stats | [card-snapshot-integrity](../plcs/planned/card-snapshot-integrity/README.md) | Narrow adapter mapping fixes |
| Moxfield lacked colors, produced mana, and coverage | [card-snapshot-integrity](../plcs/planned/card-snapshot-integrity/README.md) | Narrow adapter mapping fixes |
| Refresh scopes could fall through to all cards | [card-snapshot-integrity](../plcs/planned/card-snapshot-integrity/README.md) | Closed analysis-needed/unknown-scope contract |
| Hydration failure could lose or obscure a successful provider import | [card-snapshot-integrity](../plcs/planned/card-snapshot-integrity/README.md) | Save-before-hydrate, failure/cancellation/redaction |
| Total, excluded, maybeboard, and sideboard counts had incompatible meanings | [deck-count-contracts](../plcs/planned/deck-count-contracts/README.md) | Canonical primary-category partition |
| Legacy maybeboardCards and roleCounts are public | [deck-count-contracts](../plcs/planned/deck-count-contracts/README.md) | Additive cardCounts and 0.9 preservation |
| Reveal/pay/discard lands were classified as normally untapped | [land-entry-classification](../plcs/planned/land-entry-classification/README.md) | Shared printed entry classification |
| Secondary excluded categories removed included cards from profile evidence | [simulation-profile-evidence](../plcs/planned/simulation-profile-evidence/README.md) | Primary-category auto-profile input |
| Overlapping roles inflated auto-profile evidence | [simulation-profile-evidence](../plcs/planned/simulation-profile-evidence/README.md) | Per-signal-family card deduplication |
| Built-in profiles presented speculative common routes | [simulation-profile-evidence](../plcs/planned/simulation-profile-evidence/README.md) | Remove automatic routes; preserve user intent |
| Stats Lab measured only seen and post-development held-up interaction | [stats-lab-interaction-readiness](../plcs/planned/stats-lab-interaction-readiness/README.md) | Pre-spend/current-hand checkpoints and additive metrics |
| Previously cast interaction could be called a mana failure | [stats-lab-interaction-readiness](../plcs/planned/stats-lab-interaction-readiness/README.md) | Disjoint failure precedence |
| Goldfish used optimistic pressure, speculative routes, and inconsistent models | [conservative-goldfish-v2](../plcs/planned/conservative-goldfish-v2/README.md) | One conservative effect-model kernel |
| Goldfish mana ignored yields/restrictions and complex symbols | [conservative-goldfish-v2](../plcs/planned/conservative-goldfish-v2/README.md) | Closed mana source/payment model |
| Goldfish outputs were embedded in comparison, batch, and brainstorm consumers | [conservative-goldfish-v2](../plcs/planned/conservative-goldfish-v2/README.md) | Atomic six-consumer cutover and old-code removal |
| Goldfish coverage, diagnostics, traces, and performance lacked exact bounds/evidence | [conservative-goldfish-v2](../plcs/planned/conservative-goldfish-v2/README.md) | Bounded schemas, frozen fixture, benchmark |
| Live Archidekt card count changed between observations | External state, no PLC | Remote deck edits; frozen fixture plus read-only smoke |

## Dependency And Delivery Order

| Order | Packet | Dependency |
| ---: | --- | --- |
| 1 | [card-snapshot-integrity](../plcs/planned/card-snapshot-integrity/README.md) | None |
| 2 | [deck-count-contracts](../plcs/planned/deck-count-contracts/README.md) | None |
| 3 | [land-entry-classification](../plcs/planned/land-entry-classification/README.md) | None |
| 4 | [simulation-profile-evidence](../plcs/planned/simulation-profile-evidence/README.md) | Existing DeckCategoryInclusion helper only |
| 5 | [conservative-goldfish-v2](../plcs/planned/conservative-goldfish-v2/README.md) | Packets 1, 3, and 4; packet 2 preferred; trust-evidence REQ-005 |
| 6 | [stats-lab-interaction-readiness](../plcs/planned/stats-lab-interaction-readiness/README.md) | Packets 1 and 3; independent of goldfish |

Packets 1 through 4 may be implemented independently. Conservative goldfish waits for its required prerequisites. Stats Lab may proceed before or after goldfish once metadata integrity and land classification are complete.

## Compatibility Policy

| Packet | Policy |
| --- | --- |
| card-snapshot-integrity | Additive persisted coverage with conservative old-snapshot migration; unknown refresh scopes become validation errors |
| deck-count-contracts | Add cardCounts; preserve maybeboardCards and roleCounts names, types, and behavior through 0.9 |
| land-entry-classification | No schema change; corrected classifications are documented correctness changes |
| simulation-profile-evidence | Preserve profile keys and user intent format; corrected auto selection and removal of speculative built-in routes are documented |
| stats-lab-interaction-readiness | Add new metrics/scenario/dimension; preserve all named legacy Stats Lab contracts through 0.9 |
| conservative-goldfish-v2 | Explicit broken-correctness exception permits atomic goldfish schema replacement with no shim; unrelated contracts are excluded |
| mcp-trust-evidence | Remains generally additive except where it delegates/supersedes goldfish-specific requirements |

## Shared Fixtures And Ownership

| Fixture concept | Owner | Consumers |
| --- | --- | --- |
| Old/new workspace coverage JSON | card-snapshot-integrity | Stats Lab and goldfish consume implemented semantics, not copied fixtures |
| Category inclusion/count matrix | deck-count-contracts | Goldfish may reuse canonical cardCounts after completion |
| Land oracle-text matrix | land-entry-classification | Stats Lab and goldfish use the classifier |
| Profile evidence microdecks | simulation-profile-evidence | Goldfish consumes resolved profiles |
| Stats interaction microdecks/calibration suite | stats-lab-interaction-readiness | Stats Lab only |
| Frozen Jasmine deck 22958528, effect microdecks, wrappers, and benchmark | conservative-goldfish-v2 | Goldfish surfaces, batch, brainstorm, and live smoke |
| Evidence tier serialization | mcp-trust-evidence REQ-005 | Goldfish ability diagnostics link to the canonical fixture |

The frozen Jasmine fixture is owned only by conservative-goldfish-v2. Other packets use minimal feature-specific payloads and do not make the live deck their deterministic source.

## Decisions Not Duplicated

- Metadata field membership, authoritative-empty rules, migration, and provider ownership exist only in card-snapshot-integrity.
- Zone aliases and partition equations exist only in deck-count-contracts.
- Printed tapped-land classification exists only in land-entry-classification; consumers decide condition satisfaction.
- Auto-profile evidence and descriptive-route policy exist only in simulation-profile-evidence; goldfish executes only compiled effects.
- Stats Lab checkpoints, scorecard dimensions, and failure buckets exist only in stats-lab-interaction-readiness; Stats Lab remains a separate heuristic analyzer.
- Goldfish triggers, effects, mana payment, multiplayer rules, public result schemas, and breaking exception exist only in conservative-goldfish-v2.
- Canonical evidence tiers remain owned by mcp-trust-evidence REQ-005; goldfish owns only ability evidence application and goldfish detail gating.
- Generic evidence provenance, provider trust, and non-goldfish detail gating remain outside all six repair packets.

## Lifecycle Guidance

Move each packet independently from planned to in-progress only when its prerequisites and phase-entry criteria are satisfied. Implementation branches update their own packet evidence. Do not move the roadmap as a substitute for moving a packet, and do not expose the v2 goldfish public surface before its atomic cutover phase.
