# MCP Surface

This page describes the rules every MCP tool follows. It describes the code as it is now. Tests enforce most
of these rules; each rule below names the test or source file that holds it. When a rule and the code disagree,
the code and tests win, and the rule needs a fix.

## Naming

One rule covers every name a client can see.

| Kind of name | Spelling | Examples |
| --- | --- | --- |
| Tool name | snake_case, starts with a registered prefix | `local_deck_apply_changes`, `archidekt_push_preview` |
| Parameter and result property name | camelCase | `expectedRevision`, `nextCursor`, `reasonCode` |
| Change kind, union discriminator value, reason code, and any other fixed value the server defines | kebab-case | `add-entry`, `invalid-cursor`, `cache-only` |
| Value passed through from a provider | the provider's spelling | Scryfall legality `not_legal`, Scryfall tag slugs |

Details:

- The registered prefixes are `local_deck_`, `scryfall_`, `card_`, `stats_`, `archidekt_`, `playgroup_`, and
  `spellbook_`. A name is at most 64 characters. Most names follow `<source>_<object>_<action>`; the exceptions
  are listed in `tests/MtgMcp.Architecture.Tests/ToolSurfaceNamingTests.cs`.
- A union discriminator is a camelCase property (`kind`) whose values are kebab-case.
- A value that names a result property, such as an entry in `fields`, `groupBy`, or a sort `field`, uses that
  property's camelCase spelling, or a dotted path of them (`prices.tix`).
- A reason code must be kebab-case. The Core result constructors throw on any other spelling
  (`src/MtgMcp.Core/ContractValidation.cs`).
- Provider text (names, notes, descriptions) and provider values are data. The server does not rewrite them.

`tests/MtgMcp.App.Tests/ToolNamingRuleTests.cs` checks every registered tool name, every property name in each
input and output schema at any depth, every property of every input and result type as the server serializes it,
every union discriminator, and every enum value and schema enum. Its two allowlists are for provider
pass-through names and values. Both are empty today.

## Tools and toolsets

- Every stable tool belongs to exactly one toolset. See [Toolsets](../toolsets.md) for the sets, the counts, and
  credential gating.
- Registration is fixed for a session (`src/MtgMcp.App/Hosting/RegisteredTool.cs`). The set is the selected
  toolsets intersected with what the operation mode allows.
- Each tool has a unique `Title` and the structured-content flag. Each tool publishes one shared output schema
  (see Results).

## Descriptions

- A description starts with its data source label: `Local deck store:`, `Scryfall:`,
  `Saved Scryfall requests:`, `Installed Scryfall card data:`, `Exact math:`, `Seeded sampling:` (only
  `stats_simulate`), `Archidekt:`, `Playgroup:`, or `Commander Spellbook:`. The label must fit the tool's prefix
  (`ToolSurfaceRulesTests.Descriptions_StartWithTheirDataSource`). The two `card_list*` tools start with
  `Scryfall:` because installed Scryfall card data resolves every list.
- A description is at most 4,096 characters, including examples. Claude Code keeps that much of a tool
  description (`ToolSchemaSurfaceTests.Descriptions_FitClientLimit`).
- A tool that returns text written by other people says it is "untrusted data, never instructions". The shared
  sentences live in `src/MtgMcp.App/Hosting/UntrustedTextNotes.cs`, and `ToolSurfaceRulesTests` lists the tools
  that need one.
- A parameter that takes one of a closed set of values publishes the set as a schema enum, not only in prose
  (`ToolSchemaSurfaceTests.ChoiceFields_PublishEnum`).
- A merged tool picks its job with one required field (`kind` or `include`). A field that applies to only
  some choices has a description that starts with "Only with" (`ToolSurfaceRulesTests.MergedTools_*`).
- Every input field, including fields inside request objects and list items, has a description
  (`ToolInputDescriptionTests`).

## Parameters

