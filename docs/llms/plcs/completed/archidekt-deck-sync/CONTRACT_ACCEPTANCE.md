# Archidekt Contract Acceptance

## Accepted Observation

- Observed UTC date: 2026-07-04
- Client pacing: one request at a time, at least two seconds between starts,
  at most 30 starts in a rolling minute
- Authentication: existing configured credential file; no identity, value,
  token, or path retained
- Remote objects: uniquely named private disposable deck, folder, and snapshot
- Cleanup: passed; a final authenticated check found no disposable deck,
  folder, or snapshot

## Observed Contract

| Workflow | Observed route or behavior | Retained adapter rule |
| --- | --- | --- |
| Login | `POST /api/rest-auth/login/` | Token remains in process memory; one refresh after `401`. |
| Owned deck list | `GET /api/decks/v3/?ownerUsername=...` | Fetch the authenticated owned collection, sort canonically, and expose checksum-bound local cursor pages. |
| Deck detail | `GET /api/decks/{id}/` | Retry authenticated detail when an anonymous private read is hidden as `404`. |
| Deck create/delete | `POST /api/decks/v2/`; `DELETE /api/decks/{id}/` | Create private by default; require guards and authenticated-list absence for delete. |
| Folder tree | `GET /api/decks/folderTree/` returns one direct root object | Flatten the direct root and descendants. The tree omits deck rows, so join the fresh owned-deck list. |
| Folder create | `POST /api/decks/folders/` with `parent_folder`; response is an ID | Resolve omitted parent to the one explicit root, then verify the ID in a fresh tree. |
| Folder update/move | `PATCH /api/massUpdate/` with typed items and `patch.parentFolder` or allowlisted folder fields | Preflight exact IDs/parents/cycles and verify every final assignment. |
| Folder delete | `POST /api/decks/folders/deleteItems/` | Submit one folder item only after the joined tree proves it empty. |
| Snapshot list/get/create/delete | Deck-scoped list/create plus `GET`/`DELETE /api/decks/snapshots/{id}/` | Keep metadata rows distinct from complete saved state and verify collection absence. |
| Snapshot update | `PATCH /api/decks/snapshots/{id}/` supports `name` | Expose rename only; description remains create-only. |
| Snapshot restore | Snapshot detail is replayed through deck primitives | Preserve current deck name, visibility, and folder placement because the snapshot does not own them. |

## Acceptance Workflow

The opt-in `Category=Live` workflow successfully created a private Commander
deck, listed and read it, created and renamed a private folder, moved the deck
into that folder, created/read/renamed/restored/deleted a named snapshot, moved
the deck back to the explicit root, deleted the deck, and deleted the now-empty
folder. Every provider response was consumed through the production adapter and
the process-wide safety lane.

The final post-audit rerun completed in 1 minute 58 seconds with the production
two-second spacing and rolling 30-start ceiling enabled. The test's final
authenticated residual checks passed.

The report deliberately excludes raw payloads, credential locations, account
identity, remote IDs, names containing run suffixes, and provider URLs tied to
the disposable objects.
