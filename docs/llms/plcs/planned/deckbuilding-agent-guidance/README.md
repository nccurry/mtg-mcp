# Deckbuilding Agent Guidance Plan

One Claude Code plugin installs the mtg-mcp server and a set of deckbuilding
skills, and tool results steer agents toward the search process that finds the
best evidence. The server still returns facts; skills describe a process and do
not rank cards.

## Current Work

- Status: Planned. Drafted from observed agent usage, revised after one independent review, and its evidence
  recounted across every local transcript on 2026-11-02; brought current with `main` and re-checked against
  Anthropic, OpenAI, and MCP guidance and Commander deckbuilding practice on 2026-11-05; not reviewed by the owner yet.
- Owner: mtg-mcp (owner session) (@nccurry)
- Last updated: 2026-11-05, checked against `main` at `d532ae5` (`0.15.0`, the `surface-consistency` clean break).
  The usage evidence below was measured on `0.14.0` and earlier.
- Implementation authorized: No. Approval of this packet is not permission to start; the owner must say so.
- Selected work and endpoint: phases 0 to 5, merged into `main`, released, and installed through the new plugin.
- Next action: owner review of decisions D-1 to D-7, the skill authoring rules S-1 to S-16, and open questions
  OQ-1 to OQ-5 and OQ-7. The owner answered OQ-6 and OQ-8 on 2026-11-07 (see Log).
- Blocker: none.
- Where the work lives: this packet is merged into local `main`; no implementation branch or worktree exists yet.
  Nothing is pushed.

## Phases

| Phase | Work | Exit | State |
| --- | --- | --- | --- |
| 0 | Settle OQ-1 and OQ-2, and try `claude plugin eval` on one scenario (OQ-4). Measure D-5 with and without an index, and the cost of REQ-013 tag counts on a large search. Build the usage report (REQ-016) and run it on sync sessions from `0.15.0` onward to see which sync failures remain; the 9 failures in Sources used `0.14.0` argument shapes that `0.15.0` no longer accepts. | Decisions and measurements recorded here; usage-report tests pass. | Planned |
| 1 | Tag normalization, tag check and hints, tag search matching (REQ-009, REQ-010). | Unit tests pass; `task lint`. | Planned |
| 2 | Unread-card note, tag counts, compact rows (REQ-012 to REQ-014). | Unit and surface tests pass. | Planned |
| 3 | Token facts (REQ-011). | Fixture tests pass; measured cost within the phase 0 budget. | Planned |
| 4 | Write the REQ-019 scenarios and run the baselines first. Then build the plugin, skills, tag reference and slug list, prompts, instructions, and tests (REQ-001 to REQ-008, REQ-015, REQ-017, REQ-018). | Skill content, manifest, and validate checks pass; baselines recorded. | Planned |
| 5 | README, `docs/reference.md`, CHANGELOG, `docs/toolsets.md`, `llms.txt`, and `.codex/` playbooks (`AGENTS.md` requires them to match); release workflow sets plugin versions; `task ci`; REQ-019 runs with the plugin; full audit; release; install through the plugin. | REQ-001, REQ-002, and REQ-019 results recorded; release installed. | Planned |

Phases 1 to 3 are independent of each other. Phase 4 writes skill text against the shapes from phases 1 to 3, so
it starts after them. If phase 0 finds sync failures that `0.15.0` did not fix, they are listed here for the
owner, not added to scope. The next release that carries this work is `0.16.0` (additive; see
[versioning](../../../../versioning.md)).

## Done when

Must-have unless marked Should.

### Plugin and skills

- **REQ-001 Plugin install.** The repository is a Claude Code plugin marketplace with one plugin, `mtg-mcp`, that
  registers the MCP server and ships the skills.
  - Acceptance: on a clean Claude Code profile, `/plugin marketplace add nccurry/mtg-mcp` then
    `/plugin install mtg-mcp@mtg-mcp` makes the mtg-mcp tools and the five skills available after a restart, with
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
  - Validation: phase 0 settles the launch command (OQ-1); phase 5 extends the release workflow, which today only
    stamps `server.json` from the release tag after the NuGet push (`release.yml`), so that it also stamps the
    plugin and `.mcp.json` versions and its version check covers the plugin files; phase 5 records the install
    check. The order is NuGet publish, then the stamped plugin files merge, so a pinned version is on NuGet before
    any user can install the plugin that names it.
  - A plugin manifest `version` pins users to that version until it changes, so a stale value means users never
    get an update. The version test (REQ-001) is therefore a release gate.
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
  5. Report the rule facts the user's plan depends on: `local_deck_validate` with rules `commander` (legality,
     color identity, deck size), `local_deck_duplicates_preview`, and `spellbook_deck_bracket_facts_get` for the
     bracket conversation, each as a fact with its source, never as a verdict.
  - It reports numbers and gaps and leaves the judgment to the agent and user.
  - Role counts say which tag or caller rule produced each count, because guides disagree on what counts as
    "ramp" or "removal"; the skill never states a target count of its own.
  - Acceptance: the skill content test finds each section and only registered tool names.
