# Surface Consistency Plan

## Current Work

- Status: In progress (phases 0 to 3 merged at `e547882`; audit pass 1 done, repairs running)
- Owner: mtg-mcp (owner session)
- Last updated: 2026-11-02, branch `surface-consistency/integration` at `e547882`
- Implementation authorized: Yes (owner, 2026-11-02: "Accept and implement"; D-1 to D-6 accepted as recommended)
- Selected work and endpoint: phases 0 to 3, merged into `main`, audited, released as `0.15.0`, and installed.
- State: all phases merged into integration and `task ci` passed on `e547882`. Audit pass 1 findings are being repaired: the shared foundation first, then the four families.
- Next action: merge the repairs, update the docs and evidence, run `task ci`, then merge to `main` and release `0.15.0`.
- Blocker: none.
- Resume check: `git log surface-consistency/integration` contains `e547882`; worktrees under `../mtg-mcp-wt/surface-consistency/`.

## Problem

The `deck-edit-ergonomics` audit listed eight inconsistencies in the MCP surface that could not be fixed without
breaking clients. On 2026-11-02 the owner chose to fix them with no backwards compatibility: no aliases and no
deprecation release.

Each inconsistency makes an agent learn more than one way to do the same thing, so it guesses, and a wrong guess
costs a failed call. The goal is one form for each kind of thing, written down in `docs/architecture/mcp-surface.md`
and enforced by tests, so the surface cannot drift back.

An inventory of all 86 tools at `4125d0a` (2026-11-02, read-only) found these. Phase 0 replaces every "about" below
with an exact list.

| ID | Inconsistency | Extent |
| --- | --- | --- |
| C-1 | Five paging styles | See "Paging styles" below. The Archidekt reads that page by bare offset have no guard against the deck changing between pages. Spellbook's `limit` and `offset` sit in a nested options object (`SpellbookPageOptions`). |
| C-2 | `responseFormat` defaults and meanings differ | `standard` by default on the Scryfall and card-list tools, `full` on `local_deck_get` and the Archidekt diff and previews, `concise` on Archidekt reads. On five tools `standard` means the same as `full`. |
| C-3 | Three names for the preview fingerprint | `fingerprint` and `expectedFingerprint` (import), `previewFingerprint` (results and applies), `expectedPreviewFingerprint` (later preview pages). |
| C-4 | Two styles of apply arguments | The 11 Archidekt write tools take one `request` object of scalar guards; local tools take flat arguments; `archidekt_deck_create` mixes both. |
| C-5 | Several revision-mismatch codes and names | See "Change codes" below. The parameter is `expectedRevision` on local tools and `expectedLocalRevision` on Archidekt tools; previews return `localRevision`. |
| C-6 | Several "changed since" codes | Seven apply codes for a preview that no longer matches, plus three page codes for a source that changed between pages. See "Change codes" below. |
| C-7 | Scryfall `invalid-input` results with no `path` | About 35 sites in `src/MtgMcp.Scryfall/`, covering queries, lookups, cursors, policies, tag selectors, and acknowledgements. Some reason codes cover several arguments. |
| C-8 | Synonyms for one concept | `deckId` means a local, Archidekt, or Playgroup deck depending on the tool. Archidekt tools mix `deckId`, `localDeckId`, and `remoteDeckId`. Confirmation takes three forms: a `confirmation` phrase (Archidekt), `acknowledge*` booleans (Scryfall), and a `confirmRemovals` count (push). `snapshotId` names both a Scryfall request snapshot and an Archidekt snapshot. `freshnessPolicy` and `metadataPolicy` are both used. The Scryfall generation guard is named `expectedCorpusGeneration` on category apply and `expectedActiveGeneration` on Scryfall tools. |

Paging styles (C-1):

