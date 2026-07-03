# Manual Deck Interchange Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

Interchange lives in `MtgMcp.Decks` behind pure parser/formatter interfaces.
App owns five MCP wrappers. A format catalog maps stable format IDs to parsers,
formatters, preservation capabilities, size limits, and instructions; it is a
closed built-in table, not a plugin or arbitrary code loader.

### Import flow

1. Validate format and byte bound.
2. Parse without network access into a proposed canonical deck.
3. Preserve source lines/JSON paths in bounded diagnostics.
4. Compute SHA-256 over format ID, options, and canonical proposal.
5. Return preview and fingerprint.
6. On create, reparse supplied content, verify fingerprint, and call the local
   deck create transaction.

`deck_import_create` accepts `allowPartial=false`. A preview whose completeness
is `partial` is rejected unless the caller explicitly sends `allowPartial=true`
with that preview's fingerprint. The opt-in never suppresses diagnostics.

No import merges with an existing deck. The caller can compare the preview and
issue explicit deck mutations.

### Export bundle

`DeckExportBundle` contains format/version, deck/revision, generated UTC,
ordered `ExportArtifact` records, and `FieldPreservation` rows. Each artifact
has logical name, media type, UTF-8 content, SHA-256, and purpose. No artifact
contains an absolute local path.

### Formats

| Format ID | Primary artifact | Metadata behavior |
| --- | --- | --- |
| `mtg-mcp-json-v1` | `deck.mtg-mcp.json` | Lossless document tagged `mtg-mcp.deck/v1`; stable IDs and bindings included, secrets/provider payloads excluded. |
| `generic-text-v1` | `deck.txt` | Section headings plus quantity/name/printing; manifest reports losses. |
| `archidekt-text-v1` | `deck.archidekt.txt` | `1x Name (SET) collector` and verified backtick primary category; secondary assignments in CSV/native companions. |
| `moxfield-bulk-edit-v1` | `deck.moxfield.txt` | Board sections, printing hints, and appended `#Local Tag`; native companion preserves every local field. |

The exact bundles are:

| Format | Ordered required artifacts |
| --- | --- |
| Native | `deck.mtg-mcp.json`, `preservation.json` |
| Generic | `deck.txt`, `deck.mtg-mcp.json`, `preservation.json` |
| Archidekt | `deck.archidekt.txt`, `category-assignments.csv`, `deck.mtg-mcp.json`, `preservation.json`, `README.txt` |
| Moxfield | `deck.moxfield.txt`, `category-assignments.csv`, `deck.mtg-mcp.json`, `preservation.json`, `README.txt` |

Moxfield global tags use `#!Tag Name` only when `tagScope=global` is explicitly
requested. Archidekt secondary categories remain `companion-only` until current
UI acceptance proves a supported multi-category import syntax. `README.txt`
contains target-specific manual instructions and limitation warnings.

Preview output is capped at 200 diagnostics, 512 Unicode characters per
diagnostic, and includes `omittedDiagnosticCount`. Export is capped at 16
artifacts and 20 MiB total UTF-8 content; bound failures return unsupported-size
without a partial bundle.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| One universal text format | Rejected; hides provider-specific loss. |
| Write files directly | Rejected; MCP need not receive arbitrary filesystem authority. |
| Resolve unknown cards during import | Rejected; acquisition belongs to Scryfall snapshots. |
| Automatically merge imports | Rejected; requires hidden conflict decisions. |
| Claim tags preserved because companion exists | Rejected; report target-applied and companion-only separately. |

## Failure Modes

- Unknown format or schema version returns unsupported.
- Malformed input returns bounded diagnostics and no preview fingerprint.
- Partially parseable text returns a preview only when at least one entry is
  valid, with completeness `partial`; create requires explicit `allowPartial`.
- Fingerprint mismatch returns conflict.
- Unsupported provider metadata remains in native companion and preservation
  report, never silently dropped.

## Test Architecture

Golden fixtures cover every format, section, tag, printing, Unicode case,
duplicate entry, line error, and loss report. Property tests confirm native JSON
round trips. Manual acceptance uses disposable empty decks in the provider UI,
records the date and observed result, and performs no automated provider call.
Each manual record also stores provider, UI flow/path, artifact checksums,
notes, and a revalidation reason such as initial verification, observed drift,
or pre-cutover refresh. The checks run during implementation and again before
stable cutover so old UI evidence is never silently treated as current.
