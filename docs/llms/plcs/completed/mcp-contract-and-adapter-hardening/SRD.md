# MCP Contract And Adapter Hardening Software Requirements Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Nick Curry, repository owner
- Last updated: 2026-07-06
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-07-06 | Codex | Initial decision-complete draft from the pre-statistics audit and owner direction. |
| 2026-07-06 | Codex | Recorded completed implementation, validation, audits, and bounded live-read acceptance. |

## Executive Summary

The implemented rewrite has strong deterministic boundaries but four contract
and maintainability defects remain: capability rows conflate implementation
with readiness, one deck batch tool exposes unrelated optional fields,
Archidekt and Playgroup inputs have uneven schema guidance, and Scryfall and
Archidekt concentrate several proven responsibilities in oversized owners.
Local entries also require mechanical multi-tool work to acquire exact
Scryfall identities safely. This child resolves those issues before statistics
adds another production assembly and public toolset.

## Scope And Non-Scope

- In scope: capability schema 6, closed batch inputs, complete root-parameter
  descriptions, two exact identity tools, Scryfall/Archidekt internal
  decomposition, lifecycle reconciliation, tests, docs, and audit closure.
- Out of scope: legality, format rules, card-role inference, fuzzy names,
  recommendations, category assignment, statistics, provider endpoint
  expansion, provider contract changes, and database schema changes.
- Compatibility: clean-break `0.9.0-preview`; no aliases for replaced schemas.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| HARD-001 | Must | Capability rows shall distinguish implemented code from credential configuration and shall not label every provider toolset available. | Schema-6 snapshots cover each toolset with absent and configured credentials. |
| HARD-002 | Must | Capability rendering shall perform no HTTP, credential verification, filesystem mutation, or database write and shall expose no secret, identity, or path. | Spies and leakage scans pass. |
| HARD-003 | Must | Each capability row shall expose `implementationStatus`, `credentialState`, and `authenticationStatusTool` using the closed values in this packet. | Official-client schema and ordering snapshots pass. |
| HARD-004 | Must | `deck_apply_changes` shall accept a closed discriminated union whose eleven branches expose only applicable fields. | MCP schema contains eleven unique `kind` constants and branch-specific required fields. |
| HARD-005 | Must | Invalid batch changes shall name the zero-based failing index, change kind when readable, and required shape without echoing values. | Unknown, missing, empty-ID, and null branch tests pass with sanitized diagnostics. |
| HARD-006 | Must | Every root MCP input and every batch-union property shall have a useful schema description including bounds, identity namespace, cursor semantics, or guard meaning where applicable. | Schema lint covers the complete registered surface. |
| HARD-007 | Must | `deck_identity_reconcile_preview` shall resolve 1 through 150 explicitly selected or stored entries in canonical deck order without mutating deck state. | Boundary, ordering, and zero-deck-write tests pass. |
| HARD-008 | Must | Identity precedence shall be printing ID, exact set/collector/language, Oracle ID, then exact card name; fuzzy lookup is forbidden. Non-English set/collector resolution requires exact corpus evidence and shall never substitute an English printing. | Precedence, language, and no-fuzzy fixtures pass without adding a provider route. |
| HARD-009 | Must | Exact printing matches may normalize canonical name, Oracle ID, printing ID, set, collector, and language; Oracle/name matches may normalize only canonical name and Oracle ID. | Before/after fixtures prove no arbitrary printing is selected. |
| HARD-010 | Must | Reconciliation shall preserve quantity, finish, zone, sort order, category relationships, and provider bindings. | Apply preservation fixture is byte-equivalent outside allowed identity fields and revision timestamps. |
| HARD-011 | Must | Duplicate lookups shall be acquired once while each entry retains an ordered result. Provider collection batches remain at most 75 identifiers and use existing pacing. | 75/76/150 and duplicate transport fixtures pass. |
| HARD-012 | Must | Preview shall return per-entry status, match method, safe message, before/after identity, evidence origin/reference, completeness, fingerprint, and opaque apply token. | Structured output schema and golden results pass. |
| HARD-013 | Must | Apply shall validate token integrity, fingerprint, deck ID/revision, selection, algorithm version, and retained Scryfall evidence before one atomic revision. | Tamper, stale, mismatch, pruned-evidence, rollback, and success fixtures pass. |
| HARD-014 | Must | An incomplete preview shall require explicit `allowPartial=true`; otherwise apply shall make no change. | Partial refusal and authorized-partial fixtures pass. |
| HARD-015 | Must | Read-only mode shall reuse retained evidence without writes and shall return the existing local-write-required outcome before acquisition requiring persistence. | Database/HTTP spies and mode E2E pass. |
| HARD-016 | Must | `ScryfallService` and its database owner shall be decomposed into cohesive card-evidence, corpus, snapshot, and coordination components without changing public APIs, SQLite schema, provider requests, pacing, results, or failures. | Characterization and existing Scryfall suites pass unchanged. |
| HARD-017 | Must | Archidekt deck, folder, snapshot, HTTP, mapping, pull, push, and binding responsibilities shall be separated while centralizing authentication, pacing, retries, cooldown, and request budgets. | Characterization and existing Archidekt suites pass unchanged. |
| HARD-018 | Must | Refactoring shall not add generic manager/helper/repository layers or interfaces without multiple implementations or a demonstrated boundary need. | Architecture and abstraction audits pass. |
| HARD-019 | Must | Manual interchange lifecycle records shall reflect owner-confirmed provider cleanup and move to `completed/`; all affected links shall resolve. | Lifecycle/index/link inspection passes. |
| HARD-020 | Must | Runtime and planned surface manifests shall reconcile to the AMEND-005 matrices; numeric totals remain checks rather than compatibility targets. | Source manifest, official-client, package, and planning crosswalks agree. |
| HARD-021 | Must | No legality, format-rule, recommendation, fuzzy-resolution, new provider endpoint, prompt, resource, database, or production assembly shall enter this child. | Forbidden-surface and dependency scans pass. |
| HARD-022 | Must | Normal tests shall remain deterministic and offline with at least 90 percent line coverage per production assembly. | Full task and coverage gates pass. |