- **REQ-007 Archidekt-sync skill.** A skill for the pull, edit, push loop, which took 32 percent of all calls in
  the sources. Sections:
  1. Snapshot first.
  2. Pull before editing, and what to do on each `0.15.0` conflict result: `stale-deck-revision`,
     `remote-deck-changed`, `preview-changed`, and `cursor-stale`.
  3. What to do when the user edits on Archidekt during the work.
  4. Patching zones with `zoneCategories`, batching, paging with `cursor` and `pageSize`, the `confirmation`
     phrase that destructive tools require, and rate limits.
  - It also says where credentials go.
  - It uses the `0.15.0` flat argument names (`remoteDeckId`, `localDeckId`, `expectedRevision`,
    `expectedRemoteFingerprint`, `previewFingerprint`). `0.15.0` refuses old names with "Property '<name>' is not
    accepted here.", so a skill copied from a `0.14.0` shape fails on first use.
  - Acceptance: the skill content test finds each section, and every tool, input field, and option value it names
    exists in that tool's input schema.
- **REQ-008 Draw and game math skill.** A skill that maps common questions to `stats_*` tools with example inputs:
  "odds of N of X by turn T" (`stats_hypergeometric`, `stats_turn_table`), and "how many of a type do I need"
  (`stats_minimum_count`), "how many lands and which colors" (`stats_mana_availability`, `stats_castability`),
  "keep or mulligan odds" (`stats_mulligan`), "odds of assembling a package or combo" (`stats_package_assembly`),
  "on-curve odds", and "sample games with a rule" (`stats_simulate`). These are the questions Commander players
  ask that need exact math (see Domain check).
  - It says which tools are exact and which are sampled, and that `stats_simulate` applies only the rules the
    request states.
  - Acceptance: the skill content test finds an example for each question above, and every tool and input field
    it names exists in that tool's input schema.
- **REQ-018 Deck-lists skill.** A skill for bringing in, exporting, and comparing deck lists, which the sources
  show agents did by hand. Sections:
  1. Bring in a deck from another site: paste or fetch its text export and use `local_deck_import_preview`, then
     `local_deck_import_create`, instead of adding cards one change at a time.
  2. Export a deck with `local_deck_export_bundle`.
  3. Compare a deck with another deck or an Archidekt snapshot using `card_lists_compare`, including the
     `archidekt-deck` source (`remoteDeckId`) and the `archidekt-snapshot` source (`remoteDeckId` and
     `snapshotId`). The `compare-decks` prompt already covers the two-deck case; the skill points to it.
  - Acceptance: the skill content test finds each section, and every tool, input field, source kind, and option
    value it names exists in that tool's input schema.

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
- **REQ-012 Unread-card note on search.** `scryfall_search` already returns `page.totalCount`, `returnedCount`, and
  `snapshot.requestSnapshotId`. Add `unreadCards` to every page that leaves cards unread. It gives the unread count and the
  exact `card_list_query` source that reads every card of this saved search.
  - Acceptance: a 161-card search returning 30 on its first page carries `unreadCards` with 131 unread and
    `{"kind": "scryfall-snapshot", "requestSnapshotId": "<id>"}`. The last page carries none.
  - The name is `unreadCards`, not `coverage`, because `scryfall_cards_get` and `scryfall_search` card views
    already carry `tagCoverage`, which means something else (how complete the installed tag data is).
  - The note counts what the request snapshot holds; it does not claim anything about Scryfall's live result
    beyond the snapshot (Scryfall `AGENTS.md`: coverage labels must describe what was actually joined).
- **REQ-013 (Should) Tag counts on search.** When card data is installed, the first page of
  `scryfall_search` includes `tagCounts`, the 15 oracle tags most common across the result, with card counts.
  - It counts at most the first 20,000 members (`ScryfallListQuery.MaximumEntries`) and reports the cap in
    `limits` when reached.
  - Acceptance: the counts match a `card_list_query` grouped by `tags` over the same saved search.
  - `card_list_query` already groups a saved search by `tags`, so this adds convenience (one fewer call on the
    first page, and the tags appear where the agent is already reading), not a new capability. The owner made it Should on
    2026-11-07.
  - The counts come from installed tag assignments joined to the snapshot's Oracle IDs. The field says so in
    `limits`; it does not claim to be Scryfall's own query result.
- **REQ-014 (Should) Compact whole-result listing.** `card_list_query` top-level `pageSize`, when `rows` is
  requested, accepts up to 250 when every requested row field is one of `name`, `manaValue`, `typeLine`, `tags`, and `edhrecRank` (D-4).
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
  - Three prompts exist today (`build-from-commander`, `audit-deck`, `compare-decks`), and tests assert exactly
    those three, each with only required ID arguments (`WorkflowPromptTests`, `WorkflowPromptsMcpTests`,
    `src/MtgMcp.App/AGENTS.md`). `find-cards` has no deck ID, so it takes no arguments, and the rule text and
    those tests change to say prompts take only required ID arguments or none.
