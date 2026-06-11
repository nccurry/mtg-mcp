namespace MtgMcp.Core;

/// <summary>
/// Provides deterministic deck construction profile thresholds.
/// </summary>
public static class DeckHeuristicProfileCatalog
{
    /// <summary>
    /// Current built-in profile catalog version.
    /// </summary>
    public const string BuiltInConfigVersion = "builtin-2026-06-10";

    /// <summary>
    /// Returns built-in Commander heuristic profiles.
    /// </summary>
    public static List<DeckHeuristicProfile> BuiltIns()
    {
        return
        [
            Profile(
                "commander-baseline",
                "Commander baseline",
                RoleTargets((DeckRoles.Lands, 35, 39), (DeckRoles.Ramp, 8, 12), (DeckRoles.Draw, 8, 12), (DeckRoles.Interaction, 8, 13), (DeckRoles.BoardWipes, 2, 5), (DeckRoles.Protection, 2, 6), (DeckRoles.Recursion, 2, 6), (DeckRoles.Wincons, 2, 6)),
                TagTargets((DeckTags.GraveyardHate, 1, 4), (DeckTags.ArtifactEnchantmentHate, 2, 6), (DeckTags.TableInteraction, 2, null), (DeckTags.TokenHate, 1, 4), (DeckTags.Finishers, 2, 6)),
                "Broad default Commander thresholds."),
            Profile(
                "command-zone-template",
                "Command Zone template",
                RoleTargets((DeckRoles.Lands, 35, 38), (DeckRoles.Ramp, 10, 12), (DeckRoles.Draw, 10, 12), (DeckRoles.Interaction, 10, 12), (DeckRoles.BoardWipes, 3, 4), (DeckRoles.Wincons, 2, 5)),
                TagTargets((DeckTags.GraveyardHate, 1, 3), (DeckTags.ArtifactEnchantmentHate, 2, 5)),
                "Classic ramp, draw, removal, and wipe package template."),
            Profile(
                "edhrec-foundation",
                "EDHREC foundation",
                RoleTargets((DeckRoles.Lands, 36, 39), (DeckRoles.Ramp, 10, 12), (DeckRoles.Draw, 10, 14), (DeckRoles.Interaction, 8, 12), (DeckRoles.BoardWipes, 2, 5), (DeckRoles.Recursion, 2, 5), (DeckRoles.Wincons, 2, 5)),
                TagTargets((DeckTags.GraveyardHate, 1, 3), (DeckTags.ArtifactEnchantmentHate, 2, 5), (DeckTags.Finishers, 2, 5)),
                "EDHREC-style foundation for mana, velocity, interaction, and game enders."),
            Profile(
                "mana-rich-39-land",
                "Mana-rich 39-land baseline",
                RoleTargets((DeckRoles.Lands, 39, 39), (DeckRoles.Ramp, 8, 12), (DeckRoles.Draw, 8, 12), (DeckRoles.Interaction, 8, 13)),
                TagTargets(),
                "Useful for higher curves, landfall, and decks that need stable early land drops."),
            Profile(
                "fifty-mana-sources",
                "Fifty mana sources",
                RoleTargets((DeckRoles.Lands, 36, 40), (DeckRoles.Ramp, 10, 14), (DeckRoles.Draw, 8, 12), (DeckRoles.Interaction, 8, 13)),
                TagTargets(),
                "Checks lands plus ramp against the 50-source mana heuristic."),
            Profile(
                "package-8x8",
                "8x8 package template",
                RoleTargets((DeckRoles.Lands, 35, 36), (DeckRoles.Ramp, 8, 10), (DeckRoles.Draw, 8, 10), (DeckRoles.Interaction, 8, 10)),
                TagTargets(),
                "Commander plus lands, then eight functional packages of about eight cards."),
            Profile(
                "package-7x9",
                "7x9 package template",
                RoleTargets((DeckRoles.Lands, 36, 37), (DeckRoles.Ramp, 7, 10), (DeckRoles.Draw, 7, 10), (DeckRoles.Interaction, 7, 10)),
                TagTargets(),
                "Commander plus lands, then seven larger packages."),
            Profile(
                "package-9x7",
                "9x7 package template",
                RoleTargets((DeckRoles.Lands, 35, 36), (DeckRoles.Ramp, 7, 9), (DeckRoles.Draw, 7, 9), (DeckRoles.Interaction, 7, 9)),
                TagTargets(),
                "Commander plus lands, then nine tighter packages."),
            Profile(
                "seventy-five-percent",
                "75 percent Commander",
                RoleTargets((DeckRoles.Lands, 36, 38), (DeckRoles.Ramp, 8, 10), (DeckRoles.Draw, 9, 12), (DeckRoles.Interaction, 10, 13), (DeckRoles.Tutors, 0, 2), (DeckRoles.BoardWipes, 2, 4), (DeckRoles.Wincons, 2, 4)),
                TagTargets((DeckTags.Finishers, 2, 4)),
                "Strong, interactive, and scalable without maximizing deterministic consistency."),
            Profile(
                "cedh-turbo",
                "cEDH turbo",
                RoleTargets((DeckRoles.Lands, 27, 31), (DeckRoles.Ramp, 14, 20), (DeckRoles.Tutors, 8, 14), (DeckRoles.Interaction, 10, 16), (DeckRoles.BoardWipes, 0, 1)),
                TagTargets((DeckTags.CardSelection, 8, 14), (DeckTags.ComboPiece, 5, 10)),
                "Fast mana, compact wins, tutors, and cheap interaction."),
            Profile(
                "cedh-midrange",
                "cEDH midrange",
                RoleTargets((DeckRoles.Lands, 28, 32), (DeckRoles.Ramp, 10, 16), (DeckRoles.Tutors, 6, 12), (DeckRoles.Interaction, 14, 20)),
                TagTargets((DeckTags.CardSelection, 8, 14), (DeckTags.ComboPiece, 3, 8)),
                "Compact wins with more interaction and value than turbo shells."),
            Profile(
                "cedh-stax",
                "cEDH stax",
                RoleTargets((DeckRoles.Lands, 29, 33), (DeckRoles.Ramp, 9, 14), (DeckRoles.Tutors, 5, 10), (DeckRoles.Interaction, 12, 18)),
                TagTargets((DeckTags.Stax, 6, 12), (DeckTags.ComboPiece, 2, 7)),
                "Permission, taxes, hate pieces, and compact win routes."),
            Profile(
                "cedh-tempo",
                "cEDH tempo",
                RoleTargets((DeckRoles.Lands, 28, 32), (DeckRoles.Ramp, 9, 14), (DeckRoles.Tutors, 5, 10), (DeckRoles.Interaction, 14, 20)),
                TagTargets((DeckTags.CardSelection, 8, 14), (DeckTags.ComboPiece, 2, 7)),
                "Low curve, high interaction, and efficient pressure."),
            Profile(
                "archetype-landfall",
                "Landfall",
                RoleTargets((DeckRoles.Lands, 38, 42), (DeckRoles.Ramp, 10, 16), (DeckRoles.Draw, 8, 12), (DeckRoles.Recursion, 3, 8), (DeckRoles.Payoffs, 5, null)),
                TagTargets(("Extra Land Drops", 3, null), ("Land Recursion", 2, null), ("Landfall Payoffs", 6, null)),
                "Archetype-specific rows use existing classifier tags or explicit workspace categories with matching names."),
            Profile(
                "archetype-sea-monsters",
                "Sea monsters",
                RoleTargets((DeckRoles.Lands, 37, 41), (DeckRoles.Ramp, 12, 18), (DeckRoles.Draw, 8, 12), (DeckRoles.Payoffs, 8, null)),
                TagTargets(("Topdeck Setup", 4, null), ("Activated Ability Support", 3, null), ("High-CMC Hits", 8, null)),
                "Use explicit categories for topdeck setup, activated ability support, and high-CMC hit density when the classifier cannot derive them."),
            Profile(
                "archetype-enchantments",
                "Enchantments",
                RoleTargets((DeckRoles.Lands, 35, 38), (DeckRoles.Ramp, 8, 12), (DeckRoles.Draw, 8, 14), (DeckRoles.Recursion, 3, 8), (DeckRoles.Payoffs, 6, null)),
                TagTargets(("Enchantments", 20, null), ("Enchantment Recursion", 3, null), ("Enchantress Engines", 4, null)),
                "Use explicit categories for enchantment density, enchantment recursion, and enchantress engines when source snapshots do not classify them."),
            Profile(
                "archetype-go-wide",
                "Go-wide",
                RoleTargets((DeckRoles.Lands, 35, 38), (DeckRoles.Ramp, 8, 12), (DeckRoles.Draw, 8, 12), (DeckRoles.Protection, 3, 8), (DeckRoles.Wincons, 3, 8)),
                TagTargets((DeckTags.Tokens, 10, null), (DeckTags.Finishers, 3, null), (DeckTags.GoWideProtection, 2, null), ("Anthems", 3, null)),
                "Go-wide expectations come from existing token/finisher/protection tags or explicit workspace categories.")
        ];
    }

