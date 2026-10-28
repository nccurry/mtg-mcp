# MCP Output Control

## Current: card response formats and the result size limit

The Scryfall card tools `scryfall_search`, `scryfall_card_get`,
`scryfall_card_collection`, and `scryfall_cards_by_tag` take `responseFormat`:

| Value | Result |
| --- | --- |
| `standard` (default) | Every card field this server returns. New fields are added, never removed. |
| `concise` | A small set of fields for comparing many cards. `fields` picks which; `scryfallId`, `oracleId`, and `name` are always included. |
| `full` | `standard` plus the raw provider objects. Pages are limited to 25 rows. |

`deck_get` with `include` returns concise card fields and oracle tag slugs for a
page of entries, using the same field names.

Results that can grow with the caller's data stay under 40,000 UTF-8 bytes of
structured result. The text block of a tool result repeats the structured JSON,
and a client passes only one of the two to the model. A `concise` page, and a
`deck_get` page with `include`, keeps the leading items that fit and then sets
`truncated: true`, a `narrow` hint, and a `nextCursor` that continues from the
first item left out. The `standard` format is reported, not truncated: a page of
25 rows can pass 40,000 bytes, so use `concise` or a smaller `pageSize`.

In `concise`, `tags` and `cheapestPriceUsd` (lowest USD price among English
printings) come from installed card data. When it is not installed they are
`null` and `limits` says why. The result also names the format it used in
`responseFormat`.

## Historical: `detailLevel`

> Historical reference: these removed legacy short-output aliases are not a
> rewrite compatibility requirement. Each approved child PLC defines its output
> limits and schema; stable `0.9.0` does not carry `compact` or workspace
> compatibility aliases just to preserve the old API.

Tools that offered `detailLevel` used one shared vocabulary:

| Value | Use |
| --- | --- |
| `summary` | Small bounded response for routine agent loops. |
| `normal` | Bounded response with supporting evidence or compact rows. |
| `full` | Raw or full-fidelity model payload. |

The released `0.8.x` server accepts legacy `compact` inputs as `summary`, and
`includeWorkspace=true` as a compatibility shortcut for `detailLevel=full`.
An older plan called this a `0.10.0` deprecation window; the clean-break `0.9.0`
program supersedes that forward-looking version claim. Maintenance of the
released legacy line may keep the aliases, but rewrite code does not reproduce
them.
