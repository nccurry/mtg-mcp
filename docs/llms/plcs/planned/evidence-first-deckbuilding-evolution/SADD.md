# Evidence-First Deckbuilding Evolution Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: product owner, Core maintainer, adapter maintainer, MCP contract maintainer
- Last updated: 2026-09-06
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)
- Implementation authorized: No

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-09-06 | mtg-mcp | Initial target architecture and delivery guardrails. |

## Executive Summary

The right end state is not a rewrite. The existing project split is good. The
work is to make it true in the files: each named provider and persistence owner
must contain its actual behavior, and cross-cutting infrastructure must have
one clear owner.

The selected design keeps a small Core, concrete vertical provider modules, a
static App composition root, and exact Statistics. New sources enter only
through source-specific modules after an admission review. A future simulator,
if it survives feasibility, lives beside exact Statistics rather than inside it.

The most important rejected alternative is a generic provider or rules-engine
framework. It would make unlike contracts look alike and turn a cleanup into an
abstraction project.

## Goals, Non-Goals, And Design Drivers

### Goals

- Let players and LLMs make informed deckbuilding decisions from visible
  evidence.
- Localize provider and persistence changes to the real responsible owner.
- Preserve the present stable MCP surface until a later child deliberately
  changes it.
- Prefer exact deck mathematics over sampled estimation whenever a question can
  be stated as a finite population and declared event.
- Give every external source a clear permission, provenance, and failure story.
- Keep tests fast, deterministic, offline, and meaningful.

### Non-goals

- Choose cards, cuts, replacements, or deck strategy for the caller.
- Infer deck intent or build a generic “synergy”/quality score.
- Scrape websites or hide unsupported acquisition behind a browser facade.
- Make an arbitrary Magic rules engine.
- Merge source facts, community tags, popularity, discussions, and modeled
  estimates into a single confidence number.
- Expand the public surface simply because the server can expose more tools.

### Design drivers

- Stable MCP tools need a unique name, JSON schemas, bounded output, and clear
  descriptions. Tool annotations are hints, not a security boundary.
- The current static toolset/mode split is correct: toolsets control relevance;
  modes control write authority.
- C# unions are useful for closed, meaningful alternatives. The current
  OperationResult and EvidenceDescriptor unions should remain the common
  vocabulary for expected outcomes and evidence classification.
- Scryfall advises bulk data for large datasets and bounded live use.
- MTG card draws are finite sampling without replacement; exact
  hypergeometric-style calculations are a better default than estimates.

## Context And Scope

The server is a local stdio MCP process. It owns local decks and caches, talks
to selected provider APIs, and returns structured results to a client LLM. The
LLM remains responsible for interpreting the result against a player’s stated
goal.

This design covers:

- physical ownership cleanup in existing Scryfall and Archidekt code;
- source admission and future provider boundaries;
- exact-analysis versus sampled-simulation separation;
- test, diagnostic, and compatibility rules for later children.

It does not prescribe the detailed endpoint list for a new provider or promise
that an experimental simulation will ship.

## Constraints

- Keep the checked-in .NET 11/C# 15 preview toolchain until a focused
  compatibility child changes it.
- Core cannot reference App, adapters, HTTP, SQLite, or provider DTOs.
- Statistics cannot reference providers or legality logic.
- App cannot become a provider/business-logic owner.
- Provider wire payloads remain in the provider adapter.
- Normal tests cannot use live network or mutate real provider data.
- Existing local/remote guards, fingerprints, read-back checks, request
  budgets, cancellation, and secret redaction are non-negotiable behavior.
- Use one provider’s source population at a time. A cross-source comparison
  must identify every population and never silently average them.

## Alternatives Considered

