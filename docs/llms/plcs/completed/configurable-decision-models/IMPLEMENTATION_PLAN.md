# Configurable Decision Models Implementation Plan

> [!CAUTION]
> Historical post-cutover reference only. Do not execute these phases on legacy
> `main` or the stable rewrite. A future experimental feasibility PLC must first
> select one concrete model, re-establish any required profile base, and adopt a
> reviewed subset of this design.

## Preconditions

- Move this packet to `in-progress`.
- Select the first two or three existing hard-coded decisions.
- Record their current behavior and calibration baseline.
- Approve configuration and output budgets.

## Phase 1: Inventory And Compatibility Contract

- Map current mulligan, sequencing, and play-choice branches.
- Choose the smallest configurable slice.
- Freeze built-in profile IDs and current default behavior in tests.

Exit: compatibility fixtures pass without adding configurable behavior.

## Phase 2: Core Types And Pure Evaluator

- Add immutable snapshots, policy IDs/versions, allowed predicates, budgets,
  typed outcomes, and decision traces.
- Add exhaustive-switch, ordering, immutability, and budget tests.

Exit: Core tests are offline, deterministic, and dependency-light.

## Phase 3: Configuration Validation

- Extend simulation profile configuration with allowlisted choices only.
- Reject unknown versions, fields, predicates, and out-of-range parameters.
- Add file, array, inheritance, migration, and invalid-schema fixtures.

Exit: every invalid fixture has a stable path and reason.

## Phase 4: Simulation Integration

- Route the selected existing decisions through the evaluator.
- Preserve default behavior with built-in policy definitions.
- Add seed, version, fingerprint, assumptions, and trace metadata.

Exit: legacy fixtures remain compatible and replay fixtures are stable.

## Phase 5: MCP Presentation And Calibration

- Add bounded summary and normal/full trace presentation.
- Update tool descriptions and simulation documentation.
- Run calibration comparisons and record intentional deltas.

Exit: surface, smoke, calibration, lint, test, and coverage gates pass.

## Rollback

Keep each integrated decision behind the built-in compatibility policy until
its phase is green. Roll back one policy integration without removing the
validated evaluator types or corrupting persisted data.

## Cleanup

Remove replaced hard-coded branches only after equivalent built-in policy
fixtures pass. Delete temporary compatibility adapters and stale profile docs.
