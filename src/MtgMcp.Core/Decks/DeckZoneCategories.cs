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
    /// Names the local sideboard zone, whose category is Sideboard.
    /// </summary>
    public const string SideboardZone = "sideboard";

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
    /// Says why a primary category cannot be named for an entry in zone main when zone categories match.
    /// </summary>
    public const string MainPrimaryProblem =
        "primaryCategoryId names a zone category, but the entry is in zone main and zoneCategories is \"match\".";

    /// <summary>
    /// Pairs each local zone code with the category name that stands for it.
    /// </summary>
    private static readonly (string Zone, string Name)[] Pairs =
    [
        (CommanderZone, Commander),
        (SideboardZone, Sideboard),
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

    /// <summary>
    /// Returns why zone categories match cannot apply to a zone, or null when it can. Zone main has no zone category
    /// and is always accepted; any other zone needs a zone category and takes no explicit primary category.
    /// </summary>
    public static string? MatchProblem(string zone, Guid? primaryCategoryId)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (IsMain(zone))
        {
            return null;
        }

        if (NameForZone(zone) is null)
        {
            return $"zoneCategories \"match\" has no category for zone '{zone.Trim()}'; use \"leave\".";
        }

        return primaryCategoryId is null
            ? null
            : "primaryCategoryId cannot be set with zoneCategories \"match\" for a zone that has a zone category; " +
                "the zone category becomes the primary category.";
    }

    /// <summary>
    /// Finds a category by name, ignoring case and surrounding space, in the given category order.
    /// </summary>
    public static DeckCategory? FindByName(IEnumerable<DeckCategory> categories, string name)
    {
        ArgumentNullException.ThrowIfNull(categories);
        foreach (DeckCategory category in categories)
        {
            if (string.Equals(category.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the IDs of the categories that stand for a zone.
    /// </summary>
    public static HashSet<Guid> ZoneCategoryIds(IEnumerable<DeckCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        HashSet<Guid> ids = [];
        foreach (DeckCategory category in categories)
        {
            if (ZoneForName(category.Name) is not null)
            {
                ids.Add(category.CategoryId);
            }
        }

        return ids;
    }

    /// <summary>
    /// Finds the category for a zone among the categories, or makes a new one when the deck lacks it. Both results
    /// are null for a zone with no zone category; Created is set only for a category the caller must now store.
    /// </summary>
    public static (DeckCategory? Category, DeckCategory? Created) FindOrCreate(
        string zone,
        IEnumerable<DeckCategory> categories,
        Func<Guid> createId)
    {
        ArgumentNullException.ThrowIfNull(createId);
        if (NameForZone(zone) is not string name)
        {
            return (null, null);
        }

        DeckCategory? existing = FindByName(categories, name);
        if (existing is not null)
        {
            return (existing, null);
        }

        DeckCategory created = new(createId(), name, null, 0);
        return (created, created);
    }
}

