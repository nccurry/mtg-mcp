# Playgroup Public API Fixtures And Acceptance Matrix

## Pinned Operations

| Method/path | Read/write | Required fixture |
| --- | --- | --- |
| GET `/me` | Read | Current user |
| GET `/commanders/{id}` | Read | Found/not found |
| GET `/commanders/by_name/{name}` | Read | Unicode name/found/not found |
| GET `/commanders/turn_damage` | Read | Paginated rows |
| GET `/decks/{id}` | Read | Full nullable deck |
| GET `/decks/{id}/elo_history` | Read | Empty and populated history |
| GET `/users/{id}` | Read | Found/not found |
| GET `/users/{user_id}/decks` | Read | Paginated decks |
| GET `/users/{user_id}/playgroups` | Read | Paginated playgroups |
| GET `/users/{user_id}/playgroups/{playgroup_id}` | Read | Membership/permission/not found |
| GET `/playgroups/{playgroup_id}/members` | Read | Paginated members |
| GET `/playgroups/{playgroup_id}/games` | Read | Pagination and `include_events` |
| GET `/playgroups/{playgroup_id}/games/{game_id}` | Read | With/without events |
| POST `/games/{game_id}/events/batch` | Write | Valid batch, validation failure, ambiguous transport |
| POST `/live_sessions` | Write | Valid request, validation failure, ambiguous transport |

## MCP Mode Matrix

| Surface | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `playgroup_auth_status` and thirteen GET tools | Visible | Visible | Visible |
| `playgroup_game_events_batch_create`, `playgroup_live_session_create` | Hidden | Hidden | Visible |

The matrix contains fourteen all-mode tools (thirteen GET operations plus auth
status) and two remote-only write tools, for sixteen registered tools. The
provider operation count remains fifteen.

## Contract Drift Cases

- Added operation fails the exact operation inventory until reviewed.
- Removed/renamed property fails affected fixture mapping.
- Optional additive property is preserved through extension data.
- Changed auth scheme fails the contract security check.

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| PLAY-001 | Pinned OpenAPI byte-size, version, and SHA-256 fixture test. |
| PLAY-002 | Pinned operations table and exact tool-registration inventory. |
| PLAY-003, PLAY-004 | Nullable, pagination, extension-data, and provenance schema round trips. |
| PLAY-005 | Bearer-header fake HTTP and auth/error/config redaction tests. |
| PLAY-006 | Network-spy, dependency, and forbidden-ranking surface tests. |
| PLAY-007 | Provider pagination boundary fixtures for every paginated operation. |
| PLAY-008, PLAY-009 | MCP mode matrix and single-attempt write failure tests. |
| PLAY-010, PLAY-011 | Fake-clock retry, stop, Retry-After, serialization, and cancellation tests. |
| PLAY-012 | Capability unsupported response and private-endpoint network spy. |
| PLAY-013 | Contract drift cases and reviewed-fixture checksum gate. |
| PLAY-014 | Sanitized provider error fixtures. |
| PLAY-015 | Offline discovery, safe live-read opt-in, hard no-live-write guard, and dated owner fixture-only decision. |
| PLAY-016 | Default/all/none/explicit `playgroup` profile matrix plus auth/provider-read/evidence-correlation workflow and remote-write spies. |

## North-Star Workflow Fixture

With `playgroup` explicitly enabled, the client inspects redacted auth status,
requests one bounded provider operation, preserves endpoint/version/retrieval
context, and correlates explicit provider IDs with caller-supplied deck context.
No quality ranking or deck update is produced. The family is absent from the
default profile, and enabling it never makes writes visible outside `remote`.

## Live Tests

Read tests require `Category=Live` and a configured key. No live-write test or
write opt-in exists for the pinned contract. Its two POST operations expose no
documented cleanup route, so both remain fixture-only under the repository-owner
decision recorded on 2026-07-03 for OpenAPI SHA-256
`2996db9134045e255987dda80ec1110dc28d2a84f2705622833d2ab339cb7ad4`.
The evidence explicitly says "not live-tested" and does not treat that state as
a pass. A later official cleanup operation may replace this decision through a
reviewed contract update.

## Acceptance Result

All offline fixtures, contract inventory, MCP mode/schema checks, north-star
workflow checks, coverage gates, and package/install smokes passed on
2026-07-04. The opt-in authenticated read remains unexecuted because no API key
was configured. Both writes remain deliberately fixture-only under the owner
decision above.
