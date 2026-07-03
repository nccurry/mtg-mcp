# Legacy Surface Audit And Disposition PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/legacy-surface-audit-and-disposition/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: disposition approved and handed off to foundation

## Summary

This docs-only packet inventories the current MCP, projects, persistence, and
provider integrations before the clean rewrite. It classifies behavior by
desired product outcome rather than by current class boundaries. It authorizes
no deletion or production edit; its approved disposition becomes the input to
the rewrite-foundation PLC.

It also classifies overlapping PLCs and ordinary plans so child 2 has a
reviewed source for what is superseded, absorbed, reference-only, post-cutover,
or still blocking.

The baseline contains 118 tools, 16 resources, and 18 prompts. The central
finding is that trustworthy source access and exact calculations are mixed with
intent inference, scoring, recommendations, simulated outcomes, and workflow
state. The rewrite should rebuild the evidence-bearing slices behind narrower
contracts and remove the decision-bearing surface from the stable product.

## Packet Contents

- [SRD.md](SRD.md): audit requirements and acceptance criteria.
- [SADD.md](SADD.md): classification method, findings, and reuse policy.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): how the disposition is reviewed and handed off.
- [FIXTURES.md](FIXTURES.md): complete surface, project, persistence, and defect inventories.

## Decision Snapshot

| Decision | Status | Rationale |
| --- | --- | --- |
| Rebuild outcomes, not existing service abstractions. | Accepted | The current layering contains useful evidence and tests but also embeds decisions the rewrite rejects. |
| Remove all stable MCP prompts. | Accepted | Prompt-owned judgment belongs to the calling LLM. |
| Rebuild local deck, Scryfall, Archidekt, Playgroup, exact-statistics, and Tagger capabilities. | Accepted | These capabilities match the evidence-first target. |
| Defer popularity sources until after cutover. | Accepted | Permission and population semantics require a separate PLC. |
| Treat goldfish, weakness, and replacement selection as experimental. | Accepted | They require explicit models and must not masquerade as facts. |
| Preserve only approved fixtures, schemas, exact test vectors, and repository wiring. | Accepted | Reusing current production abstractions would import accidental coupling. |
| Do not move or supersede existing PLCs until their audit dispositions are approved. | Accepted | Planning lifecycle changes must be explicit inputs to the foundation child. |

## Highest-Impact Findings

- **P1 — Live verification is advertised but absent.** `task test:live`
  filters on `Category=Live`, while static test inspection finds no matching
  test annotation. Provider-write confidence is therefore not established.
  Keep the task as the opt-in entry point, but describe it as unsupported until
  provider children add discoverable live tests; stabilization must prove the
  filter discovers the required tests before release.
- **P1 — Tagger is not a per-card Tagger cache.** Current support uses a curated
  tag catalog and live Scryfall `otag:` searches. Core can consume manually
  stored annotations, but no runtime path acquires complete per-card Tagger
  assignments.
- **P1 — Decision tools dominate the public surface.** Weak-spot review,
  best-practice analysis, intent suggestion, tuning reports, candidate scoring,
  bracket estimates, and advisor prompts encode choices the stable rewrite
  assigns to the LLM.
- **P1 — Simulation output has known model gaps.** Existing repair PLCs document
  optimistic goldfish behavior, category-evidence defects, land-entry defects,
  interaction-timing gaps, and count-semantics gaps.
- **P2 — Playgroup coverage is partial and mixed.** Seven tools expose selected
  reads and local ranking. The documented public API is broader, remote writes
  are absent, and estimated-power ranking is a local heuristic rather than a
  provider fact.
- **P2 — Unofficial providers are composed into stable workflows.** Moxfield
  automation and EDHREC-style sources have permission and stability concerns.
  They are not part of the stable cutover.
- **P2 — Persistence is fragmented JSON workflow state.** Workspaces, plans,
  collection, and corpus cache use separate file stores without the revisioned
  deck transaction model required by the rewrite.

## Deletion And Reuse Allowlist

Delete from the rewrite branch after the foundation child is approved:

- All advisor prompts, intent, recommendation, scoring, edit-plan, collection,
  facet-inference, and stable simulation surfaces.
- Current workspace/plan/collection JSON repositories and current tool wrappers.
- Automated Moxfield, Commander Spellbook, and current decklist-source runtime
  composition from the stable cutover.
- Legacy host registrations, configuration, and toolset mappings not explicitly
  reintroduced by a child PLC.

Eligible evidence for selective reuse:

- Sanitized HTTP fixtures and provider transport examples.
- Official schemas and known provider quirks.
- Exact-math test vectors and small exhaustive probability cases.
- Parser/import/export fixtures whose expected meaning is independently
  re-approved.
- Sanitized Commander Spellbook payloads and exact combo-query test vectors,
  only as fixture evidence for a separately approved experimental capability.
- Task, analyzer, coverage, package, release, and architecture-test wiring.

No current production service, interface, model, or helper is pre-approved for
source reuse.

## Guardrail Conformance

This packet preserves every umbrella guardrail. It distinguishes source facts,
exact derivation, heuristics, and sampled estimates; recommends no card or deck
decision; and changes documentation only.

## Planning Approval

- Status: Approved and completed
- Reviewed by: Two independent PLC reviewers; accepted by Nick Curry, repository owner
- Review date: 2026-07-03
- Reviewed revision: `9b6bfbd`
- Implementation authorized: No (docs-only audit; approval authorizes the foundation handoff, not production edits)

## Current Open Questions

None for drafting. Reviewers may reclassify individual rows before approving
the deletion and reuse allowlists.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Static MCP attribute inventory | Passed | 118 tools, 16 resources, and 18 prompts enumerated. |
| 2026-07-03 | Tool-name disposition reconciliation | Passed | Each of the 118 registered tool names appears exactly once in the disposition table. |
| 2026-07-03 | Live-test annotation search | Passed with finding | Task filter exists; no `Category=Live` test exists. |
| 2026-07-03 | Project and host registration inspection | Passed | Eight production projects and registered services inventoried. |
| 2026-07-03 | Background workflow search | Passed | No hosted background service or timer registration found. |
| 2026-07-03 | `task surface:report` | Blocked by environment | Concurrent compiler process locked `MtgMcp.Core.dll`; static inventory and checked-in surface tests supplied the counts. |
| 2026-07-03 | Existing PLC overlap review | Passed for draft | Planned, completed, ordinary-plan, and partial rewrite packets have disposition rows and blocker status. |
| 2026-07-03 | AMEND-002 Archidekt disposition | Updated; re-review required | Folder and checkpoint wrappers changed from `remove` to outcome-level `rebuild`; current production abstractions remain unapproved for reuse. |
| 2026-07-03 | Repository-owner disposition approval | Approved | The deletion, reuse, and overlapping-PLC dispositions, including AMEND-002, are authoritative inputs to the foundation implementation. |

## Completion Notes

This packet is the approved disposition input for
`rewrite-skeleton-foundation`; audit completion alone does not authorize
deletion outside the separately authorized foundation child.