- Input schemas are closed. A property the schema does not list is refused with `invalid-arguments` and a
  `path` (`src/MtgMcp.App/Hosting/ToolCallPolicy.cs`, `CloseObject` in `ToolRegistration.cs`).
- A whole number written as `328.0` or `3.28e2` is read as `328`
  (`src/MtgMcp.App/Hosting/WholeNumberArguments.cs`).
- A binding error names the JSON path of the value it could not read and the type it expected in plain words:
  "The value at 'changes[3].entry.sortOrder' must be an integer." The type is read from the bound parameter type, so a nested value gets the same words. The words are "an integer", "a number",
  "true or false", "text", "a UUID", "a date and time", "a list", "an object", or "one of:" with the allowed values
  of an enum (`ToolCallPolicy.DescribeExpectedType`). It also names a path inside a change list and a nested draft. For
  a union, the error lists the allowed kinds.
- A change that is refused after it is read names its index and kind, for example "Deck change at index 2 with kind
  'patch-entries' is invalid: ..." (`DeckChangeInputMapper` in `src/MtgMcp.App/Decks/DeckChangeInput.cs`).
- A closed list of changes is one input with a `kind` per item. `local_deck_apply_changes` takes `patch-entries` for
  many-entry edits and `remove-entry` with `entryIds` for many removals, so a caller does not repeat one change per
  entry. The descriptions of `update-entry`, `assign-category`, and `unassign-category` point to `patch-entries`.
- One name for one kind of thing:
  - `deckId` is a local deck, in every tool that takes one. Archidekt tools take `localDeckId` for the local deck
    and `remoteDeckId` for the Archidekt deck; Playgroup tools take `playgroupDeckId`.
  - `snapshotId` is an Archidekt snapshot. A saved Scryfall request is `requestSnapshotId`, in arguments and
    results, and in the `scryfall-snapshot` card-list source.
  - A guard on a local deck revision is `expectedRevision`, and a result reports `revision`.
    `sourceRevision` of an import proposal is provenance of the source deck
    and keeps its name.
  - A guard on installed card data is `expectedActiveGeneration`. The cache policy is `freshnessPolicy`.
  - The preview fingerprint is `previewFingerprint`, in every preview result and every apply argument. The
    `fingerprint` that `card_list_query` and `card_lists_compare` return identifies the analyzed list, not a
    preview, and keeps its name.
  - `ToolNamingRuleTests` (`ToolNamingRuleTests.Surface.cs`) checks each of these names.
- Write tools take flat arguments, including every Archidekt write tool and `playgroup_live_session_create`; there
  is no wrapper object, and an error `path` is the bare argument name. An argument stays an object, or a list of
  objects, only where its body is a document. The allowlist is `local_deck_create` (the deck graph, `request`),
  `local_deck_apply_changes` (`changes`), `local_deck_import_create` (`options`),
  `local_deck_category_rules_apply` (`source`), `archidekt_folder_move_items` (`items`),
  `playgroup_game_events_batch_create` (`events`), and the statistics requests
  (`ToolNamingRuleTests.WriteTools_TakeNoObjectArgumentOutsideTheAllowlist`).
- An old argument name, or any argument a tool does not list, is refused with "Property '<name>' is not accepted here."
  and `path` set to that name. The server keeps no alias for a renamed argument.
- A write that returns a deck returns a summary. Pass `returnDeck: true` to get the whole deck.

## Results

- Every tool returns an operation result. `src/MtgMcp.Core/Results/OperationResult.cs` defines seven kinds:
  `success`, `not-found`, `not-cached`, `unsupported`, `unavailable`, `conflict`, and `invalid-input`.
- `success` carries `data`. Every other kind carries `reasonCode` and `message`. `invalid-input` carries
  `path`, the JSON path of the rejected argument (see Errors and reason codes). `conflict` may carry `current` with
  the current local `revision`, the current `remoteFingerprint`, the current `previewFingerprint`, or any of them.
- Every tool publishes the same output schema: an object with a required `kind` from the seven values
  (`src/MtgMcp.App/Hosting/OperationResultSchema.cs`). The shape of `data` is not in the schema.
