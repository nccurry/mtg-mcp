# MCP Contract And Adapter Hardening Software Architecture And Design Document

## Document Control

- Lifecycle status: Completed
- PLC packet: [README.md](README.md)
- Owner: mtg-mcp
- Reviewers: Nick Curry, repository owner
- Last updated: 2026-07-06
- Related SRD: [SRD.md](SRD.md)
- Related implementation plan: [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)

## Revision History

| Date | Author | Summary |
| --- | --- | --- |
| 2026-07-06 | Codex | Initial design for AMEND-005 review. |
| 2026-07-06 | Codex | Confirmed the implemented schema, identity, ownership, and behavior-preservation boundaries. |

## Executive Summary

App keeps one explicit static toolset registry, but schema 6 stops treating
registration as runtime proof. The deck batch input becomes an attributed
polymorphic union, which the pinned SDK emits as discriminated alternatives.
A small App-owned reconciliation coordinator composes the deck store and
Scryfall service; it never enters Core or either adapter.

Provider refactors preserve their public façades. Cohesive internal components
take over existing method families while shared HTTP/database primitives remain
centralized. This is responsibility extraction, not a new abstraction layer.

## Goals, Non-Goals, And Constraints

- Make MCP metadata and schemas accurately guide an LLM.
- Normalize exact deck identity without selecting fuzzy matches or printings.
- Make provider code traceable by capability family without changing behavior.
- Preserve Core's provider-neutral boundary and existing project dependencies.
- Add no legality, rules, recommendation, migration, provider, or persistence
  capability.
- Preserve cancellation, sanitized errors, pacing, cooldown, and at least
  90-percent line coverage per production assembly.

## Alternatives Considered

| Alternative | Decision | Reason |
| --- | --- | --- |
| Dynamic capability HTTP probes | Rejected | Metadata reads must stay deterministic, cheap, and side-effect free. |
| Keep `available` and add a warning | Rejected | The primary field would remain misleading. |
| Remove `deck_apply_changes` | Rejected | Atomic heterogeneous deck edits are a real workflow. |
| Keep the flat record with more prose | Rejected | Unrelated optional fields remain structurally easy to misuse. |
| Fuzzy identity matching | Rejected | Choosing among candidates belongs to the LLM. |
| Name/Oracle lookup chooses a printing | Rejected | Oracle identity does not prove printing intent. |
| New shared provider repository interfaces | Rejected | There is one implementation and no cross-provider storage contract. |
| Split large files with partial classes only | Rejected | File size changes without fixing ownership. |

## Chosen Design

### Capability projection

Implemented descriptors remain the single registry. `status` and
`CapabilityToolsetAvailability` are removed. Each projected row contains
`implementationStatus: implemented` and a non-I/O credential projection:

| Toolset | Credential state | Authentication status tool |
| --- | --- | --- |
| `decks` | `not-required` | `null` |
| `scryfall` | `not-required` | `null` |
| `archidekt` | `configured-unverified` or `not-configured` | `archidekt_auth_status` |
| `playgroup` | `configured-unverified` or `not-configured` | `playgroup_auth_status` |

Configuration presence never means provider acceptance. Clients invoke the
named redacted auth tool when they need a more specific result.

### Batch-change schema

`DeckChangeInput` becomes an abstract record using `kind` as its JSON type
discriminator. Eleven sealed records correspond one-to-one with the Core
`DeckChange` union. Every constructor property carries a description. The
mapper switches exhaustively on runtime type and returns an indexed failure
containing only the kind and required field names.

The public wire kinds remain `update-metadata`, `add-entry`, `update-entry`,
`remove-entry`, `add-category`, `update-category`, `remove-category`,
`assign-category`, `unassign-category`, `upsert-provider-binding`, and
`remove-provider-binding`. There is no compatibility alias for the old flat
shape.

| `kind` | Exact branch fields |
| --- | --- |
| `update-metadata` | `name`, nullable `description`, `format` |
| `add-entry` | `entryDraft` |
| `update-entry` | `entry` |
| `remove-entry` | `entryId` |
| `add-category` | `categoryDraft` |
| `update-category` | `category` |
| `remove-category` | `categoryId` |
| `assign-category` | `entryId`, `categoryId`, `isPrimary` |
| `unassign-category` | `entryId`, `categoryId` |
| `upsert-provider-binding` | `providerBinding`, nullable `canonicalBaseline` |
| `remove-provider-binding` | `bindingId` |

