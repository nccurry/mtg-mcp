# Archidekt Essentials And Synchronization Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
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

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Write-through local deck | Rejected; hides remote effects and conflicts. |
| Blind overwrite push | Rejected; loses concurrent Archidekt edits. |
| Automatic three-way merge | Rejected; requires choices about categories/printings. |
| Store raw provider baseline | Rejected; transport types would leak into local persistence. |
| Reuse legacy broad gateway | Rejected; it includes out-of-scope workflows and retry assumptions. |

## Failure Modes

- Missing credentials returns unavailable only for auth-required operations.
- Contract drift returns provider-contract-unsupported with sanitized field/path
  evidence; it never guesses an alternate payload.
- Missing or drifted verified delete support blocks this child and the cutover;
  deletion is never emulated through unrelated provider operations.
- 401 permits one relogin; repeated 401 stops.
- 403 stops. A 429 stops the operation and opens a sanitized cooldown through
  `Retry-After`; ambiguous mutation failure returns partial/unknown status.
- Final verification mismatch keeps the old baseline and requires pull.

## Test Architecture

Sanitized fixtures cover anonymous/private reads, login shapes, deck payload
variants, exact IDs, categories, printings, 401, 403, 404, 409-like drift,
429, 5xx, malformed payloads, and partial mutation. A fake clock proves pacing.
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
