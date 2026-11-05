# Implementation Plans

Use this directory for durable implementation plans that should travel with the
repository.

Prefer no committed plan for tiny single-area fixes. Use an ordinary plan when
the work benefits from shared context but does not need a full PLC packet.

Keep each plan in the lifecycle folder that matches its status: `planned/`,
`in-progress/`, `completed/` (finished or historical records), or `archived/`
(retired, abandoned, or superseded).

Plans follow the shared PLC template: a one- or two-sentence summary of the
problem or opportunity under the title, then `## Current Work`, `## Phases`,
`## Done when`, `## Open questions`, and `## Log` last. Implementation order
goes in the Phases table, and validation commands go in each Done when check.
Before the Log, plans also include:

- Goals and non-goals.
- Affected projects, public tools and APIs, docs, and tests.
- Risks, assumptions, and deferred work.

Use ignored `/plans/` for local scratch.

Start a durable ordinary plan from
[`../templates/IMPLEMENTATION_PLAN.md`](../templates/IMPLEMENTATION_PLAN.md).
