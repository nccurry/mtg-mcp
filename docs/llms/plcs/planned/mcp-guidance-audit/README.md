# MCP Guidance Audit Plan

Check every tool of the server against the current MCP specification and the Anthropic and OpenAI tool-design
guidance, and fix what the check finds, so clients and agents get accurate tool hints, predictable errors, and
results that fit their limits. The product rules do not change: no tool is added or removed.

## Current Work

- Status: Planned. Drafted on 2026-11-07 from the guidance re-read recorded in the `deckbuilding-agent-guidance`
  packet (2026-11-05); reviewed once by an independent reader, not yet by the owner.
- Owner: mtg-mcp (@nccurry)
- Last updated: 2026-11-07, drafted against `main` at `ece3ba9` (`0.15.0`).
- Implementation authorized: No. The owner asked for this packet to exist; starting it needs a separate go-ahead.
- Selected work and endpoint: phases P1 to P3, merged into `main` and shipped in the next release.
- Next action: owner review of the requirements and the open question below.
- Blocker: None.
- Resume check: this packet is in `planned/`; no implementation worktree for `mcp-guidance-audit` exists.

## Phases

| Phase | Name | Owner | State |
| --- | --- | --- | --- |
| P1 | Audit: measure every requirement and record findings in the Evidence table | | Planned |
| P2 | Fix: repair each supported finding, with tests | | Planned |
| P3 | Docs and release | | Planned |

P1 changes no code. P2 starts after the owner has read the P1 findings.

## Outcome

Every tool's hints, errors, handles, descriptions, and result sizes match what MCP clients and tool-design guidance
expect, and a test locks each one so it cannot drift. The server's behavior for agents does not change except where
a finding shows a wrong hint or a missing next step.

## Done when

Every requirement passes its check, or its finding is recorded as fixed or deliberately left, with a reason.

| ID | Requirement | Check |
| --- | --- | --- |
| REQ-001 | Every registered tool sets all four annotation hints (`readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`) with values that match what it does. `openWorldHint` is true when the tool reaches a remote network service and false when it reads or writes only local state. All 86 tools already set the four fields explicitly, and `ToolSurfaceRulesTests` already checks the read-only and destructive values, so P1 judges the rest: for example `card_list_query`, `card_lists_compare`, and `local_deck_identity_reconcile_preview` are marked open-world, and `scryfall_tags_search`, `scryfall_card_data_status`, `archidekt_auth_status`, and `playgroup_auth_status` are not. | One architecture test (an extension of `ToolSurfaceRulesTests`) checks all four values for every tool against a table kept in the test. The P1 findings list each tool whose open-world or idempotent value changes, with the reason. |
| REQ-002 | Every success and error result of every tool conforms to the tool's `outputSchema`. A failure that cannot conform is returned as a tool error (REQ-003), and the schema says it is outside the success shape. | An extension of `ToolOutputSchemaTests` validates one recorded success result and one recorded failure result for each tool against its schema. |
| REQ-003 | Input validation, not-found, and stale-cursor failures are returned as tool results with `isError` set and a next step in the message, never as protocol errors. | `SurfaceConsistencyTests` and `ToolProtocolMcpTests` check `isError` and a non-empty next step for each failure reason code. |
| REQ-004 | Every `*_preview` tool and every tool that returns a handle (`requestSnapshotId`, `previewFingerprint`, `cursor`) states in its description how long the handle stays valid and what an expired or unknown handle returns. | A test fails when such a description lacks a lifetime statement. A live check confirms the stated behavior for one preview and one snapshot. |
| REQ-005 | The opening sentence of every description names the data source, as `AGENTS.md` already requires, and says what the tool does in words an agent would type to find it. | The existing description tests check the source. The P1 review lists each opening that does not say what the tool does, and P2 rewrites them. |
| REQ-006 | The default result of every tool is at most `ResultSizeLimit.MaximumBytes` (40,000 bytes, about 10,000 tokens), which is under Claude Code's 25,000-token cap. P1 names each tool that returns a result outside that limit; each one either gets the limit or raises its own cap on purpose. | A test measures the largest recorded default result of every tool against the limit. The P1 findings list any tool over it. |
| REQ-007 | `tools/list` returns the same tools in the same order for the same configuration in every session. `ToolsetNorthStarMcpTests` already checks ordinal order inside one session. | The new check starts two sessions with the same configuration and compares the lists. |
| REQ-008 | The packet records which MCP specification revision and SDK version the server speaks, and which newer-revision fields (such as `resultType`) it lacks. It adopts none of them. Input-required (elicitation) results are a watch item for preview and apply pairs. | The Evidence table has the row. The CHANGELOG lists any resulting change. |

## Product rule report

No `AGENTS.md` product rule is touched. No tool, toolset, operation mode, or alias is added, merged, or removed. A
fix that changes a public tool shape updates `docs/reference.md`, `CHANGELOG.md`, and the related tests, follows
`docs/versioning.md`, and renames everywhere with no alias (the `0.15.0` clean-break rule).

## Test architecture

Extend the existing surface tests (`ToolSurfaceRulesTests`, `ToolOutputSchemaTests`, `SurfaceConsistencyTests`,
`ToolProtocolMcpTests`, `ToolsetNorthStarMcpTests`) instead of adding a parallel suite. Tests stay offline and use
recorded results. The live checks in REQ-004 are one-off and recorded in the Evidence table, not added to `task test`.

## Assumptions and risks

- Assumption: Claude Code's limits (4,096 and 16,384 characters for descriptions, 25,000 tokens for results) and the
  MCP revision 2026-07-28 are as the 2026-11-05 re-read recorded. P1 re-reads them first.
- Risk: setting `destructiveHint` or `openWorldHint` to a different value changes how some clients prompt the user.
  P1 lists each change so the owner sees them before P2.
- Risk: size checks use recorded results, so a tool whose real worst case is larger than any record passes wrongly.
  P1 adds a record for the largest known case of each tool that can return a page.

## Sources

Read on 2026-11-05 and recorded in the `deckbuilding-agent-guidance` packet's Evidence table: the MCP specification
(revision 2026-07-28: tools, output schema, annotations, pagination, errors), Anthropic's "Writing effective tools for
agents" and the Claude Code MCP documentation (tool search, description limits, output cap,
`anthropic/maxResultSizeChars`, `anthropic/alwaysLoad`), and OpenAI's function-calling and Apps SDK metadata guidance
(annotation advice, "Use this when ..." openings, fewer than 20 tools up front when there is no tool search).

## Open questions

- Open question: Run this before or after `deckbuilding-agent-guidance`? (blocks the start of P1; waits on @nccurry.)
  Recommended: after, because the plugin work is user-facing and larger. It could go first if the owner wants the
  plugin's skills written against corrected hints.

## Evidence

| Date | Check | Result |
| --- | --- | --- |
| 2026-11-07 | Independent review of the draft | 7 findings on this packet (open-world rule contradicted by the code, existing tests not named, untestable checks, missing sections); all fixed in this revision. |
| | P1 findings: annotations, schemas, errors, handle lifetimes, openings, result sizes, tool order, spec and SDK revision | not run |

## Log

- 2026-11-07 Created from the owner's decision on `deckbuilding-agent-guidance` OQ-7 (open a separate packet for the tool-surface audit).
