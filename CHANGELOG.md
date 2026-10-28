# Changelog

## Unreleased

- Changed failed tool calls to set the MCP `isError` flag. Before this, every
  failure looked like a success to a client. Migration: clients that treated
  any non-error result as success should check `isError` and then the
  structured `kind` (`success` is the only kind without the flag). The
  structured body and text block are unchanged.
- Fixed input the server cannot read: it now returns `kind: "invalid-input"`,
  reason `invalid-arguments`, and a top-level `path` to the rejected value
  (for example `changes[0].entryDraft.quantity`), instead of a generic
  "An error occurred invoking" message. An unknown union `kind` lists the
  allowed kinds.
- Fixed `deck_apply_changes` and every other union input to accept the `kind`
  property in any position.
- Added `current` (the current local revision, remote fingerprint, or both) to
  stale-state `conflict` results from deck, statistics, Spellbook, on-curve,
  identity, categorization, and Archidekt operations, so a caller can preview
  again and retry.
- Fixed Archidekt push previews counting a reordered list of an entry's
  secondary categories as a change; the order is now ignored.
- Fixed Archidekt push apply so entries and categories it creates are linked in
  the new baseline; the next push preview plans only real changes. An entry
  that cannot be matched exactly is listed in the new `unlinkedLocalIds`
  field of the push result, and the next push removes and re-adds that row.
- Added `responseFormat` (`concise`, `standard`, `full`) and `fields` to
  `scryfall_search`, `scryfall_card_get`, `scryfall_card_collection`, and
  `scryfall_cards_by_tag`. The default is `standard`, which keeps every field
  of the old output. `concise` returns a small set of fields for comparing many
  cards (about 16 percent of the bytes of `standard` in the test fixture);
  `fields` picks other concise fields by name, including extra prices,
  `legalitiesAll`, `games`, `reserved`, and `layout`. A `concise` page is cut
  to stay under 40,000 bytes, then sets `truncated`, `narrow`, and a
  `nextCursor` for the rest. In `concise`, `tags` (official oracle tag slugs)
  and `cheapestPriceUsd` (lowest nonfoil USD price across installed printings)
  come from installed card data and are `null`, with the reason in `limits`,
  when it is not installed. Results now also carry `responseFormat`.
- Breaking: removed `includeRaw` from those four tools. Migration: send
  `responseFormat: "full"` instead of `includeRaw: true`; it returns the same
  raw provider objects with the same 25-row page cap. `includeRaw` on
  `scryfall_card_prints`, `scryfall_card_rulings`, `scryfall_sets`,
  `scryfall_snapshot_get`, and `scryfall_tag_search` is unchanged.
- Added fields to every `standard` card: `rarity`, `power`, `toughness`,
  `loyalty`, `layout`, `games`, `reserved`, and `gameChanger` (`null` when
  Scryfall omits it). Each card face also gains `power`, `toughness`, and
  `loyalty`.
- Added `include` (`cards`, `tags`), `fields`, `cursor`, and `pageSize` to
  `deck_get`. With `include`, the result holds a page of entries (25 by
  default, at most 100, cut to stay under 40,000 bytes) with
  `cardData` (`installed`, `not-installed`, or `unresolved`), concise `card`
  fields, and oracle `tags` for each entry, plus `totalEntries` and
  `nextCursor`. Without `include` the result is the same as before.
- Changed tool schemas to publish a JSON Schema `enum` for parameters with a
  fixed set of values (cache policy, search order, interchange format IDs,
  Archidekt format and visibility, and others). Accepted values are unchanged.
- Changed descriptions: each `stats_*` tool lists the fields of `request` and
  one runnable example; `scryfall_search`, `scryfall_tag_search`, and
  `scryfall_cards_by_tag` explain tag filtering with `otag:`, and
  `scryfall_search` warns that a default page can exceed 40,000 characters.

## 0.10.0 - 2026-10-25

- Added the opt-in Commander Spellbook adapter with three read-only evidence
  tools: `spellbook_variant_search`, `spellbook_variant_get`, and
  `spellbook_deck_combos_find`. Each call makes at most one upstream request,
  is paced across processes that share a data root, and caches successful
  responses in `spellbook.db` for 15 minutes by default. Configure the cache
  with `--spellbook-ttl-minutes`, `MTGMCP__SPELLBOOK__TTL_MINUTES`, or
  `SPELLBOOK_TTL_MINUTES`.
- Changed the server to require MCP protocol `2026-07-28`. Older
  initialize-based clients are no longer supported.
- Fixed deck grouping from installed official Scryfall tags: exact source IDs,
  all-parent ancestry, strict rule validation, and an in-place `common-v1`
  correction.
- Fixed Archidekt sync to exclude new maybeboard cards from deck and price
  totals, keep category assignment order, and ignore volatile view counts in
  remote-state guards.
- Moved the toolchain to mise with reproducible package locks and SHA-pinned
  GitHub Actions, and updated Microsoft.Data.Sqlite, SQLitePCLRaw, and
  analyzers.