| Option | Summary | Strengths | Weaknesses | Decision |
| --- | --- | --- | --- | --- |
| Full rewrite | Replace all projects and surfaces at once. | Can redraw every boundary. | Discards a strong 545-test baseline and risks a large behavior regression. | Rejected |
| Targeted owner extraction | Move real behavior into existing named owners while preserving facades and contracts. | Small reviewable steps, protects behavior, fixes the actual seams. | Requires disciplined characterization tests. | Chosen |
| Generic provider interface/framework | Give every source the same request/result interface. | Looks uniform at first. | Hides auth, retention, data meaning, and mutation differences; adds a leaky abstraction. | Rejected |
| Concrete provider modules | Each source owns its transport, mapping, cache, evidence model, and tests. | Honest contracts and clear failures. | Some deliberate local duplication. | Chosen |
| Third-party Result library | Replace existing native result union. | Familiar pattern to some developers. | Adds dependency/churn without solving a current problem. | Rejected |
| Existing native unions | Keep closed typed outcomes and evidence cases. | Exhaustive matching, no package, current schemas preserved. | Requires deliberate case additions. | Chosen |
| Broad scraper/browser adapter | Query any popular MTG website. | Maximum apparent coverage. | Unreliable, often disallowed, hard to attribute and test. | Rejected |
| Source admission plus supported adapters | Add only sources with a usable contract and documented limits. | Reliable, reviewable, aligned with evidence-first. | Some desired sources stay deferred. | Chosen |
| Full rules engine | Simulate arbitrary Magic games. | Broad theoretical coverage. | Open-ended rules scope and misleading partial behavior. | Rejected |
| Bounded goldfish feasibility study | Test a narrow caller-declared model after exact analysis. | Can answer a limited question honestly. | May be rejected if it cannot meet the evidence bar. | Chosen as a future experiment only |

## Chosen Design

### Layered model

Runtime calls flow from the player and LLM through App and explicit workflows
to the concrete module that owns the requested capability:

    Player goal + LLM judgment
                |
                v
    MCP App: static tools, schemas, toolsets, modes, composition
                |
                v
    Explicit workflows: local deck, provider sync, exact analysis, evidence query
                |
                +----------------------+----------------------+
                |                      |                      |
                v                      v                      v
    Decks / Scryfall / Archidekt / Playgroup / Statistics / future source module

Project references run in the other useful direction: App and every concrete
module may depend on Core, while Core depends on none of them. SQLite, files,
HTTP, and provider APIs are implementation details owned directly by Decks or
the relevant adapter; they do not sit below Core in the dependency graph.

    MCP App  ------------>  Decks / adapters / Statistics  ------------>  Core
                                  |               |
                                  v               v
                          SQLite / files      HTTP / provider APIs

A provider adapter can translate its own HTTP payload into its own public
evidence contract and shared Core evidence metadata. Core never sees a provider
DTO. App calls operations but does not absorb their behavior.

### Evidence and decision boundary

Every result belongs to one visible category:

| Category | Meaning | Example |
| --- | --- | --- |
| Source fact | Directly observed field from a named source. | Oracle text or a remote deck entry. |
| Source evidence | Attributed observation with a source-specific meaning. | Scryfall community tag, combo listing, community post, or cohort row. |
| Exact derivation | Mathematics applied to declared values. | Chance of at least one land by turn four. |
| Parser classification | Deterministic output of a versioned parser. | An interchange parse result. |
| Sampled estimate | A model run with replay metadata and uncertainty. | A future goldfish frequency estimate. |
| Unknown / unavailable / unsupported | A value the server cannot honestly provide. | Unavailable source, missing corpus, unsupported model mechanic. |

The server may format evidence for clarity but may not turn it into “therefore
play this card.” A client LLM can make that connection in conversation with the
player.

## Data Design

### Existing durable data

- decks.db remains the local deck, binding, baseline, backup, and interchange
  store.
- scryfall.db remains the official card/ruling/community-tag corpus, snapshots,
  metadata, leases, and pacing store.
- Existing data formats are preserved during the first owner-extraction child.
- New provider caches require their own lifecycle and retention decision. They
  do not enter scryfall.db or decks.db merely for convenience.

### Evidence metadata

Keep the existing EvidenceDescriptor union. A future sampled result must either
extend the shared sampled descriptor compatibly or carry a
simulation-specific provenance record that includes:

- model version;
- seed and sample count;
- immutable input/deck fingerprint;
- caller-declared policy;
- per-metric confidence interval or a documented reason it does not apply;
- supported and unsupported mechanic coverage;
- retrieval/corpus generation identity for card facts used by the model.

The child must show why shared Core metadata is needed before changing Core.
Do not put provider-specific cache, endpoint, or policy data there.

### Ordering, limits, and retention

