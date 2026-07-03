# Legacy Surface Audit And Disposition Fixtures And Inventories

## Inventory Sources

| ID | Source | Purpose |
| --- | --- | --- |
| FIX-SURFACE-TOOLS | `src/MtgMcp.App` MCP tool attributes and `ToolRegistry` | Exact 118-tool inventory. |
| FIX-SURFACE-RESOURCES | `MtgResources.cs` | Exact 16-resource inventory. |
| FIX-SURFACE-PROMPTS | `MtgPrompts.cs` | Exact 18-prompt inventory. |
| FIX-PROJECTS | Production project files and host DI registration | Adapter and module ownership. |
| FIX-PERSISTENCE | Core JSON repositories, corpus cache, and options | Local state inventory. |
| FIX-GAPS | Task definitions, tests, and existing repair PLCs | Trust/correctness findings. |
| FIX-PLC-DISPOSITION | Planned, partial, completed, and ordinary plan packets | Rewrite overlap, supersession, and blocker inventory. |

## Complete Tool Disposition

Every registered tool appears below exactly once. “Rebuild later” means the
outcome is outside the `0.9.0` cutover but remains a registered future topic.

| Current tools | Disposition | Target |
| --- | --- | --- |
| `server_get_info` | Remove | Standard MCP initialization plus the foundation capability resource make the legacy tool redundant. |
| `card_search`, `card_get`, `card_get_batch`, `card_get_image`, `card_get_prints`, `card_get_rulings` | Rebuild | Immutable Scryfall snapshots. |
| `card_facets_get`, `card_facets_explain_match`, `card_facets_set_annotations`, `deck_facets_get`, `deck_facets_count` | Remove | Source tags remain evidence; inferred facet system is not stable scope. |
| `card_classify_win_routes` | Misleading/remove | Parser classification is not a source fact. |
| `collection_set`, `collection_get`, `collection_diff_workspace` | Remove | Collection management is outside the rewrite target. |
| `workspace_start`, `workspace_list`, `workspace_open` | Rebuild | `deck_*` local store lifecycle. |
| `workspace_parse_decklist`, `workspace_export` | Rebuild | Manual deck interchange. |
| `workspace_refresh_from_source`, `workspace_reopen_with_writeback`, `workspace_diff`, `workspace_diff_last_import` | Rebuild | Explicit provider pull/diff/push. |
| `workspace_validate` | Rebuild | Structural deck validation only. |
| `workspace_validate_legality` | Unsupported/remove | Full format legality is rules-engine scope. |
| `workspace_checkpoint_create`, `workspace_checkpoint_delete`, `workspace_checkpoint_get`, `workspace_checkpoint_list`, `workspace_checkpoint_restore` | Remove | Revisioned local store replaces workflow checkpoints. |
| `deck_add_card`, `deck_add_cards_bulk`, `deck_remove_card`, `deck_set_card_quantity`, `deck_move_card`, `deck_move_cards_bulk`, `deck_update_metadata` | Rebuild | Transactional `deck_*` mutations. |
| `deck_add_card_category`, `deck_list_cards_by_category`, `deck_update_card_categories_bulk`, `deck_remove_card_category`, `deck_set_primary_card_category`, `deck_create_category`, `deck_rename_category`, `deck_delete_category` | Rebuild | Local category model and mutations. |
| `deck_list_cards_by_zone` | Rebuild | Local zone query. |
| `deck_refresh_card_metadata` | Rebuild | Explicit Scryfall identity/snapshot workflow. |
| `deck_summarize`, `deck_analyze_structure` | Rebuild | Exact deck composition summaries. |
| `deck_analyze_draw_odds`, `deck_analyze_land_drop_odds` | Rebuild | Exact statistics. |
| `deck_analyze_cost` | Rebuild later | Price evidence without recommendations. |
| `deck_explain_role_counts`, `deck_review_weak_spots`, `deck_analyze_mana`, `deck_analyze_consistency`, `deck_analyze_best_practices`, `deck_estimate_commander_bracket`, `deck_re_evaluate`, `deck_compare_workspaces_analysis` | Misleading/remove | Mix parser-derived roles, heuristics, and judgments. |
| `deck_analyze_combos`, `combo_search_by_card`, `combo_get_details`, `wincon_find_payoffs` | Experimental | Future explicit combo-evidence scope, not stable cutover. |
| `deck_analyze_commander_trends`, `deck_find_lesser_known_cards`, `deck_find_exemplar_decks` | Rebuild later | Post-cutover popularity evidence. |
| `deck_simulate_goldfish`, `deck_compare_goldfish`, `archidekt_compare_goldfish`, `deck_project_board_state`, `deck_estimate_win_turn`, `deck_analyze_performance`, `deck_plan_compare_performance` | Experimental | Separate goldfish/rules feasibility PLC. |
| `deck_intent_get`, `deck_intent_suggest`, `deck_intent_set`, `deck_intent_clear` | Remove | Intent belongs to the calling LLM and client context. |
| `deck_plan_create`, `deck_plan_preview`, `deck_preview_card_package`, `deck_plan_list`, `deck_plan_get`, `deck_plan_clone`, `deck_plan_delete`, `deck_plan_apply` | Remove | Direct explicit deck mutations replace MCP-owned decision plans. |
| `commander_get_aggregate_cards` | Rebuild later | Post-cutover popularity evidence. |
| `commander_get_tags` | Misleading/rebuild | Replace theme/catalog behavior with exact cached Tagger assignments. |
| `commander_get_win_condition_evidence`, `commander_search_candidates`, `deck_review_new_card_swaps`, `deck_query_cards`, `deck_evaluate_card`, `deck_batch_tuning_report`, `deck_score_cards_for_playgroup_meta` | Remove | Candidate selection, evaluation, and scoring belong to the LLM. |
| `source_explain_card_signal`, `source_search_evidence` | Rebuild later | Provider-specific post-cutover evidence, without blending. |
| `source_list` | Rebuild | Server capability/source status. |
| `playgroup_get_auth_status`, `playgroup_get`, `playgroup_get_deck`, `playgroup_list_observed_decks`, `playgroup_list_observed_users`, `playgroup_list_user_decks` | Rebuild | Complete documented Playgroup public API. |
| `playgroup_rank_decks` | Misleading/rebuild | Preserve official raw fields; remove estimated-power blending. |
| `archidekt_copy_workspace`, `archidekt_create_deck`, `archidekt_list_decks` | Rebuild | Essential Archidekt deck lifecycle and sync. |
| `archidekt_create_folder`, `archidekt_list_folders`, `archidekt_move_decks` | Rebuild | Preserve folder-management outcomes behind the reviewed folder tree/detail, guarded mutation, and cleanup contracts; do not reuse the legacy wrappers. |
| `archidekt_checkpoint_create`, `archidekt_checkpoint_delete`, `archidekt_checkpoint_get`, `archidekt_checkpoint_list`, `archidekt_checkpoint_rename` | Rebuild | Preserve named-snapshot lifecycle evidence and add guarded restore preview/apply under the Archidekt child; do not carry the legacy checkpoint abstraction forward. |

