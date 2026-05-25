# Simulation Profiles and Deck Intent

Simulation profiles are deterministic Commander play-pattern presets. They tune
mulligans, spell sequencing, interaction hold-up, scenario target turns, and
fallback win detection. They do not make `mtg-mcp` a Magic rules engine.

## Built-Ins

- `neutral`: least-assumption default with conservative fallback wins.
- `aggro`: pressure, combat clock, protection, and tempo.
- `combo`: route assembly, tutors, card selection, and protected wins.
- `control`: mana stability, draw, held answers, sweepers, and inevitability.
- `value`: commander midrange, engines, card advantage, and flexible answers.
- `big-mana`: ramp, land drops, large payoffs, and mana scaling.
- `stax`: early asymmetrical hate, parity-breaking, and slower clocks.

`auto` is not a profile. It asks the resolver to infer one from deck facts,
categories, tags, commander signals, and deck intent.

Resolution order:

1. explicit tool argument,
2. `Simulation Profile` in deck intent,
3. auto-inferred profile,
4. `neutral`.

## Deck Intent V2

Deck intent is stored between `MTG MCP Deck Intent` and
`End MTG MCP Deck Intent` in a workspace or Archidekt description. V1 blocks
still parse best-effort, but v2 is preferred.

Common fields:

- `Version`, `Format`, `Commander`, `Goal`, `Power Level`, `Power Target`.
- `Heuristic Profile`, `Simulation Profile`, `Package Template`.
- `Archetype Tags`, `Target Goldfish Turn`, `Local Meta`, `Budget`.
- `Build Targets`, `Simulation`, `Win Routes`, `Prefer`, `Avoid`, `Protect`.

Simulation fields:

- `Commander Dependency`
- `Mulligan Style`
- `Hold Interaction From Turn`
- `Minimum Interaction Held`
- `Prefer Commander On Curve`
- `Accept Shield Down Win Attempt`

Win route lines use this shape:

```text
Route Name: requires commander, card:Altar of the Brood, repeatable-blink; earliest turn 5; kind combo
```

Supported route requirements are `commander`, `repeatable-blink`, `card:<name>`,
`role:<role>`, `tag:<tag>`, `mana>=N`, `tokens>=N`, `interactionHeld>=N`,
`dungeonProgress>=N`, `turn>=N`, or a bare card name.

Win detection order is exact combo evidence when available, deck intent/profile
routes, then conservative fallback pressure heuristics. Fallback route evidence
is labeled as fallback evidence in simulation outputs.

## External Profiles

Built-ins work without files. Optional external profiles are loaded by the app
host from JSON files or simple glob paths:

```json
{
  "MtgMcp": {
    "Simulation": {
      "ProfilePaths": ["profiles/simulation/*.json"],
      "AllowExternalProfileOverrides": true
    }
  }
}
```

Environment aliases:

- `MTGMCP__SIMULATION__PROFILE_PATHS__0`
- `MTGMCP__SIMULATION__ALLOW_EXTERNAL_PROFILE_OVERRIDES`

Profile JSON uses camelCase property names and may contain one profile or an
array:

```json
[
  {
    "id": "blink-combo",
    "name": "Blink Combo",
    "inherits": ["combo"],
    "description": "Combo profile tuned for repeatable blink engines.",
    "themeTags": ["blink", "dungeon"],
    "sequencing": {
      "holdInteractionFromTurn": 3,
      "minimumInteractionHeld": 1,
      "tutorPriority": 1,
      "comboPriority": 1
    },
    "winRoutes": [
      {
        "name": "Altar Blink",
        "kind": "combo",
        "earliestTurn": 5,
        "requirements": ["commander", "repeatable-blink", "card:Altar of the Brood"]
      }
    ]
  }
]
```

The catalog validates duplicate ids, unknown parents, cyclic inheritance, and
unsupported route predicates. Built-ins remain available even when external
profiles fail to load.