| Style | Tools |
| --- | --- |
| `cursor` with `pageSize` | Local deck list and get, Archidekt deck list, the Scryfall reads, card lists |
| `offset` with `expectedPreviewFingerprint` | Import, identity, and category previews |
| Bare `offset` | `archidekt_deck_get`, `archidekt_folders_get`, `archidekt_snapshots_get` get, export chunks |
| Two offsets | Archidekt pull, push, and restore previews; `archidekt_sync_diff` |
| `limit` with `offset` | Spellbook (no next-page field) |
| `page` with `limit` | `playgroup_playgroup_games_list` (no next-page field) |

Change codes (C-5 and C-6), with today's codes and their end state under D-1:

| Today | Where | End state |
| --- | --- | --- |
| `stale-deck-revision` | Store writes (`SqliteDeckStore.cs:913`) | Kept: the one revision code |
| `deck-revision-conflict` | 7 sites (card lists, category rules, duplicates, identity, Spellbook, statistics) | `stale-deck-revision` |
| `local-deck-changed` | `archidekt_deck_create` (`ArchidektCoordinator.cs:1058`) | `stale-deck-revision` |
| `pull-preview-changed` and `push-preview-changed` | Pull and push apply | The revision check returns `stale-deck-revision`; a fingerprint mismatch returns `preview-changed` |
| `restore-preview-changed`, `import-preview-changed`, `category-preview-mismatch`, `identity-preview-mismatch` | Applies | `preview-changed` (a conflict) |
| `preview-changed` on a later preview page (`ResultPage.cs:140`) | Paged previews | `cursor-stale` |
| `source-changed-between-pages` (`CardListCursor.cs:102`), `deck-changed-between-pages` (`DeckCardViewCoordinator.cs:473`), `deck-list-changed` (`ArchidektDeckTransport.cs:65`) | Paged reads | `cursor-stale` |
| `category-corpus-conflict` | Category apply | `stale-scryfall-generation`, the existing code for the same cause |
| `remote-deck-changed`, `snapshot-changed`, `snapshot-content-changed`, `folder-tree-changed`, `pull-discard-option-mismatch` | Archidekt | Kept: each names a specific cause |

## Decisions For The Owner

Each decision has a recommendation, and the requirements assume it.

- **D-1, one paging form and two change codes (recommended).**
  - Every paged tool takes `cursor` and `pageSize` and returns `nextCursor`, which is null on the last page.
  - The cursor is opaque and binds what the page depends on: the deck revision, the preview fingerprint, the remote
    fingerprint, the export artifact, or the provider offset.
  - A cursor whose source changed gets one code, `cursor-stale`. An apply whose preview no longer matches gets
    `preview-changed`.
  - Alternative: offsets everywhere. That means no change guard and more numbers to copy.
- **D-2, one `responseFormat` meaning (recommended: default `standard`).**
  - `concise` returns the short fields the tool documents. `standard` returns every field this server shapes.
    `full` adds raw provider objects.
  - A tool offers only the values it has: `full` only where raw provider objects exist (Scryfall and card lists),
    and `concise` only where a short form is defined.
  - Every tool defaults to `standard`. The "`standard` is the same as `full`" alias goes.
  - Each tool's default `pageSize` is set so a default page fits the result limit (phase 0 measures).
- **D-3, flat apply arguments (recommended).**
  - Write tools take flat arguments.
  - A nested object stays only where the body is a document. The kept pairs are a named allowlist: the deck graph,
    `changes`, folder move `items`, Playgroup events, and statistics requests.
- **D-4, deck and snapshot identifier names (recommended).**
  - `deckId` names a local deck everywhere.
  - Archidekt tools take `localDeckId` and `remoteDeckId`.
  - Playgroup tools take `playgroupDeckId`.
  - The Archidekt snapshot keeps `snapshotId`, and the Scryfall request snapshot becomes `requestSnapshotId`.
  - Phase 0 lists every renamed parameter per tool.
