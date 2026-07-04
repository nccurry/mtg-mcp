# Rewrite Stabilization And 0.9.0 Cutover Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are cross-module acceptance, exact MCP-schema verification, offline and
opt-in live proof, packaging, versioning, documentation, merge/release gates,
rollback, and PLC lifecycle closure. No new product behavior is in scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| CUT-001 | Must | All ten prerequisite child PLCs shall be approved, implemented, accepted, and moved to `completed/` before cutover implementation begins. | Dependency ledger contains reviewed revisions and completion evidence. |
| CUT-002 | Must | The production solution shall contain only `MtgMcp.Core`, `MtgMcp.App`, `MtgMcp.Decks`, `MtgMcp.Scryfall`, `MtgMcp.Archidekt`, `MtgMcp.Playgroup`, and `MtgMcp.Statistics`; manual interchange and deterministic categorization remain part of `MtgMcp.Decks`. | Solution/project reference architecture test passes. |
| CUT-003 | Must | The stable MCP surface shall match the canonical manifest derived from the completed, approved capability children, expose one capability resource, and expose zero prompts. The proposed AMEND-004 remote `all` planning baseline is 91 tools, but no count is preserved solely for compatibility. | Approved schema snapshot matches the derived manifest byte-for-byte after canonicalization. |
| CUT-004 | Must | Per-mode visibility shall match every approved tool's operation-mode requirement in that manifest. The proposed AMEND-004 `all` planning baseline is 56 tools in `read-only`, 78 in `local`, and 91 in `remote`; approved design changes recalculate those totals. | Per-mode discovery fixtures and manifest reconciliation pass. |
| CUT-005 | Must | Stable releases shall contain no legacy advisor, intent, plan, recommendation, blended-score, simulation, Moxfield-network, CommanderSpellbook, or decklist-provider surface or assembly. | Forbidden-name and project scans return no matches outside explicit historical docs/fixtures. |
| CUT-006 | Must | Every production assembly shall maintain at least 90 percent line coverage without unjustified exclusions. | Per-assembly coverage report passes the existing gate. |
| CUT-007 | Must | The final release candidate shall pass repository lint, offline tests, coverage, package, and packaged-server smoke commands. | Final evidence bundle records successful supported task commands. |
| CUT-008 | Must | Operation-mode enforcement, secret redaction, cancellation, provider pacing, structured failures, and dependency-direction tests shall pass together. | Cross-module security and architecture suite passes. |
| CUT-009 | Must | Opt-in Scryfall proof shall verify official bulk metadata and a bounded official API read without remote mutation. A manual full-corpus synchronization acceptance shall verify All Cards, Rulings, Oracle Tags, and Art Tags, generation activation, and second-process reuse; packaged fixture acceptance separately verifies guarded rollback. Neither runs as an ordinary provider dependency in CI. Only a temporary official read-proof skip may be owner-approved under the waiver record. | Dated redacted live report, manual corpus-acceptance record, and packaged rollback fixture pass, or the narrowly allowed read skip is recorded without being labeled passed. |
| CUT-010 | Must | The Archidekt live workflow shall create a private throwaway folder and deck; exercise push/read/pull, folder update/move, and snapshot create/update/get/restore/delete; then move the deck to root and verify folder and deck deletion in `finally`. Inability to verify cleanup or any residual folder, snapshot, or deck shall block cutover. | Live evidence proves complete cleanup and records no credential, URL, or stable remote identifier. |
| CUT-011 | Must | Playgroup live proof shall cover safe documented reads when credentials are available. For the pinned 2026-07-03 contract, both writes shall remain explicitly fixture-only under the child owner decision because no documented cleanup exists; they shall not be labeled live-tested or passed. | Live report distinguishes exercised reads from fixture-only writes, unsupported operations, and failures. |
| CUT-012 | Must | `decks.db` and the unified `scryfall.db` shall remain independent and versioned; release installation shall not discover, alter, import, or delete legacy stores automatically. | Fresh/legacy-side-by-side smoke fixtures pass. |
| CUT-013 | Must | User documentation shall describe the clean break, modes, tools, provider limits, data directories, backup/restore, unsupported states, and rollback. | Documentation review and link checks pass. |
| CUT-014 | Must | No unresolved priority-1 or priority-2 defect, child acceptance exception, provider-contract drift, or security finding may remain at stable release approval. | Release ledger has no open blocking item. |
| CUT-015 | Must | The rewrite branch shall integrate the latest `main` through ordinary history-preserving Git operations and rerun all final gates after conflict resolution. | Merge-base and final validation evidence are recorded. |
| CUT-016 | Must | Preview artifacts shall use `0.9.0-preview.N`; only an accepted release candidate may produce stable `0.9.0`. | Package metadata and server version tests pass. |
| CUT-017 | Must | Rollback shall reinstall the prior stable release and select its prior data/configuration without transforming `0.9.0` stores. | Rollback rehearsal passes from packaged artifacts. |
| CUT-018 | Must | Lifecycle shall remain staged: umbrella planning completes after all eleven plans are approved; children 1–10 complete after their own implementations before cutover starts; child 11 moves to `in-progress` for cutover and completes after release/rollback evidence. Cutover may add evidence links to already completed packets but shall not retroactively move them. | PLC registry, approval records, folder states, and evidence links match each milestone. |
| CUT-019 | Must | `read-only` shall be validated as a zero-local-write/zero-remote-write mode that may perform explicit provider reads; offline shall remain a test-suite classification rather than a runtime mode. | Mode E2E proves provider reads can occur and all write spies remain zero. |
| CUT-020 | Must | Any approved child tool-surface change shall update that child's matrix, regenerate the cutover family crosswalk and per-mode totals, and update canonical schema snapshots in the same reviewed change. Counts follow the approved design; they do not constrain it or require legacy aliases. | Intentional drift fixture fails until the manifest, derived counts, and snapshots agree. |
| CUT-021 | Must | Every stable tool shall belong to exactly one capability toolset. Cutover shall validate `default`, `all`, `none`, and representative explicit profiles across every operation mode, prove toolsets never widen authority, and pass the program north-star workflow gate. The proposed AMEND-004 default baseline is 31/52/52 tools by mode; the `all` baseline is 56/78/91. | Canonical profile/mode manifests, capability-resource snapshots, zero-write spies, and composed deckbuilding workflow fixtures pass. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Reproducibility | A clean checkout produces identical canonical MCP schemas and offline results. |
| Safety | No normal test uses a provider; live tests are opt-in, bounded, redacted, and cleanup-aware. |
| Recoverability | Prior stable package and data remain usable after rollback rehearsal. |
| Traceability | Every release gate links to a child requirement and dated evidence artifact. |
| Transparency | Skipped, unsupported, waived, and failed checks are distinct; none count as passed. |

## Definition Of Done

- [ ] All requirements map to passing evidence in `FIXTURES.md`.
- [ ] Exact per-mode MCP schema snapshots are approved.
- [ ] Offline, package, smoke, security, and coverage gates pass from the final commit.
- [ ] Required provider live proofs pass without leaked secrets or residual state.
- [ ] Rollback rehearsal succeeds.
- [ ] Stable release authorization and PLC lifecycle closure are recorded.
- [ ] Toolset/profile governance and every child north-star acceptance workflow pass.
