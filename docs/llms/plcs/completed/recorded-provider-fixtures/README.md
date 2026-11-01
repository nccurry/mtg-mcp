# Recorded Provider Fixtures Plan

## Current Work

- Status: Complete
- Owner: mtg-mcp (owner approved 2026-11-01)
- Last updated: 2026-11-01, `main` at `fc2f32a`
- Selected work and endpoint: phases 1 to 4 below, merged into `main`. No
  release is required for the fixtures; the Archidekt card-count and
  subfolder fixes are under `Unreleased` in `CHANGELOG.md`.
- State: every requirement merged into `main` (`4754596`, `fc2f32a`);
  `task ci` passed on the merged branch.
- Where: `main`; the worktree and branch were removed.
- Next action and blocker: none.
- Decisions made during implementation:
  - POST is allowed only for Scryfall `/cards/collection`, a read-only lookup.
    Owner confirmed 2026-11-01 (D-3).
  - Sol Ring replaces Assassin's Trophy as the paging recording, because
    paging needs more than 25 printings.
  - Six legacy Scryfall card fixtures keep their prices and use
    `refresh: false`.
  - Archidekt deck list, folder, and snapshot recordings use the owner's
    login through `auth: true` entries (D-4).
  - `FakeArchidekt` list, folder, and snapshot responses now follow the
    recorded shapes; the adapter now reads `subfolders` in a folder read and
    takes a deck summary's card count from `size`.
  - The recorded deck's `parentFolder` is zeroed with the owner id.
  - Deck 22959335 is public, so its id identifies the owner; accepted under
    D-1.
- Resume check: `git -C ../mtg-mcp-wt/recorded-provider-fixtures/main log --oneline main..`.

## Outcome And Requirements

Recent bugs passed every test because the test doubles were hand-written from
our own assumptions about the providers:

