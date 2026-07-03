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

## 2026-07-03 Syntax Research

Archidekt staff examples currently support exact-printing text in the form
`1 Card Name (SET) collector` and a single custom category in backticks, as in:

```text
1 Sol Ring `Maybeboard`
```

Archidekt also documents that
Ctrl+Shift+C copies its full syntax. The canonical planned export therefore
uses `quantity name (set) collector` followed by one backtick primary category;
secondary categories remain companion-only until a disposable-deck UI check
proves more.

Moxfield does not publish an official Bulk Edit grammar. Current user-facing
evidence corroborates `#Deck Tag` and `#!Global Tag`, and Moxfield's own
feedback site confirms that Bulk Edit is used for deck/global tag workflows,
but this is not enough to claim a supported contract. Public examples agree on
quantity, name, set, collector number, and `*F*`/`*E*` finish markers but
conflict on exact token order. Because
[Moxfield's terms](https://moxfield.com/help/terms) prohibit automated access,
the implementation must verify the candidate grammar manually in a disposable
deck. No Moxfield formatter is considered accepted until that record exists.

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
