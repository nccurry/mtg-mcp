# Archidekt Decks, Folders, Snapshots, And Synchronization Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Scenario | Expected result |
| --- | --- | --- |
| ARCH-FIX-001 | Anonymous public deck | Complete mapped remote snapshot or explicit unavailable. |
| ARCH-FIX-002 | Authenticated list/private deck | Redacted auth and exact list/get mapping. |
| ARCH-FIX-003 | Alternate card/category payload shapes | Canonical equivalent fingerprint. |
| ARCH-FIX-004 | Exact set/collector/finish/language variants | Printing identity preserved or explicitly unknown. |
| ARCH-FIX-005 | Local-only change since baseline | Push preview only. |
| ARCH-FIX-006 | Remote-only change since baseline | Pull preview only. |
| ARCH-FIX-007 | Same path changed locally and remotely | Conflict; no apply operations. |
| ARCH-FIX-008 | Unrelated local/remote changes | Both visible; no automatic merge. |
| ARCH-FIX-009 | Stale local revision or remote fingerprint | Apply refuses with zero writes. |
| ARCH-FIX-010 | Failure after two remote operations | Applied/unknown/not-attempted statuses; baseline unchanged. |
| ARCH-FIX-011 | Create private shell | Remote ID bound locally; contents remain unpushed. |
| ARCH-FIX-012 | Delete contract missing/drifted | Structured unsupported, no emulation, and child/cutover gate failed. |
| ARCH-FIX-013 | Existing binding has no baseline | `baseline_missing` conflict; no remote or local write. |
| ARCH-FIX-014 | Baseline checksum is corrupt/stale | Unavailable/conflict with evidence; no guessed direction or write. |
| ARCH-FIX-015 | Previously bound remote deck returns the reviewed missing/deleted response and is absent from a fresh authenticated listing | `remote_deleted`; local deck and binding remain unchanged. An unrelated `400` is not sufficient. |
| ARCH-FIX-016 | Predicted primitive plan requires 151 requests | `request_limit_exceeded` before the first mutation. |
| ARCH-FIX-017 | Bulk fixture passes but live equivalence proof is absent | Bulk path remains disabled; primitive plan is used or cap refusal returned. |
| ARCH-FIX-018 | Bulk and primitive plans over same throwaway content | Final fingerprint and failure classification match before bulk can be enabled. |
| ARCH-FIX-019 | Recursive folder tree plus one folder detail | Exact IDs, visibility, parents, paths, children, deck summaries, checksum, and unknown fields are preserved. |
| ARCH-FIX-020 | Folder create/update with an exact parent and fresh tree fingerprint | Verified folder state is returned; no name-based destination inference occurs. |
| ARCH-FIX-021 | Move typed deck/folder items to another folder or root | Inputs are deduplicated; source assignments and destination verify exactly. |
| ARCH-FIX-022 | Folder move would place a folder under itself or a descendant | `folder_cycle`; zero provider writes. |
| ARCH-FIX-023 | Delete a stale or non-empty folder | Conflict with exact contents/tree evidence; zero provider writes or deck-delete items. |
| ARCH-FIX-024 | Delete a confirmed empty folder | One folder-only delete item is submitted and fresh tree absence is verified. |
| ARCH-FIX-025 | Snapshot list row and full snapshot get | Metadata-only list stays distinct from complete canonical saved deck state; unknown fields round trip. |
| ARCH-FIX-026 | Snapshot create then metadata update | Exact deck/snapshot IDs and verified name/description/timestamps are returned. |
| ARCH-FIX-027 | Snapshot restore preview | Exact snapshot-to-current-remote diff plus source checksum, restorable-content, remote, and preview fingerprints is returned with zero writes. |
| ARCH-FIX-028 | Snapshot or remote deck changes after restore preview | Restore apply refuses with zero writes. |
| ARCH-FIX-029 | Snapshot restore fails after two primitive operations | Applied/unknown/not-attempted statuses are explicit; baseline and local deck remain unchanged. |
| ARCH-FIX-030 | Successful snapshot restore with regenerated provider relation IDs | Final restorable-content fingerprint equals the immutable snapshot content fingerprint; provider-ID deltas are explicit; local deck and sync baseline remain unchanged. |
| ARCH-FIX-031 | Snapshot delete with stale identity or missing confirmation | Structured conflict; zero writes. |
| ARCH-FIX-032 | Disposable folder/snapshot/deck cleanup cannot be verified | Live acceptance and cutover fail; no residual object is treated as success. |

## MCP Surface Matrix

| Tool | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `archidekt_auth_status`, `archidekt_deck_list`, `archidekt_deck_get`, `archidekt_sync_diff`, `archidekt_pull_preview`, `archidekt_push_preview` | Visible | Visible | Visible |
| `archidekt_folder_list`, `archidekt_folder_get` | Visible | Visible | Visible |
| `archidekt_snapshot_list`, `archidekt_snapshot_get`, `archidekt_snapshot_restore_preview` | Visible | Visible | Visible |
| `archidekt_pull_apply` | Hidden | Visible | Visible |
| `archidekt_deck_create`, `archidekt_deck_delete`, `archidekt_push_apply` | Hidden | Hidden | Visible |
| `archidekt_folder_create`, `archidekt_folder_update`, `archidekt_folder_move_items`, `archidekt_folder_delete` | Hidden | Hidden | Visible |
| `archidekt_snapshot_create`, `archidekt_snapshot_update`, `archidekt_snapshot_delete`, `archidekt_snapshot_restore_apply` | Hidden | Hidden | Visible |

