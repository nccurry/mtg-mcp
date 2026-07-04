# Manual Provider Acceptance Records

## Record Schema

Every acceptance record contains the provider, observed UTC, exact UI flow or
path, artifact SHA-256 checksums, result, notes, and revalidation reason. A
research-only or not-run record never satisfies manual acceptance.

## Current Records

### Archidekt

- Provider: Archidekt
- Observed UTC: 2026-07-04T15:32:15Z
- UI flow/path: Deck editor, Import Cards dialog; intended manual path only
- Artifact checksums: Not recorded because no authenticated manual UI import
  was performed
- Result: Not run; `archidekt-text-v1` remains experimental
- Notes: Current public staff examples corroborate exact printing hints and one
  backtick category. Automated provider access is outside this child PLC.
- Revalidation reason: Implementation-time verification

### Moxfield

- Provider: Moxfield
- Observed UTC: 2026-07-04T15:32:15Z
- UI flow/path: Deck editor, Bulk Edit; intended manual path only
- Artifact checksums: Not recorded because no authenticated manual UI import
  was performed
- Result: Not run; `moxfield-bulk-edit-v1` remains experimental
- Notes: Moxfield publishes no stable Bulk Edit grammar, and its terms exclude
  automated probing. Candidate artifacts were exercised only through offline
  parser/formatter round trips.
- Revalidation reason: Implementation-time verification

## Open Acceptance Gate

These records prove the required metadata shape and make the missing UI checks
explicit. They do not satisfy XCHG-017 or authorize either provider format to
claim compatibility. A repository owner can complete the gate by manually
importing a generated dummy-deck artifact, recording its checksum and observed
result here, and repeating the check before stable cutover.
