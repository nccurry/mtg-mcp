namespace MtgMcp.Core.Decks;

/// <summary>
/// Compares card names where a double-faced or split card may be named by all of its faces, such as
/// "Delver of Secrets // Insectile Aberration", or by one face. Scryfall names the card by all of its faces, and a
/// local entry, an Archidekt row, or a caller may carry either form, so two names match when they are equal without
/// regard to case or when one is a face of the other. This is the one face-name rule of the server.
/// </summary>
public static class CardNames
{
    /// <summary>
    /// Separates the faces of a card name.
    /// </summary>
    private const string FaceSeparator = " // ";

    /// <summary>
    /// Tells whether two card names name the same card: equal ignoring case, or one is a single face of the other.
    /// </summary>
    public static bool Match(string? left, string? right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return left is not null && right is not null && (IsFaceOf(left, right) || IsFaceOf(right, left));
    }

    /// <summary>
    /// Tells whether the first name is one face of the second, which names more than one face.
    /// </summary>
    private static bool IsFaceOf(string face, string whole)
    {
        if (!whole.Contains(FaceSeparator, StringComparison.Ordinal))
        {
            return false;
        }

        string wanted = face.Trim();
        foreach (string part in whole.Split(FaceSeparator, StringSplitOptions.None))
        {
            if (string.Equals(part.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