The Archidekt family therefore contributes 11 tools in `read-only`, 12 in
`local`, and 23 in `remote`.

## Provider Safety Matrix

- One request at a time, at least two seconds between starts, and at most 30
  starts in any rolling 60 seconds per configured account.
- Maximum 150 requests per tool call.
- One login retry after 401.
- No retry after 403, 429, or ambiguous mutation failure; a valid
  `Retry-After` sets the earliest permitted future request time.
- Secrets and credential paths absent from all recorded fixtures.

## Planning Contract Evidence

| Observed UTC | Operation | Result | Retained conclusion |
| --- | --- | --- | --- |
| 2026-07-03 | Authenticated private empty-deck create | `POST /api/decks/v2/` returned `201`; read-back returned `200` with matching name/private state. | Current create contract is viable. |
| 2026-07-03 | Disposable-deck cleanup | `DELETE /api/decks/{id}/` returned `204`; deleted-ID read returned `400`; authenticated listing contained zero probe decks. | Cleanup is viable; absence verification must not require `404`. |
| 2026-07-03 | Provider pacing research | Current Archidekt staff guidance says throttling begins around 40 requests/minute. | Client ceiling is 30 starts/minute with two-second spacing. |
| 2026-07-03 | Public folder frontend contract | Current frontend uses folder tree/detail, create, typed mass update, and item-delete operations. | Rebuild guarded folder outcomes; never expose generic item deletion directly. |
| 2026-07-03 | Public snapshot frontend contract | Current frontend uses snapshot list/get/create/update/delete and restores by fetching saved state before deck overwrite. | Rebuild full snapshot evidence and guarded restore preview/apply. |

The live probe used the configured credential file but retained no credential,
token, path, deck URL, or remote ID. This planning evidence does not replace the
adapter-level live acceptance test.

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| ARCH-001 | Project-reference and provider-DTO architecture tests. |
| ARCH-002 | ARCH-FIX-002 plus auth, error, log, and configuration redaction tests. |
| ARCH-003, ARCH-004 | ARCH-FIX-001 through ARCH-FIX-004 and fingerprint snapshots. |
| ARCH-005, ARCH-009 | ARCH-FIX-005 through ARCH-FIX-008 three-way diff matrix. |
| ARCH-006, ARCH-007 | ARCH-FIX-009 plus temporary-database transactional pull tests. |
| ARCH-008, ARCH-014 | ARCH-FIX-010 and captured stable request/status sequence. |
| ARCH-010 | ARCH-FIX-011, private-default assertion, and remote-mode guard. |
| ARCH-011 | ARCH-FIX-012, verified delete fixture, and live residual-state gate. |
| ARCH-012, ARCH-013 | Fake-clock, cap, block, and ambiguous-write request-count tests. |
| ARCH-015 | MCP surface matrix and zero-write mode tests. |
| ARCH-016 | Sanitized fixture manifest and contract-drift test. |
| ARCH-017 | Live-test discovery, opt-in, unique-name, and cleanup guards. |
| ARCH-018 | ARCH-FIX-017, ARCH-FIX-018, and bulk-disablement architecture tests. |
| ARCH-019 | ARCH-FIX-013 through ARCH-FIX-015 and combined fake-HTTP/temporary-DB tests. |
| ARCH-020 | ARCH-FIX-019 plus folder tree/detail canonicalization and unknown-field tests. |
| ARCH-021 | ARCH-FIX-020 plus exact-parent, stale-tree, ambiguity, and verification tests. |
| ARCH-022 | ARCH-FIX-021, ARCH-FIX-022, and partial/ambiguous mass-update tests. |
| ARCH-023 | ARCH-FIX-023, ARCH-FIX-024, and a request spy proving no deck or recursive delete item is submitted. |
| ARCH-024 | ARCH-FIX-025 plus metadata/full-state distinction and lossless snapshot mapping tests. |
| ARCH-025 | ARCH-FIX-026, ARCH-FIX-031, and verified snapshot absence tests. |
| ARCH-026 | ARCH-FIX-027 through ARCH-FIX-030 and shared primitive-planner equivalence tests. |
| ARCH-027 | Dated route manifest, sanitized contract fixtures, and fail-closed drift tests. |
| ARCH-028 | ARCH-FIX-032 plus live folder/snapshot/deck lifecycle and residual-state guards. |

## Live Acceptance

The `Category=Live` test requires an explicit opt-in flag and the configured
credential file or equivalent host secret. It uses unique private dummy folder
and deck names, records only redacted checksums, verifies
create/push/get/pull, folder create/update/get/move, and snapshot
create/update/get/restore/delete. In `finally`, it moves the deck to root,
deletes the now-empty folder, and deletes the deck. Cleanup verification uses
fresh authenticated folder, snapshot, and deck reads; the deck check includes
a fresh listing because the observed deleted-ID read returns `400`. If any
absence cannot be verified, the test fails and records redacted cleanup
evidence. A live run that leaves a remote folder, snapshot, or deck does not
satisfy the gate.
