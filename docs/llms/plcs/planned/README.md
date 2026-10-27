# Planned PLCs

Store PLC packets here while requirements, design, scope, and validation are
still being shaped.

Agents may update packets in this folder, but should not treat a planned PLC as
permission to start implementation unless the user asks for implementation.
Ideas intentionally deferred beyond the rewrite live in the
[potential-features registry](../../../potential-features.md), not as implied
implementation work in these packets.

Reference-only packets may retain names and terms from the old server. In those
records, `corpus` means installed full Scryfall card data; it is not a separate
current feature.

## Evidence-First Rewrite Children

No rewrite child remains planned. Children 1 through 11 are complete. The
[stabilization child](../completed/rewrite-stabilization-cutover/README.md)
is complete, and the
[umbrella planning program](../completed/evidence-first-mcp-rewrite-program/README.md)
is complete.

## Agent Surface

| Packet | Status | Summary |
| --- | --- | --- |
| [focused-agent-tool-surface](focused-agent-tool-surface/README.md) | Planned, reviewed, awaiting implementation authorization | Smaller default toolsets with deck writes kept, one opt-in for the Archidekt round trip, tag search and deck tag view, `card_lists_compare` and `card_list_query` for list comparison and grouped deck analysis, Commander rule facts, enum schemas, opt-in concise card results, caller-ruled `stats_simulate` with colored payment, `stats_castability`, pairing and companion facts, Spellbook bracket facts, Playgroup game statistics, Archidekt deduplication, protocol fixes (`isError`, instructions, size limit), and three bug fixes from real use. Existing Playgroup tools frozen. |

## Retired Reference Packets

The nine legacy reference packets that used to wait here (Jasmine analysis
repair packets and product foundation follow-ups) were superseded by the
rewrite and moved to [`completed/`](../completed/README.md) on 2026-10-27.