## Complete Resource Disposition

| Current resources | Disposition |
| --- | --- |
| `mtg://workspaces`, `mtg://workspace/{workspaceId}`, `mtg://workspace/{workspaceId}/summary`, `mtg://workspace/{workspaceId}/state` | Rebuild as local deck resources only if a child proves resources add value beyond tools. |
| `mtg://workspace/{workspaceId}/intent`, `mtg://workspace/{workspaceId}/assistant-context`, `mtg://usage/deck-intent` | Remove with intent/advisor state. |
| `mtg://scryfall/syntax-cheatsheet`, `mtg://formats/{format}/deck-rules`, `mtg://usage/workspace-selection`, `mtg://usage/simulation-tool-selection`, `mtg://usage/operation-modes` | Remove from runtime; retain ordinary documentation where useful. |
| `mtg://config/effective` | Remove; it risks exposing implementation configuration and paths. |
| `mtg://sources/status`, `mtg://server/info`, `mtg://providers/{provider}/auth-status` | Fold source/server status into `mtg://server/capabilities`; expose provider-specific redacted auth status through the approved provider tools rather than additional resources. |

## Complete Prompt Disposition

All 18 prompts are removed from the stable rewrite:

`brew_commander_deck`, `tune_existing_deck`, `iterative_deck_review`,
`research_commander_common_cards`, `research_commander_win_conditions`,
`reduce_deck_cost`, `upgrade_deck_power`, `reduce_deck_power`,
`lower_commander_bracket`, `optimize_mana_base`,
`improve_deck_consistency`, `tune_for_local_meta`,
`review_new_card_swaps`, `check_land_drop_risk`,
`find_missing_combo_pieces`, `goldfish_deck`, `improve_deck_for_goal`, and
`rules_and_rulings_check`.

