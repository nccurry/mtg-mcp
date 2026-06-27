# Phase 8 - New Capabilities (Missing Features)

| | |
|---|---|
| Effort | M-L |
| Risk | Low-Medium |
| Depends on | Phase 1 (toolsets), Phase 2/3 (stable contracts); Phase 5 (soft - `JsonFileStore<T>`) |
| Unblocks | broader deckbuilding workflows |
| Target version | 0.16.0 |

Goal: add the high-value capabilities that are currently absent, on top of the
consolidated, conformant surface. Investigation shows several are mostly "expose existing
internal capability," which lowers the cost.

## 1. Problems addressed

- **P23 - no collection/ownership awareness** ("which of these do I own?").
- **P25 - no batch card lookup** exposed (`card_get` is one-at-a-time).
- **P26 - no image/art access** for multimodal clients.
- **P27 - pricing is Scryfall-USD-centric** with no first-class price-source abstraction.

## 2. Goals / non-goals

Goals:
- A local-first card collection so deck/candidate tools can answer ownership and
  owned-vs-budget questions.
- A batch card lookup tool.
- Optional image/art access.
- A clearer price-source abstraction.

Non-goals:
- No account integrations for collection import in this phase (manual/import-file first).
- No new scraping; honor the API-only policy.

## 3. Current state (investigation)

- Batch lookup already exists internally: `ICardCatalog.GetCardsByNamesAsync(names)`
  (`Abstractions.cs:34-37`), implemented via Scryfall `cards/collection`
  (`ScryfallClient.cs:277`). It is simply not exposed as an MCP tool.
- Images and prices already flow through the model: `CardInfo`/`CardSnapshot` carry
  `ImageUris` and `Prices` dictionaries (`ScryfallClient.Mapping.cs:77-89`,
  `DeckServiceBase.WorkspaceHelpers.cs:47-48`). `card_get` returns `CardInfo` but there is
  no image-focused affordance and prices are surfaced narrowly (USD).
- Pricing already has partial abstraction: `CardPriceEvaluation` / `PriceSource`
  (`ScryfallClient.Mapping.cs:35`, `Core/Pricing`), and `ScryfallOptions` has a
  "budget-playable pricing may use foil/etched/market fallback" flag
  (`ScryfallOptions.cs:46`). There is no multi-source price port.
- Collection/ownership: nothing exists. This is the only genuinely net-new subsystem.

## 4. Workstreams

Split into two independently shippable tracks, smallest-value-first:

- Track 1 (cheap, ship first): batch card lookup (4.2) and the image affordance (4.3).
  Both mostly expose data that already flows through `ICardCatalog`/`CardInfo`, so they are
  low-risk, no-new-persistence PRs that can land immediately on the Phase 3 conformant
  surface. The price-source abstraction (4.4) can follow in the same track.
- Track 2 (its own design decision + PR): the card collection subsystem (4.1). It is the
  only net-new persisted state and needs an explicit design decision before coding -
  persistence shape (name+quantity vs printings/foils/conditions), operation-mode
  semantics (is writing the collection a `plan`-state write or `apply`?), and where it
  lives relative to workspaces. Do not bundle it with Track 1.

### 4.1 Card collection / ownership (net-new; Track 2, design-gated)
- **Design decision required first**: persistence shape and operation-mode semantics (see
  Open questions). Land an ADR before implementation.
- Add a local-first collection store, persisted under `MTGMCP__DATA_DIR`, provider-neutral:
  owned card names/quantities/printings. Prefer reusing Phase 5's `JsonFileStore<T>`, but
  this is a **soft** dependency - if Phase 5 has not landed, the collection store can ship
  with its own minimal atomic-write persistence and adopt `JsonFileStore<T>` later. Phase 8
  is not blocked by Phase 5.
- Define a `CollectionService` and a port for future provider imports (kept local-only
  initially). Support import from pasted text / decklist-style input and from a workspace.
- Add ownership-aware affordances: an `owned`/`missing` annotation on candidate rows
  (`deck_query_cards`, `deck_review_new_card_swaps`) and an "owned vs needs-buying" view in
  cost analysis (`deck_analyze_cost`). Add tools `collection_set`/`collection_get`/
  `collection_diff_workspace` (names indicative; place in a `collection` toolset).
- Guard mutations with `OperationModeGuard` (write to local planning state).

### 4.2 Batch card lookup (expose existing)
- Add `card_get_batch(names[])` returning a typed map/rows, delegating to the existing
  `GetCardsByNamesAsync`. Bounded by a `limit`, structured output (Phase 3). Update prompts
  that hydrate many names to prefer it.

### 4.3 Image / art access (expose existing)
- Add either a tool (`card_get_image(nameOrId, kind)`) returning the relevant
  `ImageUris` entry as a resource link / image content block, or a resource
  (`mtg://card/{nameOrId}/image`). Reuse `CardInfo.ImageUris`; no new fetching of binary
  data unless a client needs inline image content (the SDK supports image content blocks).

### 4.4 Price-source abstraction
- Promote pricing behind an `IPriceSource` port (Scryfall as the default implementation),
  exposing more than USD (foil/market where available) and allowing a configured source.
  Fold the existing `CardPriceEvaluation`/`PriceSource` and the foil/market fallback flag
  into the port. Keep API-only.
- Surface price provenance in cost output (which source, foil vs nonfoil) so budgeting is
  transparent.

## 5. Files to create / change

- Create: `Core/Collection/CollectionService.cs` + models + `ICollectionStore`,
  `Core/Pricing/IPriceSource.cs` (+ Scryfall impl in `MtgMcp.Scryfall`),
  new tool classes (`CollectionTools`, batch/image tools), `docs/collection.md`.
- Change: `card_*` tools (+batch/image), `deck_analyze_cost` and candidate tools
  (ownership/price provenance), `Hosting/MtgMcpHost.cs` DI + toolsets, `README.md`.
- Tests: collection round-trip + ownership diff; batch lookup (MockHttp); price-source
  selection; image affordance.

## 6. Testing

- Offline fixture tests for collection store and ownership diff.
- Batch lookup against `cards/collection` fixtures (MockHttp).
- Cost analysis shows owned-vs-needed and price provenance deterministically.

## 7. Definition of done

Track 1 (ships first):
- `card_get_batch` exposed and used by hydration-heavy prompts.
- Image affordance available; price-source abstraction in place with provenance in output.

Track 2 (separate, after its design ADR):
- Collection capability ships behind a `collection` toolset with ownership-aware cost and
  candidate output, with persistence shape and operation-mode semantics decided in an ADR.

Both tracks:
- All new tools are structured-output + structured-error conformant (Phase 3) and gated by
  toolsets (Phase 1).

## 8. Risks & mitigations

- Risk: collection scope creep (printings, foils, conditions). Mitigation: start with
  name+quantity ownership; layer printings later.
- Risk: image content inflates payloads. Mitigation: prefer resource links/URIs over
  inline bytes unless requested.
- Risk: price-source port churn in cost code. Mitigation: keep Scryfall as default and
  refactor behind the port without changing default output first.

## 9. Open questions

- Collection persistence shape: name+quantity only first, or printings/foils/conditions? And
  operation-mode semantics: is a collection write a `plan`-state write or `apply`-only?
  (These are the ADR decisions that gate Track 2.)
- Collection import sources beyond manual/text (account integrations) - defer or include a
  read-only import? (Recommend manual/text + workspace diff first.)
- Image as tool vs resource (or both) depending on target clients.
