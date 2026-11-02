# Deckbuilding Agent Guidance Plan

## Current Work

- Status: Planned. Drafted from observed agent usage and revised after one independent review; not reviewed by
  the owner yet.
- Owner: mtg-mcp (owner session)
- Last updated: 2026-11-02, drafted against `main` at `847c965` (`0.14.0`).
- Implementation authorized: No. Approval of this packet is not permission to start; the owner must say so.
- Selected work and endpoint: phases 0 to 5, merged into `main`, released, and installed through the new plugin.
- Next action: owner review of decisions D-1 to D-7 and open questions OQ-1 to OQ-4.
- Blocker: none.
- Where the work lives: branch `docs/deckbuilding-agent-guidance-plc` in
  `../mtg-mcp-wt/deckbuilding-agent-guidance/plc`. Nothing is pushed.

## Outcome

An agent that installs mtg-mcp gets the server and a set of skills in one step, and by default it follows the
search process that finds the best evidence:

1. **Plugin install.** One Claude Code plugin installs the MCP server configuration and the skills.
2. **Skills for each deckbuilding job.** Each skill covers a job agents actually do (see Sources). It gives an
   overview, the tools in order, and worked examples. The card-search skill shows which oracle tags exist and how
   to combine them, with basic and advanced example queries.
3. **Tool input and output that steer toward the right next step.** Results state, as facts, what was not read
   and which call reads it; they flag tag names that are not in installed data; and they show the tags present in
   a result.
4. **Measured.** A usage report counts the same things this plan's evidence counts, so the change can be checked
   after release.

The server still returns facts and performs explicit operations. Skills describe a process and point to evidence;
they do not rank cards, call a card good, or infer intent (see Constraints).

## Sources

Evidence from Claude Code sessions in this project between 2026-10-31 and 2026-11-02, counted from the local
transcripts. Some sessions were development or test sessions, not deckbuilding, so the counts are a rough
baseline, not a controlled sample.

| Observation | Count |
| --- | --- |
| mtg-mcp tool calls | 361 |
| `scryfall_search` calls | 37 (about 25 distinct queries) |
| Searches using `otag:` | 1 query, with 5 guessed slugs |
| Guessed slugs that are not tags | at least 2: `board-wipe` (the tag is `sweeper`) and `token-generator` (the tag is `repeatable-token-generator`) |
| `scryfall_tags_search` or `scryfall_installed_cards_by_tag` calls | 0 |
| Search pages cut short (`truncated`) | 9 |
| Search follow-ups with a cursor | 1 |
| `card_list_query` calls | 17, all with a `deck` source; none with the `scryfall-snapshot` source that reads a whole search |
| Searches ordered by `edhrec` (popularity) | 25 of 37 |
| Archidekt sync calls (pull and push, preview and apply) | 126 of 361 |
| Failed or refused sync calls | `archidekt_push_preview` 7, `archidekt_pull_preview` 5, `archidekt_push_apply` 3 |

Concrete failures:

- An agent asked for "flying token makers" in white-blue wrote the regular expression
  `o:/create .* (token|tokens) with flying/`, read 55 of 161 results, and ranked from the most popular. No tag
  marks "creates a flying token", so the exact query needs the token facts in REQ-011.
- `scryfall_tags_search` with `board-wipe` returns nothing, though `sweeper` has the alias `boardwipe`. The
  `sweeper-one-sided` tag the user wanted ("2-3 one-sided board wipes") was never found.
- Agents wrote text matches such as `o:goad` and `o:landfall` where tags exist.

What the server already says: the server instructions, the `scryfall_search` and `scryfall_tags_search`
descriptions, and the `build-from-commander` prompt all mention `otag:`. Agents ignored it. Advice placed in
descriptions is not enough; the guidance has to be in the skill the agent loads for the job and in the result the
agent reads next.

## Scope

- A Claude Code plugin and marketplace manifest in this repository.
- Four skills: card search, deck review, Archidekt sync, and draw and game math.
- A generated oracle-tag reference that the card-search skill ships with.
- `scryfall_search` and `scryfall_tags_search` input and output changes.
- Token facts for installed card data, readable by `scryfall_cards_get`, `scryfall_search`, and `card_list_query`.
- Server instruction and description updates, one new workflow prompt, and an extended `audit-deck` prompt.
- A local usage-report script.

## Non-Scope

