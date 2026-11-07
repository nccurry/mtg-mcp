# Deck Analysis Ergonomics Plan

Agents reviewing several Archidekt decks read every entry with `archidekt_deck_get`
when one `card_list_query` or `card_lists_compare` call over an `archidekt-deck`
source would have answered the question. This packet points agents to those
tools, adds the filters deck questions need (fits a color identity, one
condition or another), trims concise Archidekt deck reads, and shrinks the two
card-list input schemas.

## Current Work

- Status: Planned (draft for owner review)
- Owner: mtg-mcp (@nccurry)
- Last updated: 2026-11-07, packet merged into `main` at `8e45bfb`; code facts read at `ece3ba9`
- Implementation authorized: No. The owner must accept the packet and answer the open questions first.
- Next action: owner review of the [open questions](#open-questions) and the decisions in [SADD.md](SADD.md).
- Blocker: None
- Depends on: None. Shares files with [deckbuilding-agent-guidance](../deckbuilding-agent-guidance/README.md); see the open question on order.
- Current state: requirements and design drafted from one observed session and a read of the code; nothing built.
- Resume check: this packet is in `planned/` on `main`; no implementation worktree for `deck-analysis-ergonomics` exists.

## Phases

| Phase | Name | Owner | State |
| --- | --- | --- | --- |
| P1 | Point agents to deck analysis tools (REQ-001 to REQ-003, REQ-009) | | Planned |
| P2 | `card_list_query` filters (REQ-004, REQ-005, REQ-011) | | Planned |
| P3 | Concise Archidekt deck reads (REQ-006, REQ-007) | | Planned |
| P4 | Card-list input schema size (REQ-008) | | Planned |
| P5 | Docs, changelog, `task ci`, full audit (REQ-010) | | Planned |

P1, P2, and P3 touch separate code and can run in parallel; P1 and P2 both
edit the `card_list_query` description, so the second to merge rebases. P4
starts after P2, because `whereAny` adds to the schema P4 measures. P5 starts
after all four.
The endpoint is P1 to P5 merged into `main`; a release is not part of this
work unless the owner adds it.

## Done when

The requirements and their checks are in [SRD.md](SRD.md).

## Open questions

- Open question: May REQ-006 remove `setCode` and `collectorNumber` from concise entries, and the extra category and `extensions` fields from the concise header, in the next minor without a deprecation release, as the `0.15.0` clean break did? The alternative keeps them and adds only a `fields` choice. (blocks P3; waits on @nccurry)
- Open question: This packet and [deckbuilding-agent-guidance](../deckbuilding-agent-guidance/README.md) both edit the server instruction jobs (its REQ-015; 334 characters of the 2,000 limit are left today) and the `card_list_query` input schema (its REQ-011 adds `tokenKeywords` and `tokenTypes` to the field enums and as new list fields for `within`; its REQ-014 changes `pageSize`). Which lands first? The second rebases its instruction text and remeasures the REQ-008 schema budget. (blocks P1 and P4 merge order; waits on @nccurry)
- Open question: Should one call compare more than two decks, for example a union of five Archidekt decks grouped by source deck? Today that question takes one `card_lists_compare` call per pair. It adds a source kind or a `groupBy` value, so it is new scope, not part of REQ-001 to REQ-011. (blocks nothing; waits on @nccurry)

## Log

- 2026-11-07 Created from a deck-research session that reviewed five Wilson, Refined Grizzly decks on Archidekt; evidence in [SRD.md](SRD.md#current-system-and-prerequisites).
- 2026-11-07 Independent review: 12 findings, all supported and fixed. Added REQ-011 (operator text in descriptions) and D-6 (`missingValues` with `whereAny`); added `stats_castability` to REQ-008; corrected the `inDeck`, schema, and refusal facts; P4 now follows P2.
