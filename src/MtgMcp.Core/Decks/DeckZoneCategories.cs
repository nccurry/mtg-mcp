namespace MtgMcp.Core.Decks;

/// <summary>
/// Defines the category names that stand for a deck zone, and how a change chooses to keep a zone and its category in
/// step. Providers that store a zone as a category read these names instead of repeating them.
/// </summary>
public static class DeckZoneCategories
{
    /// <summary>
    /// Names the category that stands for the commander zone.
    /// </summary>
    public const string Commander = "Commander";

    /// <summary>
    /// Names the category that stands for the sideboard zone.
    /// </summary>
    public const string Sideboard = "Sideboard";

    /// <summary>
    /// Names the category that stands for the maybeboard zone.
    /// </summary>
    public const string Maybeboard = "Maybeboard";

    /// <summary>
    /// Names the local commander zone, whose category is Commander.
    /// </summary>
    public const string CommanderZone = "commander";

    /// <summary>
    /// Names the local maybeboard zone, whose category is Maybeboard.
    /// </summary>
    public const string MaybeboardZone = "maybeboard";

    /// <summary>
    /// Names the local zone that has no zone category.
    /// </summary>
    public const string MainZone = "main";

    /// <summary>
    /// Names the mode in which a zone change also sets the matching zone category.
    /// </summary>
    public const string Match = "match";

    /// <summary>
    /// Names the mode in which a zone change leaves every category as it is.
    /// </summary>
    public const string Leave = "leave";

    /// <summary>
    /// Pairs each local zone code with the category name that stands for it.
    /// </summary>
    private static readonly (string Zone, string Name)[] Pairs =
    [
        (CommanderZone, Commander),
        ("sideboard", Sideboard),
        (MaybeboardZone, Maybeboard),
    ];

    /// <summary>
    /// Gives the category name for a local zone code, ignoring case and surrounding space, or null when the zone has
    /// no zone category.
    /// </summary>
    public static string? NameForZone(string? zone)
    {
        string? trimmed = zone?.Trim();
        foreach ((string code, string name) in Pairs)
        {
            if (string.Equals(code, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the local zone code a category name stands for, ignoring case and surrounding space, or null when the
    /// name is not a zone category.
    /// </summary>
    public static string? ZoneForName(string? name)
    {
        string? trimmed = name?.Trim();
        foreach ((string code, string categoryName) in Pairs)
        {
            if (string.Equals(categoryName, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>
    /// Tells whether the text is the match mode, ignoring case.
    /// </summary>
    public static bool IsMatch(string? mode)
    {
        return string.Equals(mode?.Trim(), Match, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tells whether the text is a known mode: match or leave, ignoring case.
    /// </summary>
    public static bool IsMode(string? mode)
    {
        return IsMatch(mode) || string.Equals(mode?.Trim(), Leave, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tells whether a zone code is the main zone, ignoring case and surrounding space.
    /// </summary>
    public static bool IsMain(string? zone)
    {
        return string.Equals(zone?.Trim(), MainZone, StringComparison.OrdinalIgnoreCase);
    }
}