    /// <summary>
    /// Creates a heuristic profile.
    /// </summary>
    private static DeckHeuristicProfile Profile(
        string id,
        string name,
        Dictionary<string, (int Minimum, int? Maximum)> roleTargets,
        Dictionary<string, (int Minimum, int? Maximum)> tagTargets,
        params string[] notes)
    {
        return new DeckHeuristicProfile(id, name, roleTargets, tagTargets, notes.ToList());
    }

    /// <summary>
    /// Creates role targets.
    /// </summary>
    private static Dictionary<string, (int Minimum, int? Maximum)> RoleTargets(params (string Target, int Minimum, int? Maximum)[] targets)
    {
        return Targets(targets);
    }

    /// <summary>
    /// Creates tag targets.
    /// </summary>
    private static Dictionary<string, (int Minimum, int? Maximum)> TagTargets(params (string Target, int Minimum, int? Maximum)[] targets)
    {
        return Targets(targets);
    }

    /// <summary>
    /// Creates generic target dictionaries.
    /// </summary>
    private static Dictionary<string, (int Minimum, int? Maximum)> Targets(params (string Target, int Minimum, int? Maximum)[] targets)
    {
        Dictionary<string, (int Minimum, int? Maximum)> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string target, int minimum, int? maximum) in targets)
        {
            result[target] = (minimum, maximum);
        }

        return result;
    }
}

/// <summary>
/// Stores one deterministic Commander construction profile.
/// </summary>
public sealed class DeckHeuristicProfile
{
    /// <summary>
    /// Creates a Commander heuristic profile.
    /// </summary>
    public DeckHeuristicProfile(
        string id,
        string name,
        IReadOnlyDictionary<string, (int Minimum, int? Maximum)> roleTargets,
        IReadOnlyDictionary<string, (int Minimum, int? Maximum)> tagTargets,
        IReadOnlyList<string> notes)
    {
        Id = id;
        Name = name;
        RoleTargets = roleTargets;
        TagTargets = tagTargets;
        Notes = notes;
    }

    /// <summary>
    /// Gets the profile id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the profile name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets role target bands.
    /// </summary>
    public IReadOnlyDictionary<string, (int Minimum, int? Maximum)> RoleTargets { get; }

    /// <summary>
    /// Gets tag or explicit-category target bands.
    /// </summary>
    public IReadOnlyDictionary<string, (int Minimum, int? Maximum)> TagTargets { get; }

    /// <summary>
    /// Gets profile notes.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }
}
