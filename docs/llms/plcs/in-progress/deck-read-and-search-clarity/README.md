# Deck Read And Search Clarity Plan

## Current Work

- Status: In progress
- Owner: mtg-mcp (Opus parent session with Sonnet implementation children)
- Last updated: 2026-10-31, `main` at `f0c0814`
- Implementation authorized: Yes (owner, 2026-10-31: "Ride with the plan")
- Selected work and endpoint: phases 0 to 7 (and any later phases added from new reports), merged into `main` on this host,
  audited, released together as `0.13.1`, and installed (owner, 2026-10-31:
  "fix everything identified then release 0.13.1, then install it").
- State: phases 0 to 7 implemented and merged into `drsc/integ`; docs done
  (README, CHANGELOG, `docs/output-control.md`, tool text); full audit next.
  Checks on the final merged commit are pending.
- Standing instruction (owner, 2026-10-31): other sessions will keep sending
  mtg-mcp bugs; research each, add it here as a phase, fix it, and ship it in
  `0.13.1`.
- Next action and blocker: full audit, then merge to `main`, release, and
  install. No blocker.
- Resume check: `git worktree list` matches the table below.

Where the work lives (a worktree lock, not this table, holds a checkout
against cleanup):

| Checkout | Branch | Owner | State |
| --- | --- | --- | --- |
| `mtg-mcp-wt/deck-read-and-search-clarity/integ` | `drsc/integ` | parent | integration |
| `mtg-mcp-wt/deck-read-and-search-clarity/p0-protocol` | `drsc/p0-protocol` | Sonnet child | phase 0 |
| `mtg-mcp-wt/deck-read-and-search-clarity/p1-indeck` | `drsc/p1-indeck` | Sonnet child | phase 1, then phase 2 |
| `mtg-mcp-wt/deck-read-and-search-clarity/p34-scryfall` | `drsc/p34-scryfall` | Sonnet child | phases 3 and 4, merged into integ |
| `mtg-mcp-wt/deck-read-and-search-clarity/p5-push` | `drsc/p5-push` | Sonnet child | phase 5 |

## Outcome And Requirements

An agent using `0.13.0` (2026-10-31) reported four problems while building a
Commander deck from Archidekt deck 22959335 (417 entries: 101 main, 2
commander, 21 sideboard, 347 maybeboard):

1. `archidekt_deck_get` holds about 75 entries per page, sorted by name across
   every zone, so the real deck takes about 6 pages, mostly maybeboard. Each
   entry carries fields that rarely matter for deckbuilding.
2. The deck reads as 103 main plus commander cards with no warning.
3. `scryfall_search` with `pageSize: 100` returned 87 cards with
   `truncated: true` and gave no numbers to plan with.
4. There is no commander popularity rank, so "commanders not in the top 100"
   cannot be answered.

A second session (2026-10-31) reported that Claude Code loads none of the 86
tools, and asked for commander research tools. Verified in Claude Code's MCP
logs for this project: every session that negotiated protocol `2026-07-28`
logged "tools/list failed … outputSchema expected object, received boolean";
sessions on `2025-11-25` did not.

