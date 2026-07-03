# MCP Trust Evidence PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/mcp-trust-evidence/`
- Owner: mtg-mcp
- Created: 2026-06-28
- Last updated: 2026-07-03
- Current phase: planning

## Summary

This packet defines the planned work to make MCP analysis, recommendation,
simulation, and source outputs clearer about what is source-backed, derived,
heuristic, or unknown. The smallest useful slice is a docs-backed plan for
correcting known trust leaks without implementing code yet.

The packet keeps correctness fixes separate from broader provenance work:
tri-state legality, summary caveats, and Commander bracket 1-5 behavior can be
reviewed independently before adding evidence tiers, role provenance, Tagger
evidence, or profile externalization.

Goldfish-specific summary and detail behavior is now owned by
[conservative-goldfish-v2](../conservative-goldfish-v2/README.md). This packet
continues to own the shared REQ-005 evidence vocabulary; its general additive
compatibility policy does not override compatibility decisions made by the six
Jasmine analysis repair packets.

## Packet Contents

- [SRD.md](SRD.md): requirements, acceptance criteria, scope, and validation expectations.
- [SADD.md](SADD.md): architecture, design tradeoffs, runtime flow, and test architecture.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): implementation phases and exit criteria.
- [FIXTURES.md](FIXTURES.md): fixture IDs, acceptance matrices, provider payloads, MCP surface inventories, and calibration cases.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| Use one Core-owned tri-state legality result. | Proposed | Current recommendation/query paths treat missing legality inconsistently; one result prevents missing metadata from silently meaning legal. | [SADD.md](SADD.md#chosen-design) |
| Use the closed evidence tier wire values from the SRD. | Proposed | Fixture and surface tests need exact strings before implementation starts. | [SRD.md](SRD.md#interfaces-data-states-and-modes) |
| Keep role classification cheap and evolve existing explanation rows. | Proposed | `DeckRoleClassifier` is a hot path, and `DeckRoleCountExplanation` already carries role/count evidence that should not be duplicated. | [SADD.md](SADD.md#chosen-design) |
| Gate evidence detail by MCP detail level. | Proposed | Summary output needs labels and caveats, while normal/full can carry larger success sets and per-card provenance. | [SADD.md](SADD.md#mcp-surface-schemas-and-diagnostics) |
| Delegate goldfish summary and detail contracts. | Accepted | The replacement goldfish model needs one atomic schema owner; REQ-003 is superseded there and the goldfish portion of REQ-008 is delegated. | [Conservative Goldfish V2](../conservative-goldfish-v2/SRD.md#requirements) |
| Treat Phase 3 as range/calibration work, not evidence-tier work. | Proposed | Canonical evidence tier fields arrive in Phase 4; Phase 3 should use existing notes/labels only. | [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md#phase-3-commander-bracket-1-5-correction) |
| Separate live/cached Tagger signals from local annotations. | Proposed | The Scryfall Tagger corpus provider already uses `otag:` queries; the remaining risk is Core labeling that conflates provider signals, user-set annotations, and embedded taxonomy. | [SADD.md](SADD.md#adapter-and-provider-contracts) |
| Defer broad profile externalization. | Proposed | Bracket/scoring profiles are the first likely useful slice; role-rule externalization can wait until tuning demand is proven. | [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md#phase-8-small-profile-externalization) |

## Project And Surface Impact

Affected projects and surfaces are expected to include:

- `MtgMcp.Core`: legality helper, evidence tier vocabulary, classifier explanation models, odds and recommendation metadata.
- `MtgMcp.App`: MCP presenters, tool descriptions, annotations, detail-level behavior, and surface tests.
- `MtgMcp.Scryfall`: fixture-backed Tagger evidence queries through existing Scryfall search routes if implementation reaches that phase.
- `MtgMcp.CommanderSpellbook` and corpus providers: existing `Source`/`SourceKind` labels should be inventoried before Phase 4 changes.
- `tests/MtgMcp.Calibration` and calibration corpus data: bracket 1-5 guards, fixtures, and reports are in scope for Phase 3.
- Documentation: `docs/toolsets.md`, `docs/commander-bracket-model.md`, simulation/profile docs, and this PLC packet.
- Tests: Core unit tests, App surface tests, fixture-backed adapter tests, bracket calibration tests, and offline task workflows.

No mutating MCP tool behavior, Archidekt writes, persistence format migrations,
or live-test requirements are intended by the first implementation slice.

## Current Open Questions

| Question | Impact | Owner | Resolution plan |
| --- | --- | --- | --- |
| What exact criteria should classify bracket 5/cEDH in the existing heuristic model? | Affects calibration fixtures and user-facing bracket output. | mtg-mcp implementer | Define criteria with fixture decks before changing the bracket range validator. |
| Which MCP outputs should first expose evidence tiers beyond existing evidence carriers? | Affects Phase 4 scope and surface tests. | mtg-mcp implementer | Start with `SourceEvidenceMetadata`, `DeckRoleCountExplanation`, corpus evidence tables, combo sources, and bracket/simulation labels; add more only when required by a phase. |

## Planning Readiness Checklist

- [x] Scope and non-scope are explicit.
- [x] Must requirements are testable and have acceptance criteria.
- [x] Major alternatives and tradeoffs are recorded.
- [x] Quality attributes are measurable or inspectable.
- [x] Core/App/adapter/test boundaries and dependency impact are explicit.
- [x] MCP surface, operation-mode, and documentation impacts are clear.
- [x] Adapter auth, pacing, cache, retry, and error-sanitization impacts are clear when relevant.
- [x] Documentation, readability, and abstraction reuse expectations are clear.
- [x] SRD maps Must requirements to acceptance criteria and validation.
- [x] Implementation plan has phase exit criteria.
- [x] Deferred work is visible and not required by the first implementation phase.

## Implementation Checklist

- [ ] Packet moved to `docs/llms/plcs/in-progress/mcp-trust-evidence/`.
- [ ] Current phase is named before code changes start.
- [ ] SRD/SADD updated when implementation changes the plan.
- [ ] Validation evidence recorded as phases complete.
- [ ] Obsolete or duplicate code is removed as replacement work lands.
- [ ] Completed or deferred requirements are marked in the implementation plan.
- [ ] Final review title uses a concise outcome-oriented subject.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-06-28 | PLC packet drafted from templates | Pending review | Docs-only packet; no product code implementation. |
| 2026-06-28 | PLC packet hardened after review | Passed | Added legality policy matrix, exact evidence tier strings, Phase 3/4 dependency decision, existing-surface reuse guidance, and concrete starter cases. |
| 2026-07-03 | Goldfish ownership reconciliation | Passed | REQ-003 superseded; REQ-005 retained; goldfish REQ-008 detail gating delegated. |

## Completion Notes

This PLC remains planned. Move it to `in-progress` only when implementation
starts, and move it to `completed` after validation is recorded or the packet is
explicitly superseded or abandoned.
