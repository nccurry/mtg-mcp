# Preview Validation

## Result

The 2026-07-12 preview closure pass is green. Stable release validation must be
repeated from the final release commit.

## Commands

| Check | Result |
| --- | --- |
| `task lint` | Passed with zero warnings and errors |
| `task test` | 643 offline tests passed |
| `task surface:report` | Exact 93-tool surface passed |
| `task coverage` | All seven production assemblies passed 90 percent |
| `task pack` | `0.9.0-preview.1` packages created |
| `task smoke:process` | Process readiness passed |
| `task smoke:mcp` | 43 official-client smoke tests passed |
| `task release:tool-smoke` | Installed package readiness and 43 MCP tests passed |

## Coverage

| Assembly | Line coverage |
| --- | ---: |
| `MtgMcp.App` | 91.11% |
| `MtgMcp.Archidekt` | 91.04% |
| `MtgMcp.Core` | 99.39% |
| `MtgMcp.Decks` | 93.82% |
| `MtgMcp.Playgroup` | 95.81% |
| `MtgMcp.Scryfall` | 93.81% |
| `MtgMcp.Statistics` | 96.32% |

## Audits

Code quality, abstraction boundaries, dead code, test coverage, test quality,
visual readability, dependencies, and documentation were reviewed. No
unresolved code or test finding remains.

NuGet reports no vulnerable or deprecated packages. Newer analyzer and MCP
patch/minor versions exist; they remain deferred because this pass does not
change reviewed dependency pins.

Relative links resolve across all tracked and new Markdown/text files. Stale
current-guidance searches, encoding checks, debug-marker checks, and
`git diff --check` pass.

## Open release gates

- Approve and test stable artifacts on supported platforms.
- Repeat all gates from the final release commit.
- Record stable release authority, tag, package publication, and final PLC
  lifecycle closure.

## Rollback rehearsal

The latest installable prior NuGet package is `0.7.0`; the `0.8.0` Git tag has
no corresponding NuGet package or GitHub release artifact. An isolated `0.7.0`
tool installation completed its process smoke without copying or migrating the
`v0.9` data root. The temporary installation was removed after the check.
