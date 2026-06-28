# Reference Data

This folder stores source snapshots and bounded local fixtures used for deterministic offline behavior.

## Scryfall Tagger

`scryfall-tagger-tags-2026-05-23.json` is a snapshot of the full mixed Scryfall Tagger tag directory retrieved from `https://tagger.scryfall.com/` on 2026-05-23.

The snapshot includes artwork, oracle-card, and print tags. Use `namespace = "card"` or `type = "ORACLE_CARD_TAG"` when selecting deckbuilding evidence. Artwork-only tags, print tags, visual descriptors, creature species, set cycles, and flavor labels should not drive Commander card recommendations.

## Local Combos

`local-combos.json` is a small, checked-in fallback dataset for no-catalog combo analysis. It is embedded into `MtgMcp.Core` at build time and remains clearly labeled as `local-pattern` evidence; Commander Spellbook catalog rows remain preferred when a catalog is configured and available.
