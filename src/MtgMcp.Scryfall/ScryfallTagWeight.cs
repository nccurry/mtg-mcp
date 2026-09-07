namespace MtgMcp.Scryfall;

/// <summary>
/// Maps Scryfall community-tag weights to their fixed comparison order.
/// </summary>
internal static class ScryfallTagWeight
{
    /// <summary>
    /// Gets the comparison rank for one supported provider weight.
    /// </summary>
    internal static int Rank(string weight)
    {
        return weight switch
        {
            "weak" => 0,
            "median" => 1,
            "strong" => 2,
            "very_strong" or "very-strong" => 3,
            _ => -1,
        };
    }
}
