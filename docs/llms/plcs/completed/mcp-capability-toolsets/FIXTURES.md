# MCP Capability Toolsets Fixtures And Acceptance Matrix

## Configuration Fixtures

| ID | Input | Expected result |
| --- | --- | --- |
| TSET-FIX-001 | Toolsets omitted | `default`; implemented default-enabled descriptors selected. |
| TSET-FIX-002 | `default`, `all`, or `none` | Exact reserved profile; no explicit-name mixture. |
| TSET-FIX-003 | Synthetic implemented descriptors selected as `decks,scryfall` and reversed order | Same canonical explicit selection; the runtime registry still rejects unimplemented names. |
| TSET-FIX-004 | Blank segment, duplicate, case variant, or unknown name | Sanitized startup failure before stdout. |
| TSET-FIX-005 | JSON, environment, and CLI all set | CLI wins, then environment, then JSON. |
| TSET-FIX-006 | Duplicate `--toolsets` key or incomplete CLI pair | Sanitized startup failure. |

## Current Surface Matrix

After the manual catalog consolidation, only `decks` is implemented:

| Selection | `read-only` | `local` | `remote` |
| --- | ---: | ---: | ---: |
| omitted / `default` | 7 | 23 | 23 |
| `all` | 7 | 23 | 23 |
| explicit `decks` | 7 | 23 | 23 |
| `none` | 0 | 0 | 0 |

Every row exposes one capability resource and zero prompts. No tool-list-change
capability is advertised.

## Final Planned Profiles

Using current child manifests after the one-tool interchange consolidation:

| Profile | Toolsets | `read-only` | `local` | `remote` |
| --- | --- | ---: | ---: | ---: |
| `default` | `decks,scryfall,stats` | 19 | 38 | 38 |
| `all` | All six stable toolsets | 48 | 70 | 83 |
| `none` | None | 0 | 0 | 0 |

These are derived planning checks, not compatibility constraints. Each child
surface change regenerates this table and the cutover manifest.

## Acceptance Matrix

| Requirements | Evidence |
| --- | --- |
| TSET-001, TSET-002, TSET-011 | TSET-FIX-001 through 006 and configuration round trips |
| TSET-003, TSET-005, TSET-013 | Architecture scan and direct invocation guard tests |
| TSET-004, TSET-006, TSET-007, TSET-008, TSET-009, TSET-010 | Current surface matrix in official-client tests |
| TSET-009, TSET-012 | Capability schema snapshot and default-to-all workflow |
| TSET-014 | Full offline, coverage, package, smoke, and dependency gates |

## North-Star Workflow

1. Start default local mode and read the capability resource.
2. Confirm only `decks` is implemented/enabled and 23 tools are visible.
3. Import, inspect, update, export, and delete a disposable Commander deck.
4. Restart with `none`; confirm zero tools and the same capability resource.
5. Restart with `all`; confirm every implemented stable tool returns.
6. Confirm no tool chooses cards, infers intent, or makes a deckbuilding judgment.

## Validation Evidence

The 2026-07-04 source and installed-package runs completed this matrix. The
official client observed canonical tool discovery and byte-stable capability
resources for every current profile/mode pair. The disposable Commander
workflow imported, inspected, updated, exported, deleted, and re-listed a
local deck; `none` and `all` then returned their exact expected surfaces
without extra data-root creation.
