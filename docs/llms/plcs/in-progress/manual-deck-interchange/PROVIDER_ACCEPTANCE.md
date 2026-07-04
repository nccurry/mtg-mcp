# Manual Provider Acceptance Records

## Record Schema

Every acceptance record contains the provider, observed UTC, exact UI flow or
path, artifact SHA-256 checksums, result, notes, and revalidation reason. A
research-only or not-run record never satisfies manual acceptance.

## Current Records

### Archidekt

- Provider: Archidekt
- Observed UTC: 2026-07-04T22:48:08Z
- UI flow/path: Deck editor, Import Cards dialog
- Primary artifact generated UTC: 2026-07-04T21:48:47Z
- Primary artifact SHA-256:
  `232639820ea6742f236f7c4d80ff67fe5146843be87d4d301dbb90df6bbebde8`
- Result: Core import passed; disposable-deck cleanup pending.
- Preserved: Total quantities, card names, exact `2XM` 190, `DMU` 278, and
  `2X2` 446 printings, and the emitted `Mana Sources` and `Candidate` primary
  categories.
- Companion-only: Commander/sideboard/maybeboard zones, the separate foil
  Island row, and the `Basics` and `Creatures` secondary categories. Archidekt
  merged the two DMU Island rows into one quantity-11 normal row.
- Notes: Call to the Feast was absent from provider text and remained present
  in the native companion. No raw payload, account identity, or remote deck ID
  is retained in this record.
- Cleanup: Pending repository-owner confirmation.
- Revalidation reason: Implementation-time verification

### Moxfield

- Provider: Moxfield
- Observed UTC: 2026-07-04T22:48:08Z
- UI flow/path: Deck editor, Bulk Edit
- Primary artifact generated UTC: 2026-07-04T21:48:47Z
- Primary artifact SHA-256:
  `47610c01219d39aa56d280f37281d583c5e3ea18ca7bfb88c8f244a8b591e9a0`
- Result: Core import passed by repository-owner UI verification;
  disposable-deck cleanup pending.
- Preserved: Quantities, card names, exact printings, foil/etched markers, and
  multiple local tags.
- Companion-only: Commander/sideboard/maybeboard zones, which are not encoded
  by the accepted primary artifact.
- Notes: Call to the Feast was absent from provider text and remained present
  in the native companion. The provider page was blocked from independent
  read-only inspection by its anti-automation boundary, so this record relies
  on the repository owner's authenticated UI confirmation. No remote deck ID
  is retained.
- Cleanup: Pending repository-owner confirmation.
- Revalidation reason: Implementation-time verification

## Open Acceptance Gate

These records bind the UI checks to exact artifacts and distinguish applied
fields from companion-only fields. The core-import portion of XCHG-017 passed;
the packet remains open only until the repository owner confirms both
disposable decks were deleted.

The opt-in generator is
`ManualInterchangeAcceptanceTests.GenerateProviderBundlesForDisposableUiChecks`.
It requires `MTGMCP_PROVIDER_ACCEPTANCE_DIR` to name a new caller-selected
directory and never performs provider network access.
