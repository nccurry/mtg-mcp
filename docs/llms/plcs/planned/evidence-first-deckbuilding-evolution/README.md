# Evidence-First Deckbuilding Evolution PLC Packet

> [!IMPORTANT]
> This is an umbrella planning packet. It does not authorize production edits.
> Each implementation child needs independent review, an explicit owner decision,
> and its own “Implementation authorized: Yes” before code changes begin.

## Lifecycle

- Status: Planned
- Folder: docs/llms/plcs/planned/evidence-first-deckbuilding-evolution/
- Owner: mtg-mcp
- Created: 2026-09-06
- Last updated: 2026-09-06
- Current phase: planning review
- Implementation authorized: No

## Summary

Stable 0.9 already has the right product boundary: mtg-mcp gathers evidence,
calculates declared mathematics, and performs explicit guarded workflows. The
LLM and player decide what a deck should do.

This program makes that boundary easier to maintain and expands it carefully.
The first delivery is structural: make the Scryfall and Archidekt owners real
instead of forwarding wrappers around large context classes. Later, separately
reviewed children may add more reliable evidence sources, stronger exact deck
analysis, and a tightly bounded goldfish experiment.

The product can be described in three verbs:

1. Collect card, deck, and source evidence.
2. Calculate exact answers from declared inputs.
3. Execute explicit local or remote deck workflows.

It must not add a fourth verb: decide.

## Packet Contents

- [AUDIT.md](AUDIT.md): baseline findings, validation evidence, and what to
  keep versus change.
- [SRD.md](SRD.md): product outcomes, requirements, scope, and acceptance
  criteria.
- [SADD.md](SADD.md): target architecture, provider rules, error policy, and
  experimental-simulation boundary.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): independently reviewable
  delivery sequence.
- [FIXTURES.md](FIXTURES.md): future fixture, contract, and calibration
  inventory.

## Decision Snapshot

