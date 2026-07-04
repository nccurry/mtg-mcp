# Exact Deck Statistics PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/exact-deck-statistics/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines provider-independent exact combinatorial analysis for decks
and raw populations. Every probability returns a reduced rational value plus a
documented rounded decimal. Callers explicitly define success groups, draw
schedules, mana-source capabilities, keep predicates, tutor equivalence, and
bottom policies. The package never classifies cards or falls back to sampling.

## Dependencies

- [Local Deck Store](../../completed/local-deck-store/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Use `BigInteger` combinations and reduced rational probabilities. | Proposed | Exactness is machine-verifiable. |
| Return 12-decimal, midpoint-to-even display values alongside rationals. | Proposed | Clients get stable presentation without losing exact values. |
| Accept explicit overlapping memberships and convert them to disjoint buckets. | Proposed | Joint conditions remain correct when a card belongs to several groups. |
| Bound exact work to one million states, population 1,000, eight groups, and turn 50. | Proposed | Work stays predictable without pretending a sampled answer is exact. |
| Treat mulligan keep and bottom behavior as caller-supplied policy. | Proposed | The MCP calculates; it does not decide what hands to keep. |
| Infer nothing from oracle text. | Proposed | Mana, tutors, and combo membership are explicit evidence inputs. |

## Public Surface

`stats_hypergeometric`, `stats_multivariate`, `stats_turn_table`,
`stats_mana_availability`, `stats_package_assembly`, `stats_mulligan`,
`stats_minimum_population`, and `stats_deck_summary`.

All tools are read-only, closed-world, and available in every operation mode.
Their descriptions state that callers supply every group, selector, source
capability, keep predicate, tutor equivalence, and bottom policy; none is
inferred or recommended by the statistics package.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No

## Guardrail Conformance

The package reports exact derivations with explicit assumptions and counted
sets. It provides no “good/bad” threshold, recommended land count, keep rule,
or card classification.
