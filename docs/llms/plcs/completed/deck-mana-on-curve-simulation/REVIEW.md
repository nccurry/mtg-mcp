# Deck Mana and On-Curve Estimate Review

## Review Record

- Reviewer: Independent sub-agent
- Date: 2026-09-10
- Verdict: Minor revisions needed
- Result: All high- and medium-impact findings were fixed before Phase 1.

## Findings And Resolutions

| Priority | Finding | Resolution |
| --- | --- | --- |
| P1 | Missing, null, and empty `produced_mana` values would have become one empty list. | The design now requires a closed missing/null/values source-fact union. A missing or null value on a single-faced land returns unavailable; an explicit empty list stays visible as known-empty. |
| P1 | A future default Scryfall read could download data or write the local cache. | The App path now requires one cache-only exact lookup and maps missing local facts to unavailable. Tests cover no HTTP request and no cache write in every operation mode. |
| P1 | The fingerprint omitted full deck information, so different decks could share one value. | The fingerprint now includes a canonical full mainboard, deck and target identities, quantities, resolved facts, rules, model, policy, and turn setting. It excludes only seed and sample count. |
| P1 | A JSON number cannot safely carry every 64-bit seed through all MCP clients. | The public seed is now exactly 16 hexadecimal characters, accepted case-insensitively and returned in lower case. |
| P1 | A 500-entry request exceeded the current 150-item exact Scryfall lookup limit. | Version 1 now accepts at most 150 distinct mainboard entries and requires an exact printing identity for every entry. |
| P2 | A multi-face card made land identification unclear. | Version 1 models only single-faced lands. Multi-face cards remain visible as outside the model and cannot receive a land rule. |
| P2 | The miss labels had a priority list but no exact meanings. | The design now defines each label from the final modeled state, in a fixed priority order, and requires one ordered-hand test per label. |

## Review Conclusion

The project boundary remains sound: OnCurve stays a small calculation project,
App joins saved decks to local source facts, Scryfall owns provider data, and
Statistics remains exact. The revised packet is ready for Phase 1.

## Phase 1 Implementation Review

- Reviewer: Code-quality, boundary, and reliability review.
- Date: 2026-09-10.
- Verdict: Findings fixed; Phase 1 is complete.

| Priority | Finding | Resolution |
| --- | --- | --- |
| P1 | The pure request kept source values only for candidate lands. Other mainboard states could be lost before a later fingerprint or report. | Every mainboard entry now carries its direct missing/null/values state. Candidate entry IDs and caller rules remain separate. |
| P2 | Adding a very large positive quantity could overflow the running total and bypass the 500-card bound. | The validator checks the remaining capacity before adding a quantity. A regression case uses `int.MaxValue`. |
| P2 | Malformed stored JSON for a listed source value could fail with a null-reference exception. | The custom JSON reader now checks for an array of strings and returns a JSON error for malformed input. |
| P3 | App had friend access before it used the new internal project. | Phase 1 grants access only to its test project. Phase 3 adds App access with the real composition work. |

## Final Implementation Review

- Reviewer: Architecture, correctness, test, performance, plain-language, and
  simplification review.
- Date: 2026-09-10.
- Scope: The OnCurve project, Scryfall source field, App composition and MCP
  schema, tests, Task commands, and public documentation.
- Verdict: Passed after small fixes. No high- or medium-impact finding remains.

| Priority | Finding | Resolution |
| --- | --- | --- |
| P2 | The replay fingerprint did not explicitly separate candidate-land selection from the rest of the input. Two calculations could have different source candidates without an equally precise replay identity. | The fingerprint now uses named, counted sections for the sorted mainboard, candidate list, and rules. A regression test changes only candidate selection and requires a different fingerprint. |
| P2 | A missing land rule returned a general count error, although the fixture promised a caller could identify the missing entry. | The preparation step now lists missing eligible entry IDs in stable order. A focused App test checks that result. |
| P2 | The end-to-end success path ran only in local mode. | A seeded-data process test now runs the read-only estimate in read-only, local, and remote modes and confirms the saved deck revision does not change. |
| P2 | Product documents still described sampled estimates as future work and omitted one installed project from the architecture table. | The north star, design goals, rewrite guide, architecture, README, and inventories now describe the one bounded estimate accurately. |

## Final Audit Notes

- Ownership is clear: OnCurve contains only pure calculation code and references
  Core only. App owns saved-deck reads, Scryfall reads, input preparation, MCP
  registration, and result mapping.
- The calculation remains small and direct. It adds no generic simulator,
  rules engine, provider framework, tag system, or card-text interpreter.
- Tests cover exact input checks, replay, ordered-hand rules, source states,
  cache-only behavior, cancellation, MCP schema, all operation modes, and the
  named benchmark. The normal suite stays offline and excludes the benchmark.
- The final validation and performance baseline are recorded in
  [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md#phase-4-completion-record).
