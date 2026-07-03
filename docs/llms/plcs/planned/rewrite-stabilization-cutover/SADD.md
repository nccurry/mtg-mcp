# Rewrite Stabilization And 0.9.0 Cutover Software Architecture And Design Document

## Document Control

- Lifecycle status: Planned
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)

## Chosen Design

Cutover is an evidence-producing integration stage, not another capability
layer. It accepts only completed child implementations and validates the final
composition from the same commit and packaged artifacts proposed for release.

### Final production modules

| Project | Ownership |
| --- | --- |
| `MtgMcp.Core` | Provider-neutral evidence, failures, identifiers, and shared contracts. |
| `MtgMcp.App` | MCP host, capability resource, operation modes, composition, and server metadata. |
| `MtgMcp.Decks` | Local deck domain, SQLite store, and manual interchange. |
| `MtgMcp.Scryfall` | Official Scryfall transport and immutable snapshot store. |
| `MtgMcp.Archidekt` | Archidekt transport, authentication, mapping, and synchronization. |
| `MtgMcp.Playgroup` | Pinned official Playgroup public API adapter. |
| `MtgMcp.Statistics` | Exact provider-independent probability calculations. |
| `MtgMcp.Tagger` | Tagger cache and explicitly invoked bounded acquisition. |

Dependency tests enforce that Core references no adapter or host; provider
adapters do not reference one another; App is the composition root; and Decks,
Statistics, and provider adapters exchange Core contracts rather than transport
payloads.

### Frozen MCP surface

| Family | Tool count | Source child |
| --- | ---: | --- |
| Server metadata tools | 0 | Foundation uses initialization and the capability resource. |
| `deck_*` local deck operations | 19 | Local deck store |
| `deck_*` manual interchange | 5 | Manual interchange |
| `scryfall_*` | 7 | Scryfall snapshots |
| `archidekt_*` | 10 | Archidekt synchronization |
| `playgroup_*` | 16 | Playgroup public API |
| `stats_*` | 8 | Exact statistics |
| `tagger_*` | 6 | Tagger cache |
| **Total** | **71** | |

The only resource is `mtg://server/capabilities`; there are no prompts.
Canonical per-mode discovery snapshots contain 44, 66, and 71 tools for
`read-only`, `local`, and `remote`, respectively. Schema canonicalization sorts
objects only where order is semantically irrelevant and never weakens exact
name, description, annotation, input, or output-schema comparisons.

`read-only` is a mutation-authority mode: its 44 tools may include explicit
Scryfall, Archidekt, or Playgroup network reads/previews, but every local and
remote write spy must remain zero. “Offline” describes normal validation, not a
runtime mode. Manual interchange is owned by `MtgMcp.Decks`; no separate
interchange assembly is expected.

The family table in this packet is the maintained surface-count crosswalk. Any
child surface edit updates the owning child matrix, this table, all three totals,
and canonical discovery snapshots in the same reviewed change. Until all four
agree, the change is contract drift and cannot merge.

### Provider proof and waiver classifications

| Proof | Allowed release treatment |
| --- | --- |
| Archidekt create/push/pull/delete | Must pass with verified cleanup; no waiver. |
| Tagger one-card acquisition | Must pass; policy objection, 403/429, or unsupported contract blocks release. |
| Scryfall official read | Must normally pass; repository owner may approve a temporary operational skip with current official contract evidence, offline fixtures, reason, date, and expiry. |
| Playgroup official reads | Must normally pass; repository owner may approve a credential/availability skip with pinned-contract and offline-fixture evidence. |
| Playgroup writes without safe cleanup | May remain fixture-only only under the child-7 repository-owner waiver record. |

An approved skip is never recorded as passed. Waivers cannot excuse contract
drift, unsafe setup, credential leakage, residual state, or a required
capability being disabled. Every waiver names owner, date, reviewed revision,
reason, evidence, scope, expiry/recheck trigger, and user-visible limitation.

### Integration and release flow

All implementation is integrated on `ncurry/evidence-first-mcp-rewrite` in its
sibling worktree. After every child is complete, the branch incorporates the
latest `main`, resolves conflicts according to approved PLCs, and runs the full
gate again. Integration uses ordinary merge or rebase operations permitted by
repository policy; it never replaces or filters repository history.

Preview packages use `0.9.0-preview.N`. The release candidate is installed into
clean Windows and supported non-Windows environments, launched from the package,
queried in each mode, and exercised against fresh and legacy-adjacent data
directories. Stable `0.9.0` is built only from the accepted commit.

### Evidence bundle

The release evidence bundle records:

- commit, toolchain, OS, package version, and canonical schema hashes;
- task command, start/end time, exit status, and artifact path;
- per-assembly coverage and any reviewed exclusion;
- forbidden-surface, dependency, secret-redaction, and mode results;
- redacted provider live-test classification and cleanup outcome;
- package install/smoke and rollback rehearsal results; and
- reviewed defects, waivers, approvals, and PLC revisions.

Evidence contains no tokens, cookies, usernames, credential paths, deck URLs,
stable remote identifiers, or raw provider responses. A skipped or unsupported
check remains visibly different from a pass.

### Data and rollback

`0.9.0` selects a new versioned application-data root containing separate
`decks.db`, `scryfall.db`, and `tagger.db`. It does not search legacy roots.
Rollback reinstalls the prior stable package and points it at its unchanged
legacy configuration/data. New stores remain untouched for diagnosis or later
manual export; the rollback never down-migrates them.

## Alternatives Considered

| Alternative | Decision |
| --- | --- |
| Preserve legacy tools as aliases | Rejected; obscures the clean break and keeps decision surfaces alive. |
| Release directly as stable | Rejected; preview packaging and cross-platform smoke are required. |
| Accept aggregate coverage only | Rejected; a weak assembly could hide behind a heavily tested one. |
| Treat unavailable live tests as passing | Rejected; unavailable, skipped, waived, and passed have different meanings. |
| Auto-migrate or delete legacy stores | Rejected; rollback and user-data safety require side-by-side isolation. |
| Add a compatibility host | Rejected; it expands the stable surface and migration burden. |

## Stop Conditions

Cutover stops for any incomplete dependency; schema or provider-contract drift;
failed required offline test; per-assembly coverage below 90 percent; unresolved
priority-1/priority-2 defect; credential exposure; unsafe live-test setup;
Archidekt deletion that is unavailable, unverifiable, or leaves remote state;
Tagger policy/contract objection or blocking response; package smoke failure;
or unsuccessful rollback rehearsal.

## Test Architecture

Normal validation is deterministic and offline. Unit tests cover canonical
schemas and version selection. Architecture tests cover projects and forbidden
references. MCP end-to-end tests start the packaged host in every mode.
Temporary directories cover fresh, corrupt, read-only, and legacy-adjacent data.
Opt-in `Category=Live` tests are separate jobs with provider-specific secrets,
bounds, redaction checks, and cleanup records.