Every list has a stable ordering and a bound. A response states pagination or
omitted count when it cannot return every row. Provider caches use
source-specific freshness and retention; a missing cache remains “not cached,”
not an empty source result.

## Building Blocks

| Building block | Responsibility | Owned state/lifetime | Public surface | Dependencies | Tests |
| --- | --- | --- | --- | --- | --- |
| MtgMcp.Core | Shared IDs, typed outcomes, evidence taxonomy, provider-neutral contracts | No transport/persistence state | Core records/unions | BCL only | Core unit and architecture tests |
| MtgMcp.Decks | Local deck persistence, versions, backups, interchange | decks.db and local files | Deck workflows/contracts | Core, SQLite | Decks unit/integration tests |
| Scryfall database owner | Database path, connection creation, schema bootstrap/validation, composition | scryfall.db connection policy | Internal concrete support | SQLite | Schema/connection tests |
| Scryfall corpus store | Corpus generations, cards, rulings, tags, import/activation/rollback | Corpus SQL operations | Internal concrete store | Database owner | Corpus fixtures |
| Scryfall snapshot store | Exact-request snapshot lookup, storage, replay, listing, deletion | Snapshot SQL operations | Internal concrete store | Database owner | Snapshot fixtures |
| Scryfall coordination store | Leases, metadata timestamps, provider-start reservations | Coordination SQL operations | Internal concrete store | Database owner | Multi-process/pacing fixtures |
| Scryfall operations | Official API/bulk acquisition and evidence workflows | Provider client/cache policy | ScryfallService facade | Concrete stores | Fake HTTP, corpus, and App tests |
| Shared Archidekt HTTP/session owner | HttpClient lifetime, auth, pacing, retries, cooldown, request budget, sanitized provider faults | One session per Archidekt service | Internal support | HTTP/BCL | HTTP/pacing/error tests |
| Archidekt deck transport and operations | Deck routes, payloads, normalization, validation, read-back, guarded apply | Deck workflow state | ArchidektService delegation | Shared session, Core | Fixture and workflow tests |
| Archidekt folder transport and operations | Folder routes, tree handling, validation, guarded writes | Folder workflow state | ArchidektService delegation | Shared session, Core | Fixture and workflow tests |
| Archidekt snapshot transport and operations | Snapshot routes, preview, restore, guarded writes | Snapshot workflow state | ArchidektService delegation | Shared session, Core | Fixture and workflow tests |
| MtgMcp.Playgroup | Pinned official observation contract | Provider client/cache policy | Playgroup facade | Core/HTTP | Fixture tests |
| MtgMcp.Statistics | Exact math from caller-provided values | No provider/persistence state | Statistics operations | BCL/Core contracts only | Independent formula tests |
| MtgMcp.App | Static MCP registration, modes, schemas, composition | Process/configuration lifetime | Tool/resource handlers | All capability projects | App, surface, E2E tests |
| Future concrete source module | One admitted source’s transport, mapping, cache, evidence behavior | Source-specific | Opt-in toolset only | Core/HTTP as needed | Sanitized fake-HTTP fixtures |
| Future simulation-lab module | Explicit bounded model, policies, traces, estimates | Experiment-local model state | Experimental opt-in only | Core/Deck contracts, approved card facts | Toy-deck/calibration tests |

No provider-wide IRepository, ISourceAdapter, generic transport router, or
common cache abstraction is planned. The common pieces are small: Core evidence,
OperationResult, BCL HTTP primitives, and App composition.

## Runtime And Data Flow

### Existing evidence query

1. App validates the MCP request/schema and operation mode.
2. The selected capability operation validates its input.
3. The concrete owner uses its store/transport.
4. Expected source states map to a typed OperationResult.
5. The tool returns structured, bounded data with evidence metadata.
6. The client LLM interprets it; no recommendation is generated.

### Local or remote write

1. App confirms local or remote mode.
2. Workflow reads the current state and validates revision/fingerprint.
3. The workflow produces a preview or applies an explicit confirmed request.
4. Provider workflows verify read-back when their contract supports it.
5. Conflict, unavailable, and invalid input stay typed; no automatic conflict
   resolution occurs.

### Future provider admission

