# Live Method Acceptance

> Historical acceptance record for the `0.9.0` cutover. The counts and package
> version below are not a current release checklist. Use `task --list` and the
> current release tasks for a new release.

## Purpose

This record verified the packaged `0.9.0-preview.1` MCP through the official
C# client. The unit of acceptance was a public tool or resource, not an
internal C# method.

## Safety

The runner requires:

- `MTGMCP_RUN_LIVE_METHOD_ACCEPTANCE=1`;
- an installed package command;
- an explicitly selected scratch directory outside the repository and normal
  application-data root; and
- a clean worktree pinned to the installed package commit.

Provider calls are sequential and use production pacing. The harness adds no
retry. It records no credentials, identities, provider payloads, remote IDs, or
local paths.

The Archidekt workflow uses owner-authorized disposable state and verifies
restore and cleanup. Playgroup writes are never invoked because the public API
has no cleanup operation. Full Scryfall card-data work uses a guarded scratch copy.

## Run

```powershell
$env:MTGMCP_RUN_LIVE_METHOD_ACCEPTANCE = '1'
$env:MTGMCP_LIVE_ACCEPTANCE_DATA_DIR = 'C:\path\outside\the\repository'
task test:live:methods
```

Set `MTGMCP_RUN_FULL_SCRYFALL_CORPUS=1` to exercise installed card-data versions. When
Scryfall has not published a second generation, rollback remains
`pending-provider-generation`.

## Required tools

| Feature | Registered | Live | Saved test data | Waiting for new data | No live write |
| --- | ---: | ---: | ---: | ---: | ---: |
| Decks | 29 | 29 | 0 | 0 | 0 |
| Scryfall | 18 | 15 | 2 | 1 | 0 |
| Statistics | 8 | 8 | 0 | 0 | 0 |
| Archidekt | 23 | 23 | 0 | 0 | 0 |
| Playgroup | 16 | 14 | 0 | 0 | 2 |
| **Total** | **93** | **88** | **2** | **1** | **2** |

The run also verifies the capabilities resource, exact profile and mode counts,
Archidekt restore and cleanup, zero Playgroup writes, and Scryfall database
isolation.

## Current result

The final packaged run passed on 2026-07-12 at
`e0d68e7cf897430f9c43b4657307fd520469cbf7`.

- Capabilities resource: passed.
- Live tools: 88.
- Scryfall lifecycle tools using saved test data: 2.
- Scryfall rollback: `pending-provider-generation`.
- Playgroup writes with no live write test: 2.
- Archidekt restore and cleanup: passed.
- Playgroup writes sent: 0.
- Retained Scryfall database changed: no.

See the sanitized
[cutover acceptance record](../plcs/completed/rewrite-stabilization-cutover/LIVE_ACCEPTANCE.md).