## Interfaces, States, And Modes

Capability schema 6 replaces `status` with:

```json
{
  "implementationStatus": "implemented",
  "credentialState": "not-required",
  "authenticationStatusTool": null
}
```

`credentialState` is one of `not-required`, `not-configured`, or
`configured-unverified`. Provider validation remains an explicit auth-tool
operation; capability rendering never performs it.

Identity preview is visible in every mode. Identity apply requires local-write
authority and is therefore visible in `local` and `remote`. Both belong only
to `decks`. The `default` target becomes 22/43/43 and the `all` target becomes
47/69/82 in read-only/local/remote. With the later approved statistics and
categorization surfaces, final planning targets become 32/54/54 and
57/80/93 respectively.

Per-entry reconciliation statuses are `unchanged`, `proposed`, `conflict`,
`not-found`, `not-cached`, and `unavailable`. Only `proposed` rows become deck
changes. Any non-`unchanged`/`proposed` row makes the preview incomplete.

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Honesty | Static capability metadata never claims verified provider readiness. |
| Model usability | Every tool input is described and batch alternatives are branch-specific. |
| Determinism | Same deck revision and retained evidence produce the same ordered preview and fingerprint. |
| Safety | Apply is atomic, revision guarded, evidence guarded, and partial only by explicit opt-in. |
| Maintainability | No extracted component owns more than one named provider capability family plus shared primitives. |
| Compatibility | Unchanged provider and persistence contracts pass characterization byte-for-byte. |

## Phased Delivery

| Phase | Goal | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Capability, schema, and input-description hardening. | HARD-001–006, HARD-020–022 | Schema/resource/surface tests and focused gates pass. |
| 2 | Exact identity preview and apply. | HARD-007–015, HARD-020–022 | Unit, store, MCP, package, and dummy-deck workflows pass. |
| 3 | Scryfall ownership decomposition. | HARD-016, HARD-018, HARD-022 | Characterization, full Scryfall tests, coverage, and audits pass. |
| 4 | Archidekt ownership decomposition. | HARD-017, HARD-018, HARD-022 | Characterization, full Archidekt tests, coverage, and audits pass. |
| 5 | Lifecycle, live checks, and closure. | HARD-019–022 | Docs, bounded live reads, full gates, and final audits pass. |

## Traceability

| Requirements | Design | Validation |
| --- | --- | --- |
| HARD-001–003 | SADD capability projection | HARD-FIX-001–003, unit and official-client tests |
| HARD-004–006 | SADD MCP schemas | HARD-FIX-004–006, schema lint and invalid-input tests |
| HARD-007–015 | SADD identity flow | HARD-FIX-007–020, unit/integration/E2E/package tests |
| HARD-016–018 | SADD adapter boundaries | Characterization suites, architecture and abstraction audits |
| HARD-019–022 | SADD rollout and validation | Lifecycle inspection, surface matrices, task gates, live-read record |

## Definition Of Done

- [x] AMEND-005 and this packet are independently approved.
- [x] All Must requirements pass their mapped evidence.
- [x] Runtime exposes 82 tools in remote `all`, one resource, and zero prompts.
- [x] No provider, persistence, or unchanged public result regresses.
- [x] Manual interchange is completed and all links resolve.
- [x] Full validation and audit findings are resolved.
