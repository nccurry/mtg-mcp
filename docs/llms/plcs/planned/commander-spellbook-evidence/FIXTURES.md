# Commander Spellbook Fixtures and Acceptance Cases

## Contract Fixture

| ID | Type | Proposed location | Purpose | Update rule |
| --- | --- | --- | --- | --- |
| CSB-FIX-001 | Public OpenAPI JSON | `src/MtgMcp.Spellbook/Fixtures/OpenApi/api-6.3.3.json` | Pins the routes and request shapes used by this adapter. | Fetch from the official schema, record date, byte count, SHA-256, and route inventory together. |

The fixture must include or describe these routes:

```text
GET  /variants/
GET  /variants/{id}/
POST /find-my-combos
```

## Provider Response Fixtures

| ID | Scenario | Expected result |
| --- | --- | --- |
| CSB-FIX-002 | Variant search with quotes, whitespace, and parentheses. | The raw query characters are preserved semantically, URL-encoded once, and sent with page controls; source and unknown fields survive. |
| CSB-FIX-003 | Variant get for one source ID. | The source path is escaped and the full JSON returns. |
| CSB-FIX-004 | Deck combo lookup with every source result group. | Group names such as `included`, `includedByChangingCommanders`, and `almostIncluded` remain source-owned. |
| CSB-FIX-005 | Source 400 response. | The adapter returns invalid input without the body. |
| CSB-FIX-006 | Source 404 response. | The adapter returns not found without the body. |
| CSB-FIX-007 | Source 429 with `Retry-After`. | The adapter records a cooldown, sends no retry, and returns unavailable. |
| CSB-FIX-008 | Source 5xx or network error. | The adapter returns unavailable after one request. |
| CSB-FIX-009 | Malformed JSON. | The adapter returns unsupported. |
| CSB-FIX-010 | JSON above 2 MiB. | The adapter returns unavailable before it parses an unbounded value. |

## Cache and Pacing Cases

| ID | Scenario | Expected result |
| --- | --- | --- |
| CSB-FIX-011 | The first successful request. | The cache stores a hash, response JSON, source API version, contract checksum, response checksum, and retrieval time. |
| CSB-FIX-012 | The same request inside 15 minutes. | The result uses the cache and sends no HTTP request. |
| CSB-FIX-013 | The same request after 15 minutes. | The old row is removed and one new provider request occurs. |
| CSB-FIX-014 | Read the cache database after a deck lookup. | No raw card name, query text, deck ID, or local path appears in the cache key columns. |
| CSB-FIX-015 | Two independently constructed pacer/database owners share one temporary data root. | Immediate transactions reserve starts at least one second apart. |
| CSB-FIX-016 | A source cooldown is active, including after an earlier reservation. | The next request returns unavailable before HTTP and does not wait. |
| CSB-FIX-028 | The checked-in contract checksum changes. | The old cache row misses and is never reported as current. |

## Saved Deck Cases

| ID | Scenario | Expected result |
| --- | --- | --- |
| CSB-FIX-017 | A deck has commander and main rows. | The source request separates those arrays and preserves quantities. |
| CSB-FIX-018 | A deck repeats a card in two printing rows. | The source request sends one row with the summed quantity. |
| CSB-FIX-019 | A deck has sideboard and maybeboard rows. | The tool skips them and lists them in local selection evidence. |
| CSB-FIX-020 | A deck revision changes after the caller reads it. | The tool returns a revision conflict before HTTP. |
| CSB-FIX-021 | A deck has no main or commander rows. | The tool returns invalid input before HTTP. |
| CSB-FIX-022 | A deck has more source rows than the API allows. | The tool returns invalid input without truncation. |

## MCP Cases

| ID | Scenario | Expected result |
| --- | --- | --- |
| CSB-FIX-023 | The default profile starts. | No Spellbook tool appears. |
| CSB-FIX-024 | The all profile starts in each mode. | Exactly three Spellbook read tools appear. |
| CSB-FIX-025 | The explicit `spellbook` profile starts in each mode. | Exactly three tools appear and no write authority appears. |
| CSB-FIX-026 | Read the capability resource. | The toolset says `not-required` for credentials and lists the cache schema. |
| CSB-FIX-027 | Run the live test. | One bounded public read runs only with `Category=Live`. |
| CSB-FIX-029 | Start a separate MCP server after a seed callback writes a valid Spellbook cache row. | The child returns `cached` for a saved deck and performs no network request. |

## Fixture Rules

- Use source JSON from the official API or OpenAPI document.
- Remove no unknown source fields from a success fixture.
- Do not put private deck lists, credentials, or local paths in a fixture.
- Keep normal tests offline.
- Recheck the official source guidance before a fixture refresh.
- Do not use the full variant export in a normal fixture or test.
