namespace MtgMcp.Core;

/// <summary>
/// Maps high-signal Scryfall Tagger oracle-card slugs into mtg-mcp's role taxonomy.
/// </summary>
public static class DeckTaggerTaxonomy
{
    /// <summary>
    /// Gets canonical oracle-card tag rules selected from the saved Scryfall Tagger snapshot.
    /// </summary>
    public static IReadOnlyList<DeckTaggerRule> Rules { get; } =
    [
        Rule("spot-removal", DeckRoles.Interaction, DeckTags.TableInteraction, 100),
        Rule("removal-creature", DeckRoles.Interaction, DeckTags.TableInteraction, 92),
        Rule("repeatable-removal", DeckRoles.Interaction, DeckTags.TableInteraction, 84),
        Rule("removal-destroy", DeckRoles.Interaction, DeckTags.TableInteraction, 86),
        Rule("removal-exile", DeckRoles.Interaction, DeckTags.TableInteraction, 86),
        Rule("removal-sacrifice", DeckRoles.Interaction, DeckTags.TableInteraction, 78),
        Rule("removal-bounce", DeckRoles.Interaction, DeckTags.TableInteraction, 82),
        Rule("removal-artifact", DeckRoles.Interaction, DeckTags.ArtifactEnchantmentHate, 86),
        Rule("removal-enchantment", DeckRoles.Interaction, DeckTags.ArtifactEnchantmentHate, 86),
        Rule("removal-permanent", DeckRoles.Interaction, DeckTags.TableInteraction, 82),
        Rule("counterspell", DeckRoles.Interaction, DeckTags.TableInteraction, 96),
        Rule("counterspell-soft", DeckRoles.Interaction, DeckTags.TableInteraction, 72),
        Rule("counterspell-with-set-mechanic", DeckRoles.Interaction, DeckTags.TableInteraction, 72),
        Rule("hate-graveyard", DeckRoles.Interaction, DeckTags.GraveyardHate, 98),
        Rule("hate-treasure", DeckRoles.Interaction, DeckTags.ArtifactEnchantmentHate, 80),
        Rule("mass-land-denial", DeckRoles.Interaction, DeckTags.Stax, 80),
        Rule("rule-of-law", DeckRoles.Interaction, DeckTags.Stax, 82),
        Rule("cast-tax", DeckRoles.Interaction, DeckTags.Stax, 78),
        Rule("lockdown-creature", DeckRoles.Interaction, DeckTags.Stax, 76),
        Rule("lockdown-artifact", DeckRoles.Interaction, DeckTags.Stax, 68),
        Rule("lockdown-land", DeckRoles.Interaction, DeckTags.Stax, 70),

        Rule("sweeper", DeckRoles.BoardWipes, DeckTags.TableInteraction, 100),
        Rule("sweeper-one-sided", DeckRoles.BoardWipes, DeckTags.TableInteraction, 84),

        Rule("ramp", DeckRoles.Ramp, DeckTags.ManaFixing, 100),
        Rule("land-ramp", DeckRoles.Ramp, DeckTags.ManaFixing, 92),
        Rule("adds-multiple-mana", DeckRoles.Ramp, DeckTags.ManaFixing, 88),
        Rule("mana-dork", DeckRoles.Ramp, DeckTags.ManaFixing, 88),
        Rule("mana-rock", DeckRoles.Ramp, DeckTags.ManaFixing, 82),
        Rule("utility-mana-rock", DeckRoles.Ramp, DeckTags.ManaFixing, 84),
        Rule("repeatable-treasures", DeckRoles.Ramp, DeckTags.ManaFixing, 92),
        Rule("combat-ramp", DeckRoles.Ramp, DeckTags.ManaFixing, 74),
        Rule("cost-reducer", DeckRoles.Ramp, DeckTags.ComboEnabler, 80),
        Rule("convoke", DeckRoles.Ramp, DeckTags.ManaFixing, 70),
        Rule("tutor-land-basic", DeckRoles.Ramp, DeckTags.ManaFixing, 82),
        Rule("tutor-land-to-battlefield", DeckRoles.Ramp, DeckTags.ManaFixing, 82),

        Rule("draw-engine", DeckRoles.Draw, DeckTags.Engines, 96),
        Rule("repeatable-pure-draw", DeckRoles.Draw, DeckTags.CardSelection, 92),
        Rule("repeatable-draw", DeckRoles.Draw, DeckTags.CardSelection, 92),
        Rule("pure-draw", DeckRoles.Draw, DeckTags.CardSelection, 100),
        Rule("card-advantage", DeckRoles.Draw, DeckTags.CardSelection, 88),
        Rule("burst-draw", DeckRoles.Draw, DeckTags.CardSelection, 88),
        Rule("scry", DeckRoles.Draw, DeckTags.CardSelection, 80),
        Rule("surveil", DeckRoles.Draw, DeckTags.CardSelection, 78),
        Rule("force-draw", DeckRoles.Draw, DeckTags.CardSelection, 68),
        Rule("impulsive-draw", DeckRoles.Draw, DeckTags.CardSelection, 70),
        Rule("repeatable-impulsive-draw", DeckRoles.Draw, DeckTags.CardSelection, 76),
        Rule("curiosity", DeckRoles.Draw, DeckTags.CardSelection, 78),
        Rule("repeatable-clues", DeckRoles.Draw, DeckTags.CardSelection, 72),
        Rule("wheel-one-sided", DeckRoles.Draw, DeckTags.Discard, 72),
        Rule("miniwheel", DeckRoles.Draw, DeckTags.Discard, 70),
        Rule("wheel-symmetrical", DeckRoles.Draw, DeckTags.Discard, 68),

        Rule("tutor-card", DeckRoles.Tutors, DeckTags.ComboEnabler, 100),
        Rule("tutor-to-hand", DeckRoles.Tutors, DeckTags.ComboEnabler, 98),
        Rule("tutor-to-battlefield", DeckRoles.Tutors, DeckTags.ComboEnabler, 82),
        Rule("tutor-creature", DeckRoles.Tutors, DeckTags.ComboEnabler, 70),
        Rule("tutored-by-name", DeckRoles.Tutors, DeckTags.ComboEnabler, 72),
        Rule("consult-cast", DeckRoles.Tutors, DeckTags.ComboEnabler, 76),

        Rule("gives-protection", DeckRoles.Protection, DeckTags.CombatProtection, 96),
        Rule("combat-trick", DeckRoles.Protection, DeckTags.CombatProtection, 82),
        Rule("damage-prevention", DeckRoles.Protection, DeckTags.CombatProtection, 78),
        Rule("flicker", DeckRoles.Protection, DeckTags.Blink, 84),
        Rule("cheat-death", DeckRoles.Protection, DeckTags.Reanimation, 76),

        Rule("reanimate-creature", DeckRoles.Recursion, DeckTags.Reanimation, 86),
        Rule("castable-from-graveyard", DeckRoles.Recursion, DeckTags.Reanimation, 84),
        Rule("reanimate-self", DeckRoles.Recursion, DeckTags.Reanimation, 76),
        Rule("activate-from-graveyard", DeckRoles.Recursion, DeckTags.Reanimation, 76),
        Rule("reanimate-equipment", DeckRoles.Recursion, DeckTags.Voltron, 76),
        Rule("regrowth-equipment", DeckRoles.Recursion, DeckTags.Voltron, 72),

        Rule("alternate-win-condition", DeckRoles.Wincons, DeckTags.Finishers, 88),
        Rule("burn-player", DeckRoles.Wincons, DeckTags.Finishers, 74),
        Rule("mill-opponent", DeckRoles.Wincons, DeckTags.Mill, 84),

        Rule("repeatable-creature-tokens", DeckRoles.Synergy, DeckTags.Tokens, 96),
        Rule("repeatable-token-generator", DeckRoles.Synergy, DeckTags.Tokens, 96),
        Rule("temporary-token", DeckRoles.Synergy, DeckTags.Tokens, 66),
        Rule("repeatable-artifact-tokens", DeckRoles.Synergy, DeckTags.ArtifactTokens, 84),
        Rule("synergy-token", DeckRoles.Synergy, DeckTags.Tokens, 76),
        Rule("synergy-token-creature", DeckRoles.Synergy, DeckTags.Tokens, 78),
        Rule("copy-token", DeckRoles.Synergy, DeckTags.Tokens, 70),
        Rule("creates-token-of-a-card", DeckRoles.Synergy, DeckTags.Tokens, 66),
        Rule("repeatable-food", DeckRoles.Synergy, DeckTags.Food, 66),
        Rule("synergy-food", DeckRoles.Synergy, DeckTags.Food, 66),
        Rule("lifegain", DeckRoles.Synergy, DeckTags.Lifegain, 88),
        Rule("repeatable-lifegain", DeckRoles.Synergy, DeckTags.Lifegain, 90),
        Rule("discard", DeckRoles.Synergy, DeckTags.Discard, 92),
        Rule("discard-outlet", DeckRoles.Synergy, DeckTags.Discard, 86),
        Rule("sacrifice-outlet-creature", DeckRoles.Synergy, DeckTags.SacOutlet, 96),
        Rule("repeatable-sacrifice-outlet", DeckRoles.Synergy, DeckTags.SacOutlet, 94),
        Rule("free-sacrifice-outlet", DeckRoles.Synergy, DeckTags.SacOutlet, 92),
        Rule("sacrifice-outlet-artifact", DeckRoles.Synergy, DeckTags.SacOutlet, 82),
        Rule("sacrifice-outlet-land", DeckRoles.Synergy, DeckTags.SacOutlet, 72),
        Rule("sacrifice-self", DeckRoles.Synergy, DeckTags.SacrificeFodder, 72),
        Rule("synergy-equipment", DeckRoles.Synergy, DeckTags.Voltron, 92),
        Rule("quick-equip", DeckRoles.Synergy, DeckTags.Voltron, 82),
        Rule("auto-equip", DeckRoles.Synergy, DeckTags.Voltron, 76),
        Rule("french-vanilla-equipment", DeckRoles.Synergy, DeckTags.Voltron, 64),
        Rule("copy-equipment", DeckRoles.Synergy, DeckTags.Voltron, 64),
        Rule("synergy-treasure", DeckRoles.Synergy, DeckTags.ManaFixing, 74),
        Rule("graveyard-fuel", DeckRoles.Synergy, DeckTags.Reanimation, 76),
        Rule("mill-self", DeckRoles.Synergy, DeckTags.Mill, 90),
        Rule("mill-any", DeckRoles.Synergy, DeckTags.Mill, 76),
        Rule("copy-creature", DeckRoles.Synergy, DeckTags.ComboEnabler, 74),
        Rule("copy-spell", DeckRoles.Synergy, DeckTags.ComboEnabler, 84),
        Rule("castable-from-exile", DeckRoles.Synergy, DeckTags.CardSelection, 76),
        Rule("cast-on-resolution", DeckRoles.Synergy, DeckTags.ComboEnabler, 72),
        Rule("free-cast-another", DeckRoles.Synergy, DeckTags.ComboEnabler, 78),

        Rule("attack-trigger", DeckRoles.Payoffs, DeckTags.Engines, 86),
        Rule("attacking-matters-self", DeckRoles.Payoffs, DeckTags.Engines, 82),
        Rule("attacking-matters", DeckRoles.Payoffs, DeckTags.Engines, 80),
        Rule("counters-matter", DeckRoles.Payoffs, DeckTags.Engines, 88),
        Rule("anthem", DeckRoles.Payoffs, DeckTags.Tokens, 82),
        Rule("creaturefall", DeckRoles.Payoffs, DeckTags.Tokens, 72),
        Rule("blood-artist-ability", DeckRoles.Payoffs, DeckTags.Drain, 72),
    ];

