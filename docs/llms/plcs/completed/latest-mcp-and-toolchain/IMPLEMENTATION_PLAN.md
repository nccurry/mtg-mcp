# Latest MCP and Toolchain Implementation Plan

## Document Control

- Lifecycle status: Complete
- PLC packet: [README.md](README.md)
- Parent PLC: [Evidence-First Deckbuilding Evolution](../../planned/evidence-first-deckbuilding-evolution/IMPLEMENTATION_PLAN.md#phase-2-latest-mcp-and-toolchain)
- Owner: mtg-mcp
- Last updated: 2026-09-07
- Related requirements: [SRD.md](SRD.md)
- Related design: [SADD.md](SADD.md)
- Implementation authorized: Yes

## Strategy

Start with the dependency report. Do not run a broad updater before the report
shows a direct update. Then make the small protocol change and prove that the
server accepts only the current MCP revision.

The phase remains useful even if no direct version file changes. The protocol
pin and its rejection test are the product change.

## Completion

All three delivery phases completed on 2026-09-07. The final evidence is in
[README.md](README.md#validation-evidence).

## Phase Summary

| Phase | Goal | Requirements | Main files | Exit |
| --- | --- | --- | --- | --- |
| 1 | Pin and lock the current toolchain. | LMT-001 to LMT-006 | Version files, locks, workflows, Taskfile | The reviewed tool and package graph installs without a moving download. |
| 2 | Require the newest MCP protocol. | LMT-007 to LMT-010 | App host and E2E tests | Current clients connect. Older clients fail. |
| 3 | Finish documentation and package proof. | LMT-011 to LMT-012 | Docs, tests, package tasks | The full validation suite passes. |

## Phase 1: Pin and Lock the Toolchain

- Read `task --list` and use it as the supported command menu.
- Run `mise exec -- task setup:verify`.
- Run `mise exec -- task deps:check`.
- Update an exact direct pin only when the report and official release notes
  support the update.
- Add `mcp-publisher` as an exact GitHub-release tool in Mise. Generate and
  commit its `mise.lock` entry.
- Keep `mise.toml`'s `min_version` as a local bootstrap floor. Set the
  SHA-pinned Mise action's `version` input to the exact reviewed runner version
  in both workflows.
- Enable and commit Mise lock data for lockable downloads. Use locked install
  in setup and CI where Mise supports it.
- Generate and commit NuGet package lock files. Use locked restore in CI.
- Change every GitHub Action reference to an exact SHA with its release comment.
- Replace the release workflow's moving publisher download with the Mise-managed
  publisher command.
- Keep the existing weekly Dependabot entries for GitHub Actions and NuGet.

Exit criteria:

- All direct version files and resolved lock files are current and reviewed.
- A clean checkout installs the same tool and package graph.
- The release workflow contains no moving tool download or floating action tag.

## Phase 2: Require MCP 2026-07-28

- Set `McpServerOptions.ProtocolVersion` from the host's named current-protocol constant.
- Add one private E2E client-creation method that sets
  `McpClientOptions.ProtocolVersion` for both normal and live session paths.
- Keep the helper's current protocol as the default. Add one narrow optional
  protocol input for the older-client rejection test only, and assert its
  failure kind rather than an exact SDK error message.
- Add a current-protocol connection test.
- Add an older `initialize` client rejection test.
- Assert the capability resource reports `2026-07-28`.
- Keep the tool, resource, prompt, mode, and toolset counts unchanged.

Exit criteria:

- A current client can complete the normal process smoke path.
- An older client cannot obtain a fallback session.
- The protocol policy has no configuration switch or compatibility path.

## Phase 3: Finish and Prove the Change

- Update setup, version, release, and protocol documentation.
- Run the focused App and E2E tests first.
- Run `task lint`, `task test`, `task coverage`, and `task smoke:mcp`.
- Run the installed-package smoke check after the package build.
- Run the required code and plain-language audits for the phase diff.
- Update this packet with the commands and results.

Exit criteria:

- All Must requirements have evidence.
- The full phase diff has a passing audit result.
- The repository is clean after the final checks.

## Cleanup

Remove old protocol assumptions from test descriptions and documentation. Do not
leave a compatibility comment, unused protocol constant, or fallback test.

## Rollback

If current-protocol tests expose an SDK defect, revert only the protocol pin.
Keep the version inventory and test evidence. Do not add legacy support as a
temporary repair.
