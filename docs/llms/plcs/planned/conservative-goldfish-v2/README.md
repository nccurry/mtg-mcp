# Conservative Goldfish V2 PLC Packet

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/conservative-goldfish-v2/
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: planning

## Summary

Replace the shipped optimistic and partial race simulators with one conservative effect-model kernel. Supported abilities contribute exact modeled behavior; unsupported text contributes no estimated damage and remains visible in bounded diagnostics. Six public consumers cut over atomically after private validation.

## Packet Contents

- [SRD.md](SRD.md): public contracts, semantics, compatibility, and acceptance criteria.
- [SADD.md](SADD.md): compiler, triggers/effects, mana, runtime, output schemas, and migration.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): five gated phases with atomic cutover.
- [FIXTURES.md](FIXTURES.md): frozen Jasmine deck, rules cases, wrappers, and benchmark.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| One conservative goldfish kernel | Accepted | All wrappers and comparisons must share semantics | [Kernel](SADD.md#kernel-turn-order) |
| CompiledAbility has Trigger plus Effect | Accepted | Cards may have multiple timed abilities | [Ability model](SADD.md#compiled-ability-model) |
| Atomic public cutover without shim | Accepted exception | Existing goldfish contracts produce correctness-unsafe estimates | [Compatibility](SRD.md#compatibility-exception) |
| Projection and win-turn are specialized views | Accepted | Preserve focused tools without duplicating simulation logic | [Public result schemas](SADD.md#public-result-schemas) |
| Trust REQ-005 owns evidence tiers | Accepted dependency | Avoid a competing vocabulary | [Evidence dependency](SADD.md#evidence-and-coverage) |

## Project And Surface Impact

MtgMcp.Core gains the compiler, mana/payment model, kernel, accumulators, results, and migrated batch/brainstorm consumers. MtgMcp.App atomically replaces five goldfish tools and the goldfish subtree of deck_batch_tuning_report, plus presenters, prompts, resources, and surfaces. Docs, README, CHANGELOG, versioning migration notes, offline fixtures, live smoke task, and benchmark task change. Old goldfish engines and obsolete result models are removed.

## Current Open Questions

None.

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements are testable and have acceptance criteria.
- [x] Major alternatives and tradeoffs are recorded.
- [x] Determinism, bounds, and performance are measurable.
- [x] Core/App/adapter/test boundaries and dependencies are explicit.
- [x] All affected MCP and downstream surfaces have a cutover contract.
- [x] Archidekt validation is read-only and normal tests are offline.
- [x] Existing category, land, coverage, and evidence owners are reused.
- [x] Every Must requirement maps to fixtures and phases.
- [x] Phase exits and rollback points are objective.
- [x] Full-rules-engine behavior is explicitly excluded.

## Implementation Checklist

- [ ] Prerequisite PLCs are completed and this packet moved to in-progress.
- [ ] Baseline fixture and benchmark evidence are frozen before kernel edits.
- [ ] SRD/SADD updated when implementation changes a contract.
- [ ] No public v2 shape ships before the atomic cutover phase.
- [ ] Old engines, models, and model selection are removed in the cutover.
- [ ] Validation and performance evidence are recorded.
- [ ] Final review title is outcome-oriented.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Planning packet inspection | Pass | Contracts, semantics, fixtures, phases, and dependencies specified |

## Completion Notes

Implementation, benchmark, and read-only smoke evidence will be recorded before completion.