Their procedural text may be historical test evidence, but none is a runtime
surface or production abstraction in `0.9.0`.

## Persistence And Workflow Inventory

| Current state/workflow | Location | Disposition |
| --- | --- | --- |
| Workspace JSON | `workspaces/*.json` | Replace with `decks.db`; no automatic migration. |
| Edit-plan JSON | `plans/*.json` | Remove with plan subsystem. |
| Collection JSON | `collection/*.json` | Remove from stable scope. |
| Corpus JSON cache | `corpus-cache/*.json` | Replace with independently owned Scryfall and Tagger databases. |
| External simulation profiles | configured JSON paths | Remove from stable cutover; experimental only. |
| Hosted background work | none registered | Preserve explicit call-driven acquisition. |

## Known Defect And Evidence Matrix

| Area | Current evidence | Classification |
| --- | --- | --- |
| Live providers | No `Category=Live` test despite task filter. | Unsupported verification claim |
| Tagger | Curated tag catalog and Scryfall `otag:` searches, no per-card acquisition. | Misleading relative to target |
| Deck counts | Existing `deck-count-contracts` PLC documents inconsistent partitions. | Known correctness gap |
| Card snapshots | Existing `card-snapshot-integrity` PLC documents incomplete root/face coverage. | Known correctness gap |
| Land entry | Existing `land-entry-classification` PLC documents missed conditional wording. | Known correctness gap |
| Profile evidence | Existing `simulation-profile-evidence` PLC documents category/tag/route defects. | Known correctness gap |
| Interaction readiness | Existing `stats-lab-interaction-readiness` PLC documents timing blind spots. | Known model gap |
| Goldfish | Existing `conservative-goldfish-v2` PLC documents optimistic and duplicated kernels. | Experimental/known model gap |
| Playgroup ranking | Provider observations and local heuristic estimates share one ranking tool. | Misleading evidence boundary |
| Moxfield | Automated unofficial adapter. | Unsupported stable integration |

## Existing PLC Disposition Matrix

No row authorizes moving or editing another PLC. Approval of this audit allows
the foundation child to use the matrix as input and to propose lifecycle moves
in its own implementation change.

