# Reference Data

This folder holds historical source snapshots and bounded fixtures. Current
code and tests, not these files, define the runtime.

Reference data does not justify adding a production feature. The completed
rewrite records explain why each snapshot was retained. In particular, local
combo patterns do not justify recommendations or a current fallback data path.

## Historical Scryfall Tagger Snapshot

`scryfall-tagger-tags-2026-05-23.json` is a historical snapshot of the mixed
Scryfall Tagger directory retrieved from `https://tagger.scryfall.com/` on
2026-05-23. The current server does not download or query that website.

The snapshot includes artwork, Oracle-card, and print tags. It is historic
research only. The current server gets tag evidence from the official Scryfall
bulk data and keeps it separate from card facts.

## Historical Local Combos

`local-combos.json` is not embedded or read by the current server. It remains
as historical reference data. Current combo evidence comes from the optional
Commander Spellbook adapter and never becomes a card recommendation.
