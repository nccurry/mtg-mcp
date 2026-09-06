# Evidence-First Deckbuilding Evolution Fixtures And Acceptance Matrix

This is a planning inventory. A child creates a checked-in fixture only when it
has an approved contract and a clear owner. Do not capture credentials, cookies,
private deck data, Reddit user content, or unlicensed source payloads merely to
fill this table.

## Fixture Inventory

| ID | Type | Planned location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| EFD-FIX-001 | MCP surface inventory | Existing architecture/App/E2E tests | Lock exact tool names, resource, zero prompts, toolsets, and modes before a public change. | App | Update only with approved surface change. |
| EFD-FIX-002 | Scryfall SQLite characterization | MtgMcp.Scryfall.Tests fixtures | Lock corpus generation, tag/ruling/card rows, snapshots, leases, pacing reservation, ordering, and typed failure behavior before owner movement. | Scryfall | Freeze before Phase 1A; change only with approved behavior change. |
| EFD-FIX-003 | Archidekt fake-HTTP characterization | MtgMcp.Archidekt.Tests fixtures | Lock deck/folder/snapshot routes, request counts, auth fallback, redaction, confirmations, fingerprints, read-back, and error mapping before owner movement. | Archidekt | Freeze before Phase 1B; sanitize all payloads. |
| EFD-FIX-004 | Operation-result/error matrix | Core and adapter tests | Verify expected states map to typed outcomes and cancellation is not swallowed. | Core/adapter child | Add a row for every new public failure state. |
| EFD-FIX-005 | Provider admission record | Child PLC packet | Prove source access, terms, meaning, auth, pacing, retention, cache, fixture rights, and output label before implementation. | Provider child owner | Re-check at activation and release. |
| EFD-FIX-006 | Commander Spellbook response fixtures | New provider test project, if admitted | Cover bounded search/detail/empty/error cases and source provenance. | Spellbook child owner | Use only terms-compliant sanitized captures. |
| EFD-FIX-007 | Source-policy decision record | Child PLC packet | Record admit/defer/reject for Reddit, EDHREC-style cohorts, and other candidates. | Product owner | Re-check when terms or proposed workflow changes. |
| EFD-FIX-008 | Exact-analysis reference matrix | Statistics/deck-analysis tests | Independently verify finite-population probabilities and declared assumptions. | Statistics child owner | Add cases only for new exact behavior. |
| EFD-FIX-009 | Goldfish toy deck | Simulation-lab test project, if feasibility is approved | Prove a closed supported mechanic and a transparent trace. | Simulation child owner | Immutable after calibration baseline; version a replacement. |
| EFD-FIX-010 | Unsupported-mechanic toy deck | Simulation-lab test project, if feasibility is approved | Prove unsupported text contributes no fabricated effect and is reported. | Simulation child owner | Update only when support is deliberately added. |
| EFD-FIX-011 | Sampled replay/calibration matrix | Simulation-lab test project, if feasibility is approved | Verify same seed/input/policy replay, bounds, uncertainty, and cancellation. | Simulation child owner | Version with model/policy change. |
| EFD-FIX-012 | Performance case | Child-specific benchmark/report, if justified | Protect one named hot path with deterministic representative input. | Child owner | Record environment and why the case matters. |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| EFD-001 | EFD-FIX-001 and tool-description review | No schema/tool output makes a card/deckbuilding choice for the caller. | Surface/E2E inspection |
| EFD-002 | EFD-FIX-002 and EFD-FIX-003 | Concrete stores/domains contain the behavior; retired contexts/forwarders are absent. | Focused tests and architecture inspection |
| EFD-003 | Existing project-reference/source tests | Core, Decks, Statistics, adapters, and App keep their one-way dependencies. | Architecture suite |
| EFD-004 | EFD-FIX-004 | Success, absence, unavailable, unsupported, conflict, invalid input, and cancellation remain distinguishable and safe. | Unit/adapter failure tests |
| EFD-005 | EFD-FIX-001 | Tool names, schemas, toolsets, modes, and capability resource agree. | task surface:report and E2E |
| EFD-006 | EFD-FIX-005 | No provider code starts without a complete source-specific admission record. | PLC review |
| EFD-007 | EFD-FIX-005 and provider fixtures | Results retain source/reference/time/freshness/population and explicit unknowns. | Schema and fixture tests |
| EFD-008 | EFD-FIX-008 | Exact answers match independent formulas for declared populations. | Statistics tests |
| EFD-009 | EFD-FIX-009 through EFD-FIX-011 | A feasibility study proves or rejects the narrow model before a stable tool exists. | Calibration and review |
| EFD-010 | EFD-FIX-002 through EFD-FIX-011 | Behavior moves or source adapters remain deterministic and network-free in normal tests. | Focused tests plus task test/coverage |
| EFD-011 | EFD-FIX-012 | Performance work has a named scenario and declared review/CI budget. | Child performance report |
| EFD-012 | EFD-FIX-001 plus package/client smoke | SDK upgrade preserves or explicitly versions the contract. | Package/process/client tests |
| EFD-013 | Documentation scenario | Counts, boundaries, source limits, and status match code. | Link/render review and git diff --check |

