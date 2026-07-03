# Scryfall Evidence Snapshots Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are official Scryfall search, named/ID lookup, collection, prints,
rulings, sets, catalogs, autocomplete, and bulk-data metadata. Image bytes,
random cards, local reimplementation of Scryfall search syntax, Tagger, price
recommendations, and automatic background refresh are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| SCRY-001 | Must | Acquisition shall support the listed deterministic official read families through typed request cases. | Fixture-backed contract tests cover every case. |
| SCRY-002 | Must | Search acquisition shall preserve the original query/options and fetch pages in provider order until complete. | Multi-page fixtures preserve every object and page. |
| SCRY-003 | Must | Collection acquisition shall enforce Scryfall's current identifier limit and preserve input-to-result errors. | Boundary and mixed-result fixtures pass. |
| SCRY-004 | Must | Random-card access shall not be exposed. | Surface inventory contains no random operation. |
| SCRY-005 | Must | A completed snapshot shall be immutable and content-addressed. | Attempts to update rows fail; repeated reads are byte-stable. |
| SCRY-006 | Must | Refresh shall create a new snapshot linked to its predecessor. | Old checksum remains unchanged and lineage is queryable. |
| SCRY-007 | Must | Stored pages shall preserve raw source JSON, source URL, retrieval time, ordinal, checksum, and HTTP validators when present. | Database and round-trip tests pass. |
| SCRY-008 | Must | Card results shall expose a supported typed Scryfall object with extension fields plus a normalized projection. | Unrecognized fields survive storage and output. |
| SCRY-009 | Must | Snapshot output shall distinguish complete, failed, canceled, too-large, deleted, and unavailable states. | Failure/cancellation/limit/deletion fixtures never present partial or purged rows as complete. |
| SCRY-010 | Must | Requests shall be serialized with at least 125 ms between starts and explicit User-Agent/Accept headers. | Fake-clock pacing/header tests pass. |
| SCRY-011 | Must | 403 and 429 shall stop the acquisition immediately; 429 shall record Retry-After without automatic retry. | Fake HTTP tests prove request count stops. |
| SCRY-012 | Must | Transient 5xx/network failures may retry at most twice with bounded cancellation-aware delays. | Retry/cancellation tests pass. |
| SCRY-013 | Must | One snapshot shall reject results above 20,000 objects or 256 MiB before being marked complete. | Boundary fixtures return explicit too-large status. |
| SCRY-014 | Must | Cached read tools shall never trigger network traffic or refresh. | Network-spy tests pass. |
| SCRY-015 | Must | Snapshot creation/refresh/delete shall require `local` or `remote`; cached reads shall be visible in all modes. | Surface tests pass. |
| SCRY-016 | Must | Provider errors and URLs shall be sanitized without discarding Scryfall error codes/details safe for users. | Error fixture tests pass. |
| SCRY-017 | Must | Failed, canceled, interrupted, or too-large acquisition shall retain only bounded request/status diagnostics; staging pages/objects shall be deleted and never resumed implicitly. | Failure, crash-recovery, explicit-retry, and staging-cleanup tests pass. |
| SCRY-018 | Must | Snapshot delete shall require `acknowledgeEvidenceLoss=true`, purge payload rows transactionally, retain an immutable tombstone with identity/checksum/lineage, and never cascade into decks or Tagger. | Referenced-snapshot fixtures return deleted/unavailable provenance without a dangling snapshot ID. |
| SCRY-019 | Must | The collection contract fixture shall pin the observed identifier maximum and implementation shall reverify it against official documentation before changing the limit. | The fixture records observed limit 75, date, source, and fails contract review on drift. |
| SCRY-020 | Must | Projection price and ranking fields shall be labeled provider-supplied price/popularity evidence, never card quality or recommendation scores. | Output descriptions and schema snapshots preserve source/evidence labels. |
| SCRY-021 | Must | Multi-face card projections shall preserve ordered root and face objects separately and distinguish a source-confirmed empty field group from an absent/unknown group at each level. | Split, transform, modal DFC, and single-face fixtures round-trip without flattening or unknown-to-empty coercion. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Repeatability | Snapshot checksum and ordered output never change. |
| Fidelity | Raw JSON and unknown fields survive round trip. |
| Provider safety | Serialized pacing, bounded retries, stop-on-block. |
| Boundedness | 20,000 objects, 256 MiB, bounded pages/output pagination. |
| Observability | Snapshot status and lineage explain every incomplete acquisition. |

## Traceability

| Requirements | Validation |
| --- | --- |
| SCRY-001 through SCRY-004 | Endpoint fixture matrix and surface snapshot |
| SCRY-005 through SCRY-009 | SQLite immutability, lineage, raw/projection tests |
| SCRY-010 through SCRY-013 | Fake HTTP/fake clock safety tests |
| SCRY-014 through SCRY-017 | Network-spy, mode, sanitized error, and staging-cleanup tests |
| SCRY-018 | Tombstone/dependent-provenance tests |
| SCRY-019 | Pinned collection contract fixture |
| SCRY-020 | Projection evidence-label schema snapshots |

## Definition Of Done

- [ ] All endpoint families have fixtures and typed schemas.
- [ ] Snapshot immutability and lineage are proven.
- [ ] Provider pacing and stop behavior are proven offline.
- [ ] No Tagger, random, or query-language clone is introduced.
