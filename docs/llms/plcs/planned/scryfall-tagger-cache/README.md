# Scryfall Tagger Cache PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/scryfall-tagger-cache/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines a deterministic local `tagger.db` read model and a separate
explicit acquisition workflow for Scryfall Tagger's unsupported HTML/GraphQL
contract. Cached reads never use the network. Refresh is sequential, paced at
one request per second, capped at 100 Oracle IDs, bounded across known paper
printings, skips already cached IDs by default, and stops immediately on 403 or
429.

The MCP returns actual cached community tag assignments and provenance. It
does not map them to deck categories; the calling LLM may use ordinary
`deck_category_*` tools for that choice.

## Dependencies

- [Local Deck Store](../../completed/local-deck-store/README.md)
- [Scryfall Evidence Snapshots](../scryfall-evidence-snapshots/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Keep cache reads and acquisition as separate services/tools. | Proposed | Deterministic reads must never cause unsupported traffic. |
| Request and index gameplay-tag snapshots by Oracle ID while preserving Tagger subject scope. | Proposed | Oracle tags span printings, but illustration tags belong to the queried illustration and must not be relabeled Oracle-wide. |
| Preserve direct, ancestor, tag type, returned status, subject ID, and raw association evidence. | Proposed | Consumers can distinguish what Tagger actually returned without claiming access to moderator-only states. |
| Use only printing facts from an explicit Scryfall snapshot. | Proposed | Tagger does not depend on hidden live Scryfall calls. |
| Enable refresh conservatively, with non-configurable upper safety bounds. | Proposed | Personal-use acquisition remains polite by default. |
| Trip a process circuit breaker on 403/429. | Proposed | The server must not retry through provider refusal. |

## Policy Notice

On 2026-07-03, Tagger's current `robots.txt` allowed the general user agent but
also published `Content-Signal: search=yes,ai-train=no,use=reference` and
disallowed named AI crawlers including GPTBot and ClaudeBot. Those directives
are not API endorsement. The planned client uses an honest `mtg-mcp` user-agent,
does not train a model, and returns attributable reference evidence, but
acquisition remains unsupported and subject to Scryfall's prohibition on
placing undue burden through automated means. A contract or policy change
disables refresh until review; existing cache reads remain available.

## 2026-07-03 Viability Research

The current public behavior supports a bounded read-only acquisition path:

- `GET https://tagger.scryfall.com/` returned `200` with a
  `_scryfall_tagger_session` cookie and `csrf-token` metadata.
- The current first-party JavaScript posts `FetchCard` to same-origin
  `/graphql` with `X-CSRF-Token`; the operation reads `cardBySet`, public
  taggings, tag definitions, and ancestor tags.
- One same-session lookup for Lightning Bolt (`m10`/`146`) returned `200` with
  17 direct public taggings: 6 `ORACLE_CARD_TAG` and 11 `ILLUSTRATION_TAG`, plus
  18 ancestor associations and no GraphQL errors.
- Those direct taggings used two distinct subject IDs, confirming that
  illustration assignments cannot safely be promoted to Oracle-wide facts.
- An honest `mtg-mcp/0.9` user-agent received `200`; browser impersonation is
  neither required nor allowed by this plan.

Technical conclusion: user-invoked, sequential per-card cache fill is viable
today. Bulk/background crawling is not justified, the contract is unsupported,
and a provider refusal must fail closed.

## Provider Risk Acceptance

- Risk: refresh uses unsupported Tagger HTML, CSRF/session behavior, and an
  undocumented GraphQL operation rather than an endorsed public API.
- Required decision: repository owner accepts the permission, stability, and
  maintenance risk despite conservative pacing, bounds, and fail-closed drift.
- Status: Research complete; owner implementation decision not yet accepted
- Accepted by: Not accepted
- Acceptance date/revision: Not accepted

Cache-only reads do not require this exception. The research supports a bounded
implementation, but acquisition implementation and packet approval remain
blocked until the owner explicitly accepts the updated policy record.

## Current-State Disposition

The current curated tag catalog and Scryfall `otag:` searches do not provide
complete per-card Tagger assignments. They are reference evidence and possible
fixture inputs only; this child does not reuse their classification abstraction.
There is currently no production HTML/CSRF/GraphQL acquisition path to preserve.

## Guardrail Conformance

This child returns exact cached community assignments and explicit acquisition
status. It does not infer semantic roles, map tags to deck categories, refresh
implicitly, or make a deckbuilding decision. It owns only `tagger.db`, keeps
unsupported provider transport out of Core, and uses the program operation modes.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No
