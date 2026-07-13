# Archidekt Decks, Folders, Snapshots, And Synchronization PLC Packet

## Lifecycle

- Status: Completed
- Folder: `docs/llms/plcs/completed/archidekt-deck-sync/`
- Owner: mtg-mcp
- Created: 2026-07-03
- Last updated: 2026-07-04
- Current phase: Completed

## Summary

This packet defines an isolated Archidekt adapter for deck lifecycle, folder
organization, named snapshots, snapshot restoration, and explicit
synchronization. Local decks remain authoritative. Every pull, push, or
snapshot restore is previewed against fresh remote evidence and guarded by
local revision, remote fingerprint, source fingerprint, and preview
fingerprint. The adapter never writes through ordinary local edits.

## Dependencies

- [Local Deck Store](../../completed/local-deck-store/README.md)
- [Manual Deck Interchange](../manual-deck-interchange/README.md)
- [Scryfall Corpus And Evidence](../../completed/scryfall-corpus-and-evidence/README.md)
- [MCP Capability Toolsets](../../completed/mcp-capability-toolsets/README.md)
- [Rewrite program](../evidence-first-mcp-rewrite-program/README.md)

## Decisions

| Decision | Status | Rationale |
| --- | --- | --- |
| Use Archidekt's currently available web API as an observed, replaceable contract. | Accepted | There is no stable public specification; adapter drift is normal maintenance rather than a compatibility promise. |
| Keep local editing and remote mutation separate. | Implemented | Users see the diff before provider changes. |
| Use fresh canonical remote fingerprints rather than assumed ETags. | Implemented | The observed contract does not guarantee concurrency validators. |
| Refuse automatic conflict resolution. | Implemented | The MCP exposes differences, not chosen winners. |
| Default newly created remote decks to private. | Implemented | Least exposure for remote writes. |
| Include folder organization and named snapshot lifecycle/restore. | Implemented | These are explicit Archidekt workflow operations and the repository owner placed them in stable cutover scope. |
| Exclude automatic activity logs/recent-change history, packages, deck tags, collaboration, social, and account administration. | Implemented | They are separate provider surfaces and are not required for the requested folder/snapshot workflows. |

## Public Surface

Reads/previews: `archidekt_auth_status`, `archidekt_deck_list`,
`archidekt_deck_get`, `archidekt_sync_diff`, `archidekt_pull_preview`,
`archidekt_push_preview`, `archidekt_folder_list`, `archidekt_folder_get`,
`archidekt_snapshot_list`, `archidekt_snapshot_get`, and
`archidekt_snapshot_restore_preview`.

Local apply: `archidekt_pull_apply`.

Remote apply: `archidekt_deck_create`, `archidekt_deck_delete`,
`archidekt_push_apply`, `archidekt_folder_create`,
`archidekt_folder_update`, `archidekt_folder_move_items`,
`archidekt_folder_delete`, `archidekt_snapshot_create`,
`archidekt_snapshot_update`, `archidekt_snapshot_delete`, and
`archidekt_snapshot_restore_apply`.

## Provider Contract And Risk Acceptance

- Risk: authenticated deck, folder, and snapshot mutations use Archidekt's
  observed web API without a stable public specification. Archidekt's terms
  restrict automated requests, while current staff guidance discusses API use
  below the provider's request threshold.
- Decision: use the available API for explicit operations on the configured
  user's own decks, identify the client honestly, pace below current staff
  guidance, and update or disable the adapter when the contract changes.
