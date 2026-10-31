# Provider Evidence Workflows Fixtures

## Provider Fixture Matrix

| ID | Scenario | Expected state |
| --- | --- | --- |
| PEW-SCRYFALL-001 | Complete card payload | Source fact with retrieval and cache metadata |
| PEW-TAGGER-001 | Tagger classification present | Attributed source evidence, not oracle fact |
| PEW-EDHREC-001 | Aggregate with deck count | Separate EDHREC population and sample fields |
| PEW-TOURNAMENT-001 | Tournament aggregate | Format/event population and retrieval context |
| PEW-PLAYGROUP-001 | Authorized small sample | Raw observations with permission sensitivity and sample size |
| PEW-PLAYGROUP-002 | Local-meta scoring over observations | Separate heuristic model ID and inputs |
| PEW-STALE-001 | Expired cache and provider unavailable | Stale or unavailable state according to source policy |
| PEW-PERMISSION-001 | Missing credentials | Permission-restricted status without secret material |
| PEW-PARTIAL-001 | One source fails | Successful sources remain; failed source is explicit |
| PEW-ARCHIDEKT-001 | Plan-mode mutation request | Blocked before HTTP |
| PEW-ARCHIDEKT-002 | Apply mutation with checkpoint | Fake HTTP records checkpoint-aware ordered calls |

## Acceptance Rules

- Retrieval timestamps are fixture-controlled.
- Cache status and freshness are independently asserted.
- Missing sample/population fields remain unknown, not zero.
- Source rows remain separated in serialized output.
- Any blended score is labeled heuristic and carries model/version metadata.
- Errors and recorded requests contain no credentials.

## Update Rules

- Record the documented provider contract or captured fixture provenance.
- Sanitize all identifiers and credentials before committing payloads.
- Do not refresh fixtures from live services in normal tests.
- Review schema drift as an adapter change, not a Core DTO change by default.
- Never update an Archidekt write fixture against a real deck.