- **D-5, one confirmation rule (recommended).**
  - An operation that destroys or replaces data with no local undo takes `confirmation`, an exact phrase that names
    the action and the target: `<action> <target kind> <id>`, for example `delete deck 123` or
    `restore snapshot 456`. The phrase binds the target, so an agent cannot confirm the wrong one.
  - The covered operations are the Archidekt deletes and restore, `scryfall_card_data_delete`,
    `scryfall_request_snapshot_delete`, and an activation change on `scryfall_card_data_sync`. Phase 0 confirms the
    list and each phrase. The Scryfall `acknowledge*` booleans become these phrases.
  - `confirmRemovals` stays a count: it restates a number the caller saw, not a target.
  - Local deck deletes, backup deletes and restores, and a discarding pull also take a phrase (see Phase 0 Decisions; the original exemption rested on an automatic backup that does not exist).
  - Alternative: keep the booleans. They are cheaper for agents, but they don't bind the target.
- **D-6, version `0.15.0` (recommended).** Pre-1.0 breaking changes go in a minor release.
  - `docs/versioning.md` gains an "Approved 0.15.0 clean-break exception" section beside the 0.9.0 and 0.12.0 ones.
  - The CHANGELOG lists every rename under a "Breaking" heading, old name to new.
  - An old argument name gets the existing "Property is not accepted here" refusal, which names the field.

## Requirements

| ID | Finding | Required behavior | Acceptance criteria |
| --- | --- | --- | --- |
| REQ-001 | C-1 | Every paged tool takes `cursor` and `pageSize` and returns `nextCursor` (null on the last page). These go: `offset`, `operationOffset`, `differenceOffset`, `expectedPreviewFingerprint`, `limit`, `page`, the Spellbook page options object, and every `next*Offset` field. A cursor whose source changed is refused with `cursor-stale`, carrying the current state; a malformed cursor is `invalid-cursor` with path `cursor`. Special cases follow the list after this table. | A surface test lists every tool with paging parameters and fails on any other form. Per-tool tests cover the next page, the last page, and a stale cursor. Further tests cover the Spellbook cap, a Playgroup total that is an exact multiple of the page size, and an export chunk boundary on a surrogate pair. |
| REQ-002 | C-2 | `responseFormat` follows D-2. Each tool publishes only the values it offers, with the default `standard`. A page of the default `pageSize` in `standard` fits the result limit on every tool. | `ToolAllowedValuesTests` and a surface test check each tool's values and default. Output tests cover each value. A test takes a default page of each tool from the largest fixture and asserts it fits the limit. |
| REQ-003 | C-3 | The preview fingerprint is `previewFingerprint` in every result and every apply argument. Import's `fingerprint` and `expectedFingerprint` are renamed. `applyToken` stays on the identity and category previews. | The naming test rejects any other name for a preview fingerprint. Import, identity, category, pull, push, and restore tests use the new names. |
| REQ-004 | C-4 | The 11 Archidekt write tools take flat arguments with the same fields, and `request` goes. Error paths have no `request.` prefix. The D-3 allowlist keeps its objects. | The E2E schema test lists the flat arguments. A surface test fails when a write tool takes an object parameter that is not on the D-3 allowlist. |
| REQ-005 | C-5 | One revision-mismatch code, `stale-deck-revision`, a conflict carrying `current.revision`, from every tool that checks a local deck revision. Pull and push apply check the revision first. `expectedLocalRevision` becomes `expectedRevision`, and `localRevision` in results becomes `revision`. | Reason-code tests at each former site. |
| REQ-006 | C-6 | Apply mismatches return `preview-changed`, a conflict carrying `current`. Stale page cursors return `cursor-stale`. Every other change code ends as the Change codes table says. | Apply tests for import, identity, category, pull, push, and restore assert `preview-changed`. Paging tests assert `cursor-stale`. A source test fails if any removed code appears in `src/`: `deck-revision-conflict`, `local-deck-changed`, the five apply variants, the three page codes, and `category-corpus-conflict`. |
| REQ-007 | C-7 | Every `invalid-input` from a tool names one argument path. A reason that covers several arguments is split, or carries the exact path. Where a lower layer cannot know the argument, the tool wrapper sets it. Phase 0 produces the fixed list of sites and arguments. | A theory over phase 0's list drives each invalid argument through its tool and asserts the path. A test hook in the tool invocation pipeline (beside `ToolCallPolicy`) fails any App tool test whose result is `invalid-input` with a null path. |
| REQ-008 | C-8 | Identifier, snapshot, and confirmation names follow D-4 and D-5. `expectedCorpusGeneration` becomes `expectedActiveGeneration` if phase 0 shows it guards the same thing; the same goes for `metadataPolicy` and `freshnessPolicy`. Where phase 0 finds a different meaning, the names stay and the plan records why. | The naming test checks each name per tool family against phase 0's list. Confirmation tests assert the exact phrase for each destructive operation. A test checks that `confirmRemovals` stays a count. |
| REQ-009 | all | The docs match the code, as listed after this table. | The README example tests and description tests pass. A docs check fails on any removed name outside the CHANGELOG and completed plans. |
| REQ-010 | all | An agent pages, chooses a format, and copies guards the same way on every tool. | An in-process scenario test runs the import, category, identity, and Archidekt push workflows using only `cursor`, `pageSize`, `previewFingerprint`, and `expectedRevision`. |

