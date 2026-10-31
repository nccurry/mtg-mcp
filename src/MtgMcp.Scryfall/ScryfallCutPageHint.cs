using System.Globalization;

namespace MtgMcp.Scryfall;

/// <summary>
/// Writes the notes for a card page that was cut to stay under the result size limit.
/// </summary>
public static class ScryfallCutPageHint
{
    /// <summary>
    /// Gets the cut reason that every cut card page reports.
    /// </summary>
    public const string ResultSizeLimit = "result-size-limit";

    /// <summary>
    /// Says what a cut page holds and how to read the rest: how many cards were kept of those requested, how big
    /// each card was, where the next cursor continues, and how to fit more cards in a page.
    /// </summary>
    /// <param name="returned">How many cards the page holds.</param>
    /// <param name="requested">How many cards the page would hold without the size limit.</param>
    /// <param name="pageStart">The zero-based position of the page's first card in the whole result.</param>
    /// <param name="totalCount">How many cards the whole result holds.</param>
    /// <param name="averageBytes">The average size in bytes of one card on this page.</param>
    public static string Build(int returned, int requested, int pageStart, int totalCount, int averageBytes)
    {
        return
            $"The page holds {Number(returned)} of the {Number(requested)} cards requested, because it was cut " +
            $"to stay under the result size limit (about {Number(averageBytes)} bytes per card on this page). " +
            $"nextCursor continues at card {Number(pageStart + returned + 1)} of {Number(totalCount)}; no card is " +
            "skipped. In the concise format, leaving oracleText out of fields fits more cards in a page.";
    }

    /// <summary>
    /// Writes a count with thousands separators.
    /// </summary>
    private static string Number(int value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
