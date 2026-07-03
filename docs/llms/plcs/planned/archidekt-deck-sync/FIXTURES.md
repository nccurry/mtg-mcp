# Archidekt Essentials And Synchronization Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Scenario | Expected result |
| --- | --- | --- |
| ARCH-FIX-001 | Anonymous public deck | Complete mapped remote snapshot or explicit unavailable. |
| ARCH-FIX-002 | Authenticated list/private deck | Redacted auth and exact list/get mapping. |
| ARCH-FIX-003 | Alternate card/category payload shapes | Canonical equivalent fingerprint. |
| ARCH-FIX-004 | Exact set/collector/finish/language variants | Printing identity preserved or explicitly unknown. |
| ARCH-FIX-005 | Local-only change since baseline | Push preview only. |
| ARCH-FIX-006 | Remote-only change since baseline | Pull preview only. |
| ARCH-FIX-007 | Same path changed locally and remotely | Conflict; no apply operations. |
| ARCH-FIX-008 | Unrelated local/remote changes | Both visible; no automatic merge. |
| ARCH-FIX-009 | Stale local revision or remote fingerprint | Apply refuses with zero writes. |
| ARCH-FIX-010 | Failure after two remote operations | Applied/unknown/not-attempted statuses; baseline unchanged. |
| ARCH-FIX-011 | Create private shell | Remote ID bound locally; contents remain unpushed. |
| ARCH-FIX-012 | Delete contract missing/drifted | Structured unsupported, no emulation, and child/cutover gate failed. |
| ARCH-FIX-013 | Existing binding has no baseline | `baseline_missing` conflict; no remote or local write. |
| ARCH-FIX-014 | Baseline checksum is corrupt/stale | Unavailable/conflict with evidence; no guessed direction or write. |
| ARCH-FIX-015 | Previously bound remote deck returns 404 | `remote_deleted`; local deck and binding remain unchanged. |
| ARCH-FIX-016 | Predicted primitive plan requires 151 requests | `request_limit_exceeded` before the first mutation. |
| ARCH-FIX-017 | Bulk fixture passes but live equivalence proof is absent | Bulk path remains disabled; primitive plan is used or cap refusal returned. |
| ARCH-FIX-018 | Bulk and primitive plans over same throwaway content | Final fingerprint and failure classification match before bulk can be enabled. |

## MCP Surface Matrix

| Tool | `read-only` | `local` | `remote` |
| --- | --- | --- | --- |
| `archidekt_auth_status`, `archidekt_deck_list`, `archidekt_deck_get`, `archidekt_sync_diff`, `archidekt_pull_preview`, `archidekt_push_preview` | Visible | Visible | Visible |
| `archidekt_pull_apply` | Hidden | Visible | Visible |
| `archidekt_deck_create`, `archidekt_deck_delete`, `archidekt_push_apply` | Hidden | Hidden | Visible |

## Provider Safety Matrix

- One request at a time and at least one second between starts.
- Maximum 150 requests per tool call.
- One login retry after 401.
- No retry after 403/429 or ambiguous mutation failure.
- Secrets and credential paths absent from all recorded fixtures.

## Requirement Traceability

| Requirements | Fixtures/checks |
| --- | --- |
| ARCH-001 | Project-reference and provider-DTO architecture tests. |
| ARCH-002 | ARCH-FIX-002 plus auth, error, log, and configuration redaction tests. |
| ARCH-003, ARCH-004 | ARCH-FIX-001 through ARCH-FIX-004 and fingerprint snapshots. |
| ARCH-005, ARCH-009 | ARCH-FIX-005 through ARCH-FIX-008 three-way diff matrix. |
| ARCH-006, ARCH-007 | ARCH-FIX-009 plus temporary-database transactional pull tests. |
| ARCH-008, ARCH-014 | ARCH-FIX-010 and captured stable request/status sequence. |
| ARCH-010 | ARCH-FIX-011, private-default assertion, and remote-mode guard. |
| ARCH-011 | ARCH-FIX-012, verified delete fixture, and live residual-state gate. |
| ARCH-012, ARCH-013 | Fake-clock, cap, block, and ambiguous-write request-count tests. |
| ARCH-015 | MCP surface matrix and zero-write mode tests. |
| ARCH-016 | Sanitized fixture manifest and contract-drift test. |
| ARCH-017 | Live-test discovery, opt-in, unique-name, and cleanup guards. |
| ARCH-018 | ARCH-FIX-017, ARCH-FIX-018, and bulk-disablement architecture tests. |
| ARCH-019 | ARCH-FIX-013 through ARCH-FIX-015 and combined fake-HTTP/temporary-DB tests. |

## Live Acceptance

The `Category=Live` test requires an explicit opt-in flag and credentials. It
uses a unique private deck name, records only redacted IDs/checksums, verifies
create/push/get/pull, and deletes in `finally`. If deletion cannot be verified,
the test fails acceptance and records redacted cleanup evidence. A live run that
leaves a remote deck does not satisfy the gate.