    /// <summary>
    /// Gets known aliases from older local annotations or user-facing shorthand into canonical slugs.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["exile-removal"] = "removal-exile",
            ["token-generator"] = "repeatable-token-generator",
            ["treasure"] = "repeatable-treasures",
            ["equipment"] = "synergy-equipment",
            ["stax"] = "rule-of-law",
            ["blink"] = "flicker",
        };

    /// <summary>
    /// Gets canonical rules by slug for fast classifier lookups.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, DeckTaggerRule> RulesBySlug = Rules
        .ToDictionary(rule => rule.Slug, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up a canonical Tagger rule after normalizing a slug or user-facing tag phrase.
    /// </summary>
    public static bool TryGetRule(string value, out DeckTaggerRule rule)
    {
        string normalized = NormalizeSlug(value);
        if (Aliases.TryGetValue(normalized, out string? canonical))
        {
            normalized = canonical;
        }

        return RulesBySlug.TryGetValue(normalized, out rule!);
    }

    /// <summary>
    /// Normalizes a Tagger slug or display phrase to the slug form used by Tagger.
    /// </summary>
    public static string NormalizeSlug(string value)
    {
        return string.Join(
            '-',
            value.Trim()
                .ToLowerInvariant()
                .Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>
    /// Creates one canonical Tagger rule.
    /// </summary>
    private static DeckTaggerRule Rule(string slug, string role, string secondaryTag, int priority)
    {
        return new DeckTaggerRule(slug, role, secondaryTag, priority);
    }
}

/// <summary>
/// Describes one Scryfall Tagger oracle-card tag mapped into local deck analysis roles.
/// </summary>
public sealed class DeckTaggerRule
{
    /// <summary>
    /// Creates a role mapping for one canonical oracle-card tag slug.
    /// </summary>
    public DeckTaggerRule(string slug, string role, string secondaryTag, int priority)
    {
        Slug = slug;
        Role = role;
        SecondaryTag = secondaryTag;
        Priority = priority;
    }

    /// <summary>
    /// Gets the Scryfall Tagger slug.
    /// </summary>
    public string Slug { get; }

    /// <summary>
    /// Gets the primary mtg-mcp role represented by the tag.
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// Gets the secondary mtg-mcp tag represented by the Tagger slug.
    /// </summary>
    public string SecondaryTag { get; }

    /// <summary>
    /// Gets the deterministic priority used when multiple Tagger tags imply different roles.
    /// </summary>
    public int Priority { get; }
}