The same session then reported that `archidekt_push_preview` approved 27
name-only adds (card data not installed) and `archidekt_push_apply` failed on
the first ("unknown: Archidekt could not resolve the exact requested
printing"), marking the other 26 not attempted; nothing reached Archidekt.

A third report (2026-10-31, local deck bound to Archidekt deck 27968076):
after a successful push, the user edited the deck on Archidekt (a new Removal
category; Assassin's Trophy and Pick Your Poison moved from Sideboard to main
under Removal) while the agent added 33 entries locally. `archidekt_push_preview`
reported `conflictCount: 2` (remote-added 1, remote-changed 1, local-added 33)
but `applyAvailable: true`, and its operations would have deleted the Removal
category and moved both cards back, although only the remote had changed them
(baseline equals local). That is a data-loss risk.

A fourth report (2026-10-31, same deck) said a pull dropped every remote entry
past the first page (172 on Archidekt, 79 locally), causing 17 cards to be
pushed four times. Research found no sync data loss: the local store held all
172 entries and 184 cards. `local_deck_get` returned a size-cut first page (79
entries, `truncated: true`; rows ordered commander, main, maybeboard,
sideboard), which the agent took for the whole deck and re-added cards from.

Research (2026-10-31) found that problem 2 is mostly a zone-mapping gap, not
duplicates. Archidekt counts an entry toward the deck only when its first
category has `includedInDeck`. `MapZone`
(`src/MtgMcp.Archidekt/ArchidektContractMapper.cs:448-472`) ignores that flag
and maps every entry that is not commander, sideboard, or maybeboard to `main`.
In deck 22959335, three main entries have an excluded first category
(Bloodthirsty Blade under Multiplayer, Revival // Revenge and Osteomancer
Adept under Reanimate). Counted Archidekt's way, the deck has exactly 100
cards. The owner notes those categories are a deck setup mistake on
Archidekt, but mtg-mcp must follow the deck's configuration either way. One
Bloodthirsty Blade is also a real second entry, under Removal.

| ID | Required behavior | Acceptance criteria |
| --- | --- | --- |
| REQ-011 | Every tool publishes an `outputSchema` that is a JSON object with `type: "object"` under every supported protocol version, including `2026-07-28`: `{"type":"object","properties":{"kind":{"type":"string","enum":[…the operation result kinds…]}},"required":["kind"]}`, set once where every tool is created (`ToolRegistration.cs:52`). Each `structuredContent` is the unwrapped result with a top-level `kind`. | Claude Code (protocol `2026-07-28`) lists all tools; E2E lists tools under 2024-11-05, 2025-03-26, 2025-06-18, 2025-11-25, and 2026-07-28 (through `server/discover`) and every `inputSchema` and `outputSchema` is an object with `type: "object"`. |
| REQ-012 | A tool failure sets `isError: true` under every protocol version. | E2E under each version: `local_deck_get` with an unknown ID has `isError: true` and a top-level `kind`; `scryfall_card_data_status` succeeds with a top-level `kind` that is in the schema's enum. |
| REQ-013 | Push resolves an add's Archidekt printing in one adapter resolver, from Archidekt's card search: `uid` equals `printingId`; else `edition.editioncode` (falling back to `setCode`) and collector number; else, for an entry without a printing, the first listed exact-name match (filtered by `oracleCard.uid` when an Oracle ID is set). The search sends `exact=true` (a plain `name=` search matches longer names too) and, for an entry with a set code, `edition=<set>`; a first page with no result is asked again without `exact=true` for double-faced names. A requested printing not on the first search page is followed through `next` within the request budget (12 pages for one search). An entry with a printing ID that no result has stays unresolved; it never takes the first printing. | Fake Archidekt with real result shapes (several printings per name, `edition.editioncode`, `count` and `next`, substring matching without `exact`, the `edition` filter): a name with 18 printings resolves to the first listed; set plus number matches; an Oracle ID filters; a printing on page 2 resolves; a set printing of a 955-printing name resolves through `edition`; "Shock" is not confused with longer names. |
| REQ-014 | `archidekt_push_preview` resolves every add without a provider card ID with the same resolver, names the chosen printing in the operation detail, and sets `applyAvailable: false` with a notice naming the cards when a name has no match or the requests would pass the 150-request budget. Apply uses the preview's resolution, so anything the preview approves, apply can resolve. | Workflow test reproducing the report: 27 name-only sideboard adds, no card data, one card with 18 printings: preview shows each chosen printing, apply gives `applied` with clean verification. An unknown name gives `applyAvailable: false` and apply refuses with zero writes. |
| REQ-015 | Apply resolves every add before its first write; a resolution failure refuses the push with no writes. An operation that failed before anything was sent reports `not-sent` ("Nothing was sent to Archidekt for this operation."), `unknown` is kept for a failure after a write was sent, and a push where nothing was applied reports outcome `not-applied`. | ApplyPlan tests: a lookup failure on the first operation gives `not-sent` and `not-applied`, never `unknown`; a failure during a write still gives `unknown`. |
| REQ-016 | The add-time notice for entries added without card data no longer says reconcile makes them pushable; it says push picks Archidekt's default printing unless a printing is set. | Message test. |
| REQ-017 | A push preview whose remote side changed since the last sync (`hasConflicts`) sets `applyAvailable: false`, lists no operations, and its notice says: "archidekt_push_apply would refuse this push: the Archidekt deck changed since the last sync (N rows; see differences with remote-* states). Run archidekt_pull_preview first; a pull keeps cards added locally when those are your only local changes. Then push again." The mode and credentials notice comes first when it applies. | Workflow test with the fake Archidekt reproducing the report (push; remote adds Removal and moves two Sideboard cards to it; local adds 3): push preview has `hasConflicts`, `applyAvailable: false`, the notice, no operations; push apply returns `push-conflict` with no writes. |
| REQ-018 | A pull keeps local-only additions: when every local change since the last sync is an added entry or category, the pull preview is not a conflict, shows a `local-keep` operation ("Keep N cards added locally since the last sync; push them afterwards."), and apply replaces the rest from Archidekt, then adds the kept entries, their categories (created by exact name when missing), and assignments; the new baseline leaves them `local-added` so the next push adds them. A local add that matches a card the remote also added, or a kept card whose category gives a zone contradiction, is refused. Any other local change still needs `discardLocalChanges`. | Same setup: pull without the flag succeeds; the local deck has the two moved cards in main under Removal plus the 3 local adds; the next push preview is exactly 3 `entry-add`. A local change to a card the remote also changed gives `pull-conflict`; a duplicate add on both sides is refused. |
| REQ-019 | A pull preview with `discardLocalChanges: true` says what is thrown away: "Replace the local deck with the Archidekt deck. This discards local changes made since the last sync: N cards added, N changed, N removed (categories: …). They are listed in differences with local-* states." | Pull preview test with local adds, changes, and removals checks the counts in the text. |
| REQ-020 | Every `local_deck_get` page carries deck-wide totals (`entryCount`, `cardCount`, `cardsByZone`), and a cut page's `narrow` says it is one page of the deck ("This page shows N of M entries; the deck is not missing the rest. Follow nextCursor."). `local_deck_apply_changes` summaries carry the same deck-wide counts. | A 172-entry deck (99 sideboard rows): page 1 is cut, carries 172/184 and the zone counts, and following `nextCursor` reaches all 172. |
| REQ-021 | `archidekt_push_preview` and `archidekt_push_apply` refuse a plan that removes more than 10 entries, or 3 or more entries that are more than 10% of the remote entries, unless the caller passes `confirmRemovals` equal to the planned removal count; the preview notice names the count. The apply makes this check, and the revision and fingerprint checks, before any card search. | Push from a local deck missing 93 of 172 rows is refused without `confirmRemovals` and allowed with the exact count; 11 of 200 (count rule alone) and 3 of 25 (percent rule alone) are refused, 10 of 100, 3 of 30, 2 of 15 and 1 of 5 are not; a refused apply sends no search. |
| REQ-022 | Archidekt 429s: a GET waits for Retry-After (at most about 10 s) and retries up to twice; a write that gets a 429 is `not-sent` (Archidekt did not process it) and the rest are not attempted, never retried automatically; a 429 or failure on the verification read after writes keeps the applied statuses and reports `applied-unverified`. A verification mismatch re-reads once after about 1 s and lists the rows that still differ. | Fake transport tests: 429 then success on a GET; 429 on a write gives `not-sent`; 429 on verification gives `applied-unverified` with statuses kept; a lagging read that settles gives `applied`; a real mismatch lists the differing rows. |
| REQ-023 | `archidekt_deck_get` also reports repeated cards outside commander and main (for example the same card four times in the sideboard) as their own group list, separate from the Commander duplicate facts. | A deck with 17 cards four times in the sideboard reports 17 repeat groups. |
| REQ-001 | An Archidekt entry whose first category has `includedInDeck: false` is not in the deck: the zone mapping sends it to `maybeboard` (unless it is a commander, sideboard, or maybeboard entry by the existing rules). The same mapping drives pulls, `archidekt_deck_get`, push planning, and the push zone check (`ArchidektSyncPlanner.cs:515`), so pull and push stay consistent. | Fake Archidekt test copying deck 22959335: the three entries map to `maybeboard`; main plus commander is 100; pull then push preview gives zero operations; a category whose flag is `true` (or missing) still maps to `main`. |
| REQ-002 | Decks pulled before REQ-001 keep working. A stored baseline or local entry with `main` for such an entry does not produce false differences, a refused push, or a changed fingerprint by itself; the next pull moves the local entry to `maybeboard` as an ordinary pull change. | Upgrade test with a `0.13.0`-shaped baseline and local deck: push preview has no zone contradiction and no operations; pull preview shows the zone move for exactly those entries. |
| REQ-003 | Each `archidekt_deck_get` entry reports `inDeck` (true when the first category is included, has no flag, or the entry has no category). | Same fixture: the three entries `false`, others `true`. |
| REQ-004 | The `archidekt_deck_get` summary reports, for the whole deck whatever the filter: duplicate groups across commander and main by Oracle ID, falling back to name, skipping basic lands, capped with an omitted count; and for Commander decks a size fact `{format, expected: 100, mainAndCommander}`. Facts only, no verdict. | Fixture: one duplicate group only if both copies are in the deck; basics not grouped; the cap sets the omitted count; the size fact reads 100. No card data needed. |
| REQ-005 | `archidekt_deck_get` takes an optional `zones` filter (`commander`, `main`, `sideboard`, `maybeboard`, the names `card_list_query` uses) and returns `shownEntryCount`. Offsets count the filtered list. | 417-entry fixture: `zones: ["commander","main"]` returns only those; a bad offset is refused with `invalid-offset` naming the filtered count; an unknown zone is refused by the schema enum. |
| REQ-006 | `archidekt_deck_get` takes `responseFormat` (shared attribute), default `concise`. `concise` entries hold `cardName`, `quantity`, `zone`, `categoryNames`, `setCode`, `collectorNumber`, `oracleId`, `inDeck`. `full` (and `standard`, the same) return every entry field as in `0.13.0` plus `inDeck`. The description's first lines and every cut page's `narrow` say to pass `responseFormat: "full"` for every field. | Main plus commander of the fixture fits one page in the default format; `full` matches `0.13.0` field for field plus `inDeck`; the description and `narrow` name `responseFormat: "full"`. |
| REQ-007 | Entries are ordered by zone (commander, main, sideboard, maybeboard), then name, then relation ID, in every format. | Ordering is stable across the fake's per-read reordering; paging reaches every entry once. |
| REQ-008 | `archidekt_snapshots_get` (kind `get`) takes the same `zones` and `responseFormat` with the same defaults, since it shares the deck pager. | Snapshot read test with both options and the defaults. |
| REQ-009 | `scryfall_search`, `scryfall_cards_get`, and `scryfall_installed_cards_by_tag` report `returnedCount` and `requestedCount` on every page, and `cutReason: "result-size-limit"` when cut. `narrow` gives the numbers: kept and requested counts, average bytes per card, where `nextCursor` continues, and that leaving `oracleText` out of `fields` fits more. `card_lists_compare` uses the same hint builder. | Recorded-fixture tests: a cut concise page reports 87 of 100 for the reporter's field set; an uncut page has no `cutReason`; cursors visit every card once; concise without `oracleText` fills 100. |
| REQ-010 | Descriptions say `pageSize` is a maximum, that `oracleText` is usually the largest field, and that `edhrecRank` counts deck inclusion in any slot and is not a rank of how often a card is played as commander (same for `order: "edhrec"`). | Description tests and the 4,096-character limit pass. |

Non-goals: a commander popularity rank, an EDHREC adapter, or Archidekt public
deck search or derived inclusion rates (see Decisions); an
`oracleTextMaxLength` option; size facts for formats other than Commander.

## Current System And Design

Inspected at `d8b845c` and `86d25fb`.

- Push printing resolution (`ArchidektDeckTransport.SelectExactCard`,
  `ArchidektDeckTransport.cs:379-423`) accepts a name-only add only when
  exactly one search result matches the name (Assassin's Trophy returns 18 on
  Archidekt). Its set and collector-number match reads a top-level `setCode`
  that real results do not have (the set is `edition.editioncode`), and it
  reads only the first search page (Sol Ring: 141 results, 25 per page).
  Reconcile sets only the Oracle ID (`DeckIdentityReconciliation.cs:462-466`),
  so installing card data would not have helped. `ApplyPlanAsync`
  (`ArchidektDeckOperations.cs:266-285`) labels any provider error `unknown`,
  including a failed card lookup before any write. Preview only plans
  (`ArchidektCoordinator.cs:404-478`) and never checks resolution. The fake
  Archidekt gives one printing per name with a top-level `setCode`, so tests
  passed against a shape the real service does not send.
- `applyAvailable` reflects only mode and credentials
  (`ArchidektApplyAvailability.cs:17-35`, `ArchidektResultPaging.PagePreview`).
  The push preview already computes `hasConflicts`
  (`ArchidektCoordinator.cs:452`), and `ApplyPushAsync` refuses a
  conflicting push with `push-conflict` before any write (`:513-519`, tested
  by `ApplyPush_RemoteChangedSinceBaseline_ReturnsPushConflictWithCurrentState`),
  so the reported push would not have reverted Archidekt; the preview misled.
  Push planning is two-way: `ToRemoteTarget` lays the whole local deck over
  the remote (`ArchidektLocalMapper.cs:261-339`) and `PlanRemoteApply`
  (`ArchidektSyncPlanner.cs:326-452`) never reads the baseline, which is why
  the listed operations reverted remote edits. A pull with any local change is
  a conflict (`ArchidektCoordinator.cs:225`), and the discard text has no
  counts (`:228-234`).
- Every tool returns `OperationResult<T>`, which has a custom JSON converter,
  so the schema exporter in ModelContextProtocol 2.2.0 gives the open schema
  `true`. The SDK passes it through under `2026-07-28`; under older versions
  it wraps both the schema and the content in `{"result": …}`
  (`AIFunctionMcpServerTool.cs`). The `2026-07-28` schema
  (`schema/2026-07-28/schema.json`) requires `outputSchema` to be an object,
  so the SDK's boolean is non-conformant and Claude Code rejects the list.
  SDK 2.3.0 behaves the same. Because of the wrapper, `ToolCallPolicy`
  (`ToolCallPolicy.cs:93`) misses `kind` on older protocols and leaves
  `isError` unset on failures. An explicit object schema stops the SDK from
  wrapping, which fixes both (inferred from SDK source; prove it first in
  phase 0). Fallback if that fails: drop a boolean `outputSchema` in
  `FoundationProtocolPolicy.CanonicalizeToolList`, which restores the tools
  but not `isError`. `CardListAndRuleToolsMcpTests.cs:423` missed the bug
  because a C# `JsonElement` holds `true` as happily as an object.

- `MapZone` derives a zone from category names only. It is used when reading
  Archidekt (`ArchidektContractMapper.cs:368`) and when checking a push for
  zone contradictions (`ArchidektSyncPlanner.cs:515`), so changing it changes
  both sides together. Archidekt's own Sideboard and Maybeboard categories are
  themselves categories with `includedInDeck: false`; a custom excluded
  category is the same idea, so it maps to `maybeboard`.
- `archidekt_deck_get` takes `deckId` and `offset`
  (`src/MtgMcp.App/Archidekt/ArchidektReadTools.cs:73-83`). `PageDeck`
  (`ArchidektResultPaging.cs:23-96`) sorts by name and pages with
  `ResultPage.Take`; it is shared with `archidekt_snapshots_get` (`:161-172`).
  Entries are the 14-field `RemoteDeckEntry` (`ArchidektModels.cs:55-69`).
- `zones` already exists on card-list sources (`CardListSourceInput.cs:47-83`).
  Every `responseFormat` parameter must use the shared attribute
  (`ToolAllowedValuesTests.cs:35`), and choice parameters publish an enum.
- `RemoteDeckEntry.OracleId` comes from Archidekt's `oracleCard.uid`, so
  duplicate grouping needs no Scryfall data. The Commander size fact matches
  `CommanderRuleFacts` (`CommanderRuleFacts.cs:145-167`).
- Scryfall pages are cut to 40,000 bytes by `ScryfallCardFormatter.CountAsync`
  (`ScryfallCardFormatter.cs:138-166`); the next cursor resumes exactly after
  the last kept card, from a stored snapshot, without fetching again. The
  `narrow` text is fixed (`ScryfallCardResponses.cs:16-18`). Measured on the
  reporter's query: 436 bytes per card with oracle text (87 fit), 208 without
  (100 fit). Leaving `oracleText` out of `fields` already works.

Decisions (owner, 2026-10-31):

- **D-1, follow `includedInDeck`:** mtg-mcp respects the deck's category
  configuration in pulls, reads, and pushes (REQ-001, REQ-002).
- **D-2, `concise` by default:** `archidekt_deck_get` and
  `archidekt_snapshots_get` default to `concise` now, with `full` easy to ask
  for (REQ-006). Changing a default result is breaking under
  `docs/versioning.md`; the owner approved it for this release, and the
  CHANGELOG marks it **Breaking** with `responseFormat: "full"` as the way
  back.
- **D-3, no oracle-text cap:** report counts instead; shortened oracle text
  hands the agent partial rules text, and a 200-character cap fits only 92 of
  100.
- **D-4, Commander only** for the size fact.
- **No commander research tools** (research, 2026-10-31): EDHREC stats are
  rejected as above. Archidekt public deck search by commander and inclusion
  rates derived from other users' decks are an automated search over other
  users' data, which Archidekt's terms (archidekt.com/terms) forbid and
  `SOURCE_FEASIBILITY.md:23,57-68` records as not admitted. They reopen only
  with written permission from Archidekt or a documented API; the search would
  then be a small `archidekt_*` tool that labels view order as popularity,
  not quality.
- **No commander rank** (research, accepted with the plan): Scryfall's
  `edhrec_rank` counts inclusion in any slot (EDHREC's top commander,
  Y'shtola, Night's Blessed, has rank 2,495). EDHREC's terms forbid automated
  requests, and
  `completed/evidence-first-deckbuilding-evolution/SOURCE_FEASIBILITY.md:44-56`
  already rejected its undocumented JSON routes. REQ-010 corrects the wording
  so agents say the server cannot answer instead of answering wrongly.

## Steps And Verification

6. Phase 6, conflicting pushes and merging pulls (REQ-017 to REQ-019), after
   phase 5 (same push code).
7. Phase 7, reading pages safely and sync guards (REQ-020 to REQ-023), on
   `drsc/integ` after phase 6.
5. Phase 5, push resolution (REQ-013 to REQ-016), after phase 1 (same
   Archidekt files; may run alongside phase 2 in its own worktree branched
   from phase 1).
0. Phase 0, tools load under every protocol (REQ-011, REQ-012): the shared
   output schema, the protocol E2E theory, unit tests that every registered
   tool has an object schema, and an updated
   `CardTools_OutputSchema_IsTheSameOpenSchemaAsOtherTools`. Prove first that
   the SDK stops wrapping on the older protocols. Also file an upstream SDK
   issue (boolean `outputSchema` under `2026-07-28`). Verify in Claude Code
   that all tools load.
1. Phase 1, follow `includedInDeck` (REQ-001, REQ-002, REQ-003): the zone
   mapping, the upgrade handling, `inDeck` on entries. Archidekt adapter and
   sync planner, plus fingerprint and baseline upgrade tests like the
   `0.12.0` baseline test of the previous plan.
2. Phase 2, deck facts and reading (REQ-004 to REQ-008): duplicates and the
   size fact, `zones`, `responseFormat` with `concise` default, zone ordering,
   snapshot pass-through, `narrow` naming the options.
3. Phase 3, Scryfall pages (REQ-009).
4. Phase 4, wording (REQ-010).

Each phase: focused tests with the offline fake Archidekt and recorded
Scryfall fixtures, then `task ci`, `task test:integration`, and `task test:e2e`
from PowerShell; `ToolResultBudgetTests` stays under the ceiling; schema and
surface tests updated; README, CHANGELOG, `docs/output-control.md` updated.
Phase 2 builds on phase 1's `inDeck`, so it follows phase 1. Phases 3 and 4
are independent of 1 and 2 and may run in parallel worktrees.

## Risks And Open Questions

- **D-7 (proposed, non-blocking):** a three-way push (push only what the
  local side changed relative to the baseline, keeping remote-only changes) is
  deferred. With REQ-017 a conflicting push lists no operations and refuses,
  and REQ-018 makes pull-then-push keep local additions, so the reported
  workflow is safe without it.
- The report counted `remote-changed: 1` for two moved cards; the code would
  count 2 if both kept their relation IDs. Unresolved without the preview
  JSON; REQ-017's test asserts the code's count.
- **D-8 (decided in the audit repair, 2026-10-31; owner may change):** the percent rule of REQ-021 applies only from 3
  removals, because with it alone, removing 1 card from any deck under 10 entries needed a confirmation. A push
  that removes 1 or 2 entries never needs `confirmRemovals`; the count rule (more than 10) is unchanged.
- **D-6 (proposed, non-blocking):** the default printing for a name-only add
  is Archidekt's first listed search result (its order looked newest first;
  not verified to match what Archidekt's own site picks). Whether to skip promo
  collector numbers stays open. Owner may change the rule later.
- Phase 5 makes the push preview send card searches (one GET per distinct
  name, about 2 s each with pacing), so a preview with 27 name-only adds takes
  about a minute, and one push holds about 70 new name-only cards at most.

- **D-5 (owner, 2026-10-31):** every phase ships together as `0.13.1`. The
  `concise` default (D-2) is breaking, which `docs/versioning.md` reserves for
  minor releases; the owner chose the patch number, and the CHANGELOG marks it
  **Breaking**.
- Phase 0 changes what clients on older protocols see: the `{"result": …}`
  wrapper goes away, matching what `2026-07-28` clients already get. CHANGELOG
  entry under Fixed.

- REQ-002 is the riskiest part: any change to what a pull produces can make a
  synced deck show differences that are not real. Prove it with a stored
  `0.13.0`-shaped baseline before changing the mapping.
- Mapping excluded categories to `maybeboard` means a push of a local
  `maybeboard` entry keeps its Archidekt category (`ArchidektLocalMapper`
  keeps category names); confirm a round trip never adds Archidekt's own
  Maybeboard category to such an entry.
- Follow-ups found in phases 0 to 7, not fixed in `0.13.1`:
  - `local_deck_get` and `card_list_query` cut pages carry a generic narrow hint without counts for the card
    fields (not reported by a user).
  - A name-only add for a foil or etched entry takes the first listed Archidekt printing even when that printing
    lacks the finish.
  - A set code without a collector number takes the first printing in that set.
  - An entry with only a printing ID (no set code) is looked for through the name search alone, which reads at most 12
    pages (300 printings); a basic land whose printing is past that stays unresolved until the entry has a set code.
  - A double-faced card named by its front face only takes a second search when the exact-name search finds nothing;
    that behavior of Archidekt's `exact=true` was not checked live.
  - The ModelContextProtocol SDK still publishes a boolean `outputSchema` under `2026-07-28` for a type with a
    custom converter. The upstream issue is not filed; the owner decides whether to file it. `0.13.1` works around
    it with one shared object schema.
- After Scryfall's 24-hour snapshot lifetime, an old cursor fails as
  `invalid-cursor` (inferred from `ScryfallService.cs:1588`); REQ-009's
  `narrow` should not promise more.

## Evidence And Completion

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 | implemented; checks on the merged commit pending | `c2e04ed` (`ArchidektContractMapper.MapZone`, `IsInDeck`). `ArchidektWorkflowTests`: `DeckLikeTheRealDeck_ExcludedFirstCategoriesAreMaybeboard_AndPullThenPushIsClean`, `MapZone_FollowsTheFirstCategoryFlag`, `IsInDeck_IsFalseOnlyWhenTheFirstCategoryIsFlaggedOut`. |
| REQ-002 | implemented; checks on the merged commit pending | `c2e04ed` (fingerprint recompute reads the current zone; `ArchidektSyncPlanner.FindStaleZoneMoves`); `0bc20b4` (the diff compares the baseline with current zones, a push writes the corrected zones into the local entries, and the primary category is read first everywhere). `DeckPulledByTheEarlierVersion_ShowsNoFalseDifferenceAndTheNextPullMovesTheEntries`, `DeckPulledByTheEarlierVersion_PushesTwiceWithoutAZoneContradiction`, `DeckPulledByTheEarlierVersion_AddingACardThenPulling_KeepsTheCardWithoutAConflict`, `MapZoneAndIsInDeck_ReadThePrimaryCategoryFirst`, `FindZoneContradictions_ReadsThePrimaryCategoryFirst`. |
| REQ-003 | implemented; checks on the merged commit pending | `c2e04ed`. The `inDeck` assertions in `ArchidektWorkflowTests` and `ArchidektDeckReadTests`. |
| REQ-004 | implemented; checks on the merged commit pending | `0f710cf`. `ArchidektDeckReadTests`: `Summary_HasTheDuplicatesAndTheSizeFact_ForTheWholeDeck`, `Duplicates_AcrossCommanderAndMain_AreGrouped_AndTheListIsCapped`, `Duplicates_WithoutOracleId_FallBackToTheName_AndOnlyCommanderGetsTheSizeFact`. |
| REQ-005 | implemented; checks on the merged commit pending | `0f710cf`. `ZonesFilter_ShowsTheChosenZones_AndOffsetsCountTheFilteredList`, `UnknownFormatOrZone_IsRefusedWithTheArgument`, `ZonesArgument_PublishesAnEnumOnItsItems`. |
| REQ-006 | implemented; checks on the merged commit pending | `0f710cf`. `ConciseDefault_CommanderAndMain_FitOnePage`, `ResponseFormat_ChoosesTheEntryFields`, `DescriptionAndNarrow_NameResponseFormatFullAndZones`. |
| REQ-007 | implemented; checks on the merged commit pending | `0f710cf`. `Entries_AreOrderedByZoneNameAndRelation_AndEveryEntryIsReachedOnce`. |
| REQ-008 | implemented; checks on the merged commit pending | `0f710cf`. `SnapshotGet_TakesTheSameZonesAndResponseFormat`. |
| REQ-009 | implemented; checks on the merged commit pending | `1578b82` (`ScryfallCutPageHint`, `ScryfallCardResponses`, `CardListCoordinator`). `ScryfallCardFormatTests`: `Search_CutConcisePage_ReportsItsCountsAndTheCutReason`, `Search_UncutPage_ReportsCountsWithoutCutFields`, `Search_ConciseWithoutOracleText_FillsAPageTheFullTextVersionCuts`, `Collection_CutPage_ReportsItsCountsAndTheCutReason`; `CardListCompareTests`; `CardsByTag_ConcisePageOverTheLimit_IsCutAndContinuesInOrder` asserts `returnedCount`, `requestedCount`, and `cutReason` for `scryfall_installed_cards_by_tag`. |
| REQ-010 | implemented; checks on the merged commit pending | `1578b82`. `EdhrecRank_Descriptions_SayItIsDeckInclusionNotACommanderRank`; the 4,096-character architecture test. |
| REQ-011 | implemented; checks on the merged commit pending | `4ac23c6` (`OperationResultSchema`, `ToolRegistration`). `ToolOutputSchemaTests`: `EveryRegisteredTool_PublishesTheSharedObjectOutputSchema`, `SharedSchema_ListsEveryOperationResultKind`; E2E `ToolProtocolMcpTests.ToolList_AtEachProtocolVersion_PublishesObjectSchemas`. Claude Code load not re-checked yet. Claude Code MCP logs (2026-10-31T14-59 to 15-11) and a stdio reproduction against installed 0.13.0 showed the fault. |
| REQ-012 | implemented; checks on the merged commit pending | `4ac23c6`. E2E `ToolCall_UnknownDeck_SetsIsErrorAndReturnsTopLevelKind`, `ToolCall_Success_ReturnsTopLevelKindFromTheSchema`. |
| REQ-013 | implemented; checks on the merged commit pending | `5d764bf` (`ArchidektPrintingResolver`); `9211055` (exact name, set filter, one card reader in `ArchidektJsonContract.ReadPrinting`). `ArchidektPrintingResolverTests`: `NameOnlyEntry_TakesTheFirstListedPrintingAndNamesIt`, `PrintingId_MatchesTheResultUid`, `SetAndCollectorNumber_MatchTheEditionCode`, `OracleId_FiltersTheNameMatches`, `PrintingOnPageTwo_IsFoundThroughTheNextLink`, `PrintingIdNotListed_IsUnresolvedAndNotTheDefault`, `PrintingIdOnPageTwo_WinsOverASetMatchOnPageOne`, `PrintingNeverFound_StopsAtTwelvePages`, `RequestLimitOnPageTwo_StopsTheSearchAndLeavesTheCardUnresolved`, `ResultOfAnotherCardWithALongerName_IsSkippedAndNotCounted`, `SetPrintingOfACardWithManyPrintings_IsFoundThroughTheSetFilter`, `ExactSearchWithNoResult_IsAskedAgainWithoutTheExactFlag`, `ResultWithBothSetCodes_IsReadByTheEditionCode`; `ArchidektWorkflowTests`: `SetPrintingOnPageTwoOfTheSearch_IsPreviewedAndPushed`, `SetPrintingOfACardWithManyPrintings_IsFoundThroughTheSetFilter`, `NameOnlyAdd_OfANameThatOtherCardNamesContain_PicksTheCardOfThatName`. |
| REQ-014 | implemented; checks on the merged commit pending | `5d764bf`. `NameOnlySideboardAdds_PushWithArchidektsDefaultPrinting_AndTheNextPreviewIsClean`, `NameWithNoArchidektMatch_BlocksThePushAndSendsNothing`, `PushNeedingMoreRequestsThanTheLimit_IsBlockedAndSendsNothing`. |
| REQ-015 | implemented; checks on the merged commit pending | `5d764bf`. `ApplyPlan_CardWithNoPrinting_SendsNothingAndIsNotApplied`, `ApplyPlan_SearchFailureBeforeAnyWrite_IsNotSent`, `ApplyPlan_FailureAfterAWriteStarted_IsUnknown`. |
| REQ-016 | implemented; checks on the merged commit pending | `5d764bf`. `AddByNameScenarioTests`, `DeckWriteSummaryTests`, `DeckEntryResolverTests` (message text). |
| REQ-017 | implemented; checks on the merged commit pending | `7d40eba`; notice wording fixed in the docs pass. `PushPreview_AfterTheRemoteChanged_OffersNoApplyAndNoOperations`, `PushPreview_AfterTheRemoteChanged_InLocalMode_PutsTheModeNoticeFirst`. Report from a live session (deck 27968076). |
| REQ-018 | implemented; checks on the merged commit pending | `7d40eba`; `6b6ec8d` (a kept card follows a category Archidekt renamed; `PlanPullKeep` moved to `ArchidektSyncPlanner`). `Pull_WithACardAddedToACategoryArchidektRenamed_MovesTheCardToTheNewName`, `Pull_WithAnAddedCardAndALocalRemovalOfAnotherCard_IsAConflict`, `Pull_WithAnAddedCardAndAQuantityEditOfAnotherCard_IsAConflict`, `Pull_AfterTheRemoteChanged_KeepsLocalAdditionsAndTheNextPushAddsOnlyThem`, `Pull_WithALocalCategory_KeepsItAndTheNextPushCreatesIt`, `Pull_WithALocalChangeToACardTheRemoteChanged_IsAConflict`, `Pull_WithACardBothSidesAdded_IsRefused`, `Pull_WithAKeptCardWhoseCategoriesGiveAnotherZone_IsRefused`. |
| REQ-019 | implemented; checks on the merged commit pending | `7d40eba`. `PullPreview_WithDiscard_CountsWhatIsThrownAway`. |
| REQ-020 | implemented; checks on the merged commit pending | `e51aabf` (`DeckZoneCounts`, `DeckCardViewCoordinator`). `DeckCardViewTests.Get_LargeSideboardDeck_EveryPageCarriesDeckWideTotals`; `DeckWriteSummaryTests`. Fourth report; read-only copy of the local store (deck 01a1929f: 172 entries, 184 cards) and one public GET of deck 27968076. |
| REQ-021 | implemented; checks on the merged commit pending | `8cf53f4` (`ArchidektRemovalGuard`); `3ef7229` (checks before searching, the small-deck rule of D-8, `push-preview-changed` when the local deck changes during the apply). `Push_RemovingMostOfTheArchidektDeck_NeedsTheExactRemovalCount`, `Push_RemovingAFewEntriesFromALargeDeck_NeedsNoConfirmation`, `Push_Removals_NeedConfirmationOnlyPastTheCountOrPercentRule`, `ApplyPush_WithUnconfirmedRemovalsAndANameOnlyAdd_SendsNoCardSearch`, `ApplyPush_WhenTheLocalDeckChangesDuringTheSearches_SendsNothing`; E2E expectation in `f773557`. |
| REQ-022 | implemented; checks on the merged commit pending | `bb5956f`. `ArchidektRateLimitAndVerificationTests`: `Read_WithARateLimitThenSuccess_WaitsAndReturnsTheDeck`, `Write_WithARateLimit_IsNotSentAndNotRetried`, `FinalRead_ThatFailsAfterTheWrites_KeepsTheStatusesAndIsAppliedUnverified`, `FinalRead_ThatLagsThenSettles_IsApplied`, `FinalRead_WithARealDifference_ListsTheRowsThatDiffer`; `ea0c129` (the request limit ends the verifying read; the plan budget counts every read and its retries): `VerificationRead_ThatHitsTheRequestLimitAfterTheWrites_KeepsTheStatuses`, `PlanPush_CountsTheReadsTheApplyMakesAndTheirRetries`; `3ef7229`: `ApplyPush_WhenTheReadAfterTheWritesFails_KeepsStatusesAndLeavesTheBaseline`. |
| REQ-023 | implemented; checks on the merged commit pending | `f990976`. `ArchidektDeckReadTests.Repeats_InTheSideboard_AreListedApartFromTheDuplicates_AndTheListIsCapped`. |