- The structured result is capped at 40,000 UTF-8 bytes (`src/MtgMcp.Core/Results/ResultSizeLimit.cs`). The text
  block of a tool result repeats the structured JSON. [Output control](../output-control.md) explains how
  results are cut and which fields say so (`truncated`, `narrow`, `cutReason`, `requestedCount`,
  `returnedCount`).
- Unknown, unavailable, unsupported, and empty are different results. An empty list is a `success`.

## Errors and reason codes

- Any result whose kind is not `success` also sets the MCP `isError` flag
  (`ToolCallPolicy.MarkOperationFailure`).
- A reason code names one cause and is stable. A caller branches on `kind` and `reasonCode`, never on
  `message`. A message is sanitized and never holds a credential or a local secret path.
- Every invalid-input result names one argument in `path`. A reason that covers several arguments carries the exact
  path of the one at fault, and a lower layer that cannot know the argument name leaves the wrapper of the tool to set
  it (`SurfaceConsistencyTests.EveryInvalidInputResult_NamesItsPath`,
  `tests/MtgMcp.App.Tests/InvalidInputPathAuditTests.cs`). A wrong confirmation phrase has `path` `confirmation`. A
  bad cursor has `path` `cursor`, and a bad page size has `path` `pageSize`. A provider `400` from Commander Spellbook
  (`source-request-rejected`) or a `400` or `422` from Playgroup (`provider-request-rejected`) is `invalid-input` with
  the path of the tool argument that carried the rejected value (`sourceQuery`, `events`, and so on), not `unavailable`.
- A tool refuses a change of state with one of three codes, named in `src/MtgMcp.Core/Results/SharedReasonCodes.cs`.
  All three are conflicts that carry `current`, so the caller can read again and retry:
  - `stale-deck-revision`: a tool was given an `expectedRevision` and the local deck has another revision.
    `current.revision` holds it. Every tool that checks a revision returns it: the local writes, the previews, the
    card lists, Commander Spellbook, statistics, `archidekt_deck_create` with `localDeckId`, and the pull and push
    applies, which check the revision before anything else.
  - `preview-changed`: an apply found that its preview no longer matches the evidence now, whatever the tool (the
    import, identity, category, pull, push, and restore applies). `current.previewFingerprint` holds the fingerprint
    of the preview as it is now.
  - `cursor-stale`: a cursor was issued for this request, but the data behind it changed after the first page (see
    Paging). `current` carries the state when the tool has one.
- Some changes keep a code of their own because the code names a different cause:
  `remote-deck-changed` (the Archidekt deck changed; pull and push apply, snapshot restore, and deck delete),
  `stale-scryfall-generation` (installed card data changed; category apply), `snapshot-changed`,
  `snapshot-content-changed`, `folder-tree-changed`, and `pull-discard-option-mismatch`. The earlier codes for a
  stale revision, a changed preview, or a changed page source are gone, and
  `tests/MtgMcp.Architecture.Tests/RemovedReasonCodeTests.cs` fails if one appears in `src/`. The CHANGELOG lists
  them.
- Refusals name what to change, so a caller can fix the call without a guess:
  - `pull-discard-option-mismatch`: `archidekt_pull_apply` got a preview fingerprint that matches the preview with the
    other `discardLocalChanges` value. The message names the value the preview used and says to pass the same one. The
    check rebuilds the preview from data the apply already read and sends no provider request
    (`ArchidektCoordinator.DescribeOtherDiscardValueAsync`).
  - `push-removals-unconfirmed`: a push removes too many entries and `confirmRemovals` is missing or wrong; the message
    names the option and the count.
  - `confirmation-required`: a tool got a missing or wrong `confirmation`; the message is always
    `confirmation must be exactly "<phrase>".`, and `path` is `confirmation` (see Confirmation phrases).
  - `invalid-id`: an Archidekt destructive operation got an ID that is not numeric. It is checked before the
    confirmation phrase, so a refusal never echoes arbitrary caller text, and `path` is the ID argument.
  - `entry-zone-contradicts-category`: names each local entry ID and both `patch-entries` fixes (see
    [Deck model](deck-model.md)).
  - `request-limit-exceeded` (an `unsupported` result, since no argument is at fault): names the limit, the predicted
    request count, how many operations fit, and that the push can be split by applying part of the local changes
    first.
  - `invalid-zones`, `invalid-category-names`, and `invalid-card-names`: bad `local_deck_get` filters; each carries the
    `path` of the bad value.
  - `invalid-cursor` and `invalid-page-size`: see Paging.
  - `invalid-deck-change`: a `patch-entries` change, or a `remove-entry` with both or neither of `entryId` and
    `entryIds`, is refused, and nothing is written.