- Card rankings, scores, "best card" lists, or any tool that judges a card for a deck.
- Public deck search on Archidekt, Moxfield, or EDHREC. It is useful, but it is a new provider contract and
  belongs in its own PLC.
- Reading the user's card collection.
- A plugin for clients other than Claude Code. The skill files use the shared `SKILL.md` format so other clients
  can load them by hand; packaging for them is deferred (OQ-3).
- Automatic data downloads. Token facts and tag checks use installed card data only, as today.
- Changing the Core result size limit.

## Constraints

- Product rules in `AGENTS.md` and `docs/north-star.md` hold: no advisor prompts, intent inference,
  recommendations, or blended scores on the stable surface. A skill may tell the agent which facts to gather and
  how to show them; it may not tell the agent which card is better.
- Skills describe popularity (`edhrecRank`) as deck inclusion counts, never as card quality, using the existing
  wording in `ScryfallReadTools.EdhrecRankMeaning`.
- Each tool description still starts with its data source. New result fields are facts with provenance; a field
  that needs installed card data is `null` with the reason in `limits` when the data is missing, as `tags` is today.
- No compatibility aliases. A renamed field is renamed everywhere.
- Installed tags can lag behind Scryfall's live tags. Every tag check says "not in installed tag data from
  <date>", never "not a tag".
- No secrets in plugin files. Credentials reach the server the ways they do today (D-7).

## Requirements

Must-have unless marked Should.

### Plugin and skills

- **REQ-001 Plugin install.** The repository is a Claude Code plugin marketplace with one plugin, `mtg-mcp`, that
  registers the MCP server and ships the skills.
  - Acceptance: on a clean Claude Code profile, `/plugin marketplace add nccurry/mtg-mcp` then
    `/plugin install mtg-mcp@mtg-mcp` makes the mtg-mcp tools and the four skills available after a restart, with
    each tool listed once.
  - Validation:
    - A Task command runs `claude plugin validate --strict` on the marketplace and plugin.
    - A unit test checks that `plugin.json`, the `.mcp.json` package version, and `server.json` carry the same
      version and that every skill folder named in the plan exists.
    - A manual install check is recorded in phase 5.
  - The README tells users who already registered `mtg-mcp` by hand to remove that entry, so the tools are not
    listed twice (D-6).
- **REQ-002 Server launch from the plugin.** The plugin's `.mcp.json` starts the released server at the plugin's
  pinned version, with no path specific to one machine and no secrets.
  - Acceptance: the installed plugin starts the server and `scryfall_search` answers; the Archidekt toolset turns
    on when the credential file or environment variables from the README are present.
  - Validation: phase 0 settles the launch command (OQ-1); the release workflow sets the plugin and `.mcp.json`
    versions from `server.json`; phase 5 records the install check.
- **REQ-003 Card-search skill.** A skill for "find cards for a role, theme, or slot". Sections:
  1. Look up tag slugs before searching, with the generated reference and `scryfall_tags_search`.
  2. Compose tags with `id<=`, `f:commander`, `t:`, `mv`, `kw:`, `-otag:`, and `or` groups.
  3. Read the whole result with `card_list_query` over the `scryfall-snapshot` source.
  4. Remove cards already in the deck with `card_lists_compare`.
  5. Token facts for "makes a token with X", and when to fall back to rules-text matching.
  6. Popularity ordering: what `edhrecRank` measures, and that ordering by it puts less-played cards last.
  - It ships `references/query-examples.md` with at least five basic and five advanced example queries, each with
    the question it answers.
  - Acceptance: the skill content test (REQ-017) finds every section, at least five examples marked basic and five
    marked advanced, only registered tool names, and only slugs in the committed slug list (REQ-005).
- **REQ-004 Oracle-tag reference.** The card-search skill ships `references/oracle-tags.md`, generated from
  installed card data. For each tag: slug, label, aliases, and the number of cards tagged.
  - Grouping: a tag is listed under each of its parents, so a tag with two parents appears twice; a tag with no
    parent is a top-level group.
  - It includes tags with at least 25 cards and stays under 60 KB (D-3), and says to use `scryfall_tags_search`
    for the rest.
  - It is stamped with the `oracle_tags` dataset date Scryfall reported (`corpus_datasets.provider_updated_at_utc`).
  - The same generator run writes `references/oracle-tag-slugs.txt`, the list of every installed oracle tag slug.
  - Acceptance: a Task command regenerates both files from installed card data.
  - Validation: a unit test runs the generator on fixture tag data and checks grouping (including a two-parent
    tag), counts, ordering, the card floor, the date stamp, and the size limit.
