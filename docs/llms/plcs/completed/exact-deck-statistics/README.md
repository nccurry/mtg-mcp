# Exact Deck Statistics PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/exact-deck-statistics/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-09
- Current phase: All phases complete

## Summary

This packet defines provider-independent exact combinatorial analysis for raw
populations and explicitly selected local-deck entries. Every probability
returns a reduced rational plus fixed 12-place display values. Callers define
the population, groups, draw schedules, source capabilities, keep constraints,
package requirements, numeric values, and zone partitions. The package never
classifies cards, checks deck legality, recommends thresholds, or falls back to
sampling.

## Dependencies

- [Local Deck Store](../../completed/local-deck-store/README.md)
- [MCP Capability Toolsets](../../completed/mcp-capability-toolsets/README.md)
- [MCP Contract And Adapter Hardening](../../completed/mcp-contract-and-adapter-hardening/README.md)
- [Rewrite program](../evidence-first-mcp-rewrite-program/README.md)

## Accepted Decisions

| Decision | Rationale |
| --- | --- |
| Use `BigInteger` combinations and reduced rationals. | Exactness remains machine-verifiable without numeric overflow or floating-point comparisons. |
| Return invariant 12-place, midpoint-to-even decimal and percent strings. | Clients receive stable presentation without losing the exact numerator and denominator. |
| Model raw and deck-backed populations through a closed input union. | Deck entries are selected explicitly; format names and Commander conventions never alter the population. |
| Combine exact entry-ID, exact zone-name, and category-ID selector terms by set union. | Callers can build useful populations without a free-form expression language. |
| Expand stored quantities and disclose selected/excluded entries in canonical deck order. | Deck-backed results are reproducible and auditable. |
| Convert explicit overlapping group memberships to disjoint buckets. | Joint predicates count each physical card copy once while still allowing one observed card to contribute to multiple groups. |
| Use one request-wide budget of 1,000,000 work units. | Composed tables, attempts, candidates, bucket states, and allocation checks remain predictably bounded. |
| Use a statistics-specific exact/bounded outcome inside the common operation result. | Structured limit detail is returned without weakening the shared result union or attaching a partial probability. |
| Accept caller-supplied numeric values keyed by entry ID. | Local decks do not store mana values or arbitrary numeric facts, and Statistics performs no provider lookup. |
| Model turns as an explicit ordered `drawsByTurn` schedule. | The MCP does not infer play/draw, multiplayer, replacement-effect, or normal-turn rules. |
| Model mulligans as an explicit ordered attempt schedule. | Draw count, bottom count, and forced-final behavior remain caller-owned and exact. |
| Support only explicit W/U/B/R/G/C production plus generic payment. | Hybrid, phyrexian, snow, activation costs, sequencing, and tapped-state inference remain outside the exact contract. |
| Rename the inverse tool to `stats_minimum_count`. | The varied value is a success/source copy count inside a fixed population, not the total population. |

## Public Surface

The default-enabled `stats` toolset contains exactly eight read-only tools:

- `stats_hypergeometric`
- `stats_multivariate`
- `stats_turn_table`
- `stats_mana_availability`
- `stats_package_assembly`
- `stats_mulligan`
- `stats_minimum_count`
- `stats_deck_summary`

All eight tools are visible in `read-only`, `local`, and `remote`. They make no
HTTP requests and perform no writes. Deck-backed requests load only a supplied
deck ID and expected revision through the local read boundary.

After this child, expected surfaces are:

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 30 | 51 | 51 |
| `all` | 55 | 77 | 90 |

The final post-categorization targets remain 32/54/54 for `default` and
57/80/93 for `all`. Counts are reconciliation checks, not compatibility
requirements.

## Input And Evidence Boundary

Probability tools accept either raw disjoint population buckets or a local deck
population. A local population contains one or more typed selector terms:
exact entry IDs, exact zone names, or category IDs. Terms combine by set union,
duplicates collapse by entry ID, quantities expand exactly, and every named
group must select a subset of the chosen population. Results disclose the
canonical selected and excluded entries and their quantities.

Structural deck summaries use stored deck fields only. Numeric histograms and
percentiles use optional caller-supplied exact decimal strings keyed by entry
ID. No statistics operation queries Scryfall or treats absent values as zero.

Every completed calculation identifies its stable formula ID, `exact-v1`
calculation version, canonical inputs, assumptions, exact derivation evidence,
and package implementation version. Bounded work returns a typed
`bounded-unsupported` outcome with no partial calculation.

## Toolset And North-Star Acceptance

- Toolset: `stats`, enabled by the default profile.
- User question answered: what is the exact probability or deterministic deck
  composition result for these explicit groups and assumptions?
- Evidence type: exact derived mathematics, never provider fact or heuristic.
- Replay boundary: canonical inputs, selected deck revision/entries, formula,
  assumptions, exact rational, and calculation version reproduce a result.
- Unknown boundary: malformed input, missing decks, stale revisions, missing
  caller values, and bounded exact work remain explicit.
- Decision boundary: the package does not infer card roles, mana capability,
  keep rules, thresholds, zones to include, deck format rules, or whether a
  probability is good.

## Planning Approval

- Status: Approved
- Reviewed by: Independent Codex sub-agent against commit `4fe5b51`
- Review date: 2026-07-09
- Reviewed revision: Contract reconciliation recorded in this packet
- Implementation authorized: Yes, by the repository owner's explicit request
  to review, fix, and implement this PLC phase by phase

## Guardrail Conformance

The package reports exact derivations with explicit assumptions and counted
sets. It provides no good/bad threshold, recommended count, keep decision, card
classification, format validation, Commander rule, or legality result.

## Completion Evidence

- All 156 Statistics unit tests, 101 App tests, 14 architecture tests, and the
  complete 630-test offline suite pass.
- The official C# client discovers the exact eight-tool `stats` surface in all
  modes, recursively verifies public input descriptions, exercises structured
  failure cases, and runs all eight tools against an explicit 99-card library.
- The realistic opening-hand probability is checked against an independent
  direct-combination formula. An identical custom-format library returns the
  same exact rational without a commander-zone entry.
- Static surfaces reconcile to 30/51/51 for `default`, 55/77/90 for `all`, and
  8/8/8 for explicit `stats`.
- Line coverage is 96.27 percent for `MtgMcp.Statistics` and at least 90 percent
  for every production assembly.
- Lint, package, process smoke, official-client smoke, installed-tool smoke,
  vulnerability, deprecation, outdated-package review, and applicable
  abstraction/code/dead-code/test/docs audits pass. Available patch updates
  were reported but were not required by this dependency-free child.
