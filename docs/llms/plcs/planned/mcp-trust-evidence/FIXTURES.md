# MCP Trust Evidence Fixtures And Acceptance Matrix

Use this document when implementation needs stable examples, provider payloads,
MCP surface inventories, calibration cases, or manual acceptance scenarios.

## Fixture Inventory

| ID | Type | Location | Purpose | Owner | Update rule |
| --- | --- | --- | --- | --- | --- |
| FIX-001 | Card legality fixture | To be added under tests fixture data | Cards with legal, not legal, and missing format legality plus format alias coverage. | mtg-mcp | Update when legality helper input shape changes. |
| FIX-002 | MCP response fixture | To be added under App test fixtures | Default comparison summary retaining model label and adding assumption caveat text. | mtg-mcp | Update when presenter schema changes. |
| FIX-003 | Calibration case | To be added with bracket calibration data | Commander bracket 1-5 examples, including bracket 5/cEDH. | mtg-mcp | Update when bracket criteria change. |
| FIX-004 | Evidence serialization fixture | To be added under Core/App tests | Canonical evidence tier strings in JSON. | mtg-mcp | Update only when tier vocabulary changes. |
| FIX-005 | Deck odds fixture | To be added under analysis test data | Cards counted for draw-odds target success sets. | mtg-mcp | Update when odds target rules change. |
| FIX-006 | Scryfall Tagger fixture | To be added under Scryfall fake HTTP fixtures | Existing `otag:` response plus cached/local/taxonomy contrast cases proving attribution. | mtg-mcp | Update when Scryfall search contract changes or Tagger labels change. |
| FIX-007 | Profile resolver fixture | To be added under profile test data | Host default, deck intent, built-in fallback, and missing explicit id cases. | mtg-mcp | Update when profile precedence changes. |

## Acceptance Matrix

| Requirement | Fixture or scenario | Expected result | Validation |
| --- | --- | --- | --- |
| REQ-001 | FIX-001 | Shared helper returns `legal`, `not_legal`, and `unknown`. | Core unit tests |
| REQ-002 | Legal unknown in query/recommendation paths | Unknown is visible or penalized, never silently legal. | Unit/integration tests |
| REQ-003 | FIX-002 | Default comparison summary keeps the existing model label and adds an assumption/caveat. | App presenter test |
| REQ-004 | FIX-003 | Bracket output supports 1-5 and emits 5 through explicit criteria; duplicated calibration guards accept 1-5. | Calibration and unit tests |
| REQ-005 | FIX-004 | Evidence tiers serialize as canonical strings. | Serialization and surface tests |
| REQ-006 | Mixed-source role scenario | Existing role-count explanation rows become structured/tiered; no top-level assignment source is added. | Core role explanation tests |
| REQ-007 | Hot-path classifier scenario | Existing cheap classifier/boolean match path remains available. | Unit tests and code inspection |
| REQ-008 | Detail-level response matrix | Summary has labels/caveats; normal/full have evidence rows where relevant. | App surface tests |
| REQ-009 | Recommendation score scenario | Blended score is labeled `model_score` with confidence meaning. | Recommendation tests |
| REQ-010 | FIX-006 | Source-backed/cached Tagger rows, local annotations, and embedded taxonomy matches get distinct labels. | Fixture-backed fake HTTP and Core labeling tests |
| REQ-011 | FIX-007 | Profile precedence and missing explicit id behavior follow the existing simulation profile pattern. | Profile resolver tests |
| REQ-012 | Normal test workflow | Tests run without network or real Archidekt mutations. | `task test` |

## MCP Surface Checks

| Surface | Mode | Expected visibility | Notes |
| --- | --- | --- | --- |
| `deck_compare_goldfish` | read/plan | Summary keeps the existing model label and includes assumption caveat text. | No per-card rows in summary. |
| `deck_estimate_commander_bracket` | read/plan | Output supports bracket 1-5. | Phase 3 uses existing notes/labels; canonical evidence tiers arrive in Phase 4. |
| `deck_query_cards` | read/plan | Unknown legality can appear as a visible reason or warning. | Reason-returning path. |
| Recommendation tools | read/plan | Score kind, evidence tier, and confidence meaning visible where scores are shown. | Existing fields remain where possible. |
| Draw odds tools | read/plan | Summary shows target, odds, assumptions, and evidence label. | Normal/full can include success sets. |
| Role explanation outputs | read/plan | Existing role-count explanation rows become structured/tiered at normal/full detail. | Cheap classifier remains internal. |
| Source status/list outputs | read/plan | Source evidence semantics documented for Tagger-backed, cached, local annotation, and embedded taxonomy rows. | Provider permission and attribution notes stay accurate. |

