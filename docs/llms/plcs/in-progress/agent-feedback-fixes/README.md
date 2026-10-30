# Agent Feedback Fixes Plan

## Current Work

- Status: In progress
- Owner: mtg-mcp (Opus parent session with Sonnet implementation children)
- Last updated: 2026-10-29, `main` at `5efc62a`
- Selected work and endpoint: REQ-001 to REQ-011 below, merged into `main` on
  this host and pushed, followed by a full audit (every `audit-codebase`
  specialist) over the combined changes. No release is part of this work.
- State: implemented in blocks A, B, and C. Audit pass 1 found problems; the
  repair for them is in progress on `aff/c-budget`. Verification of the repair
  and delivery are pending.
- Next action and blocker: finish the repair, run `task ci`,
  `task test:integration`, and `task test:e2e` on it, then audit again. No
  blocker.
- Resume check: `git worktree list` and `git log --oneline -3 aff/c-budget`
  match the table below.

Where the work lives (a worktree lock, not this table, holds a checkout
against cleanup):

| Checkout | Branch | State |
| --- | --- | --- |
| `mtg-mcp-wt/agent-feedback-fixes/integ` | `aff/integ` | blocks A and B, merged |
| `mtg-mcp-wt/agent-feedback-fixes/c-budget` | `aff/c-budget` | block C and the audit pass 1 repair |
| `mtg-mcp-wt/agent-feedback-fixes/a-decks` | `aff/a-decks` | merged into `aff/integ`; to remove |
| `mtg-mcp-wt/agent-feedback-fixes/b-archidekt` | `aff/b-archidekt` | merged into `aff/integ`; to remove |

## Outcome And Requirements

Agents using `0.12.0` in Claude Code reported five problems (2026-10-29):
deck tool results too large to read, no way to send an Archidekt push, false
category-order differences in push previews, an extra reconcile step after
adding cards by name, and Scryfall searches that mix in reprints. The server
already defaults to `remote` mode (full read and write); the push tool was
missing because the owner's Claude Code configuration passed `--mode=local`.

