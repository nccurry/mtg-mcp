# <Feature Name> PLC Packet

This is the short index for one packet under
`docs/llms/plcs/<lifecycle>/<feature-slug>/`. Keep detail in the linked
documents. Delete optional prompts that do not apply instead of adding filler.

## Lifecycle

- Status: Planned | In progress | Completed
- Folder: `docs/llms/plcs/planned/<feature-slug>/`
- Owner: mtg-mcp
- Created: <YYYY-MM-DD>
- Last updated: <YYYY-MM-DD>
- Current phase: <planning / phase name / validation / completed>

## Summary

State the feature or refactor, why it matters, the intended user, maintainer,
or agent outcome, and the smallest useful slice this packet should deliver.

## Packet Contents

- [SRD.md](SRD.md): requirements, acceptance criteria, scope, and validation expectations.
- [SADD.md](SADD.md): architecture, design tradeoffs, runtime flow, and test architecture.
- [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md): implementation phases and exit criteria.
- [FIXTURES.md](FIXTURES.md): fixture IDs, acceptance matrices, provider payloads, MCP surface inventories, or calibration cases when needed.

## Decision Snapshot

| Decision | Status | Rationale | Link |
| --- | --- | --- | --- |
| <Decision> | <Proposed/Accepted/Deferred> | <Rationale> | <SADD section or issue> |

## Project And Surface Impact

State the projects, adapters, MCP tools/resources/prompts, operation modes,
config keys, persistence formats, docs, generated artifacts, and downstream
users affected by this packet.

## Current Open Questions

| Question | Impact | Owner | Resolution plan |
| --- | --- | --- | --- |
| <Question> | <Impact> | <Owner> | <Plan> |

## Planning Readiness Checklist

- [ ] Scope and non-scope are explicit.
- [ ] Must requirements are testable and have acceptance criteria.
- [ ] Major alternatives and tradeoffs are recorded.
- [ ] Quality attributes are measurable or inspectable.
- [ ] Core/App/adapter/test boundaries and dependency impact are explicit.
- [ ] MCP surface, operation-mode, and documentation impacts are clear.
- [ ] Adapter auth, pacing, cache, retry, and error-sanitization impacts are clear when relevant.
- [ ] Documentation, readability, and abstraction reuse expectations are clear.
- [ ] SRD maps Must requirements to acceptance criteria and validation.
- [ ] Implementation plan has phase exit criteria.
- [ ] Deferred work is visible and not required by the first implementation phase.

## Implementation Checklist

- [ ] Packet moved to `docs/llms/plcs/in-progress/<feature-slug>/`.
- [ ] Current phase is named before code changes start.
- [ ] SRD/SADD updated when implementation changes the plan.
- [ ] Validation evidence recorded as phases complete.
- [ ] Obsolete or duplicate code is removed as replacement work lands.
- [ ] Completed or deferred requirements are marked in the implementation plan.
- [ ] Final review title uses a concise outcome-oriented subject.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| <YYYY-MM-DD> | <command, test, review, or inspection> | <Pass/fail/blocked> | <Notes> |

## Completion Notes

Record the final implementation summary, validation evidence, known residual
risks, deferred work, and follow-up links before moving the packet to
`completed/`.
