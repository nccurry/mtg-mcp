# Evidence-First Deckbuilding Evolution Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: product owner, Core maintainer, adapter maintainer, MCP contract maintainer
- Last updated: 2026-09-06
- Related design: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: No

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-09-06 | mtg-mcp | Initial post-0.9 cleanup and product-evolution roadmap. |

## Executive Summary

mtg-mcp should help a player and an LLM understand a Magic deck, not decide how
to build it. The stable server should provide source-backed card and deck
information, exact calculations, and explicit guarded deck operations. It
should make missing data, source bias, assumptions, and uncertainty visible.

This PLC turns that product statement into an incremental roadmap. First it
makes existing module ownership real. Then it provides a disciplined path for
new source evidence and, only if it proves useful, a bounded sampled goldfish
experiment.

## Audience

This document is for the repository owner and future implementation agents. A
reader should know the current North Star, the three operation modes, and that
the current 0.9 release is an evidence-first clean break.

## References

### Repository sources

- [North Star](../../../../north-star.md)
- [Design Goals](../../../../design-goals.md)
- [Architecture](../../../../architecture.md)
- [Potential Features](../../../../potential-features.md)
- [Evidence-First Rewrite Guide](../../../../rewrite-guide.md)
- [Audit baseline](AUDIT.md)
- [Performance Ratchet](../../../../performance-ratchet.md)
- [Current Scryfall/Archidekt hardening design](../../completed/mcp-contract-and-adapter-hardening/SADD.md)

### External sources checked on 2026-09-06