### Identity reconciliation contracts

Preview inputs are `deckId`, `expectedRevision`, optional unique `entryIds`,
and `freshnessPolicy`. Omitted entry IDs select all entries; selection over 150
or an unknown/duplicate ID is invalid before provider traffic.

Each stored entry produces exactly one lookup using the first complete case:

1. printing ID;
2. set code plus collector number and language;
3. Oracle ID;
4. exact name.

An entry containing stronger and weaker fields must agree with the returned
card. A mismatch is `conflict`, not permission to fall through. Exact name uses
Scryfall's exact-name case and never fuzzy-name. Canonical lookup keys dedupe
provider acquisition while result rows remain in deck order.

Set/collector/language matching is exact against the installed corpus. English
printing misses may use the existing collection contract. A non-English
printing without exact corpus evidence returns `not-cached`; this child does
not add a provider route or silently substitute the English printing.

`DeckIdentityBefore` and `DeckIdentityAfter` contain only identity fields.
Printing matches may fill the complete printing identity. Oracle/name matches
may change only canonical name and Oracle ID. The coordinator produces
`UpdateDeckEntryChange` values by copying all non-identity fields from the
current entry.

The preview fingerprint hashes canonical deck/revision/selection, ordered
outcomes, evidence references, and reconciliation schema version. The opaque
apply token contains the canonical request, evidence bindings, result checksum,
and a process-local HMAC; it contains no credential, local path, or raw
provider object. Restarting the process invalidates the token and requires a
new preview.

Apply decodes and checks the token, compares explicit inputs, reloads the deck,
reloads the exact retained snapshot/generation evidence, recomputes the ordered
reconciliation result, compares the fingerprint, and invokes the existing
transactional batch path. Token-carried proposals are never mutation authority.
An incomplete result requires `allowPartial=true`. No provider request occurs
during apply.

### Identity MCP contract

| Tool | Inputs | Successful output |
| --- | --- | --- |
| `deck_identity_reconcile_preview` | `deckId`, `expectedRevision`, optional `entryIds`, `freshnessPolicy` (`default`, `cache-only`, `refresh`) | `deckId`, `deckRevision`, ordered `rows`, `isComplete`, `proposedChangeCount`, evidence references, `previewFingerprint`, `applyToken` |
| `deck_identity_reconcile_apply` | `deckId`, `expectedRevision`, `previewFingerprint`, `applyToken`, `allowPartial=false` | The canonical post-transaction `DeckDocument` |

Each preview row contains `entryId`, `status`, `matchedBy`, nullable `message`,
`before`, nullable `after`, `origin`, nullable `corpusGenerationId`, and nullable
snapshot reference. Identity before/after values contain `cardName`,
`oracleId`, `printingId`, `setCode`, `collectorNumber`, and `language` only.

### Capability row order

Schema 6 emits each row in this deterministic order: `name`,
`implementationStatus`, `credentialState`, `authenticationStatusTool`,
`stability`, `enabled`, `defaultEnabled`, `visibleToolCount`, `description`, and
`unsupportedOperations`. Credential state derives only from already validated
configuration presence, even when the toolset is disabled.

### Scryfall building blocks

| Component | Responsibility |
| --- | --- |
| `ScryfallDatabase` | Database path, connection creation, schema bootstrap/validation, and component composition. |
| `ScryfallCorpusStore` | Active/previous generations, card/ruling/tag reads, import, activation, rollback, and deletion. |
| `ScryfallSnapshotStore` | Exact-request snapshot lookup, storage, listing, replay, and deletion. |
| `ScryfallCoordinationStore` | Cross-process leases, metadata-check timestamps, and provider-start reservations. |
| `ScryfallCardEvidenceOperations` | Search, card, collection, prints, rulings, sets, catalogs, autocomplete, metadata, and exact acquisition. |
| `ScryfallCorpusOperations` | Status, sync, rollback, delete, tag search, and cards-by-tag. |
| `ScryfallSnapshotOperations` | Snapshot list/get/delete. |
| `ScryfallService` | Existing public façade, construction, delegation, and disposal only. |

SQL schema constants and connection policy stay in the database owner. Store
components use that concrete owner directly; no repository interface is added.

### Archidekt building blocks