- `operation-mode-denied` (an `unsupported` result) is returned by a write tool called in a mode that does not
  allow it. See Operation modes.

## Paging

One form pages every tool: `cursor` and `pageSize` in, `nextCursor` out. `nextCursor` is null on the last page. No tool
takes any other paging argument, such as an offset, a limit, a page number, or a fingerprint for a later page, and no
tool returns any other next-position field. `SurfaceConsistencyTests` (`PagedTools_TakeOnlyCursorAndPageSize`,
`PagedTools_ReturnNextCursorAndNoOtherNextPosition`) fails on any other form, and `ToolPagingTests` and the
per-family paging tests cover the next page, the last page, and a stale cursor.

| Tool | `pageSize` (default, most) | Cursor is tied to |
| --- | --- | --- |
| `local_deck_list` | 50, 100 | the deck list (a deck created, edited, or deleted between pages makes it stale) |
| `local_deck_get` | 25 with `include`, otherwise the whole deck when it fits, 100 | the deck revision and the request |
| `local_deck_import_preview`, `local_deck_identity_reconcile_preview`, `local_deck_category_rules_preview` | 100, 100 | the preview fingerprint (and the deck revision) |
| `local_deck_export_bundle` | 30,000 UTF-16 code units, 30,000 | the artifact and its checksum |
| `scryfall_search`, `scryfall_cards_get`, `scryfall_installed_cards_by_tag`, `scryfall_autocomplete`, `scryfall_tags_search`, `scryfall_reference_get`, `scryfall_request_snapshots_get` | 25, 100 (25 for raw output) | the immutable result or the installed card-data generation |
| `scryfall_card_details` | 25 per section, 100 (25 with `includeRaw`) | the card and the sections asked for |
| `card_lists_compare`, `card_list_query` | 25, 100 (25 for `full`) | the request and its source checksums |
| `archidekt_deck_list` | 50, 100 | the deck list |
| `archidekt_deck_get`, `archidekt_folders_get`, `archidekt_snapshots_get` (kind `get`) | 100, 200 | the remote fingerprint or tree fingerprint |
| `archidekt_sync_diff`, `archidekt_pull_preview`, `archidekt_push_preview`, `archidekt_snapshot_restore_preview` | 50, 100 | the preview and its guards |
| `spellbook_variant_search`, `spellbook_deck_combos_find` | 20, 25 | the request (and, for combos, the deck and revision) |
| `playgroup_playgroup_games_list` | 10, 100 | the request |

Rules that hold for every paged tool:

- The cursor is opaque server data, not a provider token. It records a position and what the page depends on, such as
  the request arguments, the deck revision, or a checksum of the data. A cursor is "tied to" that state: it is accepted
  only for the same request, and refused as stale when the data changed. One codec in Core writes and reads every
  cursor (`PageCursor`, `PageCursorBinding`, and `PageCursorPosition` in `src/MtgMcp.Core/Paging/`), and every Scryfall
  cursor uses it. A position has one of three shapes: `Offset` (one row offset), `TwoOffsets` (one offset into each of
  two lists), and `ProviderPage` (a provider page number and the rows of it already shown). The checksum inside a
  cursor detects accidental edits and cuts only, since anyone can compute it. Every reader bounds the position
  against the data it pages, so a forged cursor can only point inside data the caller may read, and a position outside
  it is `invalid-cursor`.