- `FakeArchidekt` returned one printing per card name with a top-level
  `setCode`; real Archidekt returns many printings (Assassin's Trophy: 18),
  puts the set under `edition.editioncode`, and pages results. Push failed live.
- No fixture had a category with `includedInDeck: false`, so the zone mapping
  that ignored it went unseen (deck 22959335 read as 103 cards, not 100).
- The MCP output-schema bug is already covered by
  `tests/MtgMcp.E2E.Tests/ToolProtocolMcpTests.cs` (added by
  `deck-read-and-search-clarity`); this plan does not repeat it.

The outcome: tests read real provider responses, recorded once with
provenance, and the hand-written Archidekt fake is checked against them.

| ID | Required behavior | Acceptance criteria |
| --- | --- | --- |
| REQ-001 | Recorded responses live in the adapter test project that owns the provider, under `Fixtures/recorded/<route>/<case>.json`, with one `manifest.json` per adapter. Each entry records id, method, source URL, route, status, file, capture date, SHA-256 of the stored bytes, and what was trimmed or scrubbed. | A test hashes every file against its manifest entry and fails on any mismatch or unlisted file. |
| REQ-002 | One `ReplayHttpHandler` serves recordings by method, path, and sorted decoded query (host ignored), records every request, and throws on an unmatched request with the request and the known routes. Tests may add a synthetic response (for example a 429) per route. | Handler unit tests: a match, a miss listing routes, query order ignored, an override. |
| REQ-003 | `scripts/fixtures.ps1 Refresh` re-fetches every manifest entry with GET only (refuses other methods, except POST to Scryfall `/cards/collection`, a read-only lookup (D-3), and the Archidekt login POST, whose response is never recorded (D-4)), paces requests, scrubs personal fields (usernames, avatars, profile data) and volatile fields (Scryfall `prices`, image URIs), writes deterministic LF-formatted JSON, and rewrites the hashes. `task fixtures:refresh` calls it. It never runs in `task test` or CI. | Running it twice with no provider change leaves `git diff` empty; a scrub test finds no usernames in Archidekt recordings. |
| REQ-004 | Scryfall recordings: a search with `has_more` and `next_page` (two pages), a collection lookup, a named-card lookup, and the 404 error body. The six existing card fixtures move under the manifest. | Adapter tests parse each; a paging test follows the recorded `next_page`; the 404 maps to `not-found`. |
| REQ-005 | Archidekt recordings capture response shapes, not whole decks: card search for a card with many printings, including the `count` and `next` pages; one of the owner's decks trimmed to a handful of entries, keeping a category whose `includedInDeck` is false, a commander, and a card in two categories; the deck list; folder and snapshot reads. | Adapter tests parse each; printings per name and the set field come from the recording; the excluded category maps out of `main`; the trimmed deck file stays under 50 KB. |
| REQ-006 | Scenario tests use small mock decks built in the test through `FakeArchidekt` (only the cards each scenario needs), run offline through pull, local read, push preview, and push apply. The mock decks are realistic because REQ-007 holds the fake to the recorded shapes. No real mutation. | Scenarios: an excluded-category entry is left out of the in-deck count; a card in two categories is reported once per entry; push preview of an unchanged pulled deck has no operations; name-only adds resolve a printing from the recorded search. |
| REQ-007 | A contract test runs the same shape assertions over `FakeArchidekt` output and the matching recordings: key paths and value kinds, array-size buckets (0, 1, many), paging keys, and category flags. The fake may omit fields the adapter ignores only when listed in the test. | The test fails when the fake drops or renames a field the recording has, or returns one printing where the recording has many. |

Non-goals: local mock HTTP servers, scenario-file runners, scheduled live
checks, recordings for Playgroup, Spellbook, or Scryfall bulk data, and full
MCP `schema.json` validation. Existing bespoke handlers stay; tests move to the
replay handler only when touched.

## Current System And Design

Inspected at `e522c2e` (research report, 2026-11-01).

- The repo has 14 separate test HTTP handlers (for example
  `ArchidektTestHttpHandler.cs:10`, `ScryfallTestFixture.cs:441`,
  `SpellbookTestHttpHandler.cs:9`) and the stateful `FakeArchidekt.cs`, which
  throws on an unknown route.
- Fixtures are copied to output, not embedded
  (`MtgMcp.Scryfall.Tests.csproj:21`). App tests reuse adapter test files
  through linked includes (`MtgMcp.App.Tests.csproj:21-28`); this plan uses the
  same pattern for the handler, loader, and recordings.
- No shared test-support project: AGENTS.md keeps one test project per
  production project, and linked files already share code without one.
- Six recorded Scryfall cards exist with no provenance
  (`tests/MtgMcp.Scryfall.Tests/Fixtures/cards/`). Decks 22959335 and 27968076
  are only hand-built look-alikes (`ArchidektDeckReadTests.cs:412`,
  `ArchidektWorkflowTests.cs:257`).

Design rules:

- **Extensible:** a new route or provider is new JSON files plus manifest
  entries. The csproj wildcard picks the files up, and hash and shape tests
  read manifest ids through `TheoryData<string>`, so no other test changes.
- **Loosely coupled:** the handler knows HTTP only; the loader knows the
  manifest only; adapter tests know their routes. Provider HTTP contracts stay
  in the adapter test projects.
- **Shape, not bytes:** tests compare shapes through a small `JsonShape`
  helper (sorted path-and-kind pairs with array-size buckets), compared as
  subsets, never exact JSON, so a refresh that adds fields does not break tests.
- **Repo C# and test conventions:** `Method_Condition_Result` names, XML
  summaries on every member, `TestContext.Current.CancellationToken`,
  `ConfigureAwait(false)` in helpers, manifests loaded once through a static
  lazy, `IClassFixture` only for the App replay setup, deterministic ordering.
- **Tasks and CI:** one new target, `fixtures:refresh`. The new tests run in
  `test:unit` and `ci` like any offline test, so CI needs no change. Coverage
  gates can only rise.

## Steps And Verification

1. Phase 1, infrastructure and Scryfall (REQ-001 to REQ-004), about a day.
2. Phase 2, Archidekt recordings (REQ-005), one to one and a half days.
3. Phase 3, mock-deck scenarios (REQ-006), about a day.
4. Phase 4, fake contract test (REQ-007), about half a day.

Each phase: focused tests, then `task ci`, `task test:integration`, and
`task test:e2e` from PowerShell. Phases run in order; phase 3 needs phase 2's
recordings. Update `tests/AGENTS.md` with how to add a recording and when to
refresh, and `docs/llms/repo-operations.md` with the new target.

## Risks And Open Questions

- **D-1 (owner, answered 2026-11-01):** decks 22959335 and 27968076 are the
  owner's and may be recorded. Other users' decks stay out, because
  Archidekt's site terms forbid harvesting data about other users; usernames
  and avatars are still scrubbed.
- **D-2 (owner, 2026-11-01):** recordings capture shape only. Scenarios use
  small mock decks built on that real shape, not whole recorded decks. Add a
  whole real deck later only if a bug depends on a full deck's contents.
- **D-3 (owner, 2026-11-01):** the refresh script may POST to Scryfall
  `/cards/collection`, a read-only lookup Scryfall offers only as POST. Every
  other non-GET request is refused.
- **D-4 (owner, 2026-11-01):** the refresh script may also POST once to
  Archidekt `/api/rest-auth/login/` to read the owner's login-only routes
  (deck list, folders, snapshots) with the credentials file the server uses.
  The login response, token, and credentials are never recorded, logged, or
  printed, and only GET provider routes follow it.
- Scryfall allows use of its API data with attribution, and the repo already
  stores card JSON; confirm against scryfall.com/docs/api before phase 1.
- Size: a whole deck response is large (22959335 is about 1.3 MB), so the
  refresh script trims a recorded deck to the listed entries and drops fields
  the adapter ignores.
- Staleness: refresh is manual (`task fixtures:refresh`), run after a provider
  bug report or before a release. The `git diff` of a refresh shows what moved.

## Evidence And Completion

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 to REQ-004 | merged (`4754596`) | Phase 1, `fd67b83`: replay helpers, Scryfall recordings, refresh script. REQ-003: POST `/cards/collection` allowed by D-3. Phase 1 reported that a second live refresh left `git diff` empty for Scryfall; Archidekt was not refreshed twice, so the double-refresh criterion is not shown for it. Audit fixes `f5ccce0`: refresh compares against the manifest hash, stages writes, and has offline tests of its helpers. |
| REQ-005 | merged (`fc2f32a`) | Phase 2, `1e88926`: Sol Ring search pages and trimmed deck 22959335. Login-only recordings (D-4), 6 more files: deck list (3 of 58 rows), folder tree, folder reads 401005 (nested, parent object, 2 decks) and 397567 (root, 5 subfolders, no decks), snapshot list of 22959335 (4 rows), and snapshot 152482 (5 cards, 44 categories, 37 KB). Scrubbed: owner ids, deck text, folder ids, usernames, avatars, view counts; deck-row edit time, size, and color counts are fixed and rows are kept in id order, because the owner edits decks often. Adapter tests parse each; the adapter did not read `subfolders` in a folder read, now fixed. A second refresh left `git diff` empty. |
| REQ-006 | merged (`4754596`) | Phase 3, `ba55005`: mock-deck scenarios over the recorded Sol Ring search. |
| REQ-007 | merged (`4754596`) | Phase 4, `d078c2c`: `ArchidektFakeContractTests` compares `FakeArchidekt` output with the recorded search pages and deck by key path, value kind, array-size bucket, paging keys, and category flags; the fake gained `id`, `modifier`, `isPremier`, `unlisted`, `updatedAt`, `parentFolder`, and a `previous` link. Breaking the fake made all three tests fail. `task lint`, `task test:integration`, `task test:e2e`, and `task ci` passed. Login-only follow-up: the fake's deck list now returns `count`, `next`, and `results` with full rows, the folder tree is a root with nested `children` and null leaves, a folder read has `parentFolder`, `owner`, `subfolders`, and full deck rows, the snapshot list is a bare array, and a snapshot read is the flat saved deck with `snapshotMeta`; five new contract tests cover them against the recordings. |
