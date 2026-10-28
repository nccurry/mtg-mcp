# Evidence-First Rewrite Guide

This document records the clean-break `0.9.0` design and release. Use it to
understand the lasting product and architecture boundaries, not as an active
implementation plan.

All twelve rewrite children and the `0.9.0` cutover are complete. The
stabilization child added no capability.

## Authority And Routing

Use these sources in order for the kind of question being answered:

1. For what the current checkout actually does, use code, tests, project files,
   configuration, `Taskfile.yml`, and [README.md](../README.md).
2. For repository-wide coding rules, use [AGENTS.md](../AGENTS.md) and the
   closest scoped `AGENTS.md`.
3. For the rewrite's shared product and architecture constraints, use the
   [umbrella PLC](llms/plcs/completed/evidence-first-mcp-rewrite-program/README.md).
4. For one rewrite implementation, use only its independently approved child
   packet: README, SRD, SADD, implementation plan, and fixtures.
5. Treat legacy implementation docs and code as reference evidence. Do not
   copy their abstractions into the rewrite unless the approved audit/child
   packet explicitly retains them.

If an approved child conflicts with an umbrella guardrail, stop and amend the
umbrella first. Reviewing or implementing one child must not silently change
another.

## Legacy Server Versus Released `0.9.0`

| Concern | Released legacy server | Released clean-break `0.9.0` |
| --- | --- | --- |
| Product role | Evidence plus recommendation, intent, plan, scoring, and simulation features | Evidence, provider data, explicit workflow operations, exact mathematics, and one bounded sampled estimate; the client LLM decides |
| MCP modes | `read-only`, `plan`, `apply` | `read-only`, `local`, `remote` (default since the focused tool surface; `0.9.0` defaulted to `local`) |
| Public tools and API | Legacy workspace-oriented tools, resources, and prompts | Current surface: 20 `local_deck_*`, 13 `scryfall_*`, 2 `card_list*`, 9 `stats_*`, 21 `archidekt_*`, 17 `playgroup_*`, and 4 `spellbook_*` tools, one `mtg://server/capabilities` resource, and zero prompts (`0.9.0` shipped 29 `deck_*`, 18 `scryfall_*`, 8 `stats_*`, 23 `archidekt_*`, 16 `playgroup_*`, and 3 `spellbook_*` tools) |
| Tool count | Audit baseline: 118 tools, 16 resources, 18 prompts | Current `default` and `all` are 35/48/48 tools by mode with no provider credentials and 59/73/86 with Archidekt and Playgroup credentials, with one resource and zero prompts; counts are checks, not compatibility targets |
| Core | Large legacy domain containing plans, recommendations, simulation, provider abstractions, and file persistence | Dependency-light provider-neutral evidence, identifiers, failures, and shared contracts only |
| Modules | Existing Core/App plus Scryfall, Archidekt, Moxfield, Playgroup, Commander Spellbook, and decklist projects | Core, App, Decks, Scryfall, Archidekt, Playgroup, Spellbook, Statistics, and OnCurve |
| Persistence | Legacy file-oriented workspaces, plans, collection, and caches | Independent versioned `decks.db`, unified `scryfall.db`, and short-lived `spellbook.db` response cache |
| Compatibility | Existing pre-1.0 deprecation policy | Intentional clean break with no automatic legacy data, config, or tool-schema migration |
| Moxfield | Automated unofficial import adapter | Manual interchange artifacts only; no network automation |
| Community tags | Curated `otag`/`atag` evidence remains distinct from card facts | Official Scryfall bulk tags join the installed card data; no separate Tagger adapter, database, toolset, or website acquisition |
| Simulation and recommendations | Implemented legacy features | Not in stable `0.9.0`; separately reviewed post-cutover possibilities only |

The exact current baseline and deletion/reuse allowlists live in the
[legacy audit](llms/plcs/completed/legacy-surface-audit-and-disposition/README.md).
The target tool count is an internal check, not a requirement to preserve or
invent tools.

## Stable Rewrite Capabilities

The original required planning sequence covers:

1. legacy surface audit and disposition;
2. repository foundation and minimal MCP host;
3. revisioned local deck domain and SQLite store;
4. offline manual deck interchange;
5. startup-selected capability toolsets with a focused default profile;
6. unified official Scryfall card data, authoritative query cache, tag evidence,
  and immutable request snapshots;
7. explicit Archidekt deck lifecycle, conflict-safe synchronization, folder
   organization, and named snapshot lifecycle/restore;
8. the documented Playgroup public API;
9. pre-statistics MCP contract and adapter hardening, including exact-only deck
   identity reconciliation and no legality behavior;
10. exact provider-independent deck statistics;
11. deterministic caller-configured deck categorization over installed Scryfall tag
   evidence; and
12. stabilization and `0.9.0` cutover.

Every stable tool belongs to one toolset. Every toolset is on by default;
`archidekt` and `playgroup` turn on only when their credentials are present.
Toolsets control relevance, modes control authority, and registration remains
static for an MCP session. Stabilization passed its north-star workflow check
alongside schema, package, and release acceptance.

Items 6 and 11 reflect accepted umbrella amendment AMEND-004. Item 9 reflects
accepted AMEND-005. All twelve original rewrite children are complete.

The separately approved Commander Spellbook evidence child adds three opt-in
provider tools after the rewrite cutover. They return bounded source evidence
only; it does not reopen the original rewrite's removed recommendation or
simulation features.

Completed PLCs remain accurate evidence for the revision they implemented, but
they do not override a later reviewed umbrella amendment for unfinished work.
Current code and tests continue to define the implemented runtime.

Provider facts, exact derivations, sampled estimates, parser classifications,
heuristics, and unknown states must remain visibly different. Stable `0.9.0`
contains no advisor prompts, intent inference, weak-card judgments,
replacement recommendations, blended quality scores, or strategic automation.

## Use This Completed Record

The rewrite program is closed. Do not use one of its completed children as
authorization for new production work. For a new cross-cutting change:

1. Start a new PLC with its own scope, design, review, and owner approval.
2. Preserve the boundaries recorded here unless the new PLC explicitly changes
   them.
3. Keep normal tests offline and record focused and broad validation in the
   new packet.

## Deferred Work

Popularity/tournament sources, goldfish feasibility, weakness evidence, budget
alternative evidence, and conditional provider expansions live in
[Potential Features](potential-features.md). They are not cutover dependencies
or authorization to retain similar legacy code.