1. Research the source contract and current terms.
2. Write the admission record and narrow child PLC.
3. Capture sanitized fixtures and expected error cases.
4. Implement one concrete module with pacing, cache, and evidence metadata.
5. Expose a small opt-in toolset only after contract and MCP tests pass.

### Future sampled goldfish

1. The caller explicitly chooses a supported model and policy.
2. The module resolves a frozen deck/card input identity.
3. It runs a bounded number of sampled trials with a supplied seed.
4. It returns aggregate estimates, intervals, coverage, and selected traces.
5. Unsupported mechanics are recorded and contribute no invented outcome.

The model never asks an LLM to pick a play line and never silently infers one
from deck tags or prose.

## MCP Surface, Schemas, And Diagnostics

The current 93-tool surface remains static in the first cleanup child. It
continues to expose one capability resource and zero prompts.

For later public changes:

- Every tool has a unique stable name, explicit input schema, output schema, and
  description.
- Structured content conforms to the declared output schema. Text remains a
  concise human-readable companion where client interoperability benefits.
- Tool annotations accurately state read-only, destructive, idempotent, and
  open-world hints, but OperationModeGuard remains the actual authority check.
- Toolsets are selected at startup and do not claim a dynamic list change during
  the session.
- A provider tool is opt-in unless its workflow is a small coherent default
  capability.
- No tool name/prefix is reserved for a future simulation until the feasibility
  child owns the complete toolset, mode, and versioning decision.
- Long-running task support is not assumed. The 2025-11-25 MCP task protocol is
  experimental and has evolved; any adoption needs a compatibility spike with
  the target SDK and client. Until then, operations are synchronously bounded.

## Provider Admission

Every new source must have a provider-admission record before code begins.

| Gate | Required evidence |
| --- | --- |
| Supported access | Official API, published OpenAPI/SDK, or written provider permission; no guessed endpoint. |
| Meaning | What each field/population proves and what it does not prove. |
| Terms and privacy | Current terms, attribution, authorization, user-content use, retention/deletion, and commercial restrictions. |
| Auth and secrets | How tokens are supplied, redacted, rotated, and kept out of fixtures/logs. |
| Pacing and retries | Published limits where available; conservative defaults and explicit 429 behavior otherwise. |
| Cache and retention | What can be stored, how long, freshness semantics, and how deletion is handled. |
| Contract drift | Sanitized fixture captures and a defined response to unsupported/new fields. |
| Failure behavior | Typed unavailable/not found/unsupported/permission outcomes, without invented fallback. |
| MCP exposure | Toolset, modes, schemas, output bounds, source reference, and live-test boundary. |

### Current source disposition

| Source | Status | Product meaning | Design rule |
| --- | --- | --- | --- |
| Scryfall | Stable | Official card/ruling facts and separately labeled community-tag evidence | Continue official API/bulk contract; use bulk data for corpus-scale work. |
| Archidekt | Stable observed adapter | User-authorized deck/folder/snapshot state and explicit workflows | Preserve fixture-tested contract and write safeguards; do not broaden casually. |
| Playgroup | Stable official adapter | Provider-shaped playgroup observations | Keep source population separate from deck-quality judgments. |
| Commander Spellbook | Candidate | Documented combo variants, prerequisites, steps, and results | First likely new provider after an admission record and fixture review; returns evidence, not “add this combo.” |
| Reddit | Feasibility only | Attributed community discussion, not source fact | Current terms require registered authorized access and restrict user-content use/retention. Obtain policy approval before implementation; never scrape or train on content. |
| EDHREC-style aggregate source | Deferred | Source-defined popularity/cohort evidence | No public developer contract was confirmed in this audit. Require official contract or written permission. |
| Moxfield | Rejected for automation | Manual interchange remains valid | Current terms prohibit robots, spiders, automatic access, and manual monitoring/copying without approval. |

## Error Handling And Failure Modes

Use the smallest honest mechanism for each failure:

