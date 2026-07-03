# Legacy Surface Audit And Disposition Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Chosen Audit Design

The audit starts at runtime entry points: `ToolRegistry.ToolTypes`, assembly
resource/prompt discovery, host DI registration, project references, and task
definitions. It then traces inward to service families, persistence, adapters,
and tests. This avoids declaring dynamically registered C# code dead merely
because ordinary call-site search is sparse.

Disposition describes future product ownership:

- **Rebuild:** preserve the outcome behind a child-defined contract; do not copy
  the current abstraction automatically.
- **Remove:** omit from the stable rewrite because it is outside the product
  boundary or duplicates LLM reasoning.
- **Experimental:** reconsider only behind a separately enabled experimental
  contract and PLC.
- **Unsupported:** the current provider or model cannot substantiate the claim.
- **Misleading:** behavior exists, but its name or placement can overstate its
  evidence or certainty.
- **Fixture-only:** production code is not retained, but sanitized examples or
  exact expected values may be reused after review.

## Architecture Findings

The host composes sixteen tool wrapper families over a large Core that owns
workspace editing, intent, analysis, plans, recommendations, simulations,
facets, collection, and Playgroup aggregation. HTTP adapters are separate, but
Core still coordinates provider evidence into decision-oriented services. The
rewrite retains the adapter isolation idea while replacing the broad Core and
public workflow graph.

Current local state is file-oriented:

- `workspaces/*.json` via `JsonDeckWorkspaceRepository`.
- `plans/*.json` via `JsonDeckPlanRepository`.
- `collection/*.json` via `JsonCardCollectionRepository`.
- `corpus-cache/*.json`, in-memory cache, or no cache via
  `CorpusCacheFactory`.

There is no hosted background worker, scheduler, or periodic timer. Network
activity is call-driven. This is valuable behavior to preserve: future cache
refresh remains explicit.

## Provider Disposition

| Project/provider | Current responsibility | Disposition |
| --- | --- | --- |
| `MtgMcp.Scryfall` | Live cards, search, prints, rulings, prices, Tagger `otag:` signals, trends, cache. | Rebuild official facts as immutable snapshots; replace Tagger signals with per-card cache. |
| `MtgMcp.Archidekt` | Observed private deck/folder/checkpoint and writeback contract. | Rebuild deck lifecycle, explicit sync, folder organization, and named snapshot lifecycle/restore from independently verified contracts. |
| `MtgMcp.Playgroup` | Selected official reads plus Core-derived observation lists and rankings. | Rebuild the complete documented public API; remove local ranking as provider fact. |
| `MtgMcp.Moxfield` | Unofficial automated deck retrieval. | Remove runtime adapter; retain manual interchange only. |
| `MtgMcp.Decklists` | EDHREC, EDHTop16, and TopDeck evidence sources. | Rebuild only in the post-cutover popularity PLC after permission review. |
| `MtgMcp.CommanderSpellbook` | Combo lookup and corpus signals. | Remove from stable cutover; reconsider as experimental evidence. |
| `MtgMcp.Core` | Domain plus storage, analysis, intent, plans, scoring, recommendations, simulation. | Replace with a small evidence/deck core and exact statistics. |
| `MtgMcp.App` | MCP composition, modes, tools, resources, prompts. | Replace with capability-prefixed tools, factual resources, and no prompts. |

## Trust And Correctness Findings

| Finding | Evidence | Consequence |
| --- | --- | --- |
| Live suite is empty | `Taskfile.yml` filters `Category=Live`; no matching test annotation exists. | Retain the task as an unsupported opt-in entry point; provider children add tests and stabilization verifies discovery before any live claim is accepted. |
| Tagger acquisition is absent | `ScryfallTaggerCorpusSignalProvider` issues curated Scryfall `otag:` searches; stored card annotations are test/manual inputs. | Do not describe current behavior as a complete local per-card Tagger cache. |
| Playgroup rank mixes populations and models | `playgroup_rank_decks` includes estimated power, Elo, win rate, and local derived metrics. | Rebuild provider facts separately; any later score is heuristic. |
| Goldfish is model output | Simulation tools and existing repair PLCs document optimistic and unsupported behavior. | Stable cutover removes it; experimental PLC owns feasibility. |
| Role/category analysis is not factual | Facets, embedded taxonomies, text parsing, and category rules feed role counts and recommendations. | Remove stable classifications unless source/provenance is explicit. |
| Known cross-analysis defects exist | Planned repair packets cover card snapshot integrity, deck counts, land entry, profile evidence, interaction readiness, and goldfish. | Do not use current passing tests as blanket correctness proof. |
| Live Scryfall responses are time-varying | TTL cache and on-demand HTTP reads can change between requests. | Immutable named snapshots replace “deterministic” live access. |

## Reuse Decision Procedure

A future child may reuse a source artifact only when it is dependency-allowed,
provider-neutral or correctly adapter-owned, independently tested, accurately
named, unaffected by the documented defects, and simpler than replacement.
Otherwise the old artifact is reference evidence. Sanitized fixtures require a
source/update note and must contain no secrets or private user data.

## Existing PLC Disposition Design

Existing PLCs and ordinary plans are treated as audited artifacts, not active
rewrite authority. This audit classifies each overlapping packet before the
foundation child may move, supersede, or consume it. Until the disposition
matrix is approved, no legacy PLC moves or edits are performed solely because
the umbrella exists.

Disposition uses these additional labels:

- **Absorb into child:** a rewrite child owns the durable requirement or design
  idea; the original packet becomes reference evidence after approval.
- **Post-cutover:** the topic is outside the stable `0.9.0` rewrite and needs a
  later PLC before implementation.
- **Superseded:** the packet's delivery path conflicts with the clean-break
  rewrite or removed stable surface.
- **Reference-only:** useful evidence remains, but the old packet is not an
  implementation guide.
- **Retain completed:** a completed packet remains historical or infrastructure
  guidance and is not reopened by the rewrite.

The matrix also records blocking conflicts for the foundation child. Known
review items include operation-mode vocabulary (`plan`/`apply` versus
`read-only`/`local`/`remote`) and additive `0.9` compatibility assumptions
versus the clean-break `0.9.0` target.

## Test Architecture

This audit is verified by static inventory, checked-in MCP inventory tests,
host registration inspection, source search, existing defect packets, and
documentation link checks. The foundation child must turn the approved
allowlists into architecture and surface tests before any deletion.

## Deferred Work

- Exact new schemas belong to their capability child.
- Source permission decisions belong to provider and popularity children.
- Code deletion belongs to the foundation implementation after approval.
- Experimental model feasibility belongs to separate post-cutover PLCs.
