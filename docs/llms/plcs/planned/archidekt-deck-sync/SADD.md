# Archidekt Decks, Folders, Snapshots, And Synchronization Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Archidekt` contains a thin typed client, contract mapper, process-local
auth session, pacer, and sync planner. It depends on Core contracts, not on the
SQLite implementation. App composes it with the local deck service.

### Authentication

Credentials come from a configured secret file or, when explicitly configured,
`MTGMCP__ARCHIDEKT__USERNAME` and `MTGMCP__ARCHIDEKT__PASSWORD`. Live tests use
the existing credential-file path through configuration; they never discover,
print, or copy its contents. Login tokens exist only in process memory and
refresh once after an unauthorized response.
Credential paths and identity values are not returned. Public deck reads may
proceed anonymously; private reads and all writes require authenticated state.

### Canonical mapping

Provider payloads map into `RemoteDeckSnapshot`: provider/remote ID, URI,
metadata, ordered entries with provider relation/card IDs, exact printing
identity where known, normalized zones/categories, retrieval time, source
payload checksum, and canonical fingerprint. Missing fields remain unknown.

Folder payloads map into provider-owned `RemoteFolderRecord` values containing
exact ID, name, visibility when present, parent ID, path, direct child-folder
IDs, contained deck summaries, retrieval metadata, source checksum, and an
unknown-field extension bag. Snapshot list rows map into
`RemoteNamedSnapshotSummary`; snapshot get maps the same metadata plus the
complete `RemoteDeckSnapshot` returned by Archidekt. These are remote evidence
models, not local deck categories or persistence entities.

### Folder organization

`archidekt_folder_list` returns the fresh canonical tree and its fingerprint;
`archidekt_folder_get` returns one folder's direct contents. Create and update
accept exact parent IDs and never select a same-named folder automatically.
`archidekt_folder_move_items` accepts typed deck/folder IDs, deduplicates them,
checks a fresh tree for missing items and cycles, fingerprints their current
assignments, applies the explicit destination, and verifies every result.

`archidekt_folder_delete` is intentionally narrower than the provider's generic
item-delete operation. It submits exactly one folder item only after proving
that the folder contains no decks or child folders and after matching folder
ID/name, tree fingerprint, and explicit confirmation. It never recursively
deletes or submits deck items. Ambiguous mutation failure returns unknown state
and requires a fresh folder read.

### Named snapshots and restoration

Snapshot create captures the current remote deck under an explicit name and
optional description. List/get preserve provider identity and timestamps;
update changes only provider-supported metadata; delete requires exact identity
and confirmation. Every mutation refetches the affected snapshot collection
and verifies the expected result.

Restore is a guarded provider workflow rather than a special local model.
`archidekt_snapshot_restore_preview` fetches the named snapshot and current
remote deck, maps both through the canonical deck mapper, and emits their exact
diff plus snapshot checksum, restorable-content fingerprint, current remote
fingerprint, and preview fingerprint.
`archidekt_snapshot_restore_apply` refetches both sources, verifies every guard,
uses the same primitive write planner and partial-failure classification as
push, and succeeds only when the final restorable-content fingerprint equals
the snapshot's. Provider relation IDs that must be regenerated are preserved as
an explicit before/after provider-identity delta, not treated as content
equality. Restore does not change the local deck or sync baseline; a later pull
is an explicit separate operation.

### Folder and snapshot MCP contracts

All results use the shared evidence envelope and structured
unavailable/unsupported/conflict/partial outcomes. Provider reads are visible
in all modes; every mutation below is visible only in `remote`.

