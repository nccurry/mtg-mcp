# Commander Spellbook Evidence PLC Packet

> [!IMPORTANT]
> This packet plans a new provider. It does not authorize production edits.
> An independent review and an explicit `Implementation authorized: Yes` are
> required before code changes begin.

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/commander-spellbook-evidence/`
- Parent PLC: [Evidence-First Deckbuilding Evolution](../evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Created: 2026-09-07
- Last updated: 2026-09-07
- Current phase: Planning complete
- Implementation authorized: No

## Summary

Commander Spellbook has a public, documented API for combo variants and deck
combo lookup. This phase adds it as an opt-in `spellbook` toolset.

The MCP returns Commander Spellbook's data. It does not select a combo, judge a
combo, estimate a deck's power, or tell a player to add or remove cards.

The first version has three read-only tools:

| Tool | Input | Output |
| --- | --- | --- |
| `spellbook_variant_search` | A non-empty Commander Spellbook query and one bounded page. | One provider result page. |
| `spellbook_variant_get` | An exact Commander Spellbook variant ID. | One provider variant. |
| `spellbook_deck_combos_find` | A saved local deck ID, expected revision, and one bounded page. | Commander Spellbook's own combo groups for that deck. |

The deck tool reads only `main` and `commander` entries from the saved deck.
It sends a structured deck request to Commander Spellbook. It reports skipped
sideboard and maybeboard entries. It does not infer a format, choose a query,
or change the deck.

## What the Deck Tool Looks Like

**Proposed request:**

```json
{
  "deckId": "b1357522-1eb0-487b-8c6a-7a3bcad255b5",
  "expectedRevision": 12,
  "sourceQuery": "legal:commander",
  "limit": 20,
  "offset": 0,
  "groupByCombo": true
}
```

**Proposed response shape:**

```json
{
  "source": "Commander Spellbook",
  "operation": "find-my-combos",
  "cacheStatus": "network",
  "retrievedAtUtc": "2026-09-07T02:00:00Z",
  "sourceUrl": "https://commanderspellbook.com",
  "deck": {
    "deckId": "b1357522-1eb0-487b-8c6a-7a3bcad255b5",
    "revision": 12,
    "ignoredEntries": []
  },
  "data": {
    "count": 1,
    "next": null,
    "previous": null,
    "results": [
      {
        "included": [],
        "almostIncluded": []
      }
    ]
  }
}
```

The `data` object keeps the source's field names and values. Source combo
groups are inside its `results` entries. The example is a proposal, not a
current API.

## Packet Contents

- [SRD.md](SRD.md): requirements and acceptance rules.
- [SADD.md](SADD.md): adapter, cache, and MCP design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): delivery phases.
- [FIXTURES.md](FIXTURES.md): API, cache, pacing, and deck cases.

## Decision Snapshot

| Decision | Status | Reason | Detail |
| --- | --- | --- | --- |
| Add an opt-in `spellbook` toolset. | Proposed | Provider data is useful but not part of the small default surface. | [SADD](SADD.md#mcp-surface) |
| Start with search, get, and saved-deck lookup. | Accepted | These complete the evidence workflow without a generic provider router. | [SADD](SADD.md#mcp-surface) |
| Pass source query text through unchanged. | Proposed | Commander Spellbook owns its query language. | [SADD](SADD.md#request-design) |
| Use a local exact-response cache only. | Proposed | It reduces repeated provider calls without keeping a deck database or activity history. | [SADD](SADD.md#cache-design) |
| Cache for 15 minutes by default. | Proposed | The data can change, but repeated agent calls must not create needless traffic. | [SADD](SADD.md#cache-design) |
| Make one upstream request per tool call. | Proposed | The source asks clients to make sparse requests. | [SADD](SADD.md#provider-stewardship) |
| Do not expose bracket estimates. | Accepted | A qualitative provider score does not fit this first evidence-only slice. | [SADD](SADD.md#non-goals) |
| Do not use the bulk data export. | Accepted | The first slice needs small live evidence, not a full local copy. | [SADD](SADD.md#non-goals) |

## Project and Surface Impact

The phase adds these areas:

- `MtgMcp.Spellbook` for the HTTP client, pacing, cache, source contract, and
  provider-shaped output.
- `MtgMcp.App/Spellbook` for MCP tools and local-deck input mapping.
- `MtgMcp.Spellbook.Tests` for fake HTTP, cache, pacing, and source fixtures.
- App, E2E, architecture, Task, coverage, solution, and documentation updates.
- The `spellbook` capability descriptor and three read-only tools.

It does not change `MtgMcp.Core`, local deck storage, operation modes, Scryfall
data, Archidekt behavior, or exact statistics.

## Provider Rules Used Here

Commander Spellbook asks clients to make sparse unauthenticated requests, name
their service in `User-Agent`, handle `429`, and give credit with a link. The
variant endpoint warns clients not to page through the full data set. The source
publishes a separate bulk file for that job.

This packet follows those operational rules. It does not add account workflows,
scraped content, or source-data resale.

## Planning Readiness

- [x] The source API and request format were examined.
- [x] The source's request guidance and credit request were recorded.
- [x] The adapter and App boundaries are clear.
- [x] The cache purpose, duration, and retained data are clear.
- [x] The three proposed tools have distinct inputs and outputs.
- [x] The test, Task, coverage, and documentation changes are named.
- [x] The owner approved the saved-deck-only first interface on 2026-09-07.
- [x] An independent design review has examined this packet and its findings are fixed.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-07 | Public API root and OpenAPI schema | Passed | The source documents sparse use, a `User-Agent`, 429 handling, credit, variant routes, and `DeckRequest`. |
| 2026-09-07 | Current API routes | Passed | `/variants/`, `/variants/{id}/`, and `/find-my-combos` are documented. |
| 2026-09-07 | Repository architecture review | Passed | Existing Playgroup and Scryfall adapters show the required App/adapter split and offline fixture pattern. |
| 2026-09-07 | Independent design review | Passed after fixes | Added explicit cache-contract invalidation, atomic SQLite pacing, and a real separate-server cache test. |
