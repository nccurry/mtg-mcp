# Shared PLC Template Move

Convert the active PLC packets and the ordinary-plan template to the shared PLC
template, format only: lines move or take the listed format edits, and no
requirement or design text is reworded.

## Current Work

- Status: In progress
- Owner: @nccurry
- Last updated: 2026-11-05, `plc-template-move` from `main` at `18c40c8`
- Next action: owner review of the branch and the open questions below
- Blocker: None
- Depends on: None
- Current state: both phases and the owner's decisions of 2026-11-05 are
  implemented on `plc-template-move`, not pushed; the format check prints
  only the open questions below.
- Resume check: `python3 <plc-planning skill>/scripts/format_check.py docs/llms/plcs --people nccurry=nccurry --strict`
  prints only the lines named in the open questions.

Where the work lives:

| Checkout | Branch | Owner | State |
| --- | --- | --- | --- |
| `../mtg-mcp-wt/plc-template/convert` | `plc-template-move` | @nccurry | waiting — owner review |

## Phases

| Phase | Name | Owner | State |
| --- | --- | --- | --- |
| P1 | Template and planning guides | | Implemented — `plc-template-move`, not merged |
| P2 | Active packets in the shared template | | Implemented — `plc-template-move`, not merged |

- P1: `docs/llms/templates/IMPLEMENTATION_PLAN.md` takes the shared sections,
  with Goals And Non-Goals, Affected Areas, and Risks And Assumptions kept
  before the Log; its Implementation Order table becomes the Phases table and
  its Completion checklist becomes Done when. The plans, PLC, agent-workflow,
  and `docs/llms` READMEs describe the shared shape.
- P1: ordinary plans and PLCs use `planned/`, `in-progress/`, `completed/`
  and `archived/`; abandoned or superseded work goes to `archived/` from now on.
- P2: `planned/deckbuilding-agent-guidance` gains a State column (Planned) and
  its `## Phases` section moves up under Current Work; both active packets gain
  a `## Log`.
- P2, owner decisions: both packets' Owner lines gain `(@nccurry)`; their
  requirements move whole under `## Done when`; `simulate-damage-rules` gets a
  summary from its first Outcome paragraph, a `## Phases` table from its
  numbered steps, and `Next action` for `Next action and blocker`. The four
  loose plans move to `plans/archived/`, `plans/completed/` (two) and
  `plans/planned/`, and the planned one is converted.

## Done when

- REQ-001: Every packet in `planned/` and `in-progress/` uses the shared
  template where its own text allows. Check: the format check of
  `docs/llms/plcs` prints only the lines the open questions name.
- REQ-002: The edits are format only. Check: the word check on the diff of
  `docs/llms/plcs` against `main`, without this plan, prints only expected
  review lines, and every removed line still exists in its packet.
- REQ-003: The ordinary-plan template and the planning guides describe the
  shared template. Check: a copy of the template with its placeholders filled
  passes the format check with `--strict`.

## Open questions

- Open question: `planned/deckbuilding-agent-guidance` has no summary under
  its title. The first paragraph of `## Outcome` ends in a colon that leads
  into a numbered list, so moving it alone would split one sentence. Write one
  or two sentences, or move the paragraph with its list? (blocks REQ-001;
  waits on @nccurry)
- Open question: `plans/planned/local-scryfall-cohort-analysis.md` has no
  requirements to put under `## Done when`; its "Required Future PLC Gates"
  are gates for a later PLC. Leave it without `## Done when` while it is a
  design seed? (blocks nothing; waits on @nccurry)

## Log`.

## Done when

- REQ-001: Every packet in `planned/` and `in-progress/` uses the shared
  template where its own text allows. Check: the format check of
  `docs/llms/plcs` prints only the lines the open questions name.
- REQ-002: The edits are format only. Check: the word check on the diff of
  `docs/llms/plcs` against `main`, without this plan, prints only expected
  review lines, and every removed line still exists in its packet.
- REQ-003: The ordinary-plan template and the planning guides describe the
  shared template. Check: a copy of the template with its placeholders filled
  passes the format check with `--strict`.

## Open questions

- Open question: `planned/deckbuilding-agent-guidance` and
  `planned/simulate-damage-rules` have no summary under the title and no
  `## Summary` (format check line 1). Write one or two sentences, or name the
  paragraph to move? (blocks REQ-001; waits on @nccurry)
- Open question: both packets' Owner lines (`mtg-mcp (owner session)` at
  deckbuilding-agent-guidance line 7, `mtg-mcp` at simulate-damage-rules line
  6) name no person. Which handle owns them, for example `@nccurry`? (blocks
  REQ-001; waits on @nccurry)
- Open question: neither packet has `## Done when` or SRD.md; their
  requirements sit under `## Requirements` and `## Outcome And Requirements`.
  Add `## Done when`, or grow SRD.md? (blocks REQ-001; waits on @nccurry)
- Open question: `planned/simulate-damage-rules` lists its phases as numbered
  steps under `## Steps And Verification`, with no `## Phases` table, and its
  Current Work joins `Next action and blocker` in one key. Make a Phases table
  and split the key? (blocks REQ-001; waits on @nccurry)
- Open question: the four plans in `docs/llms/plans/`
  (`jasmine-analysis-repair-roadmap.md` retired,
  `live-method-acceptance.md` historical, `local-scryfall-cohort-analysis.md`
  deferred, `rewrite-documentation-refresh.md` completed) sit outside
  lifecycle folders. Where do they go, or do they stay as records? (blocks
  nothing; waits on @nccurry)

## Log

- 2026-11-05 Created.
- 2026-11-05 Decision: owner answered the conversion cases; applied, two questions left open.
