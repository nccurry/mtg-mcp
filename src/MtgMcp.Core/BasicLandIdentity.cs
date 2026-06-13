namespace MtgMcp.Core;

/// <summary>
/// Recognizes canonical and source-marked basic lands across validation and mutations.
/// </summary>
internal static class BasicLandIdentity
{
    /// <summary>
    /// Maps exact basic land requests to the canonical display names expected by deck rules.
    /// </summary>
    private static readonly Dictionary<string, string> CanonicalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plains"] = "Plains",
        ["Island"] = "Island",
        ["Swamp"] = "Swamp",
        ["Mountain"] = "Mountain",
        ["Forest"] = "Forest",
        ["Wastes"] = "Wastes",
    };

    /// <summary>
    /// Checks whether the requested card name is one of the canonical basic land names.
    /// </summary>
    internal static bool TryGetCanonicalName(string? cardName, out string canonicalName)
    {
        canonicalName = "";
        if (string.IsNullOrWhiteSpace(cardName)
            || !CanonicalNames.TryGetValue(cardName.Trim(), out string? resolvedName))
        {
            return false;
        }

        canonicalName = resolvedName;
        return true;
    }

    /// <summary>
    /// Checks whether a workspace card should be exempt from Commander singleton limits.
    /// </summary>
    internal static bool IsBasicLand(DeckCard card)
    {
        return IsBasicLandTypeLine(card.Snapshot?.TypeLine)
            || TryGetCanonicalName(card.Name, out _);
    }

    /// <summary>
    /// Checks whether a Scryfall-style type line marks a card as a basic land.
    /// </summary>
    internal static bool IsBasicLandTypeLine(string? typeLine)
    {
        return !string.IsNullOrWhiteSpace(typeLine)
            && typeLine.Contains("Basic", StringComparison.OrdinalIgnoreCase)
            && typeLine.Contains("Land", StringComparison.OrdinalIgnoreCase);
    }
}
