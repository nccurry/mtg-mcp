<!-- Ordinary plan: the shared PLC template as one file, with this
repository's sections after the shared ones and before the Log. Use it when
shared context and implementation order matter but a requirements-backed PLC
would add more ceremony than value. Save it as
`docs/llms/plans/planned/<feature-slug>.md`, delete sections that do not apply, and
delete this note. -->

# <Change Name>

<One or two sentences: the problem, the intended outcome, and the smallest
useful change.>

## Current Work

- Status: Planned
- Owner: @<handle>
- Last updated: <YYYY-MM-DD>, `<branch>` at `<short commit>`
- Next action: <one concrete step, or None>
- Blocker: <what is needed and who owns it, or None>
- Depends on: <plan or packet names, comma-separated, or None>
- Current state: <one or two lines: what is done, merged, verified or waiting>
- Resume check: <the first command to run and its expected result>

Where the work lives:

| Checkout | Branch | Owner | State |
| --- | --- | --- | --- |
| None | | | |

## Phases

| Phase | Name | Owner | State |
| --- | --- | --- | --- |
| P1 | <smallest coherent edit> | @<handle> | Planned |

## Done when

- REQ-001: <observable behavior>. Check: `<task or test>` → <expected result>

## Open questions

- Open question: <question> (blocks <phase or requirement IDs>; waits on @<handle>)

## Goals And Non-Goals

- Goals: <observable outcomes>
- Non-goals: <nearby work intentionally excluded>
- Constraints: <architecture, compatibility, security, or delivery limits>

## Affected Areas

List affected projects, public APIs, MCP tools/resources/prompts, configuration,
data formats, docs, generated files, and downstream users.

## Risks And Assumptions

| Item | Type | Mitigation or validation |
| --- | --- | --- |
| <Item> | Risk/Assumption | <Action> |

## Log

- <YYYY-MM-DD> Created.