- **REQ-016 Usage report.** A Task command reads Claude Code transcript files from a path the user gives and prints
  counts per tool.
  - It counts tools named `mcp__mtg-mcp__*` and plugin tools named `mcp__plugin_mtg-mcp_mtg-mcp__*`.
  - It reads subagent transcripts in nested folders, and counts each call once by its call ID, because forked and
    continued sessions repeat earlier calls.
  - It reports the rows of the Sources table. The "guessed slugs that are not tags" row is reported only when a
    slug list is given, such as `oracle-tag-slugs.txt`.
  - It prints counts only. It copies no transcript text into the output or the repository.
  - Acceptance: on fixture transcripts with both tool-name forms, a subagent file, and a repeated call ID, it
    prints the expected counts; on real
    transcripts it never prints query text, deck names, or card names.
  - The Sources table is the baseline. After release the owner reruns the report and records the results here as
    observations, not as a release gate.
- **REQ-017 Skill content test.** One test covers REQ-003, REQ-005 to REQ-008, and REQ-018. For each skill it
  checks:
  - the file exists with valid front matter;
  - the listed sections are present;
  - every tool name is registered, and every named input field and option value is in that tool's input schema;
  - every tag slug is in the committed slug list;
  - every example tool call's arguments validate against the live input schema of that tool, so a renamed
    argument fails the test (`0.15.0` renamed `snapshotId` to `requestSnapshotId` for `scryfall-snapshot` sources
    and `archidektDeckId` to `remoteDeckId`; a names-only check would not catch a stale example);
  - the mechanical authoring rules in "Skill Authoring Rules" (S-1 to S-4, S-7, S-8).
  - `claude plugin validate --strict` also parses each `SKILL.md` front matter, so a malformed front matter fails
    there as well as here; malformed YAML otherwise loads the body with empty metadata and the skill never
    matches a request.
- **REQ-019 Skill evaluations.** Each skill has at least three evaluation scenarios, written before its text, in
  `docs/llms/plcs/planned/deckbuilding-agent-guidance/evals/` (moved with the packet).
  - Scenarios come from the jobs in Sources. Each gives the user request, the deck or fixture it uses, and the
    expected behavior as checkable statements, such as "calls `scryfall_tags_search` before the first
    `scryfall_search`" or "reads every card of the saved search".
  - Each skill also lists at least three requests that should load it and three that should not.
  - Method, following Anthropic's evaluation-first guidance:
    1. Run each scenario without the plugin to record a baseline.
    2. Run it again with the plugin, on Haiku, Sonnet, and Opus.
    3. Record pass or fail per statement, and whether the right skill loaded.
  - Claude Code now ships `claude plugin eval` (experimental), which runs eval cases kept in the plugin's `evals/`
    folder and can grade whether a skill was used (`tool_used: Skill`), plus a skill-creator benchmark that
    compares a skill with no skill. Phase 0 tries them on one scenario; if they work, the scenarios use that
    format and the plan's own `evals/` folder keeps only what the tool cannot express (OQ-4).
  - Runs cost model calls, so they are not part of `task ci`. They run once before release in phase
    5, and again after any skill text change.
  - Acceptance: every scenario passes on Sonnet and Opus with the plugin. Haiku failures are recorded, and fixed
    when the fix does not lengthen the skill past S-3. Every "should load" request loads its skill, and no
    "should not load" request does.

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

Evidence from every local agent transcript that called an mtg-mcp tool, as of 2026-11-02:

- 11 Claude Code transcripts (about 8 separate conversations, counting continuations once) and 1 subagent, from
  2026-10-29 to 2026-11-02. All but one call came from real deckbuilding work; the exception is one card lookup
  in a development worktree.
- No Codex session called an mtg-mcp tool.
- Calls are counted once by call ID. Forked and continued sessions repeat earlier calls in their files, and an
  earlier draft of this table counted those repeats twice.
- The 5 `scryfall_tags_search` calls made while drafting this packet are left out.

The sample is small and comes from one user. All of it was measured before `0.15.0`, which renamed many arguments
and result fields (see the `0.15.0` entry in [CHANGELOG.md](../../../../../CHANGELOG.md)); some of the failed calls below
used shapes that are now refused, so the counts show agent behavior, not today's failure rate.

