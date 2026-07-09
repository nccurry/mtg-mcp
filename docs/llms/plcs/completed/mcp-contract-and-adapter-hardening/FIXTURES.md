# MCP Contract And Adapter Hardening Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Fixture | Purpose |
| --- | --- | --- |
| HARD-FIX-001 | Capability schema 6, no provider credentials | Provider rows are implemented but `not-configured`; no secret/path/I/O. |
| HARD-FIX-002 | Capability schema 6, configured provider credentials | Rows are `configured-unverified` and name the redacted auth tool. |
| HARD-FIX-003 | Disabled and permuted toolset selections | Projection order and credential state stay deterministic. |
| HARD-FIX-004 | Eleven batch-change schema branches | Each `kind` has only its applicable described properties. |
| HARD-FIX-005 | Invalid batch at indexes 0, 1, and 10 | Diagnostics identify index/kind/shape without values. |
| HARD-FIX-006 | Complete registered tool schema | Every root input property has a nonblank useful description. |
| HARD-FIX-007 | Printing-ID entry with matching weaker fields | Complete canonical printing identity is proposed. |
| HARD-FIX-008 | Set/collector/language entry | Exact printing resolution is proposed. |
| HARD-FIX-008A | Non-English set/collector with and without exact corpus evidence | Corpus match succeeds; corpus miss is `not-cached` and never substitutes English. |
| HARD-FIX-009 | Oracle-ID entry | Canonical name/Oracle identity only; no printing is selected. |
| HARD-FIX-010 | Exact-name entry | Exact name/Oracle identity only; fuzzy endpoint is never used. |
| HARD-FIX-011 | Strong/weak identity conflict | Row is `conflict` and blocks non-partial apply. |
| HARD-FIX-012 | Duplicate lookups in distinct deck rows | One acquisition, multiple ordered outcomes. |
| HARD-FIX-013 | 75, 76, and 150 unique misses | Provider batches are 75, 75; pacing and order are retained. |
| HARD-FIX-014 | 151 selected entries | Invalid before HTTP or persistence. |
| HARD-FIX-015 | Cache-only and read-only misses | Explicit not-cached/local-write-required with zero writes. |
| HARD-FIX-016 | Complete preview and guarded apply | One atomic revision changes only allowed identity fields. |
| HARD-FIX-017 | Incomplete preview with partial false/true | Refusal then explicit partial application. |
| HARD-FIX-018 | Tampered token/fingerprint or wrong deck | Invalid without reads beyond validation or any writes. |
| HARD-FIX-019 | Stale deck revision | Conflict and byte-identical deck. |
| HARD-FIX-020 | Deleted snapshot/generation evidence | `identity-evidence-unavailable`; no mutation. |
| HARD-FIX-021 | Existing Scryfall characterization corpus | SQL/schema/results/checksums/cursors remain unchanged after extraction. |
| HARD-FIX-022 | Existing Archidekt route and mapping fixtures | Requests, pacing, retries, fingerprints, results, and errors remain unchanged. |
| HARD-FIX-023 | Manual interchange cleanup record | Both provider artifacts accepted and disposable decks owner-confirmed deleted. |

## MCP Surface Checks

### Hardening target after implementation

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 22 | 43 | 43 |
| `all` | 47 | 69 | 82 |
| `none` | 0 | 0 | 0 |

### Final planning target after statistics and categorization

| Profile | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| `default` | 32 | 54 | 54 |
| `all` | 57 | 80 | 93 |
| `none` | 0 | 0 | 0 |

The two new rows are:

| Tool | Toolset | `read-only` | `local` | `remote` |
| --- | --- | --- | --- | --- |
| `deck_identity_reconcile_preview` | `decks` | Visible | Visible | Visible |
| `deck_identity_reconcile_apply` | `decks` | Hidden | Visible | Visible |

One resource and zero prompts remain unchanged. The remote `all` live manifest
becomes 82 methods: 80 live-capable tools and two Playgroup writes that remain
fixture-only-owner-approved.

## Acceptance Matrix

| Requirements | Fixtures/evidence |
| --- | --- |
| HARD-001–003 | HARD-FIX-001–003 plus capability unit/E2E/package snapshots |
| HARD-004–006 | HARD-FIX-004–006 plus complete schema lint |
| HARD-007–011 | HARD-FIX-007–015 plus fake-provider/store integration |
| HARD-012–015 | HARD-FIX-016–020 plus official-client and package dummy-deck workflow |
| HARD-016 | HARD-FIX-021 and the complete existing Scryfall suite |
| HARD-017 | HARD-FIX-022 and the complete existing Archidekt suite |
| HARD-018 | Architecture, abstraction-quality, code-quality, and dead-code audits |
| HARD-019 | HARD-FIX-023, lifecycle indexes, and relative-link validation |
| HARD-020–022 | Surface matrices, forbidden scans, task gates, coverage, dependency/docs audits |

## Dummy Deck Scenario

Create a temporary Commander deck with entries representing each lookup tier,
two duplicate exact names, one strong/weak conflict, and one valid missing
name. Preview all entries, verify canonical ordering and evidence, refuse the
incomplete apply by default, explicitly apply the safe subset, and confirm:

- one revision increment;
- exact allowed identity changes;
- unchanged quantities, finishes, zones, ordering, categories, and bindings;
- no fuzzy lookup;
- no remote deck mutation;
- deterministic repeat preview at the new revision.

## Provider And Audit Acceptance

Ordinary provider fixtures stay offline. After refactors, run bounded
sequential Scryfall and Archidekt reads through the existing opt-in harness.
Stop on 401, 403, or 429; add no harness retry and make no new remote mutation.
Tracked evidence contains no raw payload, credential, account identity, remote
object ID, or local path.

The child cannot complete with an unresolved audit finding or less than
90-percent line coverage in any production assembly.
