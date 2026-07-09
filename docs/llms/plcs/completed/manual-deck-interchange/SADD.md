# Manual Deck Interchange Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-06
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

Interchange lives in `MtgMcp.Decks` behind focused parser/formatter helpers.
App owns four MCP wrappers. One format catalog maps stable format IDs and their
supported import/export directions to parsers,
formatters, preservation capabilities, size limits, and instructions; it is a
closed built-in table, not a plugin or arbitrary code loader.

### Import flow

1. Validate format and byte bound.
2. Parse without network access into a proposed canonical deck.
3. Preserve source lines/JSON paths in bounded diagnostics.
4. Compute SHA-256 over the format ID and canonical proposal; parsing options
   that affect content are already represented in that proposal.
5. Return preview and fingerprint.
6. On create, reparse supplied content, verify fingerprint, and call the local
   deck create transaction.

`deck_import_create` accepts `allowPartial=false`. A preview whose completeness
is `partial` is rejected unless the caller explicitly sends `allowPartial=true`
with that preview's fingerprint. The opt-in never suppresses diagnostics.

No import merges with an existing deck. The caller can compare the preview and
issue explicit deck mutations.

### Export bundle

`DeckExportBundle` contains format/version, deck/revision, deterministic
generated UTC derived from the stored revision timestamp,
ordered `ExportArtifact` records, and `FieldPreservation` rows. Each artifact
has logical name, media type, UTF-8 content, SHA-256, and purpose. No artifact
contains an absolute local path.

### Formats

| Format ID | Primary artifact | Metadata behavior |
| --- | --- | --- |
| `mtg-mcp-json-v1` | `deck.mtg-mcp.json` | Lossless document tagged `mtg-mcp.deck/v1`; stable IDs and bindings included, secrets/provider payloads excluded. |
| `generic-text-v1` | `deck.txt` | Section headings plus quantity/name/printing; manifest reports losses. |
| `archidekt-text-v1` | `deck.archidekt.txt` | Accepted `1 Name (SET) collector` and one backtick primary category; zones, distinct same-print finishes, excluded entries, and secondary assignments remain in CSV/native companions. |
| `moxfield-bulk-edit-v1` | `deck.moxfield.txt` | Accepted set/collector, `*F*`/`*E*`, and appended `#Local Tag` syntax; zones and excluded entries remain in the native companion. |

The exact bundles are:

| Format | Ordered required artifacts |
| --- | --- |
| Native | `deck.mtg-mcp.json`, `preservation.json` |
| Generic | `deck.txt`, `deck.mtg-mcp.json`, `preservation.json` |
| Archidekt | `deck.archidekt.txt`, `category-assignments.csv`, `deck.mtg-mcp.json`, `preservation.json`, `README.txt` |
| Moxfield | `deck.moxfield.txt`, `category-assignments.csv`, `deck.mtg-mcp.json`, `preservation.json`, `README.txt` |

Moxfield global tags use `#!Tag Name` only when
`useGlobalMoxfieldTags=true` is explicitly requested. Dated manual acceptance
verified the default local-tag form, exact printings, and finish markers.
Archidekt acceptance verified one primary category and showed that same-print
rows can merge across zones and finishes; secondary categories and all zones
therefore remain `companion-only`. `README.txt` contains target-specific
manual instructions and limitation warnings.

Preview output is capped at 200 diagnostics, 512 Unicode characters per
diagnostic, and includes `omittedDiagnosticCount`. Export is capped at 16
artifacts and 20 MiB total UTF-8 content; bound failures return unsupported-size
without a partial bundle.

### Toolset and north-star design

All four tools are assigned to the default-enabled `decks` toolset. The
completed capability-toolset child implements startup selection and tests that
it may hide the family but cannot authorize `deck_import_create`, which retains
its local-write guard. One catalog tool returns direction flags for
each stable format because separate import/export discovery returned the same
rows and added no information. The acceptance workflow covers catalog,
preview, guarded creation, and deterministic export while preserving unresolved
and unsupported states for the client LLM.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| One universal text format | Rejected; hides provider-specific loss. |
| Write files directly | Rejected; MCP need not receive arbitrary filesystem authority. |
| Resolve unknown cards during import | Rejected; later resolution belongs to the explicit Scryfall corpus/evidence workflow. |
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
stable cutover so old UI evidence is never silently treated as current. The
2026-07-03 web research is a grammar-design input only and does not count as
manual acceptance.

Current official-client tests assert the single catalog schema, exact four-tool
surface, mode visibility, direct write guard, and full dummy Commander workflow
required by XCHG-018. The completed `mcp-capability-toolsets` child verifies
profile filtering against both the source build and installed package.
