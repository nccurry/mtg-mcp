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

[focused-agent-tool-surface](../completed/focused-agent-tool-surface/README.md)
was completed and released as 0.12.0 on 2026-10-29.

[deckbuilding-agent-guidance](deckbuilding-agent-guidance/README.md) is planned:
a Claude Code plugin with deckbuilding skills, plus search results that guide
agents to oracle tags and whole-result reads.

## Tool Surface

[mcp-guidance-audit](mcp-guidance-audit/README.md) is planned: check every tool against current MCP and
vendor tool-design guidance and fix the findings. It awaits owner review and is not authorized.

## Simulation

[simulate-damage-rules](simulate-damage-rules/README.md) plans caller-stated
commander-zone, counter, attachment, attack, and damage rules for
`stats_simulate`. It awaits owner review and is not authorized.

## Retired Reference Packets

The nine legacy reference packets that used to wait here (Jasmine analysis
repair packets and product foundation follow-ups) were superseded by the
rewrite and moved to [`completed/`](../completed/README.md) on 2026-10-27.