| Condition | Boundary behavior |
| --- | --- |
| Normal absence, stale state, unsupported feature, unavailable source, or conflict | Return the matching OperationResult case with a stable reason code and safe message. |
| Invalid external caller data at a tool/workflow boundary | Return OperationInvalidInput where the request is otherwise well-formed. |
| Invalid programmer use or constructor argument | Throw the standard synchronous argument exception. |
| Cancellation | Pass CancellationToken through; let OperationCanceledException propagate. |
| Known SQLite/HTTP/JSON/provider boundary fault | Catch at the owning adapter/store boundary, redact it, and map it to the appropriate typed outcome when recovery is possible. |
| Unexpected programming fault | Do not swallow it into a generic success-like response. Let host diagnostics retain the original failure without exposing secrets to clients. |

No provider exception type may cross into Core or MCP JSON. No error may include
credentials, cookies, authorization headers, raw local secret paths, or an
unbounded upstream response body.

## Cross-Cutting Concepts

### Exact analysis and simulation

“Simulation” should not be the default name for a card-draw calculation.
Normal questions such as “what is the chance of seeing one of these cards by
turn four?” are exact analysis when the caller states:

- deck/population size;
- quantities or explicit selected cards;
- opening hand/draw/mulligan assumption;
- play/draw convention where relevant;
- event being measured.

Statistics returns the math and assumptions. It does not classify cards as
lands, interaction, synergy, or keepable unless the caller supplies a
transparent group/predicate and the selected-card evidence is shown.

Goldfish is a different category. A future feasibility study may model only a
closed set of mechanics and caller-declared policy choices. It starts with one
player, no opponent board, no priority/stack/layer model, no arbitrary
expressions, no inferred strategy, and no claim about real matchup win rate.
The study must be willing to end with “defer” or “reject.”

### Tag ownership

The server does not invent its own card-tag taxonomy. It continues to use the
Scryfall-provided community tag corpus already stored in scryfall.db. Oracle
facts, community tags, parser classifications, and any caller-defined groups
stay visibly separate.

### Performance

Do not add performance work because a file is long. Add a measurement when a
child adds a real hot path: full corpus operation, high-volume provider page,
large deck batch, or sampled model run. Use deterministic representative input,
record the machine/runtime, and state whether the budget is informational or a
hard gate.

## Project Boundaries

| Rule | Enforcement |
| --- | --- |
| Core has no adapter/host/runtime package reference. | Project-reference and source architecture tests. |
| Provider DTOs, HTTP clients, auth, pacing, and cache behavior remain in the provider project. | Source inspection and fixture tests. |
| Decks owns local persistence/interchange, not remote provider transport. | Project boundaries and workflow tests. |
| Statistics remains exact, caller-supplied, provider-independent, and legality-free. | Project references and independent-formula tests. |
| App owns static MCP registration/composition, not provider logic. | Source/surface test and code review. |
| A simulation experiment does not contaminate exact Statistics or stable Core prematurely. | New project boundary and explicit feasibility approval. |

## Readability And Documentation

- A class name must match the behavior it owns. Do not label a forwarding
  wrapper an owner.
- Keep public facades small and deliberate. Private concrete owners can use
  direct dependencies when they clarify responsibility.
- Use C# unions for closed alternatives, records for data, and enums for simple
  categories. Do not convert types merely to use new syntax.
- Keep XML summaries useful and update them with surface/behavior changes.
- Remove temporary forwarding code as real ownership arrives.
- Update the North Star, architecture, toolset, provider, and release docs only
  when a child changes their actual behavior. This umbrella does not alter the
  stable product promise by itself.

## Quality Attribute Design

| Requirement | Design response | Validation |
| --- | --- | --- |
| EFD-001 | Evidence category and decision boundary stay in Core contracts, tool schemas, and descriptions. | Source/surface inspection and E2E tests. |
| EFD-002 | Move actual operations into concrete Scryfall/Archidekt owners; delete forwarding contexts. | Characterization, architecture, and focused adapter tests. |
| EFD-003 | Preserve one-way project references and isolated adapters. | Architecture tests. |
| EFD-004 | Keep closed typed outcomes; catch expected external faults only at boundaries. | Failure, redaction, and cancellation tests. |
| EFD-005 | Static startup-selected toolsets with schema-backed structured results. | Surface, mode, schema, process, and official-client tests. |
| EFD-006–007 | Provider admission record and source-specific module/policy. | Review checklist and fake-HTTP fixtures. |
| EFD-008 | Exact finite-population models separate from provider card semantics. | Independent-formula tests. |
| EFD-009 | Experimental model is closed, versioned, replayable, bounded, and caveated. | Toy-deck traces, calibration, and feasibility decision. |
| EFD-010–013 | Child-level characterization, Task gates, and documentation ownership. | Validation ledger and diff/link checks. |

