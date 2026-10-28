namespace MtgMcp.Scryfall;

/// <summary>
/// Builds the concise card shape from a mapped card and the facts read from installed card data.
/// </summary>
internal static class ScryfallConciseProjection
{
    /// <summary>
    /// Projects one card to the selected concise fields in their fixed order.
    /// </summary>
    /// <param name="card">The mapped card.</param>
    /// <param name="facts">
    /// The tag slugs and cheapest price read from installed card data, or null when they are not available.
    /// </param>
    /// <param name="selected">The field names to write.</param>
    internal static ScryfallConciseCard Project(
        ScryfallCard card,
        ScryfallInstalledCardFacts? facts,
        IReadOnlySet<string> selected)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(selected);
        List<KeyValuePair<string, object?>> fields = [];
        Dictionary<string, string?>? prices = null;
        foreach (ScryfallConciseFieldDefinition field in ScryfallConciseFieldTable.Definitions)
        {
            if (!selected.Contains(field.Name))
            {
                continue;
            }

            if (field.PriceSourceKey is null)
            {
                fields.Add(new KeyValuePair<string, object?>(field.Name, field.Read(card, facts)));
                continue;
            }

            // All price fields share one "prices" object, placed where the first selected price field falls.
            if (prices is null)
            {
                prices = new Dictionary<string, string?>(StringComparer.Ordinal);
                fields.Add(new KeyValuePair<string, object?>("prices", prices));
            }

            prices[field.PriceOutputKey!] = card.Prices.TryGetValue(field.PriceSourceKey, out string? amount)
                ? amount
                : null;
        }

        return new ScryfallConciseCard(fields);
    }
}
