# ADR 0003: Local Card Collection

Status: Accepted

Date: 2026-06-28

## Context

Phase 8 adds ownership awareness so users can answer "which of these cards do I
own?" without connecting another account or mutating a deck provider. The plan
left three choices open: first-version collection scope, persistence ownership,
and whether collection writes require `apply` mode or count as local planning
state.

## Decision

The first collection subsystem is local-first and provider-neutral. It stores one
workstation-local collection document under `MTGMCP__DATA_DIR/collection` using
the shared atomic `JsonFileStore<T>` path used by workspaces and plans.

Collection entries track card name and owned quantity only. The system accepts
structured rows and decklist-style pasted text, aggregates duplicate names
case-insensitively, and preserves display names from the submitted data. It does
not track printings, finishes, conditions, languages, binders, acquisition cost,
or provider account ids in this version.

Collection writes are local planning-state writes. They are allowed in `plan` and
`apply` modes and blocked in `read-only` mode through `OperationModeGuard`.

Ownership comparison is exposed through collection tools instead of changing the
gross-cost meaning of `deck_analyze_cost`. `collection_diff_workspace` compares
the local collection with a workspace's included cards and estimates known
missing replacement cost from cached card price snapshots.

## Consequences

Users can maintain a useful local ownership list without network access, secrets,
or third-party account coupling. The implementation stays inside Core and App
boundaries: Core owns collection state and diffs, while adapters continue to own
third-party contracts.

The model is intentionally lossy for collectors who need printing-accurate
inventory. Future import providers or print-level ownership can extend the schema
behind a new ADR and migration rather than leaking provider details into the
initial tool contract.

Because `deck_analyze_cost` remains gross deck cost, clients that want "what do I
still need to buy?" should call `collection_diff_workspace`.

## Alternatives Considered

- Track exact printings and finishes now. Rejected because it would add schema,
  UI, and provider-contract decisions before there is a stable import source.
- Store collection state per provider account. Rejected because normal offline
  operation must not require account integrations or real remote mutation.
- Require `apply` mode for collection writes. Rejected because collection data is
  local planning metadata, similar to saved edit plans, not a deck mutation.
- Mutate `deck_analyze_cost` to subtract owned cards. Rejected for this phase
  because it would change the meaning of an existing analysis result; the diff
  tool provides ownership-aware missing cost without breaking gross-cost callers.