- Status: Accepted for planning
- Accepted by: Nick Curry, repository owner
- Acceptance date: 2026-07-03
- Reviewed evidence: [Archidekt terms](https://archidekt.com/terms), current
  [staff rate-limit guidance](https://archidekt.com/forum/thread/19112643),
  current frontend routes, and the live probe below.

This acceptance resolved the product-boundary question. The later Planning
Approval separately approved this PLC and authorized its implementation.

## 2026-07-03 Contract Probe

Using the existing configured credential file, a redacted live probe:

1. authenticated successfully;
2. created a uniquely named private empty Commander deck with
   `POST /api/decks/v2/` (`201`);
3. read it back and verified its name and private flag (`200`);
4. deleted it with `DELETE /api/decks/{id}/` (`204`); and
5. verified that no probe deck remained in the authenticated deck listing.

A direct read of the deleted ID returned `400`, not `404`. Tests and runtime
classification must preserve that observed provider behavior and verify
absence through a fresh list/read contract rather than assuming REST status
semantics. No credential value, token, path, or persistent remote identifier is
part of the retained evidence.

## 2026-07-03 Folder And Snapshot Contract Inspection

The current public frontend exposes folder tree/detail, create, guarded item
update/move, and item-delete operations. It also exposes named-snapshot
list/get/create/update/delete operations. Snapshot restoration is composed by
fetching the exact saved deck state and applying the ordinary deck-overwrite
workflow. The PLC rebuilds those outcomes behind narrower contracts; it does
not copy the frontend service or legacy gateway abstractions.

Implementation must re-verify these routes before coding, capture sanitized
fixtures, and prove disposable cleanup. Missing folder deletion or snapshot
cleanup blocks these stable capabilities rather than permitting a live test to
leave remote state.

The completed adapter observation is retained in
[CONTRACT_ACCEPTANCE.md](CONTRACT_ACCEPTANCE.md). It records the owned-list v3
route, direct-root folder shape, mass-update envelope, name-only snapshot
update, snapshot restore exclusions, production pacing, and verified cleanup
without retaining credentials, account identity, remote IDs, or raw payloads.

## Planning Approval

- Status: Approved
- Reviewed by: Nick Curry, repository owner
- Review date: 2026-07-04
- Reviewed revision: `e256a37`
- Implementation authorized: Yes
- Authorization: Explicit request to implement this PLC with strong
  rate-limit handling, followed by PLC-completeness and audit passes.

## Guardrail Conformance

The adapter translates provider facts and performs explicitly authorized
workflow operations. It does not recommend changes, infer categories, or claim
atomic rollback when Archidekt partially accepts a multi-request update.

## Toolset And North-Star Acceptance

- Toolset: `archidekt`, disabled by default and explicitly enabled by users who
  need this provider.
- Surface rule: each tool exposes a distinct provider read, preview, or guarded
  operation. Folder and snapshot catalogs are not duplicated by generic
  provider routers or aliases.
- User question answered: what is the current Archidekt deck, folder, snapshot,
  or synchronization state, and what exact operation would change it?
- Evidence type: fresh provider observations, canonical fingerprints, explicit
  diffs, and verified operation outcomes.
- Replay boundary: local revision, provider fingerprint, source fingerprint,
  preview fingerprint, retrieval metadata, and fixture contract identify the
  evidence used for each guarded operation.
- Unknown boundary: auth absence, contract drift, stale state, conflict,
  partial acceptance, and unverifiable cleanup remain explicit.
- Decision boundary: the adapter does not select a conflict winner, category,
  folder, snapshot, or deckbuilding change.
- Complete LLM workflow: enable Archidekt, inspect auth and fresh provider
  state, preview a pull/push/restore or exact lifecycle operation, obtain user
  authority, apply it in the required mode, and verify the result.

## Validation Evidence

| Date | Check | Result | Notes |
| --- | --- | --- | --- |
| 2026-07-03 | Current folder/snapshot contract inspection | Passed for draft | The public frontend exposes folder tree/detail/create/update/move/item-delete and snapshot list/get/create/update/delete plus composed restore; implementation must re-verify and sanitize fixtures. |
| 2026-07-03 | Requirement and fixture traceability | Passed | All 29 `ARCH-*` requirements map to objective fixtures/checks. |
| 2026-07-03 | MCP surface and cutover reconciliation | Superseded by AMEND-003 | The Archidekt family remains 23 exact tools with 11/12/23 mode visibility; program totals now account for the merged interchange catalog and capability toolsets. |
| 2026-07-04 | Toolset and north-star reconciliation | Passed for amended draft | The entire family belongs to the opt-in `archidekt` toolset; relevance selection cannot widen operation-mode authority. |
| 2026-07-04 | Offline implementation and exact surface | Passed | All 23 tools, 11/12/23 mode visibility, schema-v4 capability metadata, fake HTTP, SQLite composition, request-budget, and operation-mode tests pass. |
| 2026-07-04 | Disposable authenticated lifecycle | Passed | Private deck/folder/snapshot create, read, rename, move, root move, restore, delete, and residual cleanup passed under production pacing; see `CONTRACT_ACCEPTANCE.md`. |
| 2026-07-04 | Per-assembly coverage | Passed | `MtgMcp.Archidekt` is above the 90 percent line gate and the coverage verifier includes the assembly. |
| 2026-07-04 | Audit suite | Passed | Abstraction, code quality, dead code, test coverage/quality, visual readability, dependencies, docs sync, and local-CI audits found no unresolved issue after fixes. |
| 2026-07-04 | Full repository gates | Passed | Lint, 287 non-live tests, exact surface, 91.01 percent Archidekt line coverage, package, process/MCP/installed-tool smokes, and dependency scans pass. |
| 2026-07-04 | Final production-paced live rerun | Passed | The complete private deck/folder/snapshot lifecycle passed in 1 minute 58 seconds and verified cleanup. |
| 2026-07-04 | Documentation validation | Passed | Local links resolve, Markdown fences balance, and `git diff --check` reports no whitespace errors. |
