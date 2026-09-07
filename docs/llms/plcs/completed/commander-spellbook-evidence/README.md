# Commander Spellbook Evidence PLC Packet

> [!IMPORTANT]
> This packet records the completed Commander Spellbook implementation and its
> validation evidence.

## Lifecycle

- Status: Complete
- Folder: `docs/llms/plcs/completed/commander-spellbook-evidence/`
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/README.md)
- Owner: mtg-mcp
- Created: 2026-09-07
- Last updated: 2026-09-07
- Current phase: Complete
- Implementation authorized: Yes

## Summary

Commander Spellbook has a public, documented API for combo variants and deck
combo lookup. This completed phase added it as an opt-in `spellbook` toolset.

The MCP returns Commander Spellbook's data. It does not select a combo, judge a
combo, estimate a deck's power, or tell a player to add or remove cards.

The shipped version has three read-only tools:

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

**Request:**

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

**Response shape:**

```json
{
  "deck": {
    "deckId": "b1357522-1eb0-487b-8c6a-7a3bcad255b5",
    "revision": 12,
    "sentEntries": [],
    "skippedEntries": []
  },
  "source": {
    "name": "Commander Spellbook",
    "operation": "find-my-combos",
    "request": {
      "sourceQuery": "legal:commander",
      "page": {
        "limit": 20,
        "offset": 0,
        "groupByCombo": true
      }
    },
    "cacheStatus": "network",
    "retrievedAtUtc": "2026-09-07T02:00:00Z",
    "sourceUrl": "https://commanderspellbook.com",
    "data": {
      "count": null,
      "next": null,
      "previous": null,
      "results": {
        "identity": "UBR",
        "included": [],
        "includedByChangingCommanders": [],
        "almostIncluded": [],
        "almostIncludedByAddingColors": [],
        "almostIncludedByChangingCommanders": [],
        "almostIncludedByAddingColorsAndChangingCommanders": []
      }
    }
  }
}
```

The `source.data` object keeps the source's field names and values. The local
`deck` object stays separate from provider evidence. Source combo groups are
inside one `results` object, not a list of result objects. The example mirrors
the Commander Spellbook v6.3.3 response layout checked on 2026-09-07. The MCP
still preserves fields that the source adds later. The `source.request` object
shows this tool call's query and paging controls. It is made in memory for each
result and is not written to the cache.

## Packet Contents

- [SRD.md](SRD.md): requirements and acceptance rules.
- [SADD.md](SADD.md): adapter, cache, and MCP design.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): delivery phases.
- [FIXTURES.md](FIXTURES.md): API, cache, pacing, and deck cases.

## Decision Snapshot

| Decision | Status | Reason | Detail |
| --- | --- | --- | --- |
| Add an opt-in `spellbook` toolset. | Implemented | Provider data is useful but not part of the small default surface. | [SADD](SADD.md#mcp-surface) |
| Start with search, get, and saved-deck lookup. | Implemented | These complete the evidence workflow without a generic provider router. | [SADD](SADD.md#mcp-surface) |
| Pass source query text through unchanged. | Implemented | Commander Spellbook owns its query language. | [SADD](SADD.md#request-design) |
| Use a local exact-response cache only. | Implemented | It reduces repeated provider calls without keeping a deck database or activity history. | [SADD](SADD.md#cache-design) |
| Cache for 15 minutes by default. | Implemented | The data can change, but repeated agent calls must not create needless traffic. | [SADD](SADD.md#cache-design) |
| Make one upstream request per tool call. | Implemented | The source asks clients to make sparse requests. | [SADD](SADD.md#provider-stewardship) |
| Do not expose bracket estimates. | Implemented | A qualitative provider score does not fit this first evidence-only slice. | [SADD](SADD.md#non-goals) |
| Do not use the bulk data export. | Implemented | The first slice needs small live evidence, not a full local copy. | [SADD](SADD.md#non-goals) |

## Project and Surface Impact

The phase adds these areas:

- `MtgMcp.Spellbook` for the HTTP client, pacing, cache, source contract, and
  unchanged Commander Spellbook responses.
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
- [x] The three tools have distinct inputs and outputs.
- [x] The test, Task, coverage, and documentation changes are named.
- [x] The owner approved the saved-deck-only first interface on 2026-09-07.
- [x] An independent design review has examined this packet and its findings are fixed.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-07 | Public API root and OpenAPI schema | Passed | The source documents sparse use, a `User-Agent`, 429 handling, credit, variant routes, and `DeckRequest`. |
| 2026-09-07 | Current API routes | Passed | `/variants/`, `/variants/{id}/`, and `/find-my-combos` are documented. |
| 2026-09-07 | Bounded live response check | Passed | API v6.3.3 accepted the planned search and saved-deck requests. Deck lookup returned a paginated response whose `results` object has identity plus Commander Spellbook's six combo groups. |
| 2026-09-07 | Repository architecture review | Passed | Existing Playgroup and Scryfall adapters show the required App/adapter split and offline fixture pattern. |
| 2026-09-07 | Independent design review | Passed after fixes | Added explicit paging defaults, a fixed transport timeout, caller-visible sent-request facts, cache-contract invalidation, atomic SQLite pacing, and a real separate-server cache test. |
| 2026-09-07 | Phase 2 App and process checks | Passed | The opt-in toolset, local deck bridge, configuration, architecture checks, normal tests, and coverage gates pass. |
| 2026-09-07 | Bounded live variant lookup | Passed | The `Category=Live` test made one bounded `GET /variants/` request and accepted the source response. |
| 2026-09-07 | Phase 3 validation | Passed | `task ci`, `task test`, `task test:integration`, `task coverage:spellbook`, `task pack VERSION=0.9.0`, and `task release:tool-smoke VERSION=0.9.0` passed. Spellbook line coverage was 94.63%. |
| 2026-09-07 | Final reviews | Passed after fixes | Code-quality, correctness, test, structure, and plain-language reviews found no remaining P1 or P2 issue. The implementation now returns a typed error for a corrupt or unavailable cache, tests local deck bounds, and uses clearer public wording. |

## Deferred Work

None for this child. Broader raw deck input, bulk exports, and any new source
surface need separate approval and a separate packet.