| Tool | Required input | Success result | Mutation safety |
| --- | --- | --- | --- |
| `archidekt_folder_list` | None | Canonical folder tree, retrieval metadata, checksum/fingerprint | Read-only provider request |
| `archidekt_folder_get` | `folderId` | Exact folder plus direct child folders/decks and tree fingerprint | Read-only provider request |
| `archidekt_folder_create` | `name`, optional `parentFolderId`, explicit visibility | Verified created folder | No same-name inference; non-idempotent |
| `archidekt_folder_update` | `folderId`, expected tree fingerprint, explicit metadata patch | Verified updated folder | Patch allowlist; stale tree refuses |
| `archidekt_folder_move_items` | Expected tree fingerprint, typed `{kind,id,expectedParentFolderId}` items, destination folder ID or root | Per-item verified final assignments | Deduplication, existence/cycle preflight, partial statuses |
| `archidekt_folder_delete` | `folderId`, expected name/tree fingerprint, exact confirmation | Verified folder absence | Empty-only; never submits deck or recursive-delete items |
| `archidekt_snapshot_list` | `deckId` | Ordered snapshot summaries and collection checksum | Read-only provider request |
| `archidekt_snapshot_get` | `deckId`, `snapshotId` | Snapshot metadata plus complete canonical saved deck state | Read-only; deck ownership is cross-checked |
| `archidekt_snapshot_create` | `deckId`, expected remote fingerprint, name, optional description | Verified snapshot summary/checksum | Refuses if source deck changed |
| `archidekt_snapshot_update` | `deckId`, `snapshotId`, expected checksum, explicit metadata patch | Verified updated snapshot | Provider-supported metadata only |
| `archidekt_snapshot_delete` | `deckId`, `snapshotId`, expected checksum, exact confirmation | Verified snapshot absence | Destructive and stale-safe |
| `archidekt_snapshot_restore_preview` | `deckId`, `snapshotId` | Exact snapshot-to-current diff plus source checksum, content, remote, and preview fingerprints | Read-only; zero local/remote writes |
| `archidekt_snapshot_restore_apply` | `deckId`, `snapshotId`, snapshot checksum, content, remote, and preview fingerprints, exact confirmation | Primitive-operation statuses, provider-ID delta, and verified final content fingerprint | Destructive, no ambiguous retries, no local/baseline update |

### Three-way sync

The baseline stored in `decks.db` is the canonical deck snapshot last known to
match Archidekt. `archidekt_sync_diff` compares baseline, current local, and
fresh remote states and emits path-addressed additions, removals, and changes.

- Pull preview: remote-to-local operations, conflicts, local revision, remote
  fingerprint, and preview fingerprint.
- Pull apply: refetch remote, verify all guards, then use one local transaction.
- Push preview: local-to-remote operations after conflict analysis.
- Push apply: refetch remote, verify guards, execute stable operation sequence,
  refetch final remote, and update baseline only when final state matches.

There is no "ours/theirs" flag. The caller resolves conflicts through local
deck mutations or a new pull request.

### Write behavior

Remote operations are ordered metadata, categories, entry additions/updates,
entry removals, then verification. The planner minimizes requests using an
observed bulk endpoint only when its fixture and live proof establish equivalent
semantics to the primitive plan, including final fingerprint and partial-failure
classification. Bulk support is disabled by default and fixture evidence alone
cannot enable it. Ambiguous failure does not retry and does not update the
baseline.

Before applying, the planner computes a conservative upper bound on provider
requests. A value above 150 returns `request_limit_exceeded` with zero remote
writes. A process-wide per-account pacer permits at most 30 starts in a rolling
60-second window and spaces starts by at least two seconds. This stays below the
current Archidekt staff statement that throttling begins around 40 requests per
minute and leaves room for ordinary browser use. It is a client safety policy,
not a provider guarantee; stricter published guidance supersedes it.

A missing baseline on an existing binding returns `baseline_missing` conflict;
a corrupt baseline returns unavailable; neither selects pull or push. A
verified provider-missing result for a previously bound deck returns
`remote_deleted` evidence while leaving the local deck/binding untouched. The
observed contract may return `400` for a deleted ID, so classification uses the
reviewed response fixture plus fresh authenticated-list absence and never maps
every `400` to deletion. Only a separately previewed explicit workflow may
initialize or replace a binding.

