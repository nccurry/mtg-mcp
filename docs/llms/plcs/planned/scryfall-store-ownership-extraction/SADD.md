# Scryfall Store Ownership Extraction Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Reviewers: independent design reviewer and adapter maintainer
- Last updated: 2026-09-07
- Related requirements: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: No

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-09-07 | mtg-mcp | Initial Phase 1A ownership design. |

## Executive Summary

The selected design keeps one concrete ScryfallDatabase owner for the database
path, SQLite connections, schema bootstrap, schema validation, and disposal.
Three concrete stores receive that database owner and contain their own domain
SQL.

The refactor removes indirection instead of adding it. No interface, repository,
or generic data-access layer sits between a store and SQLite. The only shared
helper is a small internal value-codec type when several stores need identical
UUID or UTC SQLite representations.

## Goals, Non-Goals, And Design Drivers

### Goals

- Make each Scryfall store own the behavior named by its type.
- Preserve every current Scryfall result, error, mutation guard, and data file.
- Keep the code easy to browse by giving each store a type-named source file.
- Keep the existing provider, Core, App, and MCP boundaries unchanged.

### Non-Goals

- Change a public method, tool, schema, mode, provider endpoint, or config key.
- Change the scryfall.db schema or write a migration.
- Introduce a generic persistence abstraction.
- Optimize a path or add a benchmark without a measured hot path.

### Design Drivers

- The parent PLC requires real store ownership before source or simulation growth.
- Existing tests give strong behavior coverage but direct database test calls hide
  the intended boundary.
- All normal tests must stay offline and deterministic.

## Context And Scope

ScryfallCardEvidenceOperations creates ScryfallDatabase, the three stores, and
ScryfallProviderClient. ScryfallService and the facade use those stores.

The present ScryfallStores.cs file exposes 27 forwarding workflow methods.
ScryfallDatabase implements corpus, snapshot, and coordination SQL in the same
class as schema and connection work.

This child moves the existing code only within MtgMcp.Scryfall. It does not
change a project reference, exported type, HTTP contract, or MCP registration.

## Constraints

- Use the checked-in .NET 11 preview toolchain and nullable reference types.
- Keep SQLite as the existing provider-owned persistence implementation.
- Preserve SchemaVersion 1 and the current authored schema checksum.
- Preserve active-plus-previous corpus retention, snapshot immutability, and
  cross-process lease and pacing semantics.
- Pass cancellation through every moved asynchronous call.
- Preserve current OperationResult cases and exception behavior.
- Keep source facts and Scryfall community tags distinct.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Keep forwarding stores. | Leave all SQL in ScryfallDatabase. | Lowest immediate edit count. | Names still hide ownership and future changes cross one god class. | Rejected. |
| Move SQL to concrete stores through ScryfallDatabase. | Stores call the concrete connection and schema owner. | Matches names, reduces navigation, and adds no runtime abstraction. | Store code has a direct SQLite dependency. | Chosen. |
| Add repository interfaces. | Hide SQLite behind common contracts. | A mock can replace SQLite in theory. | Adds abstraction cost and conceals data-specific behavior. | Rejected. |
| Split into separate database files. | Give each store a file and schema. | Strong physical isolation. | Breaks the current atomic corpus and shared pacing model. | Rejected. |

## Chosen Design

### Ownership Boundary

ScryfallDatabase remains a concrete infrastructure type. It owns:

- The private database path and Exists result.
- Read and write connection creation.
- SQLite initialization, schema bootstrap, and schema checksum validation.
- The initialization gate and disposal.

ScryfallDatabase does not contain a corpus, snapshot, or coordination workflow
method after this child.

The stores retain direct access to their shared ScryfallDatabase instance. They
open connections through narrow internal methods. This is an internal concrete
dependency, not a general persistence API.

### Shared SQLite Values

Create ScryfallSql only if more than one store needs a common representation.
It can contain fixed UUID and UTC formatting, parsing, nullable readers, and
the one-column string reader. It cannot contain domain SQL, repositories, or
connection creation.

Move the private checksum helper used only by ScryfallCardEvidenceOperations
into that type. Move the tag-weight ordering helper into ScryfallCorpusStore.

### Explicit Metadata-Check Ownership

The last_metadata_check_utc field remains in corpus_state. The corpus store
reads it when it reports corpus status. The coordination store writes it when a
process completes provider metadata acquisition.

This split is intentional. The field reports corpus freshness, but the write
coordinates an acquisition schedule. The facade changes its internal call from
CorpusStore.RecordMetadataCheckAsync to CoordinationStore.RecordMetadataCheckAsync.