| Observation | Count |
| --- | --- |
| mtg-mcp tool calls | 229 |
| `scryfall_search` calls | 32 (30 distinct queries) |
| Searches matching rules text (`o:`) | 22 of 32; 4 of them regular expressions |
| Searches using `otag:` | 3 of 32, in 3 sessions |
| Guessed slugs that are not tags | 2 of the 3 tag searches guessed: `board-wipe` (the tag is `sweeper`) and `token-generator` (the tag is `repeatable-token-generator`) |
| Result of those guesses | Both searches succeeded (623 and 746 cards) with an empty `warnings` list: the wrong tags failed silently inside `or` groups |
| `scryfall_tags_search` calls | 2, both in one session on 2026-11-02, before its one search with real slugs |
| `scryfall_installed_cards_by_tag` calls | 0 |
| First search pages that left cards unread | 11 of the 16 whose counts could be read; 705 cards unread in total |
| Search follow-ups with a cursor | 2 |
| `card_list_query` calls | 8, all with a `deck` source; none with the `scryfall-snapshot` source that reads a whole search |
| Searches ordered by `edhrec` (popularity) | 25 of 32 |
| Archidekt sync calls (pull and push, preview and apply) | 73 of 229 (32 percent) |
| Failed sync calls | `archidekt_push_preview` 3, `archidekt_pull_preview` 3, `archidekt_push_apply` 2, `archidekt_snapshot_create` 1 |
| MCP prompts run | 0 |

Jobs the sessions did, which the skills map to:

| Job | Sessions | What the agent did |
| --- | --- | --- |
| Find cards for a theme, role, or upgrade | 7 | Text and regular-expression searches, ordered by popularity |
| Review a deck against the user's plan | 4 | Read the deck and reasoned about it; `card_list_query` counts in 1 session and draw odds in 2 |
| Sync edits with Archidekt | 4 | Pull, patch, push loops, with conflicts when the user edited during the work |
| Draw odds | 2 | `stats_hypergeometric` |
| Bring in decks from another site | 1 | Fetched Moxfield pages with WebFetch and added cards change by change; `local_deck_import_preview` was not used |
| Compare a deck with an Archidekt snapshot | 1 | Read both and compared them with a PowerShell script; `card_lists_compare` with `archidekt-snapshot` sources was not used |

Concrete failures:

- An agent asked for "flying token makers" in white-blue wrote the regular expression
  `o:/create .* (token|tokens) with flying/`, read 55 of 161 results, and ranked from the most popular. No tag
  marks "creates a flying token", so the exact query needs the token facts in REQ-011.
- `scryfall_tags_search` with `board-wipe` returns nothing, though `sweeper` has the alias `boardwipe`. The
  `sweeper-one-sided` tag the user wanted ("2-3 one-sided board wipes") was never found.
- Agents wrote text matches such as `o:goad` and `o:landfall` where tags exist.

What works: in the newest session (2026-11-02, "forced combat" deck), the agent called `scryfall_tags_search`
for `goad` and `attack` first, then searched with four real slugs. It also ran a rules-text search at the same
time. So agents can follow the tag-first order; the change is to make it the usual path, not a rare one.

What the server already says: the server instructions, the `scryfall_search` and `scryfall_tags_search`
descriptions, and the `build-from-commander` prompt all mention `otag:`. Most agents still did not use tags, and no
session ran a prompt. Advice placed in descriptions is not enough; the guidance has to be in the skill the agent
loads for the job and in the result the agent reads next.

## Domain check

Checked on 2026-11-05 against Commander deckbuilding guides and tools. Sources are mostly community and vendor
pages dated 2022 to 2026, so the table states community practice (the land and role counts are guide
conventions, and the bracket list changes over time, so read it from Wizards of the Coast), not a measured rate; no published study measures how
often language-model agents fail at these tasks.

| Job a Commander deckbuilder does | Evidence it needs | Covered by | Plan item |
| --- | --- | --- | --- |
| Choose a commander and theme | Oracle text, color identity, theme tags (`otag:`) | `scryfall_search`, `scryfall_tags_search` | REQ-003, REQ-009 to REQ-011 |
| Fill role slots (about 36 to 38 lands, about 10 each of ramp, card draw, removal) | Counts per role, with the rule that produced each count; guides disagree on what counts as "ramp" | `card_list_query` grouped by `tags`; tag counts on search | REQ-006, REQ-013 |
| Check legality, color identity, and singleton | Rule facts with source | `local_deck_validate`, `local_deck_duplicates_preview` | REQ-006 |
| Set the bracket conversation (Game Changers list, extra turns, early two-card combos) | Facts with list date and source; the list changes between releases | `spellbook_deck_bracket_facts_get`, `spellbook_deck_combos_find` | REQ-006 |
| Build the mana base and curve | Colored-source counts per pip, mana value distribution | `stats_castability`, `stats_mana_availability`, `stats_local_deck_summary` | REQ-008 |
| Opening hand and mulligans (free first mulligan, then London) | Keep probability per mulligan depth under a stated keep rule | `stats_mulligan` | REQ-008 |
| Draw odds and package or combo assembly by turn N | Exact hypergeometric and joint-draw math | `stats_hypergeometric`, `stats_turn_table`, `stats_minimum_count`, `stats_package_assembly` | REQ-008 |
| Goldfish and tune | Seeded sampling under stated rules | `stats_simulate` | REQ-008; `simulate-damage-rules` |
| Import, export, and compare lists | Deck text, snapshots, set differences | `local_deck_import_*`, `local_deck_export_bundle`, `card_lists_compare` | REQ-018 |
| Keep a local deck and Archidekt in step | Pull, preview, push, verify | `archidekt_*` | REQ-007 |