## Concrete Starter Cases

### Phase 1 Legality Cases

| Case | Input | Expected result |
| --- | --- | --- |
| LEG-001 | Card legalities contain `commander=legal`; requested format is `commander`. | Shared helper returns `legal`. |
| LEG-002 | Card legalities contain `commander=legal`; requested format is `edh`. | Shared helper normalizes the alias and returns `legal`. |
| LEG-003 | Card legalities contain `commander=banned` or `commander=not_legal`. | Shared helper returns `not_legal`. |
| LEG-004 | Card has no `commander` legality entry. | Shared helper returns `unknown`; no caller treats it as legal. |
| LEG-005 | `deck_query_cards` sees LEG-004. | Candidate is rejected from accepted rows with a visible unknown-legality reason. |
| LEG-006 | Replacement/corpus/playgroup scorer sees LEG-004. | Candidate is kept only with explicit warning, refresh note, or named penalty. |

### Phase 3 Bracket Cases

| Case | Signal shape | Expected result |
| --- | --- | --- |
| BR-001 | Low-power Commander deck with no compact combo, no fast mana density, and no cEDH pressure signals. | Estimated bracket remains in the low range, including bracket 1 when current criteria indicate it. |
| BR-002 | Mid-power Commander deck with normal ramp/removal density and no compact deterministic win package. | Existing calibrated midrange behavior remains stable. |
| BR-003 | cEDH-shaped deck with compact win package plus fast mana/tutor/protection density, such as Thassa's Oracle plus consultation-style combo and free interaction signals. | Estimated bracket can be 5 through explicit criteria. |
| BR-004 | Calibration corpus expectation uses `minimumBracket=5` or `maximumBracket=5`. | Both calibration validators accept 1-5. |

### Phase 4 Evidence Tier Serialization Cases

| Wire value | Example payload scenario |
| --- | --- |
| `source_fact` | Scryfall legality or workspace category copied directly from the declared source. |
| `source_evidence` | Card returned by a Scryfall Tagger `otag:` query supporting a role/tag claim. |
| `derived_math` | Hypergeometric draw-odds row. |
| `parser_derived` | Oracle text snippet matched as evidence. |
| `heuristic_inference` | Fallback classifier branch or embedded taxonomy match. |
| `model_score` | Blended recommendation or bracket density score. |
| `unsupported` | Unsupported theme/source/provider path. |

## Provider Fixtures

| Provider | Fixture | Scenario | Sanitization notes |
| --- | --- | --- | --- |
| Scryfall | FIX-006 | Fake `otag:` search returns a known card that receives source-backed Tagger evidence. | No API key or user data. |
| Scryfall | FIX-006 | Fake cached `otag:` result returns a known card. | Card is labeled as cached source-backed Tagger evidence, not fresh live evidence if cache status is exposed. |
| Local facets | FIX-006 | User/local `tagger.oracle_tags` annotation matches a role target. | Row is labeled local/user annotation, not source-backed Tagger. |
| Embedded taxonomy | FIX-006 | Embedded deterministic taxonomy matches a tag-like rule. | Row is labeled parser-derived or heuristic, not source-backed Tagger. |
| Scryfall | FIX-006 | Fake `otag:` search omits a card that matched embedded heuristics. | Omitted card must not be labeled Tagger-backed. |
| Scryfall | FIX-006 | Fake provider error while fetching Tagger evidence. | Error text must be sanitized and normal tests remain offline. |

## Calibration Or Performance Cases

| Case | Inputs | Expected metrics | Validation |
| --- | --- | --- | --- |
| Bracket 1 baseline | Low-power Commander fixture deck | Estimated bracket remains 1. | Bracket calibration test |
| Bracket 3 baseline | Mid-power Commander fixture deck | Estimated bracket remains near current calibrated value. | Bracket calibration test |
| Bracket 5/cEDH | High-efficiency cEDH-style fixture deck | Estimated bracket can be 5 through explicit criteria. | Bracket calibration test |
| Summary token guard | Default goldfish summary response | Includes compact caveat without evidence rows. | App presenter test |
| Odds provenance detail gate | Deck with known ramp/draw targets | Summary omits success set; normal/full include counted cards. | App/Core tests |
| Classifier hot path | Existing classifier call sites | Cheap classification path remains available without evidence row allocation. | Code inspection or focused perf smoke |
