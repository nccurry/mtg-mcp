# Manual Deck Interchange PLC Packet

## Lifecycle

- Status: Planned
- Folder: `docs/llms/plcs/planned/manual-deck-interchange/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-03
- Current phase: draft review

## Summary

This packet defines deterministic, offline deck import previews and export
artifact bundles. Catalog format `mtg-mcp-json-v1`, whose document schema tag is
`mtg-mcp.deck/v1`, is the only lossless format.
Archidekt and Moxfield bundles preserve every local field in a companion
manifest while clearly distinguishing metadata their current text importers
can apply from metadata that requires manual follow-up.

## Dependencies

- [Local Deck Store](../local-deck-store/README.md)
- [Rewrite program](../../in-progress/evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Return in-memory artifact bundles, not write arbitrary paths. | Proposed | Avoids filesystem authority and makes output inspectable. |
| Import creates a new local deck only. | Proposed | Existing-deck merging remains explicit `deck_*` mutation work. |
| Require preview fingerprint on import create. | Proposed | Applied data exactly matches reviewed parsing output. |
| Include a lossless native manifest in every provider bundle. | Proposed | Provider text limitations never discard the only copy of metadata. |
| Use Moxfield Bulk Edit local-tag syntax and verify it manually. | Proposed | Ordinary exports do not include custom tags. |
| Preserve Archidekt secondary categories in companion CSV/JSON. | Proposed | Text import behavior for multiple categories is not sufficiently documented to claim full automatic round trip. |

## Public Surface

- `deck_import_formats`
- `deck_import_preview`
- `deck_import_create`
- `deck_export_formats`
- `deck_export_bundle`

## Guardrail Conformance

Parsing and formatting are deterministic transformations. The module does not
choose categories, resolve missing cards, contact Moxfield/Archidekt, or claim
that a provider consumed companion metadata.

## Planning Approval

- Status: Draft
- Reviewed by: Not reviewed
- Review date: Not reviewed
- Reviewed revision: Not reviewed
- Implementation authorized: No

## Completion Notes

Remote Archidekt synchronization is owned by `archidekt-deck-sync`; Moxfield
network automation remains excluded.
