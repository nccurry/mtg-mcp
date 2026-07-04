# Scryfall Corpus And Evidence Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are the official All Cards, Rulings, Oracle Tags, and Art Tags bulk
datasets; official search, named/ID lookup, collection, prints, rulings, sets,
catalogs, autocomplete, and bulk metadata reads; immutable request snapshots;
and one shared SQLite store. Images, random cards, local Scryfall query syntax,
category inference, background downloads, and strategic recommendations are
out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| SCRY-001 | Must | The corpus sync shall use the official All Cards, Rulings, Oracle Tags, and Art Tags metadata and compressed JSONL downloads. | Dated contract fixtures and fake-download tests cover all four fixed datasets. |
| SCRY-002 | Must | Bulk imports shall stream into staging storage with bounded memory, validate before activation, and never expose partial rows. | Large synthetic JSONL, cancellation, corruption, and interruption tests leave the active generation unchanged. |
| SCRY-003 | Must | Card, ruling, and tag evidence shall use one versioned `scryfall.db`; no separate community-tag store, adapter, or unsupported website transport is introduced. | Architecture and forbidden-marker tests pass. |
| SCRY-004 | Must | The store shall retain exactly the active and immediately previous complete corpus generations and support guarded rollback. | Third-generation fixtures prune the oldest only after successful activation; rollback swaps validated generations. |
| SCRY-005 | Must | Corpus synchronization shall occur only through `scryfall_corpus_sync`; no ordinary read, startup path, timer, or background task may initiate it. | Network/download spies remain zero outside explicit sync. |
| SCRY-006 | Must | Provider request reuse shall use one configurable 24-hour default TTL; TTL expiry changes eligibility but never deletes evidence. | Fake-clock boundary tests cover just-before, exact, and just-after expiry. |
| SCRY-007 | Must | Provider operations shall accept `default`, `cache-only`, or `refresh`; stale fallback after provider failure shall never be silent. In `read-only`, eligible stored evidence may be read, but a miss or `refresh` that would require acquisition shall return an explicit local-write-required state with zero HTTP and SQLite writes. | Policy/mode tests prove network and write behavior and expose any stale candidate snapshot ID. |
| SCRY-008 | Must | Exact canonical request fingerprints may be reused, but every uncached arbitrary Scryfall query shall execute against Scryfall. | Query-spy fixtures prove no local query approximation or membership merge. |
| SCRY-009 | Must | Card ID, exact name, set/collector, prints, and ruling operations shall consult eligible local evidence before the provider. | Active-corpus fixtures complete with zero HTTP. |
| SCRY-010 | Must | Collection lookup shall partition eligible local hits from misses and send only misses within the pinned provider limit. | Mixed-hit, duplicate, ordering, error, and 75/76 boundary fixtures pass. |
| SCRY-011 | Must | Every completed provider request in `local` or `remote` shall follow provider pagination to completion and create an immutable content-addressed snapshot with the exact request, every raw page, result order, retrieval metadata, checksum, and lineage. No completed snapshot is published when a later page fails, and `read-only` never starts an acquisition it cannot persist. | Multi-page, later-page failure, refresh-lineage, and read-only zero-write fixtures pass while predecessor bytes remain unchanged. |
| SCRY-012 | Must | Snapshot reads shall use opaque checksum-bound ordinal cursors, default page size 25, maximum 100, and maximum 25 raw source objects per page. | Cursor tamper, boundary, canonical order, and replay tests pass. |
| SCRY-013 | Must | Raw provider fields and unknown extensions shall survive storage while normalized root/face projections preserve absent versus known-empty groups. | Single-face, split, transform, modal, and extension-field round trips pass. |
| SCRY-014 | Must | Oracle tags shall join by Oracle ID and art tags by illustration ID while remaining community evidence separate from card facts. | Join fixtures expose distinct source descriptors and never embed tags as oracle truth. |
| SCRY-015 | Must | Direct tag assignments and deterministically traversed ancestor matches shall remain distinguishable with tag type, weight, annotation, hierarchy path, and corpus generation. | Cycle, dangling-reference, direct-only, and inherited fixtures pass. |
| SCRY-016 | Must | Separate write-authorized processes sharing one data root shall coordinate leases, duplicate request ownership, and provider start pacing through SQLite. Read-only processes shall not acquire or mutate those leases. | Multi-process fake-clock tests prove one owner, crash expiry, bounded waiting, global pacing, and read-only zero writes. |
| SCRY-017 | Must | Official API request starts shall be coordinated across processes at no faster than one per 125 milliseconds, use honest User-Agent/Accept headers, stop immediately on 403/429, and retry transient transport/5xx failures at most twice. | Captured HTTP, fake-clock, and request-count fixtures pass. |
| SCRY-018 | Must | Corpus status shall be network-free and report installed datasets, active/previous versions, integrity, bytes, retrieval/check times, age, and refresh eligibility without paths or secrets. | Schema snapshots and redaction tests pass. |
| SCRY-019 | Must | Corpus rollback/delete and snapshot delete shall use optimistic identity checks and explicit loss acknowledgement. | Stale-generation and missing-acknowledgement fixtures perform no mutation. |
| SCRY-020 | Must | Failed sync, query, cancellation, size limit, or checksum validation shall retain bounded diagnostics and never mark partial evidence complete. | Failure and recovery tests pass without dangling staging rows. |
| SCRY-021 | Must | All eighteen tools shall belong only to the default-enabled `scryfall` toolset; fourteen reads are visible in every mode and four explicit local mutations only in `local`/`remote`. A read tool's acquisition path shall still enforce local-write authority. | Exact toolset/mode/schema discovery matrices and invocation-time write guards pass. |
| SCRY-022 | Must | Random-card access, background sync, generic action routers, category mapping, and local Scryfall syntax shall not be exposed. | Surface and forbidden-marker tests pass. |
| SCRY-023 | Must | Price/rank fields shall retain provider, currency/context, retrieval time, and stale status and never be named quality or recommendation scores. | Output schema and 24-hour price fixtures pass. |
| SCRY-024 | Must | Lossless raw objects and normalized identity/index fields shall permit later additive query-engine migrations without a speculative evaluator abstraction in this child. | Architecture review and migration fixture prove extension without transport/Core leakage. |
| SCRY-025 | Must | Normal tests shall remain deterministic and offline with at least 90 percent line coverage per production assembly. | Full repository quality gates pass. |
| SCRY-026 | Must | Before child acceptance, an opt-in manual workflow shall install the current real four-dataset corpus, validate activation and second-process reuse, and clean up only with explicit consent; ordinary CI shall never perform the download. | Dated redacted acceptance evidence records provider versions, aggregate counts/hashes, reuse, and cleanup without local paths or raw payloads. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Repeatability | Snapshot replay and ordering are byte-stable. |
| Provider safety | Cross-process pacing and stop-on-block are proven. |
| Fidelity | Raw objects, faces, tags, and unknown fields round-trip. |
| Boundedness | Streaming imports and paginated output remain bounded. |
| Availability | Eligible local evidence supports network-free workflows. |
| Honesty | Provider facts, community tags, freshness, misses, and failures remain distinct. |

## Traceability

| Requirements | Validation |
| --- | --- |
| SCRY-001 through SCRY-005 | Bulk contract, streaming, activation, retention, and network-spy fixtures |
| SCRY-006 through SCRY-010 | TTL, policy, authoritative-query, identity, and collection fixtures |
| SCRY-011 through SCRY-015 | Snapshot, cursor, projection, and tag-join fixtures |
| SCRY-016 through SCRY-020 | Multi-process, HTTP safety, status, guard, and recovery fixtures |
| SCRY-021 through SCRY-026 | MCP surface, forbidden behavior, evidence labels, architecture, full gates, and manual real-corpus acceptance |

## Definition Of Done

- [ ] The fixed bulk profile installs and reuses across separate MCP processes.
- [ ] All eighteen tools and exact mode surfaces pass official-client tests.
- [ ] New arbitrary raw searches remain provider-authoritative.
- [ ] Card facts and community tags share storage without semantic conflation.
- [ ] Snapshot replay, current/previous rollback, and failure recovery pass.
- [ ] Full offline, coverage, package, and installed-tool gates pass.
