# Evidence-First Rewrite Guide

This document is the bridge between the currently shipped pre-rewrite server
and the planned clean-break `0.9.0` server. It prevents current implementation
documentation from being mistaken for the rewrite target.

The rewrite has entered foundation implementation. The legacy audit is
approved/completed, the foundation child is approved/in progress, and the
remaining eight children stay planning-only with
`Implementation authorized: No`. Production edits are limited to the active
foundation child and its current authorized phase.

## Authority And Routing

Use these sources in order for the kind of question being answered:

1. For what the current checkout actually does, use code, tests, project files,
   configuration, `Taskfile.yml`, and the current-release sections of
   [README.md](../README.md).
2. For repository-wide coding rules, use [AGENTS.md](../AGENTS.md) and the
   closest scoped `AGENTS.md`.
3. For the rewrite's shared product and architecture constraints, use the
   [umbrella PLC](llms/plcs/in-progress/evidence-first-mcp-rewrite-program/README.md).
4. For one rewrite implementation, use only its independently approved child
   packet: README, SRD, SADD, implementation plan, and fixtures.
5. Treat legacy implementation docs and code as reference evidence. Do not
   copy their abstractions into the rewrite unless the approved audit/child
   packet explicitly retains them.

If an approved child conflicts with an umbrella guardrail, stop and amend the
umbrella first. Reviewing or implementing one child must not silently change
another.

## Current Server Versus Rewrite Target

| Concern | Current pre-rewrite server | Clean-break `0.9.0` target |
| --- | --- | --- |
| Product role | Evidence plus recommendation, intent, plan, scoring, and simulation features | Evidence, provider data, explicit workflow operations, and exact mathematics; the client LLM decides |
| MCP modes | `read-only`, `plan`, `apply` | `read-only`, `local` (default), `remote` |
| Public surface | Legacy workspace-oriented tools, resources, and prompts | Capability-prefixed `deck_*`, `scryfall_*`, `archidekt_*`, `playgroup_*`, `stats_*`, and `tagger_*`; one capability resource; zero prompts |
| Surface size | Audit baseline: 118 tools, 16 resources, 18 prompts | Current derived planning baseline: 84 tools, one resource, zero prompts; counts follow approved design and are not compatibility targets |
| Core | Large legacy domain containing plans, recommendations, simulation, provider abstractions, and file persistence | Dependency-light provider-neutral evidence, identifiers, failures, and shared contracts only |
| Modules | Existing Core/App plus Scryfall, Archidekt, Moxfield, Playgroup, Commander Spellbook, and decklist projects | Core, App, Decks, Scryfall, Archidekt, Playgroup, Statistics, and Tagger |
| Persistence | Legacy file-oriented workspaces, plans, collection, and caches | Independent versioned `decks.db`, `scryfall.db`, and `tagger.db` stores |
| Compatibility | Existing pre-1.0 deprecation policy | Intentional clean break with no automatic legacy data, config, or tool-schema migration |
| Moxfield | Automated unofficial import adapter | Manual interchange artifacts only; no network automation |
| Tagger | Curated tag directory/`otag:`-style evidence, not a complete per-card cache | Cache-only reads plus separately invoked, bounded unsupported acquisition if the owner accepts the provider-risk record |
| Simulation and recommendations | Implemented legacy features | Not in stable `0.9.0`; separately reviewed post-cutover possibilities only |

The exact current baseline and deletion/reuse allowlists live in the
[legacy audit](llms/plcs/completed/legacy-surface-audit-and-disposition/README.md).
The target surface count is an internal reconciliation check, not a requirement
to preserve or invent tools.

## Stable Rewrite Capabilities

The required planning sequence covers:

1. legacy surface audit and disposition;
2. repository foundation and minimal MCP host;
3. revisioned local deck domain and SQLite store;
4. offline manual deck interchange;
5. immutable official Scryfall evidence snapshots;
6. explicit Archidekt deck lifecycle, conflict-safe synchronization, folder
   organization, and named snapshot lifecycle/restore;
7. the documented Playgroup public API;
8. exact provider-independent deck statistics;
9. exact cached Scryfall Tagger evidence with bounded explicit acquisition; and
10. stabilization and `0.9.0` cutover.

Provider facts, exact derivations, sampled estimates, parser classifications,
heuristics, and unknown states must remain visibly different. Stable `0.9.0`
contains no advisor prompts, intent inference, weak-card judgments,
replacement recommendations, blended quality scores, or strategic automation.

## Starting A Rewrite Child

Before the first production edit for a child:

1. Confirm its dependencies are approved.
2. Complete independent review and record approval in the child README.
3. Set `Implementation authorized: Yes` only with explicit repository-owner
   authority and move that child to `in-progress/`.
4. Use the sibling worktree and `ncurry/evidence-first-mcp-rewrite` branch
   required by the program after foundation prerequisites have landed.
5. Read the child files in order: README, SRD, SADD, implementation plan,
   fixtures.
6. Implement only the active child/phase, keep normal tests offline, and update
   acceptance evidence as work proceeds.

## Deferred Work

Popularity/tournament sources, goldfish feasibility, weakness evidence, budget
alternative evidence, and conditional provider expansions live in
[Potential Features](potential-features.md). They are not cutover dependencies
or authorization to retain similar legacy code.
