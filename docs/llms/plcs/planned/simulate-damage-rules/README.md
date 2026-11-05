# Simulate Damage Rules Plan

## Current Work

- Status: Planned (draft for owner review)
- Owner: mtg-mcp
- Last updated: 2026-11-01, drafted on `main` at `c4fa0f1`; one independent
  review pass, findings fixed
- Implementation authorized: No. The owner must accept the product-rule report
  and answer the open questions first.
- Selected work and endpoint: none yet. When authorized, phases 1 to 3, merged
  into `main` and shipped in the next release.
- Next action and blocker: owner review of [Product rule report](#product-rule-report)
  and [Open questions](#open-questions).
- Resume check: this packet is in `planned/`; no implementation worktree for
  `simulate-damage-rules` exists.

## Outcome And Requirements

Agents tuning a Wilson, Refined Grizzly voltron deck asked for "the chance of 21
commander damage by turn N" (reported in the `deck-edit-ergonomics` planning
session, 2026-11-01). They had to work it out by hand: `stats_simulate` has
rules for draws, plays, casts, mana, and counts, and nothing for the commander,
counters, attachments, or damage. This packet adds caller-stated rules for
those, so one request can sample that question.

The answer is an estimate of unopposed damage from one attacker: no blockers,
removal, or opponent actions. It is not a kill turn against real opponents or a
win rate.

| ID | Required behavior | Acceptance criteria |
| --- | --- | --- |
| REQ-001 | A group can take `zone: "commander"`, which selects commander-zone entries by `entry-ids` only. Those cards start each game in the command zone: rules take them from there as from hand; they are never shuffled, drawn, bottomed, or counted as seen. Omitting `zone` (or `"main"`) keeps today's behavior, including the `entry-not-in-main-zone` refusal. Commander entries count toward the 150-entry and 500-card limits and the samples x turns x cards work limit. | Tests: a commander card is cast on the first turn the mana allows, from any opening hand; it never appears in `hands`, `drawn`, or `bottomed`; `category-ids` with `zone: "commander"` is refused; a main group naming a commander entry is still refused; an empty main zone is still refused; limit tests count commander cards. |
| REQ-002 | Checks and settings that read the hand or library refuse a commander group, each with reason `commander-group-not-allowed` and the path: `count-between`, `drawn-at-least`, `bottom.preferGroups`, and a `can-cast` `inHand` naming a commander entry. A rule that moves a commander card must state `addsMana` (no default), because a default of 1 would quietly add mana for a creature. | One refusal test for each; a commander `cast-if-able` without `addsMana` is refused. |
| REQ-003 | A new `eachTurn` rule `attach` casts a card of its group for `cost` (0 to 99) generic mana and attaches it to the one card of its `target` group, adding `power` (0 to 99) while both are in play. It fires only while the target's card is in play. `pick` is `random` or `listed-order`; `target-first` is refused. The attached card counts as in play for `on-battlefield-at-least`. | Fixed-order tests: an aura in hand before the target is in play is not cast; after the target enters it is cast and adds its power; `maxPerTurn` and `cost` limit it as `cast-if-able` does; `target-first` is refused. |
| REQ-004 | A new top-level list `triggers` (at most 20) holds `add-counters` triggers, apart from `eachTurn` because they do not run in its listed order. Each puts `amount` (1 to 99) +1/+1 counters on the one card of its `target` group while a card of its `source` group is in play. `on` is required: `turn-start` fires once at the start of each turn, before the draw, using what was in play then; `target-enters` fires once each time the target's card enters play, if a source card is already in play. Triggers of one kind fire in listed order. | Tests reproduce Noble Heritage: Wilson entering after the Background gets 2 at once; Wilson entering before it gets none until the next turn start; each later turn adds 2. |
| REQ-005 | A new rule `attack` makes the one card of its group deal damage equal to its power when it is in play and may attack: `attacksFrom: "same-turn"` (haste) or `"next-turn"`. Power is the caller's `power` (0 to 99) plus counters plus attachment bonuses. It fires once a turn at its place in `eachTurn`, so rules listed after it act after combat. A second `attack` rule for the same group is refused (`duplicate-attack`). | Fixed-order tests: no damage the turn it enters with `next-turn`; damage that turn with `same-turn`; counters and attachments before the rule count, those after it do not; a duplicate is refused. |
| REQ-006 | A new check `damage-at-least` passes when the card of its `group` has dealt at least `amount` damage (1 to 999) by `byTurn`. Commander damage is this check on a commander group. | The two worked fixtures below give exactly the listed totals; both pass `amount: 21, byTurn: 6` and fail `byTurn: 5`. |
| REQ-007 | `target`, `source`, and the `attack` group each hold exactly one card (one entry, quantity 1); anything else is refused with `target-not-one-card` naming the group. | Refusal tests for two entries and for quantity 2. |
| REQ-008 | Returned sample games add an `attacks` list to each turn record: rule index, entry ID, power, and damage (power times strikes; 1 strike unless REQ-011 is accepted). For requests without `attack` rules the list is empty and every other field is unchanged. | Result tests; the output schema baseline is updated. |
| REQ-009 | Existing requests keep their results: the same hits and the same returned games apart from the empty `attacks` lists. New rules use random values only through `pick`, as existing rules do; `RandomVersion` stays `splitmix64-v1`. The fingerprint also hashes the commander-zone entries, each group's zone, and the new rules and triggers. | A replay test of existing fixtures compares hits and games (not fingerprints); a test shows a main group and a commander group with the same IDs give different fingerprints. |
| REQ-010 | The description lists the new fixed engine rules (command zone, trigger timing, one-card targets, unopposed attacks), rewrites rule 1 ("Groups pick main-zone entries") and the `skipped` wording for commander groups, and stays within 4,096 characters. When a request has an `attack` rule, the result `limits` include "Unopposed damage: no blockers, removal, life totals, or opponent actions are modeled." README gains the Wilson example as a checked `<!-- example: stats_simulate -->` block. CHANGELOG lists the new kinds, fields, and output list, and says fingerprints change if `ModelVersion` is bumped. | Description length test; `limits` test with and without `attack`; README example test; budget test that the work estimate counts the new rules and triggers; doc review. |

Should (owner decides, see [Open questions](#open-questions)):

- REQ-011: `attach` takes an optional `doubleStrike: true`, which makes the
  target deal its damage twice in each attack.
- REQ-012: `attach` takes an optional `powerPerInPlay` (`groups`, `power`) that
  adds `power` for each other card in play in the listed groups, for cards such
  as Ancestral Mask. Opponents' permanents are not counted.

Non-goals:

- Reading power, counters, or bonuses from card text or installed card data.
  `MtgMcp.Simulation` stays free of Scryfall; the caller reads facts with
  `scryfall_cards_get` and states them.
- Opponents, blockers, removal, life totals, poison, lifelink, extra combats,
  commander tax, or moving equipment between creatures.
- Several attackers or total damage from a creature deck; targets and attackers
  are one card each in this version.
- A `voltron` or `commander-damage` preset. The README example replaces it.
- Colored costs for rules that spend mana. `cast-if-able` and `attach` spend
  generic mana, as today; `can-cast` remains the color check.

## Product Rule Report

The product rules allow this work, with one wording change for the owner to
approve.

- **Allowed.** `AGENTS.md` permits one seeded sampler that "applies only rules
  the request states" and says "add a rule kind as one record, one evaluator
  case, and fixtures". Each addition here is one input record, one engine
  record, and one evaluator case (`attach`, `add-counters`, `attack`,
  `damage-at-least`), plus the command zone. Every Magic-specific number is a
  request field: base power, power per attachment, counters, trigger timing,
  haste, and costs.
- **Fixed engine rules stay listed.** The new fixed rules are the command zone
  (REQ-001), trigger timing (REQ-004), one-card targets (REQ-007), and
  unopposed attacks (REQ-005). Each goes in the tool description.
- **No hidden defaults.** The only defaulted field is `zone`, whose default is
  today's behavior. `on`, `attacksFrom`, and `addsMana` on commander rules are
  required because they change results. The Wilson setup is a documented example
  request, not a preset.
- **No strategic simulation.** The engine never chooses whether to attack or
  what to attach to: a listed `attack` always attacks, and a one-card target
  leaves no choice. No opponent is modeled, so nothing claims a matchup result.
- **Needs owner approval.** `docs/north-star.md` says `stats_simulate` "is not a
  general game simulator or rules engine" (line 45) and that "the one approved
  sampled estimate is land-only and bounded" (lines 61-62);
  `docs/potential-features.md` says "broad multiplayer or full-game simulation
  remains deferred". The land-only text was already out of date after
  `draw-cards` shipped in `0.12.0`. Phase 3 rewrites both north-star passages to
  "caller-ruled and bounded: unopposed damage from caller-stated power is
  allowed; opponents and combat decisions are not". If the owner reads combat
  damage as strategic or full-game simulation, this packet stops here.
- **Not a rules engine.** `docs/design-goals.md` bans a generic rules engine or
  expression language. `powerPerInPlay` (REQ-012) is a fixed formula with no
  expressions; it is optional so the owner can drop it.

## Current System

Inspected at `c4fa0f1` (read-only):

- Input kinds are a closed JSON union in
  `src/MtgMcp.App/Statistics/StatisticsSimulateInputs.cs`: rules `play`,
  `cast-if-able`, `draw-cards` (`:248-252`); checks `all`, `any`,
  `mana-at-least`, `drawn-at-least`, `on-battlefield-at-least`, `can-cast`
  (`:359-366`); keep checks add `count-between` (`:92-96`); preset `on-curve`.
- Every rule input inherits required `group`, `pick`, and `maxPerTurn` from
  `SimulationRuleInput` (`:252-274`); `cast-if-able` and `play` default
  `addsMana` to 1 (`:285`).
- Engine models and the `SimulationRule` and `SimulationCheck` unions are in
  `src/MtgMcp.Simulation/SimulationModels.cs:242,287`. `SimulationDeckEntry`
  has no zone (`:152`). A game is `SimulationTrial.Run` (`SimulationTrial.cs:156-189`),
  which puts every card into the library (`:199-203`); a turn is draw, rules in
  order, then leaf checks (`:281-303`). State is per group (`inPlayByGroup`,
  `seenByGroup`) and mana producers; nothing is kept per card.
- `pick: random` draws one random value even when one card qualifies
  (`OnCurveRandom.cs:90-103`).
- The coordinator reads only the main zone
  (`StatisticsSimulateCoordinator.cs:87-107`); an `entry-ids` group naming
  another zone is refused with `entry-not-in-main-zone`
  (`SimulationRequestMapper.cs:220-250`). Local decks use zone `commander`.
- `SimulationEngine.ModelVersion` is `simulation-v1` and `RandomVersion`
  `splitmix64-v1`; both enter the fingerprint (`SimulationFingerprint.cs:21-22`).
- The live tool description is about 3,900 of 4,096 characters, about 1,300 of
  them the full example (`StatisticsSimulateTools.cs:28-43,69-96`).
- Card facts for the example (installed data, 2026-11-01): Wilson, Refined
  Grizzly `{1}{G}` 2/2 reach, vigilance, trample, ward 2; Noble Heritage `{1}{W}`
  Background: commander creatures you own have "When this creature enters and
  at the beginning of your upkeep, each player may put two +1/+1 counters on a
  creature they control"; Rancor `{G}` +2/+0; Shadowspear `{1}`, equip `{2}`,
  +1/+1; Ancestral Mask `{2}{G}` +2/+2 for each other enchantment.

## Design

### Records and evaluator cases

Input bases: `SimulationRuleInput` becomes an empty union base;
`SimulationCardRuleInput` takes its `group`, `pick`, and `maxPerTurn`, and
`play`, `cast-if-able`, `draw-cards`, and `attach` derive from it. A schema test
holds the three existing kinds' JSON schemas unchanged.

| Kind | Input record (App) | Engine record (Simulation) | Evaluator case |
| --- | --- | --- | --- |
| group `zone` | `SimulationGroupInput.Zone` (`main`, `commander`) | `SimulationRequest.CommandZone` (a separate `SimulationDeckEntry` list) and `SimulationGroup.CommandZone` | The trial fills a `commandZone` list at game start, never in the library; `Choose` looks there for commander groups. |
| `attach` | `SimulationAttachInput : SimulationCardRuleInput` (`cost`, `target`, `power`) | `SimulationAttach(Group, Pick, MaxPerTurn, Cost, Target, Power)` | `SimulationRuleKind.Attach`: skip unless the target card is in play; spend cost; move to play; add `power` to the target's bonus. |
| `add-counters` | `SimulationAddCountersInput` in the new `triggers` list (`target`, `source`, `amount`, `on`) | `SimulationAddCounters(Target, Source, Amount, SimulationTriggerOn)` in `SimulationRequest.Triggers` | `turn-start`: a pass before the draw. `target-enters`: run when a rule moves the target's card to play. |
| `attack` | `SimulationAttackInput : SimulationRuleInput` (`group`, `power`, `attacksFrom`) | `SimulationAttack(Group, Power, SimulationAttackFrom)` | `SimulationRuleKind.Attack`: if the card is in play and may attack, add its damage and record the attack. |
| `damage-at-least` | `SimulationDamageAtLeastInput : SimulationCheckInput` (`group`, `amount`, `byTurn`) | `SimulationDamageAtLeast(Group, Amount, ByTurn)` | `SimulationLeafKind.DamageAtLeast`: the group card's damage is at least `amount`. |

`SimulationAttackFrom` (`SameTurn`, `NextTurn`) is its own enum so mana timing
and attack timing can change apart. The trial gains, for each one-card target
group: counters, attachment bonus, damage, and the turn it entered.

### Turn order

1. `turn-start` triggers, in listed order.
2. Draw (unchanged).
3. `eachTurn` rules in listed order. When `play`, `cast-if-able`, or `attach`
   moves a target's card to play, its `target-enters` triggers fire at once.
   `attack` deals damage at its place in the list.
4. Leaf checks (unchanged), now including `damage-at-least`.

### What stays caller-stated

Base power; each attachment's power, cost (cast plus equip for equipment), and
target; which card the counters go on, how many, which group's presence turns
them on, and when they trigger; whether the attacker has haste; the order of
rules, which places combat; the costs, mana, and timing of the commander and
any Background. The engine never reads card text, legality, or installed data.

### Documented example request

This goes into README as a checked example; IDs are placeholders.

```json
{
  "deck": { "deckId": "0193e97a-ef95-7700-8000-000000000001" },
  "groups": [
    { "id": "lands", "select": { "kind": "category-ids", "ids": ["0193e97a-ef95-7700-8000-000000000021"] } },
    { "id": "wilson", "zone": "commander", "select": { "kind": "entry-ids", "ids": ["0193e97a-ef95-7700-8000-000000000031"] } },
    { "id": "heritage", "zone": "commander", "select": { "kind": "entry-ids", "ids": ["0193e97a-ef95-7700-8000-000000000032"] } },
    { "id": "auras", "select": { "kind": "entry-ids", "ids": ["0193e97a-ef95-7700-8000-000000000033"] } },
    { "id": "equipment", "select": { "kind": "entry-ids", "ids": ["0193e97a-ef95-7700-8000-000000000034"] } }
  ],
  "openingHand": {
    "size": 7, "onThePlay": true,
    "mulligan": { "rule": "london", "freeFirstMulligan": true, "maxMulligans": 2,
      "keepWhen": { "kind": "count-between", "group": "lands", "min": 2, "max": 5 },
      "bottom": { "preferGroups": [], "otherwise": "random" } }
  },
  "triggers": [
    { "kind": "add-counters", "target": "wilson", "source": "heritage", "amount": 2, "on": "turn-start" },
    { "kind": "add-counters", "target": "wilson", "source": "heritage", "amount": 2, "on": "target-enters" }
  ],
  "eachTurn": [
    { "kind": "play", "group": "lands", "pick": "random", "maxPerTurn": 1, "manaFrom": "same-turn" },
    { "kind": "cast-if-able", "group": "heritage", "pick": "listed-order", "maxPerTurn": 1, "cost": 2, "addsMana": 0, "manaFrom": "same-turn" },
    { "kind": "cast-if-able", "group": "wilson", "pick": "listed-order", "maxPerTurn": 1, "cost": 2, "addsMana": 0, "manaFrom": "same-turn" },
    { "kind": "attach", "group": "auras", "pick": "random", "maxPerTurn": 2, "cost": 1, "target": "wilson", "power": 2 },
    { "kind": "attach", "group": "equipment", "pick": "random", "maxPerTurn": 1, "cost": 3, "target": "wilson", "power": 1 },
    { "kind": "attack", "group": "wilson", "power": 2, "attacksFrom": "next-turn" }
  ],
  "turns": 10,
  "conditions": [
    { "id": "lethal-commander-damage-t8", "check": { "kind": "damage-at-least", "group": "wilson", "amount": 21, "byTurn": 8 } },
    { "id": "lethal-commander-damage-t10", "check": { "kind": "damage-at-least", "group": "wilson", "amount": 21, "byTurn": 10 } }
  ],
  "samples": 20000,
  "sampleHands": 2
}
```

Here `auras` is Rancor (cost 1, +2) and `equipment` is Shadowspear (cast 1 plus
equip 2, +1). Ancestral Mask needs REQ-012. Casting Heritage before Wilson is
the caller's choice of rule order. The commander groups use `listed-order`,
which uses no random value.

### Worked fixtures

Both use `KeepDeckOrder`, on the play, no mulligan, and no equipment rule. The
command zone holds Wilson and Noble Heritage. The main zone, from the top: land,
land, land, Rancor, three cards in no group (the opening hand), then five lands
(drawn on turns 2 to 6). One land is played a turn, so the mana on turn N is N.

Fixture 1, rules as in the example (Heritage before Wilson):

| Turn | Mana | Events | Power at attack | Damage total |
| --- | --- | --- | --- | --- |
| 1 | 1 | land | - | 0 |
| 2 | 2 | Heritage | - | 0 |
| 3 | 3 | Wilson enters, `target-enters` +2; Rancor +2 | 6, but `next-turn`: no attack | 0 |
| 4 | 4 | `turn-start` +2 | 2 + 4 + 2 = 8 | 8 |
| 5 | 5 | `turn-start` +2 | 10 | 18 |
| 6 | 6 | `turn-start` +2 | 12 | 30 |

Fixture 2, the Wilson rule listed before the Heritage rule: turn 2 Wilson (no
`target-enters` counters, no Heritage); turn 3 Heritage and Rancor, attack at
2 + 2 = 4 (total 4); turns 4 to 6 `turn-start` +2 each, attacks of 6, 8, 10
(totals 10, 18, 28).

Both pass `damage-at-least 21` with `byTurn: 6` and fail with `byTurn: 5`. They
are exact tests in `tests/MtgMcp.Simulation.Tests/SimulationRuleTests.cs`.

## Steps And Verification

1. Phase 1, engine (REQ-001 to REQ-007 and REQ-009 engine parts):
   `SimulationModels`, `SimulationPlan`, `SimulationPlanner` (validation,
   refusals, one-card targets, work estimate), `SimulationTrial`,
   `SimulationFingerprint`; fixed-order fixtures, validation and budget tests,
   replay test of existing fixtures.
2. Phase 2, App (REQ-001, REQ-002, REQ-008, REQ-010): input bases and records,
   `SimulationRequestMapper` and the coordinator read the commander zone for
   commander groups, result `attacks` and `limits`, description, README example,
   CHANGELOG.
3. Phase 3, docs and release: `north-star.md`, `potential-features.md`,
   `design-goals.md` wording; full audit; merge, release, install.

Each phase: focused tests, then `task ci`, `task test:integration`, and
`task test:e2e` from PowerShell.

## Risks

- Agents may read the rate as a real kill turn. The description, README, and
  result `limits` say the damage is unopposed.
- The description budget is tight. Phase 2 moves the full example to README and
  keeps the minimal one, freeing about 1,300 characters.
- One-card targets fit voltron only. Lifting that later needs a fixed or
  caller-stated choice of which creature an aura goes on.

## Open Questions

1. Does the owner accept unopposed combat damage under the north-star wording
   in [Product rule report](#product-rule-report)? Recommended: yes, with the rewrite.
2. Bump `ModelVersion` to `simulation-v2`? Old requests give the same numbers,
   but the fixed engine rules change and every fingerprint changes with the
   bump. Recommended: bump, so a result names the rule set that produced it;
   `RandomVersion` stays.
3. Include REQ-011 (`doubleStrike`) and REQ-012 (`powerPerInPlay`)? Both are
   common in voltron decks. Recommended: REQ-011 yes (one boolean), REQ-012 only
   if the owner wants Ancestral Mask now.
4. Equipment: keep one `cost` (cast plus equip), or add `equipCost` so an
   equipment can be cast before the creature arrives? Recommended: one `cost`.
5. Should `attach` and the commander casts check colored costs? Recommended:
   no; that changes every mana-spending rule and belongs in its own plan.
6. Partner commanders: each in its own group works as written. Should a
   commander group holding two entries be refused? Recommended: allow it for
   `cast-if-able`; REQ-007 already refuses it as an attack or target group.
7. `pick` on one-card commander groups: keep it required (as written; use
   `listed-order` to avoid a random value), or fix it for commander groups?
   Recommended: keep it required, so all card rules share one shape.
8. Release: additive input kinds and output fields fit a minor release under
   `docs/versioning.md`. Ship in the next release with other planned work.

## Evidence And Completion

| Date | Check | Result |
| --- | --- | --- |
| 2026-11-01 | Independent review of the draft | 12 findings and 3 missing questions; all fixed in this revision (REQ-009 random claim, input bases, command-zone data path, fingerprint, trigger placement, refusals, output compatibility, bounds, fixture order, north-star line 45, attack enum, damage field). |

| Requirement | State | Evidence |
| --- | --- | --- |
| REQ-001 to REQ-012 | planned | none yet |

## Log

- 2026-11-05 Converted to the shared PLC template (format only).
