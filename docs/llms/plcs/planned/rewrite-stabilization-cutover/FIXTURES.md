# Rewrite Stabilization And 0.9.0 Cutover Fixtures And Acceptance Matrix

## Current Planned MCP Surface Baseline

| Family | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| Server metadata tools | 0 | 0 | 0 |
| Local deck store | 4 | 19 | 19 |
| Manual deck interchange | 3 | 4 | 4 |
| Deterministic deck categorization | 2 | 3 | 3 |
| Scryfall corpus and evidence | 14 | 18 | 18 |
| Archidekt decks, folders, and snapshots | 11 | 12 | 23 |
| Playgroup public API | 14 | 14 | 16 |
| Exact statistics | 8 | 8 | 8 |
| **`all` profile total tools** | **56** | **78** | **91** |
| **Default profile (`decks,scryfall,stats`)** | **31** | **52** | **52** |

Every mode exposes exactly one resource, `mtg://server/capabilities`, and zero
prompts. Its capability document identifies the active mode and only the tools
visible in that mode and selected toolsets. The 56/78/91 `all` counts and
31/52/52 default counts are derived from the proposed AMEND-004 child drafts. They detect
inconsistencies in those drafts; they are not legacy-
compatibility targets and may change with an approved better surface.

This table is the surface-count crosswalk. A tool change in any child must
regenerate its row and totals and update canonical per-mode snapshots in the
same change.

## Acceptance Fixtures

| ID | Requirement | Scenario | Expected result |
| --- | --- | --- | --- |
| CUT-FIX-001 | CUT-001 | One child is Draft or In progress | Cutover cannot start. |
| CUT-FIX-002 | CUT-002 | Solution/project graph | Exactly seven production projects with approved dependency direction. |
| CUT-FIX-003 | CUT-003, CUT-004 | Canonical MCP discovery in all modes | Exact names, schemas, visibility, and derived counts match the approved manifest/snapshots. |
| CUT-FIX-004 | CUT-005 | Forbidden surface/project scan | No stable match outside explicitly historical docs or fixtures. |
| CUT-FIX-005 | CUT-006 | Per-assembly coverage | Every production assembly is at least 90 percent. |
| CUT-FIX-006 | CUT-007, CUT-008 | Full final-commit offline run | Lint, tests, coverage, security, architecture, package, and smoke pass. |
| CUT-FIX-007 | CUT-009 | Scryfall bulk metadata, bounded API read, explicit manual full-corpus install/reuse, and packaged rollback | Official contract, bounds, generation activation, second-process reuse, fixture-backed rollback, no remote mutation, and redacted report pass. |
| CUT-FIX-008 | CUT-010 | Archidekt private throwaway workflow | Deck sync, folder organization, snapshot lifecycle/restore, and final folder/snapshot/deck cleanup pass with no residual object. |
| CUT-FIX-009 | CUT-010 | Archidekt folder, snapshot, or deck cleanup is unavailable or fails | Cutover stops; result cannot be waived as success. |
| CUT-FIX-010 | CUT-011 | Playgroup safe live reads plus pinned-contract write fixtures | Read status and fixture-only write limitation are explicit; no write is labeled live-tested. |
| CUT-FIX-011 | CUT-012 | Legacy root beside fresh `0.9.0` root | New host ignores and does not alter legacy files. |
| CUT-FIX-012 | CUT-013 | User and provider documentation | Links, examples, limitations, modes, and rollback review pass. |
| CUT-FIX-013 | CUT-014 | Open priority-2 defect or contract drift | Stable approval is blocked. |
| CUT-FIX-014 | CUT-015 | Latest `main` integrated | Full gate rerun passes from the resulting commit. |
| CUT-FIX-015 | CUT-016 | Preview and stable package metadata | Preview uses suffix; accepted stable package reports `0.9.0`. |
| CUT-FIX-016 | CUT-017 | Prior package/data rollback rehearsal | Prior host starts and passes smoke without transforming new stores. |
| CUT-FIX-017 | CUT-018 | PLC registry after release | Eleven children and umbrella have accurate lifecycle/approval evidence. |
| CUT-FIX-018 | CUT-019 | `read-only` provider read with local/remote write spies | Read may complete; every write spy remains zero. |
| CUT-FIX-019 | CUT-020 | Child adds/removes one tool without updating cutover | Contract test fails until child matrix, regenerated crosswalk/totals, and snapshots agree; no old count is required. |
| CUT-FIX-020 | CUT-009, CUT-011 | Provider proof unavailable | Temporary read-proof waivers use all required fields; the Scryfall full-corpus and Archidekt cleanup gates remain unwaivable; Playgroup writes retain their explicit fixture-only classification; no skip is labeled passed. |
| CUT-FIX-021 | CUT-021 | Default/all/none/explicit profiles in all modes | Exact toolset membership and capability counts match; `none` exposes zero tools; toolsets never widen mode authority. |
| CUT-FIX-022 | CUT-021 | Default-profile deckbuilding workflow | Local deck evidence, corpus-backed Scryfall facts/tags, deterministic caller rules, and exact statistics compose end to end without a recommendation, router, or optional provider surface. |
| CUT-FIX-023 | CUT-021 | Each optional provider workflow | Explicitly enabled Archidekt and Playgroup workflows preserve provenance, unknown states, and mode guards. |

## Forbidden Stable Surface

The scanner covers tool/resource/prompt names, registrations, descriptions,
project names, package contents, and public documentation. It rejects legacy
advisor or intent inference; weak-card or replacement recommendations; blended
quality scores; strategic plans; simulation/goldfish tools; unofficial Moxfield
network tools; CommanderSpellbook; legacy decklist providers; compatibility
aliases; and prompts. Historical audit evidence is excluded only by an explicit
path allowlist reviewed in the release bundle.

## Validation Matrix

| Layer | Required proof |
| --- | --- |
| Unit | Schema canonicalization, version selection, failure classification, redaction. |
| Integration | Project graph, databases, provider fakes, mode guard, package contents. |
| MCP schema | Exact tools/resource/prompts, toolset membership, profiles, and annotations for all three modes. |
| End to end | Packaged host starts and responds from fresh temporary directories. |
| Coverage | At least 90 percent line coverage for each production assembly. |
| Live | Separate opt-in provider jobs with bounds and cleanup classification. |
| Manual | Cross-platform install, documentation walkthrough, rollback rehearsal. |

## Release Stop Cases

- A requirement or child acceptance record is missing.
- An exact schema, package, project, or derived mode count differs from the
  approved manifest/snapshot.
- A forbidden surface or production assembly remains.
- A required offline command, package smoke, or coverage gate fails.
- A secret or credential path appears in output or evidence.
- A provider contract drifted without child review.
- Archidekt folder, snapshot, or deck cleanup is unavailable, unverifiable, or
  leaves remote state.
- A priority-1/priority-2 defect or security finding remains open.
- Rollback cannot restore the prior packaged server with its unchanged data.

## PLC Lifecycle Sequence

1. All eleven planning packets are independently approved; the umbrella planning
   program may complete without claiming implementation completion.
2. Children 1-10 are individually implemented, accepted, and moved to
   `completed/`.
3. This child moves to `in-progress/` and executes cutover.
4. After release and rollback evidence, this child moves to `completed/` and
   adds evidence links to the already completed prerequisites.

## Lifecycle Acceptance

The cutover child completes only after stable release evidence and rollback
proof are accepted. The umbrella completes when all eleven child planning packets
have been independently approved; that planning milestone remains distinct from
implementation and release completion. Registered post-cutover topics remain
separate PLCs and do not enter the `0.9.0` acceptance surface.
