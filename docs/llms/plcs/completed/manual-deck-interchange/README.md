# Manual Deck Interchange PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/manual-deck-interchange/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-06
- Current phase: Completed

## Summary

This packet defines deterministic, offline deck import previews and export
artifact bundles. Catalog format `mtg-mcp-json-v1`, whose document schema tag is
`mtg-mcp.deck/v1`, is the only lossless format.
Archidekt and Moxfield bundles preserve every local field in a companion
manifest while clearly distinguishing metadata their current text importers
can apply from metadata that requires manual follow-up.

## Dependencies

- [Local Deck Store](../../completed/local-deck-store/README.md)
- [MCP Capability Toolsets](../../completed/mcp-capability-toolsets/README.md)
- [Rewrite program](../evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Return in-memory artifact bundles, not write arbitrary paths. | Accepted | Avoids filesystem authority and makes output inspectable. |
| Import creates a new local deck only. | Accepted | Existing-deck merging remains explicit `deck_*` mutation work. |
| Require preview fingerprint on import create. | Accepted | Applied data exactly matches reviewed parsing output. |
| Include a lossless native manifest in every provider bundle. | Accepted | Provider text limitations never discard the only copy of metadata. |
| Use Moxfield Bulk Edit local-tag syntax and verify it manually. | Accepted | Ordinary exports do not include custom tags. |
| Preserve Archidekt secondary categories in companion CSV/JSON. | Accepted | Text import behavior for multiple categories is not sufficiently documented to claim full automatic round trip. |

## 2026-07-03 Syntax Research

Archidekt staff examples currently support exact-printing text in the form
`1 Card Name (SET) collector` and a single custom category in backticks, as in:

```text
1 Sol Ring `Maybeboard`
```

Archidekt also documents that
Ctrl+Shift+C copies its full syntax. The canonical planned export therefore
uses `quantity name (set) collector` followed by one backtick primary category;
secondary categories remain companion-only because the disposable-deck UI
check did not preserve them.

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

- `deck_interchange_formats`
- `deck_import_preview`
- `deck_import_create`
- `deck_export_bundle`

## Guardrail Conformance

Parsing and formatting are deterministic transformations. The module does not
choose categories, resolve missing cards, contact Moxfield/Archidekt, or claim
that a provider consumed companion metadata.

## Toolset And North-Star Acceptance

- Toolset assignment: `decks`, enabled by default through the completed,
  separately reviewed capability-toolset child.
- Surface rule: one `deck_interchange_formats` catalog reports both supported
  directions; import preview/create and export bundle remain separate because
  their inputs, authority, and outputs differ.
- User question answered: what can be imported or exported manually, what
  exact local deck would an import create, and which metadata would each
  artifact preserve?
- Evidence type: parser-derived classifications and deterministic artifact
  preservation evidence, and dated provider-confirmed field observations.
- Replay boundary: format/version, canonical proposal, preview fingerprint,
  deck revision, artifact checksum, and parsing options identify each result.
- Unknown boundary: malformed lines, unresolved card identities, partial
  imports, unsupported fields, and provider UI drift remain explicit.
- Decision boundary: the module does not resolve cards, choose categories,
  merge decks, or infer whether an import should be applied.
- Complete LLM workflow: list formats once, preview caller-provided content,
  review diagnostics and preservation limits, explicitly create a local deck
  when authorized, and export deterministic artifacts for manual provider use.

## Planning Approval

- Status: Approved
- Reviewed by: Nick Curry, repository owner
- Review date: 2026-07-04
- Reviewed revision: `4cc041b`
- Implementation authorized: Yes

## Completion Notes

Remote Archidekt synchronization is owned by `archidekt-deck-sync`; Moxfield
network automation remains excluded.

The four-tool implementation, offline dummy-deck workflows, and automated
acceptance gates are complete. The repository owner accepted both authenticated
manual UI imports. Archidekt and Moxfield are now `available`, with empirical
companion-only limits recorded instead of inferred syntax. The exact record is
in [Manual Provider Acceptance Records](PROVIDER_ACCEPTANCE.md); this packet
is complete because the repository owner confirmed both disposable provider
decks were deleted without retaining their IDs or URLs.

## Implementation Evidence

- All four tools are registered with the planned mode visibility. The complete
  current deck surface is 7 tools in `read-only` and 23 in `local`/`remote`,
  with one resource and zero prompts.
- The duplicate format catalogs are consolidated. Startup toolset selection
  and profile filtering are implemented by the completed
  `mcp-capability-toolsets` child; its default/all/none installed-package
  workflow supplies the dependent XCHG-018 profile evidence.
- The official-client dummy Commander workflow exercises format discovery,
  every export and preview dialect, generic creation, exact native recreation,
  category evidence, checksums, and cleanup against both the source build and
  installed NuGet tool.
- `task lint`, `task test`, `task surface:report`, `task coverage`, `task pack`,
  `task smoke:process`, `task smoke:mcp`, and `task release:tool-smoke` pass.
  The suite has 205 passing tests. Line coverage is App 94.64%, Core 100%,
  Decks 94.15%, and Scryfall 93.75%.
- Abstraction, code-quality, visual-readability, dead-code, dependency,
  test-coverage, test-quality, and documentation-sync audits passed after
  fixes. NuGet reports no vulnerable, deprecated, or outdated packages.
