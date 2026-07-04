# Full Scryfall Corpus Acceptance

## Acceptance Record

- Observed UTC: `2026-07-04T22:23:13Z`
- Last unchanged-metadata verification: `2026-07-04T22:33:19Z`
- Package version: `0.9.0-preview.1`
- Result: Passed
- Storage: Retained in the normal versioned application-data root; the path is
  intentionally not recorded.
- Activation: Passed atomically after all four datasets imported and validated.
- Second-process reuse: Passed without provider acquisition.
- Card and direct community-tag join: Passed.
- Unchanged-metadata rerun: Passed in approximately four seconds without
  replacing the active corpus.
- Rollback: Not applicable because this was the first retained generation;
  guarded current/previous rollback remains covered by offline fixtures and
  will be exercised when a second real generation exists.

## Dataset Evidence

| Dataset | Provider updated UTC | Rows | Source bytes | SHA-256 |
| --- | --- | ---: | ---: | --- |
| `all_cards` | `2026-07-04T21:33:35.170Z` | 531,725 | 2,555,905,443 | `0e99356afd9ee404fdc3502ba733babaec775507ce8ee66a3373f049cd6de9fe` |
| `art_tags` | `2026-07-04T21:01:20.277Z` | 11,314 | 40,279,392 | `113d758efa7d538d44a24cd2dff329751995d40265789ea3f51f0ad95b065c1c` |
| `oracle_tags` | `2026-07-04T21:00:35.746Z` | 4,491 | 17,993,361 | `5d69c3f492dac376d03d8edff73eb6ca2973b8b5cc7df74167a5fe16141b0a3c` |
| `rulings` | `2026-07-04T21:00:36.956Z` | 76,801 | 25,366,027 | `9f267c4f38246b7010d630b41b7063d7752d0c5d4e58ebad5668be77048a7a3a` |

## Findings Resolved During Acceptance

- Two official ruling records contain a present empty `comment`. The importer
  now preserves that source value instead of treating it as a missing field.
- The original combined tag-assignment validation join was unsuitable for the
  production corpus. Equivalent Oracle, root-art, and face-art `NOT EXISTS`
  checks now use the authored indexes and retain the same dangling-identity
  rejection behavior.
- Failed and interrupted attempts never activated partial data. Abandoned
  staging rows and their expired test lease were removed before the successful
  run.

No raw provider objects, transient download URLs, credentials, local paths, or
local generation identifiers are retained in this record.