| PLC or plan | Topic owner | Disposition | Target | Action at audit approval | Blocking? |
| --- | --- | --- | --- | --- | --- |
| `docs/llms/plcs/planned/card-snapshot-integrity/` | `scryfall-evidence-snapshots` | Superseded/reference-only | Child 5 | Annotate cross-link only | No |
| `docs/llms/plcs/planned/deck-count-contracts/` | `local-deck-store`, `exact-deck-statistics` | Superseded | Children 3 and 8 | Annotate cross-link only | No |
| `docs/llms/plcs/planned/land-entry-classification/` | `exact-deck-statistics` | Reference-only | Child 8 | Annotate cross-link only | No |
| `docs/llms/plcs/planned/simulation-profile-evidence/` | `experimental-goldfish-feasibility` | Superseded/post-cutover | Post-cutover | Annotate cross-link only | No |
| `docs/llms/plcs/planned/stats-lab-interaction-readiness/` | `experimental-goldfish-feasibility` | Superseded/post-cutover | Post-cutover | Annotate cross-link only | No |
| `docs/llms/plcs/planned/conservative-goldfish-v2/` | `experimental-goldfish-feasibility` | Superseded/post-cutover | Post-cutover | Annotate cross-link only | No |
| `docs/llms/plcs/planned/mcp-trust-evidence/` | rewrite foundation and provider children | Absorb into child | Children 2, 5, 8, and 9 | Annotate cross-link only | No |
| `docs/llms/plcs/planned/provider-evidence-workflows/` | provider children | Absorb into child | Children 5 through 7 and 9 | Annotate cross-link only | No |
| `docs/llms/plcs/planned/configurable-decision-models/` | none for stable rewrite | Post-cutover/superseded | Post-cutover experimental review | Annotate cross-link only | No |
| `docs/llms/plcs/planned/rewrite-provider-adapters/` | provider children | Absent on disk; stale reference only | Children 5 through 7 and 9 own the topic | No artifact to move or annotate | No; resolved by 2026-07-03 filesystem inspection |
| `docs/llms/plcs/planned/rewrite-analysis-simulation/` | `exact-deck-statistics` and post-cutover simulation | Absent on disk; stale reference only | Child 8 and post-cutover own the topic | No artifact to move or annotate | No; resolved by 2026-07-03 filesystem inspection |
| `docs/llms/plans/jasmine-analysis-repair-roadmap.md` | none for stable rewrite | Superseded as delivery roadmap/reference-only | Existing defect evidence | Annotate cross-link only | No |
| `docs/llms/plcs/completed/agent-quality-foundation/` | repository guidance | Retain completed as historical evidence | Infrastructure guidance | Leave completed; child 2 replaces `plan`/`apply` under the accepted `read-only`/`local`/`remote` clean-break guardrail | No; any alternative requires an umbrella amendment |

The default stance is that Jasmine repair, goldfish, Stats Lab, and
decision-model packets are not implemented on legacy `main` before the rewrite
branch unless the repository owner explicitly grants an exception. Any
`implement-on-legacy` exception must name its owner, reason, validation gate,
and relationship to the rewrite branch.

## Audit Approval Checklist

- [x] All 118 registered tool names reconcile exactly once against the grouped
  disposition table.
- [x] All 16 resources and 18 prompts have a disposition.
- [x] The two stale partial-rewrite directory references are confirmed absent
  and non-blocking.
- [x] `task test:live` is retained but makes no verification claim until
  provider children add discoverable `Category=Live` tests.
- [x] Commander Spellbook runtime code is outside the stable rewrite while
  sanitized payload/query vectors remain eligible fixture evidence.
- [x] The older `plan`/`apply` mode decision is historical and intentionally
  superseded by the accepted clean-break program guardrail.

## Acceptance Matrix

| Requirement | Evidence | Expected result |
| --- | --- | --- |
| AUD-001, AUD-004 | Complete surface tables | Every registered name appears once. |
| AUD-002 | Provider disposition table | All production projects are accounted for. |
| AUD-003 | Persistence inventory | Every durable state path and background behavior is named. |
| AUD-005 | Defect matrix | Trust gaps are concrete and not generalized beyond evidence. |
| AUD-006, AUD-007 | README allowlists | Foundation can distinguish deletion from fixture review. |
| AUD-008 | Prompt and judgment dispositions | Stable rewrite makes no deckbuilding decision. |
| AUD-009 | Validation notes and blocked surface-report evidence | Static evidence and the build-lock limitation remain explicit. |
| AUD-010, AUD-011 | Existing PLC disposition matrix | Overlapping plans have an owner, action, and blocking-conflict result. |
