# mtg-mcp Improvement Plan

Status: proposal / roadmap. Latest public tag: `0.7.0`; next roadmap release:
`0.8.0` (pre-1.0).

This document is a phase-by-phase plan to improve `mtg-mcp` across every dimension
where it should or could be better. It is intentionally high level on *solutions*:
each phase states the problem, the direction of the fix, and the definition of done,
and leaves detailed design and implementation to follow-up work.

## How to use this document

- Each phase is independently shippable and ends in a releasable state.
- Phases are ordered by leverage and dependency, but several can run in parallel
  (see [Sequencing](#sequencing--dependencies)).
- "Problem" cites concrete evidence from the current codebase. "Solution" is a
  direction, not a spec. "Done when" is the acceptance bar.
- Effort is a rough T-shirt size (S/M/L/XL) for planning only.
- Detailed, code-grounded implementation plans for each phase live in
  [`docs/improvement-plans/`](docs/improvement-plans/README.md). This file owns the
  sequencing and problem inventory; those files own the "how."

## Guiding principles (carry these into every phase)

These come from the existing `AGENTS.md` and `docs/architecture.md` and must not be
regressed:

1. `MtgMcp.Core` stays free of adapter/host references and third-party packages.
2. Default install stays lightweight and .NET-only; normal tests stay offline and
   deterministic; no live network or real Archidekt writeback in `task test`.
3. Source policy stays API-only (no HTML scraping, no browser automation).
4. Prefer explainable, deterministic heuristics over opaque simulation; never claim
   abstract simulation is a true Magic rules engine.
5. Evidence-first MCP shape: tools return grounded data/labels/assumptions; the
   calling LLM does synthesis and judgment.
6. Honor stated non-goals: no full rules engine inside `mtg-mcp`, no vendoring of
   XMage/mage-bench.
7. Prefer simplicity and the repo's stated C# style (incl. union types for
   discriminated outcomes).

## Problem inventory (summary)

| # | Dimension | Problem | Severity | Phase |
|---|---|---|---|---|
| P1 | MCP surface | ~114 tools always advertised; exceeds what clients/models handle well; token cost and selection accuracy suffer | High | 1 |
| P2 | MCP surface | All tools advertised in every operation mode; read-only deployments still expose ~40 mutating tools that only throw | High | 1 |
| P3 | API/UX | Multiple output-control idioms (`detailLevel` summary/normal/full vs compact/full vs default-full; plus `includeWorkspace`, `analysisMode`, `sourceSupportDepth`, `scope`) | Medium | 2 |
| P4 | API/UX | Inconsistent return shapes; many tools return `object`/anonymous types | Medium | 2,3 |
| P5 | API/UX | Tool/resource duplication (`server_get_info` vs `mtg://server/info`, `source_list` vs `mtg://sources/status`, auth-status) | Low | 1,2 |
| P6 | MCP protocol | `object` returns prevent stable output schemas / structured content | Medium | 3 |
| P7 | MCP protocol | Errors are thrown exceptions (prose only); no machine-readable error codes/shape | Medium | 3 |
| P8 | MCP protocol | No cursor/continuation for large result sets; only `limit` bounding | Low | 3 |
| P9 | MCP protocol | No enumerable resource for saved workspaces (resource browsing can't discover decks) | Low | 3 |
| P10 | Feature coherence | `deck_evaluate_card` started as ramp-only; Phase 7 now evaluates ramp, draw, and interaction, with explicit `unsupportedRole` output for roles outside the current rubric | High | 0,7 |
| P11 | Feature coherence | Undocumented tools (`commander_search_candidates`, `deck_evaluate_card`, `deck_batch_tuning_report`) and resources (`.../state`, `.../assistant-context`) | Medium | 0 |
| P12 | Feature coherence | Phase 7 replaced `deck_estimate_commander_bracket`'s coarse max-signal floor with a density-aware advisory model and calibration bracket-range benchmarks | Medium | 7 |
| P13 | Feature coherence | Phase 7 unified goldfish-family and draw-odds Monte Carlo RNG on `DeterministicSimulationRandom`; keep replay metadata/docs/tests aligned as analytical models evolve | Medium | 0,7 |
| P14 | Feature coherence | Phase 7 replaced the offline/no-catalog combo fallback with a bounded checked-in `docs/reference/local-combos.json` dataset | Low | 7 |
| P15 | Domain/code | Union types used once despite `net11.0`/preview bet; outcomes encoded as `bool Success` + `string Status`; string-discriminated `DeckEditOperation` god-DTO | Medium | 4 |
| P16 | Domain/code | God services (`DeckRecommendationService` ~6k LOC, concrete service-to-service coupling); fat shared `DeckServiceBase`; duplicated JSON repositories | Medium | 5 |
| P17 | Domain/code | Domain entities and tool-response DTOs mixed in large `Models/*.cs` files | Low | 4 |
| P18 | Adapters | Phase 6 established a shared resiliency convention: Scryfall/Archidekt keep provider-specific pacing/retry, CommanderSpellbook/Decklists share text-response retry, Moxfield/Playgroup keep adapter-local request loops with shared redacted failure handling | Medium | 6 |
| P19 | Adapters | Phase 6 removed bare `EnsureSuccessStatusCode` paths; adapter HTTP exceptions and source failure notes are redacted/truncated, and optional corpus sources degrade into source status rows | Medium | 6 |
| P20 | Adapters | `SecretRedactor.Redact(string)` is coarse: whole-body false positives, keyword-less token false negatives | High (safety) | 6 |
| P21 | Adapters | Archidekt JWT cached for process lifetime with no expiry/refresh | Medium | 6 |
| P22 | Adapters | Scryfall caches use `ICorpusCache`; Archidekt card-id cache is documented; adapter pacing is host-owned | Low | 6 |
| P23 | Missing | No collection/ownership awareness ("which of these do I own?") | Medium | 8 |
| P24 | Missing | No toolset/subset selection mechanism | High | 1 |
| P25 | Missing | Phase 8 Track 1 added `card_get_batch` for bounded multi-card hydration | Medium | 8 |
| P26 | Missing | Phase 8 Track 1 added `card_get_image` link-only image/art access for multimodal clients | Low | 8 |
| P27 | Missing | Phase 8 Track 1 added `IPriceSource` for normalized catalog pricing; alternate provider selection remains future work | Low | 8 |
| P28 | Quality | No structured logging/metrics; client-compatibility matrix untested | Medium | 9 |

---

## Phase 0 - Baseline, guardrails, and quick wins

Theme: make the surface measurable, fix the cheap correctness/honesty gaps, and put
policies in place before larger changes. Low risk, unblocks every later phase.

Problems addressed: P10 (label/scope only), P11, P13 (label only).

Solutions (high level):
- Add a surface inventory + metrics check (tool count, per-tool schema token
  estimate, annotation coverage) to CI so future phases can track reduction.
- Adopt a lightweight ADR (architecture decision record) folder and a public
  deprecation policy for the pre-1.0 surface (how tools are announced, deprecated,
  removed across minor versions).
- Reconcile docs with reality: document or remove `commander_search_candidates`,
  `deck_evaluate_card`, `deck_batch_tuning_report`, and the `mtg://workspace/{id}/state`
  and `.../assistant-context` resources. The README claims to enumerate the surface.
- Honesty fixes that do not need redesign:
  - Phase 0 re-scoped the then-ramp-only `deck_evaluate_card` description and status
    output. Phase 7 now broadens that same tool to ramp, draw, and interaction with
    explicit unsupported-role output for future roles.
  - Stamp the goldfish-family results with their RNG kind. Phase 7 later replaces
    the original `system-random` label with the shared stable deterministic RNG.

Done when: CI reports surface metrics; README/`docs` match the registered surface
exactly (a test enforces this); no tool description over-claims relative to its
implementation; deprecation policy + ADR process documented.

Effort: S-M.

---

## Phase 1 - Tool surface consolidation and toolsets

Theme: get the advertised tool count to a number real MCP clients and models handle
well, without losing capability. Highest-leverage change in the plan.

Problems addressed: P1, P2, P5 (partial), P24.

Solutions (high level):
- Define a target ceiling (e.g. a few dozen advertised tools) and a categorized
  inventory of keep / merge / demote-to-resource / remove. See
  [Appendix A](#appendix-a-surface-consolidation-candidates) for starting candidates.
- Consolidate overlapping tools behind a mode/parameter rather than many near-duplicate
  tools (e.g. fold board-projection and win-turn views into the goldfish tool; unify
  the `workspace_*`/`archidekt_*` checkpoint pairs behind a provider parameter).
- Introduce configurable "toolsets" (e.g. `cards`, `workspace`, `analysis`,
  `simulation`, `sources`, `archidekt`, `playgroup`) so a deployment advertises only
  what it needs. The 0.9.0 compatibility profile keeps blank `Toolsets` as "all
  selected by mode"; the reduced default profile lands only with the planned
  deprecation/removal step.
- Make tool advertising operation-mode-aware: do not list mutating tools when the
  server runs `read-only`; trim planning/writeback tools accordingly.
- Reduce tool/resource duplication: prefer one canonical home per capability and
  document the rationale where both must exist for client-compatibility reasons.

Done when: toolsets are configurable and documented; `read-only`/`plan` modes advertise
only runnable tools; the documented core/default profile is at/under the agreed ceiling
after the deprecation/removal step; the surface snapshot test reflects the new shape;
capability coverage is unchanged (every workflow in the README still achievable).

Effort: L. Risk: breaking change to the surface - sequence within the deprecation
policy from Phase 0 and land before 1.0.

---

## Phase 2 - API and UX contract unification

Theme: one consistent way to call tools and read results, applied to the surviving
(post-consolidation) surface.

Problems addressed: P3, P4 (naming/shape), P5 (partial).

Solutions (high level):
- Standardize on a single output-control idiom: one `detailLevel` enum
  (`summary`/`normal`/`full`) with consistent defaults. Retire `includeWorkspace`,
  the `compact|full` variant, and per-tool default drift. Where extra knobs are truly
  needed (`analysisMode`, `sourceSupportDepth`, refresh `scope`), give them a shared
  vocabulary and document them centrally.
- Standardize identifiers and parameter names (`workspaceId`, `planId`, `cardNameOrId`,
  `format`, `limit`) and ordering conventions across tools.
- Define a consistent result envelope concept (status, warnings, assumptions,
  determinism/source metadata) so every analytical tool returns the same framing.
- Normalize tool grouping so file/namespace organization matches public prefixes
  (e.g. `deck_analyze_*` live together).

Done when: every tool uses the unified `detailLevel`; no `includeWorkspace`/`compact`
idioms remain; a contract test asserts naming/parameter conventions; docs describe one
output-control model.

Effort: M-L. Depends on: Phase 1 (normalize once, on the final surface).

---

## Phase 3 - MCP protocol conformance

Theme: make the server a first-class citizen for capable MCP clients (structured
output, structured errors, discovery, pagination).

Problems addressed: P4, P6, P7, P8, P9.

Solutions (high level):
- Replace `Task<object>`/anonymous returns with typed result records so the SDK can
  emit output schemas and structured content for the high-traffic tools (workspace
  start/refresh/validate, mutations, evaluation, simulation presenters).
- Adopt a structured tool-error model: machine-readable error code + message + safe
  details, surfaced as MCP tool errors (`isError`) rather than raw thrown exceptions;
  keep messages actionable and secret-free.
- Add cursor/continuation to list-style tools and resources that can exceed `limit`
  (`workspace_list`, `deck_plan_list`, source/search results).
- Add an enumerable workspace resource (e.g. `mtg://workspaces`) so resource-browsing
  clients can discover saved decks without a tool call; review resource templates for
  completeness.
- Re-verify annotations and `ServerInstructions` against the consolidated surface.

Done when: high-traffic tools expose output schemas/structured content; tool errors
are structured and tested; large lists are paginable; workspace discovery works via
resources; conformance is covered by E2E tests.

Effort: L. Depends on: Phase 2 (typed returns) for output schemas.

---

## Phase 4 - Domain typing and model hygiene (Core)

Theme: close the gap between the repo's stated type-safety philosophy and the code.

Problems addressed: P15, P17.

Solutions (high level):
- Model closed alternatives and discriminated outcomes as C# union types with
  exhaustive switches (the repo already targets `net11.0`/preview for this): plan
  apply results, edit operations, source statuses, diff statuses, refresh statuses.
- Replace the string-discriminated `DeckEditOperation` god-DTO (~12 nullable fields)
  with a union of operation cases so invalid combinations are unrepresentable and new
  cases surface as compile-time work.
- Replace `string Status`/`Severity`/`Outcome` constants with enums or unions where a
  closed set exists (including `OperationMode`).
- Separate true domain entities (`DeckWorkspace`, `DeckCard`, `DeckCategory`) from
  tool-response DTOs currently colocated in large `Models/*.cs` files; consider value
  objects for mana value/color identity. Keep entities the single source of truth.

Done when: discriminated outcomes use unions with exhaustive switching; the edit
operation god-DTO is gone; status enums replace ad-hoc strings on closed sets; domain
vs response models are separated. Can begin in parallel with Phase 2 since it is
internal, but lands under the same public contracts.

Effort: L. Depends on: coordinate with Phase 2/3 result shapes.

---

## Phase 5 - Core service decomposition

Theme: tame the largest services and the shared base so the core stays changeable.

Problems addressed: P16.

Solutions (high level):
- Break up god/coordinator services (notably `DeckRecommendationService`) into focused
  collaborators; depend on interfaces rather than concrete sibling services where it
  improves testability and substitution.
- Retire the fat `DeckServiceBase` "junk drawer": move analysis-metric and
  recommendation helpers into standalone, injectable units used only where needed
  (guard against re-creating a facade - a boundary test already exists for this).
- Extract a shared `JsonFileStore<T>` to remove the duplicated workspace/plan
  repository plumbing (atomic write, id sanitization, list/deserialize); fix id
  collision risk in sanitization; consider an in-memory index for `ListAsync`.
- Consider namespace-per-subdomain (the project is one flat `namespace MtgMcp.Core`)
  to regain a cheap encapsulation boundary, if it does not churn the codebase
  excessively.

Done when: no single service file dominates; service-to-service coupling is via
interfaces where it matters; repository plumbing is shared; existing boundary tests
still pass and are extended.

Effort: XL. Depends on: Phase 4 (clearer contracts) recommended first.

---

## Phase 6 - Adapter layer hardening

Theme: consistency, resiliency, and safety across the six adapters.

Problems addressed: P18, P19, P20, P21, P22.

Solutions (high level):
- Introduce shared resiliency incrementally. The current code has a package-free
  `MtgMcpHttpRetry.SendForStringAsync` helper for text/json corpus requests, covering
  CommanderSpellbook and Decklists with transient retry, `Retry-After` handling, and
  redacted final failures. Scryfall and Archidekt retain source-specific pacing/retry
  because they have provider etiquette and mutation semantics. Moxfield and Playgroup
  keep adapter-local request loops because of Moxfield's curl fallback and Playgroup's
  per-request auth, but they share the redacted/truncated failure factory. A richer
  `Microsoft.Extensions.Http.Resilience`/Polly pipeline is deferred unless a future
  release needs registration-time timeout/circuit policy.
- Unify the adapter error model: bare `EnsureSuccessStatusCode` paths have been replaced,
  adapter HTTP failures share one redacted/truncated exception factory, and corpus
  aggregators degrade per source with redacted source-status notes. The remaining design
  question is whether Phase 3/4 should promote those failures into a richer typed
  result/outcome rather than the current exception-to-status mapping.
- Harden secret handling: make `SecretRedactor` precise (prefer structured/keyed
  redaction over substring whole-body replacement) to remove both false positives and
  the more dangerous keyword-less token false negatives; never apply coarse string
  redaction to raw bodies.
- Add Archidekt JWT expiry detection and re-login (currently cached for process
  lifetime, so expiry causes silent write failures).
- Shared helpers for JSON element readers, credentials-file parsing, `FirstNonEmpty`, and
  rate-limit body parsing are already in Core-adjacent utilities. Scryfall trend/meta now
  use the shared cache policy; Archidekt's card-id cache is documented as adapter-local
  mutation support state. Scryfall and Archidekt pacing now share a host-owned request
  pacer instead of adapter process-static mutable state.
- Re-evaluate or document the Moxfield `curl` fallback (external-binary dependency,
  fingerprint-fragile) and keep it disable-able and injection-safe.

Done when: custom adapter paths have explicit dispositions and share the same failure
conventions; secret redaction is precise and tested; Archidekt re-authenticates on
expiry; caching honors one policy.

Effort: L-XL. Independent of Phases 4/5 (can run in parallel).

---

## Phase 7 - Analytical depth and correctness

Theme: deepen the heuristics that are honest-but-shallow, and finish the determinism
story - without becoming a rules engine.

Problems addressed: P10 (real fix), P12, P13 (real fix), P14.

Solutions (high level):
- Replace the ramp-only evaluator behind `deck_evaluate_card` with a general
  operational-fact framework. Phase 7 now covers ramp, draw, and interaction first,
  with explicit unsupported-role output for future roles such as tutors and payoffs.
- Make `deck_estimate_commander_bracket` density-aware (not just max-signal floor),
  while keeping it advisory and explainable; Phase 7 added bracket-range calibration
  coverage and model docs.
- Unify simulation determinism: the goldfish family and draw/land odds Monte Carlo now
  use the deterministic RNG used by Stats Lab/race, with `rngKind` stamped on those
  result shapes where replay metadata is exposed.
- Keep the checked-in local combo dataset bounded and clearly labeled as fallback
  evidence (still catalog-first), so the offline/no-catalog experience is meaningful.
- Grow the offline calibration/benchmark suite so analytical changes are regression-safe.

Done when: card evaluation is honestly general (or honestly named/scoped); bracket and
combo depth improved with calibration; one documented determinism model across all
simulation tools.

Effort: L. Depends on: Phase 4 (typing) helpful; otherwise independent.

---

## Phase 8 - New capabilities (missing features)

Theme: add the high-value capabilities that are currently absent, on top of the now-
stable contracts.

Problems addressed: P23, P25, P26, P27.

Solutions (high level):
- Collection/ownership awareness: a way to record owned cards and answer "which cards
  in this deck/these candidates do I already own?" and budget-vs-owned framing. Keep it
  local-first and provider-neutral.
- Batch card lookup (`card_get_batch`) to hydrate many names in one call, using
  Scryfall's collection endpoint through the existing card catalog; reduces N tool calls
  to one.
- Optional image/art access (`card_get_image`) for multimodal clients, reusing existing
  Scryfall image URIs and returning links rather than binary image payloads.
- Price-source abstraction (`IPriceSource`) so cost analysis is no longer hard-bound to
  static pricing helpers. Current outputs already expose source/provenance fields; the
  new port preserves default normalized catalog/Scryfall-shaped behavior and leaves
  alternate provider selection for future work.

Done when: each capability ships behind a stable, schema-typed tool/resource with tests
and docs, and within a toolset so it can be enabled/disabled.

Effort: L. Depends on: Phases 2/3 (stable contracts) and Phase 1 (toolsets).

---

## Phase 9 - Observability, testing, performance, and release hardening

Theme: operational maturity and confidence to declare 1.0.

Problems addressed: P28, and cross-cutting hardening.

Solutions (high level):
- Structured logging (to stderr, stdio-safe) and lightweight metrics/timing for tool
  calls and source fetches; keep secrets out by construction.
- Expand the test matrix: an MCP client-compatibility matrix (advertise/list/call under
  representative clients), more E2E coverage of the consolidated surface, and adapter
  contract tests against recorded fixtures.
- Performance budgets for hot paths (simulation, large-deck analysis, source fan-out)
  with benchmarks wired into CI gates where practical.
- Finalize versioning/deprecation execution: complete any deprecation windows opened in
  Phases 1-3, then cut a stable 1.0 with a documented support policy.

Done when: tool calls are observable; client-compatibility and E2E suites are green;
perf budgets enforced; 1.0 released with a clear surface and support policy.

Effort: M-L. Runs partly throughout; finalized last.

---

## Sequencing / dependencies

```
Phase 0  (baseline, quick wins, policies)
   |
Phase 1  (surface consolidation + toolsets)        <- highest leverage
   |
Phase 2  (API/UX unification)
   |
Phase 3  (MCP protocol conformance) ----+
   |                                     |
Phase 4  (domain typing) --- can start alongside Phase 2
   |                                     |
Phase 5  (service decomposition)         |
                                         |
Phase 6  (adapter hardening) --- parallel track, independent
Phase 7  (analytical depth)  --- after Phase 4 (typing) ideally
Phase 8  (new features)      --- after Phases 1-3 (stable contracts)
Phase 9  (observability/release) --- cross-cutting, finalized last
```

Critical path for external (client-facing) quality: 0 -> 1 -> 2 -> 3.
Internal-quality track that can run in parallel: 4 -> 5, and 6.
Capability/correctness track: 7 and 8 once contracts are stable.

## Overall success criteria

- Advertised tool count at/under the agreed ceiling in the documented default/core
  profile, mode-aware, and toolset-gated.
- One output-control idiom and one result/error framing across the surface.
- High-traffic tools expose structured output and structured errors.
- Documentation matches the registered surface exactly (enforced by test).
- No tool over-claims relative to its implementation; one determinism model.
- Core uses union types for discriminated outcomes; no god-DTO edit operation.
- All adapters share resiliency, error model, and precise secret redaction.
- New capabilities (collection, batch lookup) shipped behind stable contracts.
- Guiding principles (Core independence, offline tests, API-only sources, no rules
  engine) preserved throughout.

## Appendix A: Surface consolidation candidates

Starting points for Phase 1 (illustrative, not final - other agents to confirm):

- Merge `deck_project_board_state` and `deck_estimate_win_turn` into views/parameters of
  `deck_simulate_goldfish` (they are already thin wrappers over one goldfish run).
- Collapse `workspace_checkpoint_*` and `archidekt_checkpoint_*` pairs behind a provider
  parameter on one checkpoint tool family.
- Reconsider whether `deck_compare_goldfish`, `archidekt_compare_goldfish`, and
  `deck_compare_workspaces_analysis` need to be three separate tools.
- Demote pure read-only "status/info" tools to resources where a canonical resource
  already exists (`server_get_info`, `source_list`, provider auth-status).
- Group the many `deck_analyze_*` tools and consider a single analysis entry point with
  a typed `aspect` selector for the lighter analyses.

## Appendix B: Explicitly out of scope (non-goals to preserve)

- A full Magic rules engine inside `mtg-mcp`.
- Vendoring XMage/mage-bench or requiring external simulators for ordinary tuning.
- HTML scraping, browser automation, or private web-app contracts for source data.
- Claiming abstract statistical simulation provides true matchup win rates.
- Making normal tests depend on network access or real Archidekt writeback.
