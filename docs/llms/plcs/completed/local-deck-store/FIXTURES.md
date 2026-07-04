# Local Deck Domain And SQLite Store Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Scenario | Expected result |
| --- | --- | --- |
| DECK-FIX-001 | Name-only Commander deck with commander/main/maybeboard zones | Exact unresolved identities and zones round-trip. |
| DECK-FIX-002 | Same Oracle card in two printings and finishes | Separate stable entries remain. |
| DECK-FIX-003 | One entry with three categories and one primary | Assignment order and primary invariant persist. |
| DECK-FIX-004 | Category rename/delete | Cards and zones remain; only assignments change. |
| DECK-FIX-005 | Two writers use the same revision | First commits; second receives conflict and changes nothing. |
| DECK-FIX-006 | Batch includes a final invalid operation | Entire transaction rolls back and revision is unchanged. |
| DECK-FIX-007 | Deck bound to Archidekt and another provider | Bindings and canonical baselines remain independent. |
| DECK-FIX-008 | Migration interrupted after backup | Original database and backup remain valid. |
| DECK-FIX-009 | Restore backup over changed database | Fingerprint guard and rollback backup protect newer data. |
| DECK-FIX-010 | Legacy workspace JSON beside `decks.db` | JSON remains untouched and is not listed. |
| DECK-FIX-011 | Backup manifest fingerprint differs from backup bytes | Restore returns corrupt/unavailable before swap; current DB remains byte-identical. |
| DECK-FIX-012 | Two otherwise identical entries have different entry IDs | Both persist independently; no uniqueness conflict or coalescing occurs. |
| DECK-FIX-013 | Same mutation through granular tool and one-operation batch | Canonical rows, revision increment, and result are equivalent. |
| DECK-FIX-014 | `deck_validate` on unknown legality/role/provider state | Structural result succeeds or reports local defects only; no provider, legality, role, or quality result appears. |

## MCP Surface Matrix

| Tools | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `deck_list`, `deck_get`, `deck_validate`, `deck_backup_list` | Visible | Visible | Visible |
| `deck_create`, `deck_update`, `deck_delete` | Hidden | Visible | Visible |
| `deck_entry_add`, `deck_entry_update`, `deck_entry_remove` | Hidden | Visible | Visible |
| `deck_category_create`, `deck_category_update`, `deck_category_delete` | Hidden | Visible | Visible |
| `deck_category_assign`, `deck_category_unassign`, `deck_apply_changes` | Hidden | Visible | Visible |
| `deck_backup_create`, `deck_backup_restore`, `deck_backup_delete` | Hidden | Visible | Visible |

## Acceptance Matrix

| Requirements | Fixtures/checks |
| --- | --- |
| DECK-001, DECK-002, DECK-003, DECK-004, DECK-005, DECK-006 | DECK-FIX-001 through DECK-FIX-004 |
| DECK-007, DECK-008, DECK-009 | DECK-FIX-005, DECK-FIX-006, canonical serialization snapshot |
| DECK-010 | DECK-FIX-007 |
| DECK-011, DECK-012, DECK-013, DECK-014, DECK-015 | DECK-FIX-008 through DECK-FIX-011 and pragma tests |
| DECK-016 | MCP surface matrix and process E2E |
| DECK-017 | DECK-FIX-014 and forbidden-dependency tests |
| DECK-018 | DECK-FIX-013 and shared-service architecture tests |

## Live Tests

None. The local deck module is fully testable with temporary files and SQLite.