## MCP Surface Checks

### First cleanup child

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| Existing 93 tools | Existing read-only/local/remote matrix | Unchanged | Phase 1A/1B are internal ownership refactors only. |
| mtg://server/capabilities | All existing profiles | Unchanged | Continues to describe static implementation and selected toolsets. |
| Prompts | All | None | Do not introduce an advisor prompt as part of cleanup. |

### Future child rules

| Surface type | Mode/toolset expectation | Notes |
| --- | --- | --- |
| New source evidence tool | Read-only; source-specific opt-in toolset | Name, schema, bound, provenance, and policy are child-owned. |
| Exact analysis tool | Read-only; Statistics/default only if it is a coherent small workflow | Declared inputs and exact-derivation metadata required. |
| Simulation experiment | Read-only; separate experimental opt-in toolset | No surface exists until feasibility and versioning are approved. |
| Local deck write | Local/remote as current workflow requires | OperationModeGuard and revision/fingerprint behavior stay explicit. |
| Provider write | Remote only | Requires source-specific safe mutation contract and separate approval. |

## Provider Fixtures

| Provider | Fixture | Scenario | Sanitization and policy notes |
| --- | --- | --- | --- |
| Scryfall | EFD-FIX-002 | Corpus, snapshot, tag, lease, pacing, unavailable states | Continue using official, sanitized fixture data; no background corpus download. |
| Archidekt | EFD-FIX-003 | Deck/folder/snapshot reads, writes, retries, conflicts, redaction | No real mutation in normal tests; never preserve credentials/cookies. |
| Playgroup | Existing fixture suite | Provider-shaped reads and permitted write fixtures | Retain existing provider-specific contract. |
| Commander Spellbook | EFD-FIX-006 | Search/detail/empty/rate/failure after admission | Capture only material allowed by current source terms; use short bounded output. |
| Reddit | EFD-FIX-007 first | Policy feasibility only | No post/comment payload fixture until use, storage, display, and deletion are approved. |
| EDHREC-style cohort provider | EFD-FIX-007 first | Official-contract/permission decision | No undocumented JSON endpoint or scraped response fixture. |
| Moxfield | EFD-FIX-007 | Rejected automation record | Manual user-provided interchange remains the supported workflow. |

## Calibration Or Performance Cases

| Case | Inputs | Expected metrics | Validation |
| --- | --- | --- | --- |
| Scryfall store extraction | Existing deterministic corpus/snapshot fixture | Same rows, ordering, errors, and database state before/after movement | Characterization test; no timing gate |
| Archidekt owner extraction | Existing fake HTTP deck/folder/snapshot fixtures | Same request count, payload, typed result, and redaction before/after movement | Characterization test; no timing gate |
| Exact draw analysis | 60- and 99-card finite populations; declared success quantities, hand/draw/mulligan assumptions | Matches independent hypergeometric or enumerated result exactly | Independent formula test |
| Future source query | Bounded sanitized request/page fixture | Stable ordering, provenance, output cap, and typed source error | Provider fixture test |
| Future goldfish replay | Toy deck, fixed model/policy, seed, sample count, turn cap | Same trace/fingerprint; supported/unsupported coverage visible | Feasibility test |
| Future sampled estimate | Toy deck with known outcome distribution | Reported interval method and result match fixture tolerance | Calibration test |
| Future hot path | Named deck/workload, Release, pinned settings | Review budget only after baseline is recorded | Child-specific report |
