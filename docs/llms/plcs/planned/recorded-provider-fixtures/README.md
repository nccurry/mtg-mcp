# Recorded Provider Fixtures Plan

## Current Work

- Status: Planned
- Owner: mtg-mcp (owner approval needed before implementation)
- Last updated: 2026-11-01, `main` at `e522c2e`
- Selected work and endpoint: phases 1 to 4 below, merged into `main`. No
  release is required; this changes tests, fixtures, one script, and one Task
  target.
- State: designed; not approved; implementation not started.
- Next action and blocker: owner answers D-1 and approves.
- Resume check: this file is under `docs/llms/plcs/planned/`.

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
| REQ-003 | `scripts/fixtures.ps1 Refresh` re-fetches every manifest entry with GET only (refuses other methods), paces requests, scrubs personal fields (usernames, avatars, profile data) and volatile fields (Scryfall `prices`, image URIs), writes deterministic LF-formatted JSON, and rewrites the hashes. `task fixtures:refresh` calls it. It never runs in `task test` or CI. | Running it twice with no provider change leaves `git diff` empty; a scrub test finds no usernames in Archidekt recordings. |
| REQ-004 | Scryfall recordings: a search with `has_more` and `next_page` (two pages), a collection lookup, a named-card lookup, and the 404 error body. The six existing card fixtures move under the manifest. | Adapter tests parse each; a paging test follows the recorded `next_page`; the 404 maps to `not-found`. |
| REQ-005 | Archidekt recordings: card search for a card with many printings, including the `count` and `next` pages; a deck with a category whose `includedInDeck` is false; the deck list; folder and snapshot reads. | Adapter tests parse each; printings per name and the set field come from the recording; the excluded category maps out of `main`. |
| REQ-006 | Replay tests run recorded decks through pull, local read, push preview, and push apply offline. Writes go through synthetic overrides or the seeded fake; no real mutation. | A recorded deck reads with Archidekt's own count; push preview of an unchanged pulled deck has no operations; name-only adds resolve a printing from the recorded search. |
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
3. Phase 3, replay of recorded decks (REQ-006), about a day.
4. Phase 4, fake contract test (REQ-007), about half a day.

Each phase: focused tests, then `task ci`, `task test:integration`, and
`task test:e2e` from PowerShell. Phases run in order; phase 3 needs phase 2's
recordings. Update `tests/AGENTS.md` with how to add a recording and when to
refresh, and `docs/llms/repo-operations.md` with the new target.

## Risks And Open Questions

- **D-1 (owner, blocks phase 2):** which Archidekt decks to record. Archidekt
  publishes no API terms, and its site terms forbid harvesting data about
  other users. Proposed: record only decks you own (or have the owner's
  consent for), scrub usernames and avatars, and replace other users' decks
  with your own copy. Confirm whether 22959335 and 27968076 are yours.
- Scryfall allows use of its API data with attribution, and the repo already
  stores card JSON; confirm against scryfall.com/docs/api before phase 1.
- Size: deck 22959335 is about 1.3 MB. Keep whole decks when a bug depended on
  every card; drop fields the adapter ignores.
- Staleness: refresh is manual (`task fixtures:refresh`), run after a provider
  bug report or before a release. The `git diff` of a refresh shows what moved.

## Evidence And Completion

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 to REQ-007 | planned | Test-strategy research of 2026-11-01; no provider requests made yet. |