- [MCP tools specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP tasks specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/tasks)
- [C# union types](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union)
- [.NET exception guidance](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)
- [Scryfall API FAQ](https://scryfall.com/docs/faqs/i-m-having-trouble-accessing-the-scryfall-api-or-i-m-blocked-17)
- [Commander Spellbook syntax guide](https://commanderspellbook.com/syntax-guide/)
- [Reddit Data API Terms](https://redditinc.com/policies/data-api-terms)
- [Moxfield Terms of Service](https://moxfield.com/help/terms)
- [Magic comprehensive rules landing page](https://magic.wizards.com/en/rules)
- [Official Commander rules](https://mtgcommander.net/index.php/rules/)
- [NIST hypergeometric distribution reference](https://www.itl.nist.gov/div898/software/dataplot/refman2/ch8/hypppf.pdf)

## User And Maintainer Outcomes

| Outcome | Success signal | Notes |
| --- | --- | --- |
| A player can inspect a card or deck without being handed an opaque recommendation. | Every result identifies facts, evidence, exact derivation, estimate, or unknown state. | The LLM and player make the conclusion. |
| An LLM can calculate “what is available by turn N?” reliably. | Inputs, population, draw/mulligan assumptions, formula, and exact result are shown. | Exact analysis is the default, not Monte Carlo. |
| A player can change a deck safely. | Local/remote authority, preview, fingerprints, conflicts, and final applied state are visible. | The server never silently chooses a conflict winner. |
| A source-backed claim can be checked. | Responses show source, retrieval time, source reference, cache/freshness state, and applicable population. | Popularity and discussion are evidence, not quality scores. |
| A maintainer can alter a provider family without changing unrelated families. | Behavior lives in the named owner, with fixture-backed tests and architecture tests. | This is the first cleanup priority. |
| A future goldfish result is honest about its limits. | It includes model version, seed, sample count, input fingerprint, policy, unsupported mechanics, traces, and uncertainty. | Only applies after feasibility approval. |

## System Overview

The present server has the right top-level split:

| Project | Current role | Required future boundary |
| --- | --- | --- |
| MtgMcp.Core | Shared evidence, IDs, outcomes, and small provider-neutral rules | No host, adapter, HTTP, SQLite, or provider transport reference. |
| MtgMcp.Decks | Local decks, persistence, backups, interchange, and local workflow facts | Own local persistence/interchange; do not become a provider hub. |
| MtgMcp.Scryfall | Official cards, rulings, community tags, corpus, snapshots, and pacing | Own official acquisition and its data, with real internal store boundaries. |
| MtgMcp.Archidekt | Remote deck, folder, snapshot, and sync workflows | Own observed provider contract and all provider safety details. |
| MtgMcp.Playgroup | Official playgroup observations | Remain a separate provider population. |
| MtgMcp.Statistics | Exact, caller-supplied mathematics | Stay BCL-only, legality-free, and provider-independent. |
| MtgMcp.App | MCP registration, configuration, operation modes, schemas, and composition | Stay a thin, static composition root. |

Future provider and simulation modules are additive only when their contract
passes the admission and feasibility gates in this PLC.

## Assumptions, Dependencies, And Constraints

- The current 0.9 North Star remains in force unless the owner explicitly
  amends it.
- C# 15 union types and .NET 11 preview remain the checked-in toolchain until a
  focused toolchain decision changes them.
- Every normal test stays deterministic, offline, and free of real provider
  mutation.
- Scryfall bulk data is the preferred route for large official datasets; the
  provider warns against heavy live API use.
- Magic deck draws are sampling without replacement, so exact
  hypergeometric-style analysis is appropriate for declared card populations.
- A commander deck usually has a 100-card construction constraint, but
  Statistics must remain caller-supplied and format-neutral unless a future
  workflow explicitly opts into Commander evidence.
- External source terms and APIs can change. A research result is not a
  perpetual implementation approval.
- Tool annotations are useful hints, not an authorization boundary. The server
  must continue enforcing local and remote mode guards in code.

## Use Cases

| ID | Actor and trigger | Expected outcome |
| --- | --- | --- |
| CASE-001 | A player asks what a card does, which printings exist, or what tags a source assigns. | The MCP returns direct Scryfall facts and separately labeled community-tag evidence with provenance. |
| CASE-002 | A player asks what cards are likely to be in hand, drawn, or available by a turn. | The MCP returns an exact calculation from explicit card groups and assumptions; it does not infer the deck’s plan. |
| CASE-003 | A player edits a local deck or requests an Archidekt sync. | The MCP exposes a preview/apply workflow with revision/fingerprint checks and typed conflict outcomes. |
| CASE-004 | A player asks whether an existing deck contains known documented combo pieces. | A future opt-in provider returns Commander Spellbook source evidence, prerequisites, steps, and source identity; it does not say that the combo should be added. |
| CASE-005 | A player asks what a named community discussion says about a card or archetype. | A future Reddit workflow returns only policy-compliant, attributed, bounded source material after explicit authorization. |
| CASE-006 | A player asks what is common in a source-defined deck cohort. | A future permissioned provider returns the cohort, denominator, distribution, and source bias; it never converts popularity into a deck-quality score. |
| CASE-007 | A player explicitly asks for a bounded goldfish estimate. | A future experimental tool returns sampled traces and model limits, never a claimed real-game win rate. |

## Scope And Non-Scope

### In scope

- A structural cleanup of Scryfall and Archidekt ownership.
- Preservation and strengthening of static MCP schema, toolset, mode, and
  evidence boundaries.
- An admission process for every new external source.
- A roadmap for exact deck-analysis orchestration that remains distinct from
  provider data and sampled estimates.
- A feasibility-first route for experimental goldfish analysis.
- Focused MCP SDK/toolchain compatibility work.

### Out of scope

- A full Magic rules engine.
- Autonomous deckbuilding, card cuts, replacements, intent inference, or a
  universal “power” score.
- Website scraping, browser automation, bulk crawling, or undocumented API use.
- Reintroducing retired legacy advisor, Stats Lab, or simulation designs as
  implementation authority.
- Automatic migration of legacy databases, config, or tool schemas.
- A generic provider abstraction or generic tool router.
- A blanket performance benchmark project before a concrete hot path needs one.

### Compatibility target

The first ownership-cleanup child must preserve all current 93 tool names,
schemas, descriptions, toolsets, operation-mode visibility, SQLite files,
provider behavior, and existing success/failure semantics.

Any later public surface change needs its own child PLC, a deliberate versioning
decision, updated capability evidence, and process-level MCP tests.

## Stakeholders And Affected Systems

- Players and agents using local deck, Scryfall, exact-statistics, Archidekt,
  and Playgroup workflows.
- Existing SQLite stores, caches, immutable snapshots, and package install path.
- Scryfall, Archidekt, Playgroup, potential Commander Spellbook, Reddit, and
  future permissioned population providers.
- MCP clients that depend on static tool discovery and JSON schemas.
- Unit, integration, architecture, end-to-end, coverage, package, and live
  acceptance suites.

## Requirements

| ID | Priority | Type | Requirement | Rationale | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| EFD-001 | Must | Product | The server shall return evidence, calculations, and explicit workflows without selecting deckbuilding choices for the caller. | This is the core product promise. | Tool descriptions, outputs, and tests contain no recommendation, weak-card, replacement, or inferred-intent result. |
| EFD-002 | Must | Architecture | Named internal owners shall contain the behavior their names claim to own. | Forwarding owners hide the true change boundary. | Scryfall corpus/snapshot/coordination and Archidekt deck/folder/snapshot classes own their code; temporary contexts are removed. |
| EFD-003 | Must | Architecture | Core shall remain dependency-light and provider-neutral; App shall remain MCP composition only. | Keeps data contracts stable despite provider churn. | Project-reference and source architecture tests pass; no provider DTO or HTTP client enters Core. |
| EFD-004 | Must | Reliability | Expected operational states shall use the existing typed OperationResult union; exceptions shall remain for invalid programmer use, cancellation, and unrecoverable boundary faults. | Clients need inspectable, safe outcomes. | Boundary tests cover success, not found, unavailable, unsupported, conflict, invalid input, and cancellation without leaked secrets. |
| EFD-005 | Must | MCP | Stable tool registration shall remain static for a session, schema-backed, bounded, and assigned to exactly one toolset. | MCP clients need predictable discovery. | Surface, schema, mode, and capability-resource tests agree for every changed tool. |
| EFD-006 | Must | Provider safety | Every new external source shall pass a documented admission review before production acquisition code is written. | API availability alone does not establish permitted, useful, or durable use. | The child packet contains a completed admission record covering access, terms, data meaning, auth, pacing, cache/retention, fixtures, failure behavior, and evidence label. |
| EFD-007 | Must | Evidence | Source results shall preserve provider identity, retrieval time, source reference, freshness/cache state, population/denominator when available, and unknown state when absent. | This prevents popularity, discussion, and source facts from blending into false certainty. | Provider fixtures and output schemas retain the required provenance; missing source fields are not guessed. |
| EFD-008 | Must | Statistics | Deterministic “what is available by turn” workflows shall use declared groups and exact mathematics before any sampled estimate. | Card draws are finite sampling without replacement; exact answers are clearer than simulation when possible. | Independent-formula tests validate representative 60- and 99-card cases, mulligans, and declared assumptions. |
| EFD-009 | Must | Simulation | No sampled goldfish capability shall become stable until an approved feasibility child defines its model boundary, caller policy, unsupported mechanics, replay metadata, uncertainty, and stop criteria. | A deterministic seed does not make a heuristic game model factual. | The feasibility child records an accept/defer/reject decision backed by toy-deck traces and calibration cases. |
| EFD-010 | Must | Testability | Each refactor or provider child shall add characterization tests before moving behavior and keep normal tests offline. | Existing coverage is strong; behavior must remain locked while ownership moves. | Focused tests pass before and after movement; task lint, task test, and task coverage pass for the child. |
| EFD-011 | Should | Performance | A child shall add a performance measurement only for a named, meaningful hot path with deterministic representative inputs. | Avoids benchmark theater while protecting real regressions. | A documented case has a baseline, machine/runtime metadata, and either a review budget or justified CI gate. |
| EFD-012 | Should | Dependencies | Major SDK or analyzer upgrades shall be isolated from behavior refactors and verified through installed-package and MCP client tests. | Makes failures attributable. | Compatibility child passes process, client, schema, package, and broad validation before version changes land. |
| EFD-013 | Must | Documentation | Every affected tool count, provider boundary, source limitation, and experimental status shall be updated with its code change. | Passing tests alone do not prevent misleading humans. | Documentation links render, audit wording is accurate, and git diff --check passes. |

## Interfaces, Data, States, And Modes

No new public tool is authorized by this umbrella. The existing mode rules stay:

| Mode | Allowed behavior |
| --- | --- |
| read-only | Inspect local state and perform safe provider reads. |
| local | Add explicit local writes guarded by local operation checks. |
| remote | Add explicit remote writes guarded by remote operation checks. |

Future sources are read-only and opt-in by default. Future exact analysis belongs
in a clearly named Statistics or deck-analysis workflow only if its inputs and
outputs are materially distinct from existing tools. Future sampled tools must
be separately tagged as experimental, use a separate capability toolset, and
remain hidden unless explicitly enabled.

## Quality Attributes

| Attribute | Scenario | Measure |
| --- | --- | --- |
| Honesty | A source omits a count, a rule, or a card fact. | Output uses unknown, unsupported, unavailable, or not cached; it never invents a value. |
| Determinism | The same exact-analysis request repeats. | Inputs, ordering, calculation, and result are identical. |
| Reproducibility | A sampled experiment repeats. | Model version, seed, input fingerprint, policy, and sample count reproduce the trace/result within documented limits. |
| Safety | A caller requests a write without the right mode or a stale fingerprint. | The operation is rejected before mutation. |
| Provider discipline | A provider is rate-limited or changes contract. | Pacing/backoff and typed safe failure apply; fixtures reveal supported contract drift. |
| Maintainability | A developer changes a provider family. | The change is localized to the real provider/domain owner and focused tests. |
| Output usability | A large source or trace is requested. | Pagination/detail-level caps expose omitted counts and source references. |

## Phased Delivery

| Phase | Goal | Included requirements | Exit criteria |
| --- | --- | --- | --- |
| 0 | Approve the roadmap and select the first narrow child. | EFD-001 to EFD-013 | Owner approves scope and names an active child; no production code changes occur under this umbrella alone. |
| 1 | Make Scryfall and Archidekt ownership real. | EFD-002 to EFD-005, EFD-010, EFD-013 | Behavior and surface are unchanged; characterization and broad gates pass. |
| 2 | Review the MCP SDK/toolchain upgrade separately. | EFD-005, EFD-010, EFD-012, EFD-013 | Target SDK/client contract is proven before package versions change. |
| 3 | Add a first admitted read-only evidence provider, likely Commander Spellbook. | EFD-001, EFD-003 to EFD-007, EFD-010, EFD-013 | Admission record, fixtures, boundaries, and opt-in surface pass. |
| 4 | Add declarative exact deck-analysis workflows only where current tools leave a real gap. | EFD-001, EFD-003 to EFD-005, EFD-008, EFD-010, EFD-011, EFD-013 | Exact results and selected-card evidence are independently verified. |
| 5 | Research community and cohort sources without scraping. | EFD-006, EFD-007, EFD-010, EFD-013 | Each source receives an explicit admit/defer/reject record; Reddit requires policy clearance. |
| 6 | Decide goldfish feasibility. | EFD-001, EFD-003 to EFD-005, EFD-009 to EFD-011, EFD-013 | A documented accept/defer/reject decision exists before any stable tool promise. |

## Traceability

| Requirement | Design section | Validation method | Evidence target |
| --- | --- | --- | --- |
| EFD-001 | [Evidence boundary](SADD.md#evidence-and-decision-boundary) | Tool description and schema review | MCP source/surface tests |
| EFD-002 | [Building blocks](SADD.md#building-blocks) | Characterization and source architecture tests | EFD-FIX-002, EFD-FIX-003 |
| EFD-003 | [Project boundaries](SADD.md#project-boundaries) | Project-reference/source checks | Architecture test suite |
| EFD-004 | [Error handling](SADD.md#error-handling-and-failure-modes) | Unit and adapter failure tests | EFD-FIX-004 |
| EFD-005 | [MCP surface](SADD.md#mcp-surface-schemas-and-diagnostics) | Surface, schema, mode, process tests | EFD-FIX-001 |
| EFD-006–007 | [Provider admission](SADD.md#provider-admission) | Admission checklist and fixture tests | EFD-FIX-005 to EFD-FIX-007 |
| EFD-008 | [Exact analysis](SADD.md#exact-analysis-and-simulation) | Independent math tests | EFD-FIX-008 |
| EFD-009 | [Goldfish feasibility](SADD.md#exact-analysis-and-simulation) | Trace, calibration, policy review | EFD-FIX-009 to EFD-FIX-011 |
| EFD-010–013 | [Test architecture](SADD.md#test-architecture) | Task and documentation validation | Child validation ledger |

## Risks, Assumptions, And Open Questions

| Item | Type | Impact | Owner | Resolution plan |
| --- | --- | --- | --- | --- |
| Extracting provider ownership changes behavior accidentally. | Risk | Remote/local data or writes regress. | Adapter maintainer | Characterize current behavior first; move one domain at a time; retain public facade. |
| An external source changes terms or endpoint behavior. | Risk | Unsupported acquisition or stale output. | Provider child owner | Admission record, fixture drift tests, source-specific cache/pacing, explicit defer path. |
| Reddit content use has policy ambiguity. | Risk | Privacy and license breach. | Product owner | Do not write an adapter until policy review approves the exact workflow. |
| Goldfish scope grows into a rules engine. | Risk | Unbounded cost and misleading claims. | Simulation child owner | Closed capability/model list, toy fixtures, stop criteria, and owner review. |
| A package major upgrade shifts MCP wire behavior. | Risk | Client compatibility break. | MCP child owner | Isolate version upgrade and run package/client/schema tests. |
| A refactor creates a generic abstraction to reduce file count. | Risk | More coupling and less legible ownership. | Reviewers | Reject generic provider/repository/router designs unless a concrete duplication case proves it. |

## Validation

Every implementation child must run its focused tests first, then the relevant
Task checks. Shared or public behavior changes require:

- task lint
- task test
- task coverage
- task surface:report when the MCP surface or registration changes
- task deps:check when package versions change
- installed-package/process/client smoke checks when MCP SDK or packaging changes
- git diff --check and Markdown link inspection for documentation changes

Live provider tests remain opt-in, read-only unless a specifically approved
provider child defines an independently safe mutation acceptance path.

## Definition Of Done

- [ ] Each selected Must requirement is implemented by an approved child. A
  currently authorized child cannot close a Must requirement by merely calling
  it deferred; the owner must verify it or approve an amendment that removes
  or replaces it.
- [ ] Any deferred future child or feasibility outcome has a record that names
  its rationale, owner, activation or review trigger, affected acceptance
  criteria, and confirmation that the active phase still meets its exit
  criteria.
- [ ] The evidence/advice boundary remains visible in code, schemas, and docs.
- [ ] Architecture owners match their actual responsibilities.
- [ ] Every admitted provider has current terms/contract evidence and fixtures.
- [ ] Exact and sampled outputs remain visibly different.
- [ ] Broad validation and any applicable package/client smoke evidence are
  recorded.
- [ ] Residual risks and rejected/deferred sources are documented.