Paging special cases (REQ-001):

- **Spellbook.** The cursor ends at Spellbook's offset cap of 1,000 (`SpellbookContract.cs:74`, `:79`), and the
  description says so.
- **Playgroup.** It reports no total, so `nextCursor` is returned after every full page. When the total is an exact
  multiple of the page size, the last page is empty, and the description says so.
- **Export chunks.** The cursor binds the artifact and its checksum and counts UTF-16 code units, never splitting a
  surrogate pair.
- **Import preview.** It still takes `content` on every call, because the server stores no import. The cursor
  replaces only the offset and the fingerprint.

Docs to update (REQ-009):

- `docs/architecture/mcp-surface.md` states the one form for paging, `responseFormat`, fingerprints, apply arguments,
  change codes, error paths, identifiers, and confirmation.
- `docs/versioning.md` gains the 0.15.0 exception.
- The README, `docs/output-control.md`, `deck-model.md`, prompts, server instructions, and descriptions use the new
  names.
- The CHANGELOG `0.15.0` entry has a "Breaking" section listing each old and new name.

Non-goals:

- **New features.**
- **Changing what a tool computes**, beyond paging guards and error paths.
- **Renaming tools.** The tool names already follow the naming rule.
- **Compatibility aliases or a deprecation release.** This is the owner's decision of 2026-11-02.
- **Provider HTTP contracts.** The Spellbook, Playgroup, Archidekt, and Scryfall adapters keep their requests and
  pacing. Only the App tool surface changes.

## MCP Surface

- The tool count stays 86 in `remote` mode, 59 in `read-only`, and 73 in `local`.
- Tool names, annotations, and operation-mode visibility stay the same.
- Input schemas, output schemas, descriptions, prompts, and server instructions change as above.
- Cursor errors carry no provider detail. A cursor is encoded server data, not a provider token.
- Surface tests:
  - the E2E schema test (`FoundationMcpTests`)
  - `ToolAllowedValuesTests`
  - `ToolNamingRuleTests`, extended for the D-4 and D-5 names and the D-3 allowlist
  - a new paging surface test
  - the null-path hook (REQ-007)

## Design Notes

- One shared cursor codec in `src/MtgMcp.App/Hosting/`, beside `ResultPage`, encodes a typed position and a binding.
  Each tool now builds its own cursor; all move to the codec.
- Two-list previews encode both positions in one cursor. The page fills from operations, then differences, as
  `ResultPage` already measures.
- Shared reason codes live in one Core constants class: `stale-deck-revision`, `preview-changed`, `cursor-stale`,
  and `invalid-cursor`. This lets the source test reject removed codes, and gives every tool family one place to
  read them.
- Phase 0 is read-only. It produces the exact lists the requirements name:
  - the paged tools
  - the error-path sites and their arguments
  - the renamed parameters per tool
  - the destructive operations and their phrases
  - the meaning of `metadataPolicy` and `expectedCorpusGeneration`
  - the measured default page sizes