- Added the read-only `deck_on_curve_estimate` tool. It samples one bounded
  land-only cast-by-turn model for a saved deck using direct installed Scryfall
  facts and caller-supplied land rules. Results include source facts, replay
  details, uncertainty, miss counts, and clear limits; they do not recommend a
  deck change or predict real games.
- Added the Core-only `MtgMcp.OnCurve` calculation project, offline replay and
  MCP process tests, and the separate `task benchmark:oncurve` Release
  measurement for a fixed sanitized 99-card fixture.
- Fixed full Scryfall card-data imports to read current official bulk metadata
  and accept supported tag and ruling records that refer to a card row not
  present in the installed data.
- Fixed self-contained packages to include the native SQLite runtime. `task
  install` now starts a read-only Scryfall-enabled server after installation to
  catch packaged startup failures.
- Fixed the Task and release test filters for .NET 11 and xUnit MTP so a
  focused check cannot report success after running zero tests.

## 0.9.0 - 2026-07-12

- Removed the audited legacy product implementation from the isolated rewrite
  branch while retaining ordinary Git history and released artifacts.
- Reduced the production solution to dependency-light `MtgMcp.Core` and a
  minimal resources-only `MtgMcp.App` stdio server with no provider surface.
- Rebuilt focused unit, architecture, and process tests and reconciled task,
  CI, coverage, package, release, and smoke wiring with the foundation project
  set.
- Added closed operation-result and evidence-descriptor unions with stable,
  case-specific JSON contracts.
- Added `read-only`, `local`, and `remote` mode enforcement; JSON, environment,
  and command-line configuration; a non-creating `mtg-mcp/v0.9` data root; and
  sanitized legacy-data and startup boundaries.
- Added official MCP initialization and the deterministic
  `mtg://server/capabilities` resource with zero prompts and exact static
  capability-tool counts.
- Split the one-shot process probe from official-client MCP smoke coverage,
  added installed-package MCP validation, and made the App project version the
  default for package and release automation.
- Added immutable format-neutral deck contracts, revisioned SQLite persistence,
  stable pagination, transactional entry/category mutations, and
  provider-neutral synchronization bindings.
- Added guarded opaque deck backup creation, inventory, restore, rollback, and
  deletion with integrity, schema, and database-fingerprint checks.
- Added the exact nineteen-tool local `deck_*` surface, read-only mode filtering,
  official-client schema/annotation tests, and representative MCP workflows.
- Added four offline `deck_*` interchange tools for lossless native JSON,
  generic text, fingerprint-guarded import creation, and checksummed artifact
  bundles; dated UI acceptance makes Archidekt and Moxfield formats available
  with provider-applied and companion-only fields reported separately.
- Added static startup-selected capability toolsets with `default`, `all`,
  `none`, and explicit profiles, exact mode intersection, schema-v3 capability
  metadata, and no dynamic tool-list advertisement.
- Added `MtgMcp.Scryfall` with eighteen typed tools for official API evidence,
  exact-request snapshot replay, an explicit four-dataset card-data lifecycle, and
  separately labeled Oracle/art community tags in one `scryfall.db`.
- Added configurable 24-hour evidence reuse, current-plus-previous card-data
  generations, cross-process SQLite leases and conservative 500-millisecond
  pacing, fail-fast blocking responses, bounded retry, and offline/live
  acceptance suites.
- Added the opt-in 23-tool `archidekt` capability for fresh deck, folder, and
  named-snapshot evidence; guarded pull/push/restore; private deck lifecycle;
  and verified empty-folder/snapshot cleanup.
- Added strict redacted credential loading, two-second process-wide per-account
  pacing, a rolling 30-request/minute ceiling, composed 150-request tool
  budgets, bounded read retry, fail-fast `403`/`429`, and no ambiguous write
  retry. The provider origin is fixed to Archidekt. A disposable authenticated
  lifecycle verifies current provider routes and leaves no remote test state.
- Treat Archidekt's post-mutation category rank as provider-normalized evidence
  so snapshot restore does not report a false mismatch when the provider
  renumbers otherwise identical categories.
- Added content-addressed immutable snapshot payloads, schema-checksum
  validation, abandoned-staging recovery, alias and face-art tag joins,
  face-aware exact-name matching, and provider-timestamped price/rank freshness.
- Added compact normalized provider output by default, opt-in lossless raw
  objects, and compact snapshot members with stable ordinals and checksums.
- Expanded collection resolution to 150 ordered identities with local-first
  deduplication, atomic 75-identifier provider batching, and evidence-bound
  cursor pages that never reacquire during continuation.
- Added a bounded official-client Red/White Weenies workflow that proves local
  deck creation, structural validation, batch card resolution, evidence
  persistence, and immutable replay without a full card-data download.
- Added the opt-in 16-tool Playgroup Public API 1.0.0 capability: all 15
  documented operations plus redacted auth status, lossless provider evidence,
  explicit unsupported deck updates, exact 14/14/16 mode visibility, and no
  cross-provider ranking or hydration.