- A cursor is read with the same other arguments that made it. A cursor that is malformed, edited, too long, of another
  version, or issued for another request is refused with `invalid-input`, reason `invalid-cursor`, `path` `cursor`.
  A cursor for this request whose data changed after the first page is refused with the conflict `cursor-stale`;
  start again without a cursor. The error carries no provider detail.
- `pageSize` outside the tool's range is `invalid-page-size` with `path` `pageSize`.
- `pageSize` is a maximum. A page also stops at the result size limit, sets `truncated` and `narrow`, and its
  `nextCursor` continues without skipping a row.
- A preview pages with one cursor over all its lists, so a later page needs no fingerprint. A two-list preview shows
  all of its operations before any of its differences. The apply tool takes no
  page, because the fingerprint covers the whole preview. `local_deck_import_preview` still takes `content` on every
  call, because the server stores no import.
- `scryfall_card_details` has one `nextCursor` for every section asked for in `include`. A section with no more
  items is left out of later pages, and `nextCursor` is null when no section has more.
- `card_list_query` takes `pageSize` and `cursor` at the top level and returns `nextCursor` at the top level; the
  `rows` object holds only `fields` and `sortBy`.

Special cases:

- **Commander Spellbook.** The cursor ends at the source's offset cap of 1,000 (`src/MtgMcp.Spellbook/SpellbookContract.cs`),
  and the tool descriptions say so. `spellbook_variant_search` and `spellbook_deck_combos_find` return
  `{ source, nextCursor, truncated }` (combos also return `deck`). A search page cut to the result size limit sets
  `truncated` and continues after the last row shown. A combos page cut to the limit sets `truncated` and has no
  `nextCursor`; repeat the call with a smaller `pageSize`. This is an accepted deviation: Spellbook's offset counts
  combos, not the grouped rows a page shows, so no position can name the next row. A source offset in a cursor past
  the cap is `invalid-cursor`.
- **Playgroup.** Playgroup reports no total, so `playgroup_playgroup_games_list` returns `{ source, nextCursor, truncated }`
  with `nextCursor` after every full page. When the game count is an exact multiple of `pageSize`, the last page is
  empty. A page shorter than `pageSize` has a null `nextCursor`. A cursor inside a page that was cut to the result size
  limit is tied to the checksum of that page, so a page that changed gives `cursor-stale`; a cursor at the start of a
  provider page is not, because no page was read yet. A bad page position is `invalid-cursor`.
- **Export chunks.** `local_deck_export_bundle` counts `pageSize` and positions in UTF-16 code units of the artifact
  text and never splits a surrogate pair. The cursor is tied to the artifact and its checksum, so a deck edited between
  chunks gives `cursor-stale`. The chunks joined in order are the exact file.
- **Scryfall collection.** `scryfall_cards_get` continues a later page from local data and makes no provider call. If a
  card-data sync or a deleted request snapshot changed the result between pages, the cursor is `cursor-stale`. A
  later page with a different `freshnessPolicy` than the first is another request, so its cursor is `invalid-cursor`.

Accepted deviations from the one form (recorded in the surface-consistency plan):

- `spellbook_deck_combos_find` returns no `nextCursor` after a size cut (see Commander Spellbook above).
- `local_deck_import_create.options` and `local_deck_category_rules_apply.source` stay objects, because each is a
  document (import options, a rule set).
- Scryfall list results nest `nextCursor` inside their page object; `scryfall_card_details` puts it at the top level.

## Preview and apply pairs

A preview reads and writes nothing. An apply repeats what the preview was given, so a changed input or changed
state is refused instead of applied.

