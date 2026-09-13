# Phase 5 Source Feasibility Record

## Status

- Phase: 5, community and deck-group source feasibility.
- Decision date: 2026-09-08.
- Owner: mtg-mcp.
- Result: No new source is admitted.
- Production code, MCP tools, provider caches, configuration, credentials, and
  source fixtures: none added.

This record evaluates a small set of requested community and deck-popularity
sources. A deck group means the exact set of decks that one source defines. It
is not a claim about all Commander decks or which cards are good.

## Decision Summary

| Candidate | Requested use | Decision | Reason | Reopen trigger |
| --- | --- | --- | --- | --- |
| Reddit Data API | Return bounded, attributed discussion about a card or archetype. | Deferred. | Reddit requires a request and explicit approval before API access. The policy applies to AI agents. No approval exists for this MCP workflow. | Reddit approves the exact read-only, attributed MCP-to-LLM workflow. |
| EDHREC website | Return EDHREC popularity or deck-group data. | Rejected for direct automation. | No official public developer API was found in the pages checked. Its published site terms prohibit automated agents or scripts that generate searches, requests, or queries. | EDHREC publishes an API and terms that permit this exact use. |
| Moxfield website | Gather public deck groups or card-use data. | Rejected for direct automation. | Its current terms prohibit robots and other automatic access without written approval. Manual interchange remains supported. | Moxfield publishes a supported API or grants written permission for the exact workflow. |
| Archidekt public deck collection | Build a public deck group beyond a player's own authorized decks. | Not admitted. | Its site terms prohibit automated searches, requests, and queries. The existing user-authorized sync adapter is unchanged; this decision covers only a new public collection feature. | Archidekt publishes a documented public deck-group API or permission for this exact workflow. |
| Playgroup Public API | Replace an EDHREC-style card-use source. | Not suitable for this use. | The pinned official contract exposes individual/user deck observations and a deck-list URL, but no complete card entries or global deck-group route. | The official API adds complete deck entries and a bounded, documented group query. |

## Evidence Checked

### Reddit

Reddit's [Responsible Builder Policy](https://support.reddithelp.com/hc/en-us/articles/42728983564564-Responsible-Builder-Policy)
requires a request and explicit approval before API access. It explicitly covers
apps, bots, and AI agents. Its [Data API guide](https://support.reddithelp.com/hc/en-us/articles/16160319875092-Reddit-Data-API-Wiki)
also requires registered OAuth, a truthful User-Agent, response-header rate
handling, and removal of deleted user content. The published free-access limit
is 100 queries per minute per OAuth client ID.

Reddit therefore has a technical read path, but it is not ready for this
project. No one has approved the exact use, the repository has no Reddit
credential or configuration path, and no decision has been made about how a
future adapter would handle deleted content. The future child must state that
it does not train a model on Reddit content, returns only bounded attributed
material, and follows the provider's current retention rules.

### EDHREC

EDHREC describes itself as a Commander data site and says that it gets decklists
from [Archidekt and Moxfield](https://edhrec.com/about-us). That makes any
result a view of that source-defined deck group, not a universal Commander
population. The official [EDHREC terms](https://edhrec.com/terms) grant a
personal, noncommercial site license and prohibit automated agents or scripts
that generate searches, requests, or queries. No official public developer API
was found in the EDHREC pages checked on the decision date.

Do not call undocumented JSON routes, automate a browser, or turn public web
pages into a cache.

### Moxfield and Archidekt

The current [Moxfield terms](https://moxfield.com/help/terms) prohibit robots
and other automatic access without written approval. The current
[Archidekt terms](https://archidekt.com/terms) prohibit automated searches,
requests, and queries. These are enough to reject new public deck-group
collection work.

This does not change the current manual Moxfield interchange or the existing
Archidekt adapter. The adapter's user-authorized deck synchronization is a
separate, already shipped workflow; this record does not widen it into public
deck discovery or collection.

### Playgroup

The checked-in [Public API 1.0.0 contract](../../../../../src/MtgMcp.Playgroup/Fixtures/OpenApi/public-v1-1.0.0.yaml)
has only specific deck, user, playgroup, game, commander, and live-session
routes. A deck contains provider statistics and a `decklist_url`; it does not
contain card entries. There is no global deck-list route. It can provide its
own provider observations, but it cannot provide the complete card lists needed
for the requested card-use distribution.

## What Phase 5 Changes

Phase 5 makes no runtime change. It records that the following are not allowed:

- Scraping or browser automation for Reddit, EDHREC, Moxfield, or public
  Archidekt deck collection.
- Guessing API endpoints from browser traffic or public pages.
- Storing source content while no supported source contract exists.
- Treating a source's popularity ranking as a deck-quality judgment.

The existing Scryfall, Archidekt, Playgroup, Commander Spellbook, local deck,
and exact-statistics workflows remain useful without these sources.

## No Runtime Contract

For every candidate in this record, the following details are not applicable
yet because Phase 5 adds no runtime provider contract:

| Detail | Current decision |
| --- | --- |
| Authentication and configuration | No new credential or configuration path exists. The existing user-authorized Archidekt workflow is unchanged. |
| Pacing and retries | The project makes no source request, so it has no new pacing or retry behavior. |
| Cache, expiry, and deletion | No source content or response is stored. A future Reddit child must define deleted-content handling before it can run. |
| Fixtures | No provider response fixture is added. |
| Failure behavior | No source tool or provider operation exists, so there is no new failure result to return. |
| Output and evidence labels | No source result is returned. A future admitted source must identify the source, retrieval time, cache state, and population limits. |

## Requirements And Exit Criteria

| Requirement | Phase 5 evidence |
| --- | --- |
| EFD-006, source safety | Each candidate has a supported-access decision, data meaning, access rule, and explicit reopen trigger. No provider code starts without a later source-specific child. |
| EFD-007, evidence | No source result is returned or cached. A later admitted source must retain its identity, source link, retrieval time, cache state, and group size or explicit unknown state. |
| EFD-010, testability | This is a docs-only decision. There is no provider contract to characterize and no live source call in normal tests. |
| EFD-013, documentation | The umbrella packet, fixture inventory, and potential-features registry link to this record and state that no new source is available. |

The Phase 5 exit criteria are met: every researched candidate has an explicit
decision, and no undocumented endpoint, browser automation, or cache was
introduced.

## Future Review

Reopen this record only when one of these facts changes:

1. The owner authorizes a request to Reddit and Reddit explicitly approves the
   proposed read-only workflow.
2. A source publishes an API and terms that permit a bounded deck group with
   complete card entries, a documented ordering/filter, and clear population
   limits.
3. Playgroup publishes the missing complete-deck and deck-group contract.

Before any source code begins, create one narrow provider child. It must define
the source's response shape, access approval, cache lifetime, pacing, failure
states, attribution, source link, output cap, offline fixtures, and opt-in MCP
toolset. It must not add a generic web-search or provider framework.

## Validation Record

| Date | Check | Result |
| --- | --- | --- |
| 2026-09-08 | Current provider-policy review | Completed against the linked official Reddit, EDHREC, Moxfield, and Archidekt pages. |
| 2026-09-08 | Playgroup contract review | Completed against the pinned Public API 1.0.0 fixture. |
| 2026-09-08 | Runtime/source-data check | No API call, sign-in, cache, fixture, configuration, or production code was added. |
