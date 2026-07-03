# Reference Data

This folder stores source snapshots and bounded local fixtures used for deterministic offline behavior.

Reference data does not authorize a production capability. The rewrite audit
and active child PLC decide whether each fixture is retained, transformed, or
used only as historical test evidence. In particular, local combo patterns and
Commander Spellbook preference do not imply a stable `0.9.0` combo adapter or
recommendation surface.

## Scryfall Tagger

`scryfall-tagger-tags-2026-05-23.json` is a snapshot of the full mixed Scryfall Tagger tag directory retrieved from `https://tagger.scryfall.com/` on 2026-05-23.

The snapshot includes artwork, oracle-card, and print tags. Use `namespace = "card"` or `type = "ORACLE_CARD_TAG"` when selecting deckbuilding evidence. Artwork-only tags, print tags, visual descriptors, creature species, set cycles, and flavor labels should not drive Commander card recommendations.

## Local Combos

`local-combos.json` is a small, checked-in fallback dataset for no-catalog combo analysis. It is embedded into `MtgMcp.Core` at build time and remains clearly labeled as `local-pattern` evidence; Commander Spellbook catalog rows remain preferred when a catalog is configured and available.