Pain points practitioners and vendors report for language-model deckbuilding: invented card names, cards outside
the commander's color identity, stale banlists and bracket lists, wrong hand counts of role cards, ignoring the
requested bracket, treating popularity as quality, and guessing odds instead of computing them. Most map to a
tool the server already has (card lookup, validation, rule facts, exact math), and agents do not reach for it by
default, which is the problem this plan solves; invented card names are handled by looking cards up, not by a
dedicated tool. The skills therefore name the tool and the facts to report, and never a target count or a card.

Needs this plan leaves out, each a separate packet if the owner wants it:

- A commander damage clock for voltron decks: `simulate-damage-rules`.
- Budget and owned-collection deltas (prices come with Scryfall data, but collection reading is a Non-Scope item).
- Popularity with sample size and date: blocked on a permitted source (`docs/potential-features.md`).

## Scope

- A Claude Code plugin and marketplace manifest in this repository.
- Five skills: card search, deck review, Archidekt sync, draw and game math, and deck lists (import and compare).
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
- New fields and values follow [MCP surface](../../../../architecture/mcp-surface.md): camelCase properties,
  kebab-case fixed values, a description on every input field, "Only with ..." on conditional fields, descriptions
  within 4,096 characters, and a `path` on every invalid-input result. Architecture tests enforce these
  (`ToolNamingRuleTests`, `ToolSchemaSurfaceTests`, `ToolInputDescriptionTests`, `ToolSurfaceRulesTests`).
- Facts joined from installed card data say what was joined. A tag hint, tag counts, and token facts are labeled
  installed-data facts, never Scryfall query results (`src/MtgMcp.Scryfall/AGENTS.md`).
- Skills, prompts, and examples use `0.15.0` names. `0.15.0` refuses old argument names with no alias.
- Installed tags can lag behind Scryfall's live tags. Every tag check says "not in installed tag data from
  <date>", never "not a tag".
- No secrets in plugin files. Credentials reach the server the ways they do today (D-7).

## Skill Authoring Rules

These rules come from Anthropic's and OpenAI's published skill guidance, read on 2026-11-02 and re-read on
2026-11-05 (corrections below):