## Steps And Verification

0. **Phase 0, facts (read-only).** Produce the lists named in Design Notes, at the starting commit.
1. **Phase 1, shared foundation, one worktree.**
   - The cursor codec and the Core reason-code constants.
   - The source test for removed codes, the paging surface test, and the null-path hook.
   - The naming-test extensions.
   - Every tool family then builds on this; nothing else is changed here.
2. **Phase 2, tool families in parallel worktrees, merged by one integrator worktree.** Each family applies REQ-001
   to REQ-008 to its own tools, so no two worktrees edit the same tool file:
   - (a) local deck tools, interchange, and backups
   - (b) Archidekt tools, including `ArchidektCoordinator`
   - (c) Scryfall tools and card lists
   - (d) Spellbook, Playgroup, and statistics
3. **Phase 3 (REQ-009, REQ-010).**
   - Docs and the versioning exception, the CHANGELOG with its Breaking section, and the scenario test.
   - The full audit.
   - The merge to `main`, the `0.15.0` release, and the install.

Each phase runs focused tests, then `task ci`, `task test:integration`, and `task test:e2e` from PowerShell.

Audit: the full audit runs at the end with every specialist. Fix every supported finding in scope, including P3,
with at most 3 passes.

Execution boundary: findings during implementation do not add requirements automatically; scope growth needs the
owner's approval.

## Risks And Open Questions

- Every client prompt or script that names an old argument breaks. That is the chosen trade (owner, 2026-11-02), and
  the CHANGELOG Breaking section is the migration guide.
- D-2 makes default Archidekt reads larger. REQ-002 sets each default page size so a page still fits the result
  limit, so agents may need one more page.
- D-5 phrases cost agents more than booleans. The target binding is the reason to accept that. The owner can choose
  the alternative in D-5.

## Evidence And Completion

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 | planned | |
| REQ-002 | planned | |
| REQ-003 | planned | |
| REQ-004 | planned | |
| REQ-005 | planned | |
| REQ-006 | planned | |
| REQ-007 | planned | |
| REQ-008 | planned | |
| REQ-009 | planned | |
| REQ-010 | planned | |

The plan is complete when every requirement is verified on the merge and release commits, the audit is closed, and
`0.15.0` is installed.

## Phase 0 Decisions

Phase 0 (`07fd8e0`, `PHASE0.md`) gives the exact lists that phase 2 follows. Its findings were settled under the
accepted D-1 to D-6 rules on 2026-11-02:

- **12 Archidekt tools take `request`**, not 11: `archidekt_pull_apply` too. All 12 go flat (D-3).
  `playgroup_live_session_create` takes a scalar `request` and also goes flat. The `scryfall_*` writes are already
  flat.
- **The activation boolean is on `scryfall_card_data_rollback`**, not on sync. D-5 covers `scryfall_card_data_delete`,
  `scryfall_request_snapshot_delete`, and `scryfall_card_data_rollback`, plus the Archidekt deck, folder, and
  snapshot deletes and the snapshot restore. D-5 had exempted local deletes because "backups undo them", but no
  backup is taken automatically. On 2026-11-02 the owner chose to apply the rule instead, so these local operations
  also take a phrase:
  - `local_deck_delete`: `delete deck <deckId>`
  - `local_deck_backup_delete`: `delete backup <backupId>`
  - `local_deck_backup_restore`: `restore backup <backupId>`
  - `archidekt_pull_apply` when `discardLocalChanges` is true: `discard local changes <localDeckId>`
- **Spellbook tool arguments are already flat.** Only the adapter's page options and the echoed `request.page` are
  nested; the tool takes `cursor` and `pageSize` and keeps the adapter contract.
- **Archidekt reads gain a new `pageSize` argument**, since none has one today.
- **`local_deck_list` gets a bound cursor** in place of its bare offset cursor.
- **REQ-002's size bound comes from the byte cut where a tool has one**, and those tools keep their current default
  `pageSize`. A page stops early when it reaches the result limit, and the result says so. The tools with no byte
  cut gain one: `local_deck_list`, `archidekt_deck_list`, Spellbook, Playgroup games, and the raw modes of
  `scryfall_tags_search` and `scryfall_request_snapshots_get`. Phase 0's smaller suggested defaults are not used.