| Preview | Apply | Guards the apply takes | Inputs the apply repeats |
| --- | --- | --- | --- |
| `local_deck_import_preview` | `local_deck_import_create` | `previewFingerprint` | `formatId`, `content`, `options` |
| `local_deck_identity_reconcile_preview` | `local_deck_identity_reconcile_apply` | `previewFingerprint`, `applyToken`, `expectedRevision` | `deckId`; the entry selection is inside the token; `allowPartial` is set on the apply only |
| `local_deck_category_rules_preview` | `local_deck_category_rules_apply` | `previewFingerprint`, `applyToken`, `expectedRevision`, `expectedActiveGeneration` | `deckId`, `source`, `freshnessPolicy` |
| `archidekt_pull_preview` | `archidekt_pull_apply` | `previewFingerprint`, `expectedRevision`, `expectedRemoteFingerprint` | `remoteDeckId`, `localDeckId`, `discardLocalChanges` |
| `archidekt_push_preview` | `archidekt_push_apply` | `previewFingerprint`, `expectedRevision`, `expectedRemoteFingerprint` | `localDeckId`, `confirmRemovals` |
| `archidekt_snapshot_restore_preview` | `archidekt_snapshot_restore_apply` | `previewFingerprint`, `expectedRemoteFingerprint`, `expectedSnapshotChecksum`, `expectedSnapshotContentFingerprint`, `confirmation` | `remoteDeckId`, `snapshotId` |

- A fingerprint covers the whole preview, not one page. An apply never takes a page.
- Every apply tool takes flat arguments (see Parameters).
- The Archidekt apply tools run the preview again and refuse a stale one in a fixed order: a changed local deck is
  `stale-deck-revision`, a changed Archidekt deck is `remote-deck-changed`, and any other change to the preview is
  `preview-changed`. A pull apply with a different `discardLocalChanges` than its preview is refused with
  `pull-discard-option-mismatch`, naming the value the preview used.
- `archidekt_pull_preview` and `archidekt_push_preview` return `applyOptions` when the apply call must repeat an
  option: `discardLocalChanges: true` for a pull, `confirmRemovals` with the removal count for a push. It is the field
  to read for what an apply repeats. `archidekt_push_preview` also returns `providerRequestLimit` beside
  `predictedProviderRequests`, which counts every request the push needs, with the reads and the room to retry them.
- `local_deck_duplicates_preview` has no apply tool. It returns the changes to send to `local_deck_apply_changes`.
- Other guards use the same idea without a preview: `expectedRevision` on local deck writes,
  `expectedDatabaseFingerprint` on backup restore, `expectedActiveGeneration` on card-data changes, and
  `expectedChecksum` on saved-request deletion.
- The server does not choose a conflict winner. A stale or tampered request is refused. A `conflict` result
  says what is current in `current`, so the caller can preview again and retry.

## Confirmation phrases

An operation that destroys or replaces data takes `confirmation`, an exact phrase that names the action and the
target: `<action> <target kind> <id>`. The phrase names the target, so a caller cannot confirm the wrong one. A missing
or wrong phrase is refused with `confirmation-required`, `path` `confirmation`, and the one message
`confirmation must be exactly "<phrase>".` A caller's ID is shown up to 64 characters
(`src/MtgMcp.Core/Results/Confirmation.cs`).
No tool takes a yes-or-no flag in place of the phrase. `ToolNamingRuleTests.DestructiveOperations_TakeAConfirmationPhrase_AndNoAcknowledgeBooleans`
holds the list.

| Tool | Phrase |
| --- | --- |
| `local_deck_delete` | `delete deck <deckId>` |
| `local_deck_backup_restore` | `restore backup <backupId>` |
| `local_deck_backup_delete` | `delete backup <backupId>` |
| `archidekt_deck_delete` | `delete deck <remoteDeckId>` |
| `archidekt_folder_delete` | `delete folder <folderId>` |
| `archidekt_snapshot_delete` | `delete snapshot <snapshotId>` |
| `archidekt_snapshot_restore_apply` | `restore snapshot <snapshotId>` |
| `archidekt_pull_apply`, only with `discardLocalChanges` true | `discard local changes <localDeckId>` |
| `scryfall_card_data_delete` | `delete card-data <expectedActiveGeneration>` |
| `scryfall_card_data_rollback` | `rollback card-data <expectedPreviousGeneration>` |
| `scryfall_request_snapshot_delete` | `delete request-snapshot <requestSnapshotId>` |

