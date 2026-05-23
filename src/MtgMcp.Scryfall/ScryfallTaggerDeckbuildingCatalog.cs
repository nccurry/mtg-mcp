using MtgMcp.Core;

namespace MtgMcp.Scryfall;

/// <summary>
/// Provides curated high-signal Scryfall Tagger oracle tags for deterministic deckbuilding evidence.
/// </summary>
internal static class ScryfallTaggerDeckbuildingCatalog
{
    /// <summary>
    /// Stores fallback slugs that cover common Commander deck construction needs.
    /// </summary>
    private static readonly IReadOnlyList<string> FallbackSlugs =
    [
        "ramp",
        "pure-draw",
        "spot-removal",
        "sweeper",
        "tutor-card",
        "gives-protection",
        "hate-graveyard",
        "card-advantage"
    ];

    /// <summary>
    /// Gets deterministic Scryfall Tagger lookup rules grouped by common deckbuilding language.
    /// </summary>
    public static IReadOnlyList<ScryfallTaggerRule> Rules { get; } =
    [
        Rule("spot-removal", "spot removal", DeckRoles.Interaction, DeckTags.TableInteraction, 4_891, 100, "removal", "interaction", "answer", "answers", "destroy", "exile", "kill"),
        Rule("evasion", "combat evasion", DeckRoles.Synergy, DeckTags.Evasion, 4_428, 78, "evasion", "voltron", "unblockable", "flying", "trample", "menace", "combat"),
        Rule("removal-creature", "creature removal", DeckRoles.Interaction, DeckTags.TableInteraction, 2_489, 92, "removal", "creature removal", "destroy", "exile", "kill"),
        Rule("repeatable-removal", "repeatable removal", DeckRoles.Interaction, DeckTags.TableInteraction, 1_756, 84, "repeatable removal", "removal", "control"),
        Rule("removal-destroy", "destroy removal", DeckRoles.Interaction, DeckTags.TableInteraction, 1_686, 86, "destroy", "removal", "answer"),
        Rule("attack-trigger", "attack triggers", DeckRoles.Payoffs, DeckTags.Engines, 1_908, 86, "attack", "attacks", "combat", "attack trigger"),
        Rule("draw-engine", "draw engine", DeckRoles.Draw, DeckTags.Engines, 1_415, 96, "draw", "card advantage", "cards", "engine", "engines"),
        Rule("repeatable-creature-tokens", "repeatable creature token production", DeckRoles.Synergy, DeckTags.Tokens, 1_407, 96, "tokens", "token", "go wide", "creature tokens", "token generator"),
        Rule("repeatable-lifegain", "repeatable lifegain", DeckRoles.Synergy, DeckTags.Lifegain, 1_346, 90, "lifegain", "life gain", "gain life", "lifelink"),
        Rule("repeatable-pure-draw", "repeatable pure draw", DeckRoles.Draw, DeckTags.CardSelection, 1_270, 92, "draw", "card advantage", "cards", "repeatable draw"),
        Rule("pure-draw", "pure draw", DeckRoles.Draw, DeckTags.CardSelection, 1_257, 100, "draw", "card advantage", "cards", "refill", "gas"),
        Rule("card-advantage", "card advantage", DeckRoles.Draw, DeckTags.CardSelection, 101, 88, "card advantage", "value", "draw", "cards"),
        Rule("cast-trigger-you", "cast triggers from your spells", DeckRoles.Payoffs, DeckTags.Engines, 1_214, 90, "spellslinger", "cast trigger", "magecraft", "storm", "cast"),
        Rule("burn-player", "player burn", DeckRoles.Wincons, DeckTags.Finishers, 1_676, 74, "burn", "damage", "drain", "life loss", "finish"),
        Rule("sacrifice-outlet-creature", "creature sacrifice outlet", DeckRoles.Synergy, DeckTags.SacOutlet, 876, 96, "sacrifice outlet", "sac outlet", "sacrifice", "aristocrats"),
        Rule("attacking-matters-self", "attacking with your own creatures", DeckRoles.Payoffs, DeckTags.Engines, 1_370, 82, "attack", "attacking", "combat", "go wide"),
        Rule("counters-matter", "counters-matter payoffs", DeckRoles.Payoffs, DeckTags.Engines, 1_196, 88, "counter", "counters", "+1/+1", "proliferate"),
        Rule("attacking-matters", "attacking-matters payoffs", DeckRoles.Payoffs, DeckTags.Engines, 1_081, 80, "attack", "attacking", "combat"),
        Rule("burn-creature", "creature burn", DeckRoles.Interaction, DeckTags.TableInteraction, 1_028, 76, "burn", "damage", "removal", "creature removal"),
        Rule("lifegain", "lifegain", DeckRoles.Synergy, DeckTags.Lifegain, 867, 88, "lifegain", "life gain", "gain life", "lifelink"),
        Rule("burn-any", "flexible burn", DeckRoles.Interaction, DeckTags.TableInteraction, 887, 78, "burn", "damage", "removal", "any target"),
        Rule("sweeper", "sweeper", DeckRoles.BoardWipes, DeckTags.TableInteraction, 740, 100, "board wipe", "wipe", "wrath", "sweeper", "reset"),
        Rule("bottomless-mana-sink", "repeatable mana sink", DeckRoles.Payoffs, DeckTags.Engines, 727, 70, "mana sink", "big mana", "x spell", "late game"),
        Rule("combat-trick", "combat trick", DeckRoles.Protection, DeckTags.CombatProtection, 702, 82, "combat trick", "protect", "pump", "combat"),
        Rule("multi-removal", "multi-target removal", DeckRoles.Interaction, DeckTags.TableInteraction, 619, 82, "multi removal", "removal", "interaction"),
        Rule("discard", "discard", DeckRoles.Synergy, DeckTags.Discard, 572, 92, "discard", "wheels", "wheel", "reanimator"),
        Rule("repeatable-sacrifice-outlet", "repeatable sacrifice outlet", DeckRoles.Synergy, DeckTags.SacOutlet, 572, 94, "sacrifice outlet", "sac outlet", "sacrifice", "aristocrats"),
        Rule("burst-draw", "burst draw", DeckRoles.Draw, DeckTags.CardSelection, 534, 88, "burst draw", "draw", "refill", "card advantage"),
        Rule("tutor-to-hand", "tutor to hand", DeckRoles.Tutors, DeckTags.ComboEnabler, 544, 98, "tutor", "search", "find", "combo"),
        Rule("ramp", "mana ramp", DeckRoles.Ramp, DeckTags.ManaFixing, 545, 100, "ramp", "mana", "fixing", "accelerat", "big mana"),
        Rule("land-ramp", "land ramp", DeckRoles.Ramp, DeckTags.ManaFixing, 536, 92, "land ramp", "ramp", "mana", "fixing"),
        Rule("reanimate-creature", "creature reanimation", DeckRoles.Recursion, DeckTags.Reanimation, 519, 86, "reanimate", "reanimation", "graveyard", "recursion"),
        Rule("adds-multiple-mana", "adds multiple mana", DeckRoles.Ramp, DeckTags.ManaFixing, 502, 88, "ramp", "mana", "big mana", "ritual"),
        Rule("discard-outlet", "discard outlet", DeckRoles.Synergy, DeckTags.Discard, 479, 86, "discard outlet", "discard", "reanimator"),
        Rule("scry", "scry", DeckRoles.Draw, DeckTags.CardSelection, 458, 80, "scry", "card selection", "topdeck"),
        Rule("anthem", "anthem effects", DeckRoles.Payoffs, DeckTags.Tokens, 390, 82, "anthem", "tokens", "go wide", "creatures", "combat"),
        Rule("removal-exile", "exile removal", DeckRoles.Interaction, DeckTags.TableInteraction, 440, 86, "exile removal", "exile", "removal", "answer"),
        Rule("gives-trample", "grants trample", DeckRoles.Synergy, DeckTags.Evasion, 443, 72, "trample", "evasion", "voltron", "combat"),
        Rule("mana-dork", "mana dork", DeckRoles.Ramp, DeckTags.ManaFixing, 431, 88, "mana dork", "ramp", "creature ramp"),
        Rule("castable-from-exile", "castable from exile", DeckRoles.Synergy, DeckTags.CardSelection, 426, 76, "exile", "impulse draw", "cast from exile"),
        Rule("cast-on-resolution", "cast on resolution", DeckRoles.Synergy, DeckTags.ComboEnabler, 400, 72, "cast", "spellslinger", "free spell"),
        Rule("free-cast-another", "casts another spell for free", DeckRoles.Synergy, DeckTags.ComboEnabler, 397, 78, "free spell", "cast", "cheat", "combo"),
        Rule("castable-from-graveyard", "castable from graveyard", DeckRoles.Recursion, DeckTags.Reanimation, 381, 84, "graveyard", "recursion", "reanimator", "cast from graveyard"),
        Rule("removal-sacrifice", "sacrifice removal", DeckRoles.Interaction, DeckTags.TableInteraction, 380, 78, "edict", "sacrifice removal", "removal"),
        Rule("damage-prevention", "damage prevention", DeckRoles.Protection, DeckTags.CombatProtection, 373, 78, "damage prevention", "prevent damage", "protection"),
        Rule("removal-bounce", "bounce interaction", DeckRoles.Interaction, DeckTags.TableInteraction, 358, 82, "bounce", "return", "tempo", "interaction"),
        Rule("copy-creature", "creature copying", DeckRoles.Synergy, DeckTags.ComboEnabler, 348, 74, "copy", "clone", "creature copy"),
        Rule("sacrifice-self", "self-sacrifice", DeckRoles.Synergy, DeckTags.SacrificeFodder, 346, 72, "sacrifice", "self sacrifice", "aristocrats"),
        Rule("removal-artifact", "artifact removal", DeckRoles.Interaction, DeckTags.ArtifactEnchantmentHate, 337, 86, "artifact removal", "artifact hate", "artifact", "answer artifacts"),
        Rule("tutor-land-basic", "basic land tutor", DeckRoles.Ramp, DeckTags.ManaFixing, 326, 82, "land ramp", "basic land", "ramp", "fixing"),
        Rule("mill-self", "self mill", DeckRoles.Synergy, DeckTags.Mill, 323, 90, "mill", "self mill", "graveyard"),
        Rule("hate-graveyard", "graveyard hate", DeckRoles.Interaction, DeckTags.GraveyardHate, 301, 98, "graveyard hate", "graveyard", "yards", "exile graveyard"),
        Rule("sacrifice-outlet-artifact", "artifact sacrifice outlet", DeckRoles.Synergy, DeckTags.SacOutlet, 292, 82, "sacrifice outlet", "sac outlet", "sacrifice", "artifact"),
        Rule("surveil", "surveil", DeckRoles.Draw, DeckTags.CardSelection, 281, 78, "surveil", "card selection", "graveyard"),
        Rule("tutor-land-to-battlefield", "land tutor to battlefield", DeckRoles.Ramp, DeckTags.ManaFixing, 275, 82, "land ramp", "ramp", "fixing", "battlefield"),
        Rule("force-draw", "forced draw", DeckRoles.Draw, DeckTags.CardSelection, 276, 68, "force draw", "draw", "wheels"),
        Rule("reanimate-self", "self-recursion", DeckRoles.Recursion, DeckTags.Reanimation, 263, 76, "recursion", "reanimate", "graveyard"),
        Rule("sacrifice-outlet-land", "land sacrifice outlet", DeckRoles.Synergy, DeckTags.SacOutlet, 237, 72, "sacrifice outlet", "sac outlet", "sacrifice", "land"),
        Rule("utility-mana-rock", "utility mana rock", DeckRoles.Ramp, DeckTags.ManaFixing, 233, 84, "mana rock", "ramp", "mana", "artifact ramp"),
        Rule("removal-enchantment", "enchantment removal", DeckRoles.Interaction, DeckTags.ArtifactEnchantmentHate, 220, 86, "enchantment removal", "enchantment hate", "enchantment", "answer enchantments"),
        Rule("restricted-mana", "restricted mana production", DeckRoles.Ramp, DeckTags.ManaFixing, 216, 66, "ramp", "mana", "restricted mana"),
        Rule("sweeper-one-sided", "one-sided sweeper", DeckRoles.BoardWipes, DeckTags.TableInteraction, 215, 84, "one-sided wipe", "board wipe", "wipe", "sweeper"),
        Rule("removal-permanent", "permanent removal", DeckRoles.Interaction, DeckTags.TableInteraction, 209, 82, "permanent removal", "removal", "answer"),
        Rule("temporary-token", "temporary tokens", DeckRoles.Synergy, DeckTags.Tokens, 209, 66, "tokens", "temporary token", "go wide"),
        Rule("activate-from-graveyard", "activated from graveyard", DeckRoles.Recursion, DeckTags.Reanimation, 310, 76, "graveyard", "recursion", "reanimator"),
        Rule("mill-opponent", "opponent mill", DeckRoles.Wincons, DeckTags.Mill, 194, 84, "mill", "mill opponent", "self mill"),
        Rule("repeatable-treasures", "repeatable treasure production", DeckRoles.Ramp, DeckTags.ManaFixing, 193, 92, "treasure", "treasures", "ramp", "mana", "artifact token"),
        Rule("mill-any", "flexible mill", DeckRoles.Synergy, DeckTags.Mill, 187, 76, "mill", "self mill", "graveyard"),
        Rule("graveyard-fuel", "graveyard fuel", DeckRoles.Synergy, DeckTags.Reanimation, 180, 76, "graveyard", "self mill", "fuel"),
        Rule("free-sacrifice-outlet", "free sacrifice outlet", DeckRoles.Synergy, DeckTags.SacOutlet, 177, 92, "free sacrifice outlet", "sac outlet", "sacrifice", "aristocrats", "combo"),
        Rule("synergy-equipment", "equipment synergy", DeckRoles.Synergy, DeckTags.Voltron, 171, 92, "equipment", "equip", "voltron", "aura", "weapons"),
        Rule("tutor-to-battlefield", "tutor to battlefield", DeckRoles.Tutors, DeckTags.ComboEnabler, 170, 82, "tutor", "search", "battlefield", "combo"),
        Rule("cards-in-graveyard-matter", "cards in graveyard matter", DeckRoles.Payoffs, DeckTags.Reanimation, 168, 74, "graveyard", "self mill", "delirium", "threshold"),
        Rule("repeatable-artifact-tokens", "repeatable artifact token production", DeckRoles.Synergy, DeckTags.Tokens, 167, 84, "tokens", "artifact tokens", "treasure", "clue", "food", "blood"),
        Rule("force-attacker", "forces attacks", DeckRoles.Interaction, DeckTags.Politics, 165, 68, "goad", "force attack", "politics"),
        Rule("counterspell-with-set-mechanic", "counterspell with set mechanic", DeckRoles.Interaction, DeckTags.TableInteraction, 164, 72, "counterspell", "counter magic", "permission", "interaction"),
        Rule("repeatable-impulsive-draw", "repeatable impulsive draw", DeckRoles.Draw, DeckTags.CardSelection, 153, 76, "impulse draw", "exile draw", "cast from exile"),
        Rule("cost-reducer", "cost reducer", DeckRoles.Ramp, DeckTags.ComboEnabler, 154, 80, "cost reducer", "discount", "spellslinger", "tribal", "ramp"),
        Rule("copy-spell", "spell copy", DeckRoles.Synergy, DeckTags.ComboEnabler, 147, 84, "copy spell", "copy", "spellslinger", "storm"),
        Rule("trigger-from-graveyard", "graveyard trigger", DeckRoles.Synergy, DeckTags.Reanimation, 143, 68, "graveyard", "recursion", "trigger"),
        Rule("counterspell-soft", "soft counterspell", DeckRoles.Interaction, DeckTags.TableInteraction, 133, 72, "counterspell", "tax", "permission"),
        Rule("gives-protection", "grants protection", DeckRoles.Protection, DeckTags.CombatProtection, 127, 96, "protection", "protect", "hexproof", "indestructible", "ward"),
        Rule("tutored-by-name", "named-card tutor", DeckRoles.Tutors, DeckTags.ComboEnabler, 118, 72, "tutor", "search", "named card"),
        Rule("mass-land-denial", "mass land denial", DeckRoles.Interaction, DeckTags.Stax, 109, 80, "stax", "land destruction", "mass land denial", "lock"),
        Rule("tutor-card", "card tutor", DeckRoles.Tutors, DeckTags.ComboEnabler, 110, 100, "tutor", "search", "find", "combo"),
        Rule("mana-rock", "mana rock", DeckRoles.Ramp, DeckTags.ManaFixing, 107, 82, "mana rock", "ramp", "artifact ramp"),
        Rule("card-types-in-graveyard-matter", "card types in graveyard matter", DeckRoles.Payoffs, DeckTags.Reanimation, 105, 72, "graveyard", "delirium", "self mill"),
        Rule("lockdown-creature", "creature lockdown", DeckRoles.Interaction, DeckTags.Stax, 105, 76, "stax", "lockdown", "lock", "creature lockdown"),
        Rule("synergy-token-creature", "creature token synergy", DeckRoles.Synergy, DeckTags.Tokens, 109, 78, "tokens", "creature tokens", "go wide"),
        Rule("synergy-token", "token synergy", DeckRoles.Synergy, DeckTags.Tokens, 104, 76, "tokens", "token", "go wide"),
        Rule("tutor-creature", "creature tutor", DeckRoles.Tutors, DeckTags.ComboEnabler, 99, 70, "creature tutor", "tutor", "search"),
        Rule("counterspell", "counterspell", DeckRoles.Interaction, DeckTags.TableInteraction, 95, 96, "counterspell", "counter magic", "permission", "interaction"),
        Rule("curiosity", "curiosity-style combat draw", DeckRoles.Draw, DeckTags.CardSelection, 208, 78, "curiosity", "draw", "combat damage", "voltron"),
        Rule("combat-ramp", "combat ramp", DeckRoles.Ramp, DeckTags.ManaFixing, 194, 74, "combat ramp", "attack", "ramp", "treasure"),
        Rule("blink", "blink effects", DeckRoles.Protection, DeckTags.Blink, 163, 84, "blink", "flicker", "enter the battlefield", "etb", "protection"),
        Rule("consult-cast", "consult-style casting", DeckRoles.Tutors, DeckTags.ComboEnabler, 128, 76, "consult", "combo", "tutor"),
        Rule("creates-token-of-a-card", "creates token copies of cards", DeckRoles.Synergy, DeckTags.Tokens, 72, 66, "tokens", "copy token", "token copy"),
        Rule("quick-equip", "quick equip", DeckRoles.Synergy, DeckTags.Voltron, 100, 82, "equipment", "equip", "voltron", "quick equip"),
        Rule("repeatable-clues", "repeatable clue production", DeckRoles.Draw, DeckTags.CardSelection, 86, 72, "clue", "clues", "artifact token", "draw"),
        Rule("synergy-food", "food synergy", DeckRoles.Synergy, DeckTags.Lifegain, 88, 66, "food", "lifegain", "artifact token"),
        Rule("impulsive-draw", "impulsive draw", DeckRoles.Draw, DeckTags.CardSelection, 87, 70, "impulse draw", "exile draw", "cast from exile"),
        Rule("alternate-win-condition", "alternate win condition", DeckRoles.Wincons, DeckTags.Finishers, 82, 88, "alternate win", "win condition", "wincon", "combo"),
        Rule("counterspell-reusable", "repeatable counterspell", DeckRoles.Interaction, DeckTags.TableInteraction, 76, 70, "counterspell", "repeatable", "permission"),
        Rule("copy-token", "token copying", DeckRoles.Synergy, DeckTags.Tokens, 55, 70, "copy token", "tokens", "copy", "populate"),
        Rule("synergy-treasure", "treasure synergy", DeckRoles.Synergy, DeckTags.ManaFixing, 51, 74, "treasure", "treasures", "artifact token"),
        Rule("wheel-one-sided", "one-sided wheel", DeckRoles.Draw, DeckTags.Discard, 45, 72, "wheel", "discard", "draw"),
        Rule("miniwheel", "mini wheel", DeckRoles.Draw, DeckTags.Discard, 44, 70, "wheel", "discard", "draw"),
        Rule("wheel-symmetrical", "symmetrical wheel", DeckRoles.Draw, DeckTags.Discard, 38, 68, "wheel", "discard", "draw"),
        Rule("tax-attack", "attack tax", DeckRoles.Interaction, DeckTags.Stax, 40, 76, "stax", "tax", "pillowfort", "attack tax"),
        Rule("auto-equip", "auto equip", DeckRoles.Synergy, DeckTags.Voltron, 56, 76, "equipment", "equip", "voltron", "auto equip"),
        Rule("french-vanilla-equipment", "keyword equipment", DeckRoles.Synergy, DeckTags.Voltron, 50, 64, "equipment", "equip", "voltron"),
        Rule("copy-equipment", "equipment copying", DeckRoles.Synergy, DeckTags.Voltron, 28, 64, "equipment", "equip", "copy equipment"),
        Rule("alternate-equip-cost", "alternate equip cost", DeckRoles.Synergy, DeckTags.Voltron, 27, 68, "equipment", "equip", "voltron"),
        Rule("blood-artist-ability", "Blood Artist-style drain", DeckRoles.Payoffs, DeckTags.Drain, 29, 72, "aristocrats", "blood artist", "drain", "sacrifice"),
        Rule("repeatable-food", "repeatable food production", DeckRoles.Synergy, DeckTags.Lifegain, 59, 66, "food", "lifegain", "artifact token"),
        Rule("rule-of-law", "Rule of Law effect", DeckRoles.Interaction, DeckTags.Stax, 13, 82, "stax", "rule of law", "lock", "tax"),
        Rule("cast-tax", "cast tax", DeckRoles.Interaction, DeckTags.Stax, 17, 78, "stax", "tax", "cast tax", "permission"),
        Rule("lockdown-artifact", "artifact lockdown", DeckRoles.Interaction, DeckTags.Stax, 15, 68, "stax", "lockdown", "lock", "artifact lockdown"),
        Rule("lockdown-land", "land lockdown", DeckRoles.Interaction, DeckTags.Stax, 10, 70, "stax", "lockdown", "lock", "land lockdown"),
        Rule("convoke", "convoke", DeckRoles.Ramp, DeckTags.ManaFixing, 110, 70, "convoke", "tokens", "go wide", "cost"),
        Rule("cheat-death", "cheat death", DeckRoles.Protection, DeckTags.Reanimation, 94, 76, "cheat death", "recursion", "reanimation", "protect"),
        Rule("bombard", "sacrifice payoff", DeckRoles.Synergy, DeckTags.Aristocrats, 83, 72, "sacrifice", "aristocrats", "dies", "death trigger"),
        Rule("creaturefall", "creaturefall payoffs", DeckRoles.Payoffs, DeckTags.Tokens, 530, 72, "creaturefall", "tokens", "creatures entering", "go wide")
    ];

    /// <summary>
    /// Gets fallback rules for broad deck analysis when the user goal has no exact tag-language match.
    /// </summary>
    public static IReadOnlyList<ScryfallTaggerRule> FallbackRules { get; } = FallbackSlugs
        .Select(slug => Rules.First(rule => rule.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    /// <summary>
    /// Creates one catalog rule.
    /// </summary>
    private static ScryfallTaggerRule Rule(
        string slug,
        string description,
        string role,
        string secondaryTag,
        int? taggingCount,
        int priority,
        params string[] needles)
    {
        return new ScryfallTaggerRule(slug, description, role, secondaryTag, taggingCount, priority, needles);
    }
}

/// <summary>
/// Describes one deterministic Scryfall Tagger lookup rule.
/// </summary>
internal sealed record ScryfallTaggerRule(
    string Slug,
    string Description,
    string Role,
    string SecondaryTag,
    int? TaggingCount,
    int Priority,
    IReadOnlyList<string> Needles)
{
    /// <summary>
    /// Gets whether this rule matches query text.
    /// </summary>
    public bool Matches(string text)
    {
        return Needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
