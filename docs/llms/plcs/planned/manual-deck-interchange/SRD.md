# Manual Deck Interchange Software Requirements Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SADD: [SADD.md](SADD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Scope

In scope are native JSON, generic text, Archidekt manual import text, Moxfield
Bulk Edit text, parse previews, artifact manifests, warnings, and local deck
creation. Network calls, existing-deck merge policy, remote mutation, and card
identity guessing are out of scope.

## Requirements

| ID | Priority | Requirement | Acceptance criteria |
| --- | --- | --- | --- |
| XCHG-001 | Must | Format-catalog ID `mtg-mcp-json-v1` shall emit native JSON carrying schema tag `mtg-mcp.deck/v1` and round-trip every local deck field and stable ID. | Catalog/schema snapshots and golden equality fixtures pass. |
| XCHG-002 | Must | Every import shall first return a normalized preview, line/path diagnostics, unresolved identities, and a content fingerprint. | Preview fixtures are deterministic and bounded. |
| XCHG-003 | Must | `deck_import_create` shall require the preview fingerprint and create a new deck atomically. | Modified content/fingerprint is rejected; no partial deck exists. |
| XCHG-004 | Must | Import shall never query a provider or invent a Scryfall identity. | Architecture and fake-network tests prove no HTTP path. |
| XCHG-005 | Must | Export shall return an ordered bundle of named artifacts, media types, content, checksums, and a preservation report. | Bundle schema snapshots and checksum tests pass. |
| XCHG-006 | Must | Generic text shall preserve quantity, name, zone headings, and supported printing hints while reporting all omitted fields. | Golden text and loss-report fixtures pass. |
| XCHG-007 | Must | Archidekt text shall use verified quantity/name/printing syntax and backtick primary-category syntax where supported. | Manual UI acceptance imports quantities, printings, zones/primary categories. |
| XCHG-008 | Must | Archidekt bundles shall include canonical category-assignment CSV and native JSON for secondary or unsupported metadata. | Every local category appears in at least one lossless companion artifact. |
| XCHG-009 | Must | Moxfield Bulk Edit text shall append local tags using `#Tag Name` syntax and preserve multiple tags deterministically. | Golden syntax plus manual UI acceptance confirms current behavior. |
| XCHG-010 | Must | Global Moxfield tag syntax shall not be emitted unless the caller explicitly selects global tags. | Default artifact contains no `#!` tag. |
| XCHG-011 | Must | Provider artifacts shall never claim lossless or successful import without a verified target round trip. | Preservation report uses `preserved`, `companion-only`, and `unsupported` states. |
| XCHG-012 | Must | Inputs shall be limited to 5 MiB and 10,000 parsed entries with cancellation. | Boundary and cancellation tests pass. |
| XCHG-013 | Must | Parsing shall preserve Unicode and report one-based source lines without echoing secrets or excessive content. | Unicode and sanitization tests pass. |
| XCHG-014 | Must | Reads/previews shall be visible in all modes; local creation shall require `local` or `remote`. | Surface tests pass. |
| XCHG-015 | Must | `deck_import_create` shall expose `allowPartial`, default it to `false`, and reject a partial preview unless the caller explicitly sets it to `true` with the matching preview fingerprint. | Default/refusal/explicit-opt-in fixtures pass with no partial deck on refusal. |
| XCHG-016 | Must | A preview shall return at most 200 diagnostics of at most 512 Unicode characters plus an omitted count; an export shall contain at most 16 artifacts and 20 MiB total UTF-8 content. | Exact-boundary and overflow tests return deterministic bounded results. |
| XCHG-017 | Must | Manual provider acceptance records shall include provider, observed UTC, UI flow/path, artifact checksums, result, notes, and revalidation reason; provider artifacts shall be reverified during implementation and before stable cutover. | Fixture metadata schema and dated Archidekt/Moxfield records pass review. |

## Quality Attributes

| Attribute | Measure |
| --- | --- |
| Fidelity | Native JSON exact round trip; provider loss manifest complete. |
| Determinism | Same deck/options produce identical ordered contents and checksums. |
| Safety | No network or arbitrary filesystem writes. |
| Usability | Every artifact includes concise target instructions and warnings. |
| Boundedness | Input, entry count, diagnostics, and bundle size limits are tested. |

## Traceability

| Requirements | Validation |
| --- | --- |
| XCHG-001 through XCHG-005 | Native/preview/bundle golden tests |
| XCHG-006 through XCHG-011 | Provider golden fixtures and manual acceptance |
| XCHG-012, XCHG-013 | Boundary, Unicode, cancellation, sanitization tests |
| XCHG-014 | MCP surface and E2E tests |
| XCHG-015 | Partial-preview create schema and refusal/opt-in tests |
| XCHG-016 | Diagnostic/artifact boundary tests |
| XCHG-017 | Manual acceptance metadata and cutover checklist |

## Definition Of Done

- [ ] Native JSON round trips exactly.
- [ ] Provider preservation reports are complete and honest.
- [ ] Manual Archidekt/Moxfield checks are recorded with date and UI path.
- [ ] No network adapter is introduced.
