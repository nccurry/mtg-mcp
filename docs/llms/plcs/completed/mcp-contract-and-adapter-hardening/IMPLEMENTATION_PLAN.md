# MCP Contract And Adapter Hardening Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-06
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Implementation Strategy

First lock the observable schemas and characterize existing provider behavior.
Then add identity reconciliation through existing deck and Scryfall operations.
Only after those behavior tests pass should provider owners be extracted. Each
phase leaves the repository green and is committed separately. Production work
begins only after AMEND-005 and this packet are approved and activated.

## Phase Summary

| Phase | Goal | Requirements | Validation | Exit criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 0 | Approve and activate the packet. | Governance gate | Packet/link/diff review | AMEND-005 accepted, authorization `Yes`, packet in `in-progress/`. | Completed |
| 1 | Harden capability and MCP schemas. | HARD-001–006, HARD-020–022 | Unit, schema, architecture, official-client | Schema 6 and closed batch input pass focused/full gates. | Completed |
| 2 | Add exact identity reconciliation. | HARD-007–015, HARD-020–022 | Unit, fake-provider, store, MCP, package, dummy deck | Both tools pass all modes and safety cases. | Completed |
| 3 | Decompose Scryfall owners. | HARD-016, HARD-018, HARD-022 | Characterization, adapter, architecture, coverage | Public/provider/persistence behavior is unchanged. | Completed |
| 4 | Decompose Archidekt owners. | HARD-017, HARD-018, HARD-022 | Characterization, adapter, architecture, coverage | Routes, pacing, mappings, and results are unchanged. | Completed |
| 5 | Reconcile lifecycle and close. | HARD-019–022 | Full gates, docs, audits, bounded live reads | No findings remain; packet and registry complete. | Completed |

## Phase Details

### Phase 0: Review gate

- Review all five packet files and AMEND-005 together.
- Record repository-owner approval and reviewed revision.
- Move only this packet to `in-progress/` and mark Phase 1 active.
- Make no production edit before this gate.

### Phase 1: Contract and schema hardening

- Replace capability availability with schema-6 implementation/credential
  fields derived without I/O.
- Replace the flat deck batch record with eleven attributed alternatives and
  an exhaustive indexed mapper.
- Add descriptions to every registered root input and union property.
- Add complete capability, schema, configuration, and leakage tests.
- Update current schema documentation without claiming identity tools exist.

### Phase 2: Exact identity reconciliation

- Add App-owned reconciliation models, canonical hashing/token logic, and the
  deck/Scryfall coordinator.
- Register preview/read and apply/local-write tools under `decks`.
- Reuse collection acquisition/pagination/pacing and the existing deck batch
  transaction; add only the minimum retained-evidence query needed by apply.
- Exercise a dummy Commander deck containing printing-bound, Oracle-bound,
  exact-name, duplicate, conflicting, and missing entries.
- Update runtime/package manifests from 80 to 82 tools only when the tools land.

### Phase 3: Scryfall decomposition

- Freeze request, SQL, cursor, checksum, pacing, result, and failure
  characterization before moving methods.
- Extract corpus, snapshot, and coordination stores around one concrete
  database owner.
- Extract card-evidence, corpus, and snapshot operations behind the existing
  public façade.
- Remove superseded methods and duplicated helpers as each extraction lands.

### Phase 4: Archidekt decomposition

- Freeze deck/folder/snapshot request and mapping characterization first.
- Centralize all HTTP/auth/pacing/retry/budget behavior in one transport owner.
- Extract family transports, mappers, and operations.
- Extract pull, push, and binding workflows behind the current coordinator.
- Do not alter provider routes, request bodies, safety checks, or fingerprints.

### Phase 5: Closure

- Confirm manual-interchange lifecycle and every dependency link.
- Synchronize README, changelog, architecture, rewrite guide, `llms.txt`,
  capability docs, live manifest, umbrella, and cutover planning.
- Run bounded Scryfall and Archidekt read-only live checks after all refactors.
- Run all supported task gates and applicable audits; fix findings and rerun.
- Move the child to `completed/` only after evidence is recorded.

## Validation Commands

- `task lint`
- `task test`
- `task surface:report`
- `task coverage`
- `task pack`
- `task smoke:process`
- `task smoke:mcp`
- `task release:tool-smoke`
- NuGet vulnerable, deprecated, and outdated dependency checks
- Markdown relative-link validation and `git diff --check`
- Abstraction-quality, code-quality, visual-code, dead-code, dependency,
  test-coverage, test-quality, and docs-sync audits

## Cross-Phase Risks

| Risk | Phases | Mitigation |
| --- | --- | --- |
| Schema replacement accidentally keeps the flat contract. | 1 | Assert branch properties and forbidden unrelated fields through the official client. |
| Preview applies evidence different from what the caller reviewed. | 2 | Bind revision, ordered outcomes, evidence, version, fingerprint, and token. |
| Provider decomposition changes pacing or retry order. | 3–4 | Centralize safety owners and retain fake-clock/request-sequence characterization. |
| Current and planned counts are conflated while implementation is partial. | 1–5 | Label runtime versus target counts and update runtime manifests only when tools register. |

## Completion Criteria

- [x] Every Must requirement appears in a phase and fixture.
- [x] Each phase has focused and full validation.
- [x] The two new tools are the only public-surface additions.
- [x] Existing provider and persistence behavior remains characterized.
- [x] No generic abstraction or legality logic enters the implementation.
- [x] Documentation and lifecycle state match the final checkout.