- **REQ-005 Skill examples stay valid.** A test reads every `otag:` slug in the skills, prompts, and references
  and fails when one is not in the committed `oracle-tag-slugs.txt`.
  - Acceptance: changing an example to `otag:board-wipe` fails the test, naming the slug and the file.
- **REQ-006 Deck-review skill.** A skill for "review my deck against my plan". Sections:
  1. Read the deck with card data.
  2. Count cards by tag and type for each role the user names.
  3. Compare against the counts the user wants.
  4. Use `stats_hypergeometric` or `stats_simulate` for "how often do I have X by turn N".
  - It reports numbers and gaps and leaves the judgment to the agent and user.
  - Acceptance: the skill content test finds each section and only registered tool names.
- **REQ-007 Archidekt-sync skill.** A skill for the pull, edit, push loop that the sources show takes a third of
  all calls. Sections:
  1. Snapshot first.
  2. Pull before editing, and what to do on a fingerprint conflict.
  3. What to do when the user edits on Archidekt during the work.
  4. Patching zones with `zoneCategories`, batching, and rate limits.
  - It also says where credentials go.
  - Acceptance: the skill content test finds each section, and every tool, input field, and option value it names
    exists in that tool's input schema.
- **REQ-008 Draw and game math skill.** A skill that maps common questions to `stats_*` tools with example inputs:
  "odds of N of X by turn T", "how many lands", "on-curve odds", and "sample games with a rule".
  - Acceptance: the skill content test finds an example for each of those four questions, and every tool and
    input field it names exists in that tool's input schema.

### Tool input and output

- **REQ-009 Unknown tag hint.** When card data is installed, `scryfall_search` checks each tag slug in the query
  against installed oracle tags.
  - Forms parsed: `otag:`, `oracletag:`, `function:`, the `=` forms, negated forms, and quoted values. Art tags
    (`atag:`, `arttag:`, `art:`) are ignored.
  - For a slug not in installed data, the response names the slug, the installed tag date, and up to five
    installed tags whose slug, label, or alias matches it after D-2 normalization.
  - The query still runs. The hint is attached to every outcome: a `warnings` entry on success, and the same text
    in the failure details when Scryfall answers "not found" or "invalid query". The exact failure field is set in
    phase 0 (OQ-2).
  - Acceptance:
    - `otag:board-wipe id<=wu f:commander` names `sweeper` whether the search succeeds or returns not-found.
    - `otag:sweeper` gets no hint.
    - With no card data installed there is no hint, and `limits` says the check was skipped.
    - Parser fixtures cover each form above.
- **REQ-010 Tag search matches spelling variants.** `scryfall_tags_search` compares text after D-2 normalization
  against slugs, labels, and aliases.
  - Acceptance: `board-wipe`, `board wipe`, and `boardwipe` each return `sweeper`.
- **REQ-011 Token facts.** For a card in installed card data, a `createsTokens` field lists each token Scryfall
  links to it (`all_parts` entries with component `token`). Each entry gives the token's name, type line, power,
  toughness, colors, and keywords, read from the token's own installed record.
  - In `scryfall_cards_get` and `scryfall_search`, `createsTokens` is a concise field chosen with `fields`. It is
    not in the default concise set.
  - `card_list_query` adds two flat fields:
    - `tokenKeywords`: every keyword of every linked token, filterable with `contains`.
    - `tokenTypes`: every type and subtype of every linked token, filterable with `contains`.
  - Acceptance: Spectral Procession lists a 1/1 white Spirit token with Flying; a `card_list_query` over a saved
    search with `where tokenKeywords contains Flying` returns it.
  - Limits: the field reports what Scryfall links. A token Scryfall does not link is not inferred, and the field
    description says so. A "Copy" token is listed as Scryfall names it.
- **REQ-012 Coverage note on search.** `scryfall_search` already returns `page.totalCount`, `returnedCount`, and
  `snapshot.snapshotId`. Add `coverage` to every page that leaves cards unread. It gives the unread count and the
  exact `card_list_query` source that reads every card of this saved search.
  - Acceptance: a 161-card search returning 30 on its first page carries `coverage` with 131 unread and
    `{"kind": "scryfall-snapshot", "snapshotId": "<id>"}`. The last page carries none.
