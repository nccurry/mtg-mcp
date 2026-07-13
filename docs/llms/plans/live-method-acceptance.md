# Live Method Acceptance

## Purpose

Verify the packaged `0.9.0-preview.1` MCP through the official C# client. The
unit of acceptance is a public tool or resource, not an internal C# method.

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
has no cleanup operation. Scryfall corpus work uses a guarded scratch copy.

## Run

```powershell
$env:MTGMCP_RUN_LIVE_METHOD_ACCEPTANCE = '1'
$env:MTGMCP_LIVE_ACCEPTANCE_DATA_DIR = 'C:\path\outside\the\repository'
task test:live:methods
```

Set `MTGMCP_RUN_FULL_SCRYFALL_CORPUS=1` to exercise corpus generations. When
Scryfall has not published a second generation, rollback remains
`pending-provider-generation`.

## Required surface

| Capability | Registered | Live | Fixture-backed | Pending generation | Fixture-only |
| --- | ---: | ---: | ---: | ---: | ---: |
| Decks | 28 | 28 | 0 | 0 | 0 |
| Scryfall | 18 | 15 | 2 | 1 | 0 |
| Statistics | 8 | 8 | 0 | 0 | 0 |
| Archidekt | 23 | 23 | 0 | 0 | 0 |
| Playgroup | 16 | 14 | 0 | 0 | 2 |
| **Total** | **93** | **88** | **2** | **1** | **2** |

The run must also pass the capability resource; exact profile and mode counts;
Archidekt restore and cleanup; zero Playgroup writes; and retained Scryfall
database isolation.

## Current result

The final packaged run passed on 2026-07-12 at
`e0d68e7cf897430f9c43b4657307fd520469cbf7`.

- Capability resource: passed.
- Live tools: 88.
- Fixture-backed Scryfall lifecycle tools: 2.
- Scryfall rollback: `pending-provider-generation`.
- Fixture-only Playgroup writes: 2.
- Archidekt restore and cleanup: passed.
- Playgroup writes sent: 0.
- Retained Scryfall database changed: no.

See the sanitized
[cutover acceptance record](../plcs/completed/rewrite-stabilization-cutover/LIVE_ACCEPTANCE.md).
