# Commander Spellbook Evidence Requirements

## Document Control

- Lifecycle status: In progress
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related design: [SADD.md](SADD.md)
- Related plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: Yes

## Purpose

Give an LLM reliable Commander Spellbook information while leaving deckbuilding
decisions to the LLM and player. The provider output must keep its source,
time, limits, and cache state.

## References

- [North star](../../../../north-star.md) and
  [design goals](../../../../design-goals.md).
- [Provider adapter rules](../../../../adapters.md).
- [Commander Spellbook API root](https://backend.commanderspellbook.com/).
- [Commander Spellbook variants API](https://backend.commanderspellbook.com/variants).
- [Commander Spellbook query guide](https://commanderspellbook.com/syntax-guide/).
- [Commander Spellbook OpenAPI schema](https://backend.commanderspellbook.com/schema/).

## Use Cases

| ID | Actor and trigger | Expected result |
| --- | --- | --- |
| CSB-CASE-001 | An LLM searches a named card or result. | It receives one bounded Commander Spellbook variant page and source metadata. |
| CSB-CASE-002 | An LLM has an exact variant ID. | It receives the source's full variant object without local ranking. |
| CSB-CASE-003 | An LLM has a saved deck revision. | It receives the source's complete combo-group object without a recommendation. |
| CSB-CASE-004 | The same request repeats soon after a successful request. | It receives the cached result with its original retrieval time and cache state. |
| CSB-CASE-005 | The source rejects, throttles, or cannot serve a request. | It receives a typed, safe result without a made-up replacement. |

## Scope

### In scope

- The `spellbook` opt-in toolset and its three read-only tools.
- Official REST routes `/variants/`, `/variants/{id}/`, and `/find-my-combos`.
- Raw source query text sent without local rewriting.
- A local, exact-response SQLite cache with a 15-minute default freshness time.
- A fixed source origin, honest `User-Agent`, conservative pacing, and 429
  handling.
- Sanitized fake-HTTP fixtures, one read-only live check, and full MCP surface
  tests.

### Out of scope

- Suggestions, rankings, deck power labels, bracket estimates, or card changes.
- The `/estimate-bracket` endpoint.
- The bulk JSON export, bulk paging, a local copy of all variants, or scraping.
- A generic provider framework or shared provider cache.
- Direct Archidekt lookup from the provider adapter.
- Ad-hoc text or card-list input in the first slice.
- Local format, color identity, or Commander legality decisions.
- Authentication, user accounts, provider writes, or background refresh.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| CSB-001 | Must | The server must expose a stable opt-in `spellbook` toolset. | `default` remains unchanged. `all` and explicit `spellbook` include exactly three read tools in every mode. |
| CSB-002 | Must | `spellbook_variant_search` must send a non-blank source query without local parsing or rewriting. | A fake request proves a query with quotes, whitespace, and parentheses is encoded once without changing its source characters. Omitted page controls become and send `limit=20`, `offset=0`, and `groupByCombo=true`; a whitespace-only query stops before HTTP. |
| CSB-003 | Must | `spellbook_variant_get` must use only an exact provider variant ID. | A fake request uses `/variants/{id}/` at the fixed origin. |
| CSB-004 | Must | `spellbook_deck_combos_find` must read one saved deck revision and send only its `main` and `commander` entries. | Tests show revision conflicts stop before HTTP, omitted page controls use the same explicit defaults, and skipped zones appear in a local record of what the tool sent. |
| CSB-005 | Must | The tools must preserve Commander Spellbook's response fields and group names. | Fixture tests retain unknown fields, the paginated wrapper, identity, and all six v6.3.3 combo groups: `included`, `includedByChangingCommanders`, `almostIncluded`, `almostIncludedByAddingColors`, `almostIncludedByChangingCommanders`, and `almostIncludedByAddingColorsAndChangingCommanders`. |
| CSB-006 | Must | Every successful result must name the source, endpoint, request values sent, retrieval time, cache state, checksum, source credit URL, and limitations. | Tool and serialization tests assert the response format, that request facts are not cached, and that sent deck entries are not claimed to be recognized by Commander Spellbook. |
| CSB-007 | Must | The adapter must use only the documented public origin and send a product/version `User-Agent`. | Fake-HTTP tests assert the fixed URI and header. |
| CSB-008 | Must | One tool call must make no more than one upstream request. | Tests cover cache miss, cache hit, error, and source pagination. |
| CSB-009 | Must | The adapter must keep provider request starts at least one second apart across local processes. | Two independently constructed pacer/database owners share one temporary database and prove atomic one-second reservations and a 60-per-minute ceiling. |
| CSB-010 | Must | A `429` must set a bounded cooldown and return an unavailable result without an automatic retry. | A fake `Retry-After` test records the cooldown and one request. |
| CSB-011 | Must | The cache must store only a request hash, response data, checksums, and times. | Database tests show no separate raw deck request or query columns. The cache documentation states that lossless provider responses can contain a source paging link with the original query. A changed contract checksum does not reuse an old response. |
| CSB-012 | Must | The cache must use a 15-minute default and remove expired rows during normal cache access. | Clock-controlled tests cover fresh, expired, and cleanup cases. |
| CSB-013 | Must | Provider errors must become typed safe results. | Tests cover 400, 404, 429, 5xx, network errors, the fixed transport timeout, invalid JSON, large responses, and caller cancellation. |
| CSB-014 | Must | Normal tests must be offline. | Provider tests use fixtures and fake HTTP. The live test has `Category=Live`. |
| CSB-015 | Must | The phase must keep the decision boundary explicit. | Tool descriptions state that Commander Spellbook provides source evidence and that the caller decides what to do. |

## Public Interface Limits

The provider tools accept these bounded inputs:

| Tool | Required input | Optional input | Bound |
| --- | --- | --- | --- |
| `spellbook_variant_search` | `sourceQuery` | `limit`, `offset`, `groupByCombo` | Query: 1–512 non-blank characters. Limit: 1–25, default 20. Offset: 0–1,000, default 0. `groupByCombo`: default true. |
| `spellbook_variant_get` | `variantId` | None | ID: 1–256 characters. |
| `spellbook_deck_combos_find` | `deckId`, `expectedRevision` | `sourceQuery`, `limit`, `offset`, `groupByCombo` | The same page bounds and defaults. Source input: at most 600 main rows and 12 commander rows; each sent card name is at most 256 characters. |

`sourceQuery` is a Commander Spellbook query. The MCP does not parse, repair,
or combine it. The deck tool passes an omitted query as an omitted provider
query. It does not infer one from the local deck format.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Provider care | A client asks for a page of results. | The adapter makes one request, uses a clear header, and starts no more than 60 requests per minute. |
| Privacy | A deck lookup uses the local cache. | Cache metadata stores a request hash, not a separate card-list or query column. The documented lossless source response can still contain card data and a paging link with the source query. |
| Truthfulness | The source returns a qualitative field or group. | The server labels it as Commander Spellbook data and does not turn it into local advice. |
| Bounded output | A source response is large. | The adapter rejects a response above the configured local response limit. |
| Testability | A provider endpoint fails. | A fake HTTP test proves the typed result without network access. |
| Maintainability | The source changes one payload field. | The adapter keeps unknown JSON fields and uses one source fixture review. |

## Traceability

| Requirement | Design | Validation |
| --- | --- | --- |
| CSB-001 | [MCP surface](SADD.md#mcp-surface) | App, architecture, and E2E tests |
| CSB-002 to CSB-005 | [Request design](SADD.md#request-design) | Fake-HTTP and deck resolver tests |
| CSB-006 | [Source evidence response](SADD.md#source-evidence-response) | Serialization and MCP tool tests |
| CSB-007 to CSB-010 | [Provider stewardship](SADD.md#provider-stewardship) | Transport and pacing tests |
| CSB-011 to CSB-012 | [Cache design](SADD.md#cache-design) | SQLite and clock tests |
| CSB-013 to CSB-014 | [Failure handling](SADD.md#failure-handling) | Fixture-backed adapter tests |
| CSB-015 | [Decision boundary](SADD.md#decision-boundary) | Tool description and E2E tests |

## Definition of Done

- [x] The owner approved the saved-deck-only first interface on 2026-09-07.
- [ ] All Must requirements have passing test evidence.
- [ ] The provider adds no recommendation, ranking, estimate, or write surface.
- [ ] Normal tests remain offline and each production assembly remains at least
      90 percent line covered.
- [ ] The source API and request guidance are rechecked before the live test.
- [ ] `task ci` and the package smoke path pass.