| Component | Responsibility |
| --- | --- |
| `ArchidektHttpTransport` | Credentials, HTTP lifetime, authentication, pacing, retries, cooldown, request budget, and sanitized failures. |
| Deck/folder/snapshot transports | Exact route construction and payload submission for one provider family. |
| Deck/folder/snapshot mappers | Provider-family parsing and normalized contract creation. |
| Deck/folder/snapshot operations | Validation, read-back, preview/apply, and provider-family lifecycle. |
| Pull/push workflows | One synchronization direction each. |
| Binding resolver | Local binding/baseline loading and conflict classification. |
| `ArchidektService` / `ArchidektCoordinator` | Existing public/internal façades and delegation only. |

All transports share the same HTTP/pacing owner and operation budget. Splitting
must not create parallel timelines, duplicate authentication, or independent
retry behavior.

## Runtime Flow

```text
deck_identity_reconcile_preview
  -> load exact deck revision
  -> validate/canonicalize selected entries
  -> build and deduplicate exact lookups
  -> Scryfall collection resolution (<= 75 per provider request)
  -> classify ordered matches/conflicts/unknowns
  -> return evidence-bound fingerprint and apply token

deck_identity_reconcile_apply
  -> validate token/fingerprint/deck/revision
  -> verify retained evidence without HTTP
  -> require allowPartial when incomplete
  -> copy current entries with identity-only changes
  -> existing transactional ApplyChangesAsync
```

## MCP Schemas And Diagnostics

- Preview: read-only, idempotent, open-world; visible in all modes.
- Apply: destructive local write, idempotent only through stale-revision
  refusal, closed-world; visible in local/remote and guarded at invocation.
- Batch schema alternatives use unique `kind` constants and required property
  sets. Unknown discriminators fail as sanitized invalid parameters; semantic
  failures return `invalid-deck-change` with a zero-based index.
- A schema test walks every registered root input property and rejects blank
  descriptions. It additionally walks every batch alternative.
- Capability schema serialization remains canonically ordered and path-free.

## Error Handling

| Failure | Result |
| --- | --- |
| Missing/duplicate/unknown selection or over 150 entries | `invalid-input` before HTTP or writes |
| Strong and weak identity fields disagree | Per-row `conflict`; preview incomplete |
| Exact lookup misses | Per-row `not-found`; preview incomplete |
| Cache-only evidence absent | Per-row `not-cached`; preview incomplete |
| Read-only acquisition required | Existing `local-write-required` result |
| Provider stops or rejects | `unavailable`; no fabricated proposal |
| Token/checksum/input mismatch | `invalid-input`; no writes |
| Deck revision changed | `conflict`; no writes |
| Bound evidence pruned | `unavailable` with `identity-evidence-unavailable` |
| Incomplete preview without opt-in | `invalid-input`; no writes |
| Any store mutation fails | Transaction rollback; original revision retained |

## Project Boundaries

- Core gains no Scryfall, MCP, SQLite, or provider dependency.
- App owns reconciliation composition and MCP-only wire contracts.
- Decks remains the only writer of `decks.db`.
- Scryfall remains the only owner of provider traffic and `scryfall.db`.
- Archidekt keeps provider contracts inside its adapter; App owns deck/provider
  synchronization composition.
- No project or package reference is added.

## Test Architecture

Focused unit tests cover contracts and algorithms. Deck/Scryfall integration
tests use temporary stores and fake HTTP. Existing adapter tests become
characterization gates before extraction and must remain semantically
unchanged. Official-client and installed-package tests inspect exact schemas,
counts, annotations, and the dummy-deck workflow. Live checks are bounded,
sequential reads through the existing opt-in harness; they perform no new
remote mutation.

## Decisions, Risks, And Deferred Work

| Item | Type | Resolution |
| --- | --- | --- |
| Capability cannot prove credential validity without I/O. | Decision | Report `configured-unverified` and direct clients to the auth tool. |
| Apply token may be large for 150 proposals. | Risk | Encode only canonical proposal/evidence fields and enforce the same bounded input. |
| Provider evidence may be pruned between preview and apply. | Decision | Refuse rather than applying unauditable identity changes. |
| Adapter extraction could disturb safety logic. | Risk | Characterize first and keep HTTP/pacing ownership singular. |
| Deck legality | Non-goal | Do not implement or register as a follow-up from this child. |
| Exact statistics | Deferred | Remains the next capability child after hardening completes. |
