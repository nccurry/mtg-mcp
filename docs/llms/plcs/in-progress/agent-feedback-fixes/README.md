# Agent Feedback Fixes Plan

## Current Work

- Status: In progress
- Owner: mtg-mcp (Opus parent session with Sonnet implementation children)
- Last updated: 2026-10-29, `main` at `747bd88`
- Selected work and endpoint: REQ-001 to REQ-011 below, merged into `main` on
  this host and pushed, followed by a full audit (every `audit-codebase`
  specialist) over the combined changes. No release is part of this work.
- State: implementation not started; verification pending; delivery pending.
- Next action and blocker: start blocks A and B in parallel; no blocker.
- Resume check: `git log --oneline main -5` and `git worktree list` match the
  table below.

Where the work lives (a worktree lock, not this table, holds a checkout
against cleanup):

| Checkout | Branch | Owner | State |
| --- | --- | --- | --- |
| `mtg-mcp-wt/agent-feedback-fixes/a-decks` | `aff/a-decks` | Sonnet child A | not created |
| `mtg-mcp-wt/agent-feedback-fixes/b-archidekt` | `aff/b-archidekt` | Sonnet child B | not created |
| `mtg-mcp-wt/agent-feedback-fixes/c-budget` | `aff/c-budget` | Sonnet child C | not created |

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
| REQ-005 | Adding an entry with only a card name resolves its card identity at add time from installed card data (the same resolver the reconcile tools use), and its printing when set code and collector number are given. A name that does not resolve or is ambiguous is added unresolved and reported in the summary, never a failure. | Tests: exact name resolves `oracleId`; name plus set and number resolves `printingId`; unknown and ambiguous names report a warning; no card data installed reports that resolution was skipped. |
| REQ-006 | An entry's comparison form is defined once in the Archidekt adapter (primary category separate, other category names as a set, order fields ignored) and used for both difference rows and push operations. | Metamorphic tests: reordering category names, renumbering sort orders, or recasing names yields zero differences and zero operations; a real change still shows. |
| REQ-007 | The offline fake Archidekt used by tests behaves like the real one where it has bitten us: it reorders an entry's category names, renumbers sort orders, and derives a card's zone from its primary category. A scripted workflow (pull, edit, push preview, push apply, pull preview) passes against it in `remote` mode, and in `local` mode the preview explains the missing apply. | App tests using the fake. |
| REQ-008 | The `scryfall_search` description warns about how Scryfall combines printing-level conditions such as `not:reprint` with dates, and names a workaround verified against Scryfall. | One bounded live read-only Scryfall query (or recorded response) confirms the behavior and the workaround; the description test passes the 4,096-character limit. |
| REQ-009 | One E2E test calls every tool through the real stdio server with the largest realistic fixtures and fails if any successful text result exceeds 50,000 characters. | Runs in `task test:e2e`; fails when a tool's limit is removed (checked once by the parent). |
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
  Claude Code. `returnDeck: true` keeps the old result available.
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
  allowed.

## Evidence And Completion

Results are recorded here as blocks land.

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 to REQ-011 | pending | |
