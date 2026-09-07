# Commander Spellbook Evidence Implementation Plan

## Document Control

- Lifecycle status: Complete
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/IMPLEMENTATION_PLAN.md#phase-3-commander-spellbook-evidence)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related requirements: [SRD.md](SRD.md)
- Related design: [SADD.md](SADD.md)
- Implementation authorized: Yes

## Strategy

Build the adapter first. Prove its cache, pacing, source JSON, and failures
without MCP hosting. Then add the three tool methods and the saved-deck bridge.
Finish with surface, package, and documentation checks.

The work stays in one integrated branch. The adapter and App changes share one
public surface and one cache design.

## Phase Summary

| Phase | Goal | Requirements | Main areas | Exit |
| --- | --- | --- | --- | --- |
| 1 | Build the narrow provider adapter. | CSB-002, CSB-003, CSB-005 to CSB-014 | New adapter and tests. | Fake-HTTP, cache, pacing, and contract tests pass. |
| 2 | Add the saved-deck MCP workflow. | CSB-001, CSB-004, CSB-006, CSB-015 | App, E2E, architecture, solution, Task. | Toolset and deck flow work in an isolated process. |
| 3 | Finish docs and full proof. | All requirements. | Docs, Task, coverage, package tests. | Full gate and phase audit pass. |

## Phase 1: Build the Provider Adapter

- Create `src/MtgMcp.Spellbook` and `tests/MtgMcp.Spellbook.Tests`.
- Add only a Core project reference and the adapter-owned SQLite package.
- Capture a small checked-in contract snapshot from the official OpenAPI
  schema. Record v6.3.3, the three chosen routes, their inputs, the deck
  request fields, the paginated deck response layout, its checksum, and the
  review date. Do not add a copy of the source's whole schema.
- Add `SpellbookContract`, small request and evidence records in their own
  files, `SpellbookDatabase`, `SpellbookCache`, `SpellbookRequestPacer`,
  `SpellbookTransport`, and `SpellbookService` as separate concrete owners.
- Use a fake `HttpMessageHandler`, a temporary SQLite database, and a
  controllable `TimeProvider` in normal tests.
- Add response, error, cache, pace, cancellation, size, and redaction cases.
- Prove that a raw query with quotes, whitespace, and parentheses is encoded
  once without semantic rewriting; reject whitespace-only input and always
  send the chosen explicit page defaults.
- Add the one fixed transport timeout. Prove that it becomes unavailable while
  caller cancellation still propagates.
- Prove cache identity includes the current contract checksum, so a contract
  change misses rather than relabeling an old response as current.
- Prove two separately constructed pacer/database owners reserve starts with
  immediate SQLite transactions, and that an active cooldown stops a reserved
  request before HTTP.

Exit criteria:

- Search, get, and find-my-combos work through fake HTTP.
- No test uses a network connection.
- The cache has no separate raw request or local-path fields, and tests cover
  the documented provider-paging-link limitation.
- Every success shows the current sent request without putting it in the cache.
- One tool operation maps to one or zero upstream calls.
- The adapter project clears its 90 percent coverage gate.

## Phase 2: Add App Composition and MCP Tools

- Add `src/MtgMcp.App/Spellbook` with a small deck input resolver, read tools,
  and toolset manifest.
- Add `CapabilityToolset.Spellbook` after `Playgroup` in the stable registry.
- Keep `spellbook` out of the default profile.
- Add the adapter project reference to `MtgMcp.App`.
- Update the scoped App instructions so `spellbook` is an allowed stable
  toolset alongside the existing provider toolsets.
- Add the cache time setting to the configuration loader, public configuration
  status, capability resource, and documented configuration.
- Add the service and deck-store composition to `FoundationHost`.
- Add the three read-only tool annotations, descriptions, structured schemas,
  and tool names.
- Update the solution, architecture assertions, Task test targets, coverage
  target, and E2E smoke selector.
- Add process tests for `default`, `all`, `none`, and explicit `spellbook`.
- Add one separate-server process test for a saved deck with commander, main,
  sideboard, and maybeboard rows. Its seed callback stores a valid cache result
  through the adapter before the child process starts. The child reads that
  cache result and makes no network request.

Exit criteria:

- The default tool count stays unchanged.
- `all` gains three read tools in every mode.
- The explicit `spellbook` profile exposes three read tools in every mode.
- A changed deck revision stops before provider HTTP.
- The capability resource describes the enabled toolset and cache schema.

## Phase 3: Finish and Prove the Change

- Update `docs/adapters.md`, `docs/toolsets.md`, `docs/rewrite-guide.md`, the
  main README, and relevant configuration examples so the documented stable
  module and toolset lists match the code.
- Add one live, read-only, bounded variant lookup with `Category=Live`.
- Run focused adapter, App, architecture, and E2E tests first.
- Run `task lint`, `task test`, `task coverage`, `task surface:report`, and
  `task smoke:mcp`.
- Run the installed package smoke check after packing.
- Run code-quality, correctness, test, abstraction, and plain-language audits.
- Fix every P1 or P2 finding. Record any justified P3 follow-up in this packet.

Exit criteria:

- The code and documents agree on all three tools and cache behavior.
- The full validation suite passes.
- The final phase audit passes.
- This packet contains final command results and deferred work.

## Cleanup

Remove temporary route notes and duplicated source metadata after the contract
owner exists. Do not leave a generic provider helper, a raw request recorder, or
an unused compatibility path.

## Rollback

The toolset is opt-in. If the source contract fails in release validation,
remove the `spellbook` project, App registration, and Task wiring together.
Do not leave a disabled placeholder tool or a compatibility alias.