## Toolset And North-Star Design

App assigns all 23 tools to the opt-in `archidekt` toolset. Enabling it changes
model-visible relevance only: operation mode still admits 11, 12, or 23 tools
and `OperationModeGuard` still enforces every mutation. The capability document
reports exact enabled/visible counts without probing credentials. The acceptance
workflow covers auth inspection, a fresh provider read, an explicit preview,
authorized apply, and verified outcome while leaving conflict resolution and
deckbuilding judgment to the client LLM. No provider router or compatibility
alias is permitted.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Write-through local deck | Rejected; hides remote effects and conflicts. |
| Blind overwrite push | Rejected; loses concurrent Archidekt edits. |
| Automatic three-way merge | Rejected; requires choices about categories/printings. |
| Store raw provider baseline | Rejected; transport types would leak into local persistence. |
| Reuse legacy broad gateway | Rejected; it includes out-of-scope workflows and retry assumptions. |
| Expose provider item deletion directly | Rejected; it could delete decks or non-empty folder trees. |
| Restore a snapshot without preview | Rejected; restoration is a destructive remote deck overwrite. |
| Treat automatic activity logs as snapshots | Rejected; named snapshots are explicit durable versions while logs/recent changes are a separate provider surface. |

## Failure Modes

- Missing credentials returns unavailable only for auth-required operations.
- Contract drift returns provider-contract-unsupported with sanitized field/path
  evidence; it never guesses an alternate payload.
- Missing or drifted verified delete support blocks this child and the cutover;
  deletion is never emulated through unrelated provider operations.
- Missing folder-delete cleanup, snapshot cleanup, or snapshot full-state
  retrieval blocks the affected stable surface; no residual provider object is
  accepted as live-test success.
- A stale folder assignment/tree fingerprint or a cyclic folder move performs
  zero writes. A non-empty folder delete is a structured conflict.
- A stale snapshot checksum, current-deck fingerprint, or restore preview
  fingerprint performs zero restore writes.
- 401 permits one relogin; repeated 401 stops.
- 403 stops. A 429 stops the operation and opens a sanitized cooldown through
  `Retry-After`; ambiguous mutation failure returns partial/unknown status.
- Final verification mismatch keeps the old baseline and requires pull.

## Test Architecture

Sanitized fixtures cover anonymous/private reads, login shapes, deck payload
variants, exact IDs, categories, printings, recursive folder trees/details,
folder mutation and cycle cases, snapshot metadata/full states/restoration,
401, 403, 404, 409-like drift, 429, 5xx, malformed payloads, and partial
mutation. A fake clock proves pacing.
Temporary local DB tests prove revision/baseline guards. Live tests require
explicit opt-in plus the configured credential file, create a uniquely named
private dummy deck, push/read/pull, and delete in `finally`. Verification uses a
fresh authenticated listing as well as the provider's deleted-ID response.
Missing or failed verified deletion fails the live acceptance gate and records
redacted cleanup evidence; a run that leaves a remote deck is never considered
successful.
Combined fake-HTTP/temporary-database tests prove that pull commits canonical
local content in one transaction and push updates its baseline only after final
remote verification. The live test assembly/filter is first made discoverable,
then an early contract spike proves private create/delete and cleanup before
the broader sync implementation proceeds. Planning research on 2026-07-03
already proved `POST /api/decks/v2/`, authenticated read-back, and
`DELETE /api/decks/{id}/` against a disposable private deck; implementation
repeats that proof through the actual adapter before adding broader writes.
The same disposable workflow then creates a unique private folder, moves the
dummy deck, exercises snapshot create/update/get/restore/delete, moves the deck
back to root, deletes the empty folder, and finally deletes the deck. Cleanup
runs in `finally`; fresh folder, snapshot, and deck reads must prove that no
probe object remains.
