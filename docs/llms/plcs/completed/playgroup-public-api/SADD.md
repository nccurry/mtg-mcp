# Playgroup Public API Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-04
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

`MtgMcp.Playgroup` owns the pinned spec fixture, handwritten typed HTTP client,
typed request records, lossless evidence envelope, pacing, and sanitized
errors. Provider response payloads remain detached `JsonElement` values inside
the envelope. This preserves nullable and additive fields exactly while the
operation, input, provenance, limitations, and failure contracts stay typed.
It also avoids a second provider-model hierarchy that would become stale
independently from the pinned fixture.

### Operation mapping

| Operation ID | Method/path | Tool |
| --- | --- | --- |
| `getCurrentUser` | GET `/me` | `playgroup_me_get` |
| `getCommanderById` | GET `/commanders/{id}` | `playgroup_commander_get` |
| `getCommanderByName` | GET `/commanders/by_name/{name}` | `playgroup_commander_get_by_name` |
| `getCommandersTurnDamage` | GET `/commanders/turn_damage` | `playgroup_commander_turn_damage_get` |
| `getDeckById` | GET `/decks/{id}` | `playgroup_deck_get` |
| `getDeckEloHistory` | GET `/decks/{id}/elo_history` | `playgroup_deck_elo_history_get` |
| `getUserById` | GET `/users/{id}` | `playgroup_user_get` |
| `listUserDecks` | GET `/users/{user_id}/decks` | `playgroup_user_decks_list` |
| `listUserPlaygroups` | GET `/users/{user_id}/playgroups` | `playgroup_user_playgroups_list` |
| `getUserPlaygroup` | GET `/users/{user_id}/playgroups/{playgroup_id}` | `playgroup_user_playgroup_get` |
| `listPlaygroupMembers` | GET `/playgroups/{playgroup_id}/members` | `playgroup_playgroup_members_list` |
| `listPlaygroupGames` | GET `/playgroups/{playgroup_id}/games` | `playgroup_playgroup_games_list` |
| `getPlaygroupGame` | GET `/playgroups/{playgroup_id}/games/{game_id}` | `playgroup_playgroup_game_get` |
| `batchImportEvents` | POST `/games/{game_id}/events/batch` | `playgroup_game_events_batch_create` |
| `createLiveSession` | POST `/live_sessions` | `playgroup_live_session_create` |

App additionally exposes `playgroup_auth_status` as local configuration status;
it is not counted as an API operation but is counted in the registered MCP
surface. The result is fifteen operation tools plus one status tool: sixteen
`playgroup_*` tools total.

### Data and evidence

Public outputs wrap complete provider records in typed source metadata and do
not normalize them into local deck quality concepts. The detached provider
document is itself structured JSON and retains unknown fields and explicit
nulls. Pagination is caller-controlled and one tool call maps to one provider
request except a bounded GET retry.

### Authentication and safety

The API key comes from `MTGMCP__PLAYGROUP__API_KEY` or a host secret provider.
It exists only in the Authorization header and process memory. A process-wide
pacer serializes request starts at 250 ms. The pinned OpenAPI publishes no rate
guidance, so this is a conservative client-owned default rather than a claimed
provider limit. Writes have no retry. The adapter does not launch, monitor, or
automatically update live sessions.

## Toolset And North-Star Design

App assigns all sixteen tools to the opt-in `playgroup` toolset. Startup
selection cannot make either documented write visible outside `remote`, and
the capability document reports the exact selected/visible surface without an
auth probe. The acceptance workflow reads redacted auth status, retrieves one
bounded provider-shaped observation, preserves contract/retrieval context, and
allows the client LLM to correlate it with explicitly supplied deck context.
No ranking helper, private endpoint, generic router, or compatibility alias is
part of this child.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Preserve current seven aggregate tools | Rejected; partial coverage and mixed local derivation. |
| Generate a large client | Rejected for current small surface; pinning and typed handwritten code are clearer. |
| Reverse-engineer deck updates | Rejected; absent from official public API. |
| Auto-follow Archidekt deck URLs | Rejected; providers and user intent remain separate. |
| Snapshot Playgroup responses | Deferred; current requirement is live provider evidence with retrieval metadata. |

## Failure Modes

- Missing key returns unavailable before HTTP.
- 401/403 returns sanitized authentication/permission failure.
- 404 is not found; empty page is successful empty.
- 429 may wait once only when Retry-After is present and bounded; otherwise
  unavailable.
- Schema drift returns provider-contract-unsupported rather than partial guessed
  mapping.
- Ambiguous write failure returns unknown acceptance and requires caller review.

## Test Architecture

The checked-in spec drives an operation inventory test. Sanitized fixtures cover
every response schema, pagination, nullability, extension fields, and errors.
Fake HTTP/clock tests cover headers, pacing, retries, write request counts, and
mode guards. Optional live reads use the configured account. The pinned
contract has no delete/undo operation for imported events and no close/delete
operation for a created live session. The repository owner therefore accepted
fixture-only coverage for both writes on 2026-07-03 against SHA-256
`2996db9134045e255987dda80ec1110dc28d2a84f2705622833d2ab339cb7ad4`.
The live suite has no write opt-in for this contract and cannot mutate a game or
create a session. Fixtures cover successful, rejected, unauthorized, and
ambiguous outcomes; release evidence labels these operations "not live-tested."

Refreshing the pinned contract requires fetching the same official URL,
recording observation date/version/bytes/SHA-256, generating an operation and
schema diff, reviewing auth/rate/error changes, updating only affected fixtures,
handwritten models, and tools, and re-running the resulting operation-to-tool
inventory. No spec change is silently accepted or applied through code
generation, and no obsolete operation is retained only to preserve the prior
count.
