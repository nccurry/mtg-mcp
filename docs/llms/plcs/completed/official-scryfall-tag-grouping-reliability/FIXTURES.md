# Official Scryfall Tag Grouping Reliability Fixtures And Acceptance Matrix

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| TGR-FIX-001 | Scryfall-format data generation | `tests/MtgMcp.Scryfall.Tests/ScryfallTestFixture.cs` | Represents minimal `all_cards`, `rulings`, `oracle_tags`, and `art_tags` data with direct assignments and source relationships. | mtg-mcp | Update only after reviewing a current official source contract change. |
| TGR-FIX-002 | Multi-parent tag graph | `ScryfallTestFixture.cs` and `ScryfallDeckTagOperationsTests.cs` | Proves one direct tag can have more than one reachable source parent. | mtg-mcp | Keep deterministic IDs and Scryfall-format fields. |
| TGR-FIX-003 | Reviewed `common-v1` mapping | `src/MtgMcp.App/Decks/DeckCategoryRuleResolver.cs` plus this record | Records the current official source IDs, descendant settings, checksum, and rationale for four stable role keys. | mtg-mcp | Change only after explicit owner approval; this child permits the one in-place defect correction. |
| TGR-FIX-004 | Temporary local deck | `tests/MtgMcp.App.Tests/DeckCategoryRuleSourceTests.cs` | Holds cards with direct source tags and existing category assignments for add-only/synchronize checks. | mtg-mcp | Keep cards/names minimal and deterministic. |

## Source Fixture Shape

The fixture must be shaped like the existing Scryfall bulk data, not like a
project-made tag format. It contains:

```text
Oracle tag: aggro (parent)
Oracle tag: creature (second parent)
Oracle tag: white-weenie (child; direct assignment to Card A)
Art tag: running (direct assignment to Card B)
```

The precise fixture IDs are deterministic test IDs, not a claim that these are
the current production `common-v1` IDs. The preset fixture separately records
the reviewed current source IDs and data timestamp. A normal test does not call
Scryfall to refresh either fixture.

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| TGR-001 | TGR-FIX-001 plus architecture/source inspection | Only Scryfall-shaped tag data supplies identity, assignments, and relations; no tag writer or Tagger website client exists. | Scryfall/App tests and boundary review |
| TGR-002 | Inline selector matrix | Both/neither identity, empty ID/slug, bad kind/weight, null groups, duplicate rule, and duplicate priority fail invalid input. | Core unit tests |
| TGR-003 | TGR-FIX-001 exact ID/slug/missing/ambiguous cases | ID and slug resolve to one source ID; absent/ambiguous tag stops before preview. | Scryfall/App integration tests |
| TGR-004 | TGR-FIX-002 direct/parent/multiple-parent cases | Direct tag always matches itself; each source parent matches only when descendants are enabled. | Core/Scryfall/App tests |
| TGR-005 | TGR-FIX-004 in synchronize mode | Invalid, missing, incomplete, or changed source evidence makes no destructive removal. | App preview/apply tests |
| TGR-006 | TGR-FIX-003 plus equivalent inline input | `common-v1` keeps its ID/role keys and expands to verified official source IDs with the same result as inline rules. | App/preset tests |
| TGR-007 | Existing server profiles | Same three category-rule tools, `decks` toolset membership, and operation-mode visibility. | Surface/schema/E2E tests |
| TGR-008 | Full offline fixture suite | No live request occurs in normal tests; coverage gate passes. | Task validation |

## Common-v1 Source Review Record

The checked-in preset checksum is
`358238fc9f1d8200406e5715d16e7b7868eef674807729b027f14c0f545ea900`.
It covers each role key, exact source ID, descendant setting, and minimum
weight in the stored order.

The reviewed source record is:

| Role key | Official Scryfall tag kind | Exact source ID | Descendants | Dataset timestamp/checksum | Reviewer |
| --- | --- | --- | --- | --- | --- |
| `ramp` | Oracle | `2f3e4ad7-5e60-41b4-bdbc-653f16869cf6` (`ramp`) | Yes | 2026-09-07T21:00:33.359Z; bulk record `bd8df61e-5d0a-47a2-9086-40137a645b98`; gzip SHA-256 `e0d4afc2eceb1ba575baa54206e68d506c59e62a091f187b53a28523ef0f2087` | mtg-mcp |
| `card-draw` | Oracle | `b6448c45-ce65-4848-aa98-2151e4e07437` (`draw`) | Yes | 2026-09-07T21:00:33.359Z; bulk record `bd8df61e-5d0a-47a2-9086-40137a645b98`; gzip SHA-256 `e0d4afc2eceb1ba575baa54206e68d506c59e62a091f187b53a28523ef0f2087` | mtg-mcp |
| `removal` | Oracle | `444f824c-f910-4530-9dbe-ede7a84cd7f9` (`removal`) | Yes | 2026-09-07T21:00:33.359Z; bulk record `bd8df61e-5d0a-47a2-9086-40137a645b98`; gzip SHA-256 `e0d4afc2eceb1ba575baa54206e68d506c59e62a091f187b53a28523ef0f2087` | mtg-mcp |
| `recursion` | Oracle | `82b824ad-648f-467f-a190-2e0fa9a795d2` (`recursion`) | Yes | 2026-09-07T21:00:33.359Z; bulk record `bd8df61e-5d0a-47a2-9086-40137a645b98`; gzip SHA-256 `e0d4afc2eceb1ba575baa54206e68d506c59e62a091f187b53a28523ef0f2087` | mtg-mcp |

The source review is not permission to invent a replacement tag. If an official
source role is missing or no longer has a clear direct/descendant meaning, stop
the phase for an owner decision.

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| `deck_category_rules_validate` | read-only, local, remote | Visible | Exact source-ID validation only; no write. |
| `deck_category_rules_preview` | read-only, local, remote | Visible | Existing preview workflow with repaired source matching. |
| `deck_category_rules_apply` | local, remote | Visible | Existing local-write guard; no mutation for invalid/incomplete source data. |
| Any new tag tool/resource/prompt | Any | Absent | This packet adds none. |

## Provider Fixtures

| Provider | Fixture | Scenario | Sanitization notes |
| --- | --- | --- | --- |
| Scryfall | TGR-FIX-001 and TGR-FIX-002 | Exact identity, direct assignment, hierarchy, and multi-parent matching. | Use synthetic card names and deterministic IDs; no credentials, download URLs, or local paths. |
| Scryfall | Optional `Category=Live` check | Confirm current metadata and reviewed preset source IDs before a deliberate update. | Use documented headers/pacing; never run in normal tests or store raw payloads unnecessarily. |

## North-Star Acceptance Scenario

An agent creates or selects a local deck category, explicitly chooses the
official Oracle source tag for a group, enables descendants, and asks for a
preview. The preview groups a card directly assigned to a source child tag and
does not group it when descendants are disabled. The agent can instead use
`common-v1`, see its exact official source IDs, edit those returned rules,
and resubmit them inline. The server does not claim that the category is the
right one, create a card tag, or make a recommendation.
