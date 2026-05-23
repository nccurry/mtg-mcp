# Reference Data

This folder stores source snapshots that are useful for later corpus-provider work but are not loaded by mtg-mcp at runtime.

## Scryfall Tagger

`scryfall-tagger-tags-2026-05-23.json` is a snapshot of the full mixed Scryfall Tagger tag directory retrieved from `https://tagger.scryfall.com/` on 2026-05-23.

The snapshot includes artwork, oracle-card, and print tags. Use `namespace = "card"` or `type = "ORACLE_CARD_TAG"` when selecting deckbuilding evidence. Artwork-only tags, print tags, visual descriptors, creature species, set cycles, and flavor labels should not drive Commander card recommendations.