Claude Code saves any tool text result over 50,000 characters to a file and
shows the agent only the path, regardless of `MAX_MCP_OUTPUT_TOKENS`, and
warns above 10,000 tokens (code.claude.com/docs/en/mcp, "MCP output limits
and warnings"). Pages therefore aim for 40,000 characters, and 50,000 is the
hard ceiling the E2E test enforces. The fix is
to keep results small and high-signal, not to raise per-tool limits.

| ID | Required behavior | Acceptance criteria |
| --- | --- | --- |
| REQ-001 | `local_deck_apply_changes` and `local_deck_identity_reconcile_apply` (and any other local deck write that returns a whole deck) return a summary by default: deck ID, new revision, counts, the IDs of changed entries and categories, and anything refused, skipped, or unresolved. An opt-in `returnDeck: true` returns today's whole deck. | Tests on a 300-entry deck: default result under 10,000 characters with the listed fields; `returnDeck: true` returns the deck. |
| REQ-002 | `local_deck_get` aims for at most 40,000 characters per result and never exceeds 50,000: large decks page or set `truncated: true` with a `narrow` hint naming the option to use (page, fields, `include`), as `card_list_query` does. | Test with a 300-entry deck and every `include`: each page under 40,000 characters, all entries reachable. |
| REQ-003 | `archidekt_deck_get` and `archidekt_push_preview` aim for at most 40,000 characters per result and never exceed 50,000. The push preview leads with a summary (operation counts by kind, conflict count, real difference count) and pages its operations and differences. | Tests on a 300-card remote deck and a 300-operation push: under 40,000 characters, summary present, every operation reachable. |
| REQ-004 | When `archidekt_push_apply` is not available (mode below `remote`, or credentials missing), `archidekt_push_preview` and `archidekt_pull_preview` say so and name the setting that enables it. The existing `_meta["anthropic/requiresUserInteraction"]: true` tag (SAFE-002, `ToolMetadata.cs`, shipped in 0.12.0) stays on exactly the remote destructive tools and gains a test that pins that set. | Preview tests in `local` mode show the message; a `tools/list` E2E test shows the `_meta` key on exactly the remote destructive tools. |
| REQ-005 | Adding an entry with only a card name resolves its card identity at add time from installed card data (the same resolver the reconcile tools use), and its printing when set code and collector number are given. A name that does not resolve or is ambiguous is added unresolved and reported in the summary, never a failure. An exact name prefers a card played in a deck over a token, emblem, or art card; a name shared by two different Oracle IDs is ambiguous. | Tests: exact name resolves `oracleId`; name plus set and number resolves `printingId`; unknown and ambiguous names report a warning; no card data installed reports that resolution was skipped. |
| REQ-006 | An entry's comparison form is defined once in the Archidekt adapter (primary category separate, other category names as a set, order fields ignored) and used for both difference rows and push operations. | Metamorphic tests: reordering category names, renumbering sort orders, or recasing names yields zero differences and zero operations; a real change still shows. |
| REQ-007 | The offline fake Archidekt used by tests behaves like the real one where it has bitten us: it reorders an entry's category names, renumbers sort orders, and derives a card's zone from its primary category. A scripted workflow (pull, edit, push preview, push apply, pull preview) passes against it in `remote` mode, and in `local` mode the preview explains the missing apply. | App tests using the fake. |
| REQ-008 | The `scryfall_search` description warns about how Scryfall combines printing-level conditions such as `not:reprint` with dates, and names a workaround verified against Scryfall. | One bounded live read-only Scryfall query (or recorded response) confirms the behavior and the workaround; the description test passes the 4,096-character limit. |
| REQ-009 | One test calls every tool through a real MCP client and server with the largest realistic fixtures and fails if any text result exceeds 50,000 characters. The Archidekt base address is fixed in the stdio server, so the offline fake cannot sit behind a real stdio process: the test runs in-process over real MCP pipes in `task ci` and `task test`, not in `task test:e2e`. | Runs in the App test project; fails when a tool's limit is removed (checked once by the parent). |
| REQ-010 | A scenario test adds cards by name and reaches a clean push preview in a bounded number of tool calls, without a reconcile step. | App or E2E test with the call count asserted. |
| REQ-011 | Documentation matches: README, `docs/toolsets.md`, and CHANGELOG `Unreleased` describe the new result shapes, the push availability message, the approval prompt, and the default `remote` mode; the owner's Claude Code configuration starts `mtg-mcp` with no mode override. | Doc review; `~/.claude.json` mtg-mcp entry has no `--mode`. |

Non-goals: no release or version bump; no new tools; no change to sync
semantics beyond REQ-006; EVAL-002 stays deferred.

## Current System And Design

Inspected at `747bd88`:

- Local deck writes return `OperationResult<DeckDocument>`
  (`src/MtgMcp.App/Decks/DeckBatchWriteTools.cs`,
  `src/MtgMcp.App/Decks/DeckIdentityReconciliation.cs`).
- `archidekt_push_apply` is registered only when the mode allows remote writes
  (`src/MtgMcp.App/Archidekt/ArchidektToolsetManifest.cs`).
- Difference rows serialize entries with category names in stored order
  (`SerializeEntity` in `src/MtgMcp.Archidekt/ArchidektSyncPlanner.cs`), while
  push planning compares them as a set (ARCH-001).
- `card_list_query` and the deck card view already use `truncated` and
  `narrow` (`src/MtgMcp.App/CardLists/CardListCoordinator.cs`).

Decisions (owner, 2026-10-29, by asking for these fixes):

- Changing the default result of the deck write tools is breaking under
  `docs/versioning.md`; it is taken under the "clearly broken contract"
  exception, because results over 50,000 characters cannot be read inline in
  Claude Code. `returnDeck: true` keeps the old result available. The same
  exception covers the other result changes of this work: `local_deck_get`
  paging, `archidekt_deck_get` (paged, in name order, as an `ArchidektDeckView`),
  the push and pull previews, `archidekt_sync_diff`,
  `archidekt_snapshot_restore_preview`, `archidekt_folders_get`,
  `archidekt_snapshots_get`, the three local previews, the chunked export, and
  the short pages of the Scryfall card tools. Each returned a result that could
  not be read inline for a large deck.
- Default mode stays `remote`, which is full read and write. Remote
  destructive writes keep the per-call approval prompt that 0.12.0 already
  sends through `anthropic/requiresUserInteraction` (SAFE-002).
- Push apply already resolves an Archidekt card by name when no printing is
  set (`ArchidektDeckOperations.cs`), so REQ-005 covers only local identity.
- Resolution on add lives in the App layer, which already composes decks and
  Scryfall; `MtgMcp.Core` and `MtgMcp.Decks` stay free of Scryfall.

## Steps And Verification

1. Block A (child A, `aff/a-decks`): REQ-001, REQ-002, REQ-005, REQ-008.
   Owns `src/MtgMcp.App/Decks/**`, `src/MtgMcp.App/Scryfall/**` descriptions,
   and their tests. Does not edit README, CHANGELOG, `docs/`, or
   `ServerInstructions.cs`.
2. Block B (child B, `aff/b-archidekt`): REQ-003, REQ-004, REQ-006, REQ-007.
   Owns `src/MtgMcp.Archidekt/**`, `src/MtgMcp.App/Archidekt/**`,
   `ServerInstructions.cs`, tool `_meta` registration, and their tests. Same
   doc limits as A.
3. Parent reviews, audits, and merges A and B into `main` one at a time.
4. Block C (child C, `aff/c-budget`, from the merged `main`): REQ-009,
   REQ-010, REQ-011 documentation.
5. Parent: owner configuration change (REQ-011), final checks, merge, push,
   full audit over `747bd88..HEAD`, fixes, cleanup.

Checks per block: focused tests, then `task ci`, `task test:integration`, and
`task test:e2e` from PowerShell on the delivered commit.

## Risks And Open Questions

- REQ-008 needs Scryfall's real behavior; a bounded read-only live query is
  allowed. Result (block A): live queries showed printing-level conditions
  apply to the same printing, and the description says so.
- REQ-005 ambiguity: the first reading was that an exact name is unique in Scryfall card data, so no ambiguous
  case exists. That was wrong: the same name can belong to a token, an art card, or two different cards (Un-cards).
  The exact-name lookup now prefers a card played in a deck over a token, emblem, or art card of the same name, and a
  name that still fits two Oracle IDs is not resolved: it is added unresolved and reported as `ambiguous` with the
  count. Identity reconciliation shares the lookup and reports the same name as unresolved with the count.
- Block C drove every tool at large offline sizes (REQ-009) and found seven results over 50,000 characters:
  `local_deck_export_bundle` (305,000 to 370,000 for 300 entries), `local_deck_identity_reconcile_preview` (71,000 for
  150 entries), `local_deck_import_preview` (53,000 for 300 lines), `local_deck_category_rules_preview` (62,000 for
  300 entries), and the Scryfall card tools with `responseFormat` `full`. The owner decided to fix all seven in this
  block. The three deck previews now page their rows with `offset` (the fingerprint and apply token stay bound to the
  whole preview); the export returns metadata and one chunk of one artifact by character offset; the Scryfall tools cut
  every format to the size limit. Real recorded full cards are large: only two of six recorded cards fit a page.
- Block C also paged `archidekt_sync_diff`, `archidekt_snapshots_get` (kind
  `get`, 116,000 characters before), `archidekt_snapshot_restore_preview`, and
  `archidekt_folders_get` (65,000 before), the same way as block B.
- The Archidekt base address is fixed in the stdio server, so the offline fake
  cannot sit behind a real stdio process. REQ-009 therefore runs in the App
  test project over a real MCP client and server joined by in-memory pipes, not
  through `MtgMcp.E2E.Tests`. Playgroup, Commander Spellbook, and the ten
  Archidekt writes the fake has no route for are listed in the test with a
  reason, not driven.
- Follow-up: a push of more than about 140 operations exceeds
  `MaximumRequestsPerOperation` (150) and cannot be applied in one call.
- Category reorder on Archidekt still counts as a remote change; this is by
  design.
- `local_deck_*` write tools with `returnDeck: true` can return a result over
  50,000 characters for a large deck. The budget test does not call them that
  way; the opt-in is documented.

## Evidence And Completion

Results are recorded here as blocks land. "Checks" cells are for the parent to
fill after `task ci`, `task test:integration`, and `task test:e2e` on the merged
commit.

| Requirement | State | Evidence | Checks |
| --- | --- | --- | --- |
| REQ-001 | delivered in block A (`4066c36`) | `DeckWriteResult` summary with `returnDeck`; `DeckWriteSummaryTests` on a 300-entry deck (summary under 10,000 characters). | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-002 | delivered in block A (`4066c36`) | `local_deck_get` pages with `cursor`, `pageSize`, `nextCursor`, `truncated`, `narrow`; `ToolResultBudgetTests` reads every page of a 300-entry deck with every `include`, all under 50,000. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-003 | delivered in block B (`31b4db7`), extended in block C | `ArchidektWorkflowTests` pages a 300-card deck and a 300-operation push under 40,000; block C adds the same paging to the diff, restore preview, folder, and snapshot reads. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-004 | delivered in block B (`31b4db7`) | `applyAvailable` and `applyNotice` on both previews (`ArchidektWorkflowTests`); the `_meta` set is pinned in `ToolSurfaceRulesTests` and `FoundationMcpTests`. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-005 | delivered in block A (`4066c36`) | Names resolve at add time (`DeckWriteSummaryTests`, `DeckEntryResolverTests`); a token or art card never beats a playable card, and a name shared by two cards is reported as ambiguous (see Risks). | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-006 | delivered in block B (`615d14e`, `aa473c2`) | `ArchidektEntryComparison` is the one comparison form; `ArchidektEntryComparisonTests`. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-007 | delivered in block B (`31b4db7`) | Stateful `FakeArchidekt` and `ArchidektWorkflowTests` in remote and local mode; block C added list, folder, and snapshot routes. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-008 | delivered in block A (`4066c36`) | `scryfall_search` description note; live result in Risks. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-009 | delivered in block C | `ToolResultBudgetTests` (App test project, in-memory MCP client and server): drives 55 of 86 tools, lists 31 with reasons, and fails on any result, successful or not, over 50,000 characters, a tool in neither list, or a tool that grows with its data and never pages with the large fixtures. There is no known-over-ceiling list. Parent: confirm it fails when a limit is removed. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-010 | delivered in block C | `AddByNameScenarioTests`: pull preview, pull apply, add five cards by name, push preview is all `entry-add` with no conflict and no unresolved card, in four tool calls and no reconcile call. The deck is bound by pulling the fake deck, because the fake has no deck-create route. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
| REQ-011 | documentation delivered in block C | README, `docs/toolsets.md`, `docs/output-control.md`, CHANGELOG `Unreleased`. Parent: `~/.claude.json` mtg-mcp entry has no `--mode`. | pending: `task ci`, `task test:integration`, `task test:e2e` on the merged commit |
