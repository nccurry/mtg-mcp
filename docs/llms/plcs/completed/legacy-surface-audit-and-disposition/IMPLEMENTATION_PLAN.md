# Legacy Surface Audit And Disposition Implementation Plan

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Last updated: 2026-07-03
- Related SRD: [SRD.md](SRD.md)
- Related SADD: [SADD.md](SADD.md)

## Strategy

This packet's implementation is review and handoff only. It never edits
production code. The foundation PLC consumes the approved disposition and must
name every deletion it performs.

## Phases

| Phase | Goal | Exit criteria | Status |
| --- | --- | --- | --- |
| 1 | Capture registered surface and project inventory. | Counts and names reconcile with source registration. | Completed |
| 2 | Trace persistence, adapters, workflows, and tests. | State owners and dynamic entry points are recorded. | Completed |
| 3 | Classify future disposition and trust gaps. | Every group has one disposition and evidence. | Completed |
| 4 | Classify overlapping PLCs and ordinary plans. | Every listed packet has an owner, disposition, action, and blocker result. | Completed |
| 5 | Review deletion/reuse allowlists and PLC dispositions. | Repository owner approves or requests changes. | Completed |
| 6 | Hand off to foundation planning. | Approved packet is linked as a prerequisite. | Completed |

## Review Procedure

1. Re-run the source attribute inventory if MCP registration changes.
2. Check every exact name in FIXTURES against `ToolRegistry`, resources, and
   prompts.
3. Challenge every `remove` and `misleading` classification with production,
   external-compatibility, and fixture evidence.
4. Challenge every overlapping PLC disposition before moving, editing, or
   superseding that packet in a later foundation change.
5. Record changes in this packet; do not delete code or move PLCs during audit
   review.
6. Approve the packet only when the foundation author can act without guessing.

## Rollback And Cleanup

Because this is documentation only, rollback removes the packet and resets the
umbrella registry. It does not require runtime or data rollback.

## Completion Criteria

- [x] All AUD requirements pass.
- [x] Exact surface counts and names reconcile.
- [x] Deletion, reuse, and PLC disposition allowlists are approved.
- [x] Foundation PLC links this packet.
- [x] No production file changed.
