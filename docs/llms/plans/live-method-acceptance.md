# Live Method Acceptance

## Purpose

This acceptance verifies the packaged `0.9.0-preview.1` MCP through the
official C# client. It covers the capability resource and every current public
tool. It does not treat internal C# methods as public acceptance units.

The fixed provider fixtures are:

- Archidekt deck `24086044`, with snapshot-first mutation and verified content
  restoration;
- Playgroup `49295`, using authenticated reads only; and
- a guarded scratch online backup of the retained Scryfall corpus.

## Safety Boundary

The runner requires `MTGMCP_RUN_LIVE_METHOD_ACCEPTANCE=1`, an installed package
command, and an explicitly created `MTGMCP_LIVE_ACCEPTANCE_DATA_DIR` outside
both the repository and the normal application-data root. An empty root is
marked on first use. A nonempty unmarked root is refused.

The task also refuses a dirty worktree, resolves the exact tested commit, and
binds every retained result to that commit and the installed package version.
Changing either identity starts a new empty journal rather than reusing stale
passes from an earlier build.

The scratch root owns a path-free JSON journal. It never contains credentials,
account identities, provider payloads, game identifiers, disposable provider
identifiers, or local paths. Provider calls are sequential and use production
pacing. The harness adds no retries.

The two Playgroup writes remain `fixture-only-owner-approved`: Public API
1.0.0 exposes no delete, undo, close-session, or event-removal operation.

## Running

Set the broad opt-in, scratch root, and Playgroup key in the invoking process.
The standard Archidekt credential file is discovered by the application.

```powershell
$env:MTGMCP_RUN_LIVE_METHOD_ACCEPTANCE = '1'
$env:MTGMCP_LIVE_ACCEPTANCE_DATA_DIR = 'C:\path\outside\the\repository'
$env:MTGMCP__PLAYGROUP__API_KEY = '<private value>'
task test:live:methods
```

Set `MTGMCP_RUN_FULL_SCRYFALL_CORPUS=1` when running the corpus lifecycle. If
the provider has not advanced beyond the retained generation, the runner
records `pending-provider-generation` and preserves only the guarded scratch
copy for a later rerun.

## Required Result

| Capability | Registered | Required live passes | Accepted non-live |
| --- | ---: | ---: | ---: |
| Decks | 23 | 23 | 0 |
| Scryfall | 18 | 18 | 0 |
| Archidekt | 23 | 23 | 0 |
| Playgroup | 16 | 14 | 2 |
| **Total** | **80** | **78** | **2** |

Completion additionally requires one capability resource pass, exact
`default`/`all`/`none` mode counts, exact Archidekt baseline restoration, no
remaining disposable Archidekt resources, zero Playgroup writes, unchanged
retained Scryfall bytes and timestamp, and successful rollback in both
directions before scratch-corpus deletion.

## Current Evidence

- Playgroup implementation baseline: `04dc9b5`.
- Package version: `0.9.0-preview.1`.
- Harness status: implemented; live execution pending explicit environment and
  packaged-run evidence.
- Playgroup write status: `fixture-only-owner-approved`.

Update this section only from the sanitized journal summary after a complete
packaged run. A schema listing or adapter-only test is not a live pass.
