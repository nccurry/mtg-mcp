# Latest MCP and Toolchain Test Cases

## Test Matrix

| ID | Test case | Expected result |
| --- | --- | --- |
| LMT-FIX-001 | Start the server with an E2E client pinned to `2026-07-28`. | The client lists tools and reads `mtg://server/capabilities`. |
| LMT-FIX-002 | Start the server with an E2E client pinned to `2025-11-25`. | The SDK reports an unsupported protocol error. |
| LMT-FIX-003 | Read the capability resource through a current client. | `server.protocolVersion` is `2026-07-28`. |
| LMT-FIX-004 | List the default and all-toolset profiles through a current client. | Tool counts and names remain unchanged for this phase. |
| LMT-FIX-005 | Inspect the stable server output types. | Tool results remain structured JSON and text, the capability resource remains JSON text, and no binary MCP content block is added. |
| LMT-FIX-006 | Run `mise exec -- task setup:verify`. | The declared SDK, PowerShell, and local tools are available. |
| LMT-FIX-007 | Run locked Mise install and NuGet restore from a clean tool cache. | Lockable Mise downloads and the NuGet graph resolve without a moving version lookup; `global.json` selects the exact .NET SDK. |
| LMT-FIX-008 | Read both GitHub workflows. | Each `uses:` reference is a commit SHA with a release comment, and each Mise step requests the exact reviewed runner version. |
| LMT-FIX-009 | Run the registry-publisher Task path. | The configured Mise publisher version runs; the workflow does not download `latest`. |
| LMT-FIX-010 | Run `mise exec -- task deps:check`. | The report has no unreviewed direct update or vulnerability. |

## Source Evidence

The public SDK options take protocol revision strings. SDK 2.2.0 keeps its
`McpProtocolVersions` helper internal, so the host and process-test helper each
name their small wire-version constant. The E2E test proves the client and
server still agree on the same public protocol value.

## Update Rule

If an SDK update supports a newer protocol, create a new short packet or amend
this one before changing the protocol requirement. Do not make a current client
silently negotiate an older revision.
