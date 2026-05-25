# Playgroup.gg Support Plan

## Research Snapshot

Observed on 2026-05-24:

- Playgroup publishes Swagger UI at `https://playgroup.gg/api-docs/index.html`.
- The Swagger UI points to `https://playgroup.gg/api/public/v1/openapi.yaml`.
- The public API base URL is `https://playgroup.gg/api/public/v1`.
- Authentication uses account API keys with `Authorization: Bearer <your-api-key>`.
- The OpenAPI description says commander endpoints are public, user and deck endpoints optionally accept API keys, and playgroup game listing requires API-key authentication plus playgroup membership.
- The OpenAPI document exposes no direct `GET /playgroups/{id}/decks` endpoint. Decks in a playgroup must initially be derived from `GET /playgroups/{playgroup_id}/games` participations, then enriched through `GET /decks/{id}` and `GET /decks/{id}/elo_history?playgroup_id={playgroup_id}`.
- Playgroup's FAQ defines Estimated Deck Power as deck Elo normalized to a value between 0 and 10, with confidence increasing as the deck plays more games against more decks and pilots. Competitive Rating is based on average winning turn brackets.

This means Playgroup support can be implemented against documented endpoints and should not need authenticated HTML scraping or session cookies.

## Product Goal

Give LLM clients a grounded way to answer questions like:

- "What decks are in this Playgroup.gg group?"
- "Which decks look most powerful in this playgroup?"
- "What does my deck need to handle in this local meta?"
- "Open one of those playgroup decks through the source deck URL when Playgroup exposes one."

This should complement, not replace, current local and Archidekt workspace flows.

Current status: implemented with read-only Playgroup tools plus deterministic
local-meta candidate scoring.

## Implemented MCP Surface

Read-only Playgroup tools:

- `get_playgroup_auth_status`
  - Returns configured auth mode, base URL, whether an API key or credential file exists, and any redacted credential-file parse error.

- `get_playgroup`
  - Inputs: `playgroupIdOrUrl`, `userId=null`.
  - Uses `GET /me` to discover `userId` when omitted, then calls `GET /users/{user_id}/playgroups/{playgroup_id}`.
  - Returns playgroup id, name, game count, member count, leagues, and warnings.

- `list_playgroup_decks`
  - Inputs: `playgroupIdOrUrl`, `maxGames=200`, `limit=100`.
  - Calls `GET /playgroups/{playgroup_id}/games` page by page, extracts unique deck ids from participations, enriches them with deck details and playgroup-scoped Elo history, then returns compact deck summaries.
  - Warnings must say results are derived from fetched games because the public API does not expose a direct playgroup deck list.

- `list_playgroup_users`
  - Inputs: `playgroupIdOrUrl`, `maxGames=200`, `limit=100`.
  - Calls `GET /playgroups/{playgroup_id}/games` page by page and extracts unique users from participations.
  - Warnings must say results are derived from fetched games because the public API surface exposes playgroups through users but does not expose a direct playgroup member lookup endpoint.

- `list_playgroup_user_decks`
  - Inputs: `playgroupIdOrUrl`, `userIdOrName`, `source="any"`, `maxGames=200`, `limit=100`.
  - Resolves `userIdOrName` from fetched game participations unless it is already numeric, then calls `GET /users/{user_id}/decks`.
  - Supported sources: `any`, `archidekt`.
  - If a name has no unique match in fetched games, return a clear resolution error with candidate ids/names when available.

- `rank_playgroup_decks`
  - Inputs: `playgroupIdOrUrl`, `metric="estimated_power"`, `minGames=0`, `includeLowConfidence=false`, `maxGames=200`, `limit=20`.
  - Supported metrics: `estimated_power`, `elo`, `win_rate`, `competitive_rating`, `games_played`, `average_win_turn`.
  - For "most powerful", prefer Playgroup's `power_level`, include `confidence_factor`, and warn when confidence is low. If power is missing, fall back to playgroup-scoped Elo.

- `get_playgroup_deck`
  - Inputs: `deckId`.
  - Returns normalized Playgroup deck details, including source `decklist_url` when present.

- `score_cards_for_playgroup_meta`
  - Inputs: `workspaceId`, `playgroupIdOrUrl`, optional `candidateCards`, `maxGames`, `metaDeckLimit`, `simulations`, `maxTurn`, `seed`, and `maxPrice`.
  - Scores explicit candidates, or excluded workspace cards when candidates are omitted.
  - Factors are plan fit, deterministic performance delta, local-meta coverage, self-harm penalty, price/bracket constraints, and evidence confidence.
  - Uses Playgroup-derived rankings and imports Archidekt decklists read-only when Playgroup exposes an Archidekt source URL.

Consider an optional later tool:

- `open_playgroup_deck`
  - If `decklist_url` points at Archidekt, this can delegate to the existing Archidekt local-only open flow.
  - If the source is Moxfield or another provider, return the source URL until that provider has first-class support.

Consider resources after the tool responses settle:

- `mtg://playgroup/auth-status`
- `mtg://playgroup/{playgroupId}/decks`
- `mtg://playgroup/{playgroupId}/rankings/{metric}`

## Data Model

Provider-neutral Core models:

- `PlaygroupAuthStatus`
- `PlaygroupUser`
- `PlaygroupSummary`
- `PlaygroupLeagueSummary`
- `PlaygroupGame`
- `PlaygroupParticipation`
- `PlaygroupDeck`
- `PlaygroupDeckListResult`
- `PlaygroupUserSummary`
- `PlaygroupUserListResult`
- `PlaygroupUserDeckListResult`
- `PlaygroupDeckSummary`
- `PlaygroupDeckRanking`
- `PlaygroupDeckRankingResult`
- `PlaygroupEloHistory`

Important fields for `PlaygroupDeckSummary`:

- `DeckId`
- `Name`
- `UserId`
- `OwnerName`
- `CommanderNames`
- `ColorIdentity`
- `DecklistUrl`
- `Url`
- `Games`
- `Wins`
- `Losses`
- `WinRatePercentage`
- `Elo`
- `EstimatedPower`
- `ConfidenceFactor`
- `CompetitivenessRating`
- `AverageWinsByRound`
- `LastPlayedAt`
- `Warnings`

## Architecture

Adapter project:

- `src/MtgMcp.Playgroup/MtgMcp.Playgroup.csproj`
- `PlaygroupOptions`
- `PlaygroupCredentials`
- `PlaygroupServiceCollectionExtensions`
- `PlaygroupGateway`
- request/response DTO files split by API area when useful

Core pieces:

- `IPlaygroupGateway`
- normalized Playgroup models
- `PlaygroupService` for id parsing, deck aggregation, ranking, and warnings

The adapter is registered in `MtgMcp.App` similarly to Scryfall and Archidekt.
`MtgMcp.Core` does not reference the adapter.

Configuration:

- `MtgMcp:Playgroup:BaseAddress`
- `MtgMcp:Playgroup:ApiKey`
- `MtgMcp:Playgroup:CredentialsFile`

Environment aliases:

- `PLAYGROUP:BASE_ADDRESS`
- `PLAYGROUP:API_KEY`
- `PLAYGROUP:CREDENTIALS_FILE`

The adapter mirrors Archidekt's direct credential fallback pattern with `PLAYGROUP_API_KEY`,
`PLAYGROUP_BASE_ADDRESS`, and `PLAYGROUP_CREDENTIALS_FILE`, while the canonical .NET environment keys remain
`MTGMCP__PLAYGROUP__BASE_ADDRESS`, `MTGMCP__PLAYGROUP__API_KEY`, and
`MTGMCP__PLAYGROUP__CREDENTIALS_FILE`.

Secret redaction prevents `ApiKey` and Playgroup credential-file contents from
appearing in config output, errors, logs, or tests.

## Implementation Status

Implemented:

- documented OpenAPI-backed contract and game-derived deck listing behavior.
- normalized Core models, `IPlaygroupGateway`, and `PlaygroupService`.
- `MtgMcp.Playgroup` adapter with API-key credentials, credential-file loading,
  JSON mapping, and fake HTTP tests.
- service tests for id parsing, aggregation, ranking, and warning behavior.
- `PlaygroupTools`, auth-status resource, surface tests, configuration tests,
  project boundary tests, and documentation comments.

## Risks and Decisions

- The public API does not expose a direct playgroup deck list. Initial `list_playgroup_decks` answers "decks seen in fetched playgroup games", not every deck a member has ever created.
- Fetching many deck details can be chatty. Default `maxGames` should be bounded and tool responses should explain the sampled/fetched game count.
- Playgroup game listing requires membership. Missing or invalid API keys should fail clearly without exposing key material.
- "Most powerful" should use Playgroup's own `power_level` and `confidence_factor` first, because that is how Playgroup defines deck power.
- Normal tests must stay offline with fake HTTP.

## Acceptance Criteria

- The server exposes `get_playgroup_auth_status`, `get_playgroup`, `get_playgroup_deck`, `list_playgroup_decks`, `list_playgroup_users`, `list_playgroup_user_decks`, and `rank_playgroup_decks`.
- The server exposes `score_cards_for_playgroup_meta` for local-meta candidate scoring.
- Normal tests pass without network access or real Playgroup credentials.
- Config output and errors redact all Playgroup secrets.
- A user with a valid Playgroup API key can list decks seen in a playgroup URL like `https://playgroup.gg/playgroups/49295-heaters`.
- Ranking output explains which metric was used and flags low-confidence power estimates.
