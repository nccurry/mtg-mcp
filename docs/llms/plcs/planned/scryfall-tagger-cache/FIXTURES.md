# Scryfall Tagger Cache Fixtures And Acceptance Matrix

## Acquisition Fixtures

| ID | Scenario | Expected result |
| --- | --- | --- |
| TAG-FIX-001 | HTML shell with CSRF and session cookie | GraphQL request uses same session and token. |
| TAG-FIX-002 | Rich direct oracle/illustration/printing tags | Types, ordering, and raw fields persist. |
| TAG-FIX-003 | Direct tag with ancestors | Relations remain distinct. |
| TAG-FIX-004 | Accepted and rejected assignments | Default excludes rejected; opt-in returns both with state. |
| TAG-FIX-005 | Preferred printing unknown, older known | Deterministic fallback stops at first known response. |
| TAG-FIX-006 | Five candidates unknown | Completed `not_present`, not empty success or network failure. |
| TAG-FIX-007 | Missing Scryfall printing snapshot | Unsupported dependency and zero HTTP. |
| TAG-FIX-008 | 101 unique Oracle IDs | Rejected before HTTP. |
| TAG-FIX-009 | Concurrent refreshes | Global starts remain at least one second apart. |
| TAG-FIX-010 | 403 or 429 mid-deck | Immediate stop and process circuit open. |
| TAG-FIX-011 | Missing CSRF/schema field | Unsupported; prior latest snapshot unchanged. |
| TAG-FIX-012 | Refresh changes tags | New snapshot/lineage; old snapshot byte-stable. |
| TAG-FIX-013 | 121st request would start before two minutes | Invocation stops at 120 requests; remaining Oracle IDs are not attempted. |
| TAG-FIX-014 | Two-minute deadline occurs before request cap | No next request starts; completed snapshots persist and run reports budget exhausted. |
| TAG-FIX-015 | Restart after process circuit opens | Circuit resets, refusal metadata remains, and zero request occurs until a new explicit refresh. |

## MCP Surface Matrix

| Tool | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `tagger_cache_status`, `tagger_tag_list`, `tagger_card_tags_get`, `tagger_deck_tags_get` | Visible | Visible | Visible |
| `tagger_refresh_cards`, `tagger_refresh_deck` | Hidden | Visible | Visible |

## Policy Evidence

| Source | Observed | Required interpretation |
| --- | --- | --- |
| `https://tagger.scryfall.com/robots.txt` | 2026-07-03: `User-agent: *`, `Allow: /` | Crawl directive only; not API endorsement. |
| `https://scryfall.com/docs/terms` | Reverify at implementation | Automated access must not impose undue burden. |
| Observed Tagger HTML/GraphQL | Reverify at implementation | Unsupported contract; drift fails closed. |

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| TAG-001, TAG-002 | Cache-read network spy and missing-versus-empty fixtures. |
| TAG-003, TAG-004 | TAG-FIX-002 through TAG-FIX-004 SQLite/raw-extension round trips. |
| TAG-005 | Explicit Oracle/deck request and Scryfall snapshot dependency tests. |
| TAG-006 | TAG-FIX-008 deduplication and 100/101 boundary tests. |
| TAG-007 | TAG-FIX-005 through TAG-FIX-007 ordering, fallback, and five-attempt tests. |
| TAG-008 | TAG-FIX-001 same-session HTML/CSRF/GraphQL capture. |
| TAG-009 | TAG-FIX-009 fake-clock global concurrency test. |
| TAG-010, TAG-011 | TAG-FIX-010 request-count and process-circuit tests. |
| TAG-012 | TAG-FIX-011 drift and prior-snapshot preservation tests. |
| TAG-013, TAG-014 | TAG-FIX-012 lineage, immutability, and replay tests. |
| TAG-015 | Forbidden classifier/category dependency and output-schema tests. |
| TAG-016 | MCP surface matrix and mode-guard tests. |
| TAG-017 | Cache-status schema and secret/path redaction tests. |
| TAG-018 | Offline test discovery and one-ID live cap guard. |
| TAG-019 | TAG-FIX-013 and TAG-FIX-014 fake-clock/request-count tests. |
| TAG-020 | Central package pin, license/security review record, and Core dependency prohibition. |
| TAG-021 | Owner provider-risk acceptance and dated policy/contract recheck. |

## Live Tests

One optional `Category=Live` test may refresh one well-known Oracle ID with one
preferred printing. It obeys the same pacer and circuit breaker, performs no
mutation on Tagger, records no cookies/tokens, and is never part of normal CI.