- **REQ-013 Tag counts on search.** When card data is installed, the first page of `scryfall_search` includes the
  15 oracle tags most common across the result, with card counts.
  - It counts at most the first 20,000 members (`ScryfallListQuery.MaximumEntries`) and reports the cap in
    `limits` when reached.
  - Acceptance: the counts match a `card_list_query` grouped by `tags` over the same saved search.
- **REQ-014 (Should) Compact whole-result listing.** `card_list_query` `rows` accept a page size up to 250 when
  every requested row field is one of `name`, `manaValue`, `typeLine`, `tags`, and `edhrecRank` (D-4).
  - Acceptance: a 200-card saved search lists every card with name, mana value, and type line in one call, within
    the existing result size limit. If 200 rows do not fit, the measured fit is recorded and the acceptance
    lowered with the owner's approval.

### Discovery and measurement

- **REQ-015 Instructions, descriptions, and prompts match the skills.**
  - Server instructions put the tag-first search order in their first job line and say the plugin ships skills,
    within `ServerInstructions.MaximumLength` (2,000).
  - A new `find-cards` workflow prompt gives clients without the plugin the card-search steps.
  - The existing `audit-deck` prompt gains the role and tag counts step from the deck-review skill. No second
    deck-review prompt is added.
- **REQ-016 Usage report.** A Task command reads Claude Code transcript files from a path the user gives and prints
  counts per tool.
  - It counts tools named `mcp__mtg-mcp__*` and plugin tools named `mcp__plugin_mtg-mcp_mtg-mcp__*`.
  - It reports the rows of the Sources table. The "guessed slugs that are not tags" row is reported only when a
    slug list is given, such as `oracle-tag-slugs.txt`.
  - It prints counts only. It copies no transcript text into the output or the repository.
  - Acceptance: on fixture transcripts with both tool-name forms it prints the expected counts; on real
    transcripts it never prints query text, deck names, or card names.
  - The Sources table is the baseline. After release the owner reruns the report and records the results here as
    observations, not as a release gate.
- **REQ-017 Skill content test.** One test covers REQ-003 and REQ-005 to REQ-008. For each skill it checks:
  - the file exists with valid front matter;
  - the listed sections are present;
  - every tool name is registered, and every named input field and option value is in that tool's input schema;
  - every tag slug is in the committed slug list.

## Public Surface Changes

No tool is added or removed, and no tool annotation or operation-mode visibility changes.

| Surface | Change | Compatibility |
| --- | --- | --- |
| Plugin | New `.claude-plugin/marketplace.json` and `plugins/mtg-mcp/` with `plugin.json`, `.mcp.json`, and `skills/` | Additive. Installing through `dotnet tool` keeps working. |
| `scryfall_search` | `coverage` on pages with unread cards; `tagCounts` on the first page; unknown-tag hints; `createsTokens` added to the `fields` choices | Additive fields. The default concise set is unchanged. |
| `scryfall_tags_search` | Matching ignores hyphens, spaces, underscores, and case | Returns more matches for the same text. |
| `scryfall_cards_get` | `createsTokens` added to the `fields` choices | Additive. |
| `card_list_query` | `tokenKeywords` and `tokenTypes` fields; row page size up to 250 for compact rows | Additive. |
| Prompts | New `find-cards`; `audit-deck` gains a step | Prompts are shown in every mode, as today. |
| Instructions | Reworded first job line | Text only. |

Surface tests that change: the concise field list, the `card_list_query` field list, the prompt list, and the
instruction text snapshot. README line about concise fields (the `concise` row of the format table) gains
`createsTokens` as a `fields` choice.

Examples:

```text
> scryfall_search  query: "otag:board-wipe id<=wu f:commander"
not-found: Scryfall found no cards.
  otag:board-wipe is not in installed tag data from 2026-11-01. Closest: sweeper, sweeper-one-sided.

> scryfall_search  query: "otag:sweeper-one-sided id<=wu f:commander"  pageSize: 25
page.totalCount: 40, returnedCount: 25
coverage: 15 cards not on this page. card_list_query with source
          {kind: scryfall-snapshot, snapshotId: ...} reads all 40.
tagCounts: [{tag: sweeper, cards: 40}, {tag: sweeper-one-sided, cards: 40}, ...]
```

The counts and failure wording above are illustrations; phase 0 settles the exact failure shape. Results without
card data installed keep today's shape, with `tagCounts`, `createsTokens`, and the tag check reported as skipped
in `limits`.