## Implementation Phases

The umbrella delivery order is in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md).
No phase begins solely because it is listed here. Each needs an approved narrow
child and a current validation baseline.

## Test Architecture

| Test layer | Purpose | Examples |
| --- | --- | --- |
| Unit | Prove small domain and error behavior. | Result mapping, input validation, exact formula invariants. |
| Store/adapter fixture | Prove provider SQL/HTTP mapping and failures without network. | Scryfall generation/snapshot rows; Archidekt deck/folder/snapshot payloads. |
| Characterization | Freeze pre-refactor behavior before moving it. | Identical output, SQL state, request count, redaction, fingerprint conflict. |
| Architecture | Prevent boundary drift and forbidden generic/legacy surfaces. | Project references, static toolset assignment, no provider DTO in Core. |
| MCP E2E | Prove a real process exposes the intended tool/resource/schema/mode behavior. | Capability resource, default/all/none toolsets, protected writes. |
| Coverage | Keep each production assembly above 90% line coverage. | task coverage. |
| Package/client smoke | Prove the install artifact and target MCP client still work. | Process/official-client/package smoke after SDK or package work. |
| Live acceptance | Confirm only explicitly approved external behavior. | Read-only, bounded provider check after offline gates. |
| Performance | Protect a named hot path only. | Deterministic review benchmark with documented input and environment. |

## Framework And External Notes

- MCP tools should expose valid schemas and structured output. The current
  static registration model fits this well.
- The MCP tasks feature in the 2025-11-25 revision is experimental. Recent
  protocol/SDK evolution makes it unsuitable as an assumed foundation for a
  first simulation experiment.
- .NET 11/C# 15 native unions support a closed result/evidence vocabulary and
  exhaustive matching. The existing union approach is appropriate.
- .NET guidance distinguishes common/expected conditions from exceptional
  faults. That matches typed operation outcomes plus boundary exception mapping.
- Scryfall’s supported bulk/API model is the evidence acquisition foundation.
- Commander Spellbook’s documented query syntax and public API make it a better
  candidate than an undocumented popularity endpoint, subject to admission.
- Reddit is not a general search corpus. Its Data API terms require authorized
  access, restrict retention and content use, and require a current policy
  decision before any MCP integration.
- Moxfield automation is out of scope under its current terms.

## Decisions, Risks, And Deferred Work

| Item | Type | Impact | Resolution |
| --- | --- | --- | --- |
| Preserve public facades while extracting owners. | Decision | Limits client churn. | Retain ScryfallService and ArchidektService contracts in Phase 1. |
| Do not create a generic provider framework. | Decision | Avoids leaky abstractions. | Use concrete source modules and only proven shared primitives. |
| Treat old simulation packets as reference-only. | Decision | Prevents retired advisor/rules assumptions returning accidentally. | Feasibility child reviews small useful pieces only. |
| Source terms may change. | Risk | A candidate can become unavailable. | Re-check terms at child activation and release. |
| C# union syntax is preview. | Risk | Toolchain updates may affect code/formatters/serializers. | Keep version work isolated and fully smoke-tested. |
| Popularity source may never be admitted. | Deferred | Cohort feature may remain unavailable. | Return no data rather than use undocumented/scraped source. |
| Goldfish may fail feasibility. | Deferred | No sampled deck flow ships. | Exact analysis remains useful independently. |

## Glossary

| Term | Meaning |
| --- | --- |
| Evidence | Information with source, method, and limits visible to the client. |
| Source fact | Directly observed source field. |
| Source evidence | Attributed observation that is not a universal fact. |
| Exact analysis | A mathematically exact result from declared finite inputs. |
| Sampled estimate | A result from finite model trials; it carries uncertainty and replay metadata. |
| Admission record | The provider-specific proof that a source can be safely and honestly integrated. |
| Characterization test | A test that locks current behavior before a refactor moves code. |
| Goldfish | A bounded unopposed deck model, not a full Magic game or matchup predictor. |