### Unused Convenience Method

The current ScryfallDatabase.GetDirectTagsAsync method has no source caller.
Remove it after a source reference check. The retained
GetDirectTagsInGenerationAsync method is the actual store contract used for
stable generation-bound evidence.

## Data Design

The child makes no schema change.

| Data concern | Required state after the child |
| --- | --- |
| File name and data root | scryfall.db stays at the current path. |
| SchemaVersion and checksum | Remain version 1 and retain the authored checksum. |
| Tables and indexes | Keep the current SQL text and all names unchanged. |
| Corpus generations | Keep staging, active, previous, rollback, and guarded deletion behavior. |
| Snapshots | Keep immutable payload reuse, ordering, checksums, and delete guards. |
| Coordination | Keep crash-expiring leases and one global provider-start timeline. |
| Metadata timestamp | Keep the current column and value format. Only its internal writer changes owner. |
| Existing databases | Open without migration or data conversion. |

## Building Blocks

| Building block | Responsibility | Owned data or lifetime | Dependencies | Tests |
| --- | --- | --- | --- | --- |
| ScryfallDatabase | Path, connections, schema bootstrap and validation, disposal. | scryfall.db connection policy and initialization gate. | Microsoft.Data.Sqlite | Schema checksum and reopen tests. |
| ScryfallSql | Shared value codecs only when needed by multiple stores. | No state. | Microsoft.Data.Sqlite | Indirect coverage through store tests. |
| ScryfallCorpusStore | Corpus status, generations, cards, printings, rulings, tags, imports, activation, rollback, deletion, and metadata comparison. | Corpus tables and state reads. | ScryfallDatabase, ScryfallSql | Corpus characterization tests. |
| ScryfallSnapshotStore | Snapshot find, save, list, replay, and deletion. | Snapshot tables and payload reuse. | ScryfallDatabase, ScryfallSql | Snapshot characterization tests. |
| ScryfallRequestCoordinationStore | Request leases, provider-start reservations, and metadata-check writes. | Lease, pacing, and metadata-write state. | ScryfallDatabase, ScryfallSql | Coordination tests. |
| ScryfallCardEvidenceOperations | Existing service composition and higher-level workflows. | Existing provider and operation state. | Concrete stores and provider client. | Existing service tests. |

## Runtime And Data Flow

### Corpus Read Or Mutation

1. The service calls ScryfallCorpusStore.
2. The store asks ScryfallDatabase for a read or write connection.
3. The database makes sure that the existing schema is valid.
4. The store runs its current SQL and maps the same stored records.
5. The service returns the existing typed result.

### Snapshot Acquisition Or Replay

1. The service calls ScryfallSnapshotStore.
2. The store opens the same connection through ScryfallDatabase.
3. The store reads or writes the same snapshot rows and payloads.
4. The service returns the existing immutable snapshot result.

### Cross-Process Coordination

1. The provider client or facade calls ScryfallRequestCoordinationStore.
2. The store uses the same SQLite transaction mode as the current code.
3. The store returns the current lease or paced-start result.
4. The provider client keeps its current delay, retry, and HTTP behavior.

## MCP Surface, Schemas, And Diagnostics

This child changes no MCP surface.

| Surface | Required result |
| --- | --- |
| Tool names, input schemas, output schemas, annotations, and descriptions | No change. |
| Capability toolsets and operation-mode visibility | No change. |
| Structured results and text companions | No change. |
| Provider source attribution and freshness labels | No change. |
| Errors and reason codes | No change. |

The child compares the pre-change and post-change surface reports. It does not
claim a fixed tool count as a substitute for that comparison.

## Adapter And Provider Contracts

The refactor does not change the Scryfall provider boundary.

- Use only the existing official API and bulk-data paths.
- Keep the current User-Agent, Accept, pacing, retry, cancellation, and error
  sanitization behavior.
- Do not start a corpus sync during ordinary reads or process startup.
- Keep all fixture HTTP data sanitized and offline.

## Error Handling And Failure Modes

| Condition | Required behavior |
| --- | --- |
| Database file is absent during a read. | Return the current absent or not-cached result without creating a file. |
| Schema checksum is wrong. | Throw the current InvalidDataException before data reads. |
| Corpus import is malformed, too large, or cancelled. | Preserve atomic staging and active-generation behavior. |
| Snapshot acquisition fails after a later page. | Do not publish a partial immutable snapshot. |
| Lease owner differs or lease expires. | Preserve the current ownership and expiry behavior. |
| Provider starts compete. | Preserve the current global delay calculation. |
| Caller cancels. | Let OperationCanceledException propagate. |

