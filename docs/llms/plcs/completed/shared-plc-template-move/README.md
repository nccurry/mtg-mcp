# Shared PLC Template Move

Convert the active PLC packets and the ordinary-plan template to the shared PLC
template, format only: lines move or take the listed format edits, and no
requirement or design text is reworded.

## Current Work

Historical record as of `5e65587`; the code is the current truth.

- Status: Complete
- Owner: @nccurry
- Last updated: 2026-11-05, `plc-template-followup` at `5e65587`
- Next action: None
- Blocker: None
- Depends on: None
- Current state: every active packet and plan is in the shared template; the
  strict format check of `docs/llms/plcs` and `docs/llms/plans` prints nothing.
- Resume check: `python3 <plc-planning skill>/scripts/format_check.py docs/llms/plcs docs/llms/plans --people nccurry=nccurry --strict`
  prints nothing.

Where the work lives:

| Checkout | Branch | Owner | State |
| --- | --- | --- | --- |
| None | | | |

## Phases

| Phase | Name | Owner | State |
| --- | --- | --- | --- |
| P1 | Template and planning guides | | Verified — a filled copy of the ordinary-plan template passes the format check with `--strict` @ 75fedb9 |
| P2 | Active packets in the shared template | | Verified — the strict format check of `docs/llms/plcs` and `docs/llms/plans` prints nothing @ 5e65587 |

- P1: `docs/llms/templates/IMPLEMENTATION_PLAN.md` takes the shared sections,
  with Goals And Non-Goals, Affected Areas, Risks And Assumptions, and the
  Completion checklist kept before the Log; its Implementation Order table
  becomes the Phases table. The plans, PLC, agent-workflow,
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

## Log

- 2026-11-05 Created.
- 2026-11-05 Decision: owner answered the conversion cases; applied, two questions left open.
- 2026-11-05 Merged to `main` at `75fedb9`.
- 2026-11-05 Decision: owner had a summary written for `deckbuilding-agent-guidance` from its Outcome section.
- 2026-11-05 Decision: the design seed `plans/planned/local-scryfall-cohort-analysis.md` stays without `## Done when` until it has requirements.
- 2026-11-05 Complete; moved to `completed/`.
