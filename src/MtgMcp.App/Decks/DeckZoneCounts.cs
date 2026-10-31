using MtgMcp.Core.Decks;

namespace MtgMcp.App.Decks;

/// <summary>
/// Counts the cards of a whole deck by zone, so a page or a write summary can show deck-wide totals.
/// </summary>
internal static class DeckZoneCounts
{
    /// <summary>
    /// Lists the zones in the order a result shows them. Any other zone follows in name order.
    /// </summary>
    private static readonly string[] ZoneOrder = ["commander", "main", "sideboard", "maybeboard"];

    /// <summary>
    /// Adds up the quantities of every entry by zone, in zone order. A zone with no entries is left out.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> Of(DeckDocument deck)
    {
        ArgumentNullException.ThrowIfNull(deck);
        Dictionary<string, int> totals = new(StringComparer.Ordinal);
        foreach (DeckEntry entry in deck.Entries)
        {
            totals[entry.Zone] = totals.GetValueOrDefault(entry.Zone) + entry.Quantity;
        }

        Dictionary<string, int> ordered = new(StringComparer.Ordinal);
        foreach (string zone in ZoneOrder)
        {
            if (totals.TryGetValue(zone, out int count))
            {
                ordered[zone] = count;
            }
        }

        foreach (string zone in totals.Keys.Order(StringComparer.Ordinal))
        {
            ordered.TryAdd(zone, totals[zone]);
        }

        return ordered;
    }
}