## Design And Decisions

- **D-1, skills are the main guidance, delivered by a plugin.** Skills load when a request matches their
  description, cost nothing until then, and can carry long examples and references. Prompts run only when the user
  picks one, and descriptions are skimmed. The plugin is the one install step that brings both server and skills.
  - Rejected: putting the guidance only in tool descriptions (already tried; see Sources); a "router" or advisor
    tool (forbidden by product rules); per-request tool switching.
  - Prompts stay for clients without plugins: one new prompt and one extended prompt (REQ-015).
- **D-2, one tag-name normalization.** Lowercase, then drop spaces, hyphens, and underscores. REQ-009 and REQ-010
  use the same function in `MtgMcp.Scryfall`. It finds candidates only; it never rewrites the query (no aliases).
- **D-3, generated tag reference, checked in.** The reference and slug list are generated from installed card data
  by a Task command and committed, stamped with Scryfall's `oracle_tags` dataset date. The 25-card floor and the
  60 KB cap keep it loadable; if the floor gives more than 60 KB, the generator raises the floor and prints the
  value it used.
  - Rejected: a hand-written list (goes stale, cannot be checked); generating at install time (needs card data
    the user may not have installed).
- **D-4, compact rows within the existing size limit.** `rows.pageSize` stays capped at 100 for other row fields.
  For the compact field set it rises to 250, and the existing size cut still applies, so an oversized page is cut
  with `nextCursor` as today. The Core result size limit does not change.
  - Rejected: raising the Core limit, which changes every tool; a new compact encoding, which adds a second row
    format.
- **D-5, token facts read existing data.** `card_objects.raw_json` already stores Scryfall's `all_parts`, and the
  `all_cards` dataset includes token objects, so REQ-011 needs no new download or provider contract. A projection
  in `MtgMcp.Scryfall` reads `all_parts` entries with component `token` and joins each to the token's installed
  record. Phase 0 measures `card_list_query` over a 500-card saved search with and without an index table filled
  at sync, and picks the simpler option that stays within the current query time.
- **D-6, one server entry.** Claude Code treats a plugin server as a duplicate of a hand-registered one only when
  the command matches. If OQ-1 picks the same `mtg-mcp` command users register today, duplicates collapse;
  otherwise the README tells users to remove their hand-made entry. Either way the README says which.
- **D-7, credentials stay where they are.** The server already reads Archidekt and Playgroup credentials from
  environment variables or from `~/.mtg-mcp/archidekt.json` and `~/.mtg-mcp/playgroup.json`. The plugin passes
  no credentials and adds no `userConfig`; the README and the Archidekt-sync skill point to the files.
- **Coverage and tag counts use the saved search.** Every `scryfall_search` already saves its full result as a
  request snapshot with members. Coverage comes from the snapshot header. Tag counts read each member's Oracle ID
  and join to installed tag assignments, capped as REQ-013 says; phase 0 measures the cost alongside D-5.
- **Skills' content rules.** Each skill: a one-paragraph overview, the steps with tool names, worked examples, the
  facts to report, and the limits of each fact. No card is called good or bad. Example decks and cards are
  illustrations of a query, not suggestions.

Plugin layout (paths from the Claude Code plugin reference):

```text
.claude-plugin/marketplace.json
plugins/mtg-mcp/
  .claude-plugin/plugin.json
  .mcp.json
  skills/
    card-search/SKILL.md
    card-search/references/oracle-tags.md        (generated, REQ-004)
    card-search/references/oracle-tag-slugs.txt  (generated, REQ-004)
    card-search/references/query-examples.md
    deck-review/SKILL.md
    archidekt-sync/SKILL.md
    deck-math/SKILL.md
```

Project boundaries:

- `MtgMcp.Scryfall` owns tag normalization, the tag check, token projection, snapshot tag counts, and the search
  result shapes (`ScryfallSearchResponse`, `limits`, `warnings`), which are defined there today.
- `MtgMcp.App` owns tool descriptions, `card_list_query` fields and row limits, prompts, and server instructions.
- Core is unchanged.
- The generator and usage report are scripts that Task calls.

Tests:

- Unit tests for normalization, the tag-check parser forms, hints on success and on not-found, coverage, tag counts
  and their cap, and token projection. Fixtures come from recorded data: Spectral Procession, and a card with a
  "Copy" token.
