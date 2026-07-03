# Scryfall Tagger Cache Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are exact cached tag definitions/assignments, cache snapshots,
explicit card/deck refresh, HTML CSRF/session bootstrap, unsupported GraphQL,
bounded printing fallback, provenance, and circuit breaking. Bulk crawling,
background refresh, category mapping, heuristic tags, image analysis or
locally inferred illustration tags, and live Scryfall lookup are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| TAG-001 | Must | Cache reads shall use only `tagger.db` and issue zero HTTP requests. | Network-spy tests pass. |
| TAG-002 | Must | Missing Oracle IDs shall return `not_cached`, distinct from a completed card with zero tags. | Empty/missing fixtures pass. |
| TAG-003 | Must | Each completed card snapshot shall preserve Oracle ID, queried printing and illustration identity when known, Tagger subject IDs, retrieval time, source URL, acquisition version, checksum, and status. | SQLite round-trip tests pass. |
| TAG-004 | Must | Assignments shall preserve Tagger tag ID/slug/name/type, direct/ancestor relation, returned status, subject ID/scope, and raw extension data. Public refresh shall use `moderatorView=false` and shall not claim rejected/pending completeness. | Rich public GraphQL fixture round trips. |
| TAG-005 | Must | Refresh shall accept explicit Oracle IDs or one explicit local deck and a required Scryfall printing snapshot ID. | Request schema and dependency tests pass. |
| TAG-006 | Must | Refresh shall deduplicate Oracle IDs and process at most 100 per invocation. | Boundary tests prove 101 is rejected before HTTP. |
| TAG-007 | Must | Printing candidates shall be paper printings from the supplied Scryfall snapshot, ordered preferred deck printing first then newest release/set/collector, with at most five attempts per Oracle ID. | Ordering and fallback fixtures pass. |
| TAG-008 | Must | Acquisition shall bootstrap CSRF/session state from HTML and POST only the observed public `FetchCard` GraphQL query with the same cookie session and `moderatorView=false`. | Fake HTML/cookie/GraphQL tests pass and mutation-operation spies remain zero. |
| TAG-009 | Must | Provider requests shall be globally serialized with at least one second between starts. | Fake-clock concurrency tests pass. |
| TAG-010 | Must | No HTTP request shall be automatically retried. | Failure fixtures show one attempt per candidate. |
| TAG-011 | Must | 403 or 429 shall stop the invocation immediately and disable further refresh in the process. | Circuit-breaker request counts pass. |
| TAG-012 | Must | Contract drift, missing CSRF, or GraphQL schema mismatch shall return unsupported and preserve prior cache snapshots. | Drift fixtures pass. |
| TAG-013 | Must | A refresh shall insert a new immutable card snapshot; it shall never overwrite prior evidence. | Lineage/checksum tests pass. |
| TAG-014 | Must | Reads shall accept optional snapshot ID and otherwise return latest completed snapshot with its ID. | Replay fixtures pass before/after refresh. |
| TAG-015 | Must | Tag output shall never assign deck categories, semantic roles, or inferred tags. | Surface/architecture tests find no classifier dependency. |
| TAG-016 | Must | Read tools shall be visible in all modes; refresh shall require `local` or `remote`. | Mode tests pass. |
| TAG-017 | Must | Cache status shall expose counts, oldest/newest retrieval, contract version, and circuit state without cookies, tokens, or local paths. | Redaction/schema tests pass. |
| TAG-018 | Must | Normal tests shall be offline; optional live test shall refresh at most one known Oracle ID. | Test discovery and cap guard pass. |
| TAG-019 | Must | One refresh invocation shall stop before starting request 121 or after two minutes elapsed, whichever occurs first, while retaining completed immutable card snapshots and reporting not-attempted Oracle IDs explicitly. | Fake-clock/request-count tests prove a worst-case bound and no hidden continuation. |
| TAG-020 | Must | HTML parsing may use pinned `AngleSharp` only in `MtgMcp.Tagger`; the dependency shall be centrally versioned, reviewed for license/security/maintenance, and forbidden from Core. | Package, architecture, and dependency-policy tests pass. |
| TAG-021 | Must | Unsupported acquisition shall not be implemented or enabled until the repository owner accepts the documented provider-risk record after rechecking robots, terms, and observed contract behavior. | Approval record names accepter, date, revision, and reviewed policy evidence. |
| TAG-022 | Must | Requests shall use an honest product user-agent with a project/contact URL and appropriate `Accept` headers; the adapter shall not impersonate a browser or a disallowed crawler identity. | Captured-request fixtures pass. |
| TAG-023 | Must | Refresh shall skip Oracle IDs that already have a completed snapshot unless the caller explicitly sets `forceRefresh=true`; skipped cached IDs shall be reported and shall consume no provider request. | Default/forced mixed-cache request-count fixtures pass. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Determinism | Snapshot-ID reads are byte-stable and network-free. |
| Provider safety | One request/second, no retries, 100-card and five-print bounds, stop-on-block. |
| Fidelity | Direct/inherited/type/status/subject-scope/raw association evidence preserved. |
| Honesty | Unsupported transport and missing cache states are explicit. |
| Modularity | Tagger owns HTML/GraphQL/SQLite; Core contains no transport knowledge. |

## Definition Of Done

- [ ] Cache and acquisition contracts pass offline fixtures.
- [ ] Circuit breaker and request bounds are proven.
- [ ] No category classifier or background crawler exists.
- [ ] Policy/contract notice is visible in tool descriptions and docs.
