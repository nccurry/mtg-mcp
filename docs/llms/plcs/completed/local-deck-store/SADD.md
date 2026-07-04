# Local Deck Domain And SQLite Store Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Core` owns immutable provider-neutral deck records and operation unions.
`MtgMcp.Decks` owns SQLite connections, migrations, repositories, backup files,
and the application service. App owns MCP wrappers and mode enforcement.

### Schema v1

| Table | Key fields and invariants |
| --- | --- |
| `decks` | `deck_id`, name, description, format, revision, created/updated UTC. |
| `deck_entries` | `entry_id`, deck FK, quantity > 0, name, optional identity fields, zone, sort order. `entry_id` is the only identity/uniqueness key; otherwise identical rows are allowed and never coalesced. |
| `deck_categories` | `category_id`, deck FK, case-insensitive unique name, color, sort order. |
| `deck_entry_categories` | entry/category FKs, `is_primary`; unique pair and partial unique primary per entry. |
| `provider_bindings` | binding ID, deck FK, provider, remote ID/URI, remote version, baseline fingerprint, sync times. |
| `sync_baselines` | binding FK and canonical native deck snapshot; no provider JSON. |
| `schema_migrations` | ordered version, applied UTC, application version, checksum. |

UUIDs are application-created version-7 GUIDs. UTC timestamps serialize as ISO
8601. Provider, format, zone, language, and finish strings are normalized but
remain extensible; unknown values are preserved rather than mapped to guesses.

### Mutation flow

1. App checks mode and validates the typed request.
2. Deck service opens one connection and immediate transaction.
3. It loads the deck revision and compares `expectedRevision`.
4. It applies explicit operations in caller order and validates invariants.
5. It increments revision once, writes updated UTC, and commits.
6. It returns the canonical updated deck and revision.

No add operation silently merges entries. Callers update quantity by entry ID.
Deleting a category removes assignments by FK cascade but never deletes cards.
Each granular mutator constructs exactly one case of the same operation union
accepted by `deck_apply_changes` and calls the same revision-guarded
transactional service. Granular tools are ergonomic schemas, not independent
write paths.

`deck_validate` evaluates only local structural invariants: valid references,
positive quantities, nonblank zones, category-primary consistency, and explicitly
documented Commander fixture structure. It does not call providers, determine
format legality, infer roles, classify cards, or assess deck quality.

### Canonical ordering

Decks order by normalized name then ID. Categories order by `sort_order`,
normalized name, then ID. Entries order by normalized zone, `sort_order`, card
name, set, collector number, finish, then ID. Assignments place primary first,
then category order.

### Backups and migrations

Backups live under `v0.9/backups/decks/` with opaque UUID filenames and a small
manifest containing schema version, fingerprint, timestamp, and deck count.
Restore requires `expectedDatabaseFingerprint`, uses unpooled connections,
verifies integrity in a temporary location, atomically replaces the database,
and retains the displaced DB as a rollback backup.

The fingerprint is SHA-256 over the bytes of a consistent SQLite Online Backup
snapshot after checkpointing the source connection, prefixed by the schema
version. Byte-level changes are allowed to conflict even when logically
equivalent; the guard is intentionally conservative. `deck_backup_list` returns
the current database fingerprint in its envelope and each backup's manifest
fingerprint, without a filesystem path. `deck_backup_create` returns the new
backup ID and manifest fingerprint. Restore rejects a stale current fingerprint,
a manifest/file hash mismatch, failed `PRAGMA integrity_check`, or an unknown
schema before swapping files.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| JSON per deck | Rejected; weak transactional and relational guarantees. |
| Entity Framework | Rejected; unnecessary dependency and opaque migrations. |
| One database shared with caches | Rejected; rebuildable cache churn must not endanger decks. |
| Category-driven inclusion | Rejected; caused ambiguous counts in the legacy model. |
| Name as card identity | Rejected; preserve unresolved state and exact IDs separately. |

## Failure Modes

- Constraint/input failure returns invalid input with a bounded reason and no path.
- Stale revision returns conflict with current revision and no deck payload
  unless the caller separately reads it.
- Locked database returns bounded unavailable status after busy timeout.
- Corruption fails closed without exposing local paths.
- Missing deck/entry/category is not found, distinct from empty collections.

## Test Architecture

Use temporary databases for every integration test. Cover schema creation,
upgrade/rollback, foreign keys, duplicate names, primary-category constraints,
concurrent stale writes, atomic batches, cancellation, canonical reads,
backup/restore, corruption, and legacy-file isolation. MCP tests snapshot every
tool schema, mode, and annotation. Equivalence tests run each granular mutation
as both a dedicated tool and a single-operation batch. When `MtgMcp.Decks`
lands, its project is added to applicable integration/convenience task lists;
the generic per-assembly coverage gate remains authoritative.