- Surface tests for the changes listed under Public Surface Changes.
- The skill content test (REQ-017), the manifest version test, and `claude plugin validate --strict`.
- Generator and usage-report tests on fixture inputs.

## Phases

| Phase | Work | Exit |
| --- | --- | --- |
| 0 | Settle OQ-1 and OQ-2. Measure D-5 with and without an index, and the cost of REQ-013 tag counts on a large search. Build the usage report (REQ-016) and run it on `0.14.0` sync sessions to see which sync failures remain after `deck-edit-ergonomics`. | Decisions and measurements recorded here; usage-report tests pass. |
| 1 | Tag normalization, tag check and hints, tag search matching (REQ-009, REQ-010). | Unit tests pass; `task lint`. |
| 2 | Coverage, tag counts, compact rows (REQ-012 to REQ-014). | Unit and surface tests pass. |
| 3 | Token facts (REQ-011). | Fixture tests pass; measured cost within the phase 0 budget. |
| 4 | Plugin, skills, tag reference and slug list, prompts, instructions, and tests (REQ-001 to REQ-008, REQ-015, REQ-017). | Skill content, manifest, and validate checks pass. |
| 5 | README, CHANGELOG, and `docs/toolsets.md`; release workflow sets plugin versions; `task ci`; full audit; release; install through the plugin. | REQ-001 and REQ-002 install check recorded; release installed. |

Phases 1 to 3 are independent of each other. Phase 4 writes skill text against the shapes from phases 1 to 3, so
it starts after them. If phase 0 finds sync failures that `0.14.0` did not fix, they are listed here for the
owner, not added to scope.

## Risks And Open Questions

- **OQ-1, launch command (blocks REQ-002 and D-6).** Two options:
  - Run the NuGet tool package with `dnx Nccurry.MtgMcp@<version>`, which may need `--yes` to skip a first-run
    prompt.
  - Require `dotnet tool install` first and run `mtg-mcp`, which matches what users register today.
  - The package targets `net11.0` while `global.json` pins a preview SDK, so either option needs a .NET 11 runtime.
    Phase 0 checks what a user without the repository must install.
- **OQ-2, Scryfall's unknown-tag behavior (sets the REQ-009 failure field).** A search that matches nothing returns
  Scryfall's 404, which the server reports as `scryfall-not-found`. Whether Scryfall instead returns 400 or a
  warning for an unknown `otag:` is unverified. Phase 0 records it from one live query.
- **OQ-3, other clients.** Codex and other clients may load `SKILL.md` folders; whether to package for them is
  deferred until the Claude Code plugin is in use.
- **OQ-4, skill evaluation.** The usage report measures behavior after release. A scripted check that runs an
  agent on fixed questions would catch regressions earlier, but costs model calls on every run; deferred unless the
  owner wants it.
- Risk: skill text drifts from the tool surface. REQ-005 and REQ-017 fail on drift.
- Risk: the plugin version and server version drift. The plugin pins the server version, and the release workflow
  sets both from `server.json`.
- Risk: installed tags lag Scryfall's live tags. The hint says "not in installed tag data from <date>".
- Risk: `all_parts` is incomplete for some cards. REQ-011 reports only what Scryfall links and says so.

## Execution Boundary And Finish

- Discoveries do not add requirements or phases. New deliverables, contract changes, or substantially more work
  need the owner's approval first. Routine repairs inside these requirements proceed.
- Audit: a full audit at the end of phase 5, with every supported finding in scope fixed, including P3; at most
  three passes.
- Finish: every Must requirement's check passes, the release is installed through the plugin, the baseline and
  first post-release usage counts are recorded here, and the worktree is removed.

## Evidence

| Check | Date | Result |
| --- | --- | --- |
| Baseline usage counts (Sources) | 2026-11-02 | Recorded above from local transcripts; method: count `tool_use` and `tool_result` blocks for `mcp__mtg-mcp__*` tools. |
| Tag slugs checked with `scryfall_tags_search` | 2026-11-02 | `sweeper`, `sweeper-one-sided`, `repeatable-token-generator`, `repeatable-creature-tokens`, `card-advantage` exist; `board-wipe` returns no tag; no tag for "creates a flying token". |
| Independent PLC review | 2026-11-02 | 17 findings, all accepted and fixed in this revision. Plugin layout, duplicate-server rule, plugin tool naming, and `claude plugin validate` checked against the Claude Code docs by the reviewer. |