`confirmRemovals` on a push stays a count: it restates a number the caller saw in the preview, not a target. A wrong
count is `push-removals-unconfirmed`. An ID in a phrase is written as the plain UUID with hyphens, or as Archidekt
gives it.

## responseFormat

Ten tools take `responseFormat`. Every one defaults to `standard`, and `standard` is never an alias of another
value. `concise` returns the short fields the tool documents. `standard` returns every field this server shapes.
`full` adds raw provider objects, so only the tools that have raw provider objects offer it. An unknown value is
refused with `invalid-response-format`. `ToolAllowedValuesTests` holds each tool's values.

| Tool | Values | Notes |
| --- | --- | --- |
| `scryfall_search`, `scryfall_cards_get`, `scryfall_installed_cards_by_tag`, `card_lists_compare` | `concise`, `standard`, `full` | `fields` picks concise fields; `full` adds raw provider objects and caps a page at 25 rows |
| `local_deck_get` | `concise`, `standard` | `concise` gives five entry fields; `standard` every stored entry field and the assignment rows |
| `archidekt_deck_get`, `archidekt_snapshots_get` | `concise`, `standard` | `concise` omits the provider IDs, finish, language, and sort order |
| `archidekt_sync_diff`, `archidekt_pull_preview`, `archidekt_push_preview` | `concise`, `standard` | `concise` gives each difference row as `path`, `state`, and only the differing `fields`. Fingerprints, guards, and `applyAvailable` do not change. |

See [Output control](../output-control.md).

## Operation modes and annotations

- There are three modes: `read-only`, `local`, and `remote` (default). Mode is the only write boundary
  (`src/MtgMcp.App/Configuration/OperationMode.cs`). A toolset never grants write access.
- A tool declares the authority it needs: read, provider read, local write, or remote write. Registration hides
  a tool the mode does not allow. Every write tool also checks the mode when it runs and returns
  `operation-mode-denied` if it is called anyway.
- Annotations are hints for the client, not authorization:
  - Read tools set `ReadOnly` and `Idempotent`. Write tools set neither.
  - `Destructive` is set on tools that delete or replace data, including `local_deck_apply_changes`,
    `local_deck_category_rules_apply`, `archidekt_pull_apply`, and `archidekt_push_apply`. Tools that only
    create (`local_deck_create`, `local_deck_import_create`, the `*_create` Archidekt tools) do not set it.
  - `OpenWorld` is set when the tool can reach a provider. Installed-data, local, and exact-math tools do not
    set it.
- The Archidekt remote destructive tools and `playgroup_game_events_batch_create` also carry the
  `anthropic/requiresUserInteraction` tag (`src/MtgMcp.App/Hosting/ToolMetadata.cs`), so Claude Code asks
  before each call.

## Tests that hold the surface

- `ToolNamingRuleTests`: the naming rule.
- `ToolSurfaceRulesTests`: description openings, merge rules, closed input schemas, untrusted-text notes,
  confirmation tags, mode visibility.
- `ToolSchemaSurfaceTests`: enum publication and the description length limit.
- `ToolInputDescriptionTests`: descriptions on nested input fields.
- `ToolOutputSchemaTests`: the shared output schema.
- `ToolResultBudgetTests`: the result size limit and paging.
- `SurfaceConsistencyTests`: the one paging form and a `path` on every invalid-input result.
- `ToolAllowedValuesTests`: the `responseFormat` values of each tool.
- `tests/MtgMcp.Architecture.Tests/RemovedReasonCodeTests.cs`: no removed reason code in `src/`.
- `tests/MtgMcp.Architecture.Tests/ToolSurfaceNamingTests.cs`: prefixes, name shape, and removed names.