- Added fixed-origin bearer isolation, process-wide 250-millisecond pacing,
  bounded GET retry and `Retry-After` handling, single-attempt remote writes,
  exact OpenAPI drift checks, fixture-only write proof, and an opt-in `/me`
  live-read test that cannot mutate provider state.
- Added strict Playgroup API-key loading from the standard user-profile
  `.mtg-mcp/playgroup.json` file, with explicit configuration precedence,
  redacted failures, and deterministic test-process isolation.
- Bounded the provider's multi-megabyte all-commander turn-damage response and
  changed its MCP tool to return only the exact caller-selected commander row
  with full-source checksum evidence.
- Made Windows release cleanup fall back to extended-length filesystem paths
  for deeply nested installed-tool artifacts.
- Added exact-only, evidence-bound deck identity reconciliation preview/apply,
  raising the deck toolset to 25 tools and the complete surface to 47/69/82 by
  mode without adding legality or fuzzy-resolution behavior.
- Added eight default-enabled, read-only `stats_*` tools for exact
  hypergeometric and multivariate probability, explicit turn and mulligan
  schedules, caller-declared mana/package allocation, inverse counts, and
  deterministic deck summaries. The current surfaces are 30/51/51 for
  `default` and 55/77/90 for `all`.
- Added `MtgMcp.Statistics` as a BCL-only project with reduced `BigInteger`
  rationals, invariant 12-place displays, a request-wide work budget, and
  independent exhaustive combinatorial oracles. No format or legality rule,
  provider lookup, sampling fallback, or recommendation entered the surface.
- Replaced the flat batch-change input with an eleven-variant closed schema,
  described every public MCP input, and upgraded capability metadata to schema
  6 with separate implementation and credential states and no provider I/O.
- Split Scryfall card data, snapshots, storage, and pacing into clear owners, and
  Archidekt deck, folder, snapshot, transport, mapping, and synchronization
  ownership behind behavior-preserving public facades.
- Added three deterministic `deck_*` categorization tools with explicit inline
  rules or the transparent `common-v1` preset, evidence-bound preview/apply,
  and no inferred category meaning.
- Completed the stable surface at 28 deck, 18 Scryfall, 8 statistics, 23
  Archidekt, and 16 Playgroup tools. The default profile exposes 32/54/54 tools
  by mode; the complete profile exposes 57/80/93.
- Fixed Archidekt synchronization for provider-renumbered categories, explicit
  primary-category submission, category-array reordering, and duplicate
  provider relations while preserving multiplicity.
- Passed packaged method acceptance for all 93 tools with 88 live passes, two
  fixture-backed Scryfall lifecycle operations, one provider-generation-pending
  rollback, and two fixture-only Playgroup writes. Archidekt cleanup and zero
  Playgroup writes were verified.
- Replaced active legacy architecture, adapter, and toolset documentation with
  concise task-oriented `0.9.0` guidance.

## 0.8.0 - 2026-06-28

- Audited package boundaries, release metadata, docs, and local build coverage
  for the refactor release.
- Added MCP host observability with redacted per-tool completion logs,
  OpenTelemetry-ready tool-call metrics/traces, `logging/setLevel` handling,
  and `mcpLoggingLevel` in server diagnostics.
- Added report-only performance ratchet output for release review.
- Fixed local release smoke and install validation to use the repo-pinned .NET
  runtime when validating the packaged tool.
- Added observability, MCP compatibility, performance ratchet, and 1.0 readiness documentation.
- Replaced persisted deck edit operations and several closed-set statuses with
  typed union/enum models while preserving legacy JSON strings and flat plan
  operation payloads.
- Removed unsupported Reddit discussion and Spicerack source integrations from
  registration, CLI auth help, docs, and surface tests.
- Added MCP protocol conformance improvements: object-root typed tools now
  advertise titles and output schemas with structured content, tool validation
  errors return a coded structured payload, `workspace_list` and
  `deck_plan_list` use cursor-paged envelopes, and `mtg://workspaces` is
  discoverable as a resource.
- Unified MCP `detailLevel` parsing across presenters while keeping legacy
  `compact` and `includeWorkspace` inputs during the deprecation window.
- Added method-level MCP toolset and operation-mode filtering so deployments
  advertise only selected tools that can run in the configured mode.
- Added report-only MCP surface metrics, README surface parity coverage, a
  pre-1.0 versioning/deprecation policy.
- Documented the full registered MCP tool/resource/prompt surface in the README.
- Added explicit ramp-evaluator applicability metadata to `deck_evaluate_card`
  and labeled non-ramp cards with `not-applicable` instead of relying on a bare
  zero score.
- Added `RngKind` metadata to the heuristic goldfish result family, labeled as
  `system-random` to distinguish it from the Stats Lab deterministic RNG.
- Aligned default app assembly/package version metadata with `server.json` so
  local smoke output reports the intended pre-release version unless release
  tasks override it.
- Initial MCP server scaffold with Scryfall research, local workspaces, Archidekt writeback, checkpoints, prompts, resources, Taskfile workflows, and tests.