The stores do not add broad exception catches. Existing adapter boundaries keep
their current typed outcome mapping and error redaction.

## Cross-Cutting Concepts

- The refactor adds no source fact, tag, cache, or derived value.
- The refactor adds no recommendation, card role, or inferred deck intent.
- The stores preserve existing list ordering, cursor checksums, and output caps.
- All production types and members retain useful XML documentation.
- Each store has one type-named source file after the move.

## Project Boundaries

MtgMcp.Core remains provider-neutral. MtgMcp.App remains the MCP composition
root. The work stays inside MtgMcp.Scryfall and its tests.

No provider DTO, HTTP client, SQLite reference, or Scryfall type enters Core.
No provider behavior moves into App.

## Readability And Documentation

- Use one source file per store type after the move.
- Put each SQL helper beside the operation that uses it.
- Use ScryfallSql only for true shared value codecs.
- Delete forwarding bodies and stale comments in the same change.
- Keep XML summaries useful and current.
- Record the parent Phase 0 selection and Phase 1A status.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| SSO-001 to SSO-004 | Concrete stores own domain SQL. The database owner exposes only infrastructure support. | Source ownership test and code review. |
| SSO-005 | No public or persisted contract changes. | Surface report and schema/reopen tests. |
| SSO-006 | Move code without changing transactions, queries, error paths, or cancellation. | Existing and direct-store characterization tests. |
| SSO-007 | Reuse fake HTTP and temporary SQLite fixtures. | Normal non-Live test run. |
| SSO-008 | Update the child and parent PLC status after validation. | Link and diff inspection. |

## Implementation Phases

| Phase | Code areas | Requirements | Exit criteria |
| --- | --- | --- | --- |
| 1 | Scryfall tests | SSO-005 to SSO-007 | Direct-store characterization passes on the forwarding baseline. |
| 2 | Database, corpus store, corpus tests | SSO-001, SSO-004, SSO-006 | Corpus code no longer lives in ScryfallDatabase. |
| 3 | Database, snapshot and coordination stores, tests | SSO-002 to SSO-006 | Snapshot and coordination code no longer lives in ScryfallDatabase. |
| 4 | Tests and PLC documentation | SSO-001 to SSO-008 | Broad validation and phase-close audit pass. |

## Test Architecture

Add direct-store tests that construct a ScryfallDatabase and then its owner
store. These tests prove behavior before and after the physical move.

| Domain | Required behavior cases |
| --- | --- |
| Corpus | Status, stable ordering, card and tag retrieval, full import, activation, rollback, guarded deletion, failed import, and cancellation. |
| Snapshots | Exact lookup, immutable reuse, list ordering, pagination, checksum-bound replay, guarded deletion, and later-page failure. |
| Coordination | Lease ownership, lease expiry, provider-start pacing, and metadata-check write behavior. |
| Schema | Checksum mismatch and reopen of a fixture-created database. |
| Architecture | The database declares no domain workflow method. Each store declares its named methods. |

Keep existing service tests. They prove that the service and MCP-facing behavior
did not change. Keep normal tests free of real Scryfall traffic.

## Framework And External Notes

This child does not change an external contract. The existing Scryfall adapter
rules remain in force: official API and bulk data only, bounded retries, shared
SQLite pacing, explicit corpus downloads, sanitized errors, and offline tests.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Concrete store dependencies | Decision | Stores see SQLite through the database owner. | Chosen to avoid a generic abstraction. |
| Shared value codecs | Decision | Several stores can retain exactly matching SQLite representations. | Use a small ScryfallSql helper only for shared codec logic. |
| Metadata-check writer | Decision | A corpus-state column has a coordination owner for writes. | Move RecordMetadataCheckAsync to the coordination store. |
| Unused active tag lookup | Decision | One uncalled internal convenience method remains. | Remove it after source and compiler confirmation. |
| Architecture test shape | Risk | A brittle source test can slow harmless refactors. | Assert only the named ownership boundary. |
| Archidekt extraction | Deferred | It is the parent Phase 1B child. | Do not change Archidekt in this packet. |
| SDK upgrade and new providers | Deferred | They require separate evidence and decisions. | Do not change packages or providers. |

## Glossary

| Term | Meaning |
| --- | --- |
| Corpus | The versioned Scryfall bulk-data generations in scryfall.db. |
| Snapshot | An immutable record of one exact provider request and its payloads. |
| Coordination | SQLite rows that prevent duplicate acquisition and coordinate provider start times. |
| Database owner | The concrete type that opens connections and makes sure that the schema is valid. |
| Store | A concrete internal type that owns SQLite operations for one data domain. |