- **`ConflictState` gains `previewFingerprint`** in Core (integration, before phase 2), so `preview-changed` and
  `cursor-stale` can carry the current preview.
- **Revision names.** `deckRevision` in local previews and export becomes `revision` (REQ-005). Card-list sources
  rename `archidektDeckId` to `remoteDeckId` (D-4).
- **The card-list result `fingerprint` is not a preview fingerprint.** It identifies the analyzed list. It stays and
  is exempt from the REQ-003 naming test, with that reason in the test.
- **`card_list_query` moves `rows.pageSize` to a top-level `pageSize`** (D-1).
- **Export `pageSize` counts UTF-16 code units** of the artifact text per chunk.
- **A remote-fingerprint mismatch returns `remote-deck-changed`** on both pull and push apply.
- **`metadataPolicy` becomes `freshnessPolicy`, and `expectedCorpusGeneration` becomes `expectedActiveGeneration`**:
  phase 0 found the same meanings.
- **New defect, fixed in family (a):** a category preview of an empty deck with tag selectors returns
  `invalid-card-lookup`.

## Audit Ledger

Pass 1 (2026-11-02) reviewed `4125d0a..e547882` (186 files). Seven read-only reviewers each applied two specialist
skills: correctness with security, tests with test quality, simplicity with performance, code quality with dead code,
structure with visual review, plan alignment with docs sync, and plain language with file hygiene. Before the audit,
`task ci` passed on `e547882`: 2,456 tests, 77 smoke tests, and every assembly over 90 percent line coverage. Integration
and E2E tests also passed.

No P0 findings. One P1: the null-path check ran only the budget script, which sends no invalid input, so it passed
vacuously.

P2 findings:

- **Duplicated code.** Three cursor codecs (App `PageCursor`, `ScryfallCursor`, the identity apply token) and four
  confirmation checks with two wordings.
- **Wrong codes and kinds.** Provider 400s on Spellbook and Playgroup were reported as `unavailable`.
- **Cursors.** The Playgroup cursor had no state binding. It also used a `twoLists` flag and a `Second` position to
  mean rows already shown.
- **Messages and paths.** `StatisticsFailurePath` guessed paths from message text.
- **Layering.** Scryfall stores trimmed result size through a callback. A global serializer modifier renamed
  `remoteId`.
- **Structure.** Long lines (about 60), the 424-line `PageCursor.cs`, the overloaded Spellbook paging `Shape`, and a
  duplicated Spellbook offset cap.
- **Missing tests.** No per-tool `responseFormat` test, no identity `preview-changed` test, missing Archidekt stale
  tests, and surface tests that could pass vacuously.
- **Plan and docs.** Unrecorded deviations: Spellbook combos cut with no `nextCursor`, and `options` and `source` kept
  as objects. No REQ-009 docs check. Stale test names in `mcp-surface.md`.

The P3 findings cover duplicated helpers and literals, page-bounds and foreign-cursor codes, two-list ordering, the
deck list read, the confirmation echo length, wording, unreachable codes, and stale plan text.

Accepted deviations, now recorded:

- **`spellbook_deck_combos_find` returns no `nextCursor` after a size cut.** Spellbook's offset counts combos, not the
  grouped rows a page shows, so no position can name the next row. The caller retries with a smaller `pageSize`, and
  the description says so.
- **`local_deck_import_create.options` and `local_deck_category_rules_apply.source` stay objects.** Each is a
  document (import options, a rule set), as D-3 allows.
- **Scryfall list results put `nextCursor` under `page`**, as phase 0 recorded; `scryfall_card_details` puts it at the
  top level.
- **Cursor checksums detect accidental edits, not deliberate ones.** Every reader bounds positions again, so a forged
  cursor can only point inside the data the caller may read.
