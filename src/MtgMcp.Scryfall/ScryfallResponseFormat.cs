namespace MtgMcp.Scryfall;

/// <summary>
/// Names how a card tool shapes its cards.
/// </summary>
internal enum ScryfallResponseFormat
{
    /// <summary>
    /// Every card field this server returns. This is the default.
    /// </summary>
    Standard,

    /// <summary>
    /// A small chosen set of fields for comparing many cards.
    /// </summary>
    Concise,

    /// <summary>
    /// The standard fields plus the raw provider objects.
    /// </summary>
    Full,
}

/// <summary>
/// Converts response formats to and from the names callers send and receive.
/// </summary>
internal static class ScryfallResponseFormatNames
{
    /// <summary>
    /// Reads a format name; a missing name means the default format.
    /// </summary>
    internal static bool TryParse(string? name, out ScryfallResponseFormat format)
    {
        format = ScryfallResponseFormat.Standard;
        switch (name)
        {
            case null:
            case "standard":
                return true;
            case "concise":
                format = ScryfallResponseFormat.Concise;
                return true;
            case "full":
                format = ScryfallResponseFormat.Full;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Gets the name callers send and receive for a format.
    /// </summary>
    internal static string ToName(ScryfallResponseFormat format)
    {
        return format switch
        {
            ScryfallResponseFormat.Concise => "concise",
            ScryfallResponseFormat.Full => "full",
            _ => "standard",
        };
    }
}
