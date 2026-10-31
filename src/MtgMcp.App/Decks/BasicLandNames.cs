namespace MtgMcp.App.Decks;

/// <summary>
/// Knows the names of basic lands, which a Commander deck may hold in any number, for the places that have only a
/// card name and no card data.
/// </summary>
internal static class BasicLandNames
{
    /// <summary>
    /// Lists the names of basic lands in lower case.
    /// </summary>
    private static readonly string[] Names =
    [
        "plains", "island", "swamp", "mountain", "forest", "wastes", "snow-covered plains", "snow-covered island",
        "snow-covered swamp", "snow-covered mountain", "snow-covered forest", "snow-covered wastes",
    ];

    /// <summary>
    /// Reports whether a card name is the name of a basic land, ignoring case and surrounding spaces.
    /// </summary>
    internal static bool Contains(string cardName)
    {
        ArgumentNullException.ThrowIfNull(cardName);
        return Names.Contains(cardName.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }
}