- [Anthropic: Skill authoring best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- [Claude Code: Skills](https://code.claude.com/docs/en/skills)
- [OpenAI: Build skills](https://learn.chatgpt.com/docs/build-skills) (formerly `developers.openai.com/codex/skills`)

Where the two differ, the stricter rule applies, so one skill folder works in both. REQ-017 checks the rules marked
"tested"; review checks the rest.

| ID | Rule | Source | Check |
| --- | --- | --- | --- |
| S-1 | Front matter has `name` and `description`. `name` is at most 64 characters, uses only lowercase letters, digits, and hyphens, and contains neither `anthropic` nor `claude`. `description` is non-empty, at most 1,024 characters, and has no XML tags (angle brackets show escaped in Claude Code, so use square brackets). Claude Code itself needs only `description`; the stricter rule keeps one folder valid for both the Claude API and Claude Code. | Anthropic | Tested |
| S-2 | The description is in the third person and says what the skill does and when to use it, with the main use case and trigger words first. It also says when not to use it. Claude Code cuts the listing at 1,536 characters (`description` plus `when_to_use`), and both clients budget the whole skill listing (Claude Code 1 percent of the context window, Codex 2 percent, with 8,000 characters when the window is unknown) and shorten descriptions first when many skills are installed, so the start must carry the meaning. "When not to use" comes from OpenAI's trigger-testing advice. | Anthropic, Claude Code, OpenAI | Tested for length; review for wording |
| S-3 | The `SKILL.md` body stays under 500 lines. Detail moves to reference files. The most important instructions come first, because after compaction Claude Code can keep only the start of an invoked skill. | Anthropic, Claude Code | Tested for length; review for order |
| S-4 | Reference files are linked directly from `SKILL.md`, never from another reference file. A reference file over 100 lines starts with a table of contents. The tag reference also gives search patterns in `SKILL.md`, because it is long. | Anthropic | Tested |
| S-5 | One skill per job. Each fact lives in one place: `SKILL.md` or a reference file, not both. The skill folder holds only files the skill uses, with no README or CHANGELOG (the one-job rule is OpenAI's; the no-README rule is this project's own). | OpenAI, project | Review |
| S-6 | Add only what the model does not already know. Skip explaining Magic rules or what a Scryfall search is; explain this server's tools, fields, tag names, and limits. | Anthropic | Review |
| S-7 | Skill text names a tool by its bare registered name, as `scryfall_search`. Every mtg-mcp tool name already starts with its source (`scryfall_`, `local_deck_`, `stats_`), so it is unique across servers, and clients expose it differently: Claude Code as `mcp__plugin_mtg-mcp_mtg-mcp__scryfall_search` (plugin) or `mcp__mtg-mcp__scryfall_search` (hand-registered), and the Claude API docs recommend `ServerName:tool_name`. Claude Code's tool search matches on the tool name, so the bare name resolves in both setups. REQ-019 confirms this on real runs (OQ-5). | Anthropic, Claude Code | Tested |
| S-8 | Paths use forward slashes. No dates or "new in" notes in skill text; the only date is the tag reference's data stamp. | Anthropic | Tested |
| S-9 | Freedom matches risk. Archidekt sync is low freedom: an exact order with a copyable checklist, and the preview, check, apply pattern. Card search is medium: a default query shape with a fallback. Deck review is high: steps to follow, with the judgment left to the agent. | Anthropic | Review |
| S-10 | One default per step, with at most one fallback, never a menu of options. For example: "Look up tag slugs with `scryfall_tags_search`; match rules text only when no tag fits." | Anthropic | Review |
| S-11 | One term per idea, used the same way in every skill: "oracle tag", "saved search", "card data", "deck entry". | Anthropic | Review |
| S-12 | Steps are imperative, with explicit inputs and outputs. Examples are concrete input and result pairs. | OpenAI, Anthropic | Review |
| S-13 | Instructions, not scripts. These skills call MCP tools, and no step needs a bundled script. | OpenAI | Review |
| S-14 | Every skill can be loaded by the model: none sets `disable-model-invocation`. Writes are already guarded by operation mode and Claude Code's approval of destructive tools. | Claude Code | Review |
| S-15 | Write evaluations before the skill text, measure a baseline without the skill, and test on every model the skill will run on (REQ-019). The trigger set holds direct, indirect, and negative requests, and negative-request precision comes before recall. | Anthropic, OpenAI | REQ-019 |
| S-16 | Pre-approving tools with `allowed-tools` is limited to read-only tools, and a skill never pre-approves an apply or delete tool. Operation mode and the destructive-tool `confirmation` stay the write boundary. | Claude Code | Review |

## Public Surface Changes

No tool is added or removed, and no tool annotation or operation-mode visibility changes.

| Surface | Change | Compatibility |
| --- | --- | --- |
| Plugin | New `.claude-plugin/marketplace.json` and `plugins/mtg-mcp/` with `plugin.json`, `.mcp.json`, and `skills/` | Additive. Installing through `dotnet tool` keeps working. |
| `scryfall_search` | `unreadCards` on pages with unread cards; `tagCounts` on the first page; unknown-tag hints; `createsTokens` added to the `fields` choices | Additive fields. The default concise set is unchanged. |
| `scryfall_tags_search` | Matching ignores hyphens, spaces, underscores, and case | Returns more matches for the same text. |
| `scryfall_cards_get` | `createsTokens` added to the `fields` choices | Additive. |
| `card_list_query` | `tokenKeywords` and `tokenTypes` fields; top-level `pageSize` up to 250 for compact rows | Additive. |
| Prompts | New `find-cards` (no arguments); `audit-deck` gains a step | Prompts are shown in every mode, as today. The "only required ID arguments" test rule becomes "required ID arguments or none". |
| Instructions | Reworded first job line | Text only. |

Surface tests that change: the concise field list, the `card_list_query` field list, the prompt list
(`WorkflowPromptTests`, `WorkflowPromptsMcpTests`), the instruction text snapshot (`ServerInstructionsTests`), and
the `card_list_query` `pageSize` description and schema. The architecture tests in Constraints run unchanged and
must stay green. The `docs/reference.md` line about concise fields (the `concise` row of the format table) gains
`createsTokens` as a `fields` choice.

Examples:

```text
> scryfall_search  query: "otag:board-wipe id<=wu f:commander"
not-found: Scryfall found no cards.
  otag:board-wipe is not in installed tag data from 2026-11-01. Closest: sweeper, sweeper-one-sided.

> scryfall_search  query: "otag:sweeper-one-sided id<=wu f:commander"  pageSize: 25
page.totalCount: 40, returnedCount: 25
unreadCards: 15 cards not on this page. card_list_query with source
          {kind: scryfall-snapshot, requestSnapshotId: ...} reads all 40.
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
- **D-4, compact rows within the existing size limit.** `card_list_query`'s top-level `pageSize` (in `0.15.0` it
  replaced `rows.pageSize`) stays capped at 100 for other row fields.
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
- **The unread-card note and tag counts use the saved search.** Every `scryfall_search` already saves its full result as a
  request snapshot with members. The unread count comes from the snapshot header. Tag counts read each member's Oracle ID
  and join to installed tag assignments, capped as REQ-013 says; phase 0 measures the cost alongside D-5.
- **Skills' content rules.** Each skill has a one-paragraph overview, the steps with tool names, worked examples,
  the facts to report, and the limits of each fact. It follows the authoring rules S-1 to S-16. No card is called
  good or bad. Example decks and cards are illustrations of a query, not suggestions.

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
    deck-lists/SKILL.md
```

Project boundaries:

- `MtgMcp.Scryfall` owns tag normalization, the tag check, token projection, snapshot tag counts, the
  `card_list_query` field table (`ScryfallListFields`, so `tokenKeywords` and `tokenTypes` live here), and the
  search result shapes (`ScryfallSearchResponse`, `limits`, `warnings`), which are defined there today. A token
  field needs a new installed-data need (`Tokens`) beside `Tags` and `CheapestPrice` in
  `ScryfallInstalledDataNeed`, which the concise-field table already drives.
- `MtgMcp.App` owns tool descriptions, `card_list_query` source inputs and page limits, prompts, and server
  instructions.
- Core is unchanged.
- The generator and usage report are scripts that Task calls.

Tests:

- Unit tests for normalization, the tag-check parser forms, hints on success and on not-found, unread-card note, tag counts
  and their cap, and token projection. Fixtures come from recorded data: Spectral Procession, and a card with a
  "Copy" token.
- Surface tests for the changes listed under Public Surface Changes.
- The skill content test (REQ-017), the manifest version test, and `claude plugin validate --strict`.
- Generator and usage-report tests on fixture inputs.

## Risks And Open Questions

- **OQ-1, launch command (blocks REQ-002 and D-6).** Two options:
  - Run the NuGet tool package with `dnx Nccurry.MtgMcp@<version>`, which may need `--yes` to skip a first-run
    prompt.
  - Require `dotnet tool install` first and run `mtg-mcp`, which matches what users register today.
  - The earlier draft said either option needs a .NET 11 runtime. The shipped tool is RID-specific and published
    self-contained (`src/Directory.Build.props`, `Taskfile.yml`), and the README says users need a .NET SDK 10 or
    newer to install it, which also provides `dnx` (Microsoft documents `dnx <Package>@<version> --yes` for MCP
    servers). Claude Code's docs do not cover `dnx`. Phase 0 runs both options on a clean machine and profile and
    records what a user without the repository must install, including whether the package needs a runtime beyond
    the SDK.
- **OQ-2, Scryfall's unknown-tag behavior (sets the REQ-009 failure field).** Partly answered by the sources: an
  unknown tag inside an `or` group gives a normal result with an empty `warnings` list, so the success path needs
  the hint most. A search that matches nothing returns Scryfall's 404, which the server reports as
  `scryfall-not-found`. What Scryfall returns for an unknown tag on its own (404, 400, or a warning) is unverified.
  Phase 0 records it from one live query.
- **OQ-3, other clients.** OpenAI's guidance says plugins carry skills and MCP configuration to Codex and
  ChatGPT, and that Codex finds repository skills in `.agents/skills`. The skill text follows the shared rules
  (S-1 to S-16), so a Codex plugin can reuse the same folders. Building that plugin is deferred until the Claude
  Code plugin is in use. One difference: Codex declares MCP tool dependencies in `agents/openai.yaml`.
- **OQ-4, automated evaluation.** The first draft said evaluations are run by hand. `claude plugin eval` (marked
  experimental) now runs eval cases with a skill-use grader, and could run on every skill change, at the cost of
  model calls on each run. Phase 0 tries it on one scenario. Running it in CI stays deferred unless the owner
  wants it.
- **OQ-5, tool name in skill text.** S-7 now uses the bare registered name. Phase 4 checks through the REQ-019
  runs that agents resolve it in both a plugin install (`mcp__plugin_mtg-mcp_mtg-mcp__<tool>`) and a hand-
  registered server (`mcp__mtg-mcp__<tool>`). If they do not, the skills switch to a form that does, and the
  REQ-017 check is updated to match.
- **OQ-7, tool-surface self-check against current MCP guidance (waits on @nccurry).** The 2026-11-05 guidance
  re-read found checks the server could run on itself, none of them part of this plan: all four annotation hints
  set explicitly on every tool; every success and error path conforming to its `outputSchema`; validation and
  not-found failures returned as tool errors with a next step; the lifetime of preview and snapshot IDs stated in
  the `*_preview` descriptions; descriptions that put the words a tool search matches first; large results
  staying under Claude Code's 25,000-token cap (or raising it per tool with `anthropic/maxResultSizeChars`);
  and a deterministic tool order. Recommended: a separate small audit packet, not scope here.
- Risk: skill text drifts from the tool surface. REQ-005 and REQ-017 fail on drift.
- Risk: a skill that tests well in the content checks still does not change behavior. REQ-019 compares against
  a baseline, and the usage report (REQ-016) checks real sessions after release.
- Risk: the plugin version and server version drift. The plugin pins the server version, and the release workflow
  stamps both from the release tag, as it does for `server.json`. A stale plugin `version` also blocks plugin
  updates for users, so the version test gates the release.
- Risk: skill examples go stale after a surface rename, as they would have across `0.15.0`. REQ-017 validates
  example arguments against the live input schemas.
- Risk: Claude Code's tool search defers MCP tool definitions, so 86 tools do not cost context there, but clients
  without tool search load every tool, and OpenAI advises fewer than 20 tools up front. Toolsets are the lever; the
  README documents a small recommended toolset (see OQ-7 for related checks).
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
| Baseline usage counts (Sources) | 2026-11-02 | Recounted across 1,016 Claude transcript files (including subagent folders) and 2,722 Codex session files. Method: parse each `tool_use` block for `mcp__mtg-mcp__*` tools, count each call ID once, and match its `tool_result`. The first draft's counts (361 calls) double-counted repeated calls and were replaced. |
| Tag slugs checked with `scryfall_tags_search` | 2026-11-02 | `sweeper`, `sweeper-one-sided`, `repeatable-token-generator`, `repeatable-creature-tokens`, `card-advantage` exist; `board-wipe` returns no tag; no tag for "creates a flying token". |
| Independent PLC review | 2026-11-02 | 17 findings, all accepted and fixed in this revision. Plugin layout, duplicate-server rule, plugin tool naming, and `claude plugin validate` checked against the Claude Code docs by the reviewer. |
| Currency check against `main` at `d532ae5` (`0.15.0`) | 2026-11-05 | Every code path, count, limit, and field name in this packet was checked against the code by a read-only review. Confirmed: 86, 73, and 59 tools by mode; `ServerInstructions.MaximumLength` 2,000 (current text about 1,670); `ScryfallListQuery.MaximumEntries` 20,000; `raw_json` holds `all_parts`; `provider_updated_at_utc`; no tag-slug check in `scryfall_search`; tag search is a substring match. Corrected here: `snapshotId` to `requestSnapshotId`, `archidektDeckId` to `remoteDeckId`, `rows` page size to top-level `pageSize`, the .NET 11 runtime claim, the release-version source, `card_list_query` field ownership, the prompt count (three, not two), and REQ-007 conflict codes. |
| Skill and MCP guidance re-read | 2026-11-05 | Primary pages from Anthropic (skill best practices, Claude Code skills, plugins, MCP, and the tool-writing post), OpenAI (Codex skills, Apps SDK metadata, function calling), and the MCP specification (revision 2026-07-28). S-6 and S-8 to S-14 confirmed unchanged; S-1, S-2, S-3, and S-15 confirmed with added notes (angle brackets, listing budget, compaction, trigger set); S-4 and S-5 relabeled; S-7 changed to bare tool names; S-16 proposed; OQ-4 revised for `claude plugin eval`; OQ-7 added. Claude Code docs do not cover `dnx`, so OQ-1 stays open. |
| Commander practice check | 2026-11-05 | Eight web searches over community guides and tools, summarized in Domain check. Karsten color-source figures and the Game Changers list size were not verified against primary sources, so the plan states neither. |

## Log

- 2026-11-05 Converted to the shared PLC template (format only).
- 2026-11-05 Decision: owner asked for a summary under the title, written from `## Outcome`; the Outcome section stays as it was.
- 2026-11-05 Brought current with `main` at `d532ae5` (`0.15.0`): corrected renamed fields and arguments, the launch-runtime claim, the release-version source, and the prompt rules; REQ-012's field is now `unreadCards`.
- 2026-11-05 Added the Domain check section and widened the deck-review (REQ-006) and game-math (REQ-008) skills to the tools that already cover the jobs it lists.
- 2026-11-05 Updated the Skill Authoring Rules from a re-read of current Anthropic and OpenAI guidance (S-4, S-5, S-7 corrected; S-1, S-2, S-3, S-15 annotated; S-16 proposed); added OQ-6, OQ-7, and OQ-8 for the owner. Items that add scope are marked "proposed" in the text and listed in OQ-8 until the owner accepts them.
- 2026-11-07 Decision: OQ-6 answered by the owner. REQ-012 stays Must; REQ-013 is Should until phase 0 measures its cost. Phase 2 no longer waits on an open question.
- 2026-11-07 Decision: OQ-8 accepted by the owner. The "proposed" markers are removed from REQ-006 step 5, the REQ-006 role-count rule, REQ-008's added questions, the REQ-017 example-schema check, and S-16. The release-workflow extension, the `llms.txt` and `.codex/` updates, and the README recommended-toolset note are in scope.