| Decision | Status | Rationale | Detail |
| --- | --- | --- | --- |
| Preserve evidence before advice. | Inherited | It is the current north star and the right product boundary. | [SRD](SRD.md#requirements) |
| Refactor ownership before expanding sources. | Proposed | A new provider or simulation on top of forwarding contexts would compound the hardest maintenance problem. | [Audit](AUDIT.md#findings) |
| Keep vertical provider modules; do not add a generic provider framework. | Proposed | Scryfall, Archidekt, Playgroup, combo data, and community discussion have different contracts and safety rules. | [SADD](SADD.md#alternatives-considered) |
| Keep Statistics exact and provider-independent. | Inherited | A deck statistic is reliable only when its population and assumptions are explicit. | [SADD](SADD.md#exact-analysis-and-simulation) |
| Treat advanced goldfish as a feasibility experiment, not a stable feature promise. | Proposed | A useful bounded model may be possible, but it must not masquerade as a Magic rules engine or a matchup predictor. | [SRD](SRD.md#scope-and-non-scope) |
| Admit every external source individually. | Proposed | “More sources” is valuable only when access, meaning, retention, and provenance are reliable. | [SADD](SADD.md#provider-admission) |
| Upgrade the MCP SDK in a focused compatibility child. | Proposed | The installed SDK has a major update available; mixing it into an ownership refactor would hide regressions. | [Audit](AUDIT.md#findings) |

## Project And Surface Impact

The first child affects MtgMcp.Scryfall, MtgMcp.Archidekt, their focused tests,
architecture tests, and documentation. It does not change tool names, schemas,
operation modes, SQLite formats, or provider behavior.

Future children may affect:

- MtgMcp.App static tool registrations and capability metadata;
- one new concrete provider project at a time;
- exact Statistics workflows without provider references;
- a new isolated simulation-lab project only after feasibility approval;
- test fixtures, source documentation, package compatibility, and Task tasks.

No child may introduce automatic legacy migration, a generic request router,
automatic website scraping, MCP-owned recommendations, or an unbounded rules
engine.

## Current Open Questions

| Question | Impact | Owner | Resolution plan |
| --- | --- | --- | --- |
| Can the current ModelContextProtocol 1.4 to 2.x upgrade preserve the installed MCP client contract? | Public compatibility | mtg-mcp | Create a narrow SDK compatibility child with process, official-client, schema, and package smoke tests. |
| Does Reddit use through an MCP meet the current data terms and privacy obligations? | Legal and product safety | mtg-mcp | Do not implement it until a written policy review covers OAuth, display-only handling, retention, deletion, and non-training use. |
| Which Commander Spellbook endpoints, rate limits, and content terms are stable enough for a production adapter? | Provider reliability | mtg-mcp | Freeze an OpenAPI fixture and admission record before selecting tools. |
| Is there a permissioned deck-population source for EDHREC-style cohort analysis? | Product scope | mtg-mcp | Require an official public contract or written permission. Do not consume undocumented endpoints. |
| Can a small goldfish model be honest and useful? | Experimental scope | mtg-mcp | Run a feasibility child with toy decks, fixed policies, traces, and a stop decision before any public tool. |

## Deferral Rule

An unselected future child or a feasibility outcome can be deferred only with a
durable record in its owning child and a summary here. The record must name the
scope being deferred, rationale, owner, activation or review trigger, affected
acceptance criteria, and why the active phase still meets its exit criteria.

A currently authorized child cannot close an in-scope Must requirement by
calling it deferred. It must verify the requirement or obtain an approved
amendment that removes or replaces it.

## Planning Readiness Checklist

- [x] Core target use cases and non-goals are explicit.
- [x] The present architecture and concrete ownership gaps were inspected.
- [x] Must requirements have acceptance criteria and traceability.
- [x] External source, MCP, C#, MTG-rule, and statistics research is recorded.
- [x] Core, App, adapter, test, persistence, and toolset boundaries are explicit.
- [x] Provider auth, pacing, caching, retention, and error-sanitization rules are explicit.
- [x] Exact mathematics and sampled estimates have separate contracts.
- [x] Each proposed delivery phase has an exit criterion.
- [x] Existing retired simulation and provider packets are treated as reference-only.
- [ ] The owner has selected the first implementation child.
- [ ] The selected child has independent approval and implementation authorization.

## Implementation Checklist

- [ ] Select and create the first narrow child packet.
- [ ] Move only that approved child to in-progress.
- [ ] Lock behavior with characterization fixtures before moving ownership.
- [ ] Update this umbrella if a cross-child guardrail changes.
- [ ] Record focused and broad validation as each child completes.
- [ ] Move this packet to completed only after every selected child is completed,
  explicitly deferred, or superseded with a recorded reason.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-06 | task lint | Passed | Formatting check and strict build passed with the pinned .NET 11 preview SDK. |
| 2026-09-06 | task test | Passed | 545 non-live tests passed across nine test assemblies. |
| 2026-09-06 | task coverage | Passed | Every production assembly cleared the 90% line-coverage gate. |
| 2026-09-06 | task surface:report | Passed | The current static surface has 93 tools, one resource, and zero prompts. |
| 2026-09-06 | task deps:check | Passed with follow-up | No failed check; ModelContextProtocol 2.2.0 and several analyzer/test packages are available. |
| 2026-09-06 | dotnet list package --vulnerable --include-transitive | Passed | NuGet reported no known vulnerable packages. |
| 2026-09-06 | Source and document audit | Completed | The two P2 ownership findings and one P3 documentation drift are recorded in [AUDIT.md](AUDIT.md). |
| 2026-09-06 | Independent packet review | Findings fixed | Corrected the dependency diagram, deferral control, volatile line-count claims, and an internal link. |
| 2026-09-06 | Documentation validation | Passed | git diff --check, trailing-whitespace scan, and local Markdown-link resolution passed. |

## Completion Notes

Not complete. This packet is deliberately a roadmap, not an implementation
authorization. The first recommended child is adapter ownership cleanup because
it reduces risk without changing product behavior.
