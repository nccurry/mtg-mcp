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
- **P25 - batch card lookup is now exposed.** Track 1 added `card_get_batch`,
  backed by the existing `ICardCatalog.GetCardsByNamesAsync` / Scryfall
  `cards/collection` path.
- **P26 - image/art access is now exposed as links.** Track 1 added
  `card_get_image`, reusing `CardInfo.ImageUris` and returning URI metadata
  rather than inline image bytes.
- **P27 - pricing now has an explicit price-source port.** Cost and candidate
  outputs already carried `priceSource`, `printingStatus`, and
  `selectedPrintingReason`; Track 1 added `IPriceSource` and routes cost
  analysis through the default normalized catalog source.

## 2. Goals / non-goals

Goals:
- A local-first card collection so deck/candidate tools can answer ownership and
  owned-vs-budget questions.
- A batch card lookup tool.
- Optional image/art access as link metadata.
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
  `DeckServiceBase.WorkspaceHelpers.cs:47-48`). `card_get_image` now exposes
  the image URI path as a link-only affordance.
- Pricing has a first explicit port plus provenance: `IPriceSource`,
  `CatalogPriceSource`, `CardPriceEvaluation`
  / `PriceSource` (`ScryfallClient.Mapping.cs:35`, `Core/Pricing`),
  `DeckCostDriver.PriceSource`, `PrintingStatus`, and
  `SelectedPrintingReason`, plus the Scryfall "budget-playable pricing may use
  foil/etched/market fallback" flag (`ScryfallOptions.cs:46`). There is still no
  configured alternate provider.
- Collection/ownership: nothing exists. This is the only genuinely net-new subsystem.

## 4. Workstreams

Split into two independently shippable tracks, smallest-value-first:

- Track 1 (cheap, shipped first): batch card lookup (4.2), the image affordance
  (4.3), and the initial price-source port (4.4). These expose or wrap data that
  already flows through `ICardCatalog`/`CardInfo`, so they are low-risk,
  no-new-persistence slices on the conformant surface.
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
- Done in the first Phase 8 slice: `card_get_batch(names[], limit)` returns
  normalized request-order rows plus missing names, delegates to the existing
  `GetCardsByNamesAsync`, and clamps the effective limit to 1-75. Hydration-heavy
  prompt guidance now prefers it for multiple named cards.

### 4.3 Image / art access (expose existing)
- Done in the first Phase 8 slice: `card_get_image(nameOrId, kind)` returns a
  Scryfall-hosted image URI, requested/resolved kind, available kinds, and
  status (`ok`, `not-found`, `no-image`) without fetching image bytes.

### 4.4 Price-source abstraction
- Done in the second Phase 8 Track 1 slice: promote cost analysis behind
  `IPriceSource`, with `CatalogPriceSource` preserving the existing normalized
  catalog/Scryfall-shaped price-field policy (`usd`, foil/etched, TCG fallback)
  and provenance. Alternative provider selection remains future work.
- Price provenance was already present in cost and candidate outputs
  (`priceSource`, `printingStatus`, `selectedPrintingReason`); the port keeps
  that default output stable.

## 5. Files to create / change

- Create later: `Core/Collection/CollectionService.cs` + models + `ICollectionStore`,
  and `docs/collection.md`.
- Changed in Track 1: `CardTools`, `Core/Pricing/IPriceSource.cs`,
  `DeckAnalysisMetrics`, host DI, MCP surface tests, pricing/cost tests, prompt
  guidance, `README.md`, `docs/architecture.md`, and `docs/toolsets.md`.
- Tests: collection round-trip + ownership diff later; Track 1 has direct batch
  lookup and image affordance tests plus MCP surface inventory coverage.

## 6. Testing

- Offline fixture tests for collection store and ownership diff.
- Batch lookup against `cards/collection` fixtures (MockHttp).
- Cost analysis shows owned-vs-needed and price provenance deterministically.

## 7. Definition of done

Track 1:
- Done: `card_get_batch` exposed and used by hydration-heavy prompts.
- Done: image affordance available as link-only `card_get_image`.
- Done: price-source port in place for cost analysis, preserving existing
  provenance output and default price behavior.

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
